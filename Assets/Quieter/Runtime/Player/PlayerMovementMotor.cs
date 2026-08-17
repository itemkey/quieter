using UnityEngine;

namespace Quieter.Player
{
    public readonly struct PlayerMovementTuning
    {
        public const float WalkSpeed = 5f;
        public const float SprintSpeed = 8f;
        public const float GroundAcceleration = 60f;
        public const float GroundBraking = 80f;
        public const float AirAcceleration = 18f;
        public const float Gravity = 24f;
        public const float JumpHeight = 1.1f;
        public const float TerminalFallSpeed = 45f;
        public const float GroundStickSpeed = 3f;
        public const float GroundProbeDistance = 0.16f;
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

        public PlayerMovementMotor(CharacterController characterController)
        {
            controller = characterController;
        }

        public static Vector2 AcceleratePlanar(
            Vector2 current,
            Vector2 movement,
            bool sprint,
            bool grounded,
            float deltaTime)
        {
            movement = Vector2.ClampMagnitude(movement, 1f);
            if (!grounded && movement.sqrMagnitude < 0.0001f)
            {
                return current;
            }

            var targetSpeed = sprint
                ? PlayerMovementTuning.SprintSpeed
                : PlayerMovementTuning.WalkSpeed;
            var target = movement * targetSpeed;
            var acceleration = grounded
                ? (movement.sqrMagnitude > 0.0001f
                    ? PlayerMovementTuning.GroundAcceleration
                    : PlayerMovementTuning.GroundBraking)
                : PlayerMovementTuning.AirAcceleration;
            return Vector2.MoveTowards(current, target, acceleration * deltaTime);
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

            var groundedBeforeMove = IsOnWalkableGround(out _);
            if (groundedBeforeMove)
            {
                state.CoyoteTicks = PlayerMovementTuning.CoyoteTicks;
            }

            var yawRotation = Quaternion.Euler(0f, input.Yaw, 0f);
            var localVelocity = Quaternion.Inverse(yawRotation)
                * new Vector3(state.Velocity.x, 0f, state.Velocity.z);
            var planar = AcceleratePlanar(
                new Vector2(localVelocity.x, localVelocity.z),
                input.Movement,
                input.Sprint,
                groundedBeforeMove,
                deltaTime);

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
                vertical = Mathf.Max(
                    vertical - PlayerMovementTuning.Gravity * deltaTime,
                    -PlayerMovementTuning.TerminalFallSpeed);
            }

            var worldPlanar = yawRotation * new Vector3(planar.x, 0f, planar.y);
            controller.transform.rotation = yawRotation;
            var flags = controller.Move(
                new Vector3(worldPlanar.x, vertical, worldPlanar.z) * deltaTime);

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

            controller.transform.SetPositionAndRotation(
                state.Position,
                Quaternion.Euler(0f, state.Yaw, 0f));

            if (wasEnabled)
            {
                controller.enabled = true;
            }
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
}
