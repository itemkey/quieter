using NUnit.Framework;
using Quieter.Player;
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
        public void ConfiguredJumpSpeed_ProducesRequestedApex()
        {
            var apex = PlayerMovementTuning.JumpSpeed * PlayerMovementTuning.JumpSpeed
                / (2f * PlayerMovementTuning.Gravity);
            Assert.That(apex, Is.EqualTo(PlayerMovementTuning.JumpHeight).Within(0.0001f));
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
                });
            }

            Assert.That(batch.Count, Is.EqualTo(PlayerInputBatch.Capacity));
            for (var index = 0; index < batch.Count; index++)
            {
                Assert.That(batch[index].Sequence, Is.EqualTo((uint)(10 + index)));
            }
        }
    }
}
