using System;
using System.Collections.Generic;
using UnityEngine;

namespace Quieter.Survival
{
    public static class LivingWorldSimulation
    {
        public const double GameSecondsPerDay = 86400d;
        public const double RequiredHeirContractGameSeconds = GameSecondsPerDay * 2d;
        public const double CaptureDurationRealSeconds = 3600d;

        public static bool CanRegisterHeir(
            InheritanceEligibility eligibility,
            out string error)
        {
            if (!eligibility.Alive) return Fail("Наследник мёртв.", out error);
            if (!eligibility.VoluntaryLoyalty)
                return Fail("Нужна добровольная лояльность, а не страх или плен.", out error);
            if (!eligibility.ActiveContract)
                return Fail("Нет действующего добровольного договора.", out error);
            if (!eligibility.AssignedBed)
                return Fail("Наследнику не назначена кровать.", out error);
            if (eligibility.FulfilledContractGameSeconds < RequiredHeirContractGameSeconds)
                return Fail("Условия договора выполнялись меньше двух игровых суток.", out error);
            if (eligibility.CompletedPersonalRequests < 3)
                return Fail("Нужно исполнить три личных запроса наследника.", out error);
            if (!eligibility.HasDeed)
                return Fail("Нужна наследственная грамота.", out error);
            error = string.Empty;
            return true;
        }

        public static bool TryRegisterHeir(
            EstateState estate,
            string heirCharacterId,
            InheritanceEligibility eligibility,
            long nowUtcTicks,
            long expectedRevision,
            out string error)
        {
            if (estate == null || string.IsNullOrWhiteSpace(heirCharacterId))
                return Fail("Не указано владение или тело наследника.", out error);
            if (estate.Revision != expectedRevision)
                return Fail("Владение уже было изменено другой операцией.", out error);
            if (!CanRegisterHeir(eligibility, out error)) return false;
            estate.RegisteredHeirCharacterId = heirCharacterId;
            estate.RegisteredAtUtcTicks = nowUtcTicks;
            estate.Revision++;
            return true;
        }

        public static EstateTransition ResolveOwnerDeath(
            EstateState estate,
            string deceasedCharacterId,
            bool registeredHeirStillEligible)
        {
            if (estate == null || estate.OwnerCharacterId != deceasedCharacterId)
                return new EstateTransition(false, string.Empty, null, null, null,
                    "Умерший не владеет этим имуществом.");
            if (!registeredHeirStillEligible
                || string.IsNullOrWhiteSpace(estate.RegisteredHeirCharacterId))
            {
                return new EstateTransition(false, string.Empty, null, null, null,
                    "Допустимый наследник отсутствует; замки остаются за умершим.");
            }

            // Only access rights and contracts move. The deceased inventory is
            // deliberately absent from this transition and remains on the body.
            return new EstateTransition(
                true,
                estate.RegisteredHeirCharacterId,
                new List<string>(estate.DoorLockIds),
                new List<string>(estate.ContainerLockIds),
                new List<string>(estate.WorkerContractIds),
                string.Empty);
        }

        public static bool AdvanceCapture(
            CaptureState capture,
            long nowUtcTicks,
            bool victimBoundOrImmobile,
            bool insideLockedCaptorOwnedCell,
            long expectedRevision)
        {
            if (capture == null || capture.Status == CaptureStatus.Completed
                || capture.Revision != expectedRevision)
            {
                return false;
            }

            if (!victimBoundOrImmobile || !insideLockedCaptorOwnedCell)
            {
                capture.Status = CaptureStatus.Cancelled;
                capture.ContinuousConditionsStartedUtcTicks = 0;
                capture.Revision++;
                return false;
            }

            if (capture.Status != CaptureStatus.ConditionsMet
                || capture.ContinuousConditionsStartedUtcTicks <= 0)
            {
                capture.Status = CaptureStatus.ConditionsMet;
                capture.ContinuousConditionsStartedUtcTicks = nowUtcTicks;
                capture.Revision++;
                return false;
            }

            var elapsed = TimeSpan.FromTicks(
                Math.Max(0, nowUtcTicks - capture.ContinuousConditionsStartedUtcTicks)).TotalSeconds;
            if (elapsed < CaptureDurationRealSeconds) return false;
            capture.Status = CaptureStatus.Completed;
            capture.Revision++;
            return true;
        }

        public static void UpdateCorpse(CorpseState corpse, long nowUtcTicks, float ambientTemperatureC)
        {
            if (corpse == null || corpse.Stage is CorpseDecayStage.Buried or CorpseDecayStage.Cremated)
                return;
            var realSeconds = TimeSpan.FromTicks(
                Math.Max(0, nowUtcTicks - corpse.DiedAtUtcTicks)).TotalSeconds;
            var temperatureFactor = Mathf.Lerp(0.35f, 2f, Mathf.InverseLerp(-5f, 35f, ambientTemperatureC));
            var gameDays = realSeconds / PhysiologySimulation.RealSecondsPerGameDay * temperatureFactor;
            corpse.Stage = gameDays switch
            {
                < 1d => CorpseDecayStage.Fresh,
                < 3d => CorpseDecayStage.EarlyDecay,
                < 7d => CorpseDecayStage.ActiveDecay,
                < 14d => CorpseDecayStage.AdvancedDecay,
                _ => CorpseDecayStage.DryRemains,
            };
            corpse.BiologicalContamination = corpse.Stage switch
            {
                CorpseDecayStage.Fresh => 0.08f,
                CorpseDecayStage.EarlyDecay => 0.42f,
                CorpseDecayStage.ActiveDecay => 1f,
                CorpseDecayStage.AdvancedDecay => 0.68f,
                _ => 0.2f,
            };
        }

        public static void Bury(CorpseState corpse)
        {
            if (corpse == null || corpse.Stage == CorpseDecayStage.Cremated) return;
            corpse.Stage = CorpseDecayStage.Buried;
            corpse.ItemsSealedByBurial = true;
            corpse.BiologicalContamination *= 0.04f;
        }

        public static void Cremate(CorpseState corpse)
        {
            if (corpse == null) return;
            corpse.Stage = CorpseDecayStage.Cremated;
            corpse.OrganicItemsDestroyed = true;
            corpse.BiologicalContamination = 0f;
        }

        public static float CalculateWorkerOutput(WorkerPerformanceInput input, float baseUnits)
        {
            if (baseUnits <= 0f) return 0f;
            var skill = Mathf.Lerp(0.3f, 1.5f, input.SkillLevel / 10f);
            var body = Mathf.Sqrt(input.PhysicalCapability * input.Health);
            var conditions = input.Motivation * input.ToolEfficiency
                * input.PathEfficiency * input.WeatherEfficiency;
            return Mathf.Max(0f, baseUnits * skill * body * conditions);
        }

        public static WorkerResponseKind EvaluateContractDay(
            WorkerContractState contract,
            RelationshipState relationship,
            float deliveredRationCalories,
            float experiencedSafety,
            float deterministicRoll,
            bool paymentDelivered = true)
        {
            if (contract == null || relationship == null || !contract.Active)
                return WorkerResponseKind.Leave;
            contract.EnsureInitialized();
            var rationMet = deliveredRationCalories + 1f >= contract.DailyRationCalories;
            var safetyMet = experiencedSafety + 0.001f >= contract.PromisedSafety;
            if (rationMet && safetyMet && (!contract.Voluntary || paymentDelivered))
            {
                contract.FulfilledContractGameSeconds += GameSecondsPerDay;
                contract.ConsecutiveBreaches = Math.Max(0, contract.ConsecutiveBreaches - 1);
                relationship.Trust = Mathf.Clamp01(relationship.Trust + 0.035f);
                relationship.Resentment = Mathf.Clamp01(relationship.Resentment - 0.028f);
                relationship.Loyalty = Mathf.Clamp01(relationship.Loyalty
                    + (contract.Voluntary ? 0.04f : 0.018f));
            }
            else
            {
                contract.ConsecutiveBreaches++;
                relationship.Trust = Mathf.Clamp01(relationship.Trust - 0.08f);
                relationship.Resentment = Mathf.Clamp01(relationship.Resentment + 0.11f);
                relationship.Loyalty = Mathf.Clamp01(relationship.Loyalty - 0.07f);
            }

            if (!contract.Voluntary && contract.FulfilledContractGameSeconds
                    >= GameSecondsPerDay * 30d
                && relationship.Trust >= 0.68f && relationship.Loyalty >= 0.62f
                && relationship.Resentment <= 0.3f)
            {
                contract.Voluntary = true;
                relationship.VoluntaryLoyalty = true;
            }

            var roll = Mathf.Clamp01(deterministicRoll);
            if (contract.Voluntary && contract.ConsecutiveBreaches >= 3
                && roll < Mathf.Clamp01(0.18f + relationship.Resentment * 0.55f))
                return WorkerResponseKind.Leave;
            if (!contract.Voluntary && relationship.Resentment >= 0.82f
                && relationship.Fear < 0.38f && roll < 0.32f)
                return WorkerResponseKind.Rebel;
            if (!contract.Voluntary && roll < 0.08f + relationship.Resentment * 0.16f)
                return WorkerResponseKind.Escape;
            if (relationship.Resentment >= 0.58f && roll < 0.24f)
                return WorkerResponseKind.Sabotage;
            return WorkerResponseKind.Stay;
        }

        public static float CalculateWorkerMotivation(
            WorkerContractState contract,
            RelationshipState relationship,
            PhysiologyState physiology)
        {
            if (physiology == null) return 0f;
            var physicalNeeds = Mathf.Clamp01(Mathf.Min(
                physiology.Hydration,
                Mathf.Min(physiology.EnergyReserve, 1f - physiology.SleepDebt)));
            var trust = relationship == null ? 0.4f : relationship.Trust;
            var resentment = relationship == null ? 0f : relationship.Resentment;
            var willing = contract?.Voluntary == true ? 0.25f : 0f;
            return Mathf.Clamp01(0.22f + physicalNeeds * 0.45f + trust * 0.22f
                + willing - resentment * 0.38f);
        }

        public static byte GetJobPriority(WorkerContractState contract, WorkerJobKind job)
        {
            contract?.EnsureInitialized();
            return contract == null || (int)job >= contract.JobPriorities.Length
                ? (byte)0 : contract.JobPriorities[(int)job];
        }

        public static byte AdjustJobPriority(
            WorkerContractState contract,
            WorkerJobKind job,
            int delta)
        {
            if (contract == null) return 0;
            contract.EnsureInitialized();
            var value = (byte)Mathf.Clamp(contract.JobPriorities[(int)job] + delta, 0, 3);
            contract.JobPriorities[(int)job] = value;
            var contains = contract.AllowedJobs.Contains(job);
            if (value > 0 && !contains) contract.AllowedJobs.Add(job);
            else if (value == 0 && contains) contract.AllowedJobs.Remove(job);
            return value;
        }

        public static bool TrySelectPriorityJob(
            WorkerContractState contract,
            WorkerJobKind current,
            out WorkerJobKind selected)
        {
            selected = current;
            if (contract == null) return false;
            contract.EnsureInitialized();
            var best = 0;
            foreach (WorkerJobKind job in Enum.GetValues(typeof(WorkerJobKind)))
            {
                var priority = GetJobPriority(contract, job);
                if (priority < best || priority == 0) continue;
                if (priority == best && job != current) continue;
                best = priority;
                selected = job;
            }
            return best > 0;
        }

        public static NpcActivityKind ChooseNpcActivity(
            PhysiologyState physiology,
            bool hasDrink,
            bool hasFood,
            bool threatened,
            bool assignedWork,
            bool withinWorkHours)
        {
            if (physiology == null) return NpcActivityKind.Idle;
            if (threatened) return NpcActivityKind.Flee;
            if (physiology.Hydration < 0.62f && !hasDrink)
                return NpcActivityKind.SeekWater;
            if ((physiology.StomachFullness < 0.4f
                    || physiology.EnergyReserve < 0.52f) && !hasFood)
                return NpcActivityKind.SeekFood;
            if (physiology.SleepDebt >= 0.68f) return NpcActivityKind.Rest;
            if (assignedWork && withinWorkHours) return NpcActivityKind.Work;
            return NpcActivityKind.Wander;
        }

        public static bool IsWithinWorkHours(
            float hour,
            float startHour,
            float endHour)
        {
            hour = Mathf.Repeat(hour, 24f);
            startHour = Mathf.Repeat(startHour, 24f);
            endHour = Mathf.Repeat(endHour, 24f);
            if (Mathf.Approximately(startHour, endHour)) return true;
            return startHour < endHour
                ? hour >= startHour && hour < endHour
                : hour >= startHour || hour < endHour;
        }

        public static (int Minimum, int Maximum) RevealSkillRange(
            int actualSkill,
            int relevantCompletedTasks)
        {
            actualSkill = Mathf.Clamp(actualSkill, 0, 10);
            var evidence = Mathf.Max(0, relevantCompletedTasks);
            var uncertainty = evidence switch
            {
                0 => 10,
                < 3 => 4,
                < 8 => 2,
                < 20 => 1,
                _ => 0,
            };
            return (Mathf.Max(0, actualSkill - uncertainty), Mathf.Min(10, actualSkill + uncertainty));
        }

        public static NpcPersonalRequestKind SelectPersonalRequest(
            string characterId, long gameDay)
        {
            unchecked
            {
                var hash = 1469598103934665603UL;
                foreach (var character in characterId ?? string.Empty)
                {
                    hash ^= character;
                    hash *= 1099511628211UL;
                }
                hash ^= (ulong)gameDay;
                hash *= 1099511628211UL;
                return (NpcPersonalRequestKind)(hash % 4UL);
            }
        }

        public static void DepositWaste(SanitationNodeState pit, float liters, float biologicalLoad)
        {
            if (pit == null || liters <= 0f) return;
            var previous = pit.ContentsLiters;
            pit.ContentsLiters += liters;
            pit.BiologicalLoad = WeightedAverage(
                pit.BiologicalLoad, previous, Mathf.Clamp01(biologicalLoad), liters);
        }

        public static float CalculateGroundLeakage(SanitationNodeState pit, float rainIntensity)
        {
            if (pit == null) return 0f;
            var capacity = Mathf.Max(0.01f, pit.CapacityLiters);
            var overflow = Mathf.Max(0f, pit.ContentsLiters - capacity) / capacity;
            var lining = pit.Kind == SanitationNodeKind.LinedPit ? 0.05f : 0.8f;
            var damage = pit.Broken ? 0.65f : 0f;
            return Mathf.Clamp01(pit.BiologicalLoad
                * (lining + damage + overflow * 1.2f + Mathf.Clamp01(rainIntensity) * 0.25f));
        }

        public static void TransferRainContamination(
            SanitationNodeState source,
            SanitationNodeState downstream,
            float rainIntensity,
            float elapsedGameSeconds)
        {
            if (source == null || downstream == null || rainIntensity <= 0f
                || elapsedGameSeconds <= 0f || downstream.Position.y > source.Position.y)
                return;
            var transfer = CalculateGroundLeakage(source, rainIntensity)
                * Mathf.Clamp01((float)(elapsedGameSeconds / GameSecondsPerDay))
                * 0.35f;
            downstream.BiologicalLoad = Mathf.Clamp01(downstream.BiologicalLoad + transfer);
        }

        public static (float Biological, float Toxins) BoilLiquid(
            float biologicalContamination,
            float toxinContamination,
            bool reachedBoil)
            => reachedBoil
                ? (0f, Mathf.Clamp01(toxinContamination))
                : (Mathf.Clamp01(biologicalContamination), Mathf.Clamp01(toxinContamination));

        public static MapDocumentState CopyAndMergeMaps(
            MapDocumentState first,
            MapDocumentState second,
            string newMapId,
            ulong newItemInstanceId,
            float cartographySkill)
        {
            var result = new MapDocumentState
            {
                MapId = newMapId ?? string.Empty,
                ItemInstanceId = newItemInstanceId,
                Title = first?.Title ?? second?.Title ?? "Карта",
                Revision = 1,
            };
            var seen = new HashSet<string>(StringComparer.Ordinal);
            CopyMarks(first, result, seen, cartographySkill);
            CopyMarks(second, result, seen, cartographySkill);
            return result;
        }

        private static void CopyMarks(
            MapDocumentState source,
            MapDocumentState destination,
            HashSet<string> seen,
            float skill)
        {
            if (source?.Marks == null) return;
            var accuracyMultiplier = Mathf.Lerp(0.65f, 1f, Mathf.Clamp01(skill));
            foreach (var mark in source.Marks)
            {
                if (mark == null || !seen.Add(mark.MarkId ?? string.Empty)) continue;
                destination.Marks.Add(new MapMarkState
                {
                    MarkId = mark.MarkId,
                    WorldPosition = mark.WorldPosition,
                    Text = mark.Text,
                    WrittenAtUtcTicks = mark.WrittenAtUtcTicks,
                    PositionalAccuracy = Mathf.Clamp01(mark.PositionalAccuracy * accuracyMultiplier),
                });
            }
        }

        private static float WeightedAverage(float first, float firstWeight, float second, float secondWeight)
        {
            var total = firstWeight + secondWeight;
            return total <= 0f ? 0f : Mathf.Clamp01(
                (first * firstWeight + second * secondWeight) / total);
        }

        private static bool Fail(string value, out string error)
        {
            error = value;
            return false;
        }
    }
}
