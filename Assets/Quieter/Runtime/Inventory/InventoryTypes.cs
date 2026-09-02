using System;
using Unity.Netcode;

namespace Quieter.Inventory
{
    public static class InventoryLayout
    {
        public const int MainSlotCount = 24;
        public const int HotbarSlotCount = 6;
        public const int InventorySlotCount = MainSlotCount + HotbarSlotCount;
        public const int WorkbenchSlotCount = 6;
        public const int FirstHotbarSlot = MainSlotCount;

        public static bool IsMain(int index) => index >= 0 && index < MainSlotCount;
        public static bool IsHotbar(int index) => index >= FirstHotbarSlot
            && index < InventorySlotCount;
    }

    public enum PickupPlacementPriority : byte
    {
        InventoryFirst = 0,
        HotbarFirst = 1,
    }

    public enum PickupResultCode : byte
    {
        Collected = 0,
        CollectedWithOverflow = 1,
        Unavailable = 2,
        Blocked = 3,
        InventoryFull = 4,
    }

    public enum InventorySlotArea : byte
    {
        Inventory = 0,
        Workbench = 1,
        ResearchTable = 2,
        None = byte.MaxValue,
    }

    public enum ResourceQuality : byte
    {
        None = 0,
        Low = 1,
        Modest = 2,
        Normal = 3,
        High = 4,
        Superior = 5,
    }

    [Serializable]
    public struct InventorySlotReference : INetworkSerializable, IEquatable<InventorySlotReference>
    {
        public InventorySlotArea Area;
        public byte Index;

        public InventorySlotReference(InventorySlotArea area, int index)
        {
            Area = area;
            Index = (byte)index;
        }

        public static InventorySlotReference Invalid => new(InventorySlotArea.None, 0);
        public bool IsValid => Area != InventorySlotArea.None;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Area);
            serializer.SerializeValue(ref Index);
        }

        public bool Equals(InventorySlotReference other) => Area == other.Area
            && Index == other.Index;
        public override bool Equals(object obj) => obj is InventorySlotReference other
            && Equals(other);
        public override int GetHashCode() => HashCode.Combine((byte)Area, Index);
    }

    [Serializable]
    public struct ItemStackState : INetworkSerializable, IEquatable<ItemStackState>
    {
        public ushort ItemId;
        public ushort Quantity;
        public ushort Condition;
        public ResourceQuality Quality;
        public ushort HiddenItemId;
        public ulong SourceNodeId;
        public byte RevealAtPercent;
        public ulong SampleId;

        public ItemStackState(
            ushort itemId,
            int quantity,
            ushort condition = 0,
            ResourceQuality quality = ResourceQuality.None,
            ushort hiddenItemId = 0,
            ulong sourceNodeId = 0,
            byte revealAtPercent = 0,
            ulong sampleId = 0)
        {
            ItemId = itemId;
            Quantity = (ushort)Math.Clamp(quantity, 0, ushort.MaxValue);
            Condition = condition;
            Quality = quality;
            HiddenItemId = hiddenItemId;
            SourceNodeId = sourceNodeId;
            RevealAtPercent = revealAtPercent;
            SampleId = sampleId;
            if (Quantity == 0)
            {
                Clear();
            }
        }

        public bool IsEmpty => ItemId == 0 || Quantity == 0;
        public static ItemStackState Empty => default;

        public void Clear()
        {
            ItemId = 0;
            Quantity = 0;
            Condition = 0;
            Quality = ResourceQuality.None;
            HiddenItemId = 0;
            SourceNodeId = 0;
            RevealAtPercent = 0;
            SampleId = 0;
        }

        public ItemStackState WithQuantity(int quantity) => new(
            ItemId,
            quantity,
            Condition,
            Quality,
            HiddenItemId,
            SourceNodeId,
            RevealAtPercent,
            SampleId);

        public ItemStackState ForReplication()
        {
            if (HiddenItemId == 0) return this;
            return new ItemStackState(
                ItemId,
                Quantity,
                sourceNodeId: SourceNodeId,
                revealAtPercent: RevealAtPercent,
                sampleId: SampleId);
        }

        public bool CanStackWith(ItemStackState other) => !IsEmpty && !other.IsEmpty
            && ItemId != 6 // Stable ID of the deliberately non-stackable research sample.
            && ItemId == other.ItemId
            && Condition == other.Condition
            && Quality == other.Quality
            && HiddenItemId == other.HiddenItemId
            && SourceNodeId == other.SourceNodeId
            && RevealAtPercent == other.RevealAtPercent
            && SampleId == other.SampleId;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref ItemId);
            serializer.SerializeValue(ref Quantity);
            serializer.SerializeValue(ref Condition);
            serializer.SerializeValue(ref Quality);
            serializer.SerializeValue(ref HiddenItemId);
            serializer.SerializeValue(ref SourceNodeId);
            serializer.SerializeValue(ref RevealAtPercent);
            serializer.SerializeValue(ref SampleId);
        }

        public bool Equals(ItemStackState other) => ItemId == other.ItemId
            && Quantity == other.Quantity
            && Condition == other.Condition
            && Quality == other.Quality
            && HiddenItemId == other.HiddenItemId
            && SourceNodeId == other.SourceNodeId
            && RevealAtPercent == other.RevealAtPercent
            && SampleId == other.SampleId;
        public override bool Equals(object obj) => obj is ItemStackState other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(
            ItemId,
            Quantity,
            Condition,
            (byte)Quality,
            HiddenItemId,
            SourceNodeId,
            RevealAtPercent,
            SampleId);
        public override string ToString() => IsEmpty ? "Empty" : $"{ItemId} x{Quantity}";
    }

    [Serializable]
    public sealed class StoredInventorySlot
    {
        public byte SlotIndex;
        public ushort ItemId;
        public ushort Quantity;
        public ushort Condition;
        public byte Quality;
        public ushort HiddenItemId;
        public string SourceNodeId;
        public byte RevealAtPercent;
        public string SampleId;
    }
}
