using System;
using Quieter.Inventory;
using Quieter.Player;
using Quieter.World;
using Unity.Netcode;
using UnityEngine;

namespace Quieter.Survival
{
    /// <summary>
    /// Server-only controller for persistent characters. The brain produces the
    /// same input frames as a player and consumes the same inventory, physiology,
    /// resource-node and combat systems; it never edits a client-owned result.
    /// </summary>
    [RequireComponent(typeof(NetworkObject), typeof(NetworkPlayer))]
    [RequireComponent(typeof(PlayerSurvival), typeof(PlayerInventory))]
    public sealed class NpcBrain : MonoBehaviour
    {
        private const float DetailedDistance = 110f;
        private const float AggressionDistance = 16f;
        private const float InteractionDistance = 2.7f;
        private const float ResourceSearchDistance = 70f;

        private NetworkObject networkObject;
        private NetworkPlayer networkPlayer;
        private PlayerSurvival survival;
        private PlayerInventory inventory;
        private ResourceWorldService resources;
        private PlacedObjectWorldService placedObjects;
        private WorldWeatherService weather;
        private float nextThinkAt;
        private float lastThinkAt;

        private void Awake()
        {
            networkObject = GetComponent<NetworkObject>();
            networkPlayer = GetComponent<NetworkPlayer>();
            survival = GetComponent<PlayerSurvival>();
            inventory = GetComponent<PlayerInventory>();
        }

        private void Update()
        {
            if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer
                || networkObject == null || !networkObject.IsSpawned
                || survival == null || inventory == null || networkPlayer == null)
                return;
            var state = survival.ServerState;
            if (!IsNpc(state))
            {
                networkPlayer.ServerClearAutonomousInput();
                return;
            }
            if (state.Offline) survival.ServerSetOffline(false);
            if (state.Physiology.LifeState == CharacterLifeState.Dead
                || !state.CreationCompleted)
            {
                networkPlayer.ServerClearAutonomousInput();
                return;
            }

            var now = Time.unscaledTime;
            if (now < nextThinkAt) return;
            ResolveWorldServices();
            var closestPlayer = FindClosestLivingPlayer(out var playerDistance);
            var detailed = closestPlayer != null && playerDistance <= DetailedDistance;
            var interval = ThinkInterval(detailed);
            var elapsed = lastThinkAt <= 0f ? interval : Mathf.Clamp(now - lastThinkAt, 0.05f, 10f);
            lastThinkAt = now;
            nextThinkAt = now + interval;
            TickBrain(state, closestPlayer, playerDistance, elapsed);
        }

        private void TickBrain(
            CharacterSurvivalState state,
            PlayerSurvival closestPlayer,
            float playerDistance,
            float elapsed)
        {
            state.EnsureInitialized();
            var npc = state.Npc;
            TickContract(state);

            if (state.Sleeping)
            {
                var dangerClose = npc.Disposition == NpcDisposition.Aggressive
                    && closestPlayer != null && playerDistance < 8f;
                if (state.Physiology.SleepDebt <= 0.12f || dangerClose)
                    survival.ServerNpcEndSleep();
                else
                {
                    npc.Activity = NpcActivityKind.Rest;
                    networkPlayer.ServerClearAutonomousInput();
                    return;
                }
            }
            if (!survival.CanPerformAutonomousServerAction())
            {
                networkPlayer.ServerClearAutonomousInput();
                return;
            }

            ConsumeStoredNeeds(state);
            if (HandleUrgentSanitation(state)) return;
            if (npc.PendingSabotageActions > 0 && TickSabotage(state)) return;
            if (npc.FleeUntilUtcTicks > DateTime.UtcNow.Ticks)
            {
                npc.Activity = NpcActivityKind.Flee;
                if (closestPlayer != null)
                {
                    var away = transform.position - closestPlayer.transform.position;
                    away.y = 0f;
                    if (away.sqrMagnitude < 0.01f) away = transform.forward;
                    SetDestination(state, transform.position + away.normalized * 24f);
                    MoveToward(npc.Destination, sprint: true);
                }
                else TickWander(state);
                return;
            }
            var lowCondition = state.Physiology.BloodVolume < 0.5f
                || state.Physiology.Pain > 0.78f
                || state.Physiology.Consciousness < 0.55f;
            var hostileTarget = npc.Disposition == NpcDisposition.Aggressive
                && closestPlayer != null && playerDistance <= AggressionDistance;
            if (hostileTarget && !lowCondition)
            {
                npc.Activity = NpcActivityKind.Combat;
                TickAggression(closestPlayer, playerDistance);
                return;
            }

            var contract = state.WorkerContract;
            var priorityJob = state.Npc.ActiveJob;
            var assignedWork = contract?.Active == true
                && LivingWorldSimulation.TrySelectPriorityJob(
                    contract, state.Npc.ActiveJob, out priorityJob);
            if (assignedWork) state.Npc.ActiveJob = priorityJob;
            var gameHour = weather != null ? weather.Current.DayFraction * 24f : 12f;
            var workHours = assignedWork && LivingWorldSimulation.IsWithinWorkHours(
                gameHour, contract.WorkdayStartHour, contract.WorkdayEndHour);
            npc.Activity = LivingWorldSimulation.ChooseNpcActivity(
                state.Physiology,
                inventory.HasServerDrink(),
                inventory.HasServerFood(),
                lowCondition && closestPlayer != null && playerDistance < 12f,
                assignedWork,
                workHours);
            if (npc.Activity == NpcActivityKind.Rest)
            {
                survival.ServerNpcBeginSleep();
                networkPlayer.ServerClearAutonomousInput();
                return;
            }
            // Distance changes only how often decisions are made. Distant NPCs
            // still walk to actual sources, consume their own food/tools and
            // commit through the same atomic resource operations as nearby NPCs.
            // Freezing this branch would turn leaving an area into free stasis.
            if (npc.Activity == NpcActivityKind.Flee && closestPlayer != null)
            {
                var away = transform.position - closestPlayer.transform.position;
                away.y = 0f;
                SetDestination(state, transform.position + away.normalized * 18f);
                MoveToward(npc.Destination, sprint: true);
                return;
            }
            if (npc.Activity is NpcActivityKind.SeekWater or NpcActivityKind.SeekFood)
            {
                TickNeedSearch(state, elapsed);
                return;
            }
            if (npc.Activity == NpcActivityKind.Work)
            {
                TickWork(state, elapsed);
                return;
            }
            TickWander(state);
        }

        public static float ThinkInterval(bool detailed) => detailed ? 0.35f : 10f;

        private void ConsumeStoredNeeds(CharacterSurvivalState state)
        {
            var physiology = state.Physiology;
            if (physiology.Hydration < 0.78f
                && inventory.TryConsumeServerDrink(250, out var drink))
            {
                var hydrationEfficiency = drink.LiquidKind == LiquidKind.SaltWater ? -0.35f : 1f;
                var electrolytes = drink.LiquidKind switch
                {
                    LiquidKind.Broth => 0.65f,
                    LiquidKind.HerbalInfusion => 0.18f,
                    LiquidKind.SaltWater => 1f,
                    _ => 0.05f,
                };
                survival.ServerNpcConsumeLiquid(
                    drink.LiquidMilliliters / 1000f,
                    drink.BiologicalContamination / 10000f,
                    drink.ToxinContamination / 10000f,
                    electrolytes,
                    hydrationEfficiency);
                state.Npc.LastNeedsActionUtcTicks = DateTime.UtcNow.Ticks;
            }
            if (physiology.StomachFullness < 0.88f
                && (physiology.StomachFullness < 0.58f || physiology.EnergyReserve < 0.65f)
                && inventory.TryConsumeServerFood(out var food, out var stack))
            {
                var spoilage = 1f - stack.Freshness / 10000f;
                var biological = Mathf.Clamp01(
                    stack.BiologicalContamination / 10000f + spoilage * spoilage * 0.85f);
                if (survival.ServerNpcConsumeFood(
                        food.CaloriesPerUnit,
                        food.ProteinGramsPerUnit,
                        food.MicronutrientsPerUnit,
                        food.WaterLitersPerUnit,
                        biological,
                        stack.ToxinContamination / 10000f,
                        food.FatGramsPerUnit,
                        food.MineralsPerUnit))
                {
                    state.Npc.RationCaloriesCurrentDay += food.CaloriesPerUnit;
                    state.Npc.LastNeedsActionUtcTicks = DateTime.UtcNow.Ticks;
                }
            }
        }

        private bool HandleUrgentSanitation(CharacterSurvivalState state)
        {
            if (state.Physiology.BladderFill < 0.88f
                && state.Physiology.BowelFill < 0.9f) return false;
            var owner = state.Npc.EmployerAccountId;
            if (placedObjects != null && !string.IsNullOrWhiteSpace(owner)
                && placedObjects.TryFindClosestOwnedStructure(
                    SurvivalStructureRules.LatrineItemId,
                    transform.position,
                    Mathf.Max(12f, state.WorkerContract?.WorkZoneRadius ?? 12f),
                    owner,
                    out var latrineId,
                    out var latrinePosition))
            {
                state.Npc.Activity = NpcActivityKind.SeekSanitation;
                if (PlanarDistance(transform.position, latrinePosition) > InteractionDistance)
                {
                    SetDestination(state, latrinePosition);
                    MoveToward(latrinePosition, sprint: false);
                    return true;
                }
                FaceAndStop(latrinePosition);
                if (survival.ServerUseLatrine(
                        placedObjects, latrineId, out _)) return true;
            }
            survival.ServerNpcRelieveNeeds();
            return true;
        }

        private void TickNeedSearch(CharacterSurvivalState state, float elapsed)
        {
            var search = state.Npc.Activity == NpcActivityKind.SeekWater
                ? ResourceSearchKind.Water : ResourceSearchKind.Food;
            if (search == ResourceSearchKind.Water && TryUseOwnedWell(state, drink: true))
                return;
            if (resources == null)
            {
                TickWander(state);
                return;
            }
            if (!resources.TryFindClosestAvailableNode(
                    transform.position, ResourceSearchDistance, search, out var node))
            {
                TickWander(state);
                return;
            }
            SetDestination(state, node.transform.position);
            if (PlanarDistance(transform.position, node.transform.position) > InteractionDistance)
            {
                MoveToward(node.transform.position, sprint: false);
                return;
            }
            FaceAndStop(node.transform.position);
            if (search == ResourceSearchKind.Water)
            {
                ResourceBalance.SampleSpringWater(
                    node.InstanceId, out var biological, out var toxins);
                var rain = weather != null
                    ? weather.GetEnvironment(node.transform.position).Precipitation : 0f;
                placedObjects?.ApplyWasteContamination(
                    node.transform.position, rain, ref biological, ref toxins);
                survival.ServerNpcConsumeLiquid(0.35f, biological, toxins, 0.05f, 1f);
                state.Npc.LastNeedsActionUtcTicks = DateTime.UtcNow.Ticks;
                return;
            }
            CollectLooseNode(state, node, WorkerJobKind.Foraging);
        }

        private void TickWork(CharacterSurvivalState state, float elapsed)
        {
            var contract = state.WorkerContract;
            if (contract == null || contract.AllowedJobs.Count == 0)
            {
                TickWander(state);
                return;
            }
            if (!contract.AllowedJobs.Contains(state.Npc.ActiveJob))
                state.Npc.ActiveJob = contract.AllowedJobs[0];
            if (!TryGetResourceSearch(state.Npc.ActiveJob, out var search))
            {
                TickSpecialWork(state, elapsed);
                return;
            }
            if (resources == null || !resources.TryFindClosestAvailableNode(
                    transform.position, ResourceSearchDistance, search, out var node))
            {
                TickWander(state);
                return;
            }
            if (node.Descriptor.RequiredTool != ToolKind.None
                && !inventory.HasServerTool(node.Descriptor.RequiredTool))
            {
                networkPlayer.ServerClearAutonomousInput();
                return;
            }
            SetDestination(state, node.transform.position);
            if (PlanarDistance(transform.position, node.transform.position) > InteractionDistance)
            {
                MoveToward(node.transform.position, sprint: false);
                return;
            }
            FaceAndStop(node.transform.position);
            var skill = WorkSkill(state.Npc.ActiveJob, node.Descriptor);
            var level = CharacterProgression.GetSkillLevel(state.Progression, skill);
            var capability = PhysiologySimulation.CalculateCapabilities(state);
            var health = Mathf.Clamp01(Mathf.Min(
                state.Physiology.BloodVolume,
                Mathf.Min(state.Physiology.Consciousness,
                    (state.Anatomy.HeartFunction + state.Anatomy.LungFunction) * 0.5f)));
            var weatherEfficiency = weather == null ? 1f : Mathf.Clamp01(
                1f - weather.GetEnvironment(transform.position).Precipitation * 0.35f);
            var toolEfficiency = node.Descriptor.RequiredTool == ToolKind.None ? 0.8f : 1f;
            state.Npc.Motivation = LivingWorldSimulation.CalculateWorkerMotivation(
                contract, FindEmployerRelationship(state), state.Physiology);
            var performance = LivingWorldSimulation.CalculateWorkerOutput(
                new WorkerPerformanceInput(
                    level, capability.MovementSpeed, health, state.Npc.Motivation,
                    toolEfficiency, 0.9f, weatherEfficiency), 1f);
            state.Npc.WorkProgressSeconds += elapsed * performance;
            if (state.Npc.WorkProgressSeconds < ResourceBalance.MiningCooldownSeconds) return;
            state.Npc.WorkProgressSeconds -= ResourceBalance.MiningCooldownSeconds;
            if (node.Descriptor.IsLoosePickup)
                CollectLooseNode(state, node, state.Npc.ActiveJob);
            else
                MineNode(state, node, skill);
        }

        private void TickSpecialWork(CharacterSurvivalState state, float elapsed)
        {
            switch (state.Npc.ActiveJob)
            {
                case WorkerJobKind.Hauling:
                    TickHauling(state);
                    break;
                case WorkerJobKind.Construction:
                    TickConstruction(state, elapsed);
                    break;
                case WorkerJobKind.CookingAndWater:
                    TickCookingAndWater(state, elapsed);
                    break;
                case WorkerJobKind.Sanitation:
                    TickSanitation(state, elapsed);
                    break;
                case WorkerJobKind.PatientCare:
                    TickPatientCare(state, elapsed);
                    break;
                default:
                    networkPlayer.ServerClearAutonomousInput();
                    break;
            }
        }

        private void TickHauling(CharacterSurvivalState state)
        {
            if (inventory.HasServerHaulableCargo()
                && TryStoreHaulableCargo(state)) return;
            NetworkWorldItem closest = null;
            var radius = Mathf.Max(5f, state.WorkerContract.WorkZoneRadius);
            var closestSquared = radius * radius;
            var center = ResolveWorkCenter(state);
            foreach (var item in FindObjectsByType<NetworkWorldItem>())
            {
                if (item == null || !item.IsSpawned || item.Stack.IsEmpty
                    || PlanarDistance(center, item.transform.position) > radius) continue;
                var squared = (transform.position - item.transform.position).sqrMagnitude;
                if (squared >= closestSquared) continue;
                closestSquared = squared;
                closest = item;
            }
            if (closest == null)
            {
                FaceAndStop(state.WorkerContract.StoragePosition);
                return;
            }
            SetDestination(state, closest.transform.position);
            if (PlanarDistance(transform.position, closest.transform.position) > 2.2f)
            {
                MoveToward(closest.transform.position, sprint: false);
                return;
            }
            FaceAndStop(closest.transform.position);
            if (!inventory.TryCollectWorldItemServer(closest, out var collected)
                || collected <= 0) return;
            RegisterCompletedTask(state, WorkerJobKind.Hauling,
                SkillId.LoadCarrying, 5f, 0.25f);
            survival.ServerRegisterPhysicalLoad(
                CharacterAttributeId.Strength, 5f, 0.3f);
        }

        private bool TryStoreHaulableCargo(CharacterSurvivalState state)
        {
            if (placedObjects == null || string.IsNullOrWhiteSpace(
                    state.Npc.EmployerAccountId)) return false;
            var storageCenter = state.WorkerContract.StoragePosition == Vector3.zero
                ? ResolveWorkCenter(state)
                : state.WorkerContract.StoragePosition;
            if (!placedObjects.TryFindClosestOwnedStructure(
                    SurvivalStructureRules.ChestItemId,
                    storageCenter,
                    8f,
                    state.Npc.EmployerAccountId,
                    out var chestId,
                    out var chestPosition)) return false;
            if (PlanarDistance(transform.position, chestPosition) > InteractionDistance)
            {
                SetDestination(state, chestPosition);
                MoveToward(chestPosition, sprint: false);
                return true;
            }
            FaceAndStop(chestPosition);
            if (!inventory.TryExtractFirstHaulableCargoServer(out var cargo)) return true;
            if (!placedObjects.TryInsertChestItem(
                    chestId, state.Npc.EmployerAccountId, cargo, out _))
            {
                var remainder = inventory.InsertStackServer(cargo);
                if (remainder > 0)
                    inventory.SpawnOverflowServer(
                        cargo.WithQuantity(remainder), transform.position);
                return true;
            }
            RegisterCompletedTask(state, WorkerJobKind.Hauling,
                SkillId.LoadCarrying, 5f, 0.3f);
            survival.ServerRegisterPhysicalLoad(
                CharacterAttributeId.Strength, 5f, 0.35f);
            return true;
        }

        private bool TickSabotage(CharacterSurvivalState state)
        {
            if (placedObjects == null || state.WorkerContract == null
                || string.IsNullOrWhiteSpace(state.Npc.EmployerAccountId))
            {
                state.Npc.PendingSabotageActions = 0;
                return false;
            }
            var storageCenter = state.WorkerContract.StoragePosition == Vector3.zero
                ? ResolveWorkCenter(state)
                : state.WorkerContract.StoragePosition;
            if (!placedObjects.TryFindClosestOwnedStructure(
                    SurvivalStructureRules.ChestItemId,
                    storageCenter,
                    Mathf.Max(8f, state.WorkerContract.WorkZoneRadius),
                    state.Npc.EmployerAccountId,
                    out var chestId,
                    out var chestPosition))
            {
                state.Npc.PendingSabotageActions = 0;
                return false;
            }
            state.Npc.Activity = NpcActivityKind.Sabotage;
            if (PlanarDistance(transform.position, chestPosition) > InteractionDistance)
            {
                SetDestination(state, chestPosition);
                MoveToward(chestPosition, sprint: false);
                return true;
            }
            FaceAndStop(chestPosition);
            if (placedObjects.TrySabotageChestItem(
                    chestId, state.Npc.EmployerAccountId, out _))
            {
                state.Npc.PendingSabotageActions--;
                state.Npc.LastWorkCompletedUtcTicks = DateTime.UtcNow.Ticks;
            }
            else
            {
                state.Npc.PendingSabotageActions = 0;
            }
            return true;
        }

        private void TickConstruction(CharacterSurvivalState state, float elapsed)
        {
            if (!AccumulateSpecialWork(state, elapsed, 12f)) return;
            ushort itemId = 0;
            foreach (var candidate in new ushort[] { 44, 45, 46, 48, 24 })
            {
                if (!inventory.HasServerItem(candidate)) continue;
                itemId = candidate;
                break;
            }
            if (itemId == 0 || placedObjects == null)
            {
                networkPlayer.ServerClearAutonomousInput();
                return;
            }
            var taskIndex = state.Npc.CompletedTasks[(int)WorkerJobKind.Construction];
            var sample = StableSample(state.CharacterId, taskIndex + 311);
            var angle = (sample & 0xffffUL) / 65535f * Mathf.PI * 2f;
            var ring = 4.5f + taskIndex / 6 * 3f;
            var position = ResolveWorkCenter(state) + new Vector3(
                Mathf.Cos(angle) * ring, 0f, Mathf.Sin(angle) * ring);
            var streamer = FindAnyObjectByType<WorldStreamer>();
            if (streamer != null) position.y = streamer.SampleHeight(position.x, position.z);
            if (!placedObjects.CanPlaceStructure(position, itemId)
                || !placedObjects.TryPlace(position, (float)(sample % 4UL) * 90f,
                    itemId, state.Npc.EmployerAccountId, out var objectId))
                return;
            if (!inventory.TryConsumeAnyItemServer(itemId))
            {
                placedObjects.TryRemoveJustPlaced(objectId, state.Npc.EmployerAccountId);
                return;
            }
            RegisterCompletedTask(state, WorkerJobKind.Construction,
                SkillId.Construction, 12f, 0.45f);
        }

        private void TickCookingAndWater(CharacterSurvivalState state, float elapsed)
        {
            if (placedObjects != null && placedObjects.TryFindBurningHearth(
                    transform.position, state.WorkerContract.WorkZoneRadius, out var hearthId)
                && placedObjects.TryGetBurningHearth(hearthId, out var hearthPosition))
            {
                if (PlanarDistance(transform.position, hearthPosition) > InteractionDistance)
                {
                    SetDestination(state, hearthPosition);
                    MoveToward(hearthPosition, sprint: false);
                    return;
                }
                FaceAndStop(hearthPosition);
                if (AccumulateSpecialWork(state, elapsed, 10f)
                    && inventory.TryBoilAnyWaterServer())
                {
                    RegisterCompletedTask(state, WorkerJobKind.CookingAndWater,
                        SkillId.WaterSafety, 10f, 0.35f);
                    return;
                }
            }
            if (TryUseOwnedWell(state, drink: false)) return;
            if (resources == null || !resources.TryFindClosestAvailableNode(
                    transform.position, state.WorkerContract.WorkZoneRadius,
                    ResourceSearchKind.Water, out var spring))
            {
                networkPlayer.ServerClearAutonomousInput();
                return;
            }
            if (PlanarDistance(transform.position, spring.transform.position) > InteractionDistance)
            {
                SetDestination(state, spring.transform.position);
                MoveToward(spring.transform.position, sprint: false);
                return;
            }
            FaceAndStop(spring.transform.position);
            ResourceBalance.SampleSpringWater(
                spring.InstanceId, out var biological, out var toxins);
            placedObjects?.ApplyWasteContamination(
                spring.transform.position,
                weather?.GetEnvironment(spring.transform.position).Precipitation ?? 0f,
                ref biological, ref toxins);
            if (inventory.TryFillAnyContainerServer(
                    1000, biological, toxins, out var filled) && filled > 0)
                RegisterCompletedTask(state, WorkerJobKind.CookingAndWater,
                    SkillId.WaterSafety, 6f, 0.25f);
        }

        private bool TryUseOwnedWell(CharacterSurvivalState state, bool drink)
        {
            if (placedObjects == null || string.IsNullOrWhiteSpace(
                    state.Npc.EmployerAccountId)
                || !placedObjects.TryFindClosestOwnedStructure(
                    SurvivalStructureRules.WellItemId,
                    transform.position,
                    drink ? ResourceSearchDistance : state.WorkerContract.WorkZoneRadius,
                    state.Npc.EmployerAccountId,
                    out var wellId,
                    out var wellPosition)) return false;
            if (PlanarDistance(transform.position, wellPosition) > InteractionDistance)
            {
                SetDestination(state, wellPosition);
                MoveToward(wellPosition, sprint: false);
                return true;
            }
            FaceAndStop(wellPosition);
            var rain = weather?.GetEnvironment(wellPosition).Precipitation ?? 0f;
            if (!placedObjects.TrySampleWellWater(
                    wellId, rain, out var biological, out var toxins)) return true;
            if (drink)
            {
                survival.ServerNpcConsumeLiquid(
                    0.35f, biological, toxins, 0.05f, 1f);
                state.Npc.LastNeedsActionUtcTicks = DateTime.UtcNow.Ticks;
            }
            else if (inventory.TryFillAnyContainerServer(
                         1000, biological, toxins, out var filled) && filled > 0)
            {
                RegisterCompletedTask(state, WorkerJobKind.CookingAndWater,
                    SkillId.WaterSafety, 6f, 0.3f);
            }
            return true;
        }

        private void TickSanitation(CharacterSurvivalState state, float elapsed)
        {
            if (!inventory.HasServerWaste() || placedObjects == null
                || !placedObjects.TryFindClosestOwnedWastePit(
                    transform.position, state.WorkerContract.WorkZoneRadius,
                    state.Npc.EmployerAccountId, out var pitId, out var pitPosition))
            {
                networkPlayer.ServerClearAutonomousInput();
                return;
            }
            if (PlanarDistance(transform.position, pitPosition) > InteractionDistance)
            {
                SetDestination(state, pitPosition);
                MoveToward(pitPosition, sprint: false);
                return;
            }
            FaceAndStop(pitPosition);
            if (!AccumulateSpecialWork(state, elapsed, 6f)
                || !inventory.TryDepositAnyWasteServer(
                    placedObjects, pitId, 2000, out var drained) || drained == 0) return;
            RegisterCompletedTask(state, WorkerJobKind.Sanitation,
                SkillId.Sanitation, 6f, 0.35f);
        }

        private void TickPatientCare(CharacterSurvivalState state, float elapsed)
        {
            PlayerSurvival patient = null;
            var radius = Mathf.Max(5f, state.WorkerContract.WorkZoneRadius);
            var closest = radius;
            foreach (var candidate in FindObjectsByType<PlayerSurvival>())
            {
                if (candidate == survival || !candidate.ServerNeedsCare) continue;
                var candidateState = candidate.ServerState;
                if (candidateState?.Npc?.Disposition == NpcDisposition.Aggressive
                    || candidateState?.Npc != null
                        && !string.IsNullOrWhiteSpace(candidateState.Npc.EmployerAccountId)
                        && candidateState.Npc.EmployerAccountId
                            != state.Npc.EmployerAccountId) continue;
                var distance = PlanarDistance(transform.position, candidate.transform.position);
                if (distance >= closest) continue;
                closest = distance;
                patient = candidate;
            }
            if (patient == null)
            {
                networkPlayer.ServerClearAutonomousInput();
                return;
            }
            if (closest > 2.8f)
            {
                SetDestination(state, patient.transform.position);
                MoveToward(patient.transform.position, sprint: false);
                return;
            }
            FaceAndStop(patient.transform.position);
            if (!AccumulateSpecialWork(state, elapsed, 6f)
                || !survival.ServerNpcProvideCare(patient)) return;
            RegisterCompletedTask(state, WorkerJobKind.PatientCare,
                SkillId.Nursing, 6f, 0.5f);
        }

        private bool AccumulateSpecialWork(
            CharacterSurvivalState state, float elapsed, float requiredSeconds)
        {
            state.Npc.Motivation = LivingWorldSimulation.CalculateWorkerMotivation(
                state.WorkerContract, FindEmployerRelationship(state), state.Physiology);
            state.Npc.WorkProgressSeconds += elapsed
                * Mathf.Lerp(0.3f, 1.2f, state.Npc.Motivation);
            if (state.Npc.WorkProgressSeconds < requiredSeconds) return false;
            state.Npc.WorkProgressSeconds -= requiredSeconds;
            return true;
        }

        private static Vector3 ResolveWorkCenter(CharacterSurvivalState state)
        {
            var center = state.WorkerContract.WorkZoneCenter;
            return center == Vector3.zero ? state.Npc.HomePosition : center;
        }

        private void CollectLooseNode(
            CharacterSurvivalState state,
            ResourceNodeView node,
            WorkerJobKind job)
        {
            if (!resources.TryCollectLoose(node, out _)) return;
            var quantity = node.Descriptor.Kind == WorldObjectKind.FiberPlant ? 2 : 1;
            var stack = ResourceBalance.IsWildFood(node.Descriptor.ResourceItemId)
                ? ResourceBalance.CreateWildFoodStack(node.Descriptor.ResourceItemId, node.InstanceId)
                : new ItemStackState(node.Descriptor.ResourceItemId, quantity);
            InsertOrDrop(stack, node.transform.position);
            RegisterCompletedTask(state, job, SkillId.Foraging, 2f, 0.2f);
        }

        private void MineNode(
            CharacterSurvivalState state,
            ResourceNodeView node,
            SkillId skill)
        {
            if (!resources.ApplyMiningHit(
                    node, out var completed, out var extractionIndex, out _)) return;
            var durability = ResourceBalance.DurabilityCost[
                Mathf.Clamp(node.Descriptor.Hardness - 1, 0, 4)];
            inventory.TryDamageServerTool(node.Descriptor.RequiredTool, durability, out _);
            var challenge = Mathf.Clamp01(node.Descriptor.Hardness / 5f);
            survival.ServerRegisterPractice(
                skill, ResourceBalance.MiningCooldownSeconds, challenge,
                completed ? 1f : 0.65f, 0f);
            survival.ServerRegisterPhysicalLoad(
                CharacterAttributeId.MuscularEndurance,
                ResourceBalance.MiningCooldownSeconds,
                challenge);
            if (!completed) return;
            var quantity = node.Descriptor.IsTree
                ? ResourceBalance.TreeMinimumYield
                    + (int)(resources.CalculateExtractionRoll(
                        node.InstanceId, extractionIndex, 7)
                        % (ulong)(ResourceBalance.TreeMaximumYield
                            - ResourceBalance.TreeMinimumYield + 1))
                : 1;
            var itemId = node.Descriptor.IsTree ? (ushort)2 : node.Descriptor.ResourceItemId;
            InsertOrDrop(new ItemStackState(
                itemId, quantity, quality: node.Descriptor.Quality), node.transform.position);
            RegisterCompletedTask(
                state, state.Npc.ActiveJob, skill,
                ResourceBalance.MiningCooldownSeconds, challenge);
        }

        private void TickAggression(PlayerSurvival target, float distance)
        {
            if (target == null || target.ServerIsDead)
            {
                networkPlayer.ServerClearAutonomousInput();
                return;
            }
            var destination = target.transform.position;
            var direction = destination - transform.position;
            direction.y = 0f;
            var yaw = direction.sqrMagnitude > 0.001f
                ? Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg
                : transform.eulerAngles.y;
            networkPlayer.ServerSetAutonomousInput(
                distance > 2.2f ? Vector2.up : Vector2.zero,
                yaw,
                sprint: distance > 5f,
                attack: distance <= 2.6f,
                block: false);
        }

        private void TickWander(CharacterSurvivalState state)
        {
            if (PlanarDistance(transform.position, state.Npc.Destination) < 1.7f
                || state.Npc.Destination == Vector3.zero)
            {
                var period = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 20;
                var sample = StableSample(state.CharacterId, period);
                var angle = (sample & 0xffffUL) / 65535f * Mathf.PI * 2f;
                var radius = 5f + ((sample >> 16) & 0xffffUL) / 65535f * 13f;
                SetDestination(state, state.Npc.HomePosition + new Vector3(
                    Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius));
            }
            MoveToward(state.Npc.Destination, sprint: false);
        }

        private void TickContract(CharacterSurvivalState state)
        {
            var contract = state.WorkerContract;
            if (contract?.Active != true || weather == null) return;
            var gameDay = (long)Math.Floor(weather.Current.GameSeconds
                / LivingWorldSimulation.GameSecondsPerDay);
            if (state.Npc.ContractEvaluationGameDay == long.MinValue)
            {
                state.Npc.ContractEvaluationGameDay = gameDay;
                return;
            }
            if (gameDay <= state.Npc.ContractEvaluationGameDay) return;
            var relationship = FindEmployerRelationship(state) ?? new RelationshipState
            {
                SourceCharacterId = state.CharacterId,
                TargetCharacterId = state.Npc.EmployerCharacterId,
            };
            if (!state.Relationships.Contains(relationship)) state.Relationships.Add(relationship);
            var safety = Mathf.Clamp01(1f - state.Physiology.Stress
                - state.Anatomy.Wounds.Count * 0.08f);
            var roll = (StableSample(state.CharacterId, gameDay) & 0xffffUL) / 65535f;
            var paymentDelivered = TryCollectContractPayment(state);
            var response = LivingWorldSimulation.EvaluateContractDay(
                contract, relationship, state.Npc.RationCaloriesCurrentDay, safety, roll,
                paymentDelivered);
            state.Npc.ContractEvaluationGameDay = gameDay;
            state.Npc.RationCaloriesCurrentDay = 0f;
            if (!state.Npc.PersonalRequestPending
                && state.Npc.LastPersonalRequestCompletedGameDay < gameDay)
            {
                state.Npc.PersonalRequest = LivingWorldSimulation.SelectPersonalRequest(
                    state.CharacterId, gameDay);
                state.Npc.PersonalRequestPending = true;
                state.Npc.PersonalRequestGameDay = gameDay;
            }
            if (contract.Voluntary && state.ControlKind == CharacterControlKind.ForcedNpc)
                state.ControlKind = CharacterControlKind.ContractedNpc;
            LivingWorldSimulation.ApplyWorkerResponse(
                state, response, DateTime.UtcNow.Ticks);
            if (response is WorkerResponseKind.Leave or WorkerResponseKind.Escape)
                state.Npc.HomePosition = transform.position;
        }

        private bool TryCollectContractPayment(CharacterSurvivalState state)
        {
            var contract = state.WorkerContract;
            if (contract?.Voluntary != true || contract.PaymentItemId == 0
                || contract.PaymentQuantity == 0) return true;
            PlayerInventory employerInventory = null;
            foreach (var candidate in FindObjectsByType<PlayerSurvival>(FindObjectsInactive.Exclude))
            {
                if (candidate == null || candidate.ServerState?.CharacterId
                        != state.Npc.EmployerCharacterId) continue;
                employerInventory = candidate.GetComponent<PlayerInventory>();
                break;
            }
            if (employerInventory == null
                || employerInventory.GetServerItemQuantity(contract.PaymentItemId)
                    < contract.PaymentQuantity) return false;
            for (var index = 0; index < contract.PaymentQuantity; index++)
            {
                if (!employerInventory.TryTransferAnyItemServer(
                        inventory, contract.PaymentItemId)) return false;
            }
            return true;
        }

        private void MoveToward(Vector3 destination, bool sprint)
        {
            var direction = destination - transform.position;
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.3f)
            {
                networkPlayer.ServerClearAutonomousInput();
                return;
            }
            var yaw = Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;
            networkPlayer.ServerSetAutonomousInput(Vector2.up, yaw, sprint);
        }

        private void FaceAndStop(Vector3 destination)
        {
            var direction = destination - transform.position;
            direction.y = 0f;
            var yaw = direction.sqrMagnitude > 0.001f
                ? Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg
                : transform.eulerAngles.y;
            networkPlayer.ServerSetAutonomousInput(Vector2.zero, yaw, false);
        }

        private void SetDestination(CharacterSurvivalState state, Vector3 destination)
        {
            var streamer = FindAnyObjectByType<WorldStreamer>();
            if (streamer != null && streamer.IsInitialized)
                destination = streamer.ClampToWorld(destination);
            state.Npc.Destination = destination;
        }

        private PlayerSurvival FindClosestLivingPlayer(out float distance)
        {
            PlayerSurvival closest = null;
            distance = float.MaxValue;
            foreach (var candidate in FindObjectsByType<PlayerSurvival>())
            {
                var candidateState = candidate.ServerState;
                if (candidate == survival || candidateState == null
                    || candidateState.ControlKind != CharacterControlKind.Player
                    || candidate.ServerIsDead)
                    continue;
                var candidateDistance = PlanarDistance(transform.position, candidate.transform.position);
                if (candidateDistance >= distance) continue;
                distance = candidateDistance;
                closest = candidate;
            }
            return closest;
        }

        private RelationshipState FindEmployerRelationship(CharacterSurvivalState state)
        {
            if (state?.Relationships == null) return null;
            foreach (var relationship in state.Relationships)
            {
                if (relationship != null
                    && relationship.TargetCharacterId == state.Npc.EmployerCharacterId)
                    return relationship;
            }
            return null;
        }

        private void RegisterCompletedTask(
            CharacterSurvivalState state,
            WorkerJobKind job,
            SkillId skill,
            float seconds,
            float challenge)
        {
            state.Npc.CompletedTasks[(int)job]++;
            state.Npc.LastWorkCompletedUtcTicks = DateTime.UtcNow.Ticks;
            survival.ServerRegisterPractice(skill, seconds, challenge, 1f, 0f);
        }

        private void InsertOrDrop(ItemStackState stack, Vector3 position)
        {
            var remainder = inventory.InsertStackServer(stack);
            if (remainder > 0)
                inventory.SpawnOverflowServer(stack.WithQuantity(remainder), position);
        }

        private void ResolveWorldServices()
        {
            resources ??= FindAnyObjectByType<ResourceWorldService>();
            placedObjects ??= FindAnyObjectByType<PlacedObjectWorldService>();
            weather ??= FindAnyObjectByType<WorldWeatherService>();
        }

        private static bool TryGetResourceSearch(
            WorkerJobKind job,
            out ResourceSearchKind search)
        {
            search = job switch
            {
                WorkerJobKind.Mining => ResourceSearchKind.Mining,
                WorkerJobKind.Logging => ResourceSearchKind.Logging,
                WorkerJobKind.Foraging => ResourceSearchKind.Forage,
                _ => default,
            };
            return job is WorkerJobKind.Mining or WorkerJobKind.Logging
                or WorkerJobKind.Foraging;
        }

        private static SkillId WorkSkill(
            WorkerJobKind job,
            ResourceNodeDescriptor descriptor)
        {
            return job switch
            {
                WorkerJobKind.Logging => SkillId.Woodcutting,
                WorkerJobKind.Foraging => SkillId.Foraging,
                WorkerJobKind.Hauling => SkillId.LoadCarrying,
                WorkerJobKind.Construction => SkillId.Construction,
                WorkerJobKind.CookingAndWater => SkillId.Cooking,
                WorkerJobKind.Sanitation => SkillId.Sanitation,
                WorkerJobKind.PatientCare => SkillId.Nursing,
                _ => descriptor.RequiredTool == ToolKind.Shovel
                    ? SkillId.Excavation : SkillId.Mining,
            };
        }

        private static float PlanarDistance(Vector3 first, Vector3 second)
        {
            var difference = second - first;
            difference.y = 0f;
            return difference.magnitude;
        }

        private static bool IsNpc(CharacterSurvivalState state) => state != null
            && state.ControlKind is CharacterControlKind.FreeNpc
                or CharacterControlKind.ContractedNpc or CharacterControlKind.ForcedNpc;

        private static ulong StableSample(string characterId, long period)
        {
            unchecked
            {
                ulong value = 1469598103934665603UL;
                foreach (var character in characterId ?? string.Empty)
                {
                    value ^= character;
                    value *= 1099511628211UL;
                }
                value ^= (ulong)period * 0x9E3779B97F4A7C15UL;
                value = (value ^ value >> 30) * 0xBF58476D1CE4E5B9UL;
                value = (value ^ value >> 27) * 0x94D049BB133111EBUL;
                return value ^ value >> 31;
            }
        }
    }
}
