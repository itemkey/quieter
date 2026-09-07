using System;
using System.Collections.Generic;
using UnityEngine;

namespace Quieter.Survival
{
    public enum CharacterControlKind : byte
    {
        Player,
        FreeNpc,
        ContractedNpc,
        ForcedNpc,
        Corpse,
        Remains,
    }

    public enum WorkerJobKind : byte
    {
        Mining,
        Logging,
        Foraging,
        Hauling,
        Construction,
        CookingAndWater,
        Sanitation,
        PatientCare,
    }

    public enum CaptureStatus : byte
    {
        None,
        ConditionsMet,
        Cancelled,
        Completed,
    }

    public enum CorpseDecayStage : byte
    {
        Fresh,
        EarlyDecay,
        ActiveDecay,
        AdvancedDecay,
        DryRemains,
        Buried,
        Cremated,
    }

    public enum SanitationNodeKind : byte
    {
        Latrine,
        ChamberPot,
        WashBasin,
        Barrel,
        Well,
        Drain,
        LinedPit,
        UnlinedPit,
    }

    [Serializable]
    public sealed class RelationshipState
    {
        public string SourceCharacterId = string.Empty;
        public string TargetCharacterId = string.Empty;
        [Range(0f, 1f)] public float Trust;
        [Range(0f, 1f)] public float Fear;
        [Range(0f, 1f)] public float Resentment;
        [Range(0f, 1f)] public float Loyalty;
        public bool VoluntaryLoyalty;
        public int PersonalRequestsCompleted;
    }

    [Serializable]
    public sealed class WorkerContractState
    {
        public string ContractId = string.Empty;
        public string EmployerAccountId = string.Empty;
        public string WorkerCharacterId = string.Empty;
        public string AssignedBedObjectId = string.Empty;
        public bool Active;
        public bool Voluntary;
        [Min(0f)] public float DailyRationCalories;
        [Range(0f, 1f)] public float PromisedSafety;
        [Range(0f, 24f)] public float WorkdayStartHour;
        [Range(0f, 24f)] public float WorkdayEndHour;
        public ushort PaymentItemId;
        public ushort PaymentQuantity;
        [Min(0f)] public double FulfilledContractGameSeconds;
        public int ConsecutiveBreaches;
        public List<WorkerJobKind> AllowedJobs = new();
    }

    [Serializable]
    public sealed class EstateState
    {
        public string AccountId = string.Empty;
        public string OwnerCharacterId = string.Empty;
        public string RegisteredHeirCharacterId = string.Empty;
        public long RegisteredAtUtcTicks;
        public long Revision;
        public List<string> DoorLockIds = new();
        public List<string> ContainerLockIds = new();
        public List<string> WorkerContractIds = new();
    }

    [Serializable]
    public sealed class CaptureState
    {
        public string VictimCharacterId = string.Empty;
        public string VictimAccountId = string.Empty;
        public string CaptorCharacterId = string.Empty;
        public string CaptorAccountId = string.Empty;
        public string CellObjectId = string.Empty;
        public CaptureStatus Status;
        public long ContinuousConditionsStartedUtcTicks;
        public long Revision;
    }

    [Serializable]
    public sealed class CorpseState
    {
        public string CorpseId = string.Empty;
        public string CharacterId = string.Empty;
        public long DiedAtUtcTicks;
        public CorpseDecayStage Stage;
        [Range(0f, 1f)] public float BiologicalContamination;
        public bool ItemsSealedByBurial;
        public bool OrganicItemsDestroyed;
    }

    [Serializable]
    public sealed class MapMarkState
    {
        public string MarkId = string.Empty;
        public Vector2 WorldPosition;
        public string Text = string.Empty;
        [Range(0f, 1f)] public float PositionalAccuracy = 1f;
        public long WrittenAtUtcTicks;
    }

    [Serializable]
    public sealed class MapDocumentState
    {
        public string MapId = string.Empty;
        public ulong ItemInstanceId;
        public string Title = string.Empty;
        public List<MapMarkState> Marks = new();
        public long Revision;
    }

    [Serializable]
    public sealed class LockComponentState
    {
        public string LockId = string.Empty;
        public string OwnerAccountId = string.Empty;
        public bool Locked;
    }

    [Serializable]
    public sealed class ContainerComponentState
    {
        public string ContainerId = string.Empty;
        [Min(0f)] public float CapacityLiters;
        public LockComponentState Lock = new();
    }

    [Serializable]
    public sealed class ShelterComponentState
    {
        [Range(0f, 1f)] public float RainProtection;
        [Range(0f, 1f)] public float WindProtection;
        [Range(0f, 1f)] public float Ventilation = 1f;
        [Range(0f, 1f)] public float SmokeConcentration;
    }

    [Serializable]
    public sealed class HeatComponentState
    {
        [Min(0f)] public float FuelSecondsRemaining;
        [Min(0f)] public float HeatOutput;
        [Min(0f)] public float SmokeOutput;
        public bool Burning;
    }

    [Serializable]
    public sealed class SanitationNodeState
    {
        public string NodeId = string.Empty;
        public SanitationNodeKind Kind;
        public Vector3 Position;
        [Min(0f)] public float CapacityLiters;
        [Min(0f)] public float ContentsLiters;
        [Range(0f, 1f)] public float BiologicalLoad;
        [Range(0f, 1f)] public float ToxinLoad;
        public string DownstreamNodeId = string.Empty;
        public bool Broken;
    }

    [Serializable]
    public sealed class PlacedObjectComponentState
    {
        public string ObjectId = string.Empty;
        public ContainerComponentState Container;
        public LockComponentState Lock;
        public ShelterComponentState Shelter;
        public HeatComponentState Heat;
        public SanitationNodeState Sanitation;
        public string BedAssignedCharacterId = string.Empty;
    }

    public readonly struct WorkerPerformanceInput
    {
        public WorkerPerformanceInput(
            int skillLevel,
            float physicalCapability,
            float health,
            float motivation,
            float toolEfficiency,
            float pathEfficiency,
            float weatherEfficiency)
        {
            SkillLevel = Mathf.Clamp(skillLevel, 0, 10);
            PhysicalCapability = Mathf.Clamp01(physicalCapability);
            Health = Mathf.Clamp01(health);
            Motivation = Mathf.Clamp01(motivation);
            ToolEfficiency = Mathf.Clamp(toolEfficiency, 0f, 1.5f);
            PathEfficiency = Mathf.Clamp01(pathEfficiency);
            WeatherEfficiency = Mathf.Clamp01(weatherEfficiency);
        }

        public int SkillLevel { get; }
        public float PhysicalCapability { get; }
        public float Health { get; }
        public float Motivation { get; }
        public float ToolEfficiency { get; }
        public float PathEfficiency { get; }
        public float WeatherEfficiency { get; }
    }

    public readonly struct InheritanceEligibility
    {
        public InheritanceEligibility(
            bool alive,
            bool hasDeed,
            bool assignedBed,
            double fulfilledContractGameSeconds,
            int completedPersonalRequests,
            bool activeContract,
            bool voluntaryLoyalty)
        {
            Alive = alive;
            HasDeed = hasDeed;
            AssignedBed = assignedBed;
            FulfilledContractGameSeconds = fulfilledContractGameSeconds;
            CompletedPersonalRequests = completedPersonalRequests;
            ActiveContract = activeContract;
            VoluntaryLoyalty = voluntaryLoyalty;
        }

        public bool Alive { get; }
        public bool HasDeed { get; }
        public bool AssignedBed { get; }
        public double FulfilledContractGameSeconds { get; }
        public int CompletedPersonalRequests { get; }
        public bool ActiveContract { get; }
        public bool VoluntaryLoyalty { get; }
    }

    public readonly struct EstateTransition
    {
        public EstateTransition(
            bool success,
            string controlledCharacterId,
            IReadOnlyList<string> doorLockIds,
            IReadOnlyList<string> containerLockIds,
            IReadOnlyList<string> contractIds,
            string error)
        {
            Success = success;
            ControlledCharacterId = controlledCharacterId ?? string.Empty;
            DoorLockIds = doorLockIds ?? Array.Empty<string>();
            ContainerLockIds = containerLockIds ?? Array.Empty<string>();
            ContractIds = contractIds ?? Array.Empty<string>();
            Error = error ?? string.Empty;
        }

        public bool Success { get; }
        public string ControlledCharacterId { get; }
        public IReadOnlyList<string> DoorLockIds { get; }
        public IReadOnlyList<string> ContainerLockIds { get; }
        public IReadOnlyList<string> ContractIds { get; }
        public string Error { get; }
    }
}
