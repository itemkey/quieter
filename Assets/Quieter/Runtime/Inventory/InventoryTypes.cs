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

    public enum LiquidKind : byte
    {
        None,
        Water,
        SaltWater,
        Broth,
        HerbalInfusion,
        Waste,
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
        public ulong ItemInstanceId;
        public ushort Freshness;
        public ushort BiologicalContamination;
        public ushort ToxinContamination;
        public ushort Wetness;
        public ushort Cleanliness;
        public ushort LiquidMilliliters;
        public LiquidKind LiquidKind;
        public bool Equipped;

        public ItemStackState(
            ushort itemId,
            int quantity,
            ushort condition = 0,
            ResourceQuality quality = ResourceQuality.None,
            ushort hiddenItemId = 0,
            ulong sourceNodeId = 0,
            byte revealAtPercent = 0,
            ulong sampleId = 0,
            ulong itemInstanceId = 0,
            ushort freshness = 10000,
            ushort biologicalContamination = 0,
            ushort toxinContamination = 0,
            ushort wetness = 0,
            ushort cleanliness = 10000,
            ushort liquidMilliliters = 0,
            LiquidKind liquidKind = LiquidKind.None,
            bool equipped = false)
        {
            ItemId = itemId;
            Quantity = (ushort)Math.Clamp(quantity, 0, ushort.MaxValue);
            Condition = condition;
            Quality = quality;
            HiddenItemId = hiddenItemId;
            SourceNodeId = sourceNodeId;
            RevealAtPercent = revealAtPercent;
            SampleId = sampleId;
            ItemInstanceId = itemInstanceId;
            Freshness = (ushort)Math.Min(10000, (int)freshness);
            BiologicalContamination = (ushort)Math.Min(10000, (int)biologicalContamination);
            ToxinContamination = (ushort)Math.Min(10000, (int)toxinContamination);
            Wetness = (ushort)Math.Min(10000, (int)wetness);
            Cleanliness = (ushort)Math.Min(10000, (int)cleanliness);
            LiquidMilliliters = liquidMilliliters;
            LiquidKind = liquidKind;
            Equipped = equipped;
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
            ItemInstanceId = 0;
            Freshness = 0;
            BiologicalContamination = 0;
            ToxinContamination = 0;
            Wetness = 0;
            Cleanliness = 0;
            LiquidMilliliters = 0;
            LiquidKind = LiquidKind.None;
            Equipped = false;
        }

        public ItemStackState WithQuantity(int quantity) => new(
            ItemId,
            quantity,
            Condition,
            Quality,
            HiddenItemId,
            SourceNodeId,
            RevealAtPercent,
            SampleId,
            ItemInstanceId,
            Freshness,
            BiologicalContamination,
            ToxinContamination,
            Wetness,
            Cleanliness,
            LiquidMilliliters,
            LiquidKind,
            Equipped);

        public ItemStackState ForReplication()
        {
            if (HiddenItemId == 0) return this;
            return new ItemStackState(
                ItemId,
                Quantity,
                sourceNodeId: SourceNodeId,
                revealAtPercent: RevealAtPercent,
                sampleId: SampleId,
                itemInstanceId: ItemInstanceId,
                freshness: Freshness,
                biologicalContamination: BiologicalContamination,
                toxinContamination: ToxinContamination,
                wetness: Wetness,
                cleanliness: Cleanliness,
                liquidMilliliters: LiquidMilliliters,
                liquidKind: LiquidKind,
                equipped: Equipped);
        }

        public bool CanStackWith(ItemStackState other) => !IsEmpty && !other.IsEmpty
            && ItemId != 6 // Stable ID of the deliberately non-stackable research sample.
            && ItemId == other.ItemId
            && Condition == other.Condition
            && Quality == other.Quality
            && HiddenItemId == other.HiddenItemId
            && SourceNodeId == other.SourceNodeId
            && RevealAtPercent == other.RevealAtPercent
            && SampleId == other.SampleId
            && ItemInstanceId == 0
            && other.ItemInstanceId == 0
            && Freshness == other.Freshness
            && BiologicalContamination == other.BiologicalContamination
            && ToxinContamination == other.ToxinContamination
            && Wetness == other.Wetness
            && Cleanliness == other.Cleanliness
            && LiquidMilliliters == other.LiquidMilliliters
            && LiquidKind == other.LiquidKind
            && Equipped == other.Equipped;

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
            serializer.SerializeValue(ref ItemInstanceId);
            serializer.SerializeValue(ref Freshness);
            serializer.SerializeValue(ref BiologicalContamination);
            serializer.SerializeValue(ref ToxinContamination);
            serializer.SerializeValue(ref Wetness);
            serializer.SerializeValue(ref Cleanliness);
            serializer.SerializeValue(ref LiquidMilliliters);
            serializer.SerializeValue(ref LiquidKind);
            serializer.SerializeValue(ref Equipped);
        }

        public bool Equals(ItemStackState other) => ItemId == other.ItemId
            && Quantity == other.Quantity
            && Condition == other.Condition
            && Quality == other.Quality
            && HiddenItemId == other.HiddenItemId
            && SourceNodeId == other.SourceNodeId
            && RevealAtPercent == other.RevealAtPercent
            && SampleId == other.SampleId
            && ItemInstanceId == other.ItemInstanceId
            && Freshness == other.Freshness
            && BiologicalContamination == other.BiologicalContamination
            && ToxinContamination == other.ToxinContamination
            && Wetness == other.Wetness
            && Cleanliness == other.Cleanliness
            && LiquidMilliliters == other.LiquidMilliliters
            && LiquidKind == other.LiquidKind
            && Equipped == other.Equipped;
        public override bool Equals(object obj) => obj is ItemStackState other && Equals(other);
        public override int GetHashCode()
        {
            var hash = new HashCode();
            hash.Add(ItemId);
            hash.Add(Quantity);
            hash.Add(Condition);
            hash.Add((byte)Quality);
            hash.Add(HiddenItemId);
            hash.Add(SourceNodeId);
            hash.Add(RevealAtPercent);
            hash.Add(SampleId);
            hash.Add(ItemInstanceId);
            hash.Add(Freshness);
            hash.Add(BiologicalContamination);
            hash.Add(ToxinContamination);
            hash.Add(Wetness);
            hash.Add(Cleanliness);
            hash.Add(LiquidMilliliters);
            hash.Add((byte)LiquidKind);
            hash.Add(Equipped);
            return hash.ToHashCode();
        }
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
        public string ItemInstanceId;
        public ushort Freshness = 10000;
        public ushort BiologicalContamination;
        public ushort ToxinContamination;
        public ushort Wetness;
        public ushort Cleanliness = 10000;
        public ushort LiquidMilliliters;
        public byte LiquidKind;
        public bool Equipped;
    }

    public static class ItemInstanceIdFactory
    {
        public static ulong Create()
        {
            var bytes = Guid.NewGuid().ToByteArray();
            var value = BitConverter.ToUInt64(bytes, 0);
            return value == 0 ? 1ul : value;
        }
    }
}
