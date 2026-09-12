using System;
using System.Collections.Generic;
using Quieter.Core;
using Quieter.Inventory;
using Quieter.Persistence;
using Quieter.World;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Quieter.Survival
{
    public struct PublicSymptomState : INetworkSerializable, IEquatable<PublicSymptomState>
    {
        public SymptomFlags Symptoms;
        public CharacterLifeState LifeState;
        public byte VisibleWoundCount;
        public bool Sleeping;
        public bool Bound;
        public bool Captive;
        public CharacterControlKind ControlKind;
        public NpcActivityKind Activity;
        public WorkerJobKind ObservedJob;
        public byte ObservedSkillMinimum;
        public byte ObservedSkillMaximum;
        public bool HasWorkEvidence;
        public NpcPersonalRequestKind PersonalRequest;
        public bool HasPersonalRequest;
        public CorpseDecayStage CorpseStage;
        public byte CorpseContamination;
        public BodyPosture BodyPosture;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer)
            where T : IReaderWriter
        {
            serializer.SerializeValue(ref Symptoms);
            serializer.SerializeValue(ref LifeState);
            serializer.SerializeValue(ref VisibleWoundCount);
            serializer.SerializeValue(ref Sleeping);
            serializer.SerializeValue(ref Bound);
            serializer.SerializeValue(ref Captive);
            serializer.SerializeValue(ref ControlKind);
            serializer.SerializeValue(ref Activity);
            serializer.SerializeValue(ref ObservedJob);
            serializer.SerializeValue(ref ObservedSkillMinimum);
            serializer.SerializeValue(ref ObservedSkillMaximum);
            serializer.SerializeValue(ref HasWorkEvidence);
            serializer.SerializeValue(ref PersonalRequest);
            serializer.SerializeValue(ref HasPersonalRequest);
            serializer.SerializeValue(ref CorpseStage);
            serializer.SerializeValue(ref CorpseContamination);
            serializer.SerializeValue(ref BodyPosture);
        }

        public bool Equals(PublicSymptomState other)
            => Symptoms == other.Symptoms
                && LifeState == other.LifeState
                && VisibleWoundCount == other.VisibleWoundCount
                && Sleeping == other.Sleeping
                && Bound == other.Bound
                && Captive == other.Captive
                && ControlKind == other.ControlKind
                && Activity == other.Activity
                && ObservedJob == other.ObservedJob
                && ObservedSkillMinimum == other.ObservedSkillMinimum
                && ObservedSkillMaximum == other.ObservedSkillMaximum
                && HasWorkEvidence == other.HasWorkEvidence
                && PersonalRequest == other.PersonalRequest
                && HasPersonalRequest == other.HasPersonalRequest
                && CorpseStage == other.CorpseStage
                && CorpseContamination == other.CorpseContamination
                && BodyPosture == other.BodyPosture;
    }

    public struct OwnerConditionState : INetworkSerializable, IEquatable<OwnerConditionState>
    {
        public SymptomFlags Symptoms;
        public CharacterLifeState LifeState;
        public DeathCause DeathCause;
        public byte ThirstStage;
        public byte HungerStage;
        public byte FatigueStage;
        public byte TemperatureStage;
        public byte PainStage;
        public byte BladderStage;
        public byte BowelStage;
        public byte GastrointestinalStage;
        public byte RespiratoryStage;
        public byte ParasiteStage;
        public uint WoundedRegions;
        public uint FracturedRegions;
        public bool NeedsCharacterCreation;
        public bool Sleeping;
        public bool Bound;
        public bool Captive;
        public bool BeingCarried;
        public bool VoluntaryPassingAttemptActive;
        public byte VoluntaryPassingStage;
        public BodyPosture BodyPosture;
        public CharacterControlKind ControlKind;
        public byte TraitSelectionError;
        public ushort Revision;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer)
            where T : IReaderWriter
        {
            serializer.SerializeValue(ref Symptoms);
            serializer.SerializeValue(ref LifeState);
            serializer.SerializeValue(ref DeathCause);
            serializer.SerializeValue(ref ThirstStage);
            serializer.SerializeValue(ref HungerStage);
            serializer.SerializeValue(ref FatigueStage);
            serializer.SerializeValue(ref TemperatureStage);
            serializer.SerializeValue(ref PainStage);
            serializer.SerializeValue(ref BladderStage);
            serializer.SerializeValue(ref BowelStage);
            serializer.SerializeValue(ref GastrointestinalStage);
            serializer.SerializeValue(ref RespiratoryStage);
            serializer.SerializeValue(ref ParasiteStage);
            serializer.SerializeValue(ref WoundedRegions);
            serializer.SerializeValue(ref FracturedRegions);
            serializer.SerializeValue(ref NeedsCharacterCreation);
            serializer.SerializeValue(ref Sleeping);
            serializer.SerializeValue(ref Bound);
            serializer.SerializeValue(ref Captive);
            serializer.SerializeValue(ref BeingCarried);
            serializer.SerializeValue(ref VoluntaryPassingAttemptActive);
            serializer.SerializeValue(ref VoluntaryPassingStage);
            serializer.SerializeValue(ref BodyPosture);
            serializer.SerializeValue(ref ControlKind);
            serializer.SerializeValue(ref TraitSelectionError);
            serializer.SerializeValue(ref Revision);
        }

        public bool Equals(OwnerConditionState other)
            => Symptoms == other.Symptoms
                && LifeState == other.LifeState
                && DeathCause == other.DeathCause
                && ThirstStage == other.ThirstStage
                && HungerStage == other.HungerStage
                && FatigueStage == other.FatigueStage
                && TemperatureStage == other.TemperatureStage
                && PainStage == other.PainStage
                && BladderStage == other.BladderStage
                && BowelStage == other.BowelStage
                && GastrointestinalStage == other.GastrointestinalStage
                && RespiratoryStage == other.RespiratoryStage
                && ParasiteStage == other.ParasiteStage
                && WoundedRegions == other.WoundedRegions
                && FracturedRegions == other.FracturedRegions
                && NeedsCharacterCreation == other.NeedsCharacterCreation
                && Sleeping == other.Sleeping
                && Bound == other.Bound
                && Captive == other.Captive
                && BeingCarried == other.BeingCarried
                && VoluntaryPassingAttemptActive == other.VoluntaryPassingAttemptActive
                && VoluntaryPassingStage == other.VoluntaryPassingStage
                && BodyPosture == other.BodyPosture
                && ControlKind == other.ControlKind
                && TraitSelectionError == other.TraitSelectionError
                && Revision == other.Revision;
    }

    public struct PublicWorkerContractState : INetworkSerializable,
        IEquatable<PublicWorkerContractState>
    {
        public bool Active;
        public bool Voluntary;
        public WorkerJobKind ActiveJob;
        public WorkerJobKind SelectedJob;
        public byte SelectedPriority;
        public ushort DailyRationCalories;
        public float WorkdayStartHour;
        public float WorkdayEndHour;
        public float WorkZoneRadius;
        public Vector3 WorkZoneCenter;
        public Vector3 StoragePosition;
        public ushort PaymentItemId;
        public ushort PaymentQuantity;
        public byte ConsecutiveBreaches;
        public ushort Revision;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer)
            where T : IReaderWriter
        {
            serializer.SerializeValue(ref Active);
            serializer.SerializeValue(ref Voluntary);
            serializer.SerializeValue(ref ActiveJob);
            serializer.SerializeValue(ref SelectedJob);
            serializer.SerializeValue(ref SelectedPriority);
            serializer.SerializeValue(ref DailyRationCalories);
            serializer.SerializeValue(ref WorkdayStartHour);
            serializer.SerializeValue(ref WorkdayEndHour);
            serializer.SerializeValue(ref WorkZoneRadius);
            serializer.SerializeValue(ref WorkZoneCenter);
            serializer.SerializeValue(ref StoragePosition);
            serializer.SerializeValue(ref PaymentItemId);
            serializer.SerializeValue(ref PaymentQuantity);
            serializer.SerializeValue(ref ConsecutiveBreaches);
            serializer.SerializeValue(ref Revision);
        }

        public bool Equals(PublicWorkerContractState other)
            => Active == other.Active
                && Voluntary == other.Voluntary
                && ActiveJob == other.ActiveJob
                && SelectedJob == other.SelectedJob
                && SelectedPriority == other.SelectedPriority
                && DailyRationCalories == other.DailyRationCalories
                && Mathf.Approximately(WorkdayStartHour, other.WorkdayStartHour)
                && Mathf.Approximately(WorkdayEndHour, other.WorkdayEndHour)
                && Mathf.Approximately(WorkZoneRadius, other.WorkZoneRadius)
                && WorkZoneCenter == other.WorkZoneCenter
                && StoragePosition == other.StoragePosition
                && PaymentItemId == other.PaymentItemId
                && PaymentQuantity == other.PaymentQuantity
                && ConsecutiveBreaches == other.ConsecutiveBreaches
                && Revision == other.Revision;
    }

    public struct MovementCapabilityState : INetworkSerializable, IEquatable<MovementCapabilityState>
    {
        public float MovementSpeed;
        public float Acceleration;
        public float JumpStrength;
        public float StaminaRecovery;
        public float FineMotor;
        public bool CanMove;
        public bool CanSprint;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer)
            where T : IReaderWriter
        {
            serializer.SerializeValue(ref MovementSpeed);
            serializer.SerializeValue(ref Acceleration);
            serializer.SerializeValue(ref JumpStrength);
            serializer.SerializeValue(ref StaminaRecovery);
            serializer.SerializeValue(ref FineMotor);
            serializer.SerializeValue(ref CanMove);
            serializer.SerializeValue(ref CanSprint);
        }

        public bool Equals(MovementCapabilityState other)
            => Mathf.Approximately(MovementSpeed, other.MovementSpeed)
                && Mathf.Approximately(Acceleration, other.Acceleration)
                && Mathf.Approximately(JumpStrength, other.JumpStrength)
                && Mathf.Approximately(StaminaRecovery, other.StaminaRecovery)
                && Mathf.Approximately(FineMotor, other.FineMotor)
                && CanMove == other.CanMove
                && CanSprint == other.CanSprint;

        public CharacterCapabilities ToCapabilities() => new(
            MovementSpeed,
            Acceleration,
            JumpStrength,
            StaminaRecovery,
            FineMotor,
            CanMove,
            CanSprint);

        public static MovementCapabilityState From(CharacterCapabilities value) => new()
        {
            MovementSpeed = value.MovementSpeed,
            Acceleration = value.Acceleration,
            JumpStrength = value.JumpStrength,
            StaminaRecovery = value.StaminaRecovery,
            FineMotor = value.FineMotor,
            CanMove = value.CanMove,
            CanSprint = value.CanSprint,
        };

        public static MovementCapabilityState Normal => From(CharacterCapabilities.Normal);
    }

    public struct OwnerHeirOfferState : INetworkSerializable, IEquatable<OwnerHeirOfferState>
    {
        public bool Available;
        public FixedString64Bytes OfferId;
        public FixedString64Bytes DonorName;
        public FixedString64Bytes HeirName;
        public long ExpiresAtUtcTicks;
        public ushort Revision;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer)
            where T : IReaderWriter
        {
            serializer.SerializeValue(ref Available);
            serializer.SerializeValue(ref OfferId);
            serializer.SerializeValue(ref DonorName);
            serializer.SerializeValue(ref HeirName);
            serializer.SerializeValue(ref ExpiresAtUtcTicks);
            serializer.SerializeValue(ref Revision);
        }

        public bool Equals(OwnerHeirOfferState other)
            => Available == other.Available
                && OfferId.Equals(other.OfferId)
                && DonorName.Equals(other.DonorName)
                && HeirName.Equals(other.HeirName)
                && ExpiresAtUtcTicks == other.ExpiresAtUtcTicks
                && Revision == other.Revision;
    }

    [Flags]
    public enum ObservedTreatmentFlags : byte
    {
        None = 0,
        Pressure = 1 << 0,
        Washed = 1 << 1,
        Disinfected = 1 << 2,
        Sutured = 1 << 3,
        Bandaged = 1 << 4,
        Splinted = 1 << 5,
    }

    public struct ObservedWoundState : INetworkSerializable, IEquatable<ObservedWoundState>
    {
        public uint WoundId;
        public BodyRegion Region;
        public InjuryType Type;
        public bool TypeKnown;
        public bool InternalBleedingSuspected;
        public byte SeverityStage;
        public byte BleedingStage;
        public byte ContaminationStage;
        public byte InfectionStage;
        public ObservedTreatmentFlags TreatmentFlags;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer)
            where T : IReaderWriter
        {
            serializer.SerializeValue(ref WoundId);
            serializer.SerializeValue(ref Region);
            serializer.SerializeValue(ref Type);
            serializer.SerializeValue(ref TypeKnown);
            serializer.SerializeValue(ref InternalBleedingSuspected);
            serializer.SerializeValue(ref SeverityStage);
            serializer.SerializeValue(ref BleedingStage);
            serializer.SerializeValue(ref ContaminationStage);
            serializer.SerializeValue(ref InfectionStage);
            serializer.SerializeValue(ref TreatmentFlags);
        }

        public bool Equals(ObservedWoundState other)
            => WoundId == other.WoundId
                && Region == other.Region
                && Type == other.Type
                && TypeKnown == other.TypeKnown
                && InternalBleedingSuspected == other.InternalBleedingSuspected
                && SeverityStage == other.SeverityStage
                && BleedingStage == other.BleedingStage
                && ContaminationStage == other.ContaminationStage
                && InfectionStage == other.InfectionStage
                && TreatmentFlags == other.TreatmentFlags;
    }

    public struct TreatmentActivityState : INetworkSerializable, IEquatable<TreatmentActivityState>
    {
        public bool Active;
        public uint WoundId;
        public MedicalActionType Action;
        public double CompletesAtServerTime;
        public FixedString512Bytes Message;
        public ushort Revision;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer)
            where T : IReaderWriter
        {
            serializer.SerializeValue(ref Active);
            serializer.SerializeValue(ref WoundId);
            serializer.SerializeValue(ref Action);
            serializer.SerializeValue(ref CompletesAtServerTime);
            serializer.SerializeValue(ref Message);
            serializer.SerializeValue(ref Revision);
        }

        public bool Equals(TreatmentActivityState other)
            => Active == other.Active
                && WoundId == other.WoundId
                && Action == other.Action
                && CompletesAtServerTime.Equals(other.CompletesAtServerTime)
                && Message.Equals(other.Message)
                && Revision == other.Revision;
    }

    public struct ProgressionEntryState : INetworkSerializable, IEquatable<ProgressionEntryState>
    {
        public bool Attribute;
        public byte Id;
        public byte Level;
        public byte ProgressStage;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer)
            where T : IReaderWriter
        {
            serializer.SerializeValue(ref Attribute);
            serializer.SerializeValue(ref Id);
            serializer.SerializeValue(ref Level);
            serializer.SerializeValue(ref ProgressStage);
        }

        public bool Equals(ProgressionEntryState other)
            => Attribute == other.Attribute
                && Id == other.Id
                && Level == other.Level
                && ProgressStage == other.ProgressStage;
    }

    [RequireComponent(typeof(Quieter.Player.NetworkPlayer))]
    public sealed class PlayerSurvival : NetworkBehaviour
    {
        public const float ServerSimulationInterval = 0.5f;
        public const float LessonDurationSeconds = 120f;

        private readonly NetworkVariable<PublicSymptomState> publicSymptoms = new(
            default,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<OwnerConditionState> ownerCondition = new(
            default,
            NetworkVariableReadPermission.Owner,
            NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<MovementCapabilityState> movementCapability = new(
            MovementCapabilityState.Normal,
            NetworkVariableReadPermission.Owner,
            NetworkVariableWritePermission.Server);
        private readonly NetworkList<ObservedWoundState> observedWounds = new(
            default,
            NetworkVariableReadPermission.Owner,
            NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<TreatmentActivityState> treatmentActivity = new(
            default,
            NetworkVariableReadPermission.Owner,
            NetworkVariableWritePermission.Server);
        private readonly NetworkList<ProgressionEntryState> progressionEntries = new(
            default,
            NetworkVariableReadPermission.Owner,
            NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<OwnerHeirOfferState> ownerHeirOffer = new(
            default,
            NetworkVariableReadPermission.Owner,
            NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<PublicWorkerContractState> publicWorkerContract = new(
            default,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        private CharacterSurvivalState serverState;
        private PlayerInventory inventory;
        private Quieter.Player.NetworkPlayer networkPlayer;
        private float accumulator;
        private float exertion;
        private ushort replicatedRevision;
        private byte traitSelectionError;
        private bool pendingTreatmentActive;
        private uint pendingTreatmentWoundId;
        private MedicalActionType pendingTreatmentAction;
        private double pendingTreatmentCompletesAt;
        private ushort treatmentRevision;
        private float nextProgressionReplicationAt;
        private PlayerSurvival pendingTreatmentTarget;
        private PlayerSurvival activeExternalHealer;
        private ulong consentedHealerObjectId;
        private double consentExpiresAt;
        private ulong pendingConsentHealerObjectId;
        private string pendingConsentHealerName = string.Empty;
        private string externalMedicalMessage = string.Empty;
        private float externalMedicalMessageUntil;
        private PlayerSurvival focusedMedicalTarget;
        private uint inspectedExternalWoundId;
        private PlayerSurvival serverCarrier;
        private PlayerSurvival serverCarriedTarget;
        private ulong serverSteamId;
        private bool captureCompletionRaised;
        private float nextCorpseUpdateAt;
        private PlayerSurvival lessonCounterpart;
        private double lessonCompletesAt;

        private bool HasPendingTreatment => pendingTreatmentActive
            || serverState?.MedicalActivity?.Active == true;
        private bool HasBlockingActivity => HasPendingTreatment
            || serverState?.VoluntaryPassing?.AttemptActive == true
            || serverState?.LessonActivity?.Active == true;

        public event Action Changed;
        public event Action<CharacterSurvivalState> ServerDied;
        public event Action<PlayerSurvival> ServerNewStrangerRequested;
        public event Action<PlayerSurvival> ServerCaptureCompleted;
        public event Action<PlayerSurvival, PlayerSurvival> ServerHeirRegistrationRequested;
        public event Action<PlayerSurvival, PlayerSurvival> ServerHeirDonationRequested;
        public event Action<PlayerSurvival> ServerHeirOfferAcceptanceRequested;

        public PublicSymptomState PublicSymptoms => publicSymptoms.Value;
        public OwnerConditionState OwnerCondition => ownerCondition.Value;
        public OwnerHeirOfferState OwnerHeirOffer => ownerHeirOffer.Value;
        public PublicWorkerContractState PublicWorkerContract => publicWorkerContract.Value;
        public CharacterSurvivalState ServerState => IsServer ? serverState : null;
        public bool ServerIsDead => IsServer
            && serverState?.Physiology?.LifeState == CharacterLifeState.Dead;
        public bool ServerIsLifeLost => IsServer && serverState != null
            && (ServerIsDead || serverState.ControlKind == CharacterControlKind.ForcedNpc);
        public bool ServerCanBeSearched => IsServer && serverState != null
            && (ServerIsDead && !ServerCorpseItemsSealed
                || !ServerIsDead && (serverState.Sleeping || serverState.Bound
                    || serverState.Physiology.LifeState is
                        CharacterLifeState.Unconscious or CharacterLifeState.Agonal));
        public CharacterCapabilities CurrentCapabilities
            => movementCapability.Value.ToCapabilities();
        public TreatmentActivityState TreatmentActivity => treatmentActivity.Value;
        public int ObservedWoundCount => observedWounds.Count;
        public ObservedWoundState GetObservedWound(int index)
            => index >= 0 && index < observedWounds.Count ? observedWounds[index] : default;
        public int ProgressionEntryCount => progressionEntries.Count;
        public bool HasMedicalConsentRequest => pendingConsentHealerObjectId != 0;
        public string MedicalConsentHealerName => pendingConsentHealerName;
        public string ExternalMedicalMessage => Time.unscaledTime <= externalMedicalMessageUntil
            ? externalMedicalMessage : string.Empty;
        public string FocusedMedicalTargetName => focusedMedicalTarget == null
            ? string.Empty : focusedMedicalTarget.GetComponent<Quieter.Player.NetworkPlayer>()?.DisplayName;
        public CharacterControlKind FocusedTargetControlKind => focusedMedicalTarget == null
            ? CharacterControlKind.Player : focusedMedicalTarget.PublicSymptoms.ControlKind;
        public CharacterLifeState FocusedTargetLifeState => focusedMedicalTarget == null
            ? CharacterLifeState.Conscious : focusedMedicalTarget.PublicSymptoms.LifeState;
        public BodyPosture FocusedTargetBodyPosture => focusedMedicalTarget == null
            ? BodyPosture.FaceUp : focusedMedicalTarget.PublicSymptoms.BodyPosture;
        public NpcActivityKind FocusedTargetActivity => focusedMedicalTarget == null
            ? NpcActivityKind.Idle : focusedMedicalTarget.PublicSymptoms.Activity;
        public WorkerJobKind FocusedTargetJob => focusedMedicalTarget == null
            ? WorkerJobKind.Mining : focusedMedicalTarget.PublicSymptoms.ObservedJob;
        public bool FocusedTargetHasWorkEvidence => focusedMedicalTarget != null
            && focusedMedicalTarget.PublicSymptoms.HasWorkEvidence;
        public (byte Minimum, byte Maximum) FocusedTargetSkillRange => focusedMedicalTarget == null
            ? ((byte)0, (byte)0)
            : (focusedMedicalTarget.PublicSymptoms.ObservedSkillMinimum,
                focusedMedicalTarget.PublicSymptoms.ObservedSkillMaximum);
        public bool FocusedTargetHasPersonalRequest => focusedMedicalTarget != null
            && focusedMedicalTarget.PublicSymptoms.HasPersonalRequest;
        public NpcPersonalRequestKind FocusedTargetPersonalRequest => focusedMedicalTarget == null
            ? NpcPersonalRequestKind.Food : focusedMedicalTarget.PublicSymptoms.PersonalRequest;
        public PublicWorkerContractState FocusedTargetWorkerContract => focusedMedicalTarget == null
            ? default : focusedMedicalTarget.PublicWorkerContract;
        public ProgressionEntryState GetProgressionEntry(int index)
            => index >= 0 && index < progressionEntries.Count
                ? progressionEntries[index]
                : default;

        private void Awake()
        {
            inventory = GetComponent<PlayerInventory>();
            networkPlayer = GetComponent<Quieter.Player.NetworkPlayer>();
        }

        private void Update()
        {
            if (!IsSpawned || !IsOwner) return;
            UpdateFocusedMedicalTarget();
            var keyboard = Keyboard.current;
            if (keyboard == null) return;
            if (HasMedicalConsentRequest)
            {
                if (keyboard.yKey.wasPressedThisFrame) RespondToMedicalConsent(true);
                else if (keyboard.nKey.wasPressedThisFrame) RespondToMedicalConsent(false);
            }
            var shifted = keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed;
            var controlled = keyboard.leftCtrlKey.isPressed || keyboard.rightCtrlKey.isPressed;
            if (keyboard.xKey.wasPressedThisFrame
                && (ownerCondition.Value.Bound || ownerCondition.Value.Captive))
            {
                if (shifted && controlled) BeginVoluntaryPassingServerRpc();
                else if (ownerCondition.Value.Bound) BeginRestraintEscapeServerRpc();
            }
            if (focusedMedicalTarget == null) return;
            if (shifted)
            {
                if (keyboard.f1Key.wasPressedThisFrame)
                    RequestExternalTreatment(MedicalActionType.Warm);
                else if (keyboard.f2Key.wasPressedThisFrame)
                    RequestExternalTreatment(MedicalActionType.Cool);
                else if (keyboard.f3Key.wasPressedThisFrame)
                    RequestExternalTreatment(MedicalActionType.OralRehydration);
                else if (keyboard.f4Key.wasPressedThisFrame)
                    RequestExternalTreatment(MedicalActionType.HerbalPainRelief);
                else if (keyboard.f5Key.wasPressedThisFrame)
                    RequestExternalTreatment(MedicalActionType.DentalExtraction);
                else if (keyboard.f6Key.wasPressedThisFrame)
                    RequestExternalTreatment(MedicalActionType.AntiparasiticCourse);
                else shifted = false;
                if (shifted) return;
            }
            if (keyboard.tKey.wasPressedThisFrame)
                RequestExternalTreatment(MedicalActionType.Inspect);
            else if (keyboard.f1Key.wasPressedThisFrame)
                RequestExternalTreatment(MedicalActionType.ApplyPressure);
            else if (keyboard.f2Key.wasPressedThisFrame)
                RequestExternalTreatment(MedicalActionType.Wash);
            else if (keyboard.f3Key.wasPressedThisFrame)
                RequestExternalTreatment(MedicalActionType.Disinfect);
            else if (keyboard.f4Key.wasPressedThisFrame)
                RequestExternalTreatment(MedicalActionType.Suture);
            else if (keyboard.f5Key.wasPressedThisFrame)
                RequestExternalTreatment(MedicalActionType.Bandage);
            else if (keyboard.f6Key.wasPressedThisFrame)
                RequestExternalTreatment(MedicalActionType.Splint);
            else if (keyboard.f7Key.wasPressedThisFrame)
                RequestExternalTreatment(MedicalActionType.RemoveBandage);
            else if (keyboard.f8Key.wasPressedThisFrame)
                RequestToggleBinding();
            else if (keyboard.f9Key.wasPressedThisFrame)
            {
                if (keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed)
                    RequestTurnBody();
                else RequestToggleCarry();
            }
            else if (keyboard.f10Key.wasPressedThisFrame)
                RequestBeginCapture();
            else if (keyboard.f11Key.wasPressedThisFrame)
            {
                if (keyboard.leftCtrlKey.isPressed || keyboard.rightCtrlKey.isPressed)
                    RequestIntimidate();
                else if (keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed)
                    RequestRegisterHeir();
                else RequestOfferContract();
            }
            else if (keyboard.f12Key.wasPressedThisFrame)
            {
                if (keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed)
                    RequestDonateHeir();
                else RequestCycleNpcJob();
            }
        }

        private void UpdateFocusedMedicalTarget()
        {
            focusedMedicalTarget = null;
            var camera = GetComponent<Quieter.Player.NetworkPlayer>()?.OwnerCamera;
            if (camera == null) return;
            var ray = new Ray(camera.transform.position, camera.transform.forward);
            var hits = Physics.RaycastAll(ray, 3.75f, ~0, QueryTriggerInteraction.Ignore);
            Array.Sort(hits, (left, right) => left.distance.CompareTo(right.distance));
            foreach (var hit in hits)
            {
                var candidate = hit.collider.GetComponentInParent<PlayerSurvival>();
                if (candidate == this) continue;
                if (candidate == null) return;
                focusedMedicalTarget = candidate;
                return;
            }
        }

        public override void OnNetworkSpawn()
        {
            publicSymptoms.OnValueChanged += OnPublicSymptomsChanged;
            ownerCondition.OnValueChanged += OnOwnerConditionChanged;
            movementCapability.OnValueChanged += OnMovementCapabilityChanged;
            observedWounds.OnListChanged += OnObservedWoundsChanged;
            treatmentActivity.OnValueChanged += OnTreatmentActivityChanged;
            progressionEntries.OnListChanged += OnProgressionEntriesChanged;
            ownerHeirOffer.OnValueChanged += OnOwnerHeirOfferChanged;
            publicWorkerContract.OnValueChanged += OnPublicWorkerContractChanged;
            Changed?.Invoke();
        }

        public override void OnNetworkDespawn()
        {
            if (IsServer)
            {
                ReleaseCarriedTarget();
                if (serverCarrier != null && serverCarrier.serverCarriedTarget == this)
                    serverCarrier.ReleaseCarriedTarget();
            }
            publicSymptoms.OnValueChanged -= OnPublicSymptomsChanged;
            ownerCondition.OnValueChanged -= OnOwnerConditionChanged;
            movementCapability.OnValueChanged -= OnMovementCapabilityChanged;
            observedWounds.OnListChanged -= OnObservedWoundsChanged;
            treatmentActivity.OnValueChanged -= OnTreatmentActivityChanged;
            progressionEntries.OnListChanged -= OnProgressionEntriesChanged;
            ownerHeirOffer.OnValueChanged -= OnOwnerHeirOfferChanged;
            publicWorkerContract.OnValueChanged -= OnPublicWorkerContractChanged;
        }

        private void FixedUpdate()
        {
            if (!IsSpawned || !IsServer || serverState == null)
                return;
            TickServerCarry();
            TickServerCapture();
            if (ServerIsDead)
            {
                TickCorpseState();
                return;
            }
            if (!serverState.CreationCompleted)
            {
                return;
            }

            TryRestorePendingTreatment();
            TryRestorePendingLesson();

            accumulator += Time.fixedDeltaTime;
            if (accumulator < ServerSimulationInterval)
            {
                return;
            }

            var elapsed = accumulator;
            accumulator = 0f;
            var wasDead = ServerIsDead;
            var accidentRevision = serverState.Physiology.EliminationAccidentRevision;
            SynchronizePendingTreatmentState();
            SynchronizePendingLessonState();
            TickRestraintEscape(elapsed);
            TickVoluntaryPassing(elapsed);
            TickLesson();
            if (pendingTreatmentActive && ServerClock >= pendingTreatmentCompletesAt)
            {
                CompletePendingTreatment();
            }
            if (serverState.Offline)
            {
                serverState.Physiology.SafeOfflineSeconds += elapsed;
            }
            var slowOfflineMetabolism = serverState.Offline
                && serverState.Sleeping
                && serverState.Physiology.SafeOfflineSeconds >= 600f
                && serverState.Physiology.LifeState <= CharacterLifeState.Confused
                && serverState.Physiology.BloodVolume > 0.7f
                && serverState.Physiology.SystemicInfection < 0.6f
                && serverState.Physiology.CoreTemperatureC > 35.5f
                && serverState.Physiology.CoreTemperatureC < 39.5f;
            PhysiologySimulation.Simulate(
                serverState,
                elapsed,
                ResolveEnvironment(transform.position, elapsed),
                exertion,
                offlineMetabolismSlowed: slowOfflineMetabolism);
            if (serverState.Physiology.EliminationAccidentRevision != accidentRevision)
            {
                HandleEliminationAccident(
                    serverState.Physiology.LastEliminationAccidentWasBowel);
            }
            ApplyNearbyCorpseExposure(elapsed);
            ApplyNearbyRespiratoryExposure(elapsed);
            if (!wasDead && ServerIsDead)
            {
                CancelPendingTreatment("Лечение прервано смертью пациента или лекаря.");
                activeExternalHealer?.CancelPendingTreatment(
                    "Лечение прервано: пострадавший умер.");
                CancelVoluntaryPassingAttempt(string.Empty);
                CancelLessonActivity("Занятие прервано смертью участника.");
                EnsureCorpseState();
            }
            if (serverState.Sleeping)
            {
                serverState.Physiology.CurrentSleepSeconds += elapsed;
                if (!serverState.Physiology.SleepCycleConsolidated
                    && (serverState.Physiology.CurrentSleepSeconds >= 600f
                    || (serverState.Physiology.CurrentSleepSeconds >= 300f
                        && serverState.Physiology.SleepDebt <= 0.02f)))
                {
                    var quality = CalculateSleepQuality(serverState);
                    serverState.Physiology.SleepCycleConsolidated = true;
                    if (serverState.Offline)
                    {
                        CharacterProgression.ConsolidateSleep(
                            serverState.Progression,
                            quality);
                    }
                    else
                    {
                        PhysiologySimulation.EndSleep(serverState, quality);
                        serverState.Physiology.CurrentSleepSeconds = 0f;
                    }
                }
            }
            exertion = Mathf.MoveTowards(exertion, 0f, elapsed * 0.8f);
            ReplicateState();
            if (Time.unscaledTime >= nextProgressionReplicationAt)
            {
                nextProgressionReplicationAt = Time.unscaledTime + 5f;
                ReplicateProgression();
            }
            if (!wasDead && ServerIsDead)
            {
                ReleaseCarriedTarget();
                ServerDied?.Invoke(serverState);
            }
        }

        public void InitializeServer(CharacterSurvivalState storedState, ulong steamId, string name)
        {
            if (!IsServer)
            {
                return;
            }

            serverState = storedState ?? new CharacterSurvivalState();
            serverState.EnsureInitialized();
            if (string.IsNullOrWhiteSpace(serverState.CharacterId))
            {
                serverState.CharacterId = $"character-{steamId:x16}";
            }
            if (string.IsNullOrWhiteSpace(serverState.CharacterName))
            {
                serverState.CharacterName = string.IsNullOrWhiteSpace(name) ? "Чужак" : name;
            }
            serverSteamId = steamId;
            serverState.CarriedByCharacterId = string.Empty;
            captureCompletionRaised = false;
            if (ServerIsDead) EnsureCorpseState();
            TryRestorePendingTreatment();
            TryRestorePendingLesson();
            ReplicateState();
            ReplicateProgression();
        }

        public CharacterSurvivalState CreateServerSnapshot()
        {
            if (!IsServer || serverState == null)
            {
                return null;
            }

            // JsonUtility supplies a simple deep copy and prevents persistence from
            // observing a later tick while the save is in flight.
            SynchronizePendingTreatmentState();
            SynchronizePendingLessonState();
            return JsonUtility.FromJson<CharacterSurvivalState>(
                JsonUtility.ToJson(serverState));
        }

        public void ServerSetOffline(bool offline)
        {
            if (!IsServer || serverState == null) return;
            var changed = serverState.Offline != offline;
            if (offline && changed) CancelLessonActivity(
                "Занятие прервано: один из участников покинул мир.");
            LivingWorldSimulation.ApplyOfflinePresence(
                serverState, offline, CalculateSleepQuality(serverState));
            ReplicateState();
        }

        public void ServerWakeFromDanger()
        {
            if (!IsServer || serverState == null) return;
            serverState.Physiology.SafeOfflineSeconds = 0f;
            if (serverState.Offline)
            {
                // The disconnected body remains asleep, but dangerous exposure
                // resumes full-rate simulation for the next ten minutes.
                ReplicateState();
                return;
            }
            serverState.Sleeping = false;
            serverState.Physiology.CurrentSleepSeconds = 0f;
            serverState.Physiology.SleepCycleConsolidated = false;
            ReplicateState();
        }

        public void ServerApplyNearbyNoise(float perceivedIntensity)
        {
            if (!IsServer || serverState == null || !serverState.Sleeping
                || ServerIsDead || perceivedIntensity <= 0f) return;
            var shouldWake = PhysiologySimulation.ApplySleepNoise(
                serverState, perceivedIntensity);
            if (shouldWake && serverState.Offline)
            {
                // A disconnected body stays physically present and cannot be
                // handed control, but loud danger prevents safe slowed sleep.
                serverState.Physiology.SafeOfflineSeconds = 0f;
            }
            else if (shouldWake)
            {
                var quality = CalculateSleepQuality(serverState);
                PhysiologySimulation.EndSleep(serverState, quality);
                serverState.Physiology.CurrentSleepSeconds = 0f;
            }
            ReplicateState();
        }

        private void HandleEliminationAccident(bool bowelAccident)
        {
            inventory?.ServerSoilEquippedClothing(bowelAccident);
            if (serverState.Sleeping)
            {
                QuieterRuntimeBootstrap.Instance?.Session?.PlacedObjects
                    ?.TrySoilAssignedBed(
                        transform.position, serverState.CharacterId, bowelAccident);
                if (!serverState.Offline)
                {
                    serverState.Sleeping = false;
                    serverState.Physiology.CurrentSleepSeconds = 0f;
                    serverState.Physiology.SleepCycleConsolidated = false;
                }
            }
            SetExternalMedicalFeedbackClientRpc(
                bowelAccident
                    ? "Кишечник не выдержал. Тело, одежда и постель могли быть загрязнены."
                    : "Мочевой пузырь не выдержал. Одежда и постель могли промокнуть.",
                0,
                NetworkObjectId);
        }

        public void ServerSetExertion(float value)
        {
            if (IsServer)
            {
                exertion = Mathf.Max(exertion, Mathf.Clamp01(value));
            }
        }

        public bool ServerCorpseItemsSealed => IsServer && ServerIsDead
            && serverState.Corpse?.ItemsSealedByBurial == true;

        public bool ServerBuryCorpse(out string message)
        {
            message = string.Empty;
            if (!IsServer || !ServerIsDead)
            {
                message = "Погребать можно только мёртвое тело.";
                return false;
            }
            EnsureCorpseState();
            if (serverState.Corpse.Stage == CorpseDecayStage.Cremated)
            {
                message = "После сожжения осталось только негорючее содержимое.";
                return false;
            }
            if (serverState.Corpse.Stage == CorpseDecayStage.Buried)
            {
                message = "Останки уже погребены.";
                return false;
            }
            LivingWorldSimulation.Bury(serverState.Corpse);
            serverState.ControlKind = CharacterControlKind.Remains;
            serverState.Revision++;
            ReplicateState();
            message = "Останки погребены. Вещи запечатаны вместе с ними.";
            return true;
        }

        public bool ServerCremateCorpse(out string message)
        {
            message = string.Empty;
            if (!IsServer || !ServerIsDead)
            {
                message = "Сжечь можно только мёртвое тело.";
                return false;
            }
            EnsureCorpseState();
            if (serverState.Corpse.Stage == CorpseDecayStage.Buried)
            {
                message = "Погребённые останки сначала пришлось бы выкопать.";
                return false;
            }
            if (serverState.Corpse.Stage == CorpseDecayStage.Cremated)
            {
                message = "Останки уже сожжены.";
                return false;
            }
            inventory?.ServerDestroyOrganicItemsForCremation();
            LivingWorldSimulation.Cremate(serverState.Corpse);
            serverState.ControlKind = CharacterControlKind.Remains;
            serverState.Revision++;
            ReplicateState();
            message = "Тело сожжено. Органика уничтожена, негорючие вещи остались.";
            return true;
        }

        public void ServerSetCarriedMass(float kilograms)
        {
            if (IsServer && serverState != null)
            {
                serverState.Physiology.CarriedMassKg = Mathf.Max(0f, kilograms);
            }
        }

        public void ServerRegisterLanding(float downwardSpeed)
        {
            if (!IsServer || serverState == null || downwardSpeed < 7.5f)
            {
                return;
            }

            var severity = Mathf.InverseLerp(7.5f, 18f, downwardSpeed);
            var region = UnityEngine.Random.value < 0.5f
                ? BodyRegion.LeftFoot
                : BodyRegion.RightFoot;
            PhysiologySimulation.AddInjury(
                serverState,
                region,
                DamageKind.Fall,
                severity,
                0.05f);
            ServerWakeFromDanger();
            CharacterProgression.RegisterPractice(
                serverState.Progression,
                SkillId.Landing,
                2f,
                severity,
                0.25f,
                0f,
                TraitCatalog.Resolve(serverState.Traits));
            ReplicateState();
        }

        public TreatmentResult ServerTreat(
            uint woundId,
            MedicalActionType action,
            TreatmentContext context)
        {
            if (!IsServer || serverState == null)
            {
                return new TreatmentResult(false, "Лечение проверяет сервер.", 0f);
            }

            var result = PhysiologySimulation.Treat(serverState, woundId, action, context);
            ReplicateState();
            return result;
        }

        public bool ServerConsumeFood(
            float calories,
            float protein,
            float micronutrients,
            float waterLiters,
            float biologicalContamination,
            float toxinContamination,
            float fat = 0f,
            float minerals = 0f)
        {
            if (!CanPerformServerAction()) return false;
            PhysiologySimulation.ConsumeFood(
                serverState,
                calories,
                protein,
                micronutrients,
                biologicalContamination,
                toxinContamination,
                fat,
                minerals);
            if (waterLiters > 0f)
            {
                PhysiologySimulation.ConsumeWater(
                    serverState,
                    waterLiters,
                    biologicalContamination,
                    toxinContamination);
            }
            ReplicateState();
            return true;
        }

        public bool ServerConsumeLiquid(
            float liters,
            float biologicalContamination,
            float toxinContamination,
            float electrolyteContent,
            float hydrationEfficiency)
        {
            if (!CanPerformServerAction() || liters <= 0f) return false;
            PhysiologySimulation.ConsumeWater(
                serverState,
                liters,
                biologicalContamination,
                toxinContamination,
                electrolyteContent,
                hydrationEfficiency);
            ReplicateState();
            return true;
        }

        public void ServerApplyPreparedLiquidEffects(LiquidKind kind, float liters)
        {
            if (!IsServer || serverState == null || liters <= 0f) return;
            if (kind == LiquidKind.Broth)
            {
                PhysiologySimulation.ConsumeFood(
                    serverState,
                    180f * liters,
                    5f * liters,
                    0.18f * liters,
                    0f,
                    0f,
                    2f * liters,
                    0.14f * liters);
            }
            else if (kind == LiquidKind.HerbalInfusion)
            {
                PhysiologySimulation.ConsumeHerbalInfusion(serverState, liters);
            }
            ReplicateState();
        }

        public bool ServerNpcConsumeFood(
            float calories,
            float protein,
            float micronutrients,
            float waterLiters,
            float biologicalContamination,
            float toxinContamination,
            float fat = 0f,
            float minerals = 0f)
        {
            if (!CanPerformAutonomousServerAction()) return false;
            PhysiologySimulation.ConsumeFood(
                serverState,
                calories,
                protein,
                micronutrients,
                biologicalContamination,
                toxinContamination,
                fat,
                minerals);
            if (waterLiters > 0f)
            {
                PhysiologySimulation.ConsumeWater(
                    serverState,
                    waterLiters,
                    biologicalContamination,
                    toxinContamination);
            }
            ReplicateState();
            return true;
        }

        public bool ServerNpcConsumeLiquid(
            float liters,
            float biologicalContamination,
            float toxinContamination,
            float electrolyteContent,
            float hydrationEfficiency)
        {
            if (!CanPerformAutonomousServerAction() || liters <= 0f) return false;
            PhysiologySimulation.ConsumeWater(
                serverState,
                liters,
                biologicalContamination,
                toxinContamination,
                electrolyteContent,
                hydrationEfficiency);
            ReplicateState();
            return true;
        }

        public bool ServerReceiveCareWater(float liters)
        {
            if (!IsServer || serverState?.Physiology == null || ServerIsDead || liters <= 0f)
                return false;
            PhysiologySimulation.ConsumeWater(serverState, liters, 0f, 0f);
            ReplicateState();
            return true;
        }

        public bool ServerNpcBeginSleep()
        {
            if (!CanPerformAutonomousServerAction()) return false;
            PhysiologySimulation.BeginSleep(serverState);
            serverState.Physiology.CurrentSleepSeconds = 0f;
            ReplicateState();
            return true;
        }

        public void ServerNpcEndSleep()
        {
            if (!IsServer || serverState == null || !serverState.Sleeping) return;
            PhysiologySimulation.EndSleep(serverState, CalculateSleepQuality(serverState));
            serverState.Physiology.CurrentSleepSeconds = 0f;
            ReplicateState();
        }

        public bool ServerNpcRelieveNeeds()
        {
            if (!CanPerformAutonomousServerAction()) return false;
            var changed = false;
            if (serverState.Physiology.BladderFill >= 0.88f)
            {
                var cleanly = inventory != null && inventory.TryDepositWasteServer(350);
                PhysiologySimulation.RelieveBladder(serverState, cleanly);
                changed = true;
            }
            if (serverState.Physiology.BowelFill >= 0.9f)
            {
                var cleanly = inventory != null && inventory.TryDepositWasteServer(550);
                PhysiologySimulation.RelieveBowel(serverState, cleanly);
                changed = true;
            }
            if (changed) ReplicateState();
            return changed;
        }

        public bool ServerUseLatrine(
            PlacedObjectWorldService placedObjects,
            ulong latrineObjectId,
            out string message)
        {
            message = string.Empty;
            if (!CanPerformServerAction() || placedObjects == null)
            {
                message = "Сейчас воспользоваться уборной нельзя.";
                return false;
            }
            var bowelUrgency = serverState.Physiology.BowelFill / 0.9f;
            var bladderUrgency = serverState.Physiology.BladderFill / 0.88f;
            var bowel = bowelUrgency >= bladderUrgency;
            var fill = bowel
                ? serverState.Physiology.BowelFill
                : serverState.Physiology.BladderFill;
            if (fill < 0.08f)
            {
                message = "Сейчас в этом нет необходимости.";
                return false;
            }
            var amount = bowel ? (ushort)550 : (ushort)350;
            if (!placedObjects.TryRouteSanitaryWaste(
                    latrineObjectId, amount, 1f, bowel ? 0.18f : 0.08f,
                    out _, out message)) return false;
            if (bowel) PhysiologySimulation.RelieveBowel(serverState, cleanly: true);
            else PhysiologySimulation.RelieveBladder(serverState, cleanly: true);
            CharacterProgression.RegisterPractice(
                serverState.Progression,
                SkillId.Sanitation,
                8f,
                0.32f,
                1f,
                0f,
                TraitCatalog.Resolve(serverState.Traits));
            message = bowel
                ? "Уборная отводит отходы в выгребную яму."
                : "Уборная отводит мочу в выгребную яму.";
            ReplicateState();
            return true;
        }

        public bool ServerWashAtBasin(
            float biologicalLoad,
            float toxinLoad,
            bool usedSoap,
            bool wastewaterDrained,
            out string message)
        {
            message = string.Empty;
            if (!CanPerformServerAction())
            {
                message = "Сейчас вымыться нельзя.";
                return false;
            }
            var waterSafety = 1f - Mathf.Clamp01(
                biologicalLoad * 0.8f + toxinLoad * 0.4f);
            var cleaningPower = waterSafety * (usedSoap ? 1f : 0.48f);
            serverState.Physiology.HandCleanliness = Mathf.Lerp(
                serverState.Physiology.HandCleanliness, 1f, 0.72f * cleaningPower);
            serverState.Physiology.BodyCleanliness = Mathf.Lerp(
                serverState.Physiology.BodyCleanliness, 1f, 0.32f * cleaningPower);
            serverState.Physiology.ToxinLoad = Mathf.Clamp01(
                serverState.Physiology.ToxinLoad + toxinLoad * 0.006f);
            CharacterProgression.RegisterPractice(
                serverState.Progression,
                SkillId.Sanitation,
                10f,
                wastewaterDrained ? 0.3f : 0.16f,
                waterSafety,
                0f,
                TraitCatalog.Resolve(serverState.Traits));
            message = !wastewaterDrained
                ? "Вы вымылись, но грязная вода ушла на землю: санитарная сеть не подключена."
                : biologicalLoad > 0.08f || toxinLoad > 0.03f
                    ? "Вы вымылись, но вода была сомнительного качества."
                    : "Руки и тело тщательно вымыты; сток ушёл в выгребную яму.";
            SetTreatmentMessage(message);
            ReplicateState();
            return true;
        }

        public bool ServerNeedsCare
        {
            get
            {
                if (!IsServer || serverState?.Physiology == null || ServerIsDead) return false;
                foreach (var wound in serverState.Anatomy.Wounds)
                    if (wound != null && !wound.Healed) return true;
                return serverState.Physiology.SystemicInfection > 0.2f
                    || serverState.Physiology.Pain > 0.48f
                    || serverState.Physiology.Hydration < 0.5f
                    || serverState.Physiology.CoreTemperatureC < 35.8f
                    || serverState.Physiology.CoreTemperatureC > 38.8f
                    || serverState.Conditions.GastrointestinalInfection > 0.18f
                    || serverState.Conditions.RespiratoryInfection > 0.22f
                    || serverState.Conditions.ParasiteLoad > 0.22f;
            }
        }

        public bool ServerNpcProvideCare(PlayerSurvival patient)
        {
            if (!CanPerformAutonomousServerAction() || patient == null || patient == this
                || patient.serverState == null || patient.ServerIsDead
                || Vector3.Distance(transform.position, patient.transform.position) > 3.2f)
                return false;
            WoundState wound = null;
            foreach (var candidate in patient.serverState.Anatomy.Wounds)
            {
                if (candidate == null || candidate.Healed) continue;
                wound = candidate;
                break;
            }
            var action = wound != null
                ? !wound.PressureApplied && wound.Bleeding > 0.08f
                    ? MedicalActionType.ApplyPressure
                    : !wound.Washed && wound.IsOpen
                        ? MedicalActionType.Wash
                        : !wound.Disinfected && wound.IsOpen
                            ? MedicalActionType.Disinfect
                            : wound.IsOpen && !wound.Sutured && wound.Severity > 0.35f
                                ? MedicalActionType.Suture
                                : wound.IsFracture && !wound.Splinted
                                    ? MedicalActionType.Splint
                                    : wound.IsOpen && !wound.Bandaged
                                        ? MedicalActionType.Bandage
                                        : MedicalActionType.Inspect
                : ResolveNpcSupportiveCare(patient.serverState);
            var context = BuildTreatmentContext(action);
            var missing = MissingTreatmentMaterial(action, context);
            if (!string.IsNullOrEmpty(missing))
                return false;
            var result = wound != null
                ? PhysiologySimulation.Treat(
                    patient.serverState, wound.WoundId, action, context)
                : PhysiologySimulation.ApplySupportiveTreatment(
                    patient.serverState, action, context);
            if (!result.Success) return false;
            if (action != MedicalActionType.Inspect && !ConsumeTreatmentMaterial(action))
                return false;
            CharacterProgression.RegisterPractice(
                serverState.Progression, TreatmentSkill(action), 6f,
                wound != null ? Mathf.Clamp01(wound.Severity) : 0.45f, 1f, 0f,
                TraitCatalog.Resolve(serverState.Traits));
            patient.ReplicateState();
            ReplicateProgression();
            return true;
        }

        private static MedicalActionType ResolveNpcSupportiveCare(
            CharacterSurvivalState patient)
        {
            if (patient.Conditions.ParasiteLoad > 0.22f)
                return MedicalActionType.AntiparasiticCourse;
            if (patient.Physiology.Hydration < 0.65f
                || patient.Conditions.GastrointestinalInfection > 0.18f)
                return MedicalActionType.OralRehydration;
            if (patient.Physiology.CoreTemperatureC < 36f)
                return MedicalActionType.Warm;
            if (patient.Physiology.CoreTemperatureC > 38.4f)
                return MedicalActionType.Cool;
            return MedicalActionType.HerbalPainRelief;
        }

        public bool ServerTrySpendStamina(float amount)
        {
            if (!CanPerformServerAction() || amount <= 0f
                || serverState.Physiology.AcuteStamina < amount)
                return false;
            serverState.Physiology.AcuteStamina -= amount;
            ReplicateState();
            return true;
        }

        public bool ServerNpcTrySpendStamina(float amount)
        {
            if (!CanPerformAutonomousServerAction() || amount <= 0f
                || serverState.Physiology.AcuteStamina < amount)
                return false;
            serverState.Physiology.AcuteStamina -= amount;
            ReplicateState();
            return true;
        }

        public void ServerSoilHandsFromBlood(float sourceBiologicalLoad)
        {
            if (!IsServer || serverState?.Physiology == null) return;
            serverState.Physiology.HandCleanliness = Mathf.Min(
                serverState.Physiology.HandCleanliness,
                Mathf.Lerp(0.48f, 0.18f, Mathf.Clamp01(sourceBiologicalLoad)));
            ReplicateState();
        }

        public WoundState ServerApplyDamage(
            BodyRegion region,
            DamageKind damageKind,
            float impact,
            float contamination,
            bool internalDamage = false)
        {
            if (!IsServer || serverState == null || ServerIsDead) return null;
            CancelPendingTreatment("Лечение прервано новой травмой.");
            activeExternalHealer?.CancelPendingTreatment("Лечение прервано: пострадавший получил новую травму.");
            CancelVoluntaryPassingAttempt("Сосредоточение сорвано новой травмой.");
            CancelLessonActivity("Занятие прервано новой травмой.");
            var wound = PhysiologySimulation.AddInjury(
                serverState,
                region,
                damageKind,
                impact,
                contamination,
                internalDamage);
            ServerWakeFromDanger();
            ReplicateState();
            return wound;
        }

        public void ServerRegisterPractice(
            SkillId skill,
            float realSeconds,
            float challenge,
            float outcomeQuality,
            float repetition)
        {
            if (!IsServer || serverState == null || ServerIsDead) return;
            CharacterProgression.RegisterPractice(
                serverState.Progression,
                skill,
                realSeconds,
                challenge,
                outcomeQuality,
                repetition,
                TraitCatalog.Resolve(serverState.Traits));
        }

        public void ServerRegisterPhysicalLoad(
            CharacterAttributeId attribute,
            float realSeconds,
            float intensity)
        {
            if (!IsServer || serverState == null) return;
            CharacterProgression.RegisterPhysicalLoad(
                serverState.Progression, attribute, realSeconds, intensity);
        }

        public bool CanPerformServerAction()
            => IsServer
                && serverState != null
                && serverState.CreationCompleted
                && serverState.ControlKind == CharacterControlKind.Player
                && !serverState.Sleeping
                && !serverState.Bound
                && !HasBlockingActivity
                && serverState.Physiology.LifeState <= CharacterLifeState.Confused;

        public bool CanPerformAutonomousServerAction()
            => IsServer
                && serverState != null
                && serverState.CreationCompleted
                && serverState.ControlKind is CharacterControlKind.FreeNpc
                    or CharacterControlKind.ContractedNpc
                    or CharacterControlKind.ForcedNpc
                && !serverState.Sleeping
                && !serverState.Bound
                && !HasBlockingActivity
                && serverState.Physiology.LifeState <= CharacterLifeState.Confused;

        public void RequestTreatment(uint woundId, MedicalActionType action)
        {
            if (IsOwner && woundId != 0) BeginTreatmentServerRpc(woundId, action);
        }

        public void RequestDentalExtraction()
        {
            if (IsOwner) BeginDentalExtractionServerRpc();
        }

        public void RequestSupportiveTreatment(MedicalActionType action)
        {
            if (IsOwner && IsSupportiveMedicalAction(action))
                BeginSupportiveTreatmentServerRpc(action);
        }

        private void RequestExternalTreatment(MedicalActionType action)
        {
            if (!IsOwner || focusedMedicalTarget == null || !focusedMedicalTarget.IsSpawned) return;
            var targetsWound = action <= MedicalActionType.RemoveBandage;
            var woundId = action == MedicalActionType.Inspect || !targetsWound
                ? 0u : inspectedExternalWoundId;
            if (targetsWound && action != MedicalActionType.Inspect && woundId == 0)
            {
                SetExternalMedicalFeedback("Сначала осмотрите пострадавшего клавишей T.");
                return;
            }
            BeginExternalTreatmentServerRpc(
                new NetworkObjectReference(focusedMedicalTarget.NetworkObject), woundId, action);
        }

        private void RequestToggleBinding()
        {
            if (IsOwner && focusedMedicalTarget != null && focusedMedicalTarget.IsSpawned)
                ToggleBindingServerRpc(new NetworkObjectReference(focusedMedicalTarget.NetworkObject));
        }

        private void RequestToggleCarry()
        {
            if (IsOwner && focusedMedicalTarget != null && focusedMedicalTarget.IsSpawned)
                ToggleCarryServerRpc(new NetworkObjectReference(focusedMedicalTarget.NetworkObject));
        }

        private void RequestTurnBody()
        {
            if (IsOwner && focusedMedicalTarget != null && focusedMedicalTarget.IsSpawned)
                TurnBodyServerRpc(
                    new NetworkObjectReference(focusedMedicalTarget.NetworkObject));
        }

        private void RequestBeginCapture()
        {
            if (IsOwner && focusedMedicalTarget != null && focusedMedicalTarget.IsSpawned)
                BeginCaptureServerRpc(new NetworkObjectReference(focusedMedicalTarget.NetworkObject));
        }

        private void RequestOfferContract()
        {
            if (IsOwner && focusedMedicalTarget != null && focusedMedicalTarget.IsSpawned)
                OfferContractServerRpc(new NetworkObjectReference(focusedMedicalTarget.NetworkObject));
        }

        private void RequestIntimidate()
        {
            if (IsOwner && focusedMedicalTarget != null && focusedMedicalTarget.IsSpawned)
                IntimidateNpcServerRpc(
                    new NetworkObjectReference(focusedMedicalTarget.NetworkObject));
        }

        private void RequestRegisterHeir()
        {
            if (IsOwner && focusedMedicalTarget != null && focusedMedicalTarget.IsSpawned)
                RegisterHeirServerRpc(
                    new NetworkObjectReference(focusedMedicalTarget.NetworkObject));
        }

        private void RequestDonateHeir()
        {
            if (IsOwner && focusedMedicalTarget != null && focusedMedicalTarget.IsSpawned)
                DonateHeirServerRpc(
                    new NetworkObjectReference(focusedMedicalTarget.NetworkObject));
        }

        public void RequestAcceptHeirOffer()
        {
            if (IsOwner && ownerHeirOffer.Value.Available) AcceptHeirOfferServerRpc();
        }

        private void RequestCycleNpcJob()
        {
            if (IsOwner && focusedMedicalTarget != null && focusedMedicalTarget.IsSpawned)
                ConfigureWorkerServerRpc(
                    new NetworkObjectReference(focusedMedicalTarget.NetworkObject),
                    WorkerContractAction.SelectNextJob);
        }

        public void RequestConfigureFocusedWorker(WorkerContractAction action)
        {
            if (IsOwner && focusedMedicalTarget != null && focusedMedicalTarget.IsSpawned)
                ConfigureWorkerServerRpc(
                    new NetworkObjectReference(focusedMedicalTarget.NetworkObject), action);
        }

        [ServerRpc]
        private void OfferContractServerRpc(NetworkObjectReference targetReference)
        {
            if (!CanPerformServerAction() || serverSteamId == 0
                || !TryResolveNearbyTarget(targetReference, out var target))
            {
                SetExternalMedicalFeedbackClientRpc(
                    "Разговор сейчас невозможен.", 0, 0);
                return;
            }
            if (target.serverState.ControlKind == CharacterControlKind.ContractedNpc
                && target.serverState.Npc?.EmployerAccountId == serverSteamId.ToString())
            {
                FulfillPersonalRequest(target);
                return;
            }
            if (target.serverState.ControlKind != CharacterControlKind.FreeNpc
                || target.serverState.Npc?.Disposition != NpcDisposition.Passive)
            {
                SetExternalMedicalFeedbackClientRpc(
                    "Добровольный договор можно предложить только свободному мирному человеку.",
                    0, target.NetworkObjectId);
                return;
            }
            var relationship = FindOrCreateRelationship(
                target.serverState, target.serverState.CharacterId, serverState.CharacterId);
            var nowUtcTicks = DateTime.UtcNow.Ticks;
            if (relationship.NextSocialAttemptUtcTicks > nowUtcTicks)
            {
                SetExternalMedicalFeedbackClientRpc(
                    "Собеседнику нужно время обдумать сказанное.", 0,
                    target.NetworkObjectId);
                return;
            }
            if (inventory == null || !inventory.HasServerItem(26)
                && !inventory.HasServerItem(25))
            {
                SetExternalMedicalFeedbackClientRpc(
                    "Для серьёзного разговора нужен съедобный паёк: коренья или ягоды.",
                    0, target.NetworkObjectId);
                return;
            }
            var placed = FindAnyObjectByType<PlacedObjectWorldService>();
            var bedId = 0UL;
            var bedError = "Система построек недоступна.";
            if (placed == null || !placed.TryAssignNearestOwnedBed(
                    target.transform.position, serverSteamId.ToString(),
                    target.serverState.CharacterId, out bedId, out bedError))
            {
                SetExternalMedicalFeedbackClientRpc(bedError, 0, target.NetworkObjectId);
                return;
            }
            if (!inventory.TryConsumeAnyItemServer(26)
                && !inventory.TryConsumeAnyItemServer(25))
            {
                SetExternalMedicalFeedbackClientRpc(
                    "Паёк исчез до заключения договора.", 0, target.NetworkObjectId);
                return;
            }
            var accepted = LivingWorldSimulation.AdvancePersuasion(
                serverState, target.serverState, relationship, nowUtcTicks, out _);
            CharacterProgression.RegisterPractice(
                serverState.Progression, SkillId.Persuasion,
                30f, 0.38f, accepted ? 1f : 0.55f,
                Mathf.Clamp01(relationship.PersuasionAttempts / 12f),
                TraitCatalog.Resolve(serverState.Traits));
            target.ReplicateState();
            ReplicateProgression();
            if (!accepted)
            {
                SetExternalMedicalFeedbackClientRpc(
                    relationship.Trust < 0.35f
                        ? "Человек выслушал предложение, но пока не доверяет вам. Паёк принят."
                        : "Предложение уже воспринимают всерьёз, но решение ещё не принято.",
                    0, target.NetworkObjectId);
                return;
            }
            relationship.Loyalty = Mathf.Clamp01(relationship.Loyalty + 0.12f);
            relationship.VoluntaryLoyalty = true;
            target.serverState.ControlKind = CharacterControlKind.ContractedNpc;
            target.serverState.Npc.EmployerAccountId = serverSteamId.ToString();
            target.serverState.Npc.EmployerCharacterId = serverState.CharacterId;
            target.serverState.Npc.HomePosition = target.transform.position;
            target.serverState.Npc.ActiveJob = WorkerJobKind.Foraging;
            target.serverState.Npc.WorkbookSelectedJob = WorkerJobKind.Foraging;
            target.serverState.WorkerContract = new WorkerContractState
            {
                ContractId = Guid.NewGuid().ToString("D"),
                EmployerAccountId = serverSteamId.ToString(),
                WorkerCharacterId = target.serverState.CharacterId,
                AssignedBedObjectId = bedId.ToString(),
                Active = true,
                Voluntary = true,
                DailyRationCalories = 1800f,
                PromisedSafety = 0.6f,
                WorkdayStartHour = 8f,
                WorkdayEndHour = 18f,
                PaymentItemId = 25,
                PaymentQuantity = 2,
                WorkZoneCenter = target.transform.position,
                WorkZoneRadius = 70f,
                StoragePosition = target.transform.position,
                AllowedJobs = new List<WorkerJobKind> { WorkerJobKind.Foraging },
                JobPriorities = new byte[] { 0, 0, 2, 0, 0, 0, 0, 0 },
            };
            var gameDay = CurrentGameDay();
            target.serverState.Npc.PersonalRequest = LivingWorldSimulation.SelectPersonalRequest(
                target.serverState.CharacterId, gameDay);
            target.serverState.Npc.PersonalRequestPending = true;
            target.serverState.Npc.PersonalRequestGameDay = gameDay;
            target.ReplicateState();
            ReplicateProgression();
            SetExternalMedicalFeedbackClientRpc(
                $"Договор принят, кровать закреплена. Первый личный запрос: "
                + $"{PersonalRequestName(target.serverState.Npc.PersonalRequest)}.",
                0, target.NetworkObjectId);
        }

        [ServerRpc]
        private void IntimidateNpcServerRpc(NetworkObjectReference targetReference)
        {
            if (!CanPerformServerAction()
                || !TryResolveNearbyTarget(targetReference, out var target)
                || target.serverState.ControlKind != CharacterControlKind.FreeNpc
                || target.serverState.Npc == null || target.ServerIsDead)
            {
                SetExternalMedicalFeedbackClientRpc(
                    "Запугивание сейчас невозможно.", 0, 0);
                return;
            }
            var relationship = FindOrCreateRelationship(
                target.serverState, target.serverState.CharacterId, serverState.CharacterId);
            var nowUtcTicks = DateTime.UtcNow.Ticks;
            if (relationship.NextSocialAttemptUtcTicks > nowUtcTicks)
            {
                SetExternalMedicalFeedbackClientRpc(
                    "Цель ещё реагирует на предыдущую угрозу.", 0,
                    target.NetworkObjectId);
                return;
            }
            var surrendered = LivingWorldSimulation.AdvanceIntimidation(
                serverState, target.serverState, relationship, nowUtcTicks, out _);
            CharacterProgression.RegisterPractice(
                serverState.Progression, SkillId.Intimidation,
                20f, target.serverState.Npc.Disposition == NpcDisposition.Aggressive
                    ? 0.62f : 0.34f,
                surrendered ? 1f : 0.35f,
                Mathf.Clamp01(relationship.IntimidationAttempts / 10f),
                TraitCatalog.Resolve(serverState.Traits));
            if (surrendered)
            {
                target.serverState.Npc.Disposition = NpcDisposition.Passive;
                target.serverState.Npc.Activity = NpcActivityKind.Idle;
                SetExternalMedicalFeedbackClientRpc(
                    "Человек отступил и сдался. Это страх, а не добровольная лояльность.",
                    0, target.NetworkObjectId);
            }
            else
            {
                SetExternalMedicalFeedbackClientRpc(
                    target.serverState.Npc.Disposition == NpcDisposition.Aggressive
                        ? "Угроза не сломила противника. Он остаётся опасен."
                        : "Человек испугался, но не подчинился.",
                    0, target.NetworkObjectId);
            }
            target.ReplicateState();
            ReplicateProgression();
        }

        [ServerRpc]
        private void DonateHeirServerRpc(NetworkObjectReference targetReference)
        {
            if (!CanPerformServerAction() || serverSteamId == 0
                || !targetReference.TryGet(out var targetObject)
                || targetObject == NetworkObject
                || !targetObject.TryGetComponent<PlayerSurvival>(out var recipient)
                || recipient.serverState == null || !recipient.ServerIsLifeLost
                || Vector3.Distance(transform.position, recipient.transform.position) > 3.75f
                || !HasInteractionLine(recipient))
            {
                SetExternalMedicalFeedbackClientRpc(
                    "Передать наследника можно только аккаунту, потерявшему это тело рядом с вами.",
                    0, 0);
                return;
            }
            if (ServerHeirDonationRequested == null)
            {
                SetExternalMedicalFeedbackClientRpc(
                    "Хранилище предложений наследника недоступно.", 0,
                    recipient.NetworkObjectId);
                return;
            }
            ServerHeirDonationRequested.Invoke(this, recipient);
        }

        [ServerRpc]
        private void AcceptHeirOfferServerRpc()
        {
            if (!ServerIsLifeLost || !ownerHeirOffer.Value.Available
                || DateTime.UtcNow.Ticks >= ownerHeirOffer.Value.ExpiresAtUtcTicks)
                return;
            ServerHeirOfferAcceptanceRequested?.Invoke(this);
        }

        public void ServerSetPendingHeirOffer(PendingHeirOffer offer)
        {
            if (!IsServer) return;
            var previous = ownerHeirOffer.Value;
            ownerHeirOffer.Value = offer == null
                ? new OwnerHeirOfferState { Revision = (ushort)(previous.Revision + 1) }
                : new OwnerHeirOfferState
                {
                    Available = true,
                    OfferId = new FixedString64Bytes(offer.OfferId ?? string.Empty),
                    DonorName = new FixedString64Bytes(offer.DonorDisplayName ?? "Другой игрок"),
                    HeirName = new FixedString64Bytes(offer.HeirName ?? "Наследник"),
                    ExpiresAtUtcTicks = offer.ExpiresAtUtc.ToUniversalTime().Ticks,
                    Revision = (ushort)(previous.Revision + 1),
                };
        }

        private void FulfillPersonalRequest(PlayerSurvival target)
        {
            var npc = target.serverState.Npc;
            if (npc == null || !npc.PersonalRequestPending)
            {
                SetExternalMedicalFeedbackClientRpc(
                    "Сейчас у работника нет личного запроса.", 0, target.NetworkObjectId);
                return;
            }
            var fulfilled = npc.PersonalRequest switch
            {
                NpcPersonalRequestKind.Food => TransferFirstAvailable(
                    target.inventory, 26, 25, 27),
                NpcPersonalRequestKind.CleanWater => GiveCleanWater(target),
                NpcPersonalRequestKind.Medicine => TransferFirstAvailable(
                    target.inventory, 28, 32, 31),
                NpcPersonalRequestKind.Tool => TransferFirstAvailable(
                    target.inventory, 4, 5, 22),
                _ => false,
            };
            if (!fulfilled)
            {
                SetExternalMedicalFeedbackClientRpc(
                    $"Для запроса «{PersonalRequestName(npc.PersonalRequest)}» нет подходящего предмета или места.",
                    0, target.NetworkObjectId);
                return;
            }
            var relationship = FindOrCreateRelationship(
                target.serverState, target.serverState.CharacterId, serverState.CharacterId);
            relationship.PersonalRequestsCompleted++;
            relationship.Trust = Mathf.Clamp01(relationship.Trust + 0.08f);
            relationship.Loyalty = Mathf.Clamp01(relationship.Loyalty + 0.07f);
            relationship.VoluntaryLoyalty = target.serverState.WorkerContract?.Voluntary == true;
            npc.PersonalRequestPending = false;
            npc.LastPersonalRequestCompletedGameDay = CurrentGameDay();
            target.ReplicateState();
            var compassRewarded = relationship.PersonalRequestsCompleted >= 3
                && target.inventory != null && inventory != null
                && target.inventory.TryTransferAnyItemServer(inventory, 41);
            SetExternalMedicalFeedbackClientRpc(
                compassRewarded
                    ? "Личный запрос исполнен. В знак доверия вам передан редкий компас."
                    : $"Личный запрос исполнен ({relationship.PersonalRequestsCompleted}/3 для наследования).",
                0, target.NetworkObjectId);
        }

        private bool TransferFirstAvailable(PlayerInventory destination, params ushort[] itemIds)
        {
            if (inventory == null || destination == null) return false;
            foreach (var itemId in itemIds)
            {
                if (inventory.TryTransferAnyItemServer(destination, itemId)) return true;
            }
            return false;
        }

        private bool GiveCleanWater(PlayerSurvival target)
        {
            if (inventory == null || target == null
                || !inventory.TryConsumeServerCleanWater(500, out _)) return false;
            return target.ServerReceiveCareWater(0.5f);
        }

        [ServerRpc]
        private void RegisterHeirServerRpc(NetworkObjectReference targetReference)
        {
            if (!CanPerformServerAction() || serverSteamId == 0
                || !TryResolveNearbyTarget(targetReference, out var target)
                || target.serverState.ControlKind != CharacterControlKind.ContractedNpc
                || target.serverState.WorkerContract?.Active != true
                || target.serverState.Npc?.EmployerAccountId != serverSteamId.ToString())
            {
                SetExternalMedicalFeedbackClientRpc(
                    "Наследником можно назначить только своего добровольного работника.", 0, 0);
                return;
            }
            if (ServerHeirRegistrationRequested == null)
            {
                SetExternalMedicalFeedbackClientRpc(
                    "Хранилище наследования недоступно.", 0, target.NetworkObjectId);
                return;
            }
            ServerHeirRegistrationRequested.Invoke(this, target);
        }

        public void ServerNotifyHeirRegistrationResult(string message, ulong targetObjectId = 0)
        {
            if (IsServer) SetExternalMedicalFeedbackClientRpc(message, 0, targetObjectId);
        }

        private long CurrentGameDay()
        {
            var weather = FindAnyObjectByType<WorldWeatherService>();
            return weather == null ? 0L : (long)Math.Floor(
                weather.Current.GameSeconds / LivingWorldSimulation.GameSecondsPerDay);
        }

        private static string PersonalRequestName(NpcPersonalRequestKind request) => request switch
        {
            NpcPersonalRequestKind.Food => "еда в личный запас",
            NpcPersonalRequestKind.CleanWater => "пол-литра чистой воды",
            NpcPersonalRequestKind.Medicine => "лекарственные травы или чистая ткань",
            NpcPersonalRequestKind.Tool => "личный рабочий инструмент",
            _ => "помощь",
        };

        private static string BodyPostureName(BodyPosture posture) => posture switch
        {
            BodyPosture.FaceUp => "на спине",
            BodyPosture.RightSide => "на правом боку",
            BodyPosture.FaceDown => "на животе",
            BodyPosture.LeftSide => "на левом боку",
            _ => "неясно",
        };

        [ServerRpc]
        private void ConfigureWorkerServerRpc(
            NetworkObjectReference targetReference,
            WorkerContractAction action)
        {
            if (!CanPerformServerAction() || !TryResolveNearbyTarget(targetReference, out var target)
                || target.serverState.Npc == null || target.serverState.WorkerContract?.Active != true
                || target.serverState.Npc.EmployerAccountId != serverSteamId.ToString())
            {
                SetExternalMedicalFeedbackClientRpc(
                    "Менять работу можно только у своего действующего работника.", 0, 0);
                return;
            }
            var npc = target.serverState.Npc;
            var contract = target.serverState.WorkerContract;
            contract.EnsureInitialized();
            switch (action)
            {
                case WorkerContractAction.SelectNextJob:
                    npc.WorkbookSelectedJob = (WorkerJobKind)(
                        ((int)npc.WorkbookSelectedJob + 1)
                        % Enum.GetValues(typeof(WorkerJobKind)).Length);
                    break;
                case WorkerContractAction.RaiseSelectedPriority:
                    LivingWorldSimulation.AdjustJobPriority(
                        contract, npc.WorkbookSelectedJob, 1);
                    break;
                case WorkerContractAction.LowerSelectedPriority:
                    LivingWorldSimulation.AdjustJobPriority(
                        contract, npc.WorkbookSelectedJob, -1);
                    break;
                case WorkerContractAction.SetWorkZoneHere:
                    contract.WorkZoneCenter = transform.position;
                    break;
                case WorkerContractAction.SetStorageHere:
                    contract.StoragePosition = transform.position;
                    break;
                case WorkerContractAction.NarrowWorkZone:
                    contract.WorkZoneRadius = Mathf.Max(10f, contract.WorkZoneRadius - 10f);
                    break;
                case WorkerContractAction.WidenWorkZone:
                    contract.WorkZoneRadius = Mathf.Min(150f, contract.WorkZoneRadius + 10f);
                    break;
                case WorkerContractAction.StartEarlier:
                    contract.WorkdayStartHour = Mathf.Repeat(contract.WorkdayStartHour - 1f, 24f);
                    break;
                case WorkerContractAction.StartLater:
                    contract.WorkdayStartHour = Mathf.Repeat(contract.WorkdayStartHour + 1f, 24f);
                    break;
                case WorkerContractAction.EndEarlier:
                    contract.WorkdayEndHour = Mathf.Repeat(contract.WorkdayEndHour - 1f, 24f);
                    break;
                case WorkerContractAction.EndLater:
                    contract.WorkdayEndHour = Mathf.Repeat(contract.WorkdayEndHour + 1f, 24f);
                    break;
                case WorkerContractAction.IncreaseRation:
                    contract.DailyRationCalories = Mathf.Min(
                        4000f, contract.DailyRationCalories + 200f);
                    break;
                case WorkerContractAction.DecreaseRation:
                    contract.DailyRationCalories = Mathf.Max(
                        800f, contract.DailyRationCalories - 200f);
                    break;
                case WorkerContractAction.CyclePayment:
                    if (contract.PaymentItemId == 25)
                    {
                        contract.PaymentItemId = 26;
                        contract.PaymentQuantity = 2;
                    }
                    else if (contract.PaymentItemId == 26)
                    {
                        contract.PaymentItemId = 28;
                        contract.PaymentQuantity = 1;
                    }
                    else
                    {
                        contract.PaymentItemId = 25;
                        contract.PaymentQuantity = 2;
                    }
                    break;
                case WorkerContractAction.BeginLesson:
                    BeginLessonActivity(target, ObservableWorkSkill(npc.WorkbookSelectedJob));
                    return;
                default:
                    return;
            }
            RegisterLeadershipInstruction(target, action);
            target.ReplicateState();
            SetExternalMedicalFeedbackClientRpc(
                $"Рабочая книга обновлена: {WorkerJobName(npc.WorkbookSelectedJob)}, "
                + $"приоритет {LivingWorldSimulation.GetJobPriority(contract, npc.WorkbookSelectedJob)}.",
                0, target.NetworkObjectId);
        }

        private void RegisterLeadershipInstruction(
            PlayerSurvival target,
            WorkerContractAction action)
        {
            var npc = target?.serverState?.Npc;
            if (npc == null) return;
            var now = DateTime.UtcNow.Ticks;
            var elapsed = npc.LastLeadershipPracticeUtcTicks <= 0
                ? double.MaxValue
                : TimeSpan.FromTicks(Math.Max(
                    0, now - npc.LastLeadershipPracticeUtcTicks)).TotalSeconds;
            if (elapsed < 15d) return;

            var sameInstruction = npc.LastLeadershipAction == (byte)action;
            npc.LastLeadershipAction = (byte)action;
            npc.LastLeadershipPracticeUtcTicks = now;
            npc.LeadershipInstructions++;
            CharacterProgression.RegisterPractice(
                serverState.Progression,
                SkillId.Leadership,
                10f,
                Mathf.Clamp01(0.32f
                    + target.serverState.WorkerContract.ConsecutiveBreaches * 0.06f),
                target.serverState.WorkerContract.Voluntary ? 1f : 0.55f,
                Mathf.Clamp01(npc.LeadershipInstructions / 24f
                    + (sameInstruction ? 0.45f : 0f)),
                TraitCatalog.Resolve(serverState.Traits));
            ReplicateProgression();
        }

        private void BeginLessonActivity(PlayerSurvival student, SkillId skill)
        {
            if (student == null || student.serverState == null
                || student.ServerIsDead || student == this
                || !student.CanPerformAutonomousServerAction()
                || serverState.LessonActivity?.Active == true
                || student.serverState.LessonActivity?.Active == true)
            {
                SetExternalMedicalFeedbackClientRpc(
                    "Занятие сейчас невозможно: оба участника должны быть свободны и способны действовать.",
                    0, student?.NetworkObjectId ?? 0);
                return;
            }

            serverState.LessonActivity ??= new PersistentLessonActivityState();
            student.serverState.LessonActivity ??= new PersistentLessonActivityState();
            SetLessonState(
                serverState.LessonActivity, true, student.serverState.CharacterId,
                skill, LessonDurationSeconds);
            SetLessonState(
                student.serverState.LessonActivity, false, serverState.CharacterId,
                skill, LessonDurationSeconds);
            lessonCounterpart = student;
            student.lessonCounterpart = this;
            lessonCompletesAt = ServerClock + LessonDurationSeconds;
            student.serverState.Npc.Activity = NpcActivityKind.Idle;
            student.networkPlayer?.ServerClearAutonomousInput();
            SetTreatmentMessage(
                $"Вы проводите практическое занятие: {SkillNameForMessage(skill)}…");
            student.ReplicateState();
            ReplicateState();
            SetExternalMedicalFeedbackClientRpc(
                $"Начато двухминутное практическое занятие: {SkillNameForMessage(skill)}. "
                + "Оба участника должны оставаться рядом.",
                0, student.NetworkObjectId);
        }

        private static void SetLessonState(
            PersistentLessonActivityState activity,
            bool instructor,
            string counterpartCharacterId,
            SkillId skill,
            float remainingSeconds)
        {
            activity.Active = true;
            activity.Instructor = instructor;
            activity.CounterpartCharacterId = counterpartCharacterId ?? string.Empty;
            activity.Skill = skill;
            activity.RemainingSeconds = Mathf.Max(0f, remainingSeconds);
        }

        private static string SkillNameForMessage(SkillId skill) => skill switch
        {
            SkillId.Mining => "добыча",
            SkillId.Woodcutting => "рубка",
            SkillId.Foraging => "собирательство",
            SkillId.LoadCarrying => "перенос грузов",
            SkillId.Construction => "строительство",
            SkillId.Cooking => "готовка",
            SkillId.Sanitation => "санитария",
            SkillId.Nursing => "уход за больными",
            _ => "практический навык",
        };

        private void TryRestorePendingLesson()
        {
            var activity = serverState?.LessonActivity;
            if (activity?.Active != true || lessonCounterpart != null
                || string.IsNullOrWhiteSpace(activity.CounterpartCharacterId))
                return;
            PlayerSurvival counterpart = null;
            foreach (var candidate in FindObjectsByType<PlayerSurvival>())
            {
                if (candidate == null || candidate == this
                    || candidate.serverState?.CharacterId
                        != activity.CounterpartCharacterId) continue;
                counterpart = candidate;
                break;
            }
            if (counterpart == null) return;

            lessonCounterpart = counterpart;
            counterpart.lessonCounterpart = this;
            if (activity.Instructor)
            {
                counterpart.serverState.LessonActivity ??=
                    new PersistentLessonActivityState();
                var reciprocal = counterpart.serverState.LessonActivity;
                if (!reciprocal.Active || reciprocal.Instructor
                    || reciprocal.CounterpartCharacterId != serverState.CharacterId)
                {
                    SetLessonState(
                        reciprocal, false, serverState.CharacterId,
                        activity.Skill, activity.RemainingSeconds);
                }
                lessonCompletesAt = ServerClock
                    + Mathf.Max(0.1f, activity.RemainingSeconds);
                counterpart.ReplicateState();
            }
            else
            {
                var instructorActivity = counterpart.serverState?.LessonActivity;
                if (instructorActivity?.Active == true
                    && instructorActivity.Instructor
                    && instructorActivity.CounterpartCharacterId == serverState.CharacterId)
                {
                    counterpart.lessonCompletesAt = counterpart.ServerClock
                        + Mathf.Max(0.1f, instructorActivity.RemainingSeconds);
                }
            }
        }

        private void SynchronizePendingLessonState()
        {
            var activity = serverState?.LessonActivity;
            if (activity?.Active != true || !activity.Instructor
                || lessonCounterpart == null) return;
            var remaining = Mathf.Max(
                0f, (float)(lessonCompletesAt - ServerClock));
            activity.RemainingSeconds = remaining;
            if (lessonCounterpart.serverState?.LessonActivity?.Active == true)
                lessonCounterpart.serverState.LessonActivity.RemainingSeconds = remaining;
        }

        private void TickLesson()
        {
            var activity = serverState?.LessonActivity;
            if (activity?.Active != true || !activity.Instructor) return;
            var student = lessonCounterpart;
            var studentActivity = student?.serverState?.LessonActivity;
            var invalid = student == null || !student.IsSpawned || student.ServerIsDead
                || studentActivity?.Active != true || studentActivity.Instructor
                || studentActivity.CounterpartCharacterId != serverState.CharacterId
                || studentActivity.Skill != activity.Skill
                || Vector3.Distance(transform.position, student.transform.position) > 3.75f
                || serverState.Sleeping || serverState.Offline || serverState.Bound
                || student.serverState.Sleeping || student.serverState.Offline
                || student.serverState.Bound
                || serverState.Physiology.LifeState > CharacterLifeState.Confused
                || student.serverState.Physiology.LifeState > CharacterLifeState.Confused;
            if (invalid)
            {
                CancelLessonActivity(
                    "Занятие прервано: участники больше не могут продолжать рядом.");
                return;
            }
            if (ServerClock < lessonCompletesAt) return;
            CompleteLessonActivity(student, activity.Skill);
        }

        private void CompleteLessonActivity(PlayerSurvival student, SkillId skill)
        {
            var studentState = student.serverState;
            studentState.Npc.EnsureInitialized();
            var teachingLevel = CharacterProgression.GetSkillLevel(
                serverState.Progression, SkillId.Teaching);
            var instructorLevel = CharacterProgression.GetSkillLevel(
                serverState.Progression, skill);
            var studentLevel = CharacterProgression.GetSkillLevel(
                studentState.Progression, skill);
            var lessonCount = studentState.Npc.LessonsReceived[(int)skill];
            var voluntary = studentState.WorkerContract?.Voluntary == true;
            var quality = LivingWorldSimulation.CalculateLessonQuality(
                teachingLevel, instructorLevel, studentLevel, voluntary);
            var challenge = LivingWorldSimulation.CalculateLessonChallenge(
                instructorLevel, studentLevel);
            var repetition = Mathf.Clamp01(lessonCount / 10f);

            student.ServerRegisterPractice(
                skill, LessonDurationSeconds, challenge, quality, repetition);
            CharacterProgression.RegisterPractice(
                serverState.Progression,
                SkillId.Teaching,
                LessonDurationSeconds,
                Mathf.Clamp01(0.38f + studentLevel * 0.045f),
                quality,
                Mathf.Clamp01(serverState.LessonActivity.CompletedLessons / 20f),
                TraitCatalog.Resolve(serverState.Traits));
            studentState.Npc.LessonsReceived[(int)skill]++;
            serverState.LessonActivity.CompletedLessons++;
            studentState.LessonActivity.CompletedLessons++;

            ClearLessonState(serverState.LessonActivity);
            ClearLessonState(studentState.LessonActivity);
            lessonCounterpart = null;
            student.lessonCounterpart = null;
            SetTreatmentMessage(
                $"Практическое занятие завершено: {SkillNameForMessage(skill)}.");
            student.SetTreatmentMessage("Практическое занятие завершено.");
            ReplicateProgression();
            student.ReplicateProgression();
            ReplicateState();
            student.ReplicateState();
            SetExternalMedicalFeedbackClientRpc(
                quality >= 0.65f
                    ? "Ученик хорошо усвоил практическое занятие. Часть опыта закрепится после сна."
                    : "Занятие завершено, но нехватка опыта учителя или мотивации ограничила результат.",
                0, student.NetworkObjectId);
        }

        private void CancelLessonActivity(string message)
        {
            var activity = serverState?.LessonActivity;
            if (activity?.Active != true) return;
            var counterpart = lessonCounterpart;
            ClearLessonState(activity);
            lessonCounterpart = null;
            if (counterpart?.serverState?.LessonActivity?.Active == true
                && counterpart.serverState.LessonActivity.CounterpartCharacterId
                    == serverState.CharacterId)
            {
                ClearLessonState(counterpart.serverState.LessonActivity);
                counterpart.lessonCounterpart = null;
                if (!string.IsNullOrWhiteSpace(message))
                    counterpart.SetTreatmentMessage(message);
                counterpart.ReplicateState();
            }
            if (!string.IsNullOrWhiteSpace(message)) SetTreatmentMessage(message);
            ReplicateState();
        }

        private static void ClearLessonState(PersistentLessonActivityState activity)
        {
            if (activity == null) return;
            activity.Active = false;
            activity.Instructor = false;
            activity.CounterpartCharacterId = string.Empty;
            activity.RemainingSeconds = 0f;
        }

        private bool TryResolveNearbyTarget(
            NetworkObjectReference targetReference,
            out PlayerSurvival target)
        {
            target = null;
            return targetReference.TryGet(out var targetObject)
                && targetObject != NetworkObject
                && targetObject.TryGetComponent(out target)
                && target.serverState != null
                && !target.ServerIsDead
                && Vector3.Distance(transform.position, target.transform.position) <= 3.75f
                && HasInteractionLine(target);
        }

        private static RelationshipState FindOrCreateRelationship(
            CharacterSurvivalState state,
            string sourceCharacterId,
            string targetCharacterId)
        {
            state.Relationships ??= new List<RelationshipState>();
            foreach (var relationship in state.Relationships)
            {
                if (relationship != null
                    && relationship.SourceCharacterId == sourceCharacterId
                    && relationship.TargetCharacterId == targetCharacterId)
                    return relationship;
            }
            var created = new RelationshipState
            {
                SourceCharacterId = sourceCharacterId,
                TargetCharacterId = targetCharacterId,
            };
            state.Relationships.Add(created);
            return created;
        }

        private static string WorkerJobName(WorkerJobKind job) => job switch
        {
            WorkerJobKind.Mining => "добыча",
            WorkerJobKind.Logging => "рубка",
            WorkerJobKind.Foraging => "сбор",
            WorkerJobKind.Hauling => "переноска",
            WorkerJobKind.Construction => "строительство",
            WorkerJobKind.CookingAndWater => "готовка и вода",
            WorkerJobKind.Sanitation => "санитария",
            WorkerJobKind.PatientCare => "уход за больными",
            _ => "ожидание",
        };

        private static SkillId ObservableWorkSkill(WorkerJobKind job) => job switch
        {
            WorkerJobKind.Mining => SkillId.Mining,
            WorkerJobKind.Logging => SkillId.Woodcutting,
            WorkerJobKind.Foraging => SkillId.Foraging,
            WorkerJobKind.Hauling => SkillId.LoadCarrying,
            WorkerJobKind.Construction => SkillId.Construction,
            WorkerJobKind.CookingAndWater => SkillId.Cooking,
            WorkerJobKind.Sanitation => SkillId.Sanitation,
            WorkerJobKind.PatientCare => SkillId.Nursing,
            _ => SkillId.Foraging,
        };

        [ServerRpc]
        private void ToggleBindingServerRpc(NetworkObjectReference targetReference)
        {
            if (!CanPerformServerAction() || !targetReference.TryGet(out var targetObject)
                || targetObject == NetworkObject
                || !targetObject.TryGetComponent<PlayerSurvival>(out var target)
                || target.serverState == null
                || Vector3.Distance(transform.position, target.transform.position) > 3.75f
                || !HasInteractionLine(target)) return;
            var actorId = serverState.CharacterId;
            if (target.serverState.Bound)
            {
                if (target.serverState.CaptorCharacterId != actorId) return;
                if (inventory != null && !inventory.TryGiveServerItem(3))
                {
                    SetExternalMedicalFeedbackClientRpc(
                        "Некуда сложить снятую верёвку.", 0, target.NetworkObjectId);
                    return;
                }
                target.serverState.Bound = false;
                target.serverState.Restraint = new RestraintState();
                target.CancelCaptureState();
                target.serverState.CaptorCharacterId = string.Empty;
                target.ReplicateState();
                SetExternalMedicalFeedbackClientRpc("Путы сняты.", 0, target.NetworkObjectId);
                return;
            }
            var immobile = target.serverState.Sleeping
                || target.serverState.Physiology.LifeState >= CharacterLifeState.Unconscious
                || !PhysiologySimulation.CalculateCapabilities(target.serverState).CanMove;
            if (!immobile)
            {
                SetExternalMedicalFeedbackClientRpc(
                    "Связать можно только спящего или обездвиженного человека.", 0,
                    target.NetworkObjectId);
                return;
            }
            if (inventory == null || !inventory.TryConsumeAnyItemServer(3))
            {
                SetExternalMedicalFeedbackClientRpc("Для пут нужна верёвка.", 0, target.NetworkObjectId);
                return;
            }
            target.serverState.Bound = true;
            var binderStrength = serverState.Progression.Attributes[
                (int)CharacterAttributeId.Strength] / 100f;
            var binderFineMotor = serverState.Progression.Attributes[
                (int)CharacterAttributeId.FineMotorControl] / 100f;
            target.serverState.Restraint = new RestraintState
            {
                Integrity = Mathf.Clamp01(
                    0.72f + binderStrength * 0.16f + binderFineMotor * 0.12f),
            };
            target.serverState.CaptorCharacterId = actorId;
            target.CancelCaptureState();
            target.ServerWakeFromDanger();
            target.ReplicateState();
            SetExternalMedicalFeedbackClientRpc("Человек связан.", 0, target.NetworkObjectId);
        }

        [ServerRpc]
        private void BeginCaptureServerRpc(NetworkObjectReference targetReference)
        {
            if (!CanPerformServerAction() || serverSteamId == 0
                || !targetReference.TryGet(out var targetObject) || targetObject == NetworkObject
                || !targetObject.TryGetComponent<PlayerSurvival>(out var target)
                || target.serverState == null || target.ServerIsDead || !target.serverState.Bound
                || target.serverState.CaptorCharacterId != serverState.CharacterId
                || !string.IsNullOrEmpty(target.serverState.CarriedByCharacterId)) return;
            var placed = QuieterRuntimeBootstrap.Instance?.Session?.PlacedObjects;
            if (placed == null || !placed.TryFindOwnedLockedHoldingCell(
                    target.transform.position, serverSteamId.ToString(), out var cellId))
            {
                SetExternalMedicalFeedbackClientRpc(
                    "Пленник должен находиться внутри вашей запертой камеры.", 0,
                    target.NetworkObjectId);
                return;
            }
            target.serverState.Captive = true;
            target.serverState.CaptorAccountId = serverSteamId.ToString();
            target.serverState.CaptureCellObjectId = cellId.ToString();
            target.serverState.CaptureStartedAtUtc = DateTime.UtcNow.ToString("O");
            target.serverState.CaptureStatus = CaptureStatus.ConditionsMet;
            target.serverState.CaptureRevision++;
            target.captureCompletionRaised = false;
            target.ReplicateState();
            SetExternalMedicalFeedbackClientRpc(
                "Захват начат. Камера должна оставаться запертой непрерывно один реальный час.",
                0, target.NetworkObjectId);
        }

        private void TickServerCapture()
        {
            if (serverState == null || !serverState.Captive
                || serverState.CaptureStatus == CaptureStatus.Completed) return;
            if (ServerIsDead)
            {
                CancelCaptureState();
                ReplicateState();
                return;
            }
            var placed = QuieterRuntimeBootstrap.Instance?.Session?.PlacedObjects;
            var cellValid = ulong.TryParse(serverState.CaptureCellObjectId, out var cellId)
                && placed != null && placed.IsInsideOwnedLockedHoldingCell(
                    cellId, transform.position, serverState.CaptorAccountId);
            var immobile = serverState.Bound
                || serverState.Physiology.LifeState >= CharacterLifeState.Unconscious
                || !PhysiologySimulation.CalculateCapabilities(serverState).CanMove;
            var capture = new CaptureState
            {
                VictimCharacterId = serverState.CharacterId,
                VictimAccountId = serverSteamId.ToString(),
                CaptorCharacterId = serverState.CaptorCharacterId,
                CaptorAccountId = serverState.CaptorAccountId,
                CellObjectId = serverState.CaptureCellObjectId,
                Status = serverState.CaptureStatus,
                ContinuousConditionsStartedUtcTicks = ParseCaptureStartedTicks(
                    serverState.CaptureStartedAtUtc),
                Revision = serverState.CaptureRevision,
            };
            var completed = LivingWorldSimulation.AdvanceCapture(
                capture, DateTime.UtcNow.Ticks, immobile, cellValid, capture.Revision);
            serverState.CaptureRevision = capture.Revision;
            serverState.CaptureStatus = capture.Status;
            if (capture.ContinuousConditionsStartedUtcTicks > 0)
            {
                serverState.CaptureStartedAtUtc = new DateTime(
                    capture.ContinuousConditionsStartedUtcTicks, DateTimeKind.Utc).ToString("O");
            }
            if (!cellValid || !immobile)
            {
                CancelCaptureState();
                ReplicateState();
                return;
            }
            if (completed && !captureCompletionRaised)
            {
                captureCompletionRaised = true;
                serverState.ControlKind = CharacterControlKind.ForcedNpc;
                serverState.Offline = false;
                serverState.Npc ??= new NpcRuntimeState();
                serverState.Npc.EmployerAccountId = serverState.CaptorAccountId;
                serverState.Npc.EmployerCharacterId = serverState.CaptorCharacterId;
                serverState.Npc.HomePosition = transform.position;
                serverState.Npc.Motivation = 0.18f;
                serverState.Npc.WorkbookSelectedJob = WorkerJobKind.Mining;
                serverState.Npc.EnsureInitialized();
                serverState.WorkerContract = new WorkerContractState
                {
                    ContractId = Guid.NewGuid().ToString("D"),
                    EmployerAccountId = serverState.CaptorAccountId,
                    WorkerCharacterId = serverState.CharacterId,
                    Active = true,
                    Voluntary = false,
                    DailyRationCalories = 1800f,
                    PromisedSafety = 0.2f,
                    WorkdayStartHour = 6f,
                    WorkdayEndHour = 20f,
                    WorkZoneCenter = transform.position,
                    WorkZoneRadius = 70f,
                    StoragePosition = transform.position,
                    AllowedJobs = new List<WorkerJobKind>(),
                    JobPriorities = new byte[] { 1, 1, 1, 1, 1, 1, 1, 1 },
                };
                foreach (WorkerJobKind job in Enum.GetValues(typeof(WorkerJobKind)))
                    serverState.WorkerContract.AllowedJobs.Add(job);
                serverState.Relationships ??= new List<RelationshipState>();
                serverState.Relationships.Add(new RelationshipState
                {
                    SourceCharacterId = serverState.CharacterId,
                    TargetCharacterId = serverState.CaptorCharacterId,
                    Fear = 0.78f,
                    Resentment = 0.86f,
                    Loyalty = 0f,
                    VoluntaryLoyalty = false,
                });
                ReplicateState();
                ServerCaptureCompleted?.Invoke(this);
            }
        }

        private void CancelCaptureState()
        {
            if (serverState == null) return;
            serverState.Captive = false;
            serverState.CaptorAccountId = string.Empty;
            serverState.CaptureCellObjectId = string.Empty;
            serverState.CaptureStartedAtUtc = string.Empty;
            serverState.CaptureStatus = CaptureStatus.Cancelled;
            serverState.CaptureRevision++;
            captureCompletionRaised = false;
        }

        private static long ParseCaptureStartedTicks(string value) => DateTime.TryParse(
            value, null, System.Globalization.DateTimeStyles.RoundtripKind, out var parsed)
                ? parsed.ToUniversalTime().Ticks : 0;

        [ServerRpc]
        private void TurnBodyServerRpc(NetworkObjectReference targetReference)
        {
            if (!CanPerformServerAction()
                || !targetReference.TryGet(out var targetObject)
                || targetObject == NetworkObject
                || !targetObject.TryGetComponent<PlayerSurvival>(out var target)
                || target.serverState == null || target.serverCarrier != null
                || Vector3.Distance(transform.position, target.transform.position) > 3.75f
                || !HasInteractionLine(target)) return;
            var canBeTurned = target.ServerIsDead || target.serverState.Sleeping
                || target.serverState.Bound
                || target.serverState.Physiology.LifeState
                    >= CharacterLifeState.Unconscious;
            if (!canBeTurned)
            {
                SetExternalMedicalFeedbackClientRpc(
                    "Изменить положение можно у спящего, связанного или потерявшего сознание человека.",
                    0, target.NetworkObjectId);
                return;
            }

            target.serverState.BodyPosture = (BodyPosture)(
                ((int)target.serverState.BodyPosture + 1)
                % Enum.GetValues(typeof(BodyPosture)).Length);
            target.ServerWakeFromDanger();
            CharacterProgression.RegisterPractice(
                serverState.Progression,
                SkillId.Nursing,
                4f,
                0.18f,
                1f,
                0f,
                TraitCatalog.Resolve(serverState.Traits));
            target.ReplicateState();
            ReplicateProgression();
            SetExternalMedicalFeedbackClientRpc(
                $"Положение тела изменено: {BodyPostureName(target.serverState.BodyPosture)}.",
                0, target.NetworkObjectId);
        }

        [ServerRpc]
        private void ToggleCarryServerRpc(NetworkObjectReference targetReference)
        {
            if (!CanPerformServerAction()) return;
            if (serverCarriedTarget != null)
            {
                ReleaseCarriedTarget();
                SetExternalMedicalFeedbackClientRpc("Вы опустили человека на землю.", 0, 0);
                return;
            }
            if (!targetReference.TryGet(out var targetObject) || targetObject == NetworkObject
                || !targetObject.TryGetComponent<PlayerSurvival>(out var target)
                || target.serverState == null || target.serverCarrier != null
                || Vector3.Distance(transform.position, target.transform.position) > 3.75f
                || !HasInteractionLine(target)) return;
            var movable = target.serverState.Bound || target.serverState.Sleeping || target.ServerIsDead
                || target.serverState.Physiology.LifeState >= CharacterLifeState.Unconscious;
            if (!movable)
            {
                SetExternalMedicalFeedbackClientRpc(
                    "Перенести можно связанного, спящего или потерявшего сознание человека.", 0,
                    target.NetworkObjectId);
                return;
            }
            serverCarriedTarget = target;
            target.serverCarrier = this;
            target.serverState.CarriedByCharacterId = serverState.CharacterId;
            target.ReplicateState();
            ReplicateState();
            SetExternalMedicalFeedbackClientRpc("Вы подняли человека. Движение сильно замедлено.", 0,
                target.NetworkObjectId);
        }

        private void TickServerCarry()
        {
            if (serverCarrier == null) return;
            if (!serverCarrier.IsSpawned || serverCarrier.serverState == null
                || serverCarrier.ServerIsDead || serverCarrier.serverCarriedTarget != this)
            {
                serverCarrier = null;
                serverState.CarriedByCharacterId = string.Empty;
                ReplicateState();
                return;
            }
            var destination = serverCarrier.transform.position
                - serverCarrier.transform.forward * 0.65f + Vector3.up * 0.35f;
            networkPlayer?.ServerWarpTo(destination);
        }

        private bool HasInteractionLine(PlayerSurvival target)
        {
            if (target == null) return false;
            var origin = transform.position + Vector3.up * 1.25f;
            var destination = target.transform.position + Vector3.up * 1.05f;
            var direction = destination - origin;
            var hits = Physics.RaycastAll(origin, direction.normalized, direction.magnitude,
                ~0, QueryTriggerInteraction.Ignore);
            Array.Sort(hits, (left, right) => left.distance.CompareTo(right.distance));
            foreach (var hit in hits)
            {
                var candidate = hit.collider.GetComponentInParent<PlayerSurvival>();
                if (candidate == this) continue;
                return candidate == target;
            }
            return true;
        }

        private void ReleaseCarriedTarget()
        {
            if (serverCarriedTarget == null) return;
            var target = serverCarriedTarget;
            serverCarriedTarget = null;
            target.serverCarrier = null;
            if (target.serverState != null)
            {
                target.serverState.CarriedByCharacterId = string.Empty;
                target.ReplicateState();
            }
            ReplicateState();
        }

        private void RespondToMedicalConsent(bool accepted)
        {
            if (!IsOwner || pendingConsentHealerObjectId == 0) return;
            RespondMedicalConsentServerRpc(pendingConsentHealerObjectId, accepted);
            pendingConsentHealerObjectId = 0;
            pendingConsentHealerName = string.Empty;
            Changed?.Invoke();
        }

        [ServerRpc]
        private void BeginExternalTreatmentServerRpc(
            NetworkObjectReference targetReference, uint woundId, MedicalActionType action)
        {
            if (!CanPerformServerAction() || HasPendingTreatment
                || !targetReference.TryGet(out var targetObject) || targetObject == NetworkObject
                || !targetObject.TryGetComponent<PlayerSurvival>(out var target)
                || target.serverState == null || target.ServerIsDead
                || (target.activeExternalHealer != null
                    && target.activeExternalHealer != this)
                || Vector3.Distance(transform.position, target.transform.position) > 3.75f
                || !HasInteractionLine(target)
                || !Enum.IsDefined(typeof(MedicalActionType), action))
            {
                SetExternalMedicalFeedbackClientRpc("Лечение сейчас невозможно.", 0, 0);
                return;
            }
            if (target.RequiresTreatmentConsent()
                && (target.consentedHealerObjectId != NetworkObjectId
                    || target.consentExpiresAt < ServerClock))
            {
                target.RequestMedicalConsentClientRpc(
                    NetworkObjectId,
                    new FixedString64Bytes(GetComponent<Quieter.Player.NetworkPlayer>()?.DisplayName ?? "Незнакомец"));
                SetExternalMedicalFeedbackClientRpc(
                    "Ожидается согласие пострадавшего. После согласия повторите действие.", 0,
                    target.NetworkObjectId);
                return;
            }
            var targetsWound = action <= MedicalActionType.RemoveBandage;
            var wound = targetsWound ? target.ResolveExternalWound(woundId) : null;
            if (targetsWound && wound == null)
            {
                SetExternalMedicalFeedbackClientRpc(
                    "Явных травм, требующих обработки, не обнаружено.", 0, target.NetworkObjectId);
                return;
            }
            if (wound != null && action != MedicalActionType.Inspect
                && !LivingWorldSimulation.IsBodyRegionAccessible(
                    target.serverState.BodyPosture, wound.Region))
            {
                SetExternalMedicalFeedbackClientRpc(
                    "Область закрыта положением тела. Shift+F9 — осторожно повернуть пострадавшего.",
                    wound.WoundId, target.NetworkObjectId);
                return;
            }
            var context = BuildTreatmentContext(action);
            var missing = MissingTreatmentMaterial(action, context);
            if (!string.IsNullOrEmpty(missing))
            {
                SetExternalMedicalFeedbackClientRpc(
                    missing, wound?.WoundId ?? 0, target.NetworkObjectId);
                return;
            }
            BeginPersistentTreatment(
                target,
                wound?.WoundId ?? 0,
                action,
                "Помощь другому человеку…");
            ReplicateState();
        }

        [ClientRpc]
        private void RequestMedicalConsentClientRpc(
            ulong healerObjectId, FixedString64Bytes healerName)
        {
            if (!IsOwner) return;
            pendingConsentHealerObjectId = healerObjectId;
            pendingConsentHealerName = healerName.ToString();
            SetExternalMedicalFeedback(
                $"{pendingConsentHealerName} просит разрешение на осмотр или лечение.");
            Changed?.Invoke();
        }

        [ServerRpc]
        private void RespondMedicalConsentServerRpc(
            ulong healerObjectId, bool accepted, ServerRpcParams rpcParams = default)
        {
            if (!IsServer || rpcParams.Receive.SenderClientId != OwnerClientId
                || !NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(
                    healerObjectId, out var healerObject)
                || !healerObject.TryGetComponent<PlayerSurvival>(out var healer)) return;
            if (accepted)
            {
                consentedHealerObjectId = healerObjectId;
                consentExpiresAt = ServerClock + 60d;
            }
            healer.SetExternalMedicalFeedbackClientRpc(
                accepted ? "Согласие получено на одну минуту." : "Пострадавший отказался от помощи.",
                0, NetworkObjectId);
        }

        [ClientRpc]
        private void SetExternalMedicalFeedbackClientRpc(
            FixedString512Bytes message, uint woundId, ulong targetObjectId)
        {
            if (!IsOwner) return;
            if (woundId != 0) inspectedExternalWoundId = woundId;
            SetExternalMedicalFeedback(message.ToString());
        }

        private void SetExternalMedicalFeedback(string message)
        {
            externalMedicalMessage = message ?? string.Empty;
            externalMedicalMessageUntil = Time.unscaledTime + 8f;
            Changed?.Invoke();
        }

        private bool RequiresTreatmentConsent() => serverState != null
            && serverState.Physiology.LifeState <= CharacterLifeState.Confused
            && !serverState.Sleeping && !serverState.Bound && !serverState.Captive;

        private WoundState ResolveExternalWound(uint woundId)
        {
            if (serverState?.Anatomy?.Wounds == null) return null;
            if (woundId != 0)
                return serverState.Anatomy.Wounds.Find(entry => entry.WoundId == woundId && !entry.Healed);
            WoundState result = null;
            var urgency = float.MinValue;
            foreach (var wound in serverState.Anatomy.Wounds)
            {
                if (wound.Healed) continue;
                var candidate = wound.Bleeding * 2f + wound.Infection + wound.Severity;
                if (candidate <= urgency) continue;
                urgency = candidate;
                result = wound;
            }
            return result;
        }

        public void RequestRelieve(bool bowel)
        {
            if (IsOwner) RelieveServerRpc(bowel);
        }

        public void RequestWashHands()
        {
            if (IsOwner) WashHandsServerRpc();
        }

        public void RequestTraitSelection(IReadOnlyList<TraitId> traits)
        {
            if (!IsOwner || traits == null || traits.Count > 4) return;
            SelectTraitsServerRpc(
                (byte)traits.Count,
                traits.Count > 0 ? (byte)traits[0] : byte.MaxValue,
                traits.Count > 1 ? (byte)traits[1] : byte.MaxValue,
                traits.Count > 2 ? (byte)traits[2] : byte.MaxValue,
                traits.Count > 3 ? (byte)traits[3] : byte.MaxValue);
        }

        public void RequestSleepToggle()
        {
            if (IsOwner) ToggleSleepServerRpc();
        }

        public void RequestReadWeather()
        {
            if (IsOwner) ReadWeatherServerRpc();
        }

        public void RequestNewStranger()
        {
            if (IsOwner) NewStrangerServerRpc();
        }

        [ServerRpc]
        private void NewStrangerServerRpc(ServerRpcParams rpcParams = default)
        {
            if (!IsServer || !ServerIsLifeLost
                || rpcParams.Receive.SenderClientId != OwnerClientId) return;
            ServerNewStrangerRequested?.Invoke(this);
        }

        [ServerRpc]
        private void BeginTreatmentServerRpc(uint woundId, MedicalActionType action)
        {
            if (!CanPerformServerAction())
            {
                SetTreatmentMessage("Сейчас лечение невозможно.");
                return;
            }
            var wound = serverState.Anatomy.Wounds.Find(candidate =>
                candidate.WoundId == woundId && !candidate.Healed);
            if (wound == null)
            {
                SetTreatmentMessage("Травма больше не требует обработки.");
                return;
            }
            if (!Enum.IsDefined(typeof(MedicalActionType), action)
                || action > MedicalActionType.RemoveBandage)
            {
                SetTreatmentMessage("Это медицинское действие пока недоступно.");
                return;
            }

            var context = BuildTreatmentContext(action);
            var missing = MissingTreatmentMaterial(action, context);
            if (!string.IsNullOrEmpty(missing))
            {
                SetTreatmentMessage(missing);
                return;
            }

            BeginPersistentTreatment(null, woundId, action, "Действие выполняется…");
            ReplicateState();
        }

        [ServerRpc]
        private void BeginDentalExtractionServerRpc()
        {
            if (!CanPerformServerAction() || HasPendingTreatment)
            {
                SetTreatmentMessage("Сейчас удалить зуб невозможно.");
                return;
            }
            if (serverState.Physiology.DentalHealth > 0.55f
                && serverState.Physiology.DentalInfection < 0.12f)
            {
                SetTreatmentMessage("Нет зуба, который явно требует удаления.");
                return;
            }
            var action = MedicalActionType.DentalExtraction;
            var context = BuildTreatmentContext(action);
            var missing = MissingTreatmentMaterial(action, context);
            if (!string.IsNullOrEmpty(missing))
            {
                SetTreatmentMessage(missing);
                return;
            }
            BeginPersistentTreatment(null, 0, action, "Готовите зуб к удалению…");
            ReplicateState();
        }

        [ServerRpc]
        private void BeginSupportiveTreatmentServerRpc(MedicalActionType action)
        {
            if (!CanPerformServerAction() || HasPendingTreatment
                || !IsSupportiveMedicalAction(action))
            {
                SetTreatmentMessage("Сейчас этот уход невозможен.");
                return;
            }
            var context = BuildTreatmentContext(action);
            var missing = MissingTreatmentMaterial(action, context);
            if (!string.IsNullOrEmpty(missing))
            {
                SetTreatmentMessage(missing);
                return;
            }
            BeginPersistentTreatment(null, 0, action, "Подготовка общего ухода…");
            ReplicateState();
        }

        [ServerRpc]
        private void RelieveServerRpc(bool bowel)
        {
            if (!CanPerformServerAction()) return;
            var fill = bowel
                ? serverState.Physiology.BowelFill
                : serverState.Physiology.BladderFill;
            if (fill < 0.08f)
            {
                SetTreatmentMessage("Сейчас в этом нет необходимости.");
                return;
            }
            var usedPot = inventory != null
                && inventory.TryDepositWasteServer(bowel ? (ushort)500 : (ushort)350);
            if (bowel)
            {
                PhysiologySimulation.RelieveBowel(serverState, usedPot);
            }
            else
            {
                PhysiologySimulation.RelieveBladder(serverState, usedPot);
            }
            SetTreatmentMessage(usedPot
                ? "Отходы собраны в ночной горшок. Его нужно опорожнить и вымыть."
                : "Пришлось справиться без санитарного сосуда; одежда и тело загрязнены.");
            CharacterProgression.RegisterPractice(
                serverState.Progression,
                SkillId.Sanitation,
                8f,
                usedPot ? 0.25f : 0.1f,
                usedPot ? 1f : 0.25f,
                0f,
                TraitCatalog.Resolve(serverState.Traits));
            ReplicateState();
        }

        [ServerRpc]
        private void WashHandsServerRpc()
        {
            if (!CanPerformServerAction() || inventory == null) return;
            if (!inventory.TryGetServerCleanWater(100, out _)
                || !inventory.TryGetServerItemCleanliness(31, out _))
            {
                SetTreatmentMessage("Для мытья рук нужны 100 мл чистой воды и мыло.");
                return;
            }
            if (!inventory.TryConsumeServerCleanWater(100, out _)
                || !inventory.TryConsumeAnyItemServer(31))
            {
                SetTreatmentMessage("Не удалось подготовить воду и мыло.");
                return;
            }
            serverState.Physiology.HandCleanliness = 1f;
            serverState.Physiology.BodyCleanliness = Mathf.Clamp01(
                serverState.Physiology.BodyCleanliness + 0.1f);
            CharacterProgression.RegisterPractice(
                serverState.Progression,
                SkillId.Sanitation,
                12f,
                0.25f,
                1f,
                0f,
                TraitCatalog.Resolve(serverState.Traits));
            SetTreatmentMessage("Руки тщательно вымыты.");
            ReplicateState();
        }

        private void CompletePendingTreatment()
        {
            if (!pendingTreatmentActive || serverState == null) return;
            var woundId = pendingTreatmentWoundId;
            var action = pendingTreatmentAction;
            var target = pendingTreatmentTarget;
            pendingTreatmentActive = false;
            pendingTreatmentTarget = null;
            ClearPersistentTreatmentState();
            if (target != null)
            {
                target.activeExternalHealer = null;
                if (!target.IsSpawned || target.serverState == null || target.ServerIsDead
                    || Vector3.Distance(transform.position, target.transform.position) > 3.75f)
                {
                    SetTreatmentMessage("Лечение прервано: пострадавший слишком далеко или недоступен.");
                    ReplicateState();
                    return;
                }
            }
            var context = BuildTreatmentContext(action);
            var missing = MissingTreatmentMaterial(action, context);
            if (!string.IsNullOrEmpty(missing))
            {
                SetTreatmentMessage(missing);
                ReplicateState();
                return;
            }

            var patientState = target?.serverState ?? serverState;
            var result = action switch
            {
                MedicalActionType.DentalExtraction
                    => PhysiologySimulation.ExtractTooth(patientState, context),
                MedicalActionType.Warm or MedicalActionType.Cool
                    or MedicalActionType.OralRehydration
                    or MedicalActionType.HerbalPainRelief
                    or MedicalActionType.AntiparasiticCourse
                    => PhysiologySimulation.ApplySupportiveTreatment(
                        patientState, action, context),
                _ => PhysiologySimulation.Treat(patientState, woundId, action, context),
            };
            if (result.Success && !ConsumeTreatmentMaterial(action))
            {
                result = new TreatmentResult(
                    false,
                    "Материал исчез до завершения действия.",
                    0f);
            }
            if (result.Success)
            {
                var skill = TreatmentSkill(action);
                CharacterProgression.RegisterPractice(
                    serverState.Progression,
                    skill,
                    TreatmentDurationSeconds(action),
                    0.35f + Mathf.Clamp01(result.ComplicationRisk) * 0.5f,
                    1f - result.ComplicationRisk,
                    0f,
                    TraitCatalog.Resolve(serverState.Traits));
            }
            SetTreatmentMessage(result.Message);
            if (target != null)
            {
                target.ReplicateState();
                SetExternalMedicalFeedbackClientRpc(
                    result.Message, woundId, target.NetworkObjectId);
            }
            ReplicateState();
        }

        private TreatmentContext BuildTreatmentContext(MedicalActionType action)
        {
            var cleanliness = 1f;
            var waterCleanliness = 0f;
            var soapCleanliness = 0f;
            var needleCleanliness = 0f;
            var bandageCleanliness = 0f;
            var splintCleanliness = 0f;
            var herbCleanliness = 0f;
            var requiredWater = action switch
            {
                MedicalActionType.OralRehydration => (ushort)350,
                MedicalActionType.AntiparasiticCourse => (ushort)200,
                _ => (ushort)100,
            };
            var hasWater = inventory != null
                && inventory.TryGetServerCleanWater(requiredWater, out waterCleanliness);
            var hasSoap = inventory != null
                && inventory.TryGetServerItemCleanliness(31, out soapCleanliness);
            var hasNeedle = inventory != null
                && inventory.TryGetServerItemCleanliness(33, out needleCleanliness);
            var hasBandage = inventory != null
                && inventory.TryGetServerItemCleanliness(32, out bandageCleanliness);
            var hasSplint = inventory != null
                && inventory.TryGetServerItemCleanliness(34, out splintCleanliness);
            var hasSalt = inventory != null && inventory.HasServerItem(14);
            var hasHerbs = inventory != null
                && inventory.TryGetServerItemCleanliness(28, out herbCleanliness);
            var hasHeatSource = ResolveEnvironment(transform.position, 0f).ExternalHeat > 0.08f;
            cleanliness = action switch
            {
                MedicalActionType.Wash => hasWater ? waterCleanliness : 0f,
                MedicalActionType.Disinfect => hasSoap ? soapCleanliness : 0f,
                MedicalActionType.Suture => hasNeedle ? needleCleanliness : 0f,
                MedicalActionType.Bandage => hasBandage ? bandageCleanliness : 0f,
                MedicalActionType.Splint => hasSplint ? splintCleanliness : 0f,
                MedicalActionType.DentalExtraction => hasWater && hasBandage
                    ? Mathf.Min(waterCleanliness, bandageCleanliness) : 0f,
                MedicalActionType.Cool or MedicalActionType.OralRehydration
                    => hasWater ? waterCleanliness : 0f,
                MedicalActionType.HerbalPainRelief
                    => hasHerbs ? herbCleanliness : 0f,
                MedicalActionType.AntiparasiticCourse
                    => hasHerbs && hasWater
                        ? Mathf.Min(herbCleanliness, waterCleanliness) : 0f,
                _ => 1f,
            };
            var level = CharacterProgression.GetSkillLevel(
                serverState.Progression, TreatmentSkill(action));
            var fineMotor = serverState.Progression.Attributes[
                (int)CharacterAttributeId.FineMotorControl] / 100f;
            var skill = Mathf.Clamp01(level / 10f * 0.82f + fineMotor * 0.18f);
            return new TreatmentContext(
                skill,
                cleanliness,
                hasWater,
                hasSoap,
                hasNeedle,
                hasBandage,
                hasSplint,
                serverState.Physiology.HandCleanliness,
                hasSalt,
                hasHerbs,
                hasHeatSource);
        }

        private static string MissingTreatmentMaterial(
            MedicalActionType action,
            TreatmentContext context) => action switch
        {
            MedicalActionType.Wash when !context.HasWater
                => "Нужно не менее 100 мл чистой или кипячёной воды.",
            MedicalActionType.Disinfect when !context.HasDisinfectant
                => "Нужен чистый кусок мыла или подходящий состав.",
            MedicalActionType.Suture when !context.HasNeedleAndThread
                => "Нужны чистые игла и нить.",
            MedicalActionType.Bandage when !context.HasBandage
                => "Нужна чистая ткань для повязки.",
            MedicalActionType.Splint when !context.HasSplint
                => "Нужна деревянная шина.",
            MedicalActionType.DentalExtraction when !context.HasWater
                => "Для удаления зуба нужно не менее 100 мл чистой воды.",
            MedicalActionType.DentalExtraction when !context.HasBandage
                => "Для удаления зуба нужна чистая ткань.",
            MedicalActionType.Warm when !context.HasHeatSource
                => "Для согревания нужен работающий очаг.",
            MedicalActionType.Cool when !context.HasWater
                => "Для охлаждения нужно не менее 100 мл чистой воды.",
            MedicalActionType.OralRehydration when !context.HasWater
                => "Для питьевого раствора нужно не менее 350 мл чистой воды.",
            MedicalActionType.OralRehydration when !context.HasSalt
                => "Для питьевого раствора нужна каменная соль.",
            MedicalActionType.HerbalPainRelief when !context.HasHerbs
                => "Нужны лекарственные травы.",
            MedicalActionType.AntiparasiticCourse when !context.HasWater
                => "Для курса нужно не менее 200 мл чистой воды.",
            MedicalActionType.AntiparasiticCourse when !context.HasHerbs
                => "Для курса нужны лекарственные травы.",
            _ => string.Empty,
        };

        private bool ConsumeTreatmentMaterial(MedicalActionType action) => action switch
        {
            MedicalActionType.Wash => inventory != null
                && inventory.TryConsumeServerCleanWater(100, out _),
            MedicalActionType.Disinfect => inventory != null
                && inventory.TryConsumeAnyItemServer(31),
            MedicalActionType.Suture => inventory != null
                && inventory.TryConsumeAnyItemServer(33),
            MedicalActionType.Bandage => inventory != null
                && inventory.TryConsumeAnyItemServer(32),
            MedicalActionType.Splint => inventory != null
                && inventory.TryConsumeAnyItemServer(34),
            MedicalActionType.DentalExtraction => inventory != null
                && inventory.TryConsumeServerCleanWater(100, out _)
                && inventory.TryConsumeAnyItemServer(32),
            MedicalActionType.Cool => inventory != null
                && inventory.TryConsumeServerCleanWater(100, out _),
            MedicalActionType.OralRehydration => inventory != null
                && inventory.TryConsumeServerCleanWater(350, out _)
                && inventory.TryConsumeAnyItemServer(14),
            MedicalActionType.HerbalPainRelief => inventory != null
                && inventory.TryConsumeAnyItemServer(28),
            MedicalActionType.AntiparasiticCourse => inventory != null
                && inventory.TryConsumeServerCleanWater(200, out _)
                && inventory.TryConsumeAnyItemServer(28),
            _ => true,
        };

        private static SkillId TreatmentSkill(MedicalActionType action) => action switch
        {
            MedicalActionType.Inspect => SkillId.Diagnosis,
            MedicalActionType.ApplyPressure => SkillId.FirstAid,
            MedicalActionType.Wash => SkillId.WoundCare,
            MedicalActionType.Disinfect => SkillId.HerbalMedicine,
            MedicalActionType.Suture => SkillId.Suturing,
            MedicalActionType.Bandage => SkillId.WoundCare,
            MedicalActionType.Splint => SkillId.Bonesetting,
            MedicalActionType.DentalExtraction => SkillId.SurgeryAndDentistry,
            MedicalActionType.HerbalPainRelief
                or MedicalActionType.AntiparasiticCourse => SkillId.HerbalMedicine,
            _ => SkillId.Nursing,
        };

        private static float TreatmentDurationSeconds(MedicalActionType action) => action switch
        {
            MedicalActionType.Inspect => 3f,
            MedicalActionType.ApplyPressure => 6f,
            MedicalActionType.Wash => 12f,
            MedicalActionType.Disinfect => 8f,
            MedicalActionType.Suture => 25f,
            MedicalActionType.Bandage => 10f,
            MedicalActionType.Splint => 20f,
            MedicalActionType.RemoveBandage => 5f,
            MedicalActionType.DentalExtraction => 35f,
            MedicalActionType.Warm => 18f,
            MedicalActionType.Cool => 15f,
            MedicalActionType.OralRehydration => 20f,
            MedicalActionType.HerbalPainRelief => 24f,
            MedicalActionType.AntiparasiticCourse => 30f,
            _ => 8f,
        };

        private static bool IsSupportiveMedicalAction(MedicalActionType action)
            => action is MedicalActionType.Warm
                or MedicalActionType.Cool
                or MedicalActionType.OralRehydration
                or MedicalActionType.HerbalPainRelief
                or MedicalActionType.AntiparasiticCourse;

        [ServerRpc]
        private void BeginRestraintEscapeServerRpc(ServerRpcParams rpcParams = default)
        {
            if (!IsServer || serverState == null
                || rpcParams.Receive.SenderClientId != OwnerClientId
                || !serverState.Bound || ServerIsDead
                || serverState.Sleeping
                || serverState.Physiology.LifeState > CharacterLifeState.Confused)
                return;
            serverState.Restraint ??= new RestraintState();
            if (serverState.Restraint.EscapeAttemptRemainingSeconds > 0f)
            {
                SetTreatmentMessage("Вы уже пытаетесь ослабить путы.");
                return;
            }
            serverState.Restraint.EscapeAttemptRemainingSeconds = 12f;
            serverState.Physiology.Stress = Mathf.Clamp01(
                serverState.Physiology.Stress + 0.035f);
            SetTreatmentMessage("Вы осторожно ищете слабое место в путах…");
            ReplicateState();
        }

        private void TickRestraintEscape(float elapsedSeconds)
        {
            if (!serverState.Bound || serverState.Restraint == null
                || serverState.Restraint.EscapeAttemptRemainingSeconds <= 0f)
                return;
            if (serverState.Sleeping
                || serverState.Physiology.LifeState > CharacterLifeState.Confused)
            {
                serverState.Restraint.EscapeAttemptRemainingSeconds = 0f;
                SetTreatmentMessage("Попытка освободиться сорвалась: вы не можете продолжать.");
                return;
            }
            serverState.Restraint.EscapeAttemptRemainingSeconds = Mathf.Max(
                0f,
                serverState.Restraint.EscapeAttemptRemainingSeconds - elapsedSeconds);
            exertion = Mathf.Max(exertion, 0.42f);
            if (serverState.Restraint.EscapeAttemptRemainingSeconds > 0f) return;

            var escaped = LivingWorldSimulation.CompleteRestraintEscapeAttempt(
                serverState, out var gained);
            CharacterProgression.RegisterPractice(
                serverState.Progression,
                SkillId.RestraintEscape,
                12f,
                Mathf.Lerp(0.55f, 0.9f, serverState.Restraint.Integrity),
                Mathf.Clamp01(0.45f + gained * 2.5f),
                0f,
                TraitCatalog.Resolve(serverState.Traits));
            if (escaped)
            {
                serverState.Bound = false;
                serverState.CaptorCharacterId = string.Empty;
                serverState.Restraint = new RestraintState();
                CancelCaptureState();
                SetTreatmentMessage("Путы поддались. Вы свободны.");
                ReplicateProgression();
                return;
            }

            if (serverState.Restraint.CompletedEscapeAttempts % 3 == 0)
            {
                PhysiologySimulation.AddInjury(
                    serverState,
                    serverState.Restraint.CompletedEscapeAttempts % 2 == 0
                        ? BodyRegion.LeftHand : BodyRegion.RightHand,
                    DamageKind.Blunt,
                    0.045f,
                    Mathf.Clamp01(1f - serverState.Physiology.BodyCleanliness));
            }
            SetTreatmentMessage(serverState.Restraint.EscapeProgress < 0.35f
                ? "Верёвка пока держит; кисти саднят. Можно попытаться снова."
                : serverState.Restraint.EscapeProgress < 0.72f
                    ? "В узле появился небольшой люфт. Можно попытаться снова."
                    : "Путы заметно ослабли, но ещё держат.");
            ReplicateProgression();
        }

        [ServerRpc]
        private void BeginVoluntaryPassingServerRpc(ServerRpcParams rpcParams = default)
        {
            if (!IsServer || serverState == null
                || rpcParams.Receive.SenderClientId != OwnerClientId
                || ServerIsDead
                || serverState.ControlKind != CharacterControlKind.Player
                || (!serverState.Bound && !serverState.Captive)
                || serverState.Sleeping || serverState.Offline
                || serverState.Physiology.LifeState > CharacterLifeState.Confused
                || HasPendingTreatment
                || serverState.Restraint?.EscapeAttemptRemainingSeconds > 0f)
                return;
            serverState.VoluntaryPassing ??= new VoluntaryPassingState();
            if (serverState.VoluntaryPassing.AttemptActive)
            {
                SetTreatmentMessage("Долгая попытка древней тишины уже продолжается.");
                return;
            }

            serverState.VoluntaryPassing.AttemptActive = true;
            serverState.VoluntaryPassing.AttemptRemainingSeconds =
                PhysiologySimulation.FictionalSilenceAttemptSeconds;
            serverState.Physiology.Stress = Mathf.Clamp01(
                serverState.Physiology.Stress + 0.04f);
            SetTreatmentMessage(
                "Вы начинаете долгий вымышленный обряд древней тишины. Новая травма прервёт попытку.");
            ReplicateState();
        }

        private void TickVoluntaryPassing(float elapsedSeconds)
        {
            var rite = serverState?.VoluntaryPassing;
            if (rite?.AttemptActive != true) return;
            if ((!serverState.Bound && !serverState.Captive)
                || serverState.Sleeping || serverState.Offline
                || serverState.Physiology.LifeState > CharacterLifeState.Confused)
            {
                CancelVoluntaryPassingAttempt(
                    "Попытка древней тишины прервана: сосредоточение потеряно.");
                return;
            }

            rite.AttemptRemainingSeconds = Mathf.Max(
                0f, rite.AttemptRemainingSeconds - elapsedSeconds);
            exertion = Mathf.Max(exertion, 0.08f);
            if (rite.AttemptRemainingSeconds > 0f) return;

            rite.AttemptActive = false;
            var completed = PhysiologySimulation.CompleteFictionalSilenceAttempt(
                serverState, out _);
            if (completed)
            {
                SetTreatmentMessage(
                    "Вымышленный обряд завершён. Жизненные функции необратимо угасают.");
                ReplicateState();
                return;
            }

            serverState.Physiology.Stress = Mathf.Clamp01(
                serverState.Physiology.Stress + 0.12f);
            serverState.Physiology.AcuteStamina = Mathf.Clamp01(
                serverState.Physiology.AcuteStamina - 0.18f);
            SetTreatmentMessage(rite.PassageProgress < 0.45f
                ? "Паника разрушила неподвижное сосредоточение. Для освоения обряда нужны новые долгие попытки."
                : "Обряд снова сорвался, но вымышленная техника стала понятнее.");
            ReplicateState();
        }

        private void CancelVoluntaryPassingAttempt(string message)
        {
            var rite = serverState?.VoluntaryPassing;
            if (rite?.AttemptActive != true) return;
            rite.AttemptActive = false;
            rite.AttemptRemainingSeconds = 0f;
            SetTreatmentMessage(message);
            ReplicateState();
        }

        private void BeginPersistentTreatment(
            PlayerSurvival target,
            uint woundId,
            MedicalActionType action,
            string message)
        {
            var duration = TreatmentDurationSeconds(action);
            pendingTreatmentActive = true;
            pendingTreatmentTarget = target;
            if (target != null) target.activeExternalHealer = this;
            pendingTreatmentWoundId = woundId;
            pendingTreatmentAction = action;
            pendingTreatmentCompletesAt = ServerClock + duration;
            serverState.MedicalActivity.Active = true;
            serverState.MedicalActivity.WoundId = woundId;
            serverState.MedicalActivity.Action = action;
            serverState.MedicalActivity.RemainingSeconds = duration;
            serverState.MedicalActivity.TargetCharacterId =
                target?.serverState?.CharacterId ?? string.Empty;
            treatmentActivity.Value = new TreatmentActivityState
            {
                Active = true,
                WoundId = woundId,
                Action = action,
                CompletesAtServerTime = pendingTreatmentCompletesAt,
                Message = new FixedString512Bytes(message ?? string.Empty),
                Revision = ++treatmentRevision,
            };
        }

        private void TryRestorePendingTreatment()
        {
            if (serverState?.MedicalActivity?.Active != true || pendingTreatmentActive)
                return;
            var saved = serverState.MedicalActivity;
            PlayerSurvival target = null;
            if (!string.IsNullOrEmpty(saved.TargetCharacterId)
                && saved.TargetCharacterId != serverState.CharacterId)
            {
                foreach (var candidate in FindObjectsByType<PlayerSurvival>())
                {
                    if (candidate == null || candidate.serverState?.CharacterId
                        != saved.TargetCharacterId) continue;
                    target = candidate;
                    break;
                }
                // Persistent characters may be spawned in a different order. Keep
                // the saved timer paused until its patient exists again.
                if (target == null) return;
                // A second restored healer must not overwrite the patient lock.
                // Its persisted timer remains paused until the active pair finish.
                if (target.activeExternalHealer != null
                    && target.activeExternalHealer != this)
                    return;
            }

            pendingTreatmentActive = true;
            pendingTreatmentTarget = target;
            if (target != null) target.activeExternalHealer = this;
            pendingTreatmentWoundId = saved.WoundId;
            pendingTreatmentAction = saved.Action;
            pendingTreatmentCompletesAt = ServerClock
                + Mathf.Max(0.1f, saved.RemainingSeconds);
            treatmentActivity.Value = new TreatmentActivityState
            {
                Active = true,
                WoundId = saved.WoundId,
                Action = saved.Action,
                CompletesAtServerTime = pendingTreatmentCompletesAt,
                Message = new FixedString512Bytes(target == null
                    ? "Незавершённое лечение продолжается…"
                    : "Помощь другому человеку продолжается…"),
                Revision = ++treatmentRevision,
            };
        }

        private void SynchronizePendingTreatmentState()
        {
            if (!pendingTreatmentActive || serverState?.MedicalActivity?.Active != true)
                return;
            serverState.MedicalActivity.RemainingSeconds = Mathf.Max(
                0f, (float)(pendingTreatmentCompletesAt - ServerClock));
        }

        private void ClearPersistentTreatmentState()
        {
            if (serverState?.MedicalActivity == null) return;
            serverState.MedicalActivity.Active = false;
            serverState.MedicalActivity.RemainingSeconds = 0f;
            serverState.MedicalActivity.TargetCharacterId = string.Empty;
        }

        private void CancelPendingTreatment(string message)
        {
            if (!HasPendingTreatment) return;
            pendingTreatmentActive = false;
            if (pendingTreatmentTarget != null)
                pendingTreatmentTarget.activeExternalHealer = null;
            pendingTreatmentTarget = null;
            ClearPersistentTreatmentState();
            SetTreatmentMessage(message);
            ReplicateState();
        }

        private void SetTreatmentMessage(string message)
        {
            treatmentActivity.Value = new TreatmentActivityState
            {
                Active = false,
                WoundId = pendingTreatmentWoundId,
                Action = pendingTreatmentAction,
                CompletesAtServerTime = ServerClock,
                Message = new FixedString512Bytes(message ?? string.Empty),
                Revision = ++treatmentRevision,
            };
        }

        private double ServerClock => NetworkManager != null
            ? NetworkManager.ServerTime.Time
            : Time.unscaledTimeAsDouble;

        [ServerRpc]
        private void ReadWeatherServerRpc()
        {
            if (!CanPerformServerAction()) return;
            var weather = FindAnyObjectByType<WorldWeatherService>();
            if (weather == null)
            {
                SetTreatmentMessage("Небо не удаётся прочитать.");
                return;
            }
            var skill = CharacterProgression.GetSkillLevel(
                serverState.Progression, SkillId.WeatherReading);
            var current = weather.Sample(transform.position, weather.Current.GameSeconds);
            var horizonHours = skill switch
            {
                >= 8 => 12f,
                >= 5 => 6f,
                >= 2 => 2f,
                _ => 0f,
            };
            var message = $"Сейчас {WeatherTemperatureName(current.TemperatureC)}, "
                + $"{WeatherWindName(current.WindMetersPerSecond)}; "
                + WeatherRainName(current.Precipitation) + ".";
            if (horizonHours > 0f)
            {
                var forecast = weather.Sample(
                    transform.position,
                    current.GameSeconds + horizonHours * 3600d);
                message += $" Примерно через {horizonHours:0} ч.: "
                    + $"{WeatherTemperatureName(forecast.TemperatureC)}, "
                    + $"{WeatherWindName(forecast.WindMetersPerSecond)}, "
                    + WeatherRainName(forecast.Precipitation) + ".";
            }
            else
            {
                message += " Для прогноза пока не хватает наблюдений и опыта.";
            }
            CharacterProgression.RegisterPractice(
                serverState.Progression,
                SkillId.WeatherReading,
                20f,
                Mathf.Clamp01(0.2f + current.Precipitation * 0.45f
                    + current.WindMetersPerSecond / 30f),
                1f,
                0.45f,
                TraitCatalog.Resolve(serverState.Traits));
            SetTreatmentMessage(message);
            ReplicateProgression();
        }

        private static string WeatherTemperatureName(float temperatureC) => temperatureC switch
        {
            < -10f => "лютый мороз",
            < 0f => "морозно",
            < 8f => "холодно",
            < 17f => "прохладно",
            < 26f => "тепло",
            < 34f => "жарко",
            _ => "изнуряющая жара",
        };

        private static string WeatherWindName(float metersPerSecond) => metersPerSecond switch
        {
            < 1.5f => "почти штиль",
            < 4f => "лёгкий ветер",
            < 8f => "сильный ветер",
            _ => "опасные порывы",
        };

        private static string WeatherRainName(float precipitation) => precipitation switch
        {
            < 0.12f => "осадков не видно",
            < 0.42f => "возможна морось",
            < 0.72f => "идёт дождь",
            _ => "идёт сильный дождь",
        };

        [ServerRpc]
        private void ToggleSleepServerRpc()
        {
            if (!IsServer || serverState == null || !serverState.CreationCompleted)
                return;
            if (serverState.Sleeping)
            {
                var quality = CalculateSleepQuality(serverState);
                PhysiologySimulation.EndSleep(serverState, quality);
                serverState.Physiology.CurrentSleepSeconds = 0f;
            }
            else if (serverState.Physiology.LifeState <= CharacterLifeState.Confused
                && serverState.Physiology.Pain < 0.75f)
            {
                serverState.Physiology.CurrentSleepSeconds = 0f;
                PhysiologySimulation.BeginSleep(serverState);
            }
            ReplicateState();
        }

        [ServerRpc]
        private void SelectTraitsServerRpc(
            byte count,
            byte first,
            byte second,
            byte third,
            byte fourth)
        {
            if (!IsServer || serverState == null || serverState.CreationCompleted || count > 4)
            {
                return;
            }

            var values = new[] { first, second, third, fourth };
            var selected = new System.Collections.Generic.List<TraitId>(count);
            for (var index = 0; index < count; index++)
            {
                var id = (TraitId)values[index];
                try
                {
                    _ = TraitCatalog.Get(id);
                    selected.Add(id);
                }
                catch (ArgumentOutOfRangeException)
                {
                    traitSelectionError = 1;
                    ReplicateState();
                    return;
                }
            }

            if (!TraitCatalog.TryValidate(selected, out _, out _))
            {
                traitSelectionError = 2;
                ReplicateState();
                return;
            }

            serverState.Traits = selected;
            ApplyStartingPredispositions(serverState);
            serverState.CreationCompleted = true;
            traitSelectionError = 0;
            ReplicateState();
            ReplicateProgression();
        }

        private void ReplicateState()
        {
            if (!IsServer || serverState == null)
            {
                return;
            }

            var physiology = serverState.Physiology;
            var symptoms = PhysiologySimulation.ObserveSymptoms(serverState);
            var diagnosisLevel = CharacterProgression.GetSkillLevel(
                serverState.Progression, SkillId.Diagnosis);
            var woundCount = 0;
            uint woundedRegions = 0;
            uint fracturedRegions = 0;
            foreach (var wound in serverState.Anatomy.Wounds)
            {
                if (wound.Healed) continue;
                if (wound.IsOpen) woundCount++;
                woundedRegions |= 1u << (int)wound.Region;
                if (wound.IsFracture) fracturedRegions |= 1u << (int)wound.Region;
            }

            var publicState = new PublicSymptomState
            {
                Symptoms = symptoms & PubliclyVisibleSymptoms,
                LifeState = physiology.LifeState,
                VisibleWoundCount = (byte)Mathf.Min(byte.MaxValue, woundCount),
                Sleeping = serverState.Sleeping,
                Bound = serverState.Bound,
                Captive = serverState.Captive,
                ControlKind = serverState.ControlKind,
                Activity = serverState.Npc?.Activity ?? NpcActivityKind.Idle,
                ObservedJob = serverState.Npc?.ActiveJob ?? WorkerJobKind.Mining,
                PersonalRequest = serverState.Npc?.PersonalRequest ?? NpcPersonalRequestKind.Food,
                HasPersonalRequest = serverState.Npc?.PersonalRequestPending == true,
                CorpseStage = serverState.Corpse?.Stage ?? CorpseDecayStage.Fresh,
                CorpseContamination = (byte)Mathf.RoundToInt(Mathf.Clamp01(
                    serverState.Corpse?.BiologicalContamination ?? 0f) * 255f),
                BodyPosture = serverState.BodyPosture,
            };
            if (serverState.Npc != null)
            {
                var job = serverState.Npc.ActiveJob;
                var evidence = serverState.Npc.CompletedTasks[(int)job];
                if (evidence >= 3)
                {
                    var actual = CharacterProgression.GetSkillLevel(
                        serverState.Progression, ObservableWorkSkill(job));
                    var range = LivingWorldSimulation.RevealSkillRange(actual, evidence);
                    publicState.HasWorkEvidence = true;
                    publicState.ObservedSkillMinimum = (byte)range.Minimum;
                    publicState.ObservedSkillMaximum = (byte)range.Maximum;
                }
            }
            publicSymptoms.Value = publicState;
            ReplicateWorkerContract();
            ReplicateObservedWounds();
            ownerCondition.Value = new OwnerConditionState
            {
                Symptoms = symptoms,
                LifeState = physiology.LifeState,
                DeathCause = physiology.DeathCause,
                ThirstStage = InverseStage(physiology.Hydration),
                HungerStage = InverseStage(Mathf.Min(physiology.StomachFullness, physiology.EnergyReserve)),
                FatigueStage = Stage(Mathf.Max(
                    physiology.SleepDebt, physiology.CircadianFatigue * 0.75f)),
                TemperatureStage = TemperatureStage(physiology.CoreTemperatureC),
                PainStage = Stage(physiology.Pain),
                BladderStage = Stage(physiology.BladderFill),
                BowelStage = Stage(physiology.BowelFill),
                GastrointestinalStage = diagnosisLevel >= 2
                    ? Stage(serverState.Conditions.GastrointestinalInfection)
                    : byte.MaxValue,
                RespiratoryStage = diagnosisLevel >= 2
                    ? Stage(serverState.Conditions.RespiratoryInfection)
                    : byte.MaxValue,
                ParasiteStage = diagnosisLevel >= 4
                    ? Stage(serverState.Conditions.ParasiteLoad)
                    : byte.MaxValue,
                WoundedRegions = woundedRegions,
                FracturedRegions = fracturedRegions,
                NeedsCharacterCreation = !serverState.CreationCompleted,
                Sleeping = serverState.Sleeping,
                Bound = serverState.Bound,
                Captive = serverState.Captive,
                BeingCarried = serverCarrier != null,
                VoluntaryPassingAttemptActive =
                    serverState.VoluntaryPassing?.AttemptActive == true,
                VoluntaryPassingStage = Stage(
                    serverState.VoluntaryPassing?.PassageProgress ?? 0f),
                BodyPosture = serverState.BodyPosture,
                ControlKind = serverState.ControlKind,
                TraitSelectionError = traitSelectionError,
                Revision = ++replicatedRevision,
            };
            if (serverState.CreationCompleted && !HasBlockingActivity
                && !serverState.Bound
                && serverState.ControlKind is CharacterControlKind.Player
                    or CharacterControlKind.FreeNpc
                    or CharacterControlKind.ContractedNpc
                    or CharacterControlKind.ForcedNpc)
            {
                var capability = PhysiologySimulation.CalculateCapabilities(serverState);
                if (serverCarriedTarget != null)
                    capability = new CharacterCapabilities(
                        capability.MovementSpeed * 0.45f,
                        capability.Acceleration * 0.5f,
                        0f,
                        capability.StaminaRecovery * 0.35f,
                        capability.FineMotor,
                        capability.CanMove,
                        false);
                movementCapability.Value = MovementCapabilityState.From(capability);
            }
            else
            {
                movementCapability.Value = MovementCapabilityState.From(
                    new CharacterCapabilities(0f, 0f, 0f, 0f, 0f, false, false));
            }
            Changed?.Invoke();
        }

        private void ReplicateWorkerContract()
        {
            var current = publicWorkerContract.Value;
            var contract = serverState.WorkerContract;
            var npc = serverState.Npc;
            var next = new PublicWorkerContractState { Revision = current.Revision };
            if (contract?.Active == true && npc != null)
            {
                contract.EnsureInitialized();
                next.Active = true;
                next.Voluntary = contract.Voluntary;
                next.ActiveJob = npc.ActiveJob;
                next.SelectedJob = npc.WorkbookSelectedJob;
                next.SelectedPriority = LivingWorldSimulation.GetJobPriority(
                    contract, npc.WorkbookSelectedJob);
                next.DailyRationCalories = (ushort)Mathf.Clamp(
                    Mathf.RoundToInt(contract.DailyRationCalories), 0, ushort.MaxValue);
                next.WorkdayStartHour = contract.WorkdayStartHour;
                next.WorkdayEndHour = contract.WorkdayEndHour;
                next.WorkZoneRadius = contract.WorkZoneRadius;
                next.WorkZoneCenter = contract.WorkZoneCenter;
                next.StoragePosition = contract.StoragePosition;
                next.PaymentItemId = contract.PaymentItemId;
                next.PaymentQuantity = contract.PaymentQuantity;
                next.ConsecutiveBreaches = (byte)Mathf.Clamp(
                    contract.ConsecutiveBreaches, 0, byte.MaxValue);
            }
            if (next.Equals(current)) return;
            next.Revision++;
            publicWorkerContract.Value = next;
        }

        private void ReplicateObservedWounds()
        {
            observedWounds.Clear();
            var diagnosisLevel = CharacterProgression.GetSkillLevel(
                serverState.Progression, SkillId.Diagnosis);
            foreach (var wound in serverState.Anatomy.Wounds)
            {
                if (wound.Healed) continue;
                var flags = ObservedTreatmentFlags.None;
                if (wound.PressureApplied) flags |= ObservedTreatmentFlags.Pressure;
                if (wound.Washed) flags |= ObservedTreatmentFlags.Washed;
                if (wound.Disinfected) flags |= ObservedTreatmentFlags.Disinfected;
                if (wound.Sutured) flags |= ObservedTreatmentFlags.Sutured;
                if (wound.Bandaged) flags |= ObservedTreatmentFlags.Bandaged;
                if (wound.Splinted) flags |= ObservedTreatmentFlags.Splinted;
                observedWounds.Add(new ObservedWoundState
                {
                    WoundId = wound.WoundId,
                    Region = wound.Region,
                    Type = wound.Type,
                    TypeKnown = diagnosisLevel >= 2
                        || wound.Type == InjuryType.Burn
                        || wound.Type == InjuryType.Laceration,
                    InternalBleedingSuspected = diagnosisLevel >= 5
                        && wound.InternalBleedingSeverity > 0.02f,
                    SeverityStage = Stage(wound.Severity),
                    BleedingStage = Stage(wound.Bleeding),
                    ContaminationStage = diagnosisLevel >= 2
                        ? Stage(wound.Contamination)
                        : byte.MaxValue,
                    InfectionStage = diagnosisLevel >= 3
                        ? Stage(wound.Infection)
                        : byte.MaxValue,
                    TreatmentFlags = flags,
                });
            }
        }

        private void ReplicateProgression()
        {
            if (!IsServer || serverState == null) return;
            progressionEntries.Clear();
            for (var index = 0; index < (int)CharacterAttributeId.Count; index++)
            {
                progressionEntries.Add(new ProgressionEntryState
                {
                    Attribute = true,
                    Id = (byte)index,
                    Level = (byte)CharacterProgression.GetAttributeGrade(
                        serverState.Progression, (CharacterAttributeId)index),
                    ProgressStage = 0,
                });
            }
            for (var index = 0; index < (int)SkillId.Count; index++)
            {
                var skill = (SkillId)index;
                progressionEntries.Add(new ProgressionEntryState
                {
                    Attribute = false,
                    Id = (byte)index,
                    Level = (byte)CharacterProgression.GetSkillLevel(
                        serverState.Progression, skill),
                    ProgressStage = (byte)Mathf.Clamp(
                        Mathf.FloorToInt(
                            CharacterProgression.GetSkillLevelProgress(
                                serverState.Progression, skill) * 5f),
                        0,
                        4),
                });
            }
        }

        private void EnsureCorpseState()
        {
            if (serverState == null || serverState.Physiology.LifeState != CharacterLifeState.Dead)
                return;
            serverState.Corpse ??= new CorpseState();
            if (string.IsNullOrWhiteSpace(serverState.Corpse.CharacterId))
                serverState.Corpse.CharacterId = serverState.CharacterId;
            if (string.IsNullOrWhiteSpace(serverState.Corpse.CorpseId))
                serverState.Corpse.CorpseId = $"corpse-{serverState.CharacterId}";
            if (serverState.Corpse.DiedAtUtcTicks <= 0)
                serverState.Corpse.DiedAtUtcTicks = DateTime.UtcNow.Ticks;
            LivingWorldSimulation.UpdateCorpse(
                serverState.Corpse, DateTime.UtcNow.Ticks,
                ResolveEnvironment(transform.position, 0f).AmbientTemperatureC);
        }

        private void TickCorpseState()
        {
            if (Time.unscaledTime < nextCorpseUpdateAt) return;
            nextCorpseUpdateAt = Time.unscaledTime + 5f;
            EnsureCorpseState();
            if (serverState?.Corpse == null) return;
            var previousStage = serverState.Corpse.Stage;
            var previousContamination = serverState.Corpse.BiologicalContamination;
            LivingWorldSimulation.UpdateCorpse(
                serverState.Corpse,
                DateTime.UtcNow.Ticks,
                ResolveEnvironment(transform.position, 0f).AmbientTemperatureC);
            if (serverState.Corpse.Stage == CorpseDecayStage.DryRemains)
                serverState.ControlKind = CharacterControlKind.Remains;
            if (serverState.Corpse.Stage == previousStage
                && Mathf.Approximately(
                    serverState.Corpse.BiologicalContamination,
                    previousContamination)) return;
            serverState.Revision++;
            ReplicateState();
        }

        private void ApplyNearbyCorpseExposure(float elapsedSeconds)
        {
            if (elapsedSeconds <= 0f || serverState?.Physiology == null) return;
            foreach (var candidate in FindObjectsByType<PlayerSurvival>())
            {
                if (candidate == null || candidate == this || !candidate.ServerIsDead
                    || candidate.serverState?.Corpse == null) continue;
                var distance = Vector3.Distance(transform.position, candidate.transform.position);
                if (distance > 10f) continue;
                var exposure = candidate.serverState.Corpse.BiologicalContamination
                    * (1f - distance / 10f) * elapsedSeconds;
                serverState.Physiology.SystemicInfection = Mathf.Clamp01(
                    serverState.Physiology.SystemicInfection + exposure / 24000f);
                serverState.Physiology.BodyCleanliness = Mathf.Clamp01(
                    serverState.Physiology.BodyCleanliness - exposure / 14000f);
            }
        }

        private void ApplyNearbyRespiratoryExposure(float elapsedSeconds)
        {
            if (elapsedSeconds <= 0f || serverState?.Conditions == null
                || serverState.Physiology.LifeState == CharacterLifeState.Dead) return;
            foreach (var candidate in FindObjectsByType<PlayerSurvival>())
            {
                if (candidate == null || candidate == this || candidate.ServerIsDead
                    || candidate.serverState?.Conditions == null) continue;
                var source = candidate.serverState.Conditions.RespiratoryInfection;
                if (source < 0.1f) continue;
                var distance = Vector3.Distance(transform.position, candidate.transform.position);
                if (distance > 4f) continue;
                var proximity = 1f - distance / 4f;
                PhysiologySimulation.ExposeRespiratoryInfection(
                    serverState, source * proximity * elapsedSeconds / 300f);
            }
        }

        private SurvivalEnvironment ResolveEnvironment(Vector3 position, float elapsedSeconds)
        {
            var weather = FindAnyObjectByType<WorldWeatherService>();
            var environment = weather != null
                ? weather.GetEnvironment(position)
                : SurvivalEnvironment.Temperate;
            var placedObjects = QuieterRuntimeBootstrap.Instance?.Session?.PlacedObjects;
            if (placedObjects != null)
            {
                environment = placedObjects.ApplyEnvironmentalInfluence(position, environment);
            }
            inventory?.ServerUpdateEquippedClothingWetness(
                elapsedSeconds,
                environment.Precipitation,
                environment.Humidity,
                environment.Sheltered,
                environment.ExternalHeat,
                exertion,
                serverState?.Physiology?.BodyCleanliness ?? 1f);
            var clothingInsulation = inventory?.GetServerClothingInsulation() ?? 0f;
            var rainProtection = inventory?.GetServerClothingRainProtection() ?? 0f;
            var clothingBurden = inventory?.GetServerClothingHygieneBurden() ?? 0f;
            if (serverState?.Physiology != null && elapsedSeconds > 0f
                && clothingBurden > 0f)
            {
                serverState.Physiology.BodyCleanliness = Mathf.Max(
                    0f,
                    serverState.Physiology.BodyCleanliness
                        - clothingBurden * elapsedSeconds / 18000f);
                if (clothingBurden > 0.72f)
                {
                    serverState.Physiology.SystemicInfection = Mathf.Clamp01(
                        serverState.Physiology.SystemicInfection
                            + (clothingBurden - 0.72f) * elapsedSeconds / 90000f);
                }
            }
            return new SurvivalEnvironment(
                environment.AmbientTemperatureC,
                environment.WindMetersPerSecond,
                environment.Humidity,
                environment.Precipitation * (1f - rainProtection),
                Mathf.Clamp01(0.08f + clothingInsulation),
                environment.ExternalHeat,
                environment.Sheltered,
                environment.SmokeConcentration,
                environment.DayFraction);
        }

        private static byte Stage(float value)
            => (byte)Mathf.Clamp(Mathf.FloorToInt(Mathf.Clamp01(value) * 5f), 0, 4);

        private static byte InverseStage(float value) => Stage(1f - value);

        private static byte TemperatureStage(float coreTemperatureC)
        {
            if (coreTemperatureC < 34f) return 4;
            if (coreTemperatureC < 35f) return 3;
            if (coreTemperatureC < 36.2f) return 2;
            if (coreTemperatureC > 41f) return 4;
            if (coreTemperatureC > 39.5f) return 3;
            if (coreTemperatureC > 38f) return 2;
            return 0;
        }

        private void OnPublicSymptomsChanged(PublicSymptomState _, PublicSymptomState __)
            => Changed?.Invoke();

        private void OnOwnerConditionChanged(OwnerConditionState _, OwnerConditionState __)
            => Changed?.Invoke();

        private void OnMovementCapabilityChanged(
            MovementCapabilityState _,
            MovementCapabilityState __)
            => Changed?.Invoke();

        private void OnObservedWoundsChanged(NetworkListEvent<ObservedWoundState> _)
            => Changed?.Invoke();

        private void OnTreatmentActivityChanged(
            TreatmentActivityState _,
            TreatmentActivityState __)
            => Changed?.Invoke();

        private void OnProgressionEntriesChanged(NetworkListEvent<ProgressionEntryState> _)
            => Changed?.Invoke();

        private void OnOwnerHeirOfferChanged(OwnerHeirOfferState _, OwnerHeirOfferState __)
            => Changed?.Invoke();

        private void OnPublicWorkerContractChanged(
            PublicWorkerContractState _, PublicWorkerContractState __)
            => Changed?.Invoke();

        private const SymptomFlags PubliclyVisibleSymptoms =
            SymptomFlags.Weakness
            | SymptomFlags.Fatigue
            | SymptomFlags.Microsleep
            | SymptomFlags.Shivering
            | SymptomFlags.Dizzy
            | SymptomFlags.Pain
            | SymptomFlags.SeverePain
            | SymptomFlags.Bleeding
            | SymptomFlags.Fever
            | SymptomFlags.Nausea
            | SymptomFlags.Breathless
            | SymptomFlags.Cough
            | SymptomFlags.Diarrhea
            | SymptomFlags.NutritionalDeficiency
            | SymptomFlags.Panic
            | SymptomFlags.Confusion
            | SymptomFlags.LosingConsciousness
            | SymptomFlags.AgonalBreathing;

        private static void ApplyStartingPredispositions(CharacterSurvivalState character)
        {
            character.Progression.EnsureInitialized();
            foreach (var trait in character.Traits)
            {
                switch (trait)
                {
                    case TraitId.Athletic:
                        character.Progression.Attributes[(int)CharacterAttributeId.AerobicCapacity] = 58f;
                        character.Progression.Attributes[(int)CharacterAttributeId.MuscularEndurance] = 55f;
                        break;
                    case TraitId.StrongBuild:
                        character.Progression.Attributes[(int)CharacterAttributeId.Strength] = 58f;
                        break;
                    case TraitId.Dexterous:
                        character.Progression.Attributes[(int)CharacterAttributeId.FineMotorControl] = 58f;
                        break;
                    case TraitId.KeenSenses:
                        character.Progression.Attributes[(int)CharacterAttributeId.Perception] = 58f;
                        break;
                    case TraitId.Myopia:
                        character.Progression.Attributes[(int)CharacterAttributeId.Perception] = 42f;
                        break;
                    case TraitId.Clumsy:
                        character.Progression.Attributes[(int)CharacterAttributeId.Coordination] = 42f;
                        character.Progression.Attributes[(int)CharacterAttributeId.Balance] = 45f;
                        break;
                    case TraitId.FragileBones:
                        character.Progression.Attributes[(int)CharacterAttributeId.Constitution] = 44f;
                        break;
                }
            }
        }

        private float CalculateSleepQuality(CharacterSurvivalState character)
        {
            var p = character.Physiology;
            var disruption = p.Pain * 0.35f
                + p.SystemicInfection * 0.25f
                + Mathf.Max(0f, p.BladderFill - 0.75f) * 0.7f
                + Mathf.Max(0f, 36f - p.CoreTemperatureC) * 0.15f
                + Mathf.Max(0f, 0.2f - p.EnergyReserve) * 0.6f
                + p.SleepNoiseBurden * 0.55f;
            var placedObjects = QuieterRuntimeBootstrap.Instance?.Session?.PlacedObjects;
            if (placedObjects != null && placedObjects.TryGetAssignedBedHygiene(
                    transform.position, character.CharacterId, out var bedHygiene))
            {
                disruption += (1f - bedHygiene) * 0.42f;
            }
            var circadianAlignment = Mathf.Lerp(
                0.46f, 0.86f, p.CircadianFatigue);
            return Mathf.Clamp01(circadianAlignment - disruption);
        }
    }
}
