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
