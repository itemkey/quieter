using System.Collections.Generic;
using Quieter.Player;
using Unity.Netcode;
using UnityEngine;

namespace Quieter.World
{
    public sealed class WorldStreamer : MonoBehaviour
    {
        private const int StreamRadius = 3;
        private const int UnloadRadius = 4;
        private readonly Dictionary<ChunkCoord, WorldChunkView> activeChunks = new();
        private readonly HashSet<ChunkCoord> desiredChunks = new();
        private readonly HashSet<ChunkCoord> retainedChunks = new();
        private readonly List<ChunkCoord> removalBuffer = new();
        private readonly List<ChunkCoord> focusChunks = new();
        private readonly List<ChunkCoord> creationBuffer = new();
        private readonly Queue<ChunkCoord> creationQueue = new();
        private readonly Queue<ChunkCoord> removalQueue = new();

        private WorldDefinition definition;
        private IChunkGenerator generator;
        private WorldObjectCatalog catalog;
        private bool initialized;
        private bool serverMode;
        private bool renderVisuals;
        private float nextRefreshAt;

        public WorldDefinition Definition => definition;
        public bool IsInitialized => initialized;
        public int ActiveChunkCount => activeChunks.Count;

        public void Initialize(
            WorldDefinition newDefinition,
            WorldObjectCatalog newCatalog,
            bool isServer,
            bool shouldRenderVisuals)
        {
            if (initialized
                && definition.Equals(newDefinition)
                && serverMode == isServer
                && renderVisuals == shouldRenderVisuals)
            {
                return;
            }

            Clear();
            definition = newDefinition;
            catalog = newCatalog;
            serverMode = isServer;
            renderVisuals = shouldRenderVisuals;
            generator = new DeterministicChunkGenerator();
            initialized = true;
            Refresh(forceSpawnChunk: true);
            Debug.Log(
                $"[Quieter] Мир загружен: {activeChunks.Count} чанков, "
                + $"визуализация {(renderVisuals ? "включена" : "отключена")}.");
        }

        public float SampleHeight(float worldX, float worldZ)
        {
            return initialized ? generator.SampleHeight(definition, worldX, worldZ) : 6f;
        }

        public Vector3 ClampToWorld(Vector3 position)
        {
            var minimum = definition.WorldMinimum;
            var maximum = definition.WorldMaximum;
            position.x = Mathf.Clamp(position.x, minimum.x + 1f, maximum.x - 1f);
            position.z = Mathf.Clamp(position.z, minimum.z + 1f, maximum.z - 1f);
            return position;
        }

        public void EnsureLoadedAround(Vector3 worldPosition)
        {
            if (!initialized)
            {
                return;
            }

            AddFocus(worldPosition);
            foreach (var coordinate in desiredChunks)
            {
                if (!activeChunks.ContainsKey(coordinate))
                {
                    CreateChunk(coordinate);
                }
            }
        }

        private void Update()
        {
            if (!initialized)
            {
                return;
            }

            if (Time.unscaledTime >= nextRefreshAt)
            {
                Refresh(forceSpawnChunk: false);
            }

            ProcessOneQueuedChange();
        }

        private void Refresh(bool forceSpawnChunk)
        {
            nextRefreshAt = Time.unscaledTime + 0.75f;
            desiredChunks.Clear();
            retainedChunks.Clear();
            focusChunks.Clear();
            var foundFocus = false;

            if (NetworkManager.Singleton != null)
            {
                if (serverMode && NetworkManager.Singleton.IsServer)
                {
                    foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
                    {
                        if (client.PlayerObject != null)
                        {
                            AddFocus(client.PlayerObject.transform.position);
                            foundFocus = true;
                        }
                    }
                }
                else if (NetworkManager.Singleton.IsClient
                         && NetworkManager.Singleton.LocalClient?.PlayerObject != null)
                {
                    AddFocus(NetworkManager.Singleton.LocalClient.PlayerObject.transform.position);
                    foundFocus = true;
                }
            }

            if (!foundFocus && forceSpawnChunk)
            {
                AddFocus(Vector3.zero);
            }
            else if (!foundFocus)
            {
                creationQueue.Clear();
                removalQueue.Clear();
                return;
            }

            creationQueue.Clear();
            removalQueue.Clear();
            creationBuffer.Clear();
            foreach (var coordinate in desiredChunks)
            {
                if (!activeChunks.ContainsKey(coordinate))
                {
                    creationBuffer.Add(coordinate);
                }
            }

            creationBuffer.Sort((left, right) =>
                DistanceToNearestFocus(left).CompareTo(DistanceToNearestFocus(right)));
            foreach (var coordinate in creationBuffer)
            {
                if (forceSpawnChunk)
                {
                    CreateChunk(coordinate);
                }
                else
                {
                    creationQueue.Enqueue(coordinate);
                }
            }

            removalBuffer.Clear();
            foreach (var pair in activeChunks)
            {
                if (!retainedChunks.Contains(pair.Key))
                {
                    removalBuffer.Add(pair.Key);
                }
            }

            removalBuffer.Sort((left, right) =>
                DistanceToNearestFocus(right).CompareTo(DistanceToNearestFocus(left)));
            foreach (var coordinate in removalBuffer)
            {
                removalQueue.Enqueue(coordinate);
            }
        }

        private void AddFocus(Vector3 worldPosition)
        {
            var minimum = definition.WorldMinimum;
            var centerX = Mathf.FloorToInt((worldPosition.x - minimum.x) / definition.ChunkSize);
            var centerZ = Mathf.FloorToInt((worldPosition.z - minimum.z) / definition.ChunkSize);
            var center = new ChunkCoord(centerX, centerZ);
            focusChunks.Add(center);
            AddRadius(center, StreamRadius, desiredChunks);
            AddRadius(center, UnloadRadius, retainedChunks);
        }

        private void AddRadius(ChunkCoord center, int radius, HashSet<ChunkCoord> destination)
        {
            for (var z = -radius; z <= radius; z++)
            {
                for (var x = -radius; x <= radius; x++)
                {
                    var coordinate = new ChunkCoord(center.X + x, center.Z + z);
                    if (definition.Contains(coordinate))
                    {
                        destination.Add(coordinate);
                    }
                }
            }
        }

        private int DistanceToNearestFocus(ChunkCoord coordinate)
        {
            var closest = int.MaxValue;
            foreach (var focus in focusChunks)
            {
                var deltaX = coordinate.X - focus.X;
                var deltaZ = coordinate.Z - focus.Z;
                closest = Mathf.Min(closest, deltaX * deltaX + deltaZ * deltaZ);
            }

            return closest;
        }

        private void ProcessOneQueuedChange()
        {
            while (creationQueue.Count > 0)
            {
                var coordinate = creationQueue.Dequeue();
                if (desiredChunks.Contains(coordinate) && !activeChunks.ContainsKey(coordinate))
                {
                    CreateChunk(coordinate);
                    return;
                }
            }

            while (removalQueue.Count > 0)
            {
                var coordinate = removalQueue.Dequeue();
                if (retainedChunks.Contains(coordinate)
                    || !activeChunks.Remove(coordinate, out var chunk))
                {
                    continue;
                }

                Destroy(chunk.gameObject);
                return;
            }
        }

        private void CreateChunk(ChunkCoord coordinate)
        {
            var child = new GameObject($"Chunk_{coordinate.X}_{coordinate.Z}");
            child.transform.SetParent(transform, false);
            var view = child.AddComponent<WorldChunkView>();
            view.Build(definition, generator.Generate(definition, coordinate), catalog, renderVisuals);
            activeChunks.Add(coordinate, view);
        }

        private void Clear()
        {
            foreach (var chunk in activeChunks.Values)
            {
                if (chunk != null)
                {
                    Destroy(chunk.gameObject);
                }
            }

            activeChunks.Clear();
            creationQueue.Clear();
            removalQueue.Clear();
        }

        private void OnDestroy() => Clear();
    }
}
