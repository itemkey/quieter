using System.Collections;
using System.Linq;
using NUnit.Framework;
using Quieter.Player;
using Quieter.World;
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
        public IEnumerator CharacterController_CanRecoverFromRaisedCubeEdge()
        {
            var floor = CreateFloor(
                "EdgeRecoveryFloor",
                new Vector3(0f, -0.5f, 0f),
                new Vector3(10f, 1f, 10f));
            var cube = CreateFloor(
                "EdgeRecoveryCube",
                new Vector3(0f, 1f, 0f),
                new Vector3(2f, 2f, 2f));
            var player = CreatePlayer(new Vector3(1.3f, 2.02f, 0f));
            try
            {
                Physics.SyncTransforms();
                yield return null;

                var motor = new PlayerMovementMotor(player.GetComponent<CharacterController>());
                var state = new PlayerNetworkState { Position = player.transform.position };
                var input = new PlayerInputFrame { Movement = Vector2.left };
                SimulateTicks(motor, ref state, ref input, 18);

                Assert.That(
                    player.transform.position.x,
                    Is.LessThan(0.9f),
                    "Player could not steer back onto the cube from its rounded edge.");
                Assert.That(player.transform.position.y, Is.GreaterThan(1.9f));
                Assert.That(state.Grounded, Is.True);
            }
            finally
            {
                Object.Destroy(player);
                Object.Destroy(cube);
                Object.Destroy(floor);
            }
        }

        [UnityTest]
        public IEnumerator CharacterController_CanRecoverFromRaisedCubeCorner()
        {
            const float cubeHeight = 1.68f;
            var floor = CreateFloor(
                "CornerRecoveryFloor",
                new Vector3(0f, -0.5f, 0f),
                new Vector3(10f, 1f, 10f));
            var cube = CreateFloor(
                "CornerRecoveryCube",
                new Vector3(0f, cubeHeight * 0.5f, 0f),
                new Vector3(1.52f, cubeHeight, 1.64f));
            var player = CreatePlayer(new Vector3(0.84f, 1.56f, -0.9f));
            try
            {
                Physics.SyncTransforms();
                yield return null;

                var motor = new PlayerMovementMotor(player.GetComponent<CharacterController>());
                var state = new PlayerNetworkState
                {
                    Position = player.transform.position,
                    Velocity = Vector3.down * PlayerMovementTuning.GroundStickSpeed,
                    Grounded = true,
                    CoyoteTicks = PlayerMovementTuning.CoyoteTicks,
                };
                var input = new PlayerInputFrame
                {
                    Movement = new Vector2(-1f, 1f).normalized,
                };
                SimulateTicks(motor, ref state, ref input, 30);

                Assert.That(
                    player.transform.position.x,
                    Is.LessThan(0.45f),
                    "Player could not steer inward from the cube corner.");
                Assert.That(
                    player.transform.position.z,
                    Is.GreaterThan(-0.5f),
                    "Player remained pinned against the second corner face.");
                Assert.That(player.transform.position.y, Is.GreaterThan(1.6f));
                Assert.That(state.Grounded, Is.True);
            }
            finally
            {
                Object.Destroy(player);
                Object.Destroy(cube);
                Object.Destroy(floor);
            }
        }

        [UnityTest]
        public IEnumerator CharacterController_CanRecoverFromScreenshotCubeCorner()
        {
            const long screenshotWorldSeed = 4999677592911300101L;
            const ulong screenshotCubeId = 1697769707074391986UL;
            var generator = new DeterministicChunkGenerator();
            var definition = WorldDefinition.CreateDefault(screenshotWorldSeed);
            var spawn = generator.Generate(definition, new ChunkCoord(16, 15))
                .Objects.Single(item => item.InstanceId == screenshotCubeId);
            var groundHeight = generator.SampleHeight(
                definition,
                spawn.Position.x,
                spawn.Position.z);
            var floor = CreateFloor(
                "ScreenshotCornerFloor",
                new Vector3(spawn.Position.x, groundHeight - 0.5f, spawn.Position.z),
                new Vector3(10f, 1f, 10f));
            var cube = CreateFloor(
                "ScreenshotCornerCube",
                spawn.Position,
                spawn.Scale);
            cube.transform.rotation = spawn.Rotation;
            var screenshotPosition = new Vector3(38f, 7.3f, -29.7f);
            var player = CreatePlayer(screenshotPosition);
            try
            {
                Physics.SyncTransforms();
                yield return null;

                var motor = new PlayerMovementMotor(player.GetComponent<CharacterController>());
                var state = new PlayerNetworkState
                {
                    Position = player.transform.position,
                    Velocity = Vector3.down * PlayerMovementTuning.GroundStickSpeed,
                    Grounded = true,
                    CoyoteTicks = PlayerMovementTuning.CoyoteTicks,
                };
                var towardCenter = new Vector2(
                    spawn.Position.x - screenshotPosition.x,
                    spawn.Position.z - screenshotPosition.z).normalized;
                var idleInput = new PlayerInputFrame();
                var idleStart = new Vector2(
                    player.transform.position.x,
                    player.transform.position.z);
                SimulateTicks(motor, ref state, ref idleInput, 10);
                var idleEnd = new Vector2(
                    player.transform.position.x,
                    player.transform.position.z);
                Assert.That(
                    Vector2.Distance(idleStart, idleEnd),
                    Is.LessThan(0.05f),
                    "Player drifted across the screenshot cube corner without input.");
                Assert.That(state.Grounded, Is.True);

                var input = new PlayerInputFrame { Movement = towardCenter };
                var initialDistance = Vector2.Distance(
                    new Vector2(player.transform.position.x, player.transform.position.z),
                    new Vector2(spawn.Position.x, spawn.Position.z));
                SimulateTicks(motor, ref state, ref input, 10);
                var finalDistance = Vector2.Distance(
                    new Vector2(player.transform.position.x, player.transform.position.z),
                    new Vector2(spawn.Position.x, spawn.Position.z));

                Assert.That(
                    finalDistance,
                    Is.LessThan(initialDistance - 0.3f),
                    $"Player remained pinned at the screenshot corner; "
                    + $"distance {initialDistance:F3} -> {finalDistance:F3}, "
                    + $"position={player.transform.position}, velocity={state.Velocity}, "
                    + $"grounded={state.Grounded}, cube={spawn.Position}, "
                    + $"scale={spawn.Scale}, rotation={spawn.Rotation}.");
                Assert.That(player.transform.position.y, Is.GreaterThan(groundHeight + 1f));
                Assert.That(state.Grounded, Is.True);
            }
            finally
            {
                Object.Destroy(player);
                Object.Destroy(cube);
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
                // Sprint intent without actual speed must not boost the jump.
                var input = new PlayerInputFrame { Yaw = 0f, Sprint = true };
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
                    Is.EqualTo(PlayerMovementTuning.StandingJumpHeight).Within(0.12f));
                Assert.That(state.Grounded, Is.True);
            }
            finally
            {
                Object.Destroy(player);
                Object.Destroy(floor);
            }
        }

        [UnityTest]
        public IEnumerator CharacterController_JumpPressProducesFullFixedArc()
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
                for (var tick = 0; tick < 120; tick++)
                {
                    input.Sequence++;
                    motor.Simulate(ref state, input, 1f / 60f);
                    maximumHeight = Mathf.Max(maximumHeight, player.transform.position.y);
                }

                Assert.That(
                    maximumHeight - groundedHeight,
                    Is.EqualTo(PlayerMovementTuning.StandingJumpHeight).Within(0.12f));
                Assert.That(state.Grounded, Is.True);
            }
            finally
            {
                Object.Destroy(player);
                Object.Destroy(floor);
            }
        }

        [UnityTest]
        public IEnumerator CharacterController_JumpHeightScalesWithActualTakeoffSpeed()
        {
            var floor = CreateFloor(
                "SpeedProfileFloor",
                new Vector3(0f, -0.5f, 0f),
                new Vector3(18f, 1f, 40f));
            var players = new[]
            {
                CreatePlayer(new Vector3(-4f, 0.02f, -4f)),
                CreatePlayer(new Vector3(0f, 0.02f, -4f)),
                CreatePlayer(new Vector3(4f, 0.02f, -4f)),
            };

            try
            {
                Physics.SyncTransforms();
                yield return null;

                var motors = new PlayerMovementMotor[players.Length];
                var states = new PlayerNetworkState[players.Length];
                var inputs = new PlayerInputFrame[players.Length];
                for (var index = 0; index < players.Length; index++)
                {
                    motors[index] = new PlayerMovementMotor(
                        players[index].GetComponent<CharacterController>());
                    states[index] = new PlayerNetworkState
                    {
                        Position = players[index].transform.position,
                    };
                    for (var tick = 0; tick < 5; tick++)
                    {
                        inputs[index].Sequence++;
                        motors[index].Simulate(
                            ref states[index],
                            inputs[index],
                            1f / 60f);
                    }

                    Assert.That(states[index].Grounded, Is.True);
                }

                states[1].Velocity = Vector3.forward * PlayerMovementTuning.WalkSpeed;
                states[2].Velocity = Vector3.forward * PlayerMovementTuning.SprintSpeed;
                inputs[1].Movement = Vector2.up;
                inputs[2].Movement = Vector2.up;
                inputs[2].Sprint = true;

                var groundedHeights = new float[players.Length];
                var maximumHeights = new float[players.Length];
                var takeoffs = new int[players.Length];
                var wasGrounded = new bool[players.Length];
                for (var index = 0; index < players.Length; index++)
                {
                    groundedHeights[index] = players[index].transform.position.y;
                    maximumHeights[index] = groundedHeights[index];
                    wasGrounded[index] = true;
                    inputs[index].JumpPressId = 1;
                }

                for (var tick = 0; tick < 120; tick++)
                {
                    for (var index = 0; index < players.Length; index++)
                    {
                        inputs[index].Sequence++;
                        motors[index].Simulate(
                            ref states[index],
                            inputs[index],
                            1f / 60f);
                        maximumHeights[index] = Mathf.Max(
                            maximumHeights[index],
                            players[index].transform.position.y);
                        if (wasGrounded[index] && !states[index].Grounded)
                        {
                            takeoffs[index]++;
                        }

                        if (states[index].Velocity.y > 0f)
                        {
                            Assert.That(
                                states[index].Grounded,
                                Is.False,
                                $"Profile {index} re-grounded while rising on tick {tick}.");
                        }

                        wasGrounded[index] = states[index].Grounded;
                    }
                }

                var expectedHeights = new[]
                {
                    PlayerMovementTuning.StandingJumpHeight,
                    PlayerMovementTuning.WalkJumpHeight,
                    PlayerMovementTuning.SprintJumpHeight,
                };
                for (var index = 0; index < players.Length; index++)
                {
                    Assert.That(takeoffs[index], Is.EqualTo(1));
                    Assert.That(
                        maximumHeights[index] - groundedHeights[index],
                        Is.EqualTo(expectedHeights[index]).Within(0.12f));
                    Assert.That(states[index].Grounded, Is.True);
                }
            }
            finally
            {
                foreach (var player in players)
                {
                    Object.Destroy(player);
                }

                Object.Destroy(floor);
            }
        }

        [UnityTest]
        public IEnumerator CharacterController_SprintJumpTravelsAboutFourMetres()
        {
            var floor = CreateFloor(
                "SprintDistanceFloor",
                new Vector3(0f, -0.5f, 4f),
                new Vector3(10f, 1f, 24f));
            var player = CreatePlayer(new Vector3(0f, 0.02f, 0f));
            try
            {
                Physics.SyncTransforms();
                yield return null;

                var motor = new PlayerMovementMotor(player.GetComponent<CharacterController>());
                var state = new PlayerNetworkState { Position = player.transform.position };
                var input = new PlayerInputFrame
                {
                    Movement = Vector2.up,
                    Sprint = true,
                };
                SimulateTicks(motor, ref state, ref input, 5);
                Assert.That(state.Grounded, Is.True);

                state.Velocity = Vector3.forward * PlayerMovementTuning.SprintSpeed;
                var startZ = player.transform.position.z;
                var airborneTicks = 0;
                var tookOff = false;
                input.JumpPressId = 1;
                for (var tick = 0; tick < 90; tick++)
                {
                    input.Sequence++;
                    motor.Simulate(ref state, input, 1f / 60f);
                    if (!state.Grounded)
                    {
                        tookOff = true;
                        airborneTicks++;
                    }
                    else if (tookOff)
                    {
                        break;
                    }
                }

                var distance = player.transform.position.z - startZ;
                var airtime = airborneTicks / 60f;
                Assert.That(tookOff, Is.True);
                Assert.That(airtime, Is.EqualTo(0.560f).Within(2f / 60f));
                Assert.That(distance, Is.InRange(4f, 4.5f));
                Assert.That(state.Grounded, Is.True);
            }
            finally
            {
                Object.Destroy(player);
                Object.Destroy(floor);
            }
        }

        [UnityTest]
        public IEnumerator CharacterController_JumpIntoWallClearsBlockedMomentum()
        {
            var floor = CreateFloor(
                "WallJumpFloor",
                new Vector3(0f, -0.5f, 2f),
                new Vector3(8f, 1f, 12f));
            var wall = CreateFloor(
                "WallJumpBlocker",
                new Vector3(0f, 1.5f, 1.5f),
                new Vector3(4f, 3f, 0.4f));
            var player = CreatePlayer(new Vector3(0f, 0.02f, 0f));
            try
            {
                Physics.SyncTransforms();
                yield return null;

                var controller = player.GetComponent<CharacterController>();
                var motor = new PlayerMovementMotor(controller);
                var state = new PlayerNetworkState { Position = player.transform.position };
                var input = new PlayerInputFrame { Movement = Vector2.up };
                SimulateTicks(motor, ref state, ref input, 5);
                Assert.That(state.Grounded, Is.True);

                var groundedHeight = player.transform.position.y;
                var maximumHeight = groundedHeight;
                var minimumContactZ = float.PositiveInfinity;
                var maximumContactZ = float.NegativeInfinity;
                var maximumBlockedSpeed = 0f;
                var maximumRetreat = 0f;
                var airborneContactTicks = 0;
                var contactDetected = false;
                var previousZ = player.transform.position.z;

                state.Velocity = Vector3.forward * PlayerMovementTuning.WalkSpeed;
                input.JumpPressId = 1;
                for (var tick = 0; tick < 90; tick++)
                {
                    input.Sequence++;
                    motor.Simulate(ref state, input, 1f / 60f);
                    maximumHeight = Mathf.Max(maximumHeight, player.transform.position.y);
                    var planarStep = player.transform.position.z - previousZ;
                    if (!contactDetected
                        && !state.Grounded
                        && player.transform.position.z > 0.3f
                        && Mathf.Abs(planarStep) < 0.002f)
                    {
                        contactDetected = true;
                    }

                    if (contactDetected && !state.Grounded)
                    {
                        airborneContactTicks++;
                        minimumContactZ = Mathf.Min(minimumContactZ, player.transform.position.z);
                        maximumContactZ = Mathf.Max(maximumContactZ, player.transform.position.z);
                        maximumBlockedSpeed = Mathf.Max(
                            maximumBlockedSpeed,
                            Mathf.Abs(state.Velocity.z));
                        maximumRetreat = Mathf.Max(
                            maximumRetreat,
                            previousZ - player.transform.position.z);
                    }

                    previousZ = player.transform.position.z;
                }

                Assert.That(airborneContactTicks, Is.GreaterThan(5));
                Assert.That(
                    maximumContactZ - minimumContactZ,
                    Is.LessThan(0.08f),
                    $"Wall contact moved from {minimumContactZ:F3} to {maximumContactZ:F3}.");
                Assert.That(maximumRetreat, Is.LessThan(0.005f));
                Assert.That(maximumBlockedSpeed, Is.LessThan(0.2f));
                Assert.That(
                    maximumHeight - groundedHeight,
                    Is.EqualTo(PlayerMovementTuning.WalkJumpHeight).Within(0.12f));
                Assert.That(state.Grounded, Is.True);
            }
            finally
            {
                Object.Destroy(player);
                Object.Destroy(wall);
                Object.Destroy(floor);
            }
        }

        [UnityTest]
        public IEnumerator CharacterController_MovingJumpStaysAirborneWhileRisingOnSlope()
        {
            var slope = CreateFloor(
                "JumpSlope",
                new Vector3(0f, -0.5f, 0f),
                new Vector3(14f, 1f, 18f));
            slope.transform.rotation = Quaternion.Euler(10f, 0f, 0f);
            var players = new[]
            {
                CreatePlayer(new Vector3(-2f, 1f, 0f)),
                CreatePlayer(new Vector3(2f, 1f, 0f)),
            };

            try
            {
                Physics.SyncTransforms();
                yield return null;

                var motors = new[]
                {
                    new PlayerMovementMotor(players[0].GetComponent<CharacterController>()),
                    new PlayerMovementMotor(players[1].GetComponent<CharacterController>()),
                };
                var states = new[]
                {
                    new PlayerNetworkState { Position = players[0].transform.position },
                    new PlayerNetworkState { Position = players[1].transform.position },
                };
                var inputs = new[]
                {
                    new PlayerInputFrame { Movement = Vector2.up },
                    new PlayerInputFrame { Movement = Vector2.down },
                };
                for (var tick = 0; tick < 30; tick++)
                {
                    for (var index = 0; index < players.Length; index++)
                    {
                        inputs[index].Sequence++;
                        motors[index].Simulate(
                            ref states[index],
                            inputs[index],
                            1f / 60f);
                    }
                }

                for (var index = 0; index < players.Length; index++)
                {
                    Assert.That(states[index].Grounded, Is.True);
                    states[index].Velocity = (index == 0 ? Vector3.forward : Vector3.back)
                        * PlayerMovementTuning.WalkSpeed;
                    inputs[index].JumpPressId = 1;
                }

                var sawAscent = new bool[players.Length];
                for (var tick = 0; tick < 45; tick++)
                {
                    for (var index = 0; index < players.Length; index++)
                    {
                        inputs[index].Sequence++;
                        motors[index].Simulate(
                            ref states[index],
                            inputs[index],
                            1f / 60f);
                        if (states[index].Velocity.y > 0f)
                        {
                            sawAscent[index] = true;
                            Assert.That(
                                states[index].Grounded,
                                Is.False,
                                $"Slope direction {index} re-grounded on tick {tick}.");
                        }
                    }
                }

                Assert.That(sawAscent, Is.All.True);
            }
            finally
            {
                foreach (var player in players)
                {
                    Object.Destroy(player);
                }

                Object.Destroy(slope);
            }
        }

        [UnityTest]
        public IEnumerator CharacterController_JumpableAndBlockingObstacleHeightsAreDistinct()
        {
            var floor = CreateFloor(
                "ObstacleFloor",
                new Vector3(0f, -0.5f, 4f),
                new Vector3(18f, 1f, 28f));
            var obstacles = new[]
            {
                CreateFloor("WalkJumpable", new Vector3(-4f, 0.45f, 2f), new Vector3(2f, 0.9f, 1.5f)),
                CreateFloor("SprintJumpable", new Vector3(0f, 0.45f, 2f), new Vector3(2f, 0.9f, 1.5f)),
                CreateFloor("WalkBlocking", new Vector3(4f, 0.84f, 2f), new Vector3(2f, 1.68f, 1.5f)),
            };
            var players = new[]
            {
                CreatePlayer(new Vector3(-4f, 0.02f, -1f)),
                CreatePlayer(new Vector3(0f, 0.02f, -2.5f)),
                CreatePlayer(new Vector3(4f, 0.02f, -1f)),
            };

            try
            {
                Physics.SyncTransforms();
                yield return null;

                var motors = new PlayerMovementMotor[players.Length];
                var states = new PlayerNetworkState[players.Length];
                var inputs = new PlayerInputFrame[players.Length];
                for (var index = 0; index < players.Length; index++)
                {
                    motors[index] = new PlayerMovementMotor(
                        players[index].GetComponent<CharacterController>());
                    states[index] = new PlayerNetworkState
                    {
                        Position = players[index].transform.position,
                    };
                    inputs[index] = new PlayerInputFrame { Movement = Vector2.up };
                    SimulateTicks(motors[index], ref states[index], ref inputs[index], 5);
                    Assert.That(states[index].Grounded, Is.True);
                }

                states[0].Velocity = Vector3.forward * PlayerMovementTuning.WalkSpeed;
                states[1].Velocity = Vector3.forward * PlayerMovementTuning.SprintSpeed;
                states[2].Velocity = Vector3.forward * PlayerMovementTuning.WalkSpeed;
                inputs[1].Sprint = true;
                for (var index = 0; index < players.Length; index++)
                {
                    inputs[index].JumpPressId = 1;
                }

                var landedOnTop = new bool[players.Length];
                var maximumY = new float[players.Length];
                var topLandingTick = new[] { -1, -1, -1 };
                var topLandingZ = new float[players.Length];
                for (var tick = 0; tick < 120; tick++)
                {
                    for (var index = 0; index < players.Length; index++)
                    {
                        inputs[index].Sequence++;
                        motors[index].Simulate(
                            ref states[index],
                            inputs[index],
                            1f / 60f);
                        maximumY[index] = Mathf.Max(
                            maximumY[index],
                            players[index].transform.position.y);
                        var obstacleHeight = index < 2 ? 0.9f : 1.68f;
                        if (states[index].Grounded
                            && players[index].transform.position.y > obstacleHeight - 0.08f)
                        {
                            landedOnTop[index] = true;
                            if (topLandingTick[index] < 0)
                            {
                                topLandingTick[index] = tick;
                                topLandingZ[index] = players[index].transform.position.z;
                            }
                        }
                    }
                }

                Assert.That(landedOnTop[0], Is.True, "Walk jump did not clear 0.9 m.");
                Assert.That(landedOnTop[1], Is.True, "Sprint jump did not clear 0.9 m.");
                Assert.That(
                    landedOnTop[2],
                    Is.False,
                    $"Walk jump unexpectedly cleared 1.68 m; maxY={maximumY[2]:F3}, "
                    + $"tick={topLandingTick[2]}, z={topLandingZ[2]:F3}.");
            }
            finally
            {
                foreach (var player in players)
                {
                    Object.Destroy(player);
                }

                foreach (var obstacle in obstacles)
                {
                    Object.Destroy(obstacle);
                }

                Object.Destroy(floor);
            }
        }

        [UnityTest]
        public IEnumerator CharacterController_ReferenceCubeIsMaximumReachableHeight()
        {
            const float reachableHeight = 1.63f;
            const float nextHigherHeight = 1.68f;
            var floor = CreateFloor(
                "ReferenceCubeFloor",
                new Vector3(0f, -0.5f, 4f),
                new Vector3(12f, 1f, 20f));
            var obstacles = new[]
            {
                CreateFloor(
                    "ScreenshotReferenceCube",
                    new Vector3(-2f, reachableHeight * 0.5f, 2f),
                    new Vector3(1.92f, reachableHeight, 1.64f)),
                CreateFloor(
                    "NextHigherCube",
                    new Vector3(2f, nextHigherHeight * 0.5f, 2f),
                    new Vector3(1.52f, nextHigherHeight, 1.64f)),
            };
            var players = new[]
            {
                CreatePlayer(new Vector3(-2f, 0.02f, -1.2f)),
                CreatePlayer(new Vector3(2f, 0.02f, -1.2f)),
            };

            try
            {
                Physics.SyncTransforms();
                yield return null;

                var motors = new PlayerMovementMotor[players.Length];
                var states = new PlayerNetworkState[players.Length];
                var inputs = new PlayerInputFrame[players.Length];
                var takeoffPositions = new Vector3[players.Length];
                for (var index = 0; index < players.Length; index++)
                {
                    motors[index] = new PlayerMovementMotor(
                        players[index].GetComponent<CharacterController>());
                    states[index] = new PlayerNetworkState
                    {
                        Position = players[index].transform.position,
                    };
                    inputs[index] = new PlayerInputFrame
                    {
                        Movement = Vector2.up,
                        Sprint = true,
                    };
                    SimulateTicks(motors[index], ref states[index], ref inputs[index], 5);
                    Assert.That(states[index].Grounded, Is.True);
                    takeoffPositions[index] = players[index].transform.position;
                    states[index].Velocity = Vector3.forward
                        * PlayerMovementTuning.SprintSpeed;
                    inputs[index].JumpPressId = 1;
                }

                var landedOnTop = new bool[players.Length];
                var landingTicks = new[] { -1, -1 };
                var landingPositions = new Vector3[players.Length];
                var maximumPositions = new Vector3[players.Length];
                for (var tick = 0; tick < 120; tick++)
                {
                    for (var index = 0; index < players.Length; index++)
                    {
                        inputs[index].Sequence++;
                        motors[index].Simulate(
                            ref states[index],
                            inputs[index],
                            1f / 60f);
                        if (players[index].transform.position.y
                            > maximumPositions[index].y)
                        {
                            maximumPositions[index] = players[index].transform.position;
                        }

                        var obstacleHeight = index == 0
                            ? reachableHeight
                            : nextHigherHeight;
                        if (states[index].Grounded
                            && players[index].transform.position.y
                                > obstacleHeight - 0.08f)
                        {
                            landedOnTop[index] = true;
                            if (landingTicks[index] < 0)
                            {
                                landingTicks[index] = tick;
                                landingPositions[index] = players[index].transform.position;
                            }
                        }
                    }
                }

                Assert.That(
                    landedOnTop[0],
                    Is.True,
                    "A full-speed jump did not land on the 1.63 m screenshot cube.");
                Assert.That(
                    landedOnTop[1],
                    Is.False,
                    "A full-speed jump unexpectedly climbed the next 1.68 m cube; "
                    + $"landingTick={landingTicks[1]}, "
                    + $"takeoff={takeoffPositions[1]}, landing={landingPositions[1]}, "
                    + $"max={maximumPositions[1]}.");
            }
            finally
            {
                foreach (var player in players)
                {
                    Object.Destroy(player);
                }

                foreach (var obstacle in obstacles)
                {
                    Object.Destroy(obstacle);
                }

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
                    Is.EqualTo(PlayerMovementTuning.StandingJumpHeight).Within(0.12f));
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
                SimulateTicks(motor, ref state, ref input, 5);

                Assert.That(state.Crouched, Is.True);
                Assert.That(state.Grounded, Is.True);
                Assert.That(state.LastConsumedJumpPressId, Is.EqualTo(1));

                ceiling.transform.position = new Vector3(0f, 4f, 0f);
                Physics.SyncTransforms();
                SimulateTicks(motor, ref state, ref input, 10);

                Assert.That(state.Crouched, Is.False);
                Assert.That(state.Grounded, Is.True);
                Assert.That(controller.height, Is.EqualTo(PlayerMovementTuning.StandingHeight).Within(0.0001f));

                input.JumpPressId = 2;
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
