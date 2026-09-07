using System;
using System.Collections.Generic;
using UnityEngine;

namespace Quieter.Survival
{
    public enum CharacterAttributeId : byte
    {
        Strength,
        MuscularEndurance,
        AerobicCapacity,
        Mobility,
        Balance,
        Coordination,
        FineMotorControl,
        Perception,
        Memory,
        Reasoning,
        Willpower,
        Constitution,
        Count,
    }

    public enum SkillId : byte
    {
        Running,
        LoadCarrying,
        Landing,
        RestraintEscape,
        UnarmedCombat,
        BluntWeapons,
        EdgedWeapons,
        Polearms,
        Defence,
        Foraging,
        Woodcutting,
        Mining,
        Excavation,
        Carpentry,
        Masonry,
        Pottery,
        Toolmaking,
        CordageAndTextiles,
        Construction,
        Firekeeping,
        Cooking,
        WaterSafety,
        Sanitation,
        Navigation,
        Cartography,
        Geology,
        Botany,
        WeatherReading,
        Diagnosis,
        FirstAid,
        WoundCare,
        Suturing,
        Bonesetting,
        SurgeryAndDentistry,
        HerbalMedicine,
        Nursing,
        Persuasion,
        Intimidation,
        Leadership,
        Teaching,
        Count,
    }

    public enum TraitId : byte
    {
        FastHealing,
        StrongImmunity,
        FastLearner,
        Athletic,
        StrongBuild,
        Dexterous,
        KeenSenses,
        IronStomach,
        LowSleepNeed,
        ColdAdapted,
        HeatAdapted,
        HighPainThreshold,

        SlowHealing = 64,
        WeakImmunity,
        SlowLearner,
        Asthma,
        Coagulopathy,
        FragileBones,
        RottenTeeth,
        SensitiveDigestion,
        Insomnia,
        Myopia,
        Clumsy,
        PoorThermoregulation,
        HighMetabolism,
        ChronicPain,
    }

    public enum BodyRegion : byte
    {
        Head,
        Neck,
        Chest,
        Abdomen,
        Pelvis,
        LeftUpperArm,
        RightUpperArm,
        LeftForearm,
        RightForearm,
        LeftHand,
        RightHand,
        LeftThigh,
        RightThigh,
        LeftShin,
        RightShin,
        LeftFoot,
        RightFoot,
    }

    public enum InjuryType : byte
    {
        Abrasion,
        Laceration,
        Puncture,
        Contusion,
        Sprain,
        ClosedFracture,
        OpenFracture,
        Burn,
    }

    public enum DamageKind : byte
    {
        Blunt,
        Edged,
        Puncture,
        Heat,
        Fall,
    }

    public enum CharacterLifeState : byte
    {
        Conscious,
        Confused,
        Unconscious,
        Agonal,
        Dead,
    }

    public enum DeathCause : byte
    {
        None,
        BloodLoss,
        RespiratoryFailure,
        BrainFailure,
        CardiacFailure,
        Hypothermia,
        Hyperthermia,
        Dehydration,
        Starvation,
        Sepsis,
        Poisoning,
        MultipleOrganFailure,
        VoluntaryPassing,
        Captured,
    }

    [Flags]
    public enum SymptomFlags : uint
    {
        None = 0,
        Thirst = 1 << 0,
        DryMouth = 1 << 1,
        Hunger = 1 << 2,
        Weakness = 1 << 3,
        Fatigue = 1 << 4,
        Microsleep = 1 << 5,
        Cold = 1 << 6,
        Shivering = 1 << 7,
        Overheated = 1 << 8,
        Dizzy = 1 << 9,
        TunnelVision = 1 << 10,
        Pain = 1 << 11,
        SeverePain = 1 << 12,
        Bleeding = 1 << 13,
        Fever = 1 << 14,
        Nausea = 1 << 15,
        Breathless = 1 << 16,
        Panic = 1 << 17,
        BladderPressure = 1 << 18,
        BowelPressure = 1 << 19,
        Dirty = 1 << 20,
        Toothache = 1 << 21,
        Confusion = 1 << 22,
        LosingConsciousness = 1 << 23,
        AgonalBreathing = 1 << 24,
        SmokeIrritation = 1 << 25,
    }

    public enum MedicalActionType : byte
    {
        Inspect,
        ApplyPressure,
        Wash,
        Disinfect,
        Suture,
        Bandage,
        Splint,
        RemoveBandage,
        Warm,
        Cool,
        OralRehydration,
        HerbalPainRelief,
        DentalExtraction,
    }

    [Serializable]
    public sealed class WoundState
    {
        public uint WoundId;
        public BodyRegion Region;
        public InjuryType Type;
        [Range(0f, 1f)] public float Severity;
        [Range(0f, 1f)] public float TissueDamage;
        [Range(0f, 1f)] public float Contamination;
        [Range(0f, 1f)] public float Infection;
        [Range(0f, 1f)] public float Bleeding;
        [Range(0f, 1f)] public float Pain;
        [Range(0f, 1f)] public float PermanentImpairment;
        public bool InternalBleeding;
        public bool PressureApplied;
        public bool Washed;
        public bool Disinfected;
        public bool Sutured;
        public bool Bandaged;
        public bool Splinted;
        public bool Healed;

        public bool IsFracture => Type == InjuryType.ClosedFracture
            || Type == InjuryType.OpenFracture;
        public bool IsOpen => Type == InjuryType.Abrasion
            || Type == InjuryType.Laceration
            || Type == InjuryType.Puncture
            || Type == InjuryType.OpenFracture
            || Type == InjuryType.Burn;
    }

    [Serializable]
    public sealed class AnatomyState
    {
        [Range(0f, 1f)] public float BrainFunction = 1f;
        [Range(0f, 1f)] public float HeartFunction = 1f;
        [Range(0f, 1f)] public float LeftLungFunction = 1f;
        [Range(0f, 1f)] public float RightLungFunction = 1f;
        [Range(0f, 1f)] public float LiverFunction = 1f;
        [Range(0f, 1f)] public float KidneyFunction = 1f;
        [Range(0f, 1f)] public float GutFunction = 1f;
        public List<WoundState> Wounds = new();
        public uint NextWoundId = 1;

        public float LungFunction => (LeftLungFunction + RightLungFunction) * 0.5f;
    }

    [Serializable]
    public sealed class PhysiologyState
    {
        [Range(0f, 1f)] public float AcuteStamina = 1f;
        [Range(0f, 1f)] public float Oxygenation = 1f;
        [Range(0f, 1f)] public float BloodVolume = 1f;
        [Range(0f, 1f)] public float Hydration = 1f;
        [Range(0f, 1f)] public float ElectrolyteBalance = 1f;
        [Range(0f, 1f)] public float StomachFullness = 0.65f;
        [Range(0f, 1f)] public float EnergyReserve = 0.85f;
        [Range(0f, 1f)] public float ProteinReserve = 0.8f;
        [Range(0f, 1f)] public float MicronutrientReserve = 0.8f;
        [Range(0f, 1f)] public float SleepDebt;
        [Range(0f, 1f)] public float BladderFill = 0.15f;
        [Range(0f, 1f)] public float BowelFill = 0.2f;
        [Range(0f, 1f)] public float HandCleanliness = 0.75f;
        [Range(0f, 1f)] public float BodyCleanliness = 0.8f;
        [Range(0f, 1f)] public float DentalHealth = 0.85f;
        [Range(0f, 1f)] public float ImmuneReserve = 1f;
        [Min(0f)] public float CarriedMassKg;
        [Range(0f, 1f)] public float SystemicInfection;
        [Range(0f, 1f)] public float ToxinLoad;
        [Range(0f, 1f)] public float SmokeIrritation;
        [Range(0f, 1f)] public float Pain;
        [Range(0f, 1f)] public float Stress = 0.05f;
        [Range(0f, 1f)] public float Consciousness = 1f;
        public float CoreTemperatureC = 37f;
        public float Wetness;
        public float StableFailureSeconds;
        public DeathCause CriticalCause;
        public float SafeOfflineSeconds;
        public float CurrentSleepSeconds;
        public bool SleepCycleConsolidated;
        public CharacterLifeState LifeState = CharacterLifeState.Conscious;
        public DeathCause DeathCause;
    }

    [Serializable]
    public sealed class CharacterProgressionState
    {
        public float[] Attributes = new float[(int)CharacterAttributeId.Count];
        public float[] SkillPracticeHours = new float[(int)SkillId.Count];
        public float[] RelevantPracticeHours = new float[(int)SkillId.Count];
        public float[] PendingConsolidationHours = new float[(int)SkillId.Count];
        public float[] AttributeTrainingLoad = new float[(int)CharacterAttributeId.Count];

        public void EnsureInitialized()
        {
            var attributesWereMissing = Attributes == null
                || Attributes.Length != (int)CharacterAttributeId.Count;
            if (Attributes == null || Attributes.Length != (int)CharacterAttributeId.Count)
            {
                Attributes = new float[(int)CharacterAttributeId.Count];
            }
            if (SkillPracticeHours == null || SkillPracticeHours.Length != (int)SkillId.Count)
            {
                SkillPracticeHours = new float[(int)SkillId.Count];
            }
            if (RelevantPracticeHours == null || RelevantPracticeHours.Length != (int)SkillId.Count)
                RelevantPracticeHours = new float[(int)SkillId.Count];
            if (PendingConsolidationHours == null
                || PendingConsolidationHours.Length != (int)SkillId.Count)
            {
                PendingConsolidationHours = new float[(int)SkillId.Count];
            }
            if (AttributeTrainingLoad == null
                || AttributeTrainingLoad.Length != (int)CharacterAttributeId.Count)
            {
                AttributeTrainingLoad = new float[(int)CharacterAttributeId.Count];
            }

            var hasAnyAttributeValue = false;
            for (var index = 0; index < Attributes.Length; index++)
            {
                hasAnyAttributeValue |= Attributes[index] > 0f;
            }

            if (attributesWereMissing || !hasAnyAttributeValue)
            {
                for (var index = 0; index < Attributes.Length; index++)
                {
                    Attributes[index] = 50f;
                }
            }
        }
    }

    [Serializable]
    public sealed class CharacterSurvivalState
    {
        public string CharacterId = string.Empty;
        public string CharacterName = string.Empty;
        public List<TraitId> Traits = new();
        public PhysiologyState Physiology = new();
        public AnatomyState Anatomy = new();
        public CharacterProgressionState Progression = new();
        public bool Sleeping;
        public bool Offline;
        public bool Bound;
        public bool Captive;
        public string CaptorCharacterId = string.Empty;
        public string CaptureStartedAtUtc = string.Empty;
        public long Revision;
        public bool CreationCompleted;

        public void EnsureInitialized()
        {
            Traits ??= new List<TraitId>();
            Physiology ??= new PhysiologyState();
            Anatomy ??= new AnatomyState();
            Anatomy.Wounds ??= new List<WoundState>();
            Progression ??= new CharacterProgressionState();
            Progression.EnsureInitialized();
        }
    }

    public readonly struct SurvivalEnvironment
    {
        public SurvivalEnvironment(
            float ambientTemperatureC,
            float windMetersPerSecond,
            float humidity,
            float precipitation,
            float insulation,
            float externalHeat,
            bool sheltered,
            float smokeConcentration = 0f)
        {
            AmbientTemperatureC = ambientTemperatureC;
            WindMetersPerSecond = Mathf.Max(0f, windMetersPerSecond);
            Humidity = Mathf.Clamp01(humidity);
            Precipitation = Mathf.Clamp01(precipitation);
            Insulation = Mathf.Clamp01(insulation);
            ExternalHeat = Mathf.Clamp(externalHeat, 0f, 2f);
            Sheltered = sheltered;
            SmokeConcentration = Mathf.Clamp01(smokeConcentration);
        }

        public float AmbientTemperatureC { get; }
        public float WindMetersPerSecond { get; }
        public float Humidity { get; }
        public float Precipitation { get; }
        public float Insulation { get; }
        public float ExternalHeat { get; }
        public bool Sheltered { get; }
        public float SmokeConcentration { get; }

        public static SurvivalEnvironment Temperate => new(
            18f, 1f, 0.5f, 0f, 0.25f, 0f, false);
    }

    public readonly struct CharacterCapabilities
    {
        public CharacterCapabilities(
            float movementSpeed,
            float acceleration,
            float jumpStrength,
            float staminaRecovery,
            float fineMotor,
            bool canMove,
            bool canSprint)
        {
            MovementSpeed = Mathf.Clamp(movementSpeed, 0f, 1.25f);
            Acceleration = Mathf.Clamp(acceleration, 0f, 1.25f);
            JumpStrength = Mathf.Clamp(jumpStrength, 0f, 1.25f);
            StaminaRecovery = Mathf.Clamp(staminaRecovery, 0f, 2f);
            FineMotor = Mathf.Clamp(fineMotor, 0f, 1.25f);
            CanMove = canMove;
            CanSprint = canSprint;
        }

        public float MovementSpeed { get; }
        public float Acceleration { get; }
        public float JumpStrength { get; }
        public float StaminaRecovery { get; }
        public float FineMotor { get; }
        public bool CanMove { get; }
        public bool CanSprint { get; }

        public static CharacterCapabilities Normal => new(1f, 1f, 1f, 1f, 1f, true, true);
    }

    public readonly struct TreatmentContext
    {
        public TreatmentContext(
            float skill,
            float materialCleanliness,
            bool hasWater = false,
            bool hasDisinfectant = false,
            bool hasNeedleAndThread = false,
            bool hasBandage = false,
            bool hasSplint = false)
        {
            Skill = Mathf.Clamp01(skill);
            MaterialCleanliness = Mathf.Clamp01(materialCleanliness);
            HasWater = hasWater;
            HasDisinfectant = hasDisinfectant;
            HasNeedleAndThread = hasNeedleAndThread;
            HasBandage = hasBandage;
            HasSplint = hasSplint;
        }

        public float Skill { get; }
        public float MaterialCleanliness { get; }
        public bool HasWater { get; }
        public bool HasDisinfectant { get; }
        public bool HasNeedleAndThread { get; }
        public bool HasBandage { get; }
        public bool HasSplint { get; }
    }

    public readonly struct TreatmentResult
    {
        public TreatmentResult(bool success, string message, float complicationRisk)
        {
            Success = success;
            Message = message ?? string.Empty;
            ComplicationRisk = Mathf.Clamp01(complicationRisk);
        }

        public bool Success { get; }
        public string Message { get; }
        public float ComplicationRisk { get; }
    }
}
