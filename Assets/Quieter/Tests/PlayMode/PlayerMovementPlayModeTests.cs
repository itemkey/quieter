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
                var input = new PlayerInputFrame { Yaw = 0f };
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
            controller.height = 1.8f;
            controller.radius = 0.38f;
            controller.center = new Vector3(0f, 0.9f, 0f);
            controller.stepOffset = 0.35f;
            controller.slopeLimit = 55f;
            controller.skinWidth = 0.08f;
            controller.minMoveDistance = 0f;
            return player;
        }
    }
}
