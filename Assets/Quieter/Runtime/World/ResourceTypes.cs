using System;
using System.Text;
using Quieter.Inventory;
using UnityEngine;

namespace Quieter.World
{
    public enum WorldObjectKind : byte
    {
        Decoration = 0,
        LoosePickup = 1,
        StoneOutcrop = 2,
        Deposit = 3,
        Tree = 4,
        FiberPlant = 5,
        Spring = 6,
    }

    public enum ResourceSearchKind : byte
    {
        Water,
        Food,
        Forage,
        Logging,
        Mining,
    }

    public enum ResearchResultTone : byte
    {
        None = 0,
        Success = 1,
        Failure = 2,
        Cancelled = 3,
    }

    public enum ResearchCancelReason : byte
    {
        Released = 0,
        InterfaceClosed = 1,
        InteractionLost = 2,
    }

    public enum ResourceCategory : byte
    {
        Unknown = 0,
        MetallicOre = 1,
        Coal = 2,
        Salt = 3,
        Mineral = 4,
        Stone = 5,
        Forage = 6,
        NonOre = 7,
    }

    public enum DepositGenerationPool : byte
    {
        Base = 0,
        NonOre = 1,
    }

    public enum HarvestFeedbackCode : byte
    {
        Accepted = 0,
        YieldReceived = 1,
        WrongTool = 2,
        ToolBroken = 3,
        TargetUnavailable = 4,
    }

    public enum ResourceVisualArchetype : byte
    {
        VeinedRock = 0,
        LayeredLimestone = 1,
        ClayBed = 2,
        GypsumCrystals = 3,
        FlintNodules = 4,
        MarbleBlocks = 5,
    }

    public enum DepositRichness : byte
    {
        Scant = 0,
        Poor = 1,
        Ordinary = 2,
        Rich = 3,
        Exceptional = 4,
    }

    public enum DepositReserveSize : byte
    {
        VerySmall = 0,
        Small = 1,
        Medium = 2,
        Large = 3,
        Huge = 4,
    }

    [Serializable]
    public readonly struct ResourceNodeDescriptor : IEquatable<ResourceNodeDescriptor>
    {
        public readonly WorldObjectKind Kind;
        public readonly ushort ResourceItemId;
        public readonly ResourceCategory Category;
        public readonly DepositRichness Richness;
        public readonly DepositReserveSize ReserveSize;
        public readonly ushort InitialReserves;
        public readonly ResourceQuality Quality;
        public readonly byte Hardness;
        public readonly ushort ImpurityItemId;
        public readonly byte ImpurityChancePercent;
        public readonly ushort RespawnSeconds;
        public readonly ToolKind RequiredTool;

        public ResourceNodeDescriptor(
            WorldObjectKind kind,
            ushort resourceItemId,
            ResourceCategory category,
            DepositRichness richness,
            DepositReserveSize reserveSize,
            ushort initialReserves,
            ResourceQuality quality,
            byte hardness,
            ushort impurityItemId = 0,
            byte impurityChancePercent = 0,
            ushort respawnSeconds = 0,
            ToolKind requiredTool = ToolKind.Pickaxe)
        {
            Kind = kind;
            ResourceItemId = resourceItemId;
            Category = category;
            Richness = richness;
            ReserveSize = reserveSize;
            InitialReserves = initialReserves;
            Quality = quality;
            Hardness = (byte)Mathf.Clamp(hardness, 1, 5);
            ImpurityItemId = impurityItemId;
            ImpurityChancePercent = impurityChancePercent;
            RespawnSeconds = respawnSeconds;
            RequiredTool = requiredTool;
        }

        public bool IsResourceNode => Kind != WorldObjectKind.Decoration;
        public bool IsResearchable => Kind == WorldObjectKind.Deposit;
        public bool IsMineable => Kind == WorldObjectKind.Deposit
            || Kind == WorldObjectKind.StoneOutcrop
            || Kind == WorldObjectKind.Tree;
        public bool IsLoosePickup => Kind == WorldObjectKind.LoosePickup
            || Kind == WorldObjectKind.FiberPlant;
        public bool IsTree => Kind == WorldObjectKind.Tree;
        public bool IsWaterSource => Kind == WorldObjectKind.Spring;

        public bool Equals(ResourceNodeDescriptor other) => Kind == other.Kind
            && ResourceItemId == other.ResourceItemId
            && Category == other.Category
            && Richness == other.Richness
            && ReserveSize == other.ReserveSize
            && InitialReserves == other.InitialReserves
            && Quality == other.Quality
            && Hardness == other.Hardness
            && ImpurityItemId == other.ImpurityItemId
            && ImpurityChancePercent == other.ImpurityChancePercent
            && RespawnSeconds == other.RespawnSeconds
            && RequiredTool == other.RequiredTool;
    }

    [Serializable]
    public sealed class StoredResourceNodeState
    {
        public int WorldId;
        public string InstanceId;
        public ushort RemainingReserves;
        public string AvailableAtUtc;
    }

    [Serializable]
    public sealed class StoredDepositKnowledge
    {
        public int WorldId;
        public string InstanceId;
        public ushort StudyBasisPoints;
        public string DiscoveredAtUtc;
    }

    public readonly struct DepositKnowledgePresentation
    {
        public readonly ulong InstanceId;
        public readonly ushort StudyBasisPoints;
        public readonly Vector3 Position;
        public readonly ResourceCategory Category;
        public readonly ushort ResourceItemId;

        public DepositKnowledgePresentation(
            ulong instanceId,
            ushort studyBasisPoints,
            Vector3 position,
            ResourceCategory category,
            ushort resourceItemId)
        {
            InstanceId = instanceId;
            StudyBasisPoints = studyBasisPoints;
            Position = position;
            Category = category;
            ResourceItemId = resourceItemId;
        }
    }

    [Serializable]
    public sealed class StoredMapNote
    {
        public int WorldId;
        public string MapItemInstanceId;
        public string NoteId;
        public float X;
        public float Z;
        public string Text;
        public string CreatedAtUtc;
        public string UpdatedAtUtc;
    }

    [Serializable]
    public sealed class StoredPlacedObject
    {
        public int WorldId;
        public string ObjectId;
        public string OwnerAccountId;
        public string AssignedCharacterId;
        public ushort ItemId;
        public float X;
        public float Y;
        public float Z;
        public float Yaw;
        public bool Locked;
        public StoredInventorySlot Input;
        public string CreatedAtUtc;
        public string UpdatedAtUtc;
    }

    public static class MapNoteRules
    {
        public const int MaximumNotesPerMap = 64;
        public const int MaximumNotesPerWorld = MaximumNotesPerMap;
        public const int MaximumTextLength = 80;
        public const float MutationCooldownSeconds = 0.25f;

        public static string NormalizeText(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            var builder = new StringBuilder(Mathf.Min(value.Length, MaximumTextLength));
            var previousWhitespace = false;
            foreach (var character in value)
            {
                if (builder.Length >= MaximumTextLength) break;
                if (char.IsControl(character) || char.IsWhiteSpace(character))
                {
                    if (builder.Length > 0 && !previousWhitespace) builder.Append(' ');
                    previousWhitespace = true;
                    continue;
                }
                builder.Append(character);
                previousWhitespace = false;
            }
            return builder.ToString().Trim();
        }

        public static Vector2 ClampToWorld(Vector2 position, WorldDefinition definition)
        {
            if (!float.IsFinite(position.x) || !float.IsFinite(position.y)) return Vector2.zero;
            return new Vector2(
                Mathf.Clamp(position.x, definition.WorldMinimum.x, definition.WorldMaximum.x),
                Mathf.Clamp(position.y, definition.WorldMinimum.z, definition.WorldMaximum.z));
        }
    }

    public readonly struct ResourceNodeRuntimeState
    {
        public readonly ulong InstanceId;
        public readonly ushort RemainingReserves;
        public readonly long AvailableAtUnixSeconds;

        public ResourceNodeRuntimeState(
            ulong instanceId,
            ushort remainingReserves,
            long availableAtUnixSeconds = 0)
        {
            InstanceId = instanceId;
            RemainingReserves = remainingReserves;
            AvailableAtUnixSeconds = availableAtUnixSeconds;
        }

        public bool IsDepleted => RemainingReserves == 0;
        public bool IsAvailable(long unixSeconds) => !IsDepleted
            && (AvailableAtUnixSeconds == 0 || AvailableAtUnixSeconds <= unixSeconds);
    }

    public static class ResourceBalance
    {
        public const ushort PickaxeItemId = 5;
        public const ushort UnknownSampleItemId = 6;
        public const ushort PlantFiberItemId = 23;
        public const ushort ResearchTableItemId = 24;
        public const ushort WildBerriesItemId = 25;
        public const ushort EdibleRootsItemId = 26;
        public const ushort WildMushroomsItemId = 27;
        public const ushort MedicinalHerbsItemId = 28;
        public const ushort WildNutsItemId = 68;
        public const ushort CookedRootsItemId = 69;
        public const ushort CookedMushroomsItemId = 70;
        public const ushort RoastedNutsItemId = 71;
        public const float InteractionDistance = 3f;
        public const float PlacementDistance = 10f;
        public const float MiningCooldownSeconds = 0.8f;
        public const int TreeHitsRequired = 8;
        public const int TreeMinimumYield = 8;
        public const int TreeMaximumYield = 12;
        public const float ResearchDurationSeconds = 6f;
        public const int ResearchSuccessPercent = 70;
        public const int ResearchMinimumBasisPoints = 1500;
        public const int ResearchMaximumBasisPoints = 3500;

        public static readonly ushort[] ReserveUnits = { 30, 60, 120, 240, 480 };
        public static readonly byte[] UsefulChancePercent = { 20, 35, 55, 75, 90 };
        public static readonly float[] RichnessWorkMultiplier = { 1.25f, 1.15f, 1f, 0.85f, 0.7f };
        public static readonly float[] HardnessWork = { 1.5f, 2f, 3f, 4f, 5f };
        public static readonly byte[] DurabilityCost = { 1, 1, 2, 2, 3 };

        public static bool IsWildFood(ushort itemId) => itemId is
            WildBerriesItemId or EdibleRootsItemId or WildMushroomsItemId
                or WildNutsItemId;

        public static bool IsWildForage(ushort itemId) =>
            IsWildFood(itemId) || itemId == MedicinalHerbsItemId;

        public static bool TryGetCookedFoodItemId(ushort rawItemId, out ushort cookedItemId)
        {
            cookedItemId = rawItemId switch
            {
                EdibleRootsItemId => CookedRootsItemId,
                WildMushroomsItemId => CookedMushroomsItemId,
                WildNutsItemId => RoastedNutsItemId,
                _ => (ushort)0,
            };
            return cookedItemId != 0;
        }

        public static ItemStackState CookSolidFood(ItemStackState raw, ushort cookedItemId)
        {
            if (raw.IsEmpty || !TryGetCookedFoodItemId(raw.ItemId, out var expected)
                || expected != cookedItemId)
            {
                return default;
            }
            return new ItemStackState(
                cookedItemId,
                raw.Quantity,
                itemInstanceId: raw.ItemInstanceId,
                freshness: raw.Freshness,
                // Thorough heat destroys almost all living contamination but
                // neither reverses spoilage nor neutralises unknown plant toxins.
                biologicalContamination: (ushort)Mathf.CeilToInt(
                    raw.BiologicalContamination * 0.02f),
                toxinContamination: raw.ToxinContamination,
                cleanliness: raw.Cleanliness);
        }

        public static ItemStackState CreateWildFoodStack(ushort itemId, ulong instanceId)
        {
            if (!IsWildFood(itemId)) return new ItemStackState(itemId, 1);
            var freshness = itemId == WildNutsItemId
                ? (ushort)(9700 + (instanceId >> 8) % 301UL)
                : (ushort)(9200 + (instanceId >> 8) % 801UL);
            var biological = (ushort)(180 + (instanceId >> 24) % 1021UL);
            ushort toxins = 0;
            if (itemId == WildMushroomsItemId)
            {
                toxins = instanceId % 6UL == 0
                    ? (ushort)(6200 + (instanceId >> 36) % 2501UL)
                    : (ushort)((instanceId >> 40) % 501UL);
            }
            return new ItemStackState(
                itemId,
                1,
                freshness: freshness,
                biologicalContamination: biological,
                toxinContamination: toxins);
        }

        public static void SampleSpringWater(
            ulong instanceId,
            out float biologicalContamination,
            out float toxinContamination)
        {
            biologicalContamination = 0.06f
                + ((instanceId >> 12) & 0xFFFFUL) / 65535f * 0.32f;
            toxinContamination = ((instanceId >> 35) & 0xFFFFUL) / 65535f * 0.09f;
        }

        public static int WorkRequired(ResourceNodeDescriptor descriptor)
        {
            if (descriptor.IsTree) return TreeHitsRequired;
            var hardness = HardnessWork[Mathf.Clamp(descriptor.Hardness - 1, 0, 4)];
            var richness = RichnessWorkMultiplier[(int)descriptor.Richness];
            return Mathf.Max(1, Mathf.CeilToInt(hardness * richness));
        }

        public static ulong CalculateResearchRoll(long worldSeed, ulong sampleId)
        {
            unchecked
            {
                var value = (ulong)worldSeed ^ sampleId ^ 0xD6E8FEB86659FD93UL;
                value = (value ^ (value >> 30)) * 0xBF58476D1CE4E5B9UL;
                value = (value ^ (value >> 27)) * 0x94D049BB133111EBUL;
                return value ^ (value >> 31);
            }
        }

        public static bool CanShowSampleKnowledge(
            ItemStackState sample,
            int studyBasisPoints)
        {
            return !sample.IsEmpty
                && sample.ItemId == UnknownSampleItemId
                && sample.SourceNodeId != 0
                && sample.RevealAtPercent > 0
                && studyBasisPoints >= sample.RevealAtPercent * 100;
        }

        public static string RichnessName(DepositRichness value) => value switch
        {
            DepositRichness.Scant => "скудное",
            DepositRichness.Poor => "бедное",
            DepositRichness.Ordinary => "обычное",
            DepositRichness.Rich => "богатое",
            DepositRichness.Exceptional => "исключительное",
            _ => "неизвестно",
        };

        public static string ReserveName(DepositReserveSize value) => value switch
        {
            DepositReserveSize.VerySmall => "очень малые",
            DepositReserveSize.Small => "малые",
            DepositReserveSize.Medium => "средние",
            DepositReserveSize.Large => "большие",
            DepositReserveSize.Huge => "огромные",
            _ => "неизвестно",
        };

        public static string QualityName(ResourceQuality value) => value switch
        {
            ResourceQuality.Low => "низкое",
            ResourceQuality.Modest => "посредственное",
            ResourceQuality.Normal => "обычное",
            ResourceQuality.High => "высокое",
            ResourceQuality.Superior => "превосходное",
            _ => "неизвестно",
        };

        public static string CategoryName(ResourceCategory value) => value switch
        {
            ResourceCategory.MetallicOre => "металлическая руда",
            ResourceCategory.Coal => "уголь",
            ResourceCategory.Salt => "соль",
            ResourceCategory.Mineral => "минерал",
            ResourceCategory.Stone => "камень",
            ResourceCategory.NonOre => "нерудное сырьё",
            _ => "неизвестная порода",
        };

        public static string ToolName(ToolKind value) => value switch
        {
            ToolKind.Pickaxe => "примитивная кирка",
            ToolKind.Shovel => "примитивная лопата",
            ToolKind.Axe => "примитивный топор",
            _ => "подходящий инструмент",
        };
    }
}
