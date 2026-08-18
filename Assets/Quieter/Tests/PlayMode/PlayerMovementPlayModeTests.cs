using System.Collections;
using NUnit.Framework;
using Quieter.Player;
using UnityEngine;
using UnityEngine.TestTools;

namespace Quieter.Tests
{
    public sealed class PlayerMovementPlayModeTests
    {
        [UnityTest]
        public IEnumerator CharacterController_RemainsGroundedAcrossColliderSeam()
        {
            var left = CreateFloor("LeftFloor", new Vector3(-5f, -0.5f, 0f), new Vector3(10f, 1f, 8f));
            var right = CreateFloor("RightFloor", new Vector3(5f, -0.5f, 0f), new Vector3(10f, 1f, 8f));
            var player = CreatePlayer(new Vector3(-2f, 0.02f, 0f));
            try
            {
                Physics.SyncTransforms();
                yield return null;

                var controller = player.GetComponent<CharacterController>();
                var motor = new PlayerMovementMotor(controller);
                var state = new PlayerNetworkState { Position = player.transform.position };
                var input = new PlayerInputFrame { Movement = Vector2.right, Yaw = 0f };
                var airborneTicks = 0;
                for (var tick = 0; tick < 90; tick++)
                {
                    input.Sequence++;
                    motor.Simulate(ref state, input, 1f / 60f);
                    if (!state.Grounded)
                    {
                        airborneTicks++;
                    }
                }

                Assert.That(player.transform.position.x, Is.GreaterThan(3f));
                Assert.That(airborneTicks, Is.LessThanOrEqualTo(1));
            }
            finally
            {
                Object.Destroy(player);
                Object.Destroy(left);
                Object.Destroy(right);
            }
        }

        [UnityTest]
        public IEnumerator CharacterController_ClimbsConfiguredStepWithoutGroundFlicker()
        {
            var floor = CreateFloor("Floor", new Vector3(0f, -0.5f, 0f), new Vector3(12f, 1f, 8f));
            var step = CreateFloor("Step", new Vector3(1.5f, 0.1f, 0f), new Vector3(2f, 0.2f, 4f));
            var player = CreatePlayer(new Vector3(-2f, 0.02f, 0f));
            try
            {
                Physics.SyncTransforms();
                yield return null;

                var motor = new PlayerMovementMotor(player.GetComponent<CharacterController>());
                var state = new PlayerNetworkState { Position = player.transform.position };
                var input = new PlayerInputFrame { Movement = Vector2.right, Yaw = 0f };
                var maximumHeight = player.transform.position.y;
                var airborneTicks = 0;
                for (var tick = 0; tick < 75; tick++)
                {
                    input.Sequence++;
                    motor.Simulate(ref state, input, 1f / 60f);
                    maximumHeight = Mathf.Max(maximumHeight, player.transform.position.y);
                    if (!state.Grounded)
                    {
                        airborneTicks++;
                    }
                }

                Assert.That(player.transform.position.x, Is.GreaterThan(2f));
                Assert.That(state.Grounded, Is.True);
                Assert.That(maximumHeight, Is.GreaterThan(0.15f));
                Assert.That(airborneTicks, Is.LessThanOrEqualTo(1));
            }
            finally
            {
                Object.Destroy(player);
                Object.Destroy(step);
                Object.Destroy(floor);
            }
        }

        [UnityTest]
        public IEnumerator CharacterController_JumpsToConfiguredHeightExactlyOncePerPress()
        {
            var floor = CreateFloor("JumpFloor", new Vector3(0f, -0.5f, 0f), new Vector3(8f, 1f, 8f));
            var player = CreatePlayer(new Vector3(0f, 0.02f, 0f));
            try
            {
                Physics.SyncTransforms();
                yield return null;

                var motor = new PlayerMovementMotor(player.GetComponent<CharacterController>());
                var state = new PlayerNetworkState { Position = player.transform.position };
                var input = new PlayerInputFrame { Yaw = 0f, JumpHeld = true };
                for (var tick = 0; tick < 5; tick++)
                {
                    input.Sequence++;
                    motor.Simulate(ref state, input, 1f / 60f);
                }

                Assert.That(state.Grounded, Is.True);
                var groundedHeight = player.transform.position.y;
                var maximumHeight = groundedHeight;
                var takeoffs = 0;
                var wasGrounded = state.Grounded;
                input.JumpPressId = 1;
                for (var tick = 0; tick < 120; tick++)
                {
                    input.Sequence++;
                    motor.Simulate(ref state, input, 1f / 60f);
                    maximumHeight = Mathf.Max(maximumHeight, player.transform.position.y);
                    if (wasGrounded && !state.Grounded)
                    {
                        takeoffs++;
                    }

                    wasGrounded = state.Grounded;
                }

                Assert.That(takeoffs, Is.EqualTo(1));
                Assert.That(
                    maximumHeight - groundedHeight,
                    Is.EqualTo(PlayerMovementTuning.JumpHeight).Within(0.12f));
                Assert.That(state.Grounded, Is.True);
            }
            finally
            {
                Object.Destroy(player);
                Object.Destroy(floor);
            }
        }

        [UnityTest]
        public IEnumerator CharacterController_ReleasedJumpIsShorterThanHeldJump()
        {
            var floor = CreateFloor("ShortJumpFloor", new Vector3(0f, -0.5f, 0f), new Vector3(8f, 1f, 8f));
            var player = CreatePlayer(new Vector3(0f, 0.02f, 0f));
            try
            {
                Physics.SyncTransforms();
                yield return null;

                var motor = new PlayerMovementMotor(player.GetComponent<CharacterController>());
                var state = new PlayerNetworkState { Position = player.transform.position };
                var input = new PlayerInputFrame { Yaw = 0f };
                for (var tick = 0; tick < 5; tick++)
                {
                    input.Sequence++;
                    motor.Simulate(ref state, input, 1f / 60f);
                }

                var groundedHeight = player.transform.position.y;
                var maximumHeight = groundedHeight;
                input.JumpPressId = 1;
                input.JumpHeld = true;
                for (var tick = 0; tick < 120; tick++)
                {
                    input.Sequence++;
                    if (tick >= 3)
                    {
                        input.JumpHeld = false;
                    }

                    motor.Simulate(ref state, input, 1f / 60f);
                    maximumHeight = Mathf.Max(maximumHeight, player.transform.position.y);
                }

                Assert.That(maximumHeight - groundedHeight, Is.GreaterThan(0.25f));
                Assert.That(
                    maximumHeight - groundedHeight,
                    Is.LessThan(PlayerMovementTuning.JumpHeight * 0.75f));
                Assert.That(state.Grounded, Is.True);
            }
            finally
            {
                Object.Destroy(player);
                Object.Destroy(floor);
            }
        }

        [UnityTest]
        public IEnumerator CharacterController_CrouchesWithoutMovingFeetAndSuppressesSprint()
        {
            var floor = CreateFloor("CrouchFloor", new Vector3(0f, -0.5f, 0f), new Vector3(12f, 1f, 12f));
            var player = CreatePlayer(new Vector3(0f, 0.02f, 0f));
            try
            {
                Physics.SyncTransforms();
                yield return null;

                var controller = player.GetComponent<CharacterController>();
                var motor = new PlayerMovementMotor(controller);
                var state = new PlayerNetworkState { Position = player.transform.position };
                var input = new PlayerInputFrame { Yaw = 0f };
                SimulateTicks(motor, ref state, ref input, 5);
                var standingFootHeight = player.transform.position.y;

                input.CrouchHeld = true;
                input.Sprint = true;
                SimulateTicks(motor, ref state, ref input, 1);

                Assert.That(state.Crouched, Is.True);
                Assert.That(
                    player.transform.position.y,
                    Is.EqualTo(standingFootHeight).Within(0.01f));

                input.Movement = Vector2.up;
                SimulateTicks(motor, ref state, ref input, 60);

                Assert.That(controller.height, Is.EqualTo(PlayerMovementTuning.CrouchHeight).Within(0.0001f));
                Assert.That(controller.center.y, Is.EqualTo(PlayerMovementTuning.CrouchCenterY).Within(0.0001f));
                Assert.That(controller.stepOffset, Is.EqualTo(PlayerMovementTuning.CrouchStepOffset).Within(0.0001f));
                Assert.That(new Vector2(state.Velocity.x, state.Velocity.z).magnitude,
                    Is.EqualTo(PlayerMovementTuning.CrouchSpeed).Within(0.02f));

                input.CrouchHeld = false;
                input.Sprint = false;
                input.Movement = Vector2.zero;
                var crouchedFootHeight = player.transform.position.y;
                SimulateTicks(motor, ref state, ref input, 1);

                Assert.That(state.Crouched, Is.False);
                Assert.That(controller.height, Is.EqualTo(PlayerMovementTuning.StandingHeight).Within(0.0001f));
                Assert.That(
                    player.transform.position.y,
                    Is.EqualTo(crouchedFootHeight).Within(0.01f));
            }
            finally
            {
                Object.Destroy(player);
                Object.Destroy(floor);
            }
        }

        [UnityTest]
        public IEnumerator CharacterController_CrouchJumpStandsAndJumpsWhenClear()
        {
            var floor = CreateFloor("CrouchJumpFloor", new Vector3(0f, -0.5f, 0f), new Vector3(8f, 1f, 8f));
            var player = CreatePlayer(new Vector3(0f, 0.02f, 0f));
            try
            {
                Physics.SyncTransforms();
                yield return null;

                var motor = new PlayerMovementMotor(player.GetComponent<CharacterController>());
                var state = new PlayerNetworkState { Position = player.transform.position };
                var input = new PlayerInputFrame { Yaw = 0f };
                SimulateTicks(motor, ref state, ref input, 5);
                var groundedHeight = player.transform.position.y;

                input.CrouchHeld = true;
                SimulateTicks(motor, ref state, ref input, 1);
                Assert.That(state.Crouched, Is.True);

                input.JumpPressId = 1;
                input.JumpHeld = true;
                SimulateTicks(motor, ref state, ref input, 1);
                Assert.That(state.Crouched, Is.False);
                Assert.That(state.Grounded, Is.False);

                input.CrouchHeld = false;
                var maximumHeight = player.transform.position.y;
                for (var tick = 0; tick < 120; tick++)
                {
                    input.Sequence++;
                    motor.Simulate(ref state, input, 1f / 60f);
                    maximumHeight = Mathf.Max(maximumHeight, player.transform.position.y);
                }

                Assert.That(
                    maximumHeight - groundedHeight,
                    Is.EqualTo(PlayerMovementTuning.JumpHeight).Within(0.12f));
                Assert.That(state.Grounded, Is.True);
            }
            finally
            {
                Object.Destroy(player);
                Object.Destroy(floor);
            }
        }

        [UnityTest]
        public IEnumerator CharacterController_CrouchCoyoteJumpStandsBeforeTakeoff()
        {
            var floor = CreateFloor("CrouchCoyoteFloor", new Vector3(0f, -0.5f, 0f), new Vector3(8f, 1f, 8f));
            var player = CreatePlayer(new Vector3(0f, 0.02f, 0f));
            try
            {
                Physics.SyncTransforms();
                yield return null;

                var motor = new PlayerMovementMotor(player.GetComponent<CharacterController>());
                var state = new PlayerNetworkState { Position = player.transform.position };
                var input = new PlayerInputFrame { Yaw = 0f, CrouchHeld = true };
                SimulateTicks(motor, ref state, ref input, 5);
                Assert.That(state.Crouched, Is.True);

                state.Position += Vector3.up * 0.35f;
                state.Grounded = false;
                state.Velocity = Vector3.zero;
                motor.Warp(state);
                Physics.SyncTransforms();

                input.JumpPressId = 1;
                input.JumpHeld = true;
                SimulateTicks(motor, ref state, ref input, 1);

                Assert.That(state.Crouched, Is.False);
                Assert.That(state.Grounded, Is.False);
                Assert.That(state.Velocity.y, Is.GreaterThan(0f));
            }
            finally
            {
                Object.Destroy(player);
                Object.Destroy(floor);
            }
        }

        [UnityTest]
        public IEnumerator CharacterController_BlockedStandConsumesJumpWithoutDelayedTakeoff()
        {
            var floor = CreateFloor("LowCeilingFloor", new Vector3(0f, -0.5f, 0f), new Vector3(8f, 1f, 8f));
            var ceiling = CreateFloor("LowCeiling", new Vector3(0f, 1.4f, 0f), new Vector3(3f, 0.2f, 3f));
            var player = CreatePlayer(new Vector3(0f, 0.02f, 0f));
            try
            {
                Physics.SyncTransforms();
                yield return null;

                var controller = player.GetComponent<CharacterController>();
                var motor = new PlayerMovementMotor(controller);
                var state = new PlayerNetworkState { Position = player.transform.position };
                var input = new PlayerInputFrame { Yaw = 0f, CrouchHeld = true };
                SimulateTicks(motor, ref state, ref input, 5);
                Assert.That(state.Crouched, Is.True);
                Assert.That(motor.CanStand(), Is.False);

                input.CrouchHeld = false;
                input.JumpPressId = 1;
                input.JumpHeld = true;
                SimulateTicks(motor, ref state, ref input, 5);

                Assert.That(state.Crouched, Is.True);
                Assert.That(state.Grounded, Is.True);
                Assert.That(state.LastConsumedJumpPressId, Is.EqualTo(1));

                ceiling.transform.position = new Vector3(0f, 4f, 0f);
                Physics.SyncTransforms();
                input.JumpHeld = false;
                SimulateTicks(motor, ref state, ref input, 10);

                Assert.That(state.Crouched, Is.False);
                Assert.That(state.Grounded, Is.True);
                Assert.That(controller.height, Is.EqualTo(PlayerMovementTuning.StandingHeight).Within(0.0001f));

                input.JumpPressId = 2;
                input.JumpHeld = true;
                SimulateTicks(motor, ref state, ref input, 1);
                Assert.That(state.Grounded, Is.False);
            }
            finally
            {
                Object.Destroy(player);
                Object.Destroy(ceiling);
                Object.Destroy(floor);
            }
        }

        private static GameObject CreateFloor(string name, Vector3 position, Vector3 scale)
        {
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.name = name;
            floor.transform.SetPositionAndRotation(position, Quaternion.identity);
            floor.transform.localScale = scale;
            return floor;
        }

        private static GameObject CreatePlayer(Vector3 position)
        {
            var player = new GameObject("MovementTestPlayer");
            player.transform.position = position;
            var controller = player.AddComponent<CharacterController>();
            controller.height = PlayerMovementTuning.StandingHeight;
            controller.radius = 0.38f;
            controller.center = new Vector3(0f, PlayerMovementTuning.StandingCenterY, 0f);
            controller.stepOffset = PlayerMovementTuning.StandingStepOffset;
            controller.slopeLimit = 55f;
            controller.skinWidth = 0.08f;
            controller.minMoveDistance = 0f;
            return player;
        }

        private static void SimulateTicks(
            PlayerMovementMotor motor,
            ref PlayerNetworkState state,
            ref PlayerInputFrame input,
            int count)
        {
            for (var tick = 0; tick < count; tick++)
            {
                input.Sequence++;
                motor.Simulate(ref state, input, 1f / 60f);
            }
        }
    }
}
