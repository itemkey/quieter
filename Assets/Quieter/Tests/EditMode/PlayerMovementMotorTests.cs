using NUnit.Framework;
using Quieter.Player;
using Quieter.Survival;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace Quieter.Tests
{
    public sealed class PlayerMovementMotorTests
    {
        private const float Step = 1f / 60f;

        [Test]
        public void DiagonalMovement_DoesNotExceedWalkSpeed()
        {
            var velocity = Vector2.zero;
            for (var tick = 0; tick < 60; tick++)
            {
                velocity = PlayerMovementMotor.AcceleratePlanar(
                    velocity,
                    new Vector2(1f, 1f),
                    sprint: false,
                    grounded: true,
                    Step);
            }

            Assert.That(velocity.magnitude, Is.EqualTo(PlayerMovementTuning.WalkSpeed).Within(0.001f));
            Assert.That(velocity.x, Is.EqualTo(velocity.y).Within(0.001f));
        }

        [Test]
        public void GroundAccelerationAndBraking_AreShortAndControlled()
        {
            var velocity = Vector2.zero;
            for (var tick = 0; tick < 5; tick++)
            {
                velocity = PlayerMovementMotor.AcceleratePlanar(
                    velocity,
                    Vector2.up,
                    sprint: false,
                    grounded: true,
                    Step);
            }

            Assert.That(velocity.y, Is.EqualTo(PlayerMovementTuning.WalkSpeed).Within(0.001f));

            velocity = new Vector2(0f, PlayerMovementTuning.SprintSpeed);
            for (var tick = 0; tick < 6; tick++)
            {
                velocity = PlayerMovementMotor.AcceleratePlanar(
                    velocity,
                    Vector2.zero,
                    sprint: false,
                    grounded: true,
                    Step);
            }

            Assert.That(velocity.magnitude, Is.LessThan(0.001f));
        }

        [Test]
        public void CrouchMovement_CapsSpeedAndSuppressesSprint()
        {
            var velocity = Vector2.zero;
            for (var tick = 0; tick < 60; tick++)
            {
                velocity = PlayerMovementMotor.AcceleratePlanar(
                    velocity,
                    Vector2.up,
                    sprint: true,
                    grounded: true,
                    Step,
                    crouched: true);
            }

            Assert.That(
                velocity.magnitude,
                Is.EqualTo(PlayerMovementTuning.CrouchSpeed).Within(0.001f));
        }

        [Test]
        public void AirControl_CorrectsMomentumWithoutInstantlyReversingIt()
        {
            var velocity = new Vector2(0f, PlayerMovementTuning.WalkSpeed);
            var corrected = PlayerMovementMotor.AcceleratePlanar(
                velocity,
                Vector2.down,
                sprint: false,
                grounded: false,
                Step);

            Assert.That(corrected.y, Is.LessThan(velocity.y));
            Assert.That(corrected.y, Is.GreaterThan(0f));
        }

        [Test]
        public void AirControl_PreservesMostSprintMomentumDuringHalfSecondReversal()
        {
            var velocity = new Vector2(0f, PlayerMovementTuning.SprintSpeed);
            for (var tick = 0; tick < 30; tick++)
            {
                velocity = PlayerMovementMotor.AcceleratePlanar(
                    velocity,
                    Vector2.down,
                    sprint: true,
                    grounded: false,
                    Step);
            }

            Assert.That(velocity.y, Is.GreaterThan(PlayerMovementTuning.WalkSpeed - 0.01f));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void AirControl_SoftlyLimitsExcessSprintMomentum(bool keepMoving)
        {
            var velocity = Vector2.up * PlayerMovementTuning.SprintSpeed;
            var movement = keepMoving ? Vector2.up : Vector2.zero;
            var ticksToCap = Mathf.CeilToInt(
                (PlayerMovementTuning.SprintSpeed - PlayerMovementTuning.AirborneSpeedCap)
                / (PlayerMovementTuning.AirAcceleration * Step));

            var firstTick = PlayerMovementMotor.AcceleratePlanar(
                velocity,
                movement,
                sprint: true,
                grounded: false,
                Step);
            Assert.That(firstTick.magnitude, Is.LessThan(velocity.magnitude));
            Assert.That(firstTick.magnitude, Is.GreaterThan(PlayerMovementTuning.AirborneSpeedCap));

            velocity = firstTick;
            for (var tick = 1; tick < ticksToCap; tick++)
            {
                velocity = PlayerMovementMotor.AcceleratePlanar(
                    velocity,
                    movement,
                    sprint: true,
                    grounded: false,
                    Step);
            }

            Assert.That(
                velocity.magnitude,
                Is.EqualTo(PlayerMovementTuning.AirborneSpeedCap).Within(0.001f));
        }

        [Test]
        public void AirControl_PreservesMomentumBelowAirborneCapWithoutInput()
        {
            var initial = new Vector2(2f, PlayerMovementTuning.WalkSpeed);
            var result = PlayerMovementMotor.AcceleratePlanar(
                initial,
                Vector2.zero,
                sprint: true,
                grounded: false,
                Step);

            Assert.That(result, Is.EqualTo(initial));
        }

        [Test]
        public void GroundTurning_ChangesDirectionFasterThanOrdinaryAcceleration()
        {
            var forward = new Vector2(0f, PlayerMovementTuning.WalkSpeed);
            var reversed = PlayerMovementMotor.AcceleratePlanar(
                forward,
                Vector2.down,
                sprint: false,
                grounded: true,
                Step);

            var ordinaryAccelerationPerTick = PlayerMovementTuning.GroundAcceleration * Step;
            Assert.That(
                forward.y - reversed.y,
                Is.GreaterThan(ordinaryAccelerationPerTick));
            Assert.That(reversed.y, Is.GreaterThan(0f));
        }

        [Test]
        public void GroundProjection_PreservesSurfaceSpeedAndFollowsSlope()
        {
            var planar = new Vector3(0f, 0f, PlayerMovementTuning.WalkSpeed);
            var slopeNormal = new Vector3(0f, 1f, -1f).normalized;
            var projected = PlayerMovementMotor.ProjectPlanarOnGround(planar, slopeNormal);

            Assert.That(projected.magnitude, Is.EqualTo(planar.magnitude).Within(0.0001f));
            Assert.That(Vector3.Dot(projected, slopeNormal), Is.EqualTo(0f).Within(0.0001f));
            Assert.That(projected.y, Is.GreaterThan(0f));
        }

        [Test]
        public void CameraMotion_UsesDistanceAndDistinctGaitProfiles()
        {
            var phase = PlayerCameraMotion.AdvancePhase(
                0f,
                PlayerCameraMotion.WalkStrideLength * 0.25f,
                PlayerGait.Walk);
            Assert.That(phase, Is.EqualTo(Mathf.PI * 0.5f).Within(0.0001f));

            var crouch = PlayerCameraMotion.CalculateTarget(
                phase,
                PlayerGait.Crouch,
                movementWeight: 1f,
                enabled: true);
            var walk = PlayerCameraMotion.CalculateTarget(
                phase,
                PlayerGait.Walk,
                movementWeight: 1f,
                enabled: true);
            var sprint = PlayerCameraMotion.CalculateTarget(
                phase,
                PlayerGait.Sprint,
                movementWeight: 1f,
                enabled: true);

            Assert.That(Mathf.Abs(crouch.PositionOffset.x), Is.LessThan(Mathf.Abs(walk.PositionOffset.x)));
            Assert.That(Mathf.Abs(walk.PositionOffset.x), Is.LessThan(Mathf.Abs(sprint.PositionOffset.x)));
            Assert.That(Mathf.Abs(crouch.RotationOffset.z), Is.LessThan(Mathf.Abs(walk.RotationOffset.z)));
            Assert.That(Mathf.Abs(walk.RotationOffset.z), Is.LessThan(Mathf.Abs(sprint.RotationOffset.z)));

            var pose = sprint;
            for (var frame = 0; frame < 60; frame++)
            {
                pose = PlayerCameraMotion.Damp(
                    pose,
                    PlayerCameraPose.Neutral,
                    Step);
            }

            Assert.That(pose.PositionOffset.magnitude, Is.LessThan(0.00001f));
            Assert.That(pose.RotationOffset.magnitude, Is.LessThan(0.00001f));
        }

        [Test]
        public void CameraMotion_IsDistanceInvariantAndNeutralWhenInactive()
        {
            var singleStep = PlayerCameraMotion.AdvancePhase(
                0f,
                1.2f,
                PlayerGait.Walk);
            var splitSteps = 0f;
            for (var index = 0; index < 12; index++)
            {
                splitSteps = PlayerCameraMotion.AdvancePhase(
                    splitSteps,
                    0.1f,
                    PlayerGait.Walk);
            }

            Assert.That(splitSteps, Is.EqualTo(singleStep).Within(0.0001f));
            Assert.That(
                PlayerCameraMotion.AdvancePhase(1.25f, 0f, PlayerGait.Walk),
                Is.EqualTo(1.25f));

            var idle = PlayerCameraMotion.CalculateTarget(
                1.25f,
                PlayerGait.Idle,
                movementWeight: 1f,
                enabled: true);
            var airborne = PlayerCameraMotion.CalculateTarget(
                1.25f,
                PlayerGait.Airborne,
                movementWeight: 1f,
                enabled: true);
            Assert.That(idle.PositionOffset, Is.EqualTo(Vector3.zero));
            Assert.That(airborne.PositionOffset, Is.EqualTo(Vector3.zero));
        }

        [Test]
        public void CameraJumpImpulses_ScaleWithTakeoffAndLandingSpeed()
        {
            var standing = PlayerCameraMotion.CalculateTakeoffImpulse(0f);
            var sprinting = PlayerCameraMotion.CalculateTakeoffImpulse(
                PlayerMovementTuning.SprintSpeed);
            var softLanding = PlayerCameraMotion.CalculateLandingImpulse(2f);
            var hardLanding = PlayerCameraMotion.CalculateLandingImpulse(10f);

            Assert.That(
                standing.PositionOffset.y,
                Is.EqualTo(-PlayerCameraMotion.MinimumTakeoffOffset).Within(0.0001f));
            Assert.That(
                sprinting.PositionOffset.y,
                Is.EqualTo(-PlayerCameraMotion.MaximumTakeoffOffset).Within(0.0001f));
            Assert.That(
                sprinting.RotationOffset.x,
                Is.EqualTo(-PlayerCameraMotion.MaximumTakeoffPitch).Within(0.0001f));
            Assert.That(softLanding, Is.EqualTo(PlayerCameraPose.Neutral));
            Assert.That(
                hardLanding.PositionOffset.y,
                Is.EqualTo(-PlayerCameraMotion.MaximumLandingOffset).Within(0.0001f));
            Assert.That(
                hardLanding.RotationOffset.x,
                Is.EqualTo(PlayerCameraMotion.MaximumLandingPitch).Within(0.0001f));
        }

        [Test]
        public void NetworkMovementTypes_RoundTripCrouchState()
        {
            var input = new PlayerInputFrame
            {
                Sequence = 42,
                Movement = new Vector2(0.25f, -0.75f),
                Yaw = 123f,
                JumpPressId = 7,
                Sprint = true,
                CrouchHeld = true,
            };
            var state = new PlayerNetworkState
            {
                ServerTick = 11,
                LastProcessedSequence = 42,
                Position = new Vector3(1f, 2f, 3f),
                Velocity = new Vector3(4f, 5f, 6f),
                Yaw = 123f,
                Grounded = true,
                Crouched = true,
            };

            using var writer = new FastBufferWriter(256, Allocator.Temp);
            writer.WriteNetworkSerializable(input);
            writer.WriteNetworkSerializable(state);
            using var reader = new FastBufferReader(writer, Allocator.Temp);
            reader.ReadNetworkSerializable(out PlayerInputFrame inputCopy);
            reader.ReadNetworkSerializable(out PlayerNetworkState stateCopy);

            Assert.That(inputCopy.CrouchHeld, Is.True);
            Assert.That(inputCopy.Sequence, Is.EqualTo(input.Sequence));
            Assert.That(stateCopy.Crouched, Is.True);
            Assert.That(stateCopy.Equals(state), Is.True);
        }

        [Test]
        public void JumpPress_IsConsumedExactlyOnce()
        {
            var state = new PlayerNetworkState
            {
                Grounded = true,
                CoyoteTicks = PlayerMovementTuning.CoyoteTicks,
            };

            Assert.That(PlayerMovementMotor.RegisterJump(ref state, 1, grounded: true), Is.True);
            Assert.That(PlayerMovementMotor.RegisterJump(ref state, 1, grounded: true), Is.False);
            Assert.That(state.LastConsumedJumpPressId, Is.EqualTo(1));
        }

        [Test]
        public void JumpBuffer_FiresWhenGroundArrivesWithinWindow()
        {
            var state = new PlayerNetworkState();
            Assert.That(PlayerMovementMotor.RegisterJump(ref state, 7, grounded: false), Is.False);
            for (var tick = 0; tick < 5; tick++)
            {
                Assert.That(PlayerMovementMotor.RegisterJump(ref state, 7, grounded: false), Is.False);
            }

            Assert.That(PlayerMovementMotor.RegisterJump(ref state, 7, grounded: true), Is.True);
        }

        [Test]
        public void CoyoteTime_AllowsJumpImmediatelyAfterLeavingGround()
        {
            var state = new PlayerNetworkState
            {
                Grounded = false,
                CoyoteTicks = PlayerMovementTuning.CoyoteTicks,
            };

            Assert.That(PlayerMovementMotor.RegisterJump(ref state, 3, grounded: false), Is.True);
        }

        [Test]
        [TestCase(0f, PlayerMovementTuning.StandingJumpHeight)]
        [TestCase(PlayerMovementTuning.WalkSpeed, PlayerMovementTuning.WalkJumpHeight)]
        [TestCase(PlayerMovementTuning.SprintSpeed, PlayerMovementTuning.SprintJumpHeight)]
        public void ConfiguredJumpSpeed_ProducesRequestedApex(
            float planarSpeed,
            float expectedHeight)
        {
            var jumpSpeed = PlayerMovementTuning.CalculateJumpSpeed(planarSpeed);
            var apex = jumpSpeed * jumpSpeed
                / (2f * PlayerMovementTuning.Gravity);
            Assert.That(apex, Is.EqualTo(expectedHeight).Within(0.0001f));
        }

        [Test]
        public void ConfiguredJumpProfile_HasDenseGameAirtimes()
        {
            var standingAirtime = 2f * PlayerMovementTuning.CalculateJumpSpeed(0f)
                / PlayerMovementTuning.Gravity;
            var walkingAirtime = 2f * PlayerMovementTuning.CalculateJumpSpeed(
                    PlayerMovementTuning.WalkSpeed)
                / PlayerMovementTuning.Gravity;
            var sprintingAirtime = 2f * PlayerMovementTuning.CalculateJumpSpeed(
                    PlayerMovementTuning.SprintSpeed)
                / PlayerMovementTuning.Gravity;

            Assert.That(standingAirtime, Is.EqualTo(0.542f).Within(0.002f));
            Assert.That(walkingAirtime, Is.EqualTo(0.551f).Within(0.002f));
            Assert.That(sprintingAirtime, Is.EqualTo(0.560f).Within(0.002f));
        }

        [Test]
        public void JumpHeight_ChangesContinuouslyWithActualPlanarSpeed()
        {
            var previous = PlayerMovementTuning.CalculateJumpHeight(0f);
            for (var speed = 0.1f; speed <= PlayerMovementTuning.SprintSpeed; speed += 0.1f)
            {
                var current = PlayerMovementTuning.CalculateJumpHeight(speed);
                Assert.That(current, Is.GreaterThanOrEqualTo(previous));
                Assert.That(current - previous, Is.LessThan(0.02f));
                previous = current;
            }

            Assert.That(
                PlayerMovementTuning.CalculateJumpHeight(100f),
                Is.EqualTo(PlayerMovementTuning.SprintJumpHeight));
        }

        [Test]
        public void InputBatch_PreservesRedundantFramesInSequenceOrder()
        {
            var batch = new PlayerInputBatch();
            for (uint sequence = 10; sequence < 10 + PlayerInputBatch.Capacity; sequence++)
            {
                batch.Add(new PlayerInputFrame
                {
                    Sequence = sequence,
                    JumpPressId = sequence / 3,
                    CrouchHeld = sequence % 2 == 0,
                });
            }

            Assert.That(batch.Count, Is.EqualTo(PlayerInputBatch.Capacity));
            for (var index = 0; index < batch.Count; index++)
            {
                Assert.That(batch[index].Sequence, Is.EqualTo((uint)(10 + index)));
                Assert.That(batch[index].CrouchHeld, Is.EqualTo((10 + index) % 2 == 0));
            }
        }

        [Test]
        public void ConditionCameraEffects_AreAbsentWithoutSymptoms()
        {
            var pose = ConditionCameraEffects.CalculateShake(SymptomFlags.None, 12.5f);

            Assert.That(pose.Position, Is.EqualTo(Vector3.zero));
            Assert.That(pose.Rotation, Is.EqualTo(Vector3.zero));
            Assert.That(
                ConditionCameraEffects.CalculateFieldOfView(60f, SymptomFlags.None),
                Is.EqualTo(60f));
        }

        [Test]
        public void ConditionCameraEffects_CommunicateDangerWithoutExtremeMotion()
        {
            var symptoms = SymptomFlags.Shivering
                | SymptomFlags.SeverePain
                | SymptomFlags.Dizzy
                | SymptomFlags.TunnelVision;
            var pose = ConditionCameraEffects.CalculateShake(symptoms, 7.25f);

            Assert.That(pose.Position.magnitude, Is.GreaterThan(0f).And.LessThan(0.03f));
            Assert.That(pose.Rotation.magnitude, Is.GreaterThan(0f).And.LessThan(1.5f));
            Assert.That(
                ConditionCameraEffects.CalculateFieldOfView(60f, symptoms),
                Is.EqualTo(48f));
        }
    }
}
