using System;
using System.Collections.Generic;
using Quieter.Core;
using Quieter.Inventory;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace Quieter.Survival
{
    public struct PublicSymptomState : INetworkSerializable, IEquatable<PublicSymptomState>
    {
        public SymptomFlags Symptoms;
        public CharacterLifeState LifeState;
        public byte VisibleWoundCount;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer)
            where T : IReaderWriter
        {
            serializer.SerializeValue(ref Symptoms);
            serializer.SerializeValue(ref LifeState);
            serializer.SerializeValue(ref VisibleWoundCount);
        }

        public bool Equals(PublicSymptomState other)
            => Symptoms == other.Symptoms
                && LifeState == other.LifeState
                && VisibleWoundCount == other.VisibleWoundCount;
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
        public uint WoundedRegions;
        public uint FracturedRegions;
        public bool NeedsCharacterCreation;
        public bool Sleeping;
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
            serializer.SerializeValue(ref WoundedRegions);
            serializer.SerializeValue(ref FracturedRegions);
            serializer.SerializeValue(ref NeedsCharacterCreation);
            serializer.SerializeValue(ref Sleeping);
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
                && WoundedRegions == other.WoundedRegions
                && FracturedRegions == other.FracturedRegions
                && NeedsCharacterCreation == other.NeedsCharacterCreation
                && Sleeping == other.Sleeping
                && TraitSelectionError == other.TraitSelectionError
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

        private CharacterSurvivalState serverState;
        private PlayerInventory inventory;
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

        public event Action Changed;
        public event Action<CharacterSurvivalState> ServerDied;

        public PublicSymptomState PublicSymptoms => publicSymptoms.Value;
        public OwnerConditionState OwnerCondition => ownerCondition.Value;
        public CharacterSurvivalState ServerState => IsServer ? serverState : null;
        public bool ServerIsDead => IsServer
            && serverState?.Physiology?.LifeState == CharacterLifeState.Dead;
        public CharacterCapabilities CurrentCapabilities
            => movementCapability.Value.ToCapabilities();
        public TreatmentActivityState TreatmentActivity => treatmentActivity.Value;
        public int ObservedWoundCount => observedWounds.Count;
        public ObservedWoundState GetObservedWound(int index)
            => index >= 0 && index < observedWounds.Count ? observedWounds[index] : default;
        public int ProgressionEntryCount => progressionEntries.Count;
        public ProgressionEntryState GetProgressionEntry(int index)
            => index >= 0 && index < progressionEntries.Count
                ? progressionEntries[index]
                : default;

        private void Awake()
        {
            inventory = GetComponent<PlayerInventory>();
        }

        public override void OnNetworkSpawn()
        {
            publicSymptoms.OnValueChanged += OnPublicSymptomsChanged;
            ownerCondition.OnValueChanged += OnOwnerConditionChanged;
            movementCapability.OnValueChanged += OnMovementCapabilityChanged;
            observedWounds.OnListChanged += OnObservedWoundsChanged;
            treatmentActivity.OnValueChanged += OnTreatmentActivityChanged;
            progressionEntries.OnListChanged += OnProgressionEntriesChanged;
            Changed?.Invoke();
        }

        public override void OnNetworkDespawn()
        {
            publicSymptoms.OnValueChanged -= OnPublicSymptomsChanged;
            ownerCondition.OnValueChanged -= OnOwnerConditionChanged;
            movementCapability.OnValueChanged -= OnMovementCapabilityChanged;
            observedWounds.OnListChanged -= OnObservedWoundsChanged;
            treatmentActivity.OnValueChanged -= OnTreatmentActivityChanged;
            progressionEntries.OnListChanged -= OnProgressionEntriesChanged;
        }

        private void FixedUpdate()
        {
            if (!IsSpawned || !IsServer || serverState == null || ServerIsDead
                || !serverState.CreationCompleted)
            {
                return;
            }

            accumulator += Time.fixedDeltaTime;
            if (accumulator < ServerSimulationInterval)
            {
                return;
            }

            var elapsed = accumulator;
            accumulator = 0f;
            var wasDead = ServerIsDead;
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
            serverState.Offline = false;
            serverState.Physiology.SafeOfflineSeconds = 0f;
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
            return JsonUtility.FromJson<CharacterSurvivalState>(
                JsonUtility.ToJson(serverState));
        }

        public void ServerSetOffline(bool offline)
        {
            if (!IsServer || serverState == null) return;
            serverState.Offline = offline;
            serverState.Physiology.SafeOfflineSeconds = 0f;
            if (offline && serverState.Physiology.LifeState <= CharacterLifeState.Confused)
            {
                PhysiologySimulation.BeginSleep(serverState);
            }
            else if (!offline && serverState.Sleeping)
            {
                PhysiologySimulation.EndSleep(serverState, CalculateSleepQuality(serverState));
                serverState.Physiology.CurrentSleepSeconds = 0f;
            }
            ReplicateState();
        }

        public void ServerWakeFromDanger()
        {
            if (!IsServer || serverState == null) return;
            serverState.Physiology.SafeOfflineSeconds = 0f;
            serverState.Sleeping = false;
            serverState.Physiology.CurrentSleepSeconds = 0f;
            serverState.Physiology.SleepCycleConsolidated = false;
            ReplicateState();
        }

        public void ServerSetExertion(float value)
        {
            if (IsServer)
            {
                exertion = Mathf.Max(exertion, Mathf.Clamp01(value));
            }
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
            float toxinContamination)
        {
            if (!CanPerformServerAction()) return false;
            PhysiologySimulation.ConsumeFood(
                serverState,
                calories,
                protein,
                micronutrients,
                biologicalContamination,
                toxinContamination);
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

        public bool ServerTrySpendStamina(float amount)
        {
            if (!CanPerformServerAction() || amount <= 0f
                || serverState.Physiology.AcuteStamina < amount)
                return false;
            serverState.Physiology.AcuteStamina -= amount;
            ReplicateState();
            return true;
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
                && !serverState.Sleeping
                && !serverState.Bound
                && !pendingTreatmentActive
                && serverState.Physiology.LifeState <= CharacterLifeState.Confused;

        public void RequestTreatment(uint woundId, MedicalActionType action)
        {
            if (IsOwner && woundId != 0) BeginTreatmentServerRpc(woundId, action);
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

            pendingTreatmentActive = true;
            pendingTreatmentWoundId = woundId;
            pendingTreatmentAction = action;
            pendingTreatmentCompletesAt = ServerClock + TreatmentDurationSeconds(action);
            treatmentActivity.Value = new TreatmentActivityState
            {
                Active = true,
                WoundId = woundId,
                Action = action,
                CompletesAtServerTime = pendingTreatmentCompletesAt,
                Message = new FixedString512Bytes("Действие выполняется…"),
                Revision = ++treatmentRevision,
            };
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
            pendingTreatmentActive = false;
            var context = BuildTreatmentContext(action);
            var missing = MissingTreatmentMaterial(action, context);
            if (!string.IsNullOrEmpty(missing))
            {
                SetTreatmentMessage(missing);
                ReplicateState();
                return;
            }

            var result = PhysiologySimulation.Treat(serverState, woundId, action, context);
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
            var hasWater = inventory != null
                && inventory.TryGetServerCleanWater(100, out waterCleanliness);
            var hasSoap = inventory != null
                && inventory.TryGetServerItemCleanliness(31, out soapCleanliness);
            var hasNeedle = inventory != null
                && inventory.TryGetServerItemCleanliness(33, out needleCleanliness);
            var hasBandage = inventory != null
                && inventory.TryGetServerItemCleanliness(32, out bandageCleanliness);
            var hasSplint = inventory != null
                && inventory.TryGetServerItemCleanliness(34, out splintCleanliness);
            cleanliness = action switch
            {
                MedicalActionType.Wash => hasWater ? waterCleanliness : 0f,
                MedicalActionType.Disinfect => hasSoap ? soapCleanliness : 0f,
                MedicalActionType.Suture => hasNeedle ? needleCleanliness : 0f,
                MedicalActionType.Bandage => hasBandage ? bandageCleanliness : 0f,
                MedicalActionType.Splint => hasSplint ? splintCleanliness : 0f,
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
                hasSplint);
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
            _ => 8f,
        };

        private void CancelPendingTreatment(string message)
        {
            if (!pendingTreatmentActive) return;
            pendingTreatmentActive = false;
            SetTreatmentMessage(message);
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

            publicSymptoms.Value = new PublicSymptomState
            {
                Symptoms = symptoms & PubliclyVisibleSymptoms,
                LifeState = physiology.LifeState,
                VisibleWoundCount = (byte)Mathf.Min(byte.MaxValue, woundCount),
            };
            ReplicateObservedWounds();
            ownerCondition.Value = new OwnerConditionState
            {
                Symptoms = symptoms,
                LifeState = physiology.LifeState,
                DeathCause = physiology.DeathCause,
                ThirstStage = InverseStage(physiology.Hydration),
                HungerStage = InverseStage(Mathf.Min(physiology.StomachFullness, physiology.EnergyReserve)),
                FatigueStage = Stage(physiology.SleepDebt),
                TemperatureStage = TemperatureStage(physiology.CoreTemperatureC),
                PainStage = Stage(physiology.Pain),
                BladderStage = Stage(physiology.BladderFill),
                BowelStage = Stage(physiology.BowelFill),
                WoundedRegions = woundedRegions,
                FracturedRegions = fracturedRegions,
                NeedsCharacterCreation = !serverState.CreationCompleted,
                Sleeping = serverState.Sleeping,
                TraitSelectionError = traitSelectionError,
                Revision = ++replicatedRevision,
            };
            movementCapability.Value = serverState.CreationCompleted && !pendingTreatmentActive
                ? MovementCapabilityState.From(
                    PhysiologySimulation.CalculateCapabilities(serverState))
                : MovementCapabilityState.From(
                    new CharacterCapabilities(0f, 0f, 0f, 0f, 0f, false, false));
            Changed?.Invoke();
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
                        && wound.InternalBleeding,
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
                environment.ExternalHeat);
            var clothingInsulation = inventory?.GetServerClothingInsulation() ?? 0f;
            var rainProtection = inventory?.GetServerClothingRainProtection() ?? 0f;
            return new SurvivalEnvironment(
                environment.AmbientTemperatureC,
                environment.WindMetersPerSecond,
                environment.Humidity,
                environment.Precipitation * (1f - rainProtection),
                Mathf.Clamp01(0.08f + clothingInsulation),
                environment.ExternalHeat,
                environment.Sheltered,
                environment.SmokeConcentration);
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

        private static float CalculateSleepQuality(CharacterSurvivalState character)
        {
            var p = character.Physiology;
            var disruption = p.Pain * 0.35f
                + p.SystemicInfection * 0.25f
                + Mathf.Max(0f, p.BladderFill - 0.75f) * 0.7f
                + Mathf.Max(0f, 36f - p.CoreTemperatureC) * 0.15f
                + Mathf.Max(0f, 0.2f - p.EnergyReserve) * 0.6f;
            return Mathf.Clamp01(0.7f - disruption);
        }
    }
}
