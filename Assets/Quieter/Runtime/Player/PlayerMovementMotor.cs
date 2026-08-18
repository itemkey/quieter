using UnityEngine;

namespace Quieter.Player
{
    public readonly struct PlayerMovementTuning
    {
        public const float WalkSpeed = 5f;
        public const float SprintSpeed = 8f;
        public const float CrouchSpeed = 2.6f;
        public const float GroundAcceleration = 72f;
        public const float GroundTurningAcceleration = 120f;
        public const float GroundBraking = 96f;
        public const float AirAcceleration = 14f;
        public const float Gravity = 24f;
        public const float JumpReleaseGravityMultiplier = 2.4f;
        public const float FallGravityMultiplier = 1.35f;
        public const float JumpHeight = 1.1f;
        public const float TerminalFallSpeed = 45f;
        public const float GroundStickSpeed = 3f;
        public const float GroundProbeDistance = 0.16f;
        public const float StandingHeight = 1.8f;
        public const float StandingCenterY = 0.9f;
        public const float StandingStepOffset = 0.35f;
        public const float CrouchHeight = 1.15f;
        public const float CrouchCenterY = 0.575f;
        public const float CrouchStepOffset = 0.18f;
        public const float StandingEyeHeight = 1.62f;
        public const float CrouchEyeHeight = 1.03f;
        public const byte JumpBufferTicks = 7;
        public const byte CoyoteTicks = 6;

        public static float JumpSpeed => Mathf.Sqrt(2f * Gravity * JumpHeight);
    }

    /// <summary>
    /// Shared movement rules for host, server and client prediction.
    /// </summary>
    public sealed class PlayerMovementMotor
    {
        private const float ProbeStartOffset = 0.05f;
        private readonly CharacterController controller;
        private readonly RaycastHit[] groundHits = new RaycastHit[8];
        private readonly Collider[] clearanceHits = new Collider[16];

        public PlayerMovementMotor(CharacterController characterController)
        {
            controller = characterController;
        }

        public static Vector2 AcceleratePlanar(
            Vector2 current,
            Vector2 movement,
            bool sprint,
            bool grounded,
            float deltaTime,
            bool crouched = false)
        {
            movement = Vector2.ClampMagnitude(movement, 1f);
            if (!grounded && movement.sqrMagnitude < 0.0001f)
            {
                return current;
            }

            var targetSpeed = crouched
                ? PlayerMovementTuning.CrouchSpeed
                : sprint
                    ? PlayerMovementTuning.SprintSpeed
                    : PlayerMovementTuning.WalkSpeed;
            var target = movement * targetSpeed;
            var acceleration = PlayerMovementTuning.AirAcceleration;
            if (grounded)
            {
                if (movement.sqrMagnitude < 0.0001f)
                {
                    acceleration = PlayerMovementTuning.GroundBraking;
                }
                else if (current.sqrMagnitude < 0.0001f)
                {
                    acceleration = PlayerMovementTuning.GroundAcceleration;
                }
                else
                {
                    var alignment = Vector2.Dot(current.normalized, movement.normalized);
                    var turning = 1f - Mathf.Clamp01(alignment);
                    acceleration = Mathf.Lerp(
                        PlayerMovementTuning.GroundAcceleration,
                        PlayerMovementTuning.GroundTurningAcceleration,
                        turning);

                    // Dropping out of sprint or easing an analog stick should feel
                    // as decisive as releasing the movement input.
                    if (alignment > 0.95f && target.sqrMagnitude < current.sqrMagnitude)
                    {
                        acceleration = Mathf.Max(
                            acceleration,
                            PlayerMovementTuning.GroundBraking);
                    }
                }
            }

            return Vector2.MoveTowards(current, target, acceleration * deltaTime);
        }

        public static Vector3 ProjectPlanarOnGround(Vector3 planarVelocity, Vector3 groundNormal)
        {
            if (planarVelocity.sqrMagnitude < 0.000001f
                || groundNormal.sqrMagnitude < 0.5f)
            {
                return planarVelocity;
            }

            var tangent = Vector3.ProjectOnPlane(planarVelocity, groundNormal);
            return tangent.sqrMagnitude > 0.000001f
                ? tangent.normalized * planarVelocity.magnitude
                : Vector3.zero;
        }

        public static bool RegisterJump(
            ref PlayerNetworkState state,
            uint jumpPressId,
            bool grounded)
        {
            var isNewPress = jumpPressId != state.LastObservedJumpPressId;
            if (isNewPress)
            {
                state.LastObservedJumpPressId = jumpPressId;
                state.JumpBufferTicks = PlayerMovementTuning.JumpBufferTicks;
            }

            var shouldJump = state.JumpBufferTicks > 0
                && state.LastConsumedJumpPressId != state.LastObservedJumpPressId
                && (grounded || state.CoyoteTicks > 0);
            if (shouldJump)
            {
                state.LastConsumedJumpPressId = state.LastObservedJumpPressId;
                state.JumpBufferTicks = 0;
                state.CoyoteTicks = 0;
                return true;
            }

            if (!isNewPress && state.JumpBufferTicks > 0)
            {
                state.JumpBufferTicks--;
            }

            return false;
        }

        public CollisionFlags Simulate(
            ref PlayerNetworkState state,
            PlayerInputFrame input,
            float deltaTime)
        {
            if (!controller.enabled)
            {
                return CollisionFlags.None;
            }

            EnsureStance(state.Crouched);
            var groundedBeforeMove = IsOnWalkableGround(out var groundBeforeMove);
            if (groundedBeforeMove)
            {
                state.CoyoteTicks = PlayerMovementTuning.CoyoteTicks;
            }

            UpdateStance(ref state, input, groundedBeforeMove);

            var yawRotation = Quaternion.Euler(0f, input.Yaw, 0f);
            var localVelocity = Quaternion.Inverse(yawRotation)
                * new Vector3(state.Velocity.x, 0f, state.Velocity.z);
            var planar = AcceleratePlanar(
                new Vector2(localVelocity.x, localVelocity.z),
                input.Movement,
                input.Sprint && !state.Crouched,
                groundedBeforeMove,
                deltaTime,
                state.Crouched);

            var jumpStarted = RegisterJump(ref state, input.JumpPressId, groundedBeforeMove);
            var vertical = state.Velocity.y;
            if (jumpStarted)
            {
                vertical = PlayerMovementTuning.JumpSpeed;
                groundedBeforeMove = false;
            }
            else if (groundedBeforeMove && vertical <= 0f)
            {
                vertical = -PlayerMovementTuning.GroundStickSpeed;
            }
            else
            {
                var gravityMultiplier = vertical < 0f
                    ? PlayerMovementTuning.FallGravityMultiplier
                    : input.JumpHeld
                        ? 1f
                        : PlayerMovementTuning.JumpReleaseGravityMultiplier;
                vertical = Mathf.Max(
                    vertical - PlayerMovementTuning.Gravity * gravityMultiplier * deltaTime,
                    -PlayerMovementTuning.TerminalFallSpeed);
            }

            var worldPlanar = yawRotation * new Vector3(planar.x, 0f, planar.y);
            var movementPlanar = groundedBeforeMove
                ? ProjectPlanarOnGround(worldPlanar, groundBeforeMove.normal)
                : worldPlanar;
            controller.transform.rotation = yawRotation;
            var flags = controller.Move(
                new Vector3(
                    movementPlanar.x,
                    movementPlanar.y + vertical,
                    movementPlanar.z) * deltaTime);

            if ((flags & CollisionFlags.Above) != 0 && vertical > 0f)
            {
                vertical = 0f;
            }

            var groundedAfterMove = !jumpStarted && (flags & CollisionFlags.Below) != 0;
            if (!groundedAfterMove && !jumpStarted && vertical <= 0f
                && IsOnWalkableGround(out var groundHit))
            {
                var gap = Mathf.Max(0f, groundHit.distance - ProbeStartOffset);
                if (gap <= PlayerMovementTuning.GroundProbeDistance)
                {
                    if (gap > 0.001f)
                    {
                        flags |= controller.Move(Vector3.down * (gap + 0.005f));
                    }

                    groundedAfterMove = true;
                }
            }

            if (!groundedAfterMove && state.CoyoteTicks > 0)
            {
                state.CoyoteTicks--;
            }
            else if (groundedAfterMove)
            {
                state.CoyoteTicks = PlayerMovementTuning.CoyoteTicks;
                if (vertical < 0f)
                {
                    vertical = -PlayerMovementTuning.GroundStickSpeed;
                }
            }

            state.Position = controller.transform.position;
            state.Velocity = new Vector3(worldPlanar.x, vertical, worldPlanar.z);
            state.Yaw = Mathf.Repeat(input.Yaw, 360f);
            state.Grounded = groundedAfterMove;
            return flags;
        }

        public void Warp(PlayerNetworkState state)
        {
            var wasEnabled = controller.enabled;
            if (wasEnabled)
            {
                controller.enabled = false;
            }

            ApplyStance(state.Crouched);

            controller.transform.SetPositionAndRotation(
                state.Position,
                Quaternion.Euler(0f, state.Yaw, 0f));

            if (wasEnabled)
            {
                controller.enabled = true;
            }
        }

        public bool CanStand()
        {
            if (!controller.enabled)
            {
                return true;
            }

            var transform = controller.transform;
            var up = transform.up;
            var currentCenter = transform.TransformPoint(controller.center);
            var currentBottom = currentCenter - up * (controller.height * 0.5f);
            var radius = Mathf.Max(
                0.01f,
                controller.radius - controller.skinWidth * 0.5f);
            var standingCenter = currentBottom
                + up * (PlayerMovementTuning.StandingHeight * 0.5f);
            var sphereOffset = Mathf.Max(
                0f,
                PlayerMovementTuning.StandingHeight * 0.5f - radius);
            var count = Physics.OverlapCapsuleNonAlloc(
                standingCenter - up * sphereOffset,
                standingCenter + up * sphereOffset,
                radius,
                clearanceHits,
                Physics.AllLayers,
                QueryTriggerInteraction.Ignore);

            for (var index = 0; index < count; index++)
            {
                var hit = clearanceHits[index];
                clearanceHits[index] = null;
                if (hit == null
                    || hit == controller
                    || hit.transform == transform
                    || hit.transform.IsChildOf(transform))
                {
                    continue;
                }

                return false;
            }

            // A full buffer is treated conservatively: an uninspected collider may
            // still be blocking the standing capsule.
            return count < clearanceHits.Length;
        }

        private void UpdateStance(
            ref PlayerNetworkState state,
            PlayerInputFrame input,
            bool grounded)
        {
            var isNewJumpPress = input.JumpPressId != state.LastObservedJumpPressId;
            var hasBufferedJump = state.JumpBufferTicks > 0
                && state.LastConsumedJumpPressId != state.LastObservedJumpPressId;
            var wantsToJump = isNewJumpPress || hasBufferedJump;
            if (state.Crouched)
            {
                if (wantsToJump && (grounded || state.CoyoteTicks > 0))
                {
                    if (CanStand())
                    {
                        state.Crouched = false;
                        ApplyStance(crouched: false);
                    }
                    else
                    {
                        ConsumeJumpPress(
                            ref state,
                            isNewJumpPress
                                ? input.JumpPressId
                                : state.LastObservedJumpPressId);
                    }

                    return;
                }

                if (!grounded)
                {
                    return;
                }

                if (!input.CrouchHeld && CanStand())
                {
                    state.Crouched = false;
                    ApplyStance(crouched: false);
                }

                return;
            }

            if (grounded && input.CrouchHeld && !wantsToJump)
            {
                state.Crouched = true;
                ApplyStance(crouched: true);
            }
        }

        private static void ConsumeJumpPress(ref PlayerNetworkState state, uint jumpPressId)
        {
            state.LastObservedJumpPressId = jumpPressId;
            state.LastConsumedJumpPressId = jumpPressId;
            state.JumpBufferTicks = 0;
        }

        private void EnsureStance(bool crouched)
        {
            var targetHeight = crouched
                ? PlayerMovementTuning.CrouchHeight
                : PlayerMovementTuning.StandingHeight;
            var targetStepOffset = crouched
                ? PlayerMovementTuning.CrouchStepOffset
                : PlayerMovementTuning.StandingStepOffset;
            if (Mathf.Abs(controller.height - targetHeight) > 0.0001f
                || Mathf.Abs(controller.stepOffset - targetStepOffset) > 0.0001f)
            {
                ApplyStance(crouched);
            }
        }

        private void ApplyStance(bool crouched)
        {
            controller.height = crouched
                ? PlayerMovementTuning.CrouchHeight
                : PlayerMovementTuning.StandingHeight;
            var center = controller.center;
            center.y = crouched
                ? PlayerMovementTuning.CrouchCenterY
                : PlayerMovementTuning.StandingCenterY;
            controller.center = center;
            controller.stepOffset = crouched
                ? PlayerMovementTuning.CrouchStepOffset
                : PlayerMovementTuning.StandingStepOffset;
        }

        private bool IsOnWalkableGround(out RaycastHit bestHit)
        {
            bestHit = default;
            var transform = controller.transform;
            var up = transform.up;
            var radius = Mathf.Max(0.01f, controller.radius * 0.92f);
            var halfHeight = Mathf.Max(controller.height * 0.5f, controller.radius);
            var center = transform.TransformPoint(controller.center);
            var bottomSphereCenter = center - up * (halfHeight - controller.radius);
            var origin = bottomSphereCenter + up * ProbeStartOffset;
            var distance = PlayerMovementTuning.GroundProbeDistance + ProbeStartOffset;
            var count = Physics.SphereCastNonAlloc(
                origin,
                radius,
                -up,
                groundHits,
                distance,
                Physics.AllLayers,
                QueryTriggerInteraction.Ignore);

            var closest = float.PositiveInfinity;
            for (var index = 0; index < count; index++)
            {
                var hit = groundHits[index];
                if (hit.collider == null || hit.collider == controller
                    || Vector3.Angle(hit.normal, up) > controller.slopeLimit + 0.5f
                    || hit.distance >= closest)
                {
                    continue;
                }

                closest = hit.distance;
                bestHit = hit;
            }

            return closest < float.PositiveInfinity;
        }
    }

    public enum PlayerGait : byte
    {
        Idle,
        Crouch,
        Walk,
        Sprint,
        Airborne,
    }

    public readonly struct PlayerCameraPose
    {
        public PlayerCameraPose(Vector3 positionOffset, Vector3 rotationOffset)
        {
            PositionOffset = positionOffset;
            RotationOffset = rotationOffset;
        }

        public Vector3 PositionOffset { get; }
        public Vector3 RotationOffset { get; }
        public static PlayerCameraPose Neutral => new(Vector3.zero, Vector3.zero);
    }

    /// <summary>
    /// Distance-driven first-person camera motion. A full phase is a left/right
    /// stride, so foot plants remain tied to travelled ground distance.
    /// </summary>
    public static class PlayerCameraMotion
    {
        public const float CrouchStrideLength = 2.8f;
        public const float WalkStrideLength = 4.8f;
        public const float SprintStrideLength = 5.2f;
        public const float PoseResponse = 18f;
        private const float TwoPi = Mathf.PI * 2f;

        private readonly struct MotionProfile
        {
            public MotionProfile(
                float strideLength,
                Vector3 positionAmplitude,
                float rollAmplitude)
            {
                StrideLength = strideLength;
                PositionAmplitude = positionAmplitude;
                RollAmplitude = rollAmplitude;
            }

            public float StrideLength { get; }
            public Vector3 PositionAmplitude { get; }
            public float RollAmplitude { get; }
        }

        public static PlayerGait ResolveGait(
            bool grounded,
            bool crouched,
            bool sprintRequested,
            float planarSpeed)
        {
            if (!grounded)
            {
                return PlayerGait.Airborne;
            }

            if (planarSpeed <= 0.05f)
            {
                return PlayerGait.Idle;
            }

            if (crouched)
            {
                return PlayerGait.Crouch;
            }

            return sprintRequested ? PlayerGait.Sprint : PlayerGait.Walk;
        }

        public static float AdvancePhase(
            float phase,
            float groundDistance,
            PlayerGait gait)
        {
            if (groundDistance <= 0f
                || gait == PlayerGait.Idle
                || gait == PlayerGait.Airborne)
            {
                return phase;
            }

            var strideLength = GetProfile(gait).StrideLength;
            return Mathf.Repeat(
                phase + groundDistance / strideLength * TwoPi,
                TwoPi);
        }

        public static PlayerCameraPose CalculateTarget(
            float phase,
            PlayerGait gait,
            float movementWeight,
            bool enabled)
        {
            if (!enabled
                || gait == PlayerGait.Idle
                || gait == PlayerGait.Airborne)
            {
                return PlayerCameraPose.Neutral;
            }

            var profile = GetProfile(gait);
            var weight = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(movementWeight));
            var lateral = Mathf.Sin(phase);
            var footPlant = Mathf.Pow(Mathf.Abs(lateral), 6f);
            var foreAft = Mathf.Sin(phase * 2f);
            var position = new Vector3(
                lateral * profile.PositionAmplitude.x,
                -footPlant * profile.PositionAmplitude.y,
                -foreAft * profile.PositionAmplitude.z) * weight;
            var rotation = new Vector3(
                -footPlant * profile.RollAmplitude * 0.35f,
                0f,
                -lateral * profile.RollAmplitude) * weight;
            return new PlayerCameraPose(position, rotation);
        }

        public static PlayerCameraPose Damp(
            PlayerCameraPose current,
            PlayerCameraPose target,
            float deltaTime)
        {
            if (deltaTime <= 0f)
            {
                return current;
            }

            var blend = 1f - Mathf.Exp(-PoseResponse * deltaTime);
            return new PlayerCameraPose(
                Vector3.LerpUnclamped(
                    current.PositionOffset,
                    target.PositionOffset,
                    blend),
                Vector3.LerpUnclamped(
                    current.RotationOffset,
                    target.RotationOffset,
                    blend));
        }

        public static float MovementWeight(PlayerGait gait, float planarSpeed)
        {
            var referenceSpeed = gait switch
            {
                PlayerGait.Crouch => PlayerMovementTuning.CrouchSpeed,
                PlayerGait.Sprint => PlayerMovementTuning.SprintSpeed,
                PlayerGait.Walk => PlayerMovementTuning.WalkSpeed,
                _ => 1f,
            };
            return Mathf.InverseLerp(0.1f, referenceSpeed, planarSpeed);
        }

        private static MotionProfile GetProfile(PlayerGait gait)
        {
            return gait switch
            {
                PlayerGait.Crouch => new MotionProfile(
                    CrouchStrideLength,
                    new Vector3(0.006f, 0.003f, 0.002f),
                    0.08f),
                PlayerGait.Sprint => new MotionProfile(
                    SprintStrideLength,
                    new Vector3(0.016f, 0.010f, 0.007f),
                    0.25f),
                _ => new MotionProfile(
                    WalkStrideLength,
                    new Vector3(0.012f, 0.006f, 0.003f),
                    0.15f),
            };
        }
    }
}
