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
        public const float AirAcceleration = 6f;
        public const float AirborneSpeedCap = 7.3f;
        // Keep the taller jump compact: the higher gravity offsets the extra
        // clearance so the player does not regain the old "moon jump" hang time.
        public const float Gravity = 40f;
        public const float StandingJumpHeight = 1.47f;
        public const float WalkJumpHeight = 1.52f;
        public const float SprintJumpHeight = 1.57f;
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

        public static float CalculateJumpHeight(float planarSpeed)
        {
            planarSpeed = Mathf.Max(0f, planarSpeed);
            if (planarSpeed <= WalkSpeed)
            {
                var walkWeight = Mathf.SmoothStep(
                    0f,
                    1f,
                    Mathf.InverseLerp(0f, WalkSpeed, planarSpeed));
                return Mathf.Lerp(StandingJumpHeight, WalkJumpHeight, walkWeight);
            }

            var sprintWeight = Mathf.SmoothStep(
                0f,
                1f,
                Mathf.InverseLerp(WalkSpeed, SprintSpeed, planarSpeed));
            return Mathf.Lerp(WalkJumpHeight, SprintJumpHeight, sprintWeight);
        }

        public static float CalculateJumpSpeed(float planarSpeed)
        {
            return Mathf.Sqrt(2f * Gravity * CalculateJumpHeight(planarSpeed));
        }
    }

    /// <summary>
    /// Shared movement rules for host, server and client prediction.
    /// </summary>
    public sealed class PlayerMovementMotor
    {
        private const float ProbeStartOffset = 0.05f;
        private const float AirWallContactPadding = 0.1f;
        private const float GroundSupportTolerance = 0.02f;
        private readonly CharacterController controller;
        private readonly RaycastHit[] groundHits = new RaycastHit[8];
        private readonly RaycastHit[] airObstacleHits = new RaycastHit[8];
        private readonly Collider[] airObstacleOverlaps = new Collider[8];
        private readonly Collider[] topSupportOverlaps = new Collider[8];
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
                if (current.magnitude <= PlayerMovementTuning.AirborneSpeedCap)
                {
                    return current;
                }

                var cappedMomentum = current.normalized
                    * PlayerMovementTuning.AirborneSpeedCap;
                return Vector2.MoveTowards(
                    current,
                    cappedMomentum,
                    PlayerMovementTuning.AirAcceleration * deltaTime);
            }

            var targetSpeed = crouched
                ? PlayerMovementTuning.CrouchSpeed
                : sprint
                    ? PlayerMovementTuning.SprintSpeed
                    : PlayerMovementTuning.WalkSpeed;
            if (!grounded)
            {
                targetSpeed = Mathf.Min(
                    targetSpeed,
                    PlayerMovementTuning.AirborneSpeedCap);
            }

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
            // The probe remains within ground range for the first few airborne ticks.
            // Ignoring it while rising prevents moving jumps from re-entering ground
            // acceleration and slope projection immediately after takeoff.
            var canAttachToGround = state.Velocity.y <= 0f;
            var groundBeforeMove = default(RaycastHit);
            var groundedBeforeMove = canAttachToGround
                && IsOnWalkableGround(out groundBeforeMove);
            var recoveredBoxTopBeforeMove = false;
            if (!groundedBeforeMove
                && canAttachToGround
                && TryRecoverBoxTopSupport(
                    state.Grounded || state.CoyoteTicks > 0))
            {
                // At a rotated corner the capsule footprint can be supported even
                // though a downward sphere cast has no single top-face hit.
                recoveredBoxTopBeforeMove = true;
                groundedBeforeMove = true;
            }

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
                vertical = PlayerMovementTuning.CalculateJumpSpeed(planar.magnitude);
                groundedBeforeMove = false;
            }
            else if (groundedBeforeMove && vertical <= 0f)
            {
                vertical = -PlayerMovementTuning.GroundStickSpeed;
            }

            var verticalBeforeGravity = vertical;
            var verticalMoveSpeed = vertical;
            if (!groundedBeforeMove)
            {
                var nextVertical = Mathf.Max(
                    vertical - PlayerMovementTuning.Gravity * deltaTime,
                    -PlayerMovementTuning.TerminalFallSpeed);
                verticalMoveSpeed = (vertical + nextVertical) * 0.5f;
                vertical = nextVertical;
            }

            var worldPlanar = yawRotation * new Vector3(planar.x, 0f, planar.y);
            var movementPlanar = groundedBeforeMove
                ? ProjectPlanarOnGround(worldPlanar, groundBeforeMove.normal)
                : worldPlanar;
            var canUseStepOffset = groundedBeforeMove || state.CoyoteTicks > 0;
            controller.stepOffset = canUseStepOffset
                ? ConfiguredStepOffset(state.Crouched)
                : 0f;
            controller.transform.rotation = yawRotation;
            var positionBeforeMove = controller.transform.position;
            var ballisticApex = positionBeforeMove.y;
            if (!groundedBeforeMove)
            {
                ballisticApex += verticalBeforeGravity * verticalBeforeGravity
                    / (2f * PlayerMovementTuning.Gravity);
            }

            var planarStep = new Vector3(
                movementPlanar.x * deltaTime,
                0f,
                movementPlanar.z * deltaTime);
            if (!groundedBeforeMove)
            {
                planarStep = ConstrainAirbornePlanarStep(planarStep, ballisticApex);
                // Persist the same wall-projected velocity that is actually moved.
                // Keeping the blocked component would make prediction push into
                // the wall again every tick and accumulate visible corrections.
                if (deltaTime > 0f)
                {
                    movementPlanar = planarStep / deltaTime;
                    worldPlanar = new Vector3(
                        movementPlanar.x,
                        0f,
                        movementPlanar.z);
                }
            }

            var requestedVerticalStep = (movementPlanar.y + verticalMoveSpeed) * deltaTime;
            var flags = controller.Move(planarStep + Vector3.up * requestedVerticalStep);

            // Capsule collision resolution can push a moving player upward along a
            // box edge. During ascent that would create height beyond the ballistic
            // jump and make nominally blocking props climbable.
            if (!groundedBeforeMove && verticalMoveSpeed > 0f)
            {
                var excessRise = controller.transform.position.y
                    - (positionBeforeMove.y + requestedVerticalStep);
                if (excessRise > 0.001f)
                {
                    flags |= controller.Move(Vector3.down * excessRise);
                }
            }

            if (!groundedBeforeMove)
            {
                if (controller.transform.position.y > ballisticApex + 0.001f)
                {
                    // The capsule's rounded foot can otherwise ride over a box
                    // corner whose top is above the physically reachable apex.
                    // Replaying the vertical move through CharacterController here
                    // would resolve the same overlap upward again and ratchet the
                    // capsule onto the forbidden top face, so restore the intended
                    // ballistic position directly.
                    var correctedPosition = positionBeforeMove
                        + Vector3.up * requestedVerticalStep;
                    controller.enabled = false;
                    controller.transform.position = correctedPosition;
                    controller.enabled = true;
                    flags &= ~CollisionFlags.Below;
                    flags |= CollisionFlags.Sides;
                }
            }

            if ((flags & CollisionFlags.Above) != 0 && vertical > 0f)
            {
                vertical = 0f;
            }

            // Preserve an already verified ground contact across a step seam.
            // Below alone is not enough after takeoff: the rounded capsule foot
            // can report it against a vertical edge and enable false step climbing.
            var groundedAfterMove = !jumpStarted
                && groundedBeforeMove
                && vertical <= 0f
                && ((flags & CollisionFlags.Below) != 0
                    || recoveredBoxTopBeforeMove);
            if (!jumpStarted && vertical <= 0f
                && IsOnWalkableGround(out var groundHit))
            {
                var gap = Mathf.Max(0f, groundHit.distance - ProbeStartOffset);
                // CharacterController's skin can overlap a top face slightly above
                // the ballistic apex. Do not turn that overlap into a landing: it
                // would make a nominally higher cube climbable at the edge.
                var surfaceWithinBallisticReach = groundedBeforeMove
                    || groundHit.point.y <= ballisticApex + 0.005f;
                if (gap <= PlayerMovementTuning.GroundProbeDistance
                    && surfaceWithinBallisticReach)
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
            controller.stepOffset = groundedAfterMove || state.CoyoteTicks > 0
                ? ConfiguredStepOffset(state.Crouched)
                : 0f;
            return flags;
        }

        private Vector3 ConstrainAirbornePlanarStep(
            Vector3 planarStep,
            float ballisticApex)
        {
            var distance = planarStep.magnitude;
            if (distance <= 0.000001f)
            {
                return planarStep;
            }

            var transform = controller.transform;
            var up = transform.up;
            var halfHeight = Mathf.Max(controller.height * 0.5f, controller.radius);
            var center = transform.TransformPoint(controller.center);
            var bottom = center - up * halfHeight;
            var sphereOffset = halfHeight - controller.radius;
            var bottomSphereCenter = center - up * sphereOffset;
            var topSphereCenter = center + up * sphereOffset;
            var radius = Mathf.Max(0.01f, controller.radius - controller.skinWidth * 0.25f);
            var direction = planarStep / distance;
            var closestDistance = float.PositiveInfinity;
            var blockingNormal = Vector3.zero;

            // Capsule casts do not report colliders that already overlap their
            // starting volume. Check the controller skin explicitly so holding
            // into a wall cannot rebuild blocked momentum on following ticks.
            var overlapCount = Physics.OverlapCapsuleNonAlloc(
                bottomSphereCenter,
                topSphereCenter,
                controller.radius + controller.skinWidth + AirWallContactPadding,
                airObstacleOverlaps,
                Physics.AllLayers,
                QueryTriggerInteraction.Ignore);
            for (var index = 0; index < overlapCount; index++)
            {
                var overlap = airObstacleOverlaps[index];
                airObstacleOverlaps[index] = null;
                if (overlap == null
                    || overlap == controller
                    || overlap is not BoxCollider
                    || overlap.bounds.max.y
                        <= bottom.y + controller.skinWidth + GroundSupportTolerance
                    || overlap.bounds.max.y + controller.skinWidth
                        <= ballisticApex + 0.005f)
                {
                    continue;
                }

                var separation = center - overlap.ClosestPoint(center);
                separation.y = 0f;
                if (separation.sqrMagnitude <= 0.000001f)
                {
                    // The capsule is above a supporting top face rather than
                    // beside a wall. It must remain free to steer back from an edge.
                    continue;
                }

                var normal = separation.normalized;
                if (Vector3.Dot(planarStep, normal) >= -0.000001f)
                {
                    continue;
                }

                var separationDistance = separation.magnitude;
                if (separationDistance >= closestDistance)
                {
                    continue;
                }

                closestDistance = separationDistance;
                blockingNormal = normal;
            }

            var count = Physics.CapsuleCastNonAlloc(
                bottomSphereCenter,
                topSphereCenter,
                radius,
                direction,
                airObstacleHits,
                distance + controller.skinWidth,
                Physics.AllLayers,
                QueryTriggerInteraction.Ignore);

            for (var index = 0; index < count; index++)
            {
                var hit = airObstacleHits[index];
                if (hit.collider == null
                    || hit.collider == controller
                    || hit.collider is not BoxCollider
                    || hit.collider.bounds.max.y
                        <= bottom.y + controller.skinWidth + GroundSupportTolerance
                    || hit.collider.bounds.max.y + controller.skinWidth
                        <= ballisticApex + 0.005f
                    || hit.distance >= closestDistance)
                {
                    continue;
                }

                closestDistance = hit.distance;
                blockingNormal = hit.normal;
            }

            if (blockingNormal.sqrMagnitude < 0.5f)
            {
                return planarStep;
            }

            var constrained = Vector3.ProjectOnPlane(planarStep, blockingNormal);
            constrained.y = 0f;
            return constrained;
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

        private static float ConfiguredStepOffset(bool crouched)
        {
            return crouched
                ? PlayerMovementTuning.CrouchStepOffset
                : PlayerMovementTuning.StandingStepOffset;
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
            var bottom = center - up * halfHeight;
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
                var maximumContactRise = radius
                    * (1f - Mathf.Clamp01(Vector3.Dot(hit.normal, up)))
                    + 0.005f;
                if (hit.collider == null || hit.collider == controller
                    || Vector3.Angle(hit.normal, up) > controller.slopeLimit + 0.5f
                    || Vector3.Dot(hit.point - bottom, up) > maximumContactRise
                    || hit.distance >= closest)
                {
                    continue;
                }

                closest = hit.distance;
                bestHit = hit;
            }

            return closest < float.PositiveInfinity;
        }

        private bool TryRecoverBoxTopSupport(bool hadRecentGroundSupport)
        {
            var transform = controller.transform;
            var up = transform.up;
            var halfHeight = Mathf.Max(controller.height * 0.5f, controller.radius);
            var center = transform.TransformPoint(controller.center);
            var bottom = center - up * halfHeight;
            var overlapCenter = bottom
                + up * (PlayerMovementTuning.GroundProbeDistance * 0.5f);
            var overlapRadius = controller.radius
                + controller.skinWidth
                + GroundSupportTolerance;
            var count = Physics.OverlapSphereNonAlloc(
                overlapCenter,
                overlapRadius,
                topSupportOverlaps,
                Physics.AllLayers,
                QueryTriggerInteraction.Ignore);
            var bestTop = float.NegativeInfinity;

            for (var index = 0; index < count; index++)
            {
                var hit = topSupportOverlaps[index];
                topSupportOverlaps[index] = null;
                if (hit == null
                    || hit == controller
                    || hit is not BoxCollider box
                    // Normal step handling is more stable for low ledges and
                    // should remain solely responsible for them.
                    || box.bounds.size.y
                        <= ConfiguredStepOffset(controller.height
                            <= PlayerMovementTuning.CrouchHeight + 0.001f)
                            + GroundSupportTolerance
                    || Vector3.Angle(box.transform.up, up)
                        > controller.slopeLimit + 0.5f)
                {
                    continue;
                }

                var top = box.bounds.max.y;
                var rise = top - bottom.y;
                if (rise > PlayerMovementTuning.GroundProbeDistance
                        + GroundSupportTolerance
                    || rise < -PlayerMovementTuning.GroundProbeDistance
                    // A recently grounded capsule may sink slightly beside a
                    // corner. An airborne jump may only recover a top face it has
                    // actually reached, otherwise higher props become climbable.
                    || (!hadRecentGroundSupport && rise > 0.005f))
                {
                    continue;
                }

                var topProbe = bottom + up * rise;
                var closestPoint = box.ClosestPoint(topProbe);
                var horizontalSeparation = Vector3.ProjectOnPlane(
                    topProbe - closestPoint,
                    up).magnitude;
                if (horizontalSeparation
                    > controller.radius + controller.skinWidth + GroundSupportTolerance)
                {
                    continue;
                }

                bestTop = Mathf.Max(bestTop, top);
            }

            if (bestTop == float.NegativeInfinity)
            {
                return false;
            }

            var targetBottom = bestTop + controller.skinWidth;
            var correction = targetBottom - bottom.y;
            if (Mathf.Abs(correction)
                > PlayerMovementTuning.GroundProbeDistance
                    + controller.skinWidth
                    + GroundSupportTolerance)
            {
                return false;
            }

            var wasEnabled = controller.enabled;
            if (wasEnabled)
            {
                controller.enabled = false;
            }

            transform.position += up * correction;

            if (wasEnabled)
            {
                controller.enabled = true;
            }

            return true;
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
        public const float VerticalImpulseHalfLife = 0.11f;
        public const float MinimumTakeoffOffset = 0.012f;
        public const float MaximumTakeoffOffset = 0.018f;
        public const float MinimumTakeoffPitch = 0.1f;
        public const float MaximumTakeoffPitch = 0.15f;
        public const float MaximumLandingOffset = 0.03f;
        public const float MaximumLandingPitch = 0.4f;
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

        public static PlayerCameraPose CalculateTakeoffImpulse(float planarSpeed)
        {
            var speedWeight = Mathf.SmoothStep(
                0f,
                1f,
                Mathf.InverseLerp(0f, PlayerMovementTuning.SprintSpeed, planarSpeed));
            return new PlayerCameraPose(
                new Vector3(
                    0f,
                    -Mathf.Lerp(MinimumTakeoffOffset, MaximumTakeoffOffset, speedWeight),
                    0f),
                new Vector3(
                    -Mathf.Lerp(MinimumTakeoffPitch, MaximumTakeoffPitch, speedWeight),
                    0f,
                    0f));
        }

        public static PlayerCameraPose CalculateLandingImpulse(float downwardSpeed)
        {
            var landingWeight = Mathf.InverseLerp(2f, 10f, downwardSpeed);
            return new PlayerCameraPose(
                new Vector3(0f, -MaximumLandingOffset * landingWeight, 0f),
                new Vector3(MaximumLandingPitch * landingWeight, 0f, 0f));
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
