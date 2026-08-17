using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace Quieter.World
{
    [Serializable]
    public struct WorldDefinition : INetworkSerializable, IEquatable<WorldDefinition>
    {
        public int WorldId;
        public long Seed;
        public ushort GeneratorVersion;
        public ushort ChunkCountX;
        public ushort ChunkCountZ;
        public ushort ChunkSize;
        public ushort SamplesPerSide;
        public float HeightStep;

        public static WorldDefinition CreateDefault(long seed)
        {
            return new WorldDefinition
            {
                WorldId = 1,
                Seed = seed,
                GeneratorVersion = Core.QuieterConstants.GeneratorVersion,
                ChunkCountX = 32,
                ChunkCountZ = 32,
                ChunkSize = 64,
                SamplesPerSide = 33,
                HeightStep = 0.25f,
            };
        }

        public float Width => ChunkCountX * ChunkSize;
        public float Depth => ChunkCountZ * ChunkSize;
        public float SampleSpacing => ChunkSize / (float)(SamplesPerSide - 1);
        public Vector3 WorldMinimum => new(-Width * 0.5f, 0f, -Depth * 0.5f);
        public Vector3 WorldMaximum => new(Width * 0.5f, 0f, Depth * 0.5f);

        public bool Contains(ChunkCoord coordinate)
        {
            return coordinate.X >= 0 && coordinate.Z >= 0
                && coordinate.X < ChunkCountX && coordinate.Z < ChunkCountZ;
        }

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref WorldId);
            serializer.SerializeValue(ref Seed);
            serializer.SerializeValue(ref GeneratorVersion);
            serializer.SerializeValue(ref ChunkCountX);
            serializer.SerializeValue(ref ChunkCountZ);
            serializer.SerializeValue(ref ChunkSize);
            serializer.SerializeValue(ref SamplesPerSide);
            serializer.SerializeValue(ref HeightStep);
        }

        public bool Equals(WorldDefinition other)
        {
            return WorldId == other.WorldId
                && Seed == other.Seed
                && GeneratorVersion == other.GeneratorVersion
                && ChunkCountX == other.ChunkCountX
                && ChunkCountZ == other.ChunkCountZ
                && ChunkSize == other.ChunkSize
                && SamplesPerSide == other.SamplesPerSide
                && HeightStep.Equals(other.HeightStep);
        }
    }

    [Serializable]
    public readonly struct ChunkCoord : IEquatable<ChunkCoord>
    {
        public readonly int X;
        public readonly int Z;

        public ChunkCoord(int x, int z)
        {
            X = x;
            Z = z;
        }

        public bool Equals(ChunkCoord other) => X == other.X && Z == other.Z;
        public override bool Equals(object obj) => obj is ChunkCoord other && Equals(other);
        public override int GetHashCode() => unchecked((X * 397) ^ Z);
        public override string ToString() => $"({X}, {Z})";
    }

    [Serializable]
    public readonly struct WorldObjectTypeId : IEquatable<WorldObjectTypeId>
    {
        public readonly ushort Value;

        public WorldObjectTypeId(ushort value) => Value = value;
        public bool Equals(WorldObjectTypeId other) => Value == other.Value;
        public override bool Equals(object obj) => obj is WorldObjectTypeId other && Equals(other);
        public override int GetHashCode() => Value;
        public override string ToString() => Value.ToString();
    }

    [Serializable]
    public readonly struct WorldObjectSpawn
    {
        public readonly ulong InstanceId;
        public readonly WorldObjectTypeId TypeId;
        public readonly Vector3 Position;
        public readonly Quaternion Rotation;
        public readonly Vector3 Scale;

        public WorldObjectSpawn(
            ulong instanceId,
            WorldObjectTypeId typeId,
            Vector3 position,
            Quaternion rotation,
            Vector3 scale)
        {
            InstanceId = instanceId;
            TypeId = typeId;
            Position = position;
            Rotation = rotation;
            Scale = scale;
        }
    }

    public sealed class ChunkData
    {
        public ChunkCoord Coordinate { get; }
        public int SamplesPerSide { get; }
        public int[] QuantizedHeights { get; }
        public IReadOnlyList<WorldObjectSpawn> Objects { get; }

        public ChunkData(
            ChunkCoord coordinate,
            int samplesPerSide,
            int[] quantizedHeights,
            IReadOnlyList<WorldObjectSpawn> objects)
        {
            Coordinate = coordinate;
            SamplesPerSide = samplesPerSide;
            QuantizedHeights = quantizedHeights;
            Objects = objects;
        }

        public int HeightAt(int x, int z) => QuantizedHeights[z * SamplesPerSide + x];
    }

    public interface IChunkGenerator
    {
        ChunkData Generate(WorldDefinition definition, ChunkCoord coordinate);
        float SampleHeight(WorldDefinition definition, float worldX, float worldZ);
        ulong CalculateHash(ChunkData data);
    }
}
