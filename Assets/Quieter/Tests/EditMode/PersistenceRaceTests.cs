using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Quieter.Inventory;
using Quieter.Networking;
using Quieter.Persistence;
using Quieter.Player;
using Quieter.World;
using Quieter.Survival;
using Unity.Netcode;
using UnityEngine;

namespace Quieter.Tests.EditMode
{
    public sealed class PersistenceRaceTests
    {
        private readonly List<GameObject> objects = new();

        [TearDown]
        public void TearDown()
        {
            foreach (var instance in objects)
            {
                if (instance != null) UnityEngine.Object.DestroyImmediate(instance);
            }
            objects.Clear();
        }

        [Test]
        public async Task ResourceFlush_WaitsForActiveSaveAndPersistsNewestState()
        {
            var repository = new DelayedWorldRepository();
            var serviceObject = new GameObject("Resource persistence race test");
            objects.Add(serviceObject);
            var service = serviceObject.AddComponent<ResourceWorldService>();
            var definition = WorldDefinition.CreateDefault(445566);
            await service.InitializeServerAsync(definition, repository, CancellationToken.None);

            const ulong instanceId = 778899;
            var states = GetField<Dictionary<ulong, ResourceNodeRuntimeState>>(
                service, "states");
            var dirty = GetField<HashSet<ulong>>(service, "dirty");
            states[instanceId] = new ResourceNodeRuntimeState(instanceId, 10);
            dirty.Add(instanceId);

            repository.DelayFirstResourceSave();
            var firstFlush = service.FlushAsync(CancellationToken.None);
            await AwaitStarted(repository.FirstResourceSaveStarted);
            states[instanceId] = new ResourceNodeRuntimeState(instanceId, 9);
            dirty.Add(instanceId);
            var secondFlush = service.FlushAsync(CancellationToken.None);
            repository.ReleaseFirstResourceSave();
            await Task.WhenAll(firstFlush, secondFlush);

            Assert.That(repository.ResourceSaves, Has.Count.EqualTo(2));
            Assert.That(repository.ResourceSaves[0].Single.RemainingReserves, Is.EqualTo(10));
            Assert.That(repository.ResourceSaves[1].Single.RemainingReserves, Is.EqualTo(9));
            Assert.That(dirty, Is.Empty);
        }

        [Test]
        public async Task PlacedObjectFlush_KeepsRevisionDirtyWhenTableChangesDuringSave()
        {
            const ulong objectId = 112233;
            var repository = new DelayedWorldRepository
            {
                StoredPlacedObjects = new[]
                {
                    new StoredPlacedObject
                    {
                        WorldId = 1,
                        ObjectId = objectId.ToString(),
                        ItemId = ResourceBalance.ResearchTableItemId,
                        X = 4f,
                        Y = 2f,
                        Z = -3f,
                        Yaw = 15f,
                    },
                },
            };
            var serviceObject = new GameObject("Placed object persistence race test");
            objects.Add(serviceObject);
            var service = serviceObject.AddComponent<PlacedObjectWorldService>();
            await service.InitializeServerAsync(
                WorldDefinition.CreateDefault(889900), repository, CancellationToken.None);

            InvokeMarkDirty(service, objectId);
            repository.DelayFirstPlacedObjectSave();
            var firstFlush = service.FlushAsync(CancellationToken.None);
            await AwaitStarted(repository.FirstPlacedObjectSaveStarted);

            var runtimeObjects = GetField<IDictionary>(service, "objects");
            var runtime = runtimeObjects[objectId];
            var sample = new ItemStackState(
                ResourceBalance.UnknownSampleItemId,
                1,
                hiddenItemId: 17,
                sourceNodeId: 9988,
                revealAtPercent: 50,
                sampleId: 776655);
            runtime.GetType().GetField("Input", BindingFlags.Instance | BindingFlags.Public)
                ?.SetValue(runtime, sample);
            InvokeMarkDirty(service, objectId);
            var secondFlush = service.FlushAsync(CancellationToken.None);
            repository.ReleaseFirstPlacedObjectSave();
            await Task.WhenAll(firstFlush, secondFlush);

            Assert.That(repository.PlacedObjectSaves, Has.Count.EqualTo(2));
            Assert.That(repository.PlacedObjectSaves[0].Single.Input, Is.Null);
            Assert.That(repository.PlacedObjectSaves[1].Single.Input, Is.Not.Null);
            Assert.That(repository.PlacedObjectSaves[1].Single.Input.SampleId,
                Is.EqualTo(sample.SampleId.ToString()));
            Assert.That(GetField<HashSet<ulong>>(service, "dirtyObjects"), Is.Empty);
        }

        [Test]
        public async Task ResearchTable_AllowsOneAtomicAttemptAndPreservesCancelledSample()
        {
            const ulong objectId = 445577;
            var sample = new StoredInventorySlot
            {
                ItemId = ResourceBalance.UnknownSampleItemId,
                Quantity = 1,
                HiddenItemId = 17,
                SourceNodeId = "998877",
                RevealAtPercent = 50,
                SampleId = "665544",
            };
            var repository = new DelayedWorldRepository
            {
                StoredPlacedObjects = new[]
                {
                    new StoredPlacedObject
                    {
                        WorldId = 1,
                        ObjectId = objectId.ToString(),
                        ItemId = ResourceBalance.ResearchTableItemId,
                        Input = sample,
                    },
                },
            };
            var networkObject = new GameObject("Research table server role");
            objects.Add(networkObject);
            var manager = networkObject.AddComponent<NetworkManager>();
            SetNetworkServerRole(manager);
            var serviceObject = new GameObject("Research table atomic attempt test");
            objects.Add(serviceObject);
            var service = serviceObject.AddComponent<PlacedObjectWorldService>();
            service.Configure(manager);
            await service.InitializeServerAsync(
                WorldDefinition.CreateDefault(334455), repository, CancellationToken.None);

            Assert.That(service.TryBeginResearch(
                objectId, 10, out var firstAttempt, out _), Is.True);
            Assert.That(service.TryBeginResearch(objectId, 20, out _, out _), Is.False,
                "A second player must not start while the table is locked.");
            Assert.That(service.TryCompleteResearch(
                objectId, 10, firstAttempt.SampleId + 1, out _), Is.False,
                "A stale sample id must never consume the input.");
            service.CancelResearch(objectId, 10);
            Assert.That(service.TryGetState(objectId, out _, out var afterCancel, out var busy),
                Is.True);
            Assert.That(busy, Is.False);
            Assert.That(afterCancel.SampleId, Is.EqualTo(firstAttempt.SampleId));

            Assert.That(service.TryBeginResearch(
                objectId, 20, out var secondAttempt, out _), Is.True);
            Assert.That(service.TryCompleteResearch(
                objectId, 20, secondAttempt.SampleId, out var consumed), Is.True);
            Assert.That(consumed.SampleId, Is.EqualTo(secondAttempt.SampleId));
            Assert.That(service.TryGetState(objectId, out _, out var afterCompletion, out busy),
                Is.True);
            Assert.That(busy, Is.False);
            Assert.That(afterCompletion.IsEmpty, Is.True);
        }

        [Test]
        public async Task SurvivalStructures_PersistWasteAndExpiredFuelWithoutTableAccess()
        {
            var repository = new DelayedWorldRepository
            {
                StoredPlacedObjects = new[]
                {
                    new StoredPlacedObject
                    {
                        WorldId = 1, ObjectId = "901", ItemId = SurvivalStructureRules.UnlinedWastePitItemId,
                        X = 0f, Y = 20f, Z = 0f,
                    },
                    new StoredPlacedObject
                    {
                        WorldId = 1, ObjectId = "902", ItemId = SurvivalStructureRules.HearthItemId,
                        X = 100f, Y = 20f, Z = 0f,
                        UpdatedAtUtc = DateTime.UtcNow.AddHours(-1).ToString("O"),
                        Input = new StoredInventorySlot { ItemId = 2, Quantity = 1, Condition = 10000 },
                    },
                },
            };
            var managerObject = new GameObject("Sanitation server role");
            objects.Add(managerObject);
            var manager = managerObject.AddComponent<NetworkManager>();
            SetNetworkServerRole(manager);
            var serviceObject = new GameObject("Sanitation persistence");
            objects.Add(serviceObject);
            var service = serviceObject.AddComponent<PlacedObjectWorldService>();
            service.Configure(manager);
            await service.InitializeServerAsync(
                WorldDefinition.CreateDefault(443322), repository, CancellationToken.None);
            Assert.That(service.TryGetBurningHearth(902, out _), Is.False);
            Assert.That(service.TryDepositWaste(901, 59000, 0.9f, 0.2f), Is.True);
            Assert.That(service.TryDepositWaste(901, 2000, 1f, 1f), Is.False);
            Assert.That(service.TryGetWastePitSpace(901, out _, out var available), Is.True);
            Assert.That(available, Is.EqualTo(1000));
            Assert.That(service.TryTakeInput(901, out _), Is.False);
            Assert.That(service.TryDismantle(902, out _), Is.False);
            Assert.That(service.TryInsertInput(902, new ItemStackState(
                6, 1, hiddenItemId: 7, sourceNodeId: 1, sampleId: 2)), Is.False);
            var biological = 0f;
            var toxins = 0f;
            service.ApplyWasteContamination(new Vector3(10f, 10f, 0f), 1f, ref biological, ref toxins);
            Assert.That(biological, Is.GreaterThan(0f));
            Assert.That(toxins, Is.GreaterThan(0f));
            await service.FlushAsync(CancellationToken.None);
            var saved = repository.PlacedObjectSaves.Last().Entries;
            Assert.That(saved.Single(s => s.ObjectId == "901").Input.LiquidMilliliters, Is.EqualTo(59000));
            Assert.That(saved.Single(s => s.ObjectId == "902").Input, Is.Null);
            Assert.That(GetField<HashSet<ulong>>(service, "dirtyObjects"), Is.Empty);
        }

        [Test]
        public async Task SanitaryDrainAndWaterStorage_MoveRealPersistentLiquids()
        {
            const string owner = "76561198012345678";
            var repository = new DelayedWorldRepository
            {
                StoredPlacedObjects = new[]
                {
                    new StoredPlacedObject
                    {
                        WorldId = 1, ObjectId = "910", OwnerAccountId = owner,
                        ItemId = SurvivalStructureRules.LatrineItemId,
                        X = 0f, Y = 20f, Z = 0f,
                    },
                    new StoredPlacedObject
                    {
                        WorldId = 1, ObjectId = "911", OwnerAccountId = owner,
                        ItemId = SurvivalStructureRules.DrainItemId,
                        X = 2f, Y = 19.8f, Z = 0f,
                    },
                    new StoredPlacedObject
                    {
                        WorldId = 1, ObjectId = "912", OwnerAccountId = owner,
                        ItemId = SurvivalStructureRules.LinedWastePitItemId,
                        X = 4.5f, Y = 19.4f, Z = 0f,
                    },
                    new StoredPlacedObject
                    {
                        WorldId = 1, ObjectId = "913", OwnerAccountId = owner,
                        ItemId = SurvivalStructureRules.BarrelItemId,
                        X = 8f, Y = 20f, Z = 0f,
                    },
                },
            };
            var managerObject = new GameObject("Sanitary network server role");
            objects.Add(managerObject);
            var manager = managerObject.AddComponent<NetworkManager>();
            SetNetworkServerRole(manager);
            var serviceObject = new GameObject("Sanitary network service");
            objects.Add(serviceObject);
            var service = serviceObject.AddComponent<PlacedObjectWorldService>();
            service.Configure(manager);
            await service.InitializeServerAsync(
                WorldDefinition.CreateDefault(223344), repository, CancellationToken.None);

            Assert.That(service.TryRouteSanitaryWaste(
                910, 550, 1f, 0.2f, out var pitId, out var error), Is.True, error);
            Assert.That(pitId, Is.EqualTo(912));
            Assert.That(service.TryGetWastePitSpace(912, out _, out var pitSpace), Is.True);
            Assert.That(pitSpace, Is.EqualTo(59450));

            Assert.That(service.TryStoreWater(913, 5000, 0.2f, 0.1f), Is.True);
            var received = 0;
            Assert.That(service.TryDrawStoredWater(913, 1200,
                (amount, biological, toxins) =>
                {
                    received = amount;
                    Assert.That(biological, Is.EqualTo(0.2f).Within(0.001f));
                    Assert.That(toxins, Is.EqualTo(0.1f).Within(0.001f));
                    return true;
                }, out var transferred), Is.True);
            Assert.That(received, Is.EqualTo(1200));
            Assert.That(transferred, Is.EqualTo(1200));
            Assert.That(service.TryGetWaterStorage(
                913, out _, out var stored, out var available), Is.True);
            Assert.That(stored, Is.EqualTo(3800));
            Assert.That(available, Is.EqualTo(56200));
        }

        [Test]
        public async Task PlayerSave_WaitsForActiveWriteThenCapturesFreshestState()
        {
            const ulong steamId = 76561198012345678;
            var repository = new DelayedProfileRepository();
            var coordinatorObject = new GameObject("Serialized player save test");
            objects.Add(coordinatorObject);
            var coordinator = coordinatorObject.AddComponent<SessionCoordinator>();
            SetField(coordinator, "playerRepository", repository);

            var playerObject = new GameObject("Serialized player save subject");
            objects.Add(playerObject);
            var player = playerObject.AddComponent<NetworkPlayer>();
            player.transform.position = new Vector3(1f, 2f, 3f);
            var clientType = typeof(SessionCoordinator).GetNestedType(
                "AuthenticatedClient", BindingFlags.NonPublic);
            Assert.That(clientType, Is.Not.Null);
            var client = Activator.CreateInstance(clientType);
            SetField(client, "SteamId", steamId);
            SetField(client, "Player", player);

            repository.DelayFirstPositionSave();
            var firstSave = InvokePlayerSave(coordinator, client);
            await AwaitStarted(repository.FirstPositionSaveStarted);
            player.transform.position = new Vector3(4f, 5f, 6f);
            var queuedSave = InvokePlayerSave(coordinator, client);
            player.transform.position = new Vector3(7f, 8f, 9f);
            repository.ReleaseFirstPositionSave();
            await Task.WhenAll(firstSave, queuedSave);

            Assert.That(repository.SavedPositions, Has.Count.EqualTo(2));
            Assert.That(repository.SavedPositions[0], Is.EqualTo(new Vector3(1f, 2f, 3f)));
            Assert.That(repository.SavedPositions[1], Is.EqualTo(new Vector3(7f, 8f, 9f)),
                "The queued save must take its snapshot after the active write finishes.");
            Assert.That(repository.InventorySaveCount, Is.EqualTo(2));
        }

        [Test]
        public async Task KnowledgeAndNotes_KeepChangesMadeDuringDelayedWrite()
        {
            const ulong steamId = 76561198012345679;
            const ulong nodeId = 991122;
            const ulong noteId = 334455;
            var repository = new DelayedProfileRepository();
            var interactionObject = new GameObject("Knowledge persistence race test");
            objects.Add(interactionObject);
            var interaction = interactionObject.AddComponent<PlayerResourceInteraction>();
            SetField(interaction, "repository", repository);
            SetField(interaction, "steamId", steamId);
            SetField(interaction, "characterId", Guid.NewGuid().ToString("D"));
            SetField(interaction, "worldId", 1);
            var knowledge = GetField<Dictionary<ulong, ushort>>(
                interaction, "serverKnowledge");
            var discovered = GetField<Dictionary<ulong, DateTime>>(
                interaction, "discoveredAt");
            var notes = GetField<Dictionary<ulong, MapNoteNetworkState>>(
                interaction, "serverMapNotes");
            var created = GetField<Dictionary<ulong, DateTime>>(
                interaction, "noteCreatedAt");
            var updated = GetField<Dictionary<ulong, DateTime>>(
                interaction, "noteUpdatedAt");
            knowledge[nodeId] = 1000;
            discovered[nodeId] = DateTime.UtcNow;
            notes[noteId] = new MapNoteNetworkState(
                7001, noteId, new Vector2(1f, 2f), "Первая");
            created[noteId] = DateTime.UtcNow;
            updated[noteId] = DateTime.UtcNow;
            SetField(interaction, "knowledgeDirty", true);
            SetField(interaction, "notesDirty", true);
            SetField(interaction, "knowledgeRevision", (uint)1);
            SetField(interaction, "noteRevision", (uint)1);

            repository.DelayFirstKnowledgeSave();
            var firstFlush = InvokeKnowledgeFlush(interaction);
            await AwaitStarted(repository.FirstKnowledgeSaveStarted);
            knowledge[nodeId] = 4200;
            notes[noteId] = new MapNoteNetworkState(
                7001, noteId, new Vector2(7f, 8f), "Новая");
            updated[noteId] = DateTime.UtcNow;
            SetField(interaction, "knowledgeDirty", true);
            SetField(interaction, "notesDirty", true);
            SetField(interaction, "knowledgeRevision", (uint)2);
            SetField(interaction, "noteRevision", (uint)2);
            var secondFlush = InvokeKnowledgeFlush(interaction);
            repository.ReleaseFirstKnowledgeSave();
            await Task.WhenAll(firstFlush, secondFlush);

            Assert.That(repository.KnowledgeSaves, Has.Count.EqualTo(2));
            Assert.That(repository.KnowledgeSaves[0].Single.StudyBasisPoints, Is.EqualTo(1000));
            Assert.That(repository.KnowledgeSaves[1].Single.StudyBasisPoints, Is.EqualTo(4200));
            Assert.That(repository.NoteSaves, Has.Count.EqualTo(2));
            Assert.That(repository.NoteSaves[0].Single.Text, Is.EqualTo("Первая"));
            Assert.That(repository.NoteSaves[1].Single.Text, Is.EqualTo("Новая"));
            Assert.That(GetField<bool>(interaction, "knowledgeDirty"), Is.False);
            Assert.That(GetField<bool>(interaction, "notesDirty"), Is.False);
        }

        private static void InvokeMarkDirty(PlacedObjectWorldService service, ulong objectId)
        {
            var method = typeof(PlacedObjectWorldService).GetMethod(
                "MarkDirty",
                BindingFlags.Instance | BindingFlags.NonPublic,
                null,
                new[] { typeof(ulong), typeof(bool) },
                null);
            Assert.That(method, Is.Not.Null);
            method.Invoke(service, new object[] { objectId, false });
        }

        private static T GetField<T>(object target, string name)
        {
            var field = target.GetType().GetField(
                name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, $"Missing field {name}.");
            return (T)field.GetValue(target);
        }

        private static void SetField(object target, string name, object value)
        {
            var field = target.GetType().GetField(
                name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, $"Missing field {name}.");
            field.SetValue(target, value);
        }

        private static Task InvokePlayerSave(SessionCoordinator coordinator, object client)
        {
            var method = typeof(SessionCoordinator).GetMethod(
                "SavePlayerStateAsync", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);
            return (Task)method.Invoke(
                coordinator, new[] { client, CancellationToken.None, (object)false });
        }

        private static Task InvokeKnowledgeFlush(PlayerResourceInteraction interaction)
        {
            var method = typeof(PlayerResourceInteraction).GetMethod(
                "FlushKnowledgeCoreAsync", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);
            return (Task)method.Invoke(interaction, new object[] { CancellationToken.None });
        }

        private static void SetNetworkServerRole(NetworkManager manager)
        {
            var property = manager.LocalClient.GetType().GetProperty(
                "IsServer", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.That(property, Is.Not.Null);
            property.SetValue(manager.LocalClient, true);
            Assert.That(manager.IsServer, Is.True);
        }

        private static async Task AwaitStarted(Task started)
        {
            var timeout = Task.Delay(TimeSpan.FromSeconds(3));
            Assert.That(await Task.WhenAny(started, timeout), Is.SameAs(started),
                "The delayed repository save did not start.");
            await started;
        }

        private sealed class DelayedWorldRepository : IWorldRepository
        {
            private TaskCompletionSource<bool> firstResourceRelease;
            private TaskCompletionSource<bool> firstPlacedRelease;
            private readonly TaskCompletionSource<bool> firstResourceStarted = NewSignal();
            private readonly TaskCompletionSource<bool> firstPlacedStarted = NewSignal();

            public IReadOnlyList<StoredPlacedObject> StoredPlacedObjects { get; set; } =
                Array.Empty<StoredPlacedObject>();
            public List<SavedResourceBatch> ResourceSaves { get; } = new();
            public List<SavedPlacedObjectBatch> PlacedObjectSaves { get; } = new();
            public Task FirstResourceSaveStarted => firstResourceStarted.Task;
            public Task FirstPlacedObjectSaveStarted => firstPlacedStarted.Task;

            public void DelayFirstResourceSave() => firstResourceRelease = NewSignal();
            public void DelayFirstPlacedObjectSave() => firstPlacedRelease = NewSignal();
            public void ReleaseFirstResourceSave() => firstResourceRelease?.TrySetResult(true);
            public void ReleaseFirstPlacedObjectSave() => firstPlacedRelease?.TrySetResult(true);

            public Task<WorldDefinition> GetOrCreateWorldAsync(
                CancellationToken cancellationToken = default) =>
                Task.FromResult(WorldDefinition.CreateDefault(1));

            public Task<IReadOnlyList<StoredResourceNodeState>> LoadResourceNodeStatesAsync(
                int worldId,
                CancellationToken cancellationToken = default) =>
                Task.FromResult<IReadOnlyList<StoredResourceNodeState>>(
                    Array.Empty<StoredResourceNodeState>());

            public async Task SaveResourceNodeStatesAsync(
                int worldId,
                IReadOnlyList<StoredResourceNodeState> states,
                CancellationToken cancellationToken = default)
            {
                var copy = new List<StoredResourceNodeState>();
                foreach (var state in states)
                {
                    copy.Add(new StoredResourceNodeState
                    {
                        WorldId = state.WorldId,
                        InstanceId = state.InstanceId,
                        RemainingReserves = state.RemainingReserves,
                        AvailableAtUtc = state.AvailableAtUtc,
                    });
                }
                ResourceSaves.Add(new SavedResourceBatch(copy));
                if (ResourceSaves.Count == 1 && firstResourceRelease != null)
                {
                    firstResourceStarted.TrySetResult(true);
                    await firstResourceRelease.Task;
                }
            }

            public Task<IReadOnlyList<StoredPlacedObject>> LoadPlacedObjectsAsync(
                int worldId,
                CancellationToken cancellationToken = default) =>
                Task.FromResult(StoredPlacedObjects);

            public async Task SavePlacedObjectsAsync(
                int worldId,
                IReadOnlyList<StoredPlacedObject> placedObjects,
                CancellationToken cancellationToken = default)
            {
                var copy = new List<StoredPlacedObject>();
                foreach (var entry in placedObjects)
                {
                    copy.Add(new StoredPlacedObject
                    {
                        WorldId = entry.WorldId,
                        ObjectId = entry.ObjectId,
                        ItemId = entry.ItemId,
                        X = entry.X,
                        Y = entry.Y,
                        Z = entry.Z,
                        Yaw = entry.Yaw,
                        Input = entry.Input == null ? null : new StoredInventorySlot
                        {
                            ItemId = entry.Input.ItemId,
                            Quantity = entry.Input.Quantity,
                            Condition = entry.Input.Condition,
                            LiquidMilliliters = entry.Input.LiquidMilliliters,
                            LiquidKind = entry.Input.LiquidKind,
                            BiologicalContamination = entry.Input.BiologicalContamination,
                            ToxinContamination = entry.Input.ToxinContamination,
                            Cleanliness = entry.Input.Cleanliness,
                            HiddenItemId = entry.Input.HiddenItemId,
                            SourceNodeId = entry.Input.SourceNodeId,
                            RevealAtPercent = entry.Input.RevealAtPercent,
                            SampleId = entry.Input.SampleId,
                        },
                    });
                }
                PlacedObjectSaves.Add(new SavedPlacedObjectBatch(copy));
                if (PlacedObjectSaves.Count == 1 && firstPlacedRelease != null)
                {
                    firstPlacedStarted.TrySetResult(true);
                    await firstPlacedRelease.Task;
                }
            }

            private static TaskCompletionSource<bool> NewSignal() => new(
                TaskCreationOptions.RunContinuationsAsynchronously);
        }

        private sealed class DelayedProfileRepository : IPlayerProfileRepository
        {
            private TaskCompletionSource<bool> firstPositionRelease;
            private TaskCompletionSource<bool> firstKnowledgeRelease;
            private readonly TaskCompletionSource<bool> firstPositionStarted = NewSignal();
            private readonly TaskCompletionSource<bool> firstKnowledgeStarted = NewSignal();

            public List<Vector3> SavedPositions { get; } = new();
            public List<SavedKnowledgeBatch> KnowledgeSaves { get; } = new();
            public List<SavedNoteBatch> NoteSaves { get; } = new();
            public int InventorySaveCount { get; private set; }
            public Task FirstPositionSaveStarted => firstPositionStarted.Task;
            public Task FirstKnowledgeSaveStarted => firstKnowledgeStarted.Task;

            public void DelayFirstPositionSave() => firstPositionRelease = NewSignal();
            public void DelayFirstKnowledgeSave() => firstKnowledgeRelease = NewSignal();
            public void ReleaseFirstPositionSave() => firstPositionRelease?.TrySetResult(true);
            public void ReleaseFirstKnowledgeSave() => firstKnowledgeRelease?.TrySetResult(true);

            public Task<PlayerProfile> LoginAsync(
                ulong steamId,
                string displayName,
                Vector3 defaultSpawn,
                CancellationToken cancellationToken = default) =>
                Task.FromResult(new PlayerProfile
                {
                    SteamId = steamId,
                    DisplayName = displayName,
                    Position = defaultSpawn,
                });

            public async Task SavePositionAsync(
                ulong steamId,
                Vector3 position,
                CancellationToken cancellationToken = default)
            {
                SavedPositions.Add(position);
                if (SavedPositions.Count == 1 && firstPositionRelease != null)
                {
                    firstPositionStarted.TrySetResult(true);
                    await firstPositionRelease.Task;
                }
            }

            public Task SaveInventoryAsync(
                ulong steamId,
                IReadOnlyList<StoredInventorySlot> slots,
                IReadOnlyList<StoredInventorySlot> pendingItems,
                byte selectedHotbarIndex,
                CancellationToken cancellationToken = default)
            {
                InventorySaveCount++;
                return Task.CompletedTask;
            }

            public async Task SaveDepositKnowledgeAsync(
                ulong steamId,
                string characterId,
                int worldId,
                IReadOnlyList<StoredDepositKnowledge> knowledge,
                CancellationToken cancellationToken = default)
            {
                var copy = new List<StoredDepositKnowledge>();
                foreach (var entry in knowledge)
                {
                    copy.Add(new StoredDepositKnowledge
                    {
                        WorldId = entry.WorldId,
                        InstanceId = entry.InstanceId,
                        StudyBasisPoints = entry.StudyBasisPoints,
                        DiscoveredAtUtc = entry.DiscoveredAtUtc,
                    });
                }
                KnowledgeSaves.Add(new SavedKnowledgeBatch(copy));
                if (KnowledgeSaves.Count == 1 && firstKnowledgeRelease != null)
                {
                    firstKnowledgeStarted.TrySetResult(true);
                    await firstKnowledgeRelease.Task;
                }
            }

            public Task SaveMapNotesAsync(
                ulong steamId,
                int worldId,
                IReadOnlyList<StoredMapNote> notes,
                CancellationToken cancellationToken = default)
            {
                var copy = new List<StoredMapNote>();
                foreach (var entry in notes)
                {
                    copy.Add(new StoredMapNote
                    {
                        WorldId = entry.WorldId,
                        NoteId = entry.NoteId,
                        X = entry.X,
                        Z = entry.Z,
                        Text = entry.Text,
                        CreatedAtUtc = entry.CreatedAtUtc,
                        UpdatedAtUtc = entry.UpdatedAtUtc,
                    });
                }
                NoteSaves.Add(new SavedNoteBatch(copy));
                return Task.CompletedTask;
            }

            public Task SaveSurvivalAsync(
                ulong steamId,
                CharacterSurvivalState survival,
                CancellationToken cancellationToken = default)
                => Task.CompletedTask;

            private static TaskCompletionSource<bool> NewSignal() => new(
                TaskCreationOptions.RunContinuationsAsynchronously);
        }

        private sealed class SavedResourceBatch
        {
            private readonly IReadOnlyList<StoredResourceNodeState> entries;
            public SavedResourceBatch(IReadOnlyList<StoredResourceNodeState> values) =>
                entries = values;
            public StoredResourceNodeState Single => entries.Count == 1 ? entries[0] : null;
        }

        private sealed class SavedPlacedObjectBatch
        {
            private readonly IReadOnlyList<StoredPlacedObject> entries;
            public IReadOnlyList<StoredPlacedObject> Entries => entries;
            public SavedPlacedObjectBatch(IReadOnlyList<StoredPlacedObject> values) =>
                entries = values;
            public StoredPlacedObject Single => entries.Count == 1 ? entries[0] : null;
        }

        private sealed class SavedKnowledgeBatch
        {
            private readonly IReadOnlyList<StoredDepositKnowledge> entries;
            public SavedKnowledgeBatch(IReadOnlyList<StoredDepositKnowledge> values) =>
                entries = values;
            public StoredDepositKnowledge Single => entries.Count == 1 ? entries[0] : null;
        }

        private sealed class SavedNoteBatch
        {
            private readonly IReadOnlyList<StoredMapNote> entries;
            public SavedNoteBatch(IReadOnlyList<StoredMapNote> values) => entries = values;
            public StoredMapNote Single => entries.Count == 1 ? entries[0] : null;
        }
    }
}
