using System;
using System.Collections.Generic;
using Quieter.Core;
using Quieter.World;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Quieter.Player
{
    [RequireComponent(typeof(CharacterController))]
    public sealed class NetworkPlayer : NetworkBehaviour
    {
        private const int PredictionHistorySize = 256;
        private const int SnapshotHistorySize = 48;
        private const int MissingInputGraceTicks = 2;
        private const double InputTimeoutSeconds = 0.15d;
        private const float RemoteInterpolationSeconds = 0.1f;
        private const float RemoteExtrapolationSeconds = 0.05f;
        private const float SoftCorrectionLimit = 0.75f;
        private const float CorrectionHalfLife = 0.075f;
        private const float BobFadeSpeed = 8f;
        private const float WalkBobFrequency = 1.7f;
        private const float SprintBobFrequency = 2.2f;
        private const float BobHorizontalAmplitude = 0.015f;
        private const float BobVerticalAmplitude = 0.025f;

        private readonly struct PredictedInput
        {
            public PredictedInput(PlayerInputFrame frame) => Frame = frame;
            public PlayerInputFrame Frame { get; }
        }

        private readonly struct MovementSnapshot
        {
            public MovementSnapshot(PlayerNetworkState state, double receivedAt)
            {
                State = state;
                ReceivedAt = receivedAt;
            }

            public PlayerNetworkState State { get; }
            public double ReceivedAt { get; }
        }

        private readonly NetworkVariable<PlayerNetworkState> authoritativeState = new(
            default,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<FixedString64Bytes> displayName = new(
            default,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);
        private readonly List<PredictedInput> predictionHistory = new(PredictionHistorySize);
        private readonly SortedDictionary<uint, PlayerInputFrame> serverInputQueue = new();
        private readonly List<MovementSnapshot> snapshotHistory = new(SnapshotHistorySize);

        [SerializeField] private Transform presentationRoot;
        [SerializeField] private Transform cameraPivot;
        [SerializeField] private Camera ownerCamera;

        private CharacterController characterController;
        private PlayerMovementMotor movementMotor;
        private WorldStreamer worldStreamer;
        private PlayerNetworkState simulatedState;
        private PlayerInputFrame latestServerInput;
        private Vector2 sampledMovement;
        private Vector3 previousSimulationPosition;
        private Vector3 currentSimulationPosition;
        private Vector3 visualCorrectionOffset;
        private Vector3 presentationLocalPosition;
        private Quaternion presentationLocalRotation;
        private float cameraEyeHeight = 1.62f;
        private float viewYaw;
        private float pitch;
        private float bobPhase;
        private float bobWeight;
        private uint nextInputSequence;
        private uint jumpPressId;
        private uint serverSimulationTick;
        private int missingInputTicks;
        private double latestInputReceivedAt;
        private bool sampledSprint;
        private bool hasServerInput;
        private TextMesh nameLabel;

        public ulong SteamId { get; private set; }
        public string DisplayName => displayName.Value.ToString();
        public event Action<NetworkPlayer> ServerDespawning;

        private void Awake()
        {
            characterController = GetComponent<CharacterController>();
            movementMotor = new PlayerMovementMotor(characterController);
            if (presentationRoot != null)
            {
                presentationLocalPosition = presentationRoot.localPosition;
                presentationLocalRotation = presentationRoot.localRotation;
            }

            if (cameraPivot != null)
            {
                cameraEyeHeight = cameraPivot.localPosition.y;
            }
        }

        public override void OnNetworkSpawn()
        {
            worldStreamer = FindAnyObjectByType<WorldStreamer>();
            authoritativeState.OnValueChanged += OnAuthoritativeStateChanged;
            displayName.OnValueChanged += OnDisplayNameChanged;
            NetworkManager.NetworkTickSystem.Tick += OnNetworkTick;

            if (ownerCamera != null)
            {
                ownerCamera.enabled = IsOwner;
                ownerCamera.gameObject.tag = IsOwner ? "MainCamera" : "Untagged";
                var listener = ownerCamera.GetComponent<AudioListener>();
                if (listener != null)
                {
                    listener.enabled = IsOwner;
                }
            }

            characterController.enabled = IsServer || IsOwner;
            simulatedState = authoritativeState.Value;
            if (simulatedState.Position == default)
            {
                simulatedState.Position = transform.position;
                simulatedState.Yaw = transform.eulerAngles.y;
            }

            movementMotor.Warp(simulatedState);
            viewYaw = simulatedState.Yaw;
            previousSimulationPosition = simulatedState.Position;
            currentSimulationPosition = simulatedState.Position;
            latestServerInput.Yaw = simulatedState.Yaw;
            CreateNameLabel();
            OnDisplayNameChanged(default, displayName.Value);

            if (IsOwner && presentationRoot != null)
            {
                foreach (var renderer in presentationRoot.GetComponentsInChildren<Renderer>(true))
                {
                    renderer.enabled = false;
                }
            }
            else
            {
                AddSnapshot(simulatedState);
            }

            if (IsOwner)
            {
                LockCursor(true);
            }
        }

        public override void OnNetworkDespawn()
        {
            if (IsServer)
            {
                ServerDespawning?.Invoke(this);
            }

            if (NetworkManager != null)
            {
                NetworkManager.NetworkTickSystem.Tick -= OnNetworkTick;
            }

            authoritativeState.OnValueChanged -= OnAuthoritativeStateChanged;
            displayName.OnValueChanged -= OnDisplayNameChanged;
            if (IsOwner)
            {
                LockCursor(false);
            }
        }

        private void Update()
        {
            if (!IsSpawned || !IsOwner)
            {
                return;
            }

            SampleInput();
            UpdateLook();
            if (Keyboard.current?.escapeKey.wasPressedThisFrame == true)
            {
                LockCursor(Cursor.lockState != CursorLockMode.Locked);
            }
        }

        private void FixedUpdate()
        {
            if (!IsSpawned || !characterController.enabled)
            {
                return;
            }

            previousSimulationPosition = currentSimulationPosition;
            var deltaTime = 1f / QuieterConstants.MovementSimulationRate;
            if (IsServer)
            {
                var input = IsOwner ? CreateInputFrame() : GetServerInputForTick();
                SimulateFrame(ref simulatedState, input, deltaTime);
                simulatedState.ServerTick = ++serverSimulationTick;
                if (IsOwner || input.Sequence > simulatedState.LastProcessedSequence)
                {
                    simulatedState.LastProcessedSequence = input.Sequence;
                }

                currentSimulationPosition = simulatedState.Position;
                return;
            }

            if (!IsOwner)
            {
                return;
            }

            var predictedInput = CreateInputFrame();
            SimulateFrame(ref simulatedState, predictedInput, deltaTime);
            predictionHistory.Add(new PredictedInput(predictedInput));
            if (predictionHistory.Count > PredictionHistorySize)
            {
                predictionHistory.RemoveAt(0);
            }

            currentSimulationPosition = simulatedState.Position;
        }

        public void AssignServerIdentity(ulong steamId, string playerDisplayName, Vector3 spawnPosition)
        {
            if (!IsServer)
            {
                return;
            }

            SteamId = steamId;
            displayName.Value = new FixedString64Bytes(SanitizeName(playerDisplayName));
            simulatedState = new PlayerNetworkState
            {
                Position = spawnPosition,
                Yaw = 0f,
                Grounded = false,
            };
            movementMotor.Warp(simulatedState);
            previousSimulationPosition = spawnPosition;
            currentSimulationPosition = spawnPosition;
            authoritativeState.Value = simulatedState;
        }

        [ServerRpc(Delivery = RpcDelivery.Unreliable)]
        private void SubmitInputServerRpc(PlayerInputBatch batch)
        {
            if (!IsServer)
            {
                return;
            }

            var acceptedAny = false;
            for (var index = 0; index < batch.Count; index++)
            {
                var frame = batch[index];
                if (frame.Sequence <= simulatedState.LastProcessedSequence
                    || frame.Sequence > simulatedState.LastProcessedSequence + PredictionHistorySize
                    || !IsFinite(frame.Movement.x)
                    || !IsFinite(frame.Movement.y)
                    || !IsFinite(frame.Yaw))
                {
                    continue;
                }

                frame.Movement = Vector2.ClampMagnitude(frame.Movement, 1f);
                frame.Yaw = Mathf.Repeat(frame.Yaw, 360f);
                if (!serverInputQueue.ContainsKey(frame.Sequence))
                {
                    serverInputQueue.Add(frame.Sequence, frame);
                    acceptedAny = true;
                }
            }

            if (acceptedAny)
            {
                latestInputReceivedAt = NetworkManager.ServerTime.Time;
            }
        }

        private void OnNetworkTick()
        {
            if (!IsSpawned)
            {
                return;
            }

            if (IsServer)
            {
                authoritativeState.Value = simulatedState;
                if (!IsOwner)
                {
                    AddSnapshot(simulatedState);
                }

                return;
            }

            if (!IsOwner || predictionHistory.Count == 0)
            {
                return;
            }

            var batch = new PlayerInputBatch();
            var start = Mathf.Max(0, predictionHistory.Count - PlayerInputBatch.Capacity);
            for (var index = start; index < predictionHistory.Count; index++)
            {
                batch.Add(predictionHistory[index].Frame);
            }

            SubmitInputServerRpc(batch);
        }

        private void SampleInput()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null)
            {
                sampledMovement = Vector2.zero;
                sampledSprint = false;
                return;
            }

            var movement = Vector2.zero;
            if (keyboard.wKey.isPressed) movement.y += 1f;
            if (keyboard.sKey.isPressed) movement.y -= 1f;
            if (keyboard.dKey.isPressed) movement.x += 1f;
            if (keyboard.aKey.isPressed) movement.x -= 1f;
            sampledMovement = Vector2.ClampMagnitude(movement, 1f);
            sampledSprint = keyboard.leftShiftKey.isPressed;
            if (keyboard.spaceKey.wasPressedThisFrame)
            {
                jumpPressId++;
            }
        }

        private PlayerInputFrame CreateInputFrame()
        {
            return new PlayerInputFrame
            {
                Sequence = ++nextInputSequence,
                Movement = sampledMovement,
                Yaw = viewYaw,
                JumpPressId = jumpPressId,
                Sprint = sampledSprint,
            };
        }

        private PlayerInputFrame GetServerInputForTick()
        {
            var expectedSequence = simulatedState.LastProcessedSequence + 1;
            if (serverInputQueue.Remove(expectedSequence, out var exact))
            {
                latestServerInput = exact;
                hasServerInput = true;
                missingInputTicks = 0;
                return ApplyInputTimeout(exact);
            }

            var hasLaterInput = false;
            foreach (var sequence in serverInputQueue.Keys)
            {
                hasLaterInput = sequence > expectedSequence;
                break;
            }

            if (hasLaterInput && ++missingInputTicks >= MissingInputGraceTicks)
            {
                missingInputTicks = 0;
                var skipped = latestServerInput;
                skipped.Sequence = expectedSequence;
                simulatedState.LastProcessedSequence = expectedSequence;
                return ApplyInputTimeout(skipped);
            }

            var held = latestServerInput;
            held.Sequence = simulatedState.LastProcessedSequence;
            return ApplyInputTimeout(held);
        }

        private PlayerInputFrame ApplyInputTimeout(PlayerInputFrame input)
        {
            if (!hasServerInput
                || NetworkManager.ServerTime.Time - latestInputReceivedAt > InputTimeoutSeconds)
            {
                input.Movement = Vector2.zero;
                input.Sprint = false;
            }

            return input;
        }

        private void SimulateFrame(
            ref PlayerNetworkState state,
            PlayerInputFrame input,
            float deltaTime)
        {
            movementMotor.Simulate(ref state, input, deltaTime);
            if (worldStreamer == null || !worldStreamer.IsInitialized)
            {
                return;
            }

            var clamped = worldStreamer.ClampToWorld(state.Position);
            if ((clamped - state.Position).sqrMagnitude <= 0.001f)
            {
                return;
            }

            state.Position = clamped;
            movementMotor.Warp(state);
        }

        private void OnAuthoritativeStateChanged(
            PlayerNetworkState previous,
            PlayerNetworkState current)
        {
            if (IsServer)
            {
                return;
            }

            if (!IsOwner)
            {
                transform.SetPositionAndRotation(
                    current.Position,
                    Quaternion.Euler(0f, current.Yaw, 0f));
                AddSnapshot(current);
                return;
            }

            predictionHistory.RemoveAll(
                prediction => prediction.Frame.Sequence <= current.LastProcessedSequence);
            var positionBeforeCorrection = transform.position;
            simulatedState = current;
            movementMotor.Warp(simulatedState);
            var deltaTime = 1f / QuieterConstants.MovementSimulationRate;
            foreach (var prediction in predictionHistory)
            {
                SimulateFrame(ref simulatedState, prediction.Frame, deltaTime);
            }

            var correctedPosition = simulatedState.Position;
            var correction = positionBeforeCorrection - correctedPosition;
            if (correction.sqrMagnitude <= SoftCorrectionLimit * SoftCorrectionLimit)
            {
                visualCorrectionOffset += correction;
                visualCorrectionOffset = Vector3.ClampMagnitude(
                    visualCorrectionOffset,
                    SoftCorrectionLimit);
            }
            else
            {
                visualCorrectionOffset = Vector3.zero;
            }

            previousSimulationPosition = correctedPosition;
            currentSimulationPosition = correctedPosition;
        }

        private void UpdateLook()
        {
            if (Mouse.current == null || Cursor.lockState != CursorLockMode.Locked)
            {
                return;
            }

            var delta = Mouse.current.delta.ReadValue() * ClientPreferences.MouseSensitivity;
            viewYaw = Mathf.Repeat(viewYaw + delta.x, 360f);
            pitch = Mathf.Clamp(pitch - delta.y, -85f, 85f);
        }

        private void LateUpdate()
        {
            if (!IsSpawned)
            {
                return;
            }

            if (IsOwner)
            {
                UpdateOwnerCamera();
            }
            else
            {
                UpdateRemotePresentation();
            }

            if (nameLabel != null && Camera.main != null)
            {
                nameLabel.transform.rotation = Camera.main.transform.rotation;
            }
        }

        private void UpdateOwnerCamera()
        {
            if (cameraPivot == null)
            {
                return;
            }

            var alpha = Time.fixedDeltaTime > 0f
                ? Mathf.Clamp01((Time.time - Time.fixedTime) / Time.fixedDeltaTime)
                : 1f;
            var bodyPosition = Vector3.Lerp(
                previousSimulationPosition,
                currentSimulationPosition,
                alpha);

            if (visualCorrectionOffset.sqrMagnitude > 0.0000001f)
            {
                var decay = Mathf.Pow(0.5f, Time.unscaledDeltaTime / CorrectionHalfLife);
                visualCorrectionOffset *= decay;
                if (visualCorrectionOffset.sqrMagnitude < 0.0000001f)
                {
                    visualCorrectionOffset = Vector3.zero;
                }
            }

            bodyPosition += visualCorrectionOffset;
            var planarSpeed = new Vector2(simulatedState.Velocity.x, simulatedState.Velocity.z).magnitude;
            var wantsBob = ClientPreferences.HeadBobEnabled
                && simulatedState.Grounded
                && planarSpeed > 0.2f;
            bobWeight = wantsBob
                ? Mathf.MoveTowards(bobWeight, 1f, BobFadeSpeed * Time.unscaledDeltaTime)
                : Mathf.MoveTowards(bobWeight, 0f, BobFadeSpeed * Time.unscaledDeltaTime);

            if (wantsBob)
            {
                var sprintBlend = Mathf.InverseLerp(
                    PlayerMovementTuning.WalkSpeed,
                    PlayerMovementTuning.SprintSpeed,
                    planarSpeed);
                var frequency = Mathf.Lerp(WalkBobFrequency, SprintBobFrequency, sprintBlend);
                bobPhase += Time.unscaledDeltaTime * frequency * Mathf.PI * 2f;
            }
            else if (!ClientPreferences.HeadBobEnabled)
            {
                bobPhase = 0f;
                bobWeight = 0f;
            }

            var bob = new Vector3(
                Mathf.Sin(bobPhase) * BobHorizontalAmplitude,
                -Mathf.Cos(bobPhase * 2f) * BobVerticalAmplitude,
                0f) * bobWeight;
            var viewRotation = Quaternion.Euler(0f, viewYaw, 0f);
            cameraPivot.position = bodyPosition
                + Vector3.up * cameraEyeHeight
                + viewRotation * bob;
            cameraPivot.rotation = Quaternion.Euler(pitch, viewYaw, 0f);
        }

        private void AddSnapshot(PlayerNetworkState state)
        {
            if (snapshotHistory.Count > 0
                && state.ServerTick <= snapshotHistory[^1].State.ServerTick)
            {
                return;
            }

            snapshotHistory.Add(new MovementSnapshot(state, Time.unscaledTimeAsDouble));
            if (snapshotHistory.Count > SnapshotHistorySize)
            {
                snapshotHistory.RemoveAt(0);
            }
        }

        private void UpdateRemotePresentation()
        {
            if (presentationRoot == null || snapshotHistory.Count == 0)
            {
                return;
            }

            var newest = snapshotHistory[^1];
            var elapsedSinceNewest = Math.Max(0d, Time.unscaledTimeAsDouble - newest.ReceivedAt);
            var targetTick = newest.State.ServerTick
                + elapsedSinceNewest * QuieterConstants.MovementSimulationRate
                - RemoteInterpolationSeconds * QuieterConstants.MovementSimulationRate;
            var renderedPosition = newest.State.Position;
            var renderedYaw = newest.State.Yaw;

            if (targetTick <= snapshotHistory[0].State.ServerTick)
            {
                renderedPosition = snapshotHistory[0].State.Position;
                renderedYaw = snapshotHistory[0].State.Yaw;
            }
            else
            {
                var foundInterval = false;
                for (var index = 0; index < snapshotHistory.Count - 1; index++)
                {
                    var from = snapshotHistory[index].State;
                    var to = snapshotHistory[index + 1].State;
                    if (targetTick < from.ServerTick || targetTick > to.ServerTick)
                    {
                        continue;
                    }

                    var tickSpan = Math.Max(1d, to.ServerTick - from.ServerTick);
                    var interpolation = (float)((targetTick - from.ServerTick) / tickSpan);
                    renderedPosition = Vector3.Lerp(from.Position, to.Position, interpolation);
                    renderedYaw = Mathf.LerpAngle(from.Yaw, to.Yaw, interpolation);
                    foundInterval = true;
                    break;
                }

                if (!foundInterval && targetTick > newest.State.ServerTick)
                {
                    var extrapolationTicks = Math.Min(
                        targetTick - newest.State.ServerTick,
                        RemoteExtrapolationSeconds * QuieterConstants.MovementSimulationRate);
                    var extrapolationSeconds = (float)(
                        extrapolationTicks / QuieterConstants.MovementSimulationRate);
                    var velocity = newest.State.Velocity;
                    if (newest.State.Grounded)
                    {
                        velocity.y = 0f;
                    }

                    renderedPosition = newest.State.Position + velocity * extrapolationSeconds;
                }
            }

            var rotation = Quaternion.Euler(0f, renderedYaw, 0f);
            presentationRoot.SetPositionAndRotation(
                renderedPosition + rotation * presentationLocalPosition,
                rotation * presentationLocalRotation);
        }

        private void CreateNameLabel()
        {
            if (presentationRoot == null || nameLabel != null)
            {
                return;
            }

            var labelObject = new GameObject("PlayerName");
            labelObject.transform.SetParent(presentationRoot, false);
            labelObject.transform.localPosition = new Vector3(0f, 1.5f, 0f);
            nameLabel = labelObject.AddComponent<TextMesh>();
            nameLabel.anchor = TextAnchor.MiddleCenter;
            nameLabel.alignment = TextAlignment.Center;
            nameLabel.fontSize = 32;
            nameLabel.characterSize = 0.08f;
            nameLabel.color = Color.white;
        }

        private void OnDisplayNameChanged(FixedString64Bytes previous, FixedString64Bytes current)
        {
            if (nameLabel != null)
            {
                nameLabel.text = current.ToString();
            }
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static string SanitizeName(string value)
        {
            value = string.IsNullOrWhiteSpace(value) ? "Steam Player" : value.Trim();
            return value.Length <= 32 ? value : value.Substring(0, 32);
        }

        private static void LockCursor(bool locked)
        {
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
        }
    }
}
