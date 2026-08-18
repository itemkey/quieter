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
        private const float CameraStepHalfLife = 0.085f;
        private const float CameraStanceResponse = 18f;
        private const float RemoteStanceResponse = 12f;
        private const float LandingHalfLife = 0.065f;
        private const float MaximumLandingOffset = 0.012f;
        private const float MaximumLandingPitch = 0.2f;
        private const float MinimumSmoothedStepHeight = 0.045f;

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
        private PlayerCameraPose bobPose;
        private Vector3 presentationLocalPosition;
        private Vector3 presentationLocalScale;
        private Quaternion presentationLocalRotation;
        private float cameraEyeHeight = PlayerMovementTuning.StandingEyeHeight;
        private float cameraStepOffset;
        private float pendingCameraStepOffset;
        private float remoteCrouchBlend;
        private float landingOffset;
        private float landingPitch;
        private float viewYaw;
        private float pitch;
        private float bobPhase;
        private PlayerGait cameraGait;
        private uint nextInputSequence;
        private uint jumpPressId;
        private uint serverSimulationTick;
        private int missingInputTicks;
        private double latestInputReceivedAt;
        private bool sampledSprint;
        private bool sampledJumpHeld;
        private bool sampledCrouchHeld;
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
                presentationLocalScale = presentationRoot.localScale;
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
            cameraEyeHeight = simulatedState.Crouched
                ? PlayerMovementTuning.CrouchEyeHeight
                : PlayerMovementTuning.StandingEyeHeight;
            remoteCrouchBlend = simulatedState.Crouched ? 1f : 0f;
            viewYaw = simulatedState.Yaw;
            previousSimulationPosition = simulatedState.Position;
            currentSimulationPosition = simulatedState.Position;
            latestServerInput.Yaw = simulatedState.Yaw;
            CreateNameLabel();
            OnDisplayNameChanged(default, displayName.Value);
            if (nameLabel != null)
            {
                nameLabel.gameObject.SetActive(!IsOwner);
            }

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

            CommitPendingCameraStep();
            previousSimulationPosition = currentSimulationPosition;
            var wasGrounded = simulatedState.Grounded;
            var previousVerticalVelocity = simulatedState.Velocity.y;
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
                if (IsOwner)
                {
                    AccumulateCameraStep(wasGrounded, simulatedState.Grounded);
                    AccumulateCameraMotion(
                        wasGrounded,
                        previousVerticalVelocity,
                        input.Sprint,
                        deltaTime);
                }

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
            AccumulateCameraStep(wasGrounded, simulatedState.Grounded);
            AccumulateCameraMotion(
                wasGrounded,
                previousVerticalVelocity,
                predictedInput.Sprint,
                deltaTime);
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
                sampledJumpHeld = false;
                sampledCrouchHeld = false;
                return;
            }

            var movement = Vector2.zero;
            if (keyboard.wKey.isPressed) movement.y += 1f;
            if (keyboard.sKey.isPressed) movement.y -= 1f;
            if (keyboard.dKey.isPressed) movement.x += 1f;
            if (keyboard.aKey.isPressed) movement.x -= 1f;
            sampledMovement = Vector2.ClampMagnitude(movement, 1f);
            sampledSprint = keyboard.leftShiftKey.isPressed;
            sampledJumpHeld = keyboard.spaceKey.isPressed;
            sampledCrouchHeld = keyboard.leftCtrlKey.isPressed;
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
                JumpHeld = sampledJumpHeld,
                Sprint = sampledSprint,
                CrouchHeld = sampledCrouchHeld,
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
                input.JumpHeld = false;
                input.CrouchHeld = false;
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
            var headBobEnabled = ClientPreferences.HeadBobEnabled;
            var bobTarget = PlayerCameraMotion.CalculateTarget(
                bobPhase,
                cameraGait,
                PlayerCameraMotion.MovementWeight(cameraGait, planarSpeed),
                headBobEnabled);
            bobPose = PlayerCameraMotion.Damp(
                bobPose,
                bobTarget,
                Time.unscaledDeltaTime);

            var eyeHeightTarget = simulatedState.Crouched
                ? PlayerMovementTuning.CrouchEyeHeight
                : PlayerMovementTuning.StandingEyeHeight;
            var stanceBlend = 1f - Mathf.Exp(
                -CameraStanceResponse * Time.unscaledDeltaTime);
            cameraEyeHeight = Mathf.LerpUnclamped(
                cameraEyeHeight,
                eyeHeightTarget,
                stanceBlend);

            if (Mathf.Abs(cameraStepOffset) > 0.00001f)
            {
                var decay = Mathf.Pow(0.5f, Time.unscaledDeltaTime / CameraStepHalfLife);
                cameraStepOffset *= decay;
            }
            else
            {
                cameraStepOffset = 0f;
            }

            if (Mathf.Abs(landingOffset) > 0.00001f
                || Mathf.Abs(landingPitch) > 0.00001f)
            {
                var landingDecay = Mathf.Pow(
                    0.5f,
                    Time.unscaledDeltaTime / LandingHalfLife);
                landingOffset *= landingDecay;
                landingPitch *= landingDecay;
            }
            else
            {
                landingOffset = 0f;
                landingPitch = 0f;
            }

            var renderedStepOffset = cameraStepOffset + pendingCameraStepOffset * alpha;
            var viewRotation = Quaternion.Euler(0f, viewYaw, 0f);
            cameraPivot.position = bodyPosition
                + Vector3.up * (cameraEyeHeight + renderedStepOffset + landingOffset)
                + viewRotation * bobPose.PositionOffset;
            cameraPivot.rotation = Quaternion.Euler(
                pitch + bobPose.RotationOffset.x + landingPitch,
                viewYaw + bobPose.RotationOffset.y,
                bobPose.RotationOffset.z);
        }

        private void CommitPendingCameraStep()
        {
            if (!IsOwner || Mathf.Abs(pendingCameraStepOffset) <= 0.00001f)
            {
                pendingCameraStepOffset = 0f;
                return;
            }

            var maximumOffset = characterController.stepOffset
                + PlayerMovementTuning.GroundProbeDistance;
            cameraStepOffset = Mathf.Clamp(
                cameraStepOffset + pendingCameraStepOffset,
                -maximumOffset,
                maximumOffset);
            pendingCameraStepOffset = 0f;
        }

        private void AccumulateCameraStep(bool wasGrounded, bool isGrounded)
        {
            if (!IsOwner || !wasGrounded || !isGrounded)
            {
                return;
            }

            var verticalStep = currentSimulationPosition.y - previousSimulationPosition.y;
            var maximumStep = characterController.stepOffset
                + PlayerMovementTuning.GroundProbeDistance;
            if (Mathf.Abs(verticalStep) < MinimumSmoothedStepHeight
                || Mathf.Abs(verticalStep) > maximumStep)
            {
                return;
            }

            pendingCameraStepOffset = Mathf.Clamp(
                pendingCameraStepOffset - verticalStep,
                -maximumStep,
                maximumStep);
        }

        private void AccumulateCameraMotion(
            bool wasGrounded,
            float previousVerticalVelocity,
            bool sprintRequested,
            float deltaTime)
        {
            if (!IsOwner)
            {
                return;
            }

            var planarSpeed = new Vector2(
                simulatedState.Velocity.x,
                simulatedState.Velocity.z).magnitude;
            cameraGait = PlayerCameraMotion.ResolveGait(
                simulatedState.Grounded,
                simulatedState.Crouched,
                sprintRequested && !simulatedState.Crouched,
                planarSpeed);

            if (!ClientPreferences.HeadBobEnabled)
            {
                bobPhase = 0f;
                return;
            }

            if (!wasGrounded && simulatedState.Grounded && previousVerticalVelocity < -2f)
            {
                var landingWeight = Mathf.InverseLerp(3f, 18f, -previousVerticalVelocity);
                landingOffset = Mathf.Min(
                    landingOffset,
                    -MaximumLandingOffset * landingWeight);
                landingPitch = Mathf.Max(
                    landingPitch,
                    MaximumLandingPitch * landingWeight);
            }

            if (cameraGait == PlayerGait.Idle || cameraGait == PlayerGait.Airborne)
            {
                bobPhase = 0f;
                return;
            }

            if (!wasGrounded || !simulatedState.Grounded)
            {
                return;
            }

            var displacement = currentSimulationPosition - previousSimulationPosition;
            var groundDistance = new Vector2(displacement.x, displacement.z).magnitude;
            var maximumExpectedDistance = PlayerMovementTuning.SprintSpeed
                * deltaTime * 1.75f + 0.02f;
            if (groundDistance <= maximumExpectedDistance)
            {
                bobPhase = PlayerCameraMotion.AdvancePhase(
                    bobPhase,
                    groundDistance,
                    cameraGait);
            }
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
            var renderedCrouched = newest.State.Crouched;

            if (targetTick <= snapshotHistory[0].State.ServerTick)
            {
                renderedPosition = snapshotHistory[0].State.Position;
                renderedYaw = snapshotHistory[0].State.Yaw;
                renderedCrouched = snapshotHistory[0].State.Crouched;
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
                    renderedCrouched = interpolation < 0.5f
                        ? from.Crouched
                        : to.Crouched;
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

            var stanceBlend = 1f - Mathf.Exp(
                -RemoteStanceResponse * Time.unscaledDeltaTime);
            remoteCrouchBlend = Mathf.LerpUnclamped(
                remoteCrouchBlend,
                renderedCrouched ? 1f : 0f,
                stanceBlend);
            var renderedHeight = Mathf.Lerp(
                PlayerMovementTuning.StandingHeight,
                PlayerMovementTuning.CrouchHeight,
                remoteCrouchBlend);
            var renderedCenterY = Mathf.Lerp(
                PlayerMovementTuning.StandingCenterY,
                PlayerMovementTuning.CrouchCenterY,
                remoteCrouchBlend);
            var stanceLocalPosition = presentationLocalPosition;
            stanceLocalPosition.y += renderedCenterY
                - PlayerMovementTuning.StandingCenterY;
            var stanceLocalScale = presentationLocalScale;
            stanceLocalScale.y = presentationLocalScale.y
                * renderedHeight / PlayerMovementTuning.StandingHeight;
            presentationRoot.localScale = stanceLocalScale;

            var rotation = Quaternion.Euler(0f, renderedYaw, 0f);
            presentationRoot.SetPositionAndRotation(
                renderedPosition + rotation * stanceLocalPosition,
                rotation * presentationLocalRotation);
            if (nameLabel != null && nameLabel.gameObject.activeSelf)
            {
                nameLabel.transform.position = renderedPosition
                    + Vector3.up * (renderedHeight + 0.35f);
            }
        }

        private void CreateNameLabel()
        {
            if (presentationRoot == null || nameLabel != null)
            {
                return;
            }

            var labelObject = new GameObject("PlayerName");
            labelObject.transform.SetParent(transform, false);
            labelObject.transform.localPosition = new Vector3(
                0f,
                PlayerMovementTuning.StandingHeight + 0.35f,
                0f);
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
