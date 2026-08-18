using Unity.Netcode;
using UnityEngine;

namespace Quieter.Player
{
    public struct PlayerInputFrame : INetworkSerializable
    {
        public uint Sequence;
        public Vector2 Movement;
        public float Yaw;
        public uint JumpPressId;
        public bool JumpHeld;
        public bool Sprint;
        public bool CrouchHeld;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Sequence);
            serializer.SerializeValue(ref Movement);
            serializer.SerializeValue(ref Yaw);
            serializer.SerializeValue(ref JumpPressId);
            serializer.SerializeValue(ref JumpHeld);
            serializer.SerializeValue(ref Sprint);
            serializer.SerializeValue(ref CrouchHeld);
        }
    }

    /// <summary>
    /// A small redundant input window. Re-sending recent frames makes unreliable
    /// delivery safe for short packet-loss bursts without making movement wait for ACKs.
    /// </summary>
    public struct PlayerInputBatch : INetworkSerializable
    {
        public const byte Capacity = 8;

        public byte Count;
        private PlayerInputFrame frame0;
        private PlayerInputFrame frame1;
        private PlayerInputFrame frame2;
        private PlayerInputFrame frame3;
        private PlayerInputFrame frame4;
        private PlayerInputFrame frame5;
        private PlayerInputFrame frame6;
        private PlayerInputFrame frame7;

        public PlayerInputFrame this[int index]
        {
            readonly get => index switch
            {
                0 => frame0,
                1 => frame1,
                2 => frame2,
                3 => frame3,
                4 => frame4,
                5 => frame5,
                6 => frame6,
                7 => frame7,
                _ => throw new System.ArgumentOutOfRangeException(nameof(index)),
            };
            set
            {
                switch (index)
                {
                    case 0: frame0 = value; break;
                    case 1: frame1 = value; break;
                    case 2: frame2 = value; break;
                    case 3: frame3 = value; break;
                    case 4: frame4 = value; break;
                    case 5: frame5 = value; break;
                    case 6: frame6 = value; break;
                    case 7: frame7 = value; break;
                    default: throw new System.ArgumentOutOfRangeException(nameof(index));
                }
            }
        }

        public void Add(PlayerInputFrame frame)
        {
            if (Count >= Capacity)
            {
                throw new System.InvalidOperationException("The player input batch is full.");
            }

            this[Count++] = frame;
        }

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Count);
            if (serializer.IsReader && Count > Capacity)
            {
                Count = Capacity;
            }

            if (Count > 0) serializer.SerializeValue(ref frame0);
            if (Count > 1) serializer.SerializeValue(ref frame1);
            if (Count > 2) serializer.SerializeValue(ref frame2);
            if (Count > 3) serializer.SerializeValue(ref frame3);
            if (Count > 4) serializer.SerializeValue(ref frame4);
            if (Count > 5) serializer.SerializeValue(ref frame5);
            if (Count > 6) serializer.SerializeValue(ref frame6);
            if (Count > 7) serializer.SerializeValue(ref frame7);
        }
    }

    public struct PlayerNetworkState : INetworkSerializable, System.IEquatable<PlayerNetworkState>
    {
        public uint ServerTick;
        public uint LastProcessedSequence;
        public uint LastObservedJumpPressId;
        public uint LastConsumedJumpPressId;
        public Vector3 Position;
        public Vector3 Velocity;
        public float Yaw;
        public byte JumpBufferTicks;
        public byte CoyoteTicks;
        public bool Grounded;
        public bool Crouched;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref ServerTick);
            serializer.SerializeValue(ref LastProcessedSequence);
            serializer.SerializeValue(ref LastObservedJumpPressId);
            serializer.SerializeValue(ref LastConsumedJumpPressId);
            serializer.SerializeValue(ref Position);
            serializer.SerializeValue(ref Velocity);
            serializer.SerializeValue(ref Yaw);
            serializer.SerializeValue(ref JumpBufferTicks);
            serializer.SerializeValue(ref CoyoteTicks);
            serializer.SerializeValue(ref Grounded);
            serializer.SerializeValue(ref Crouched);
        }

        public readonly bool Equals(PlayerNetworkState other)
        {
            return ServerTick == other.ServerTick
                && LastProcessedSequence == other.LastProcessedSequence
                && LastObservedJumpPressId == other.LastObservedJumpPressId
                && LastConsumedJumpPressId == other.LastConsumedJumpPressId
                && Position.Equals(other.Position)
                && Velocity.Equals(other.Velocity)
                && Yaw.Equals(other.Yaw)
                && JumpBufferTicks == other.JumpBufferTicks
                && CoyoteTicks == other.CoyoteTicks
                && Grounded == other.Grounded
                && Crouched == other.Crouched;
        }
    }
}
