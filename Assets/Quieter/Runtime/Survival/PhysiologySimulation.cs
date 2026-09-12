using System;
using UnityEngine;

namespace Quieter.Survival
{
    /// <summary>
    /// Deterministic, server-authoritative organism model. Values describe
    /// physiological reserves and organ function; there is intentionally no HP.
    /// </summary>
    public static class PhysiologySimulation
    {
        public const float MinimumConsolidatingSleepSeconds = 60f;
        public const float FullConsolidatingSleepSeconds = 300f;
        public const float RealSecondsPerGameDay = 7200f;
        public const float MaximumStepSeconds = 1f;
        public const float ActiveSleepPhysiologyScale = 16f / 3f;
        public const float FictionalSilenceAttemptSeconds = 90f;

        public static void Simulate(
            CharacterSurvivalState character,
            float elapsedRealSeconds,
            SurvivalEnvironment environment,
            float exertion,
            bool offlineMetabolismSlowed = false)
        {
            if (character == null || elapsedRealSeconds <= 0f)
            {
                return;
            }

            character.EnsureInitialized();
            if (character.Physiology.LifeState == CharacterLifeState.Dead)
            {
                return;
            }

            var remaining = Mathf.Min(elapsedRealSeconds, RealSecondsPerGameDay * 30f);
            while (remaining > 0f)
            {
                var step = Mathf.Min(MaximumStepSeconds, remaining);
                SimulateStep(
                    character,
                    step,
                    environment,
                    Mathf.Clamp01(exertion),
                    offlineMetabolismSlowed);
                remaining -= step;
                if (character.Physiology.LifeState == CharacterLifeState.Dead)
                {
                    break;
                }
            }
        }

        public static void BeginSleep(CharacterSurvivalState character)
        {
            character.EnsureInitialized();
            if (character.Physiology.LifeState <= CharacterLifeState.Confused)
            {
                character.Sleeping = true;
                character.Physiology.SleepCycleConsolidated = false;
            }
        }

        public static float EndSleep(CharacterSurvivalState character, float quality)
        {
            character.EnsureInitialized();
            var durationQuality = Mathf.SmoothStep(
                0f,
                1f,
                Mathf.InverseLerp(
                    MinimumConsolidatingSleepSeconds,
                    FullConsolidatingSleepSeconds,
                    character.Physiology.CurrentSleepSeconds));
            character.Sleeping = false;
            character.Physiology.SleepCycleConsolidated = false;
            return CharacterProgression.ConsolidateSleep(
                character.Progression,
                Mathf.Clamp01(quality) * durationQuality);
        }

        /// <summary>
        /// Resolves one completed attempt of the setting's imaginary "ancient
        /// silence" rite. This deliberately contains no real-world mechanism:
        /// holding one's breath is not routed here and cannot cause this state.
        /// </summary>
        public static bool CompleteFictionalSilenceAttempt(
            CharacterSurvivalState character,
            out float gainedProgress)
        {
            gainedProgress = 0f;
            if (character == null) return false;
            character.EnsureInitialized();
            var rite = character.VoluntaryPassing;
            var will = Mathf.Clamp01(character.Progression.Attributes[
                (int)CharacterAttributeId.Willpower] / 100f);
            var composure = 1f - Mathf.Clamp01(character.Physiology.Stress);
            gainedProgress = 0.11f
                + will * 0.08f
                + rite.TechniqueFamiliarity * 0.06f
                + composure * 0.03f;
            rite.CompletedAttempts++;
            rite.TechniqueFamiliarity = Mathf.Clamp01(
                rite.TechniqueFamiliarity + 0.12f + will * 0.08f);
            rite.PassageProgress = Mathf.Clamp01(
                rite.PassageProgress + gainedProgress);
            if (rite.PassageProgress < 1f) return false;

            BeginFictionalVoluntaryPassing(character);
            return true;
        }

        public static void BeginFictionalVoluntaryPassing(
            CharacterSurvivalState character)
        {
            if (character == null) return;
            character.EnsureInitialized();
            var physiology = character.Physiology;
            if (physiology.LifeState == CharacterLifeState.Dead) return;

            character.VoluntaryPassing.AttemptActive = false;
            character.VoluntaryPassing.AttemptRemainingSeconds = 0f;
            // These are deliberately fictional, compressed organ-state changes,
            // not a simulation or description of a reproducible real process.
            character.Anatomy.HeartFunction = Mathf.Min(
                character.Anatomy.HeartFunction, 0.075f);
            character.Anatomy.BrainFunction = Mathf.Min(
                character.Anatomy.BrainFunction, 0.3f);
            physiology.Oxygenation = Mathf.Min(physiology.Oxygenation, 0.24f);
            physiology.Consciousness = Mathf.Min(physiology.Consciousness, 0.12f);
            physiology.CriticalCause = DeathCause.VoluntaryPassing;
            physiology.LifeState = CharacterLifeState.Agonal;
        }

        public static void ConsumeWater(
            CharacterSurvivalState character,
            float liters,
            float biologicalContamination,
            float toxinContamination,
            float electrolyteContent = 0f,
            float hydrationEfficiency = 1f)
        {
            character.EnsureInitialized();
            var physiology = character.Physiology;
            var amount = Mathf.Clamp(liters, 0f, 3f);
            physiology.Hydration = Mathf.Clamp01(
                physiology.Hydration + amount / 2.6f * Mathf.Clamp(hydrationEfficiency, -1f, 1.5f));
            physiology.BladderFill = Mathf.Clamp01(physiology.BladderFill + amount / 1.2f);
            physiology.ElectrolyteBalance = Mathf.Clamp01(
                physiology.ElectrolyteBalance + electrolyteContent * amount * 0.1f);
            var digestion = TraitCatalog.Resolve(character.Traits).Digestion;
            var biological = Mathf.Clamp01(biologicalContamination);
            character.Conditions.WaterborneInfection = Mathf.Clamp01(
                character.Conditions.WaterborneInfection
                + biological * amount * 0.18f / digestion);
            physiology.SystemicInfection = Mathf.Clamp01(
                physiology.SystemicInfection
                + biological * amount * 0.008f / digestion);
            physiology.ToxinLoad = Mathf.Clamp01(
                physiology.ToxinLoad + Mathf.Clamp01(toxinContamination) * amount * 0.08f);
        }

        public static void ConsumeFood(
            CharacterSurvivalState character,
            float calories,
            float protein,
            float micronutrients,
            float biologicalContamination,
            float toxinContamination,
            float fat = 0f,
            float minerals = 0f)
        {
            character.EnsureInitialized();
            var physiology = character.Physiology;
            var digestion = TraitCatalog.Resolve(character.Traits).Digestion;
            var chewingEfficiency = Mathf.Lerp(
                1f, 0.62f, physiology.MissingTeeth / 32f);
            physiology.StomachFullness = Mathf.Clamp01(
                physiology.StomachFullness + Mathf.Max(0f, calories) / 2600f);
            physiology.DigestingEnergy += Mathf.Max(0f, calories)
                / 9000f * chewingEfficiency;
            physiology.DigestingProtein += Mathf.Max(0f, protein)
                / 450f * chewingEfficiency;
            physiology.DigestingFat += Mathf.Max(0f, fat)
                / 650f * chewingEfficiency;
            physiology.DigestingMicronutrients += Mathf.Max(0f, micronutrients) * 0.1f;
            physiology.DigestingMinerals += Mathf.Max(0f, minerals) * 0.1f;
            physiology.BowelFill = Mathf.Clamp01(
                physiology.BowelFill + Mathf.Max(0f, calories) / 4500f);
            var biological = Mathf.Clamp01(biologicalContamination);
            physiology.DigestingBiologicalContamination += biological / digestion;
            physiology.DigestingToxins += Mathf.Clamp01(toxinContamination);
        }

        public static void ConsumeHerbalInfusion(
            CharacterSurvivalState character,
            float liters)
        {
            character.EnsureInitialized();
            var amount = Mathf.Clamp(liters, 0f, 1f);
            if (amount <= 0f) return;
            character.Conditions.HerbalAnalgesiaSeconds = Mathf.Max(
                character.Conditions.HerbalAnalgesiaSeconds,
                240f + amount * 900f);
            character.Conditions.HerbalAnalgesiaStrength = Mathf.Max(
                character.Conditions.HerbalAnalgesiaStrength,
                0.04f + amount * 0.08f);
            // An ordinary medieval infusion is supportive, variable and not a
            // substitute for treating infection or trauma.
            character.Physiology.ToxinLoad = Mathf.Clamp01(
                character.Physiology.ToxinLoad + amount * 0.004f);
        }

        public static void RelieveBladder(CharacterSurvivalState character, bool cleanly)
        {
            character.EnsureInitialized();
            character.Physiology.BladderFill = 0f;
            if (!cleanly)
            {
                character.Physiology.BodyCleanliness = Mathf.Max(
                    0f,
                    character.Physiology.BodyCleanliness - 0.35f);
            }
        }

        public static void RelieveBowel(CharacterSurvivalState character, bool cleanly)
        {
            character.EnsureInitialized();
            character.Physiology.BowelFill = 0f;
            if (!cleanly)
            {
                character.Physiology.BodyCleanliness = Mathf.Max(
                    0f,
                    character.Physiology.BodyCleanliness - 0.55f);
                character.Physiology.HandCleanliness = Mathf.Max(
                    0f,
                    character.Physiology.HandCleanliness - 0.45f);
            }
        }

        public static WoundState AddInjury(
            CharacterSurvivalState character,
            BodyRegion region,
            DamageKind damageKind,
            float impact,
            float contamination = 0f,
            bool internalDamage = false)
        {
            character.EnsureInitialized();
            var severity = Mathf.Clamp01(impact);
            var modifiers = TraitCatalog.Resolve(character.Traits);
            var type = ResolveInjuryType(damageKind, severity, modifiers.FractureResistance);
            var bleeding = type switch
            {
                InjuryType.Laceration => severity * 0.65f,
                InjuryType.Puncture => severity * 0.45f,
                InjuryType.OpenFracture => severity * 0.8f,
                InjuryType.Abrasion => severity * 0.12f,
                _ => 0f,
            };
            // The same visible cut is much more dangerous near the large neck
            // vessels.  This keeps lethality anatomical instead of introducing
            // a hidden pool of hit points.
            if (region == BodyRegion.Neck && bleeding > 0f)
            {
                bleeding = Mathf.Clamp01(bleeding * 1.5f);
            }

            var vulnerableCavity = region is BodyRegion.Head or BodyRegion.Neck
                or BodyRegion.Chest or BodyRegion.Abdomen or BodyRegion.Pelvis;
            var internalBleeding = internalDamage || (vulnerableCavity
                && damageKind switch
                {
                    DamageKind.Blunt or DamageKind.Fall => severity >= 0.56f,
                    DamageKind.Puncture => severity >= 0.32f,
                    DamageKind.Edged => severity >= 0.74f,
                    _ => false,
                });
            var internalBleedingSeverity = internalBleeding
                ? Mathf.Clamp01(severity * (damageKind == DamageKind.Puncture ? 0.92f : 0.72f))
                : 0f;

            var wound = new WoundState
            {
                WoundId = character.Anatomy.NextWoundId++,
                Region = region,
                Type = type,
                Severity = severity,
                TissueDamage = severity,
                Contamination = Mathf.Clamp01(contamination + (type == InjuryType.OpenFracture ? 0.25f : 0f)),
                Bleeding = bleeding,
                InternalBleedingSeverity = internalBleedingSeverity,
                Pain = Mathf.Clamp01(severity * 0.9f),
                InternalBleeding = internalBleeding,
            };
            // C# cannot pattern-match an extension-like category in an object
            // initializer, so fractures get their pain correction here.
            if (wound.IsFracture)
            {
                wound.Pain = Mathf.Clamp01(severity * 1.2f);
            }

            character.Anatomy.Wounds.Add(wound);
            ApplyOrganTrauma(character.Anatomy, region, damageKind, severity);
            return wound;
        }

        public static TreatmentResult Treat(
            CharacterSurvivalState character,
            uint woundId,
            MedicalActionType action,
            TreatmentContext context)
        {
            character.EnsureInitialized();
            var wound = character.Anatomy.Wounds.Find(candidate => candidate.WoundId == woundId);
            if (wound == null || wound.Healed)
            {
                return new TreatmentResult(false, "Рана не найдена.", 0f);
            }

            var dirtyHands = 1f - context.PractitionerHandCleanliness;
            var contaminationRisk = Mathf.Clamp01(
                (1f - context.MaterialCleanliness) * 0.55f + dirtyHands * 0.35f);
            switch (action)
            {
                case MedicalActionType.Inspect:
                    return new TreatmentResult(true, DescribeWound(wound, context.Skill), 0f);
                case MedicalActionType.ApplyPressure:
                    if (!wound.IsOpen || wound.Bleeding <= 0.01f)
                        return new TreatmentResult(false, "Кровоточащей открытой раны нет.", 0f);
                    wound.PressureApplied = true;
                    wound.Bleeding *= Mathf.Lerp(0.6f, 0.22f, context.Skill);
                    break;
                case MedicalActionType.Wash:
                    if (!wound.IsOpen)
                        return new TreatmentResult(false, "У этой травмы нет открытой раны для промывания.", 0f);
                    if (!context.HasWater)
                        return new TreatmentResult(false, "Нужна чистая или кипячёная вода.", 0f);
                    wound.Washed = true;
                    wound.Contamination *= Mathf.Lerp(0.55f, 0.18f, context.MaterialCleanliness);
                    break;
                case MedicalActionType.Disinfect:
                    if (!wound.IsOpen || !context.HasDisinfectant || !wound.Washed)
                        return new TreatmentResult(false, "Сначала промойте рану, затем используйте подходящий состав.", 0f);
                    wound.Disinfected = true;
                    wound.Contamination *= 0.35f;
                    break;
                case MedicalActionType.Suture:
                    if (!wound.IsOpen)
                        return new TreatmentResult(false, "Закрытая травма не требует швов.", 0f);
                    if (!context.HasNeedleAndThread || !wound.Washed)
                        return new TreatmentResult(false, "Нужны чистые игла и нить, а рану сначала следует промыть.", 0f);
                    if (!wound.PressureApplied && wound.Bleeding > 0.12f)
                        return new TreatmentResult(false, "Сначала остановите сильное кровотечение.", 0f);
                    if (wound.Infection > 0.25f || wound.Contamination > 0.35f)
                        return new TreatmentResult(false, "Закрывать загрязнённую или заражённую рану опасно.", 0.8f);
                    wound.Sutured = true;
                    wound.Bleeding *= 0.18f;
                    break;
                case MedicalActionType.Bandage:
                    if (!wound.IsOpen)
                        return new TreatmentResult(false, "Для закрытой травмы повязка не нужна.", 0f);
                    if (!context.HasBandage)
                        return new TreatmentResult(false, "Нужна ткань для повязки.", 0f);
                    wound.Bandaged = true;
                    wound.Bleeding *= Mathf.Lerp(0.7f, 0.3f, context.Skill);
                    break;
                case MedicalActionType.Splint:
                    if (!wound.IsFracture || !context.HasSplint)
                        return new TreatmentResult(false, "Для этой травмы подходящая шина недоступна.", 0f);
                    wound.Splinted = true;
                    wound.Pain *= 0.72f;
                    break;
                case MedicalActionType.RemoveBandage:
                    if (!wound.Bandaged)
                        return new TreatmentResult(false, "Повязки на этой травме нет.", 0f);
                    wound.Bandaged = false;
                    break;
                default:
                    return new TreatmentResult(false, "Это действие пока неприменимо к выбранной ране.", 0f);
            }

            if (wound.IsOpen && contaminationRisk > 0f)
            {
                wound.Contamination = Mathf.Clamp01(
                    wound.Contamination + contaminationRisk * (1f - context.Skill) * 0.3f);
            }

            return new TreatmentResult(true, "Действие выполнено.", contaminationRisk);
        }

        public static TreatmentResult ExtractTooth(
            CharacterSurvivalState character,
            TreatmentContext context)
        {
            character.EnsureInitialized();
            var p = character.Physiology;
            if (p.DentalHealth > 0.55f && p.DentalInfection < 0.12f)
                return new TreatmentResult(false, "Нет зуба, который явно требует удаления.", 0f);
            if (!context.HasWater || !context.HasBandage)
                return new TreatmentResult(false,
                    "Для удаления нужны чистая вода и ткань для остановки крови.", 0f);
            var contaminationRisk = Mathf.Clamp01(
                (1f - context.MaterialCleanliness) * 0.5f
                + (1f - context.PractitionerHandCleanliness) * 0.35f
                + (1f - context.Skill) * 0.35f);
            p.MissingTeeth = (byte)Mathf.Min(32, p.MissingTeeth + 1);
            p.DentalHealth = Mathf.Clamp01(Mathf.Max(0.38f, p.DentalHealth + 0.18f));
            p.DentalInfection = Mathf.Clamp01(
                p.DentalInfection * Mathf.Lerp(0.72f, 0.22f, context.Skill)
                + contaminationRisk * 0.12f);
            // A tooth socket is an open wound, but it is not penetrating head
            // trauma. Creating it directly avoids incorrectly damaging the brain.
            var severity = Mathf.Lerp(0.3f, 0.12f, context.Skill);
            var wound = new WoundState
            {
                WoundId = character.Anatomy.NextWoundId++,
                Region = BodyRegion.Head,
                Type = InjuryType.Puncture,
                Severity = severity,
                TissueDamage = severity,
                Contamination = contaminationRisk,
                Bleeding = severity * Mathf.Lerp(0.32f, 0.14f, context.Skill),
                Pain = Mathf.Lerp(0.65f, 0.35f, context.Skill),
            };
            character.Anatomy.Wounds.Add(wound);
            return new TreatmentResult(
                true,
                "Зуб удалён. Осталась кровоточащая рана, которую нужно наблюдать и обрабатывать.",
                contaminationRisk);
        }

        public static void ExposeRespiratoryInfection(
            CharacterSurvivalState character,
            float exposure)
        {
            if (character == null || exposure <= 0f) return;
            character.EnsureInitialized();
            var immunity = TraitCatalog.Resolve(character.Traits).Immunity;
            character.Conditions.RespiratoryInfection = Mathf.Clamp01(
                character.Conditions.RespiratoryInfection
                + Mathf.Clamp01(exposure) * 0.08f / immunity);
        }

        public static TreatmentResult ApplySupportiveTreatment(
            CharacterSurvivalState character,
            MedicalActionType action,
            TreatmentContext context)
        {
            character.EnsureInitialized();
            var p = character.Physiology;
            var conditions = character.Conditions;
            switch (action)
            {
                case MedicalActionType.Warm:
                    if (!context.HasHeatSource)
                        return new TreatmentResult(false, "Нужен работающий источник тепла.", 0f);
                    if (p.CoreTemperatureC >= 37.2f)
                        return new TreatmentResult(false, "Дополнительное согревание сейчас опасно.", 0f);
                    conditions.WarmingCareSeconds = Mathf.Max(
                        conditions.WarmingCareSeconds, Mathf.Lerp(480f, 900f, context.Skill));
                    conditions.CoolingCareSeconds = 0f;
                    return new TreatmentResult(true,
                        "Начато постепенное согревание. Оставайтесь у источника тепла и следите за сознанием.", 0f);
                case MedicalActionType.Cool:
                    if (!context.HasWater)
                        return new TreatmentResult(false, "Для охлаждения нужна чистая вода.", 0f);
                    if (p.CoreTemperatureC <= 37.6f)
                        return new TreatmentResult(false, "Охлаждение нормальной температуры навредит.", 0f);
                    conditions.CoolingCareSeconds = Mathf.Max(
                        conditions.CoolingCareSeconds, Mathf.Lerp(480f, 900f, context.Skill));
                    conditions.WarmingCareSeconds = 0f;
                    return new TreatmentResult(true,
                        "Начато постепенное охлаждение водой. Резкого выздоровления не будет.", 0f);
                case MedicalActionType.OralRehydration:
                    if (!context.HasWater || !context.HasSalt)
                        return new TreatmentResult(false,
                            "Нужны чистая вода и каменная соль для питьевого раствора.", 0f);
                    if (p.Hydration > 0.88f && p.ElectrolyteBalance > 0.8f
                        && conditions.GastrointestinalInfection < 0.12f)
                    {
                        return new TreatmentResult(false,
                            "Признаков обезвоживания или кишечной потери жидкости нет.", 0f);
                    }
                    ConsumeWater(character, 0.35f, 0f, 0f, 0.9f, 1.08f);
                    return new TreatmentResult(true,
                        "Раствор выпит. При поносе его придётся готовить повторно и продолжать наблюдение.", 0f);
                case MedicalActionType.HerbalPainRelief:
                    if (!context.HasHerbs)
                        return new TreatmentResult(false, "Нужны лекарственные травы.", 0f);
                    if (p.Pain < 0.08f && conditions.RespiratoryInfection < 0.15f
                        && p.SystemicInfection < 0.2f)
                        return new TreatmentResult(false, "Явных симптомов для такого состава нет.", 0f);
                    conditions.HerbalAnalgesiaSeconds = Mathf.Max(
                        conditions.HerbalAnalgesiaSeconds, Mathf.Lerp(600f, 1500f, context.Skill));
                    conditions.HerbalAnalgesiaStrength = Mathf.Max(
                        conditions.HerbalAnalgesiaStrength, Mathf.Lerp(0.08f, 0.24f, context.Skill));
                    var complication = (1f - context.Skill) * 0.16f
                        + (1f - context.MaterialCleanliness) * 0.12f;
                    p.ToxinLoad = Mathf.Clamp01(p.ToxinLoad + complication * 0.04f);
                    return new TreatmentResult(true,
                        "Травяной состав может приглушить симптомы, но не устраняет их причину.",
                        complication);
                case MedicalActionType.AntiparasiticCourse:
                    if (!context.HasHerbs || !context.HasWater)
                        return new TreatmentResult(false,
                            "Для курса нужны лекарственные травы и чистая вода.", 0f);
                    if (conditions.ParasiteLoad < 0.08f)
                        return new TreatmentResult(false,
                            "Признаков паразитарной нагрузки недостаточно; риск состава не оправдан.", 0f);
                    conditions.AntiparasiticCourseSeconds = Mathf.Max(
                        conditions.AntiparasiticCourseSeconds, RealSecondsPerGameDay * 2f);
                    conditions.AntiparasiticCourseStrength = Mathf.Max(
                        conditions.AntiparasiticCourseStrength,
                        Mathf.Lerp(0.1f, 0.28f, context.Skill));
                    var courseRisk = (1f - context.Skill) * 0.28f
                        + (1f - context.MaterialCleanliness) * 0.18f;
                    p.ToxinLoad = Mathf.Clamp01(p.ToxinLoad + courseRisk * 0.07f);
                    return new TreatmentResult(true,
                        "Начат двухсуточный противопаразитарный курс. Он действует постепенно и сам может вызвать недомогание.",
                        courseRisk);
                default:
                    return new TreatmentResult(false, "Это не действие общего ухода.", 0f);
            }
        }

        public static SymptomFlags ObserveSymptoms(CharacterSurvivalState character)
        {
            character.EnsureInitialized();
            var p = character.Physiology;
            var symptoms = SymptomFlags.None;
            if (p.Hydration < 0.78f) symptoms |= SymptomFlags.Thirst;
            if (p.Hydration < 0.55f) symptoms |= SymptomFlags.DryMouth;
            if (p.StomachFullness < 0.35f) symptoms |= SymptomFlags.Hunger;
            var nutritionFloor = Mathf.Min(p.EnergyReserve,
                Mathf.Min(p.ProteinReserve, Mathf.Min(p.FatReserve,
                    Mathf.Min(p.MicronutrientReserve, p.MineralReserve))));
            if (nutritionFloor < 0.4f || p.BloodVolume < 0.65f)
                symptoms |= SymptomFlags.Weakness;
            if (Mathf.Min(p.MicronutrientReserve, p.MineralReserve) < 0.28f)
                symptoms |= SymptomFlags.NutritionalDeficiency;
            if (p.SleepDebt > 0.3f || p.CircadianFatigue > 0.58f)
                symptoms |= SymptomFlags.Fatigue;
            if (p.SleepDebt > 0.75f
                || p.SleepDebt > 0.52f && p.CircadianFatigue > 0.78f)
                symptoms |= SymptomFlags.Microsleep;
            if (p.CoreTemperatureC < 36.2f) symptoms |= SymptomFlags.Cold;
            if (p.CoreTemperatureC < 35.5f) symptoms |= SymptomFlags.Shivering;
            if (p.CoreTemperatureC > 38.2f) symptoms |= SymptomFlags.Overheated;
            if (p.Oxygenation < 0.75f || p.BloodVolume < 0.65f) symptoms |= SymptomFlags.Dizzy;
            if (p.Oxygenation < 0.55f || p.BloodVolume < 0.45f) symptoms |= SymptomFlags.TunnelVision;
            if (p.Pain > 0.2f) symptoms |= SymptomFlags.Pain;
            if (p.Pain > 0.65f) symptoms |= SymptomFlags.SeverePain;
            if (HasActiveBleeding(character.Anatomy)) symptoms |= SymptomFlags.Bleeding;
            if (p.SystemicInfection > 0.3f) symptoms |= SymptomFlags.Fever;
            if (p.SystemicInfection > 0.25f || p.ToxinLoad > 0.2f) symptoms |= SymptomFlags.Nausea;
            if (p.Oxygenation < 0.8f) symptoms |= SymptomFlags.Breathless;
            if (p.SmokeIrritation > 0.16f) symptoms |= SymptomFlags.SmokeIrritation;
            var gastrointestinal = character.Conditions.GastrointestinalInfection;
            if (character.Conditions.RespiratoryInfection > 0.08f)
                symptoms |= SymptomFlags.Cough;
            if (gastrointestinal > 0.1f)
                symptoms |= SymptomFlags.AbdominalCramps;
            if (gastrointestinal > 0.24f)
                symptoms |= SymptomFlags.Diarrhea;
            if (character.Conditions.ParasiteLoad > 0.22f)
                symptoms |= SymptomFlags.ParasiteSigns;
            if (gastrointestinal > 0.16f) symptoms |= SymptomFlags.Nausea;
            if (character.Conditions.RespiratoryInfection > 0.28f
                || gastrointestinal > 0.35f) symptoms |= SymptomFlags.Fever;
            if (p.Stress > 0.75f) symptoms |= SymptomFlags.Panic;
            if (p.BladderFill > 0.72f) symptoms |= SymptomFlags.BladderPressure;
            if (p.BowelFill > 0.78f) symptoms |= SymptomFlags.BowelPressure;
            if (p.BodyCleanliness < 0.35f) symptoms |= SymptomFlags.Dirty;
            if (p.DentalHealth < 0.45f || p.DentalInfection > 0.12f)
                symptoms |= SymptomFlags.Toothache;
            if (p.LifeState >= CharacterLifeState.Confused) symptoms |= SymptomFlags.Confusion;
            if (p.LifeState >= CharacterLifeState.Unconscious) symptoms |= SymptomFlags.LosingConsciousness;
            if (p.LifeState == CharacterLifeState.Agonal) symptoms |= SymptomFlags.AgonalBreathing;
            return symptoms;
        }

        public static CharacterCapabilities CalculateCapabilities(CharacterSurvivalState character)
        {
            character.EnsureInitialized();
            var p = character.Physiology;
            if (p.LifeState >= CharacterLifeState.Unconscious || character.Bound
                || character.Sleeping)
            {
                return new CharacterCapabilities(0f, 0f, 0f, 0f, 0f, false, false);
            }

            var leftLeg = RegionFunction(character.Anatomy, true, BodyRegion.LeftThigh, BodyRegion.LeftShin, BodyRegion.LeftFoot);
            var rightLeg = RegionFunction(character.Anatomy, true, BodyRegion.RightThigh, BodyRegion.RightShin, BodyRegion.RightFoot);
            var legs = (leftLeg + rightLeg) * 0.5f;
            var circulation = Mathf.Clamp01(p.BloodVolume * 1.2f);
            var respiration = Mathf.Clamp01(p.Oxygenation * character.Anatomy.LungFunction * 1.25f);
            var energy = Mathf.Clamp01(Mathf.Min(
                p.Hydration * 1.3f,
                Mathf.Min(p.EnergyReserve * 1.4f, p.ElectrolyteBalance * 1.25f)));
            var consciousness = Mathf.Clamp01(p.Consciousness);
            var fatigue = Mathf.Max(p.SleepDebt, p.CircadianFatigue * 0.5f);
            var painPenalty = Mathf.Lerp(1f, 0.35f, p.Pain);
            var strength = character.Progression.Attributes[(int)CharacterAttributeId.Strength];
            var comfortableMass = 12f + strength * 0.35f;
            var loadRatio = p.CarriedMassKg / Mathf.Max(1f, comfortableMass);
            var loadPenalty = loadRatio <= 1f
                ? 1f
                : Mathf.Clamp01(1f - (loadRatio - 1f) * 0.72f);
            var movement = Mathf.Pow(legs * circulation * respiration * energy, 0.25f)
                * consciousness * painPenalty * loadPenalty
                * Mathf.Lerp(1f, 0.72f, fatigue);
            var fineMotor = RegionFunction(character.Anatomy, false, BodyRegion.LeftHand, BodyRegion.RightHand)
                * Mathf.Lerp(1f, 0.3f, p.Pain)
                * Mathf.Clamp01(p.Oxygenation * 1.2f)
                * Mathf.Lerp(1f, 0.68f, fatigue);

            return new CharacterCapabilities(
                movement,
                Mathf.Sqrt(movement),
                movement * legs,
                Mathf.Clamp01(p.Hydration * respiration * (1f - fatigue * 0.6f)),
                fineMotor,
                movement > 0.08f && loadRatio < 2.2f,
                movement > 0.48f && p.AcuteStamina > 0.08f && loadRatio < 1.35f);
        }

        private static void SimulateStep(
            CharacterSurvivalState character,
            float seconds,
            SurvivalEnvironment environment,
            float exertion,
            bool offlineMetabolismSlowed)
        {
            var p = character.Physiology;
            var a = character.Anatomy;
            var traits = TraitCatalog.Resolve(character.Traits);
            var sleeping = character.Sleeping;
            var internalScale = offlineMetabolismSlowed
                ? 0.25f
                : sleeping && !character.Offline
                    ? ActiveSleepPhysiologyScale
                    : 1f;
            var metabolicSeconds = seconds * internalScale;
            var circadianTarget = CalculateCircadianSleepPressure(environment.DayFraction);
            p.CircadianFatigue = Mathf.MoveTowards(
                p.CircadianFatigue, circadianTarget, seconds / 1200f);
            SimulateDigestion(character, metabolicSeconds, traits);

            var metabolism = traits.Metabolism * (1f + exertion * 1.35f);
            p.Hydration -= metabolicSeconds / (9000f / metabolism);
            p.StomachFullness -= metabolicSeconds / (2700f / metabolism);
            p.EnergyReserve -= metabolicSeconds / (72000f / metabolism);
            p.ProteinReserve -= metabolicSeconds / (180000f / metabolism);
            p.FatReserve -= metabolicSeconds / (240000f / metabolism);
            p.MicronutrientReserve -= metabolicSeconds / 300000f;
            p.MineralReserve -= metabolicSeconds / 360000f;
            p.BladderFill += metabolicSeconds / 3000f * Mathf.Max(0.3f, p.Hydration);
            p.BowelFill += metabolicSeconds / 11000f * Mathf.Max(0.2f, p.StomachFullness);

            if (sleeping)
            {
                var sleepInterruption = Mathf.Clamp01(
                    p.Pain * 0.5f
                    + Mathf.Max(0f, 36f - p.CoreTemperatureC) * 0.3f
                    + Mathf.Max(0f, p.BladderFill - 0.8f)
                    + p.SystemicInfection * 0.4f);
                // A personal sleep lasts 5-10 real minutes. Internal metabolism is
                // accelerated separately, but rest duration stays on real time.
                p.SleepDebt -= seconds / (420f * traits.SleepNeed)
                    * (1f - sleepInterruption * 0.85f)
                    * Mathf.Lerp(0.55f, 1f, p.CircadianFatigue);
                p.AcuteStamina += seconds / 30f;
            }
            else
            {
                p.SleepDebt += metabolicSeconds / (14400f / traits.SleepNeed)
                    * (0.62f + p.CircadianFatigue * 0.45f + exertion * 0.3f);
                p.AcuteStamina += seconds / 42f * (1f - exertion * 1.5f);
                p.AcuteStamina -= seconds / 22f * exertion;
            }

            SimulateTemperature(p, seconds, environment, exertion, traits);
            ApplySupportiveCare(character, seconds);
            SimulateWounds(character, metabolicSeconds, seconds, exertion, traits);
            SimulateRehabilitation(character, metabolicSeconds, exertion);
            SimulateDiseaseAndOrgans(character, metabolicSeconds, traits);

            var lungFunction = Mathf.Max(0.01f, a.LungFunction * traits.LungCapacity);
            var smoke = environment.SmokeConcentration;
            p.SmokeIrritation = Mathf.MoveTowards(
                p.SmokeIrritation,
                smoke,
                seconds * (smoke > p.SmokeIrritation ? 0.045f : 0.012f));
            p.ToxinLoad += smoke * seconds / 5000f;
            if (smoke > 0.55f)
            {
                var lungDamage = (smoke - 0.55f) * seconds / 12000f;
                a.LeftLungFunction = Mathf.Max(0f, a.LeftLungFunction - lungDamage);
                a.RightLungFunction = Mathf.Max(0f, a.RightLungFunction - lungDamage);
            }
            var oxygenTarget = Mathf.Clamp01(
                lungFunction * (1f - exertion * 0.32f)
                * Mathf.Lerp(0.55f, 1f, p.BloodVolume)
                * (1f - smoke * 0.96f)
                * Mathf.Lerp(1f, 0.62f, character.Conditions.RespiratoryInfection));
            p.Oxygenation = Mathf.MoveTowards(p.Oxygenation, oxygenTarget, seconds * 0.12f);

            var chronicPain = character.Traits.Contains(TraitId.ChronicPain) ? 0.18f : 0f;
            var eliminationPressure = Mathf.Max(
                Mathf.InverseLerp(0.78f, 1f, p.BladderFill),
                Mathf.InverseLerp(0.82f, 1f, p.BowelFill));
            var dentalPain = Mathf.Max(
                Mathf.InverseLerp(0.5f, 0.1f, p.DentalHealth) * 0.2f,
                p.DentalInfection * 0.32f);
            var woundPain = chronicPain + eliminationPressure * 0.24f + dentalPain;
            foreach (var wound in a.Wounds)
            {
                if (!wound.Healed) woundPain += wound.Pain * 0.35f;
            }
            var analgesia = character.Conditions.HerbalAnalgesiaSeconds > 0f
                ? character.Conditions.HerbalAnalgesiaStrength : 0f;
            p.Pain = Mathf.Clamp01(woundPain / traits.PainTolerance - analgesia);
            var dangerStress = Mathf.Max(
                Mathf.Max(p.Pain, 1f - p.Oxygenation),
                eliminationPressure * 0.75f);
            p.Stress = Mathf.MoveTowards(p.Stress, dangerStress, seconds * 0.02f);

            if (p.BladderFill >= 0.999f)
            {
                RelieveBladder(character, false);
                unchecked { p.EliminationAccidentRevision++; }
                p.LastEliminationAccidentWasBowel = false;
                p.Stress = Mathf.Max(p.Stress, 0.72f);
            }
            if (p.BowelFill >= 0.999f)
            {
                RelieveBowel(character, false);
                unchecked { p.EliminationAccidentRevision++; }
                p.LastEliminationAccidentWasBowel = true;
                p.Stress = Mathf.Max(p.Stress, 0.86f);
            }

            p.AcuteStamina = ClampFinite01(p.AcuteStamina);
            p.Hydration = ClampFinite01(p.Hydration);
            p.ElectrolyteBalance = ClampFinite01(p.ElectrolyteBalance);
            p.StomachFullness = ClampFinite01(p.StomachFullness);
            p.EnergyReserve = ClampFinite01(p.EnergyReserve);
            p.ProteinReserve = ClampFinite01(p.ProteinReserve);
            p.FatReserve = ClampFinite01(p.FatReserve);
            p.MicronutrientReserve = ClampFinite01(p.MicronutrientReserve);
            p.MineralReserve = ClampFinite01(p.MineralReserve);
            p.DigestingEnergy = ClampFiniteNonnegative(p.DigestingEnergy);
            p.DigestingProtein = ClampFiniteNonnegative(p.DigestingProtein);
            p.DigestingFat = ClampFiniteNonnegative(p.DigestingFat);
            p.DigestingMicronutrients = ClampFiniteNonnegative(
                p.DigestingMicronutrients);
            p.DigestingMinerals = ClampFiniteNonnegative(p.DigestingMinerals);
            p.DigestingBiologicalContamination = ClampFiniteNonnegative(
                p.DigestingBiologicalContamination);
            p.DigestingToxins = ClampFiniteNonnegative(p.DigestingToxins);
            p.DentalHealth = ClampFinite01(p.DentalHealth);
            p.DentalInfection = ClampFinite01(p.DentalInfection);
            p.SleepDebt = ClampFinite01(p.SleepDebt);
            p.CircadianFatigue = ClampFinite01(p.CircadianFatigue);
            p.BladderFill = ClampFinite01(p.BladderFill);
            p.BowelFill = ClampFinite01(p.BowelFill);
            p.BloodVolume = ClampFinite01(p.BloodVolume);
            p.Oxygenation = ClampFinite01(p.Oxygenation);
            p.SystemicInfection = ClampFinite01(p.SystemicInfection);
            p.ToxinLoad = ClampFinite01(p.ToxinLoad);
            p.SmokeIrritation = ClampFinite01(p.SmokeIrritation);
            p.Consciousness = ClampFinite01(p.Consciousness);
            character.Conditions.FoodborneInfection = ClampFinite01(
                character.Conditions.FoodborneInfection);
            character.Conditions.WaterborneInfection = ClampFinite01(
                character.Conditions.WaterborneInfection);
            character.Conditions.ParasiteLoad = ClampFinite01(
                character.Conditions.ParasiteLoad);
            character.Conditions.RespiratoryInfection = ClampFinite01(
                character.Conditions.RespiratoryInfection);

            CharacterProgression.SimulateAttributeAdaptation(
                character.Progression,
                metabolicSeconds,
                Mathf.Min(p.EnergyReserve, Mathf.Min(p.ProteinReserve, p.FatReserve)),
                1f - Mathf.Max(p.SystemicInfection, p.ToxinLoad),
                sleeping);
            UpdateLifeState(character, seconds);
        }

        private static void SimulateDigestion(
            CharacterSurvivalState character,
            float metabolicSeconds,
            TraitModifiers traits)
        {
            if (metabolicSeconds <= 0f) return;
            var p = character.Physiology;
            var gutFunction = Mathf.Max(0.08f, character.Anatomy.GutFunction);
            var fraction = 1f - Mathf.Exp(
                -metabolicSeconds * gutFunction * traits.Digestion / 900f);
            fraction = Mathf.Clamp01(fraction);

            Absorb(ref p.DigestingEnergy, ref p.EnergyReserve, fraction);
            Absorb(ref p.DigestingProtein, ref p.ProteinReserve, fraction);
            Absorb(ref p.DigestingFat, ref p.FatReserve, fraction);
            Absorb(ref p.DigestingMicronutrients,
                ref p.MicronutrientReserve, fraction);
            Absorb(ref p.DigestingMinerals, ref p.MineralReserve, fraction);

            var biologicalExposure = p.DigestingBiologicalContamination * fraction;
            p.DigestingBiologicalContamination = Mathf.Max(
                0f, p.DigestingBiologicalContamination - biologicalExposure);
            character.Conditions.FoodborneInfection = Mathf.Clamp01(
                character.Conditions.FoodborneInfection + biologicalExposure * 0.18f);
            character.Conditions.ParasiteLoad = Mathf.Clamp01(
                character.Conditions.ParasiteLoad + biologicalExposure * 0.04f);
            p.SystemicInfection = Mathf.Clamp01(
                p.SystemicInfection + biologicalExposure * 0.008f);

            var toxinExposure = p.DigestingToxins * fraction;
            p.DigestingToxins = Mathf.Max(0f, p.DigestingToxins - toxinExposure);
            p.ToxinLoad = Mathf.Clamp01(p.ToxinLoad + toxinExposure * 0.08f);
        }

        private static void Absorb(ref float digestivePool, ref float reserve, float fraction)
        {
            var absorbed = Mathf.Max(0f, digestivePool) * Mathf.Clamp01(fraction);
            digestivePool = Mathf.Max(0f, digestivePool - absorbed);
            reserve = Mathf.Clamp01(reserve + absorbed);
        }

        private static void SimulateTemperature(
            PhysiologyState p,
            float seconds,
            SurvivalEnvironment environment,
            float exertion,
            TraitModifiers traits)
        {
            var rainExposure = environment.Sheltered ? 0f : environment.Precipitation;
            p.Wetness = Mathf.Clamp01(
                p.Wetness + rainExposure * seconds / 180f
                - (1f - environment.Humidity) * (environment.ExternalHeat + 0.1f) * seconds / 900f);
            var windChill = environment.WindMetersPerSecond * (1f - environment.Insulation) * 0.45f;
            var wetChill = p.Wetness * (1f - environment.Insulation) * 8f;
            var effectiveAmbient = environment.AmbientTemperatureC
                - windChill - wetChill + environment.ExternalHeat * 18f;
            var producedHeat = exertion * 2.2f;
            var targetCore = 37f + producedHeat
                + (effectiveAmbient - 22f) * 0.055f / traits.Thermoregulation;
            if (targetCore < 37f)
            {
                targetCore = 37f - (37f - targetCore) / traits.ColdTolerance;
            }
            else
            {
                targetCore = 37f + (targetCore - 37f) / traits.HeatTolerance;
            }
            var dryExchangeRate = 0.0012f
                * Mathf.Lerp(1f, 0.25f, environment.Insulation);
            var wetExchangeRate = p.Wetness * 0.0018f
                * (1f - environment.Insulation * 0.75f);
            p.CoreTemperatureC = Mathf.MoveTowards(
                p.CoreTemperatureC,
                targetCore,
                seconds * (dryExchangeRate + wetExchangeRate));
        }

        private static void SimulateWounds(
            CharacterSurvivalState character,
            float metabolicSeconds,
            float externalSeconds,
            float exertion,
            TraitModifiers traits)
        {
            var p = character.Physiology;
            foreach (var wound in character.Anatomy.Wounds)
            {
                if (wound.Healed) continue;

                var bleedingControl = (wound.PressureApplied ? 0.35f : 1f)
                    * (wound.Bandaged ? 0.55f : 1f)
                    * (wound.Sutured ? 0.15f : 1f);
                var activeBleeding = wound.Bleeding * bleedingControl;
                p.BloodVolume -= activeBleeding * externalSeconds / 1800f;
                wound.Bleeding = Mathf.Max(
                    0f,
                    wound.Bleeding - externalSeconds / 7200f * traits.Coagulation);

                // Surface pressure, sutures and bandages control only blood that
                // leaves through an open wound. Internal bleeding is hidden,
                // accelerates with movement and must clot on its own; rest is the
                // only broadly plausible treatment available in this setting.
                var internalRate = wound.InternalBleedingSeverity
                    * Mathf.Lerp(0.72f, 1.7f, exertion);
                p.BloodVolume -= internalRate * externalSeconds / 1800f;
                var internalClotting = externalSeconds / 10800f * traits.Coagulation
                    * Mathf.Lerp(1.35f, 0.32f, exertion);
                wound.InternalBleedingSeverity = Mathf.Max(
                    0f, wound.InternalBleedingSeverity - internalClotting);
                if (wound.InternalBleedingSeverity <= 0.005f)
                {
                    wound.InternalBleedingSeverity = 0f;
                    wound.InternalBleeding = false;
                }

                var hygieneRisk = (1f - p.BodyCleanliness) * 0.2f
                    + (wound.Bandaged ? 0.02f : 0.1f);
                var infectionGrowth = wound.Contamination * hygieneRisk
                    * metabolicSeconds / 7200f / traits.Immunity;
                wound.Infection = Mathf.Clamp01(wound.Infection + infectionGrowth);
                p.SystemicInfection += Mathf.Max(0f, wound.Infection - 0.55f)
                    * metabolicSeconds / 18000f;

                var nutrition = Mathf.Min(p.Hydration,
                    Mathf.Min(p.EnergyReserve, Mathf.Min(p.ProteinReserve,
                        Mathf.Min(p.FatReserve,
                            Mathf.Min(p.MicronutrientReserve, p.MineralReserve)))));
                var care = (wound.Washed ? 1.1f : 0.65f)
                    * (wound.Bandaged || !wound.IsOpen ? 1.1f : 0.8f)
                    * (wound.IsFracture && !wound.Splinted ? 0.18f : 1f);
                var recovery = metabolicSeconds / (RealSecondsPerGameDay * 10f)
                    * traits.Healing * nutrition * care
                    * (1f - wound.Infection);
                wound.TissueDamage = Mathf.Max(0f, wound.TissueDamage - recovery);
                wound.Pain = Mathf.Max(0f, wound.Pain - recovery * 0.7f);
                if (wound.TissueDamage <= 0.005f && wound.Infection <= 0.05f
                    && wound.Bleeding <= 0.01f
                    && wound.InternalBleedingSeverity <= 0.005f)
                {
                    wound.Healed = true;
                    if (wound.Severity > 0.7f)
                    {
                        wound.PermanentImpairment = Mathf.Max(
                            wound.PermanentImpairment,
                            (wound.Severity - 0.7f) * (wound.IsFracture ? 0.8f : 0.35f));
                    }
                }
            }
        }

        private static void SimulateDiseaseAndOrgans(
            CharacterSurvivalState character,
            float seconds,
            TraitModifiers traits)
        {
            var p = character.Physiology;
            var a = character.Anatomy;
            var conditions = character.Conditions;
            var dietaryReserve = Mathf.Min(p.ProteinReserve,
                Mathf.Min(p.FatReserve,
                    Mathf.Min(p.MicronutrientReserve, p.MineralReserve)));
            var immuneTarget = Mathf.Clamp01(0.12f + dietaryReserve * 0.95f
                - p.SleepDebt * 0.16f - p.SystemicInfection * 0.25f);
            p.ImmuneReserve = Mathf.MoveTowards(
                p.ImmuneReserve, immuneTarget, seconds / (RealSecondsPerGameDay * 6f));
            var immuneRecovery = seconds / 50000f * p.ImmuneReserve * traits.Immunity;
            p.SystemicInfection = Mathf.Max(0f, p.SystemicInfection - immuneRecovery);
            p.ToxinLoad = Mathf.Max(
                0f,
                p.ToxinLoad - seconds / 65000f * a.LiverFunction * a.KidneyFunction);

            var dentalPredisposition = character.Traits.Contains(TraitId.RottenTeeth)
                ? 4f : 1f;
            var dentalHygiene = Mathf.Lerp(2.5f, 0.65f,
                Mathf.Min(p.HandCleanliness, p.BodyCleanliness));
            p.DentalHealth = Mathf.Max(0f, p.DentalHealth
                - seconds / (RealSecondsPerGameDay * 180f)
                    * dentalPredisposition * dentalHygiene);
            if (p.DentalHealth < 0.5f)
            {
                p.DentalInfection = Mathf.Clamp01(p.DentalInfection
                    + (0.5f - p.DentalHealth) * seconds
                        / (RealSecondsPerGameDay * 5f) / traits.Immunity);
            }
            else
            {
                p.DentalInfection = Mathf.Max(0f,
                    p.DentalInfection - seconds / (RealSecondsPerGameDay * 25f)
                        * traits.Immunity);
            }
            p.SystemicInfection = Mathf.Clamp01(p.SystemicInfection
                + Mathf.Max(0f, p.DentalInfection - 0.35f)
                    * seconds / (RealSecondsPerGameDay * 8f));

            var immuneEffect = Mathf.Max(0.15f, p.ImmuneReserve * traits.Immunity);
            conditions.FoodborneInfection = Mathf.Max(0f,
                conditions.FoodborneInfection
                - seconds / (RealSecondsPerGameDay * 8f) * immuneEffect);
            conditions.WaterborneInfection = Mathf.Max(0f,
                conditions.WaterborneInfection
                - seconds / (RealSecondsPerGameDay * 7f) * immuneEffect);
            conditions.RespiratoryInfection = Mathf.Max(0f,
                conditions.RespiratoryInfection
                - seconds / (RealSecondsPerGameDay * 12f) * immuneEffect);
            conditions.ParasiteLoad = Mathf.Max(0f,
                conditions.ParasiteLoad
                - seconds / (RealSecondsPerGameDay * 80f) * immuneEffect);

            var gastrointestinal = conditions.GastrointestinalInfection;
            if (gastrointestinal > 0f)
            {
                p.Hydration -= gastrointestinal * seconds
                    / (RealSecondsPerGameDay * 3.5f);
                p.ElectrolyteBalance -= gastrointestinal * seconds
                    / (RealSecondsPerGameDay * 2.6f);
                p.BowelFill += gastrointestinal * seconds
                    / (RealSecondsPerGameDay * 0.65f);
                if (gastrointestinal > 0.68f)
                {
                    a.GutFunction = Mathf.Max(0f, a.GutFunction
                        - (gastrointestinal - 0.68f) * seconds
                            / (RealSecondsPerGameDay * 18f));
                }
            }
            if (conditions.ParasiteLoad > 0f)
            {
                p.EnergyReserve -= conditions.ParasiteLoad * seconds
                    / (RealSecondsPerGameDay * 16f);
                p.ProteinReserve -= conditions.ParasiteLoad * seconds
                    / (RealSecondsPerGameDay * 22f);
                p.FatReserve -= conditions.ParasiteLoad * seconds
                    / (RealSecondsPerGameDay * 30f);
                p.MicronutrientReserve -= conditions.ParasiteLoad * seconds
                    / (RealSecondsPerGameDay * 12f);
                p.MineralReserve -= conditions.ParasiteLoad * seconds
                    / (RealSecondsPerGameDay * 15f);
            }
            if (conditions.AntiparasiticCourseSeconds > 0f)
            {
                var treatedSeconds = Mathf.Min(
                    seconds, conditions.AntiparasiticCourseSeconds);
                conditions.ParasiteLoad = Mathf.Max(0f,
                    conditions.ParasiteLoad
                    - conditions.AntiparasiticCourseStrength * treatedSeconds
                        / (RealSecondsPerGameDay * 2f));
                conditions.AntiparasiticCourseSeconds = Mathf.Max(
                    0f, conditions.AntiparasiticCourseSeconds - treatedSeconds);
                if (conditions.AntiparasiticCourseSeconds <= 0f)
                    conditions.AntiparasiticCourseStrength = 0f;
            }
            if (conditions.RespiratoryInfection > 0.72f)
            {
                var respiratoryDamage = (conditions.RespiratoryInfection - 0.72f)
                    * seconds / (RealSecondsPerGameDay * 24f);
                a.LeftLungFunction = Mathf.Max(0f, a.LeftLungFunction - respiratoryDamage);
                a.RightLungFunction = Mathf.Max(0f, a.RightLungFunction - respiratoryDamage);
            }
            var inflammatoryLoad = Mathf.Max(gastrointestinal,
                Mathf.Max(conditions.RespiratoryInfection, p.SystemicInfection));
            if (inflammatoryLoad > 0.2f)
            {
                p.CoreTemperatureC = Mathf.Min(41.5f,
                    p.CoreTemperatureC
                    + (inflammatoryLoad - 0.2f) * seconds
                        / (RealSecondsPerGameDay * 1.4f));
            }
            p.SystemicInfection = Mathf.Clamp01(p.SystemicInfection
                + Mathf.Max(0f, gastrointestinal - 0.58f)
                    * seconds / (RealSecondsPerGameDay * 6f)
                + Mathf.Max(0f, conditions.RespiratoryInfection - 0.62f)
                    * seconds / (RealSecondsPerGameDay * 8f));

            var dehydration = Mathf.Max(0f, 0.18f - p.Hydration) / 0.18f;
            var electrolyteCrisis = Mathf.Max(
                0f, 0.12f - p.ElectrolyteBalance) / 0.12f;
            var macroReserve = Mathf.Min(p.EnergyReserve,
                Mathf.Min(p.ProteinReserve, p.FatReserve));
            var starvation = Mathf.Max(0f, 0.05f - macroReserve) / 0.05f;
            var vitaminCrisis = Mathf.Max(
                0f, 0.025f - p.MicronutrientReserve) / 0.025f;
            var mineralCrisis = Mathf.Max(
                0f, 0.025f - p.MineralReserve) / 0.025f;
            var sepsis = Mathf.Max(0f, p.SystemicInfection - 0.65f) / 0.35f;
            var toxins = Mathf.Max(0f, p.ToxinLoad - 0.72f) / 0.28f;
            var thermal = Mathf.Max(
                Mathf.InverseLerp(34f, 30f, p.CoreTemperatureC),
                Mathf.InverseLerp(41f, 43f, p.CoreTemperatureC));
            var organStress = Mathf.Max(
                Mathf.Max(dehydration, electrolyteCrisis),
                Mathf.Max(starvation, Mathf.Max(vitaminCrisis,
                    Mathf.Max(mineralCrisis,
                        Mathf.Max(sepsis, Mathf.Max(toxins, thermal))))));
            if (organStress > 0f)
            {
                var damage = organStress * seconds / 900f;
                a.KidneyFunction = Mathf.Max(0f, a.KidneyFunction - damage * (dehydration + toxins));
                a.LiverFunction = Mathf.Max(0f, a.LiverFunction - damage * (starvation + toxins + sepsis));
                a.HeartFunction = Mathf.Max(0f, a.HeartFunction
                    - damage * (thermal + sepsis + electrolyteCrisis
                        + mineralCrisis * 0.7f));
                a.BrainFunction = Mathf.Max(0f, a.BrainFunction
                    - damage * (thermal * 0.8f + vitaminCrisis * 0.25f));
            }

            var organFloor = Mathf.Min(a.BrainFunction, Mathf.Min(a.HeartFunction, Mathf.Min(a.LungFunction, Mathf.Min(a.LiverFunction, a.KidneyFunction))));
            var perfusion = p.BloodVolume * p.Oxygenation * a.HeartFunction;
            var consciousnessTarget = Mathf.Clamp01(
                Mathf.Min(perfusion * 1.7f, organFloor * 1.35f)
                * (1f - p.ToxinLoad * 0.55f)
                * (1f - Mathf.Max(p.SleepDebt, p.CircadianFatigue * 0.45f) * 0.25f));
            p.Consciousness = Mathf.MoveTowards(p.Consciousness, consciousnessTarget, seconds * 0.18f);
        }

        public static float CalculateCircadianSleepPressure(float dayFraction)
        {
            var phase = Mathf.Repeat(dayFraction - 0.125f, 1f);
            var nightly = (0.5f + 0.5f * Mathf.Cos(phase * Mathf.PI * 2f)) * 0.9f;
            var hour = Mathf.Repeat(dayFraction, 1f) * 24f;
            var afternoonDip = Mathf.Clamp01(1f - Mathf.Abs(hour - 15f) / 2f) * 0.16f;
            return Mathf.Clamp01(Mathf.Max(nightly, afternoonDip));
        }

        private static void ApplySupportiveCare(
            CharacterSurvivalState character,
            float seconds)
        {
            var conditions = character.Conditions;
            var p = character.Physiology;
            if (conditions.WarmingCareSeconds > 0f)
            {
                p.CoreTemperatureC = Mathf.MoveTowards(
                    p.CoreTemperatureC, 37f, seconds / 480f);
                p.Wetness = Mathf.Max(0f, p.Wetness - seconds / 1200f);
                conditions.WarmingCareSeconds = Mathf.Max(
                    0f, conditions.WarmingCareSeconds - seconds);
            }
            if (conditions.CoolingCareSeconds > 0f)
            {
                p.CoreTemperatureC = Mathf.MoveTowards(
                    p.CoreTemperatureC, 37.2f, seconds / 540f);
                conditions.CoolingCareSeconds = Mathf.Max(
                    0f, conditions.CoolingCareSeconds - seconds);
            }
            if (conditions.HerbalAnalgesiaSeconds > 0f)
            {
                conditions.HerbalAnalgesiaSeconds = Mathf.Max(
                    0f, conditions.HerbalAnalgesiaSeconds - seconds);
                if (conditions.HerbalAnalgesiaSeconds <= 0f)
                    conditions.HerbalAnalgesiaStrength = 0f;
            }
        }

        private static void SimulateRehabilitation(
            CharacterSurvivalState character,
            float seconds,
            float exertion)
        {
            if (seconds <= 0f || exertion <= 0.15f || character.Sleeping) return;
            var nutrition = Mathf.Min(
                character.Physiology.Hydration,
                Mathf.Min(character.Physiology.EnergyReserve,
                    Mathf.Min(character.Physiology.ProteinReserve,
                        Mathf.Min(character.Physiology.FatReserve,
                            Mathf.Min(character.Physiology.MicronutrientReserve,
                                character.Physiology.MineralReserve)))));
            var health = 1f - Mathf.Max(
                character.Physiology.SystemicInfection,
                character.Physiology.ToxinLoad);
            var rehabilitation = seconds / (40f * 3600f)
                * Mathf.InverseLerp(0.15f, 0.75f, exertion)
                * nutrition * health;
            if (rehabilitation <= 0f) return;
            foreach (var wound in character.Anatomy.Wounds)
            {
                if (wound == null || !wound.Healed || wound.PermanentImpairment <= 0f)
                    continue;
                // Severe structural damage leaves a small irreducible deficit. The
                // rest needs many real hours of safe movement to rehabilitate.
                var residual = Mathf.Max(0f, wound.Severity - 0.7f)
                    * (wound.IsFracture ? 0.18f : 0.08f);
                wound.PermanentImpairment = Mathf.Max(
                    residual, wound.PermanentImpairment - rehabilitation);
            }
        }

        private static void UpdateLifeState(CharacterSurvivalState character, float seconds)
        {
            var p = character.Physiology;
            var a = character.Anatomy;
            var cause = ResolveCriticalCause(character);
            if (cause != DeathCause.None)
            {
                if (p.CriticalCause == DeathCause.None) p.CriticalCause = cause;
                p.StableFailureSeconds += seconds;
                ApplyCriticalOrganDeterioration(character, p.CriticalCause, seconds);
                var irreversible = a.BrainFunction <= 0.02f
                    || a.HeartFunction <= 0.02f
                    || (p.BloodVolume <= 0.03f && p.Oxygenation <= 0.1f);
                if (irreversible)
                {
                    p.LifeState = CharacterLifeState.Dead;
                    p.DeathCause = p.CriticalCause;
                    p.Consciousness = 0f;
                    return;
                }

                p.LifeState = CharacterLifeState.Agonal;
                return;
            }

            p.StableFailureSeconds = Mathf.Max(0f, p.StableFailureSeconds - seconds * 2f);
            p.CriticalCause = DeathCause.None;
            p.LifeState = p.Consciousness switch
            {
                < 0.16f => CharacterLifeState.Unconscious,
                < 0.48f => CharacterLifeState.Confused,
                _ => CharacterLifeState.Conscious,
            };
        }

        private static void ApplyCriticalOrganDeterioration(
            CharacterSurvivalState character, DeathCause cause, float seconds)
        {
            var p = character.Physiology;
            var a = character.Anatomy;
            var brainDamage = 0f;
            var heartDamage = 0f;
            // Compressed game-balance rates, not real-world medical timings.
            // Agony ends only when the affected tissues become irreversibly
            // dysfunctional. Elapsed failure time is diagnostic, never a death timer.
            switch (cause)
            {
                case DeathCause.BloodLoss:
                    brainDamage = Mathf.Lerp(0.02f, 0.065f, Mathf.InverseLerp(0.09f, 0f, p.BloodVolume));
                    heartDamage = brainDamage * 0.6f;
                    break;
                case DeathCause.RespiratoryFailure:
                    brainDamage = Mathf.Lerp(0.025f, 0.055f, Mathf.InverseLerp(0.08f, 0f, p.Oxygenation));
                    heartDamage = brainDamage * 0.25f;
                    break;
                case DeathCause.CardiacFailure:
                    brainDamage = 0.05f * (1f - a.HeartFunction);
                    heartDamage = 0.004f;
                    break;
                case DeathCause.BrainFailure:
                    brainDamage = 0.002f + (0.08f - a.BrainFunction) * 0.04f;
                    break;
                case DeathCause.Hypothermia:
                    heartDamage = Mathf.Lerp(0.002f, 0.018f, Mathf.InverseLerp(29.5f, 22f, p.CoreTemperatureC));
                    brainDamage = heartDamage * 0.35f;
                    break;
                case DeathCause.Hyperthermia:
                    brainDamage = Mathf.Lerp(0.004f, 0.03f, Mathf.InverseLerp(43f, 46f, p.CoreTemperatureC));
                    heartDamage = brainDamage * 0.7f;
                    break;
                case DeathCause.Dehydration:
                    heartDamage = 0.003f * (1f - a.KidneyFunction);
                    brainDamage = heartDamage * 0.6f;
                    break;
                case DeathCause.Starvation:
                    heartDamage = 0.001f * (1f - a.LiverFunction);
                    break;
                case DeathCause.Sepsis:
                    heartDamage = 0.008f * p.SystemicInfection;
                    brainDamage = heartDamage * 0.5f;
                    break;
                case DeathCause.Poisoning:
                    brainDamage = 0.009f * p.ToxinLoad;
                    heartDamage = brainDamage * 0.7f;
                    break;
                case DeathCause.MultipleOrganFailure:
                    heartDamage = 0.006f;
                    brainDamage = 0.004f;
                    break;
                case DeathCause.VoluntaryPassing:
                    // Imaginary setting rule; intentionally unrelated to any
                    // real-world method or timing.
                    heartDamage = 0.006f;
                    brainDamage = 0.025f;
                    break;
            }
            a.BrainFunction = Mathf.Max(0f, a.BrainFunction - brainDamage * seconds);
            a.HeartFunction = Mathf.Max(0f, a.HeartFunction - heartDamage * seconds);
        }

        private static DeathCause ResolveCriticalCause(CharacterSurvivalState character)
        {
            var p = character.Physiology;
            var a = character.Anatomy;
            if (p.CriticalCause == DeathCause.VoluntaryPassing)
                return DeathCause.VoluntaryPassing;
            if (p.BloodVolume <= 0.09f) return DeathCause.BloodLoss;
            if (a.HeartFunction <= 0.08f) return DeathCause.CardiacFailure;
            if (a.BrainFunction <= 0.08f) return DeathCause.BrainFailure;
            if (a.LungFunction <= 0.08f || p.Oxygenation <= 0.08f) return DeathCause.RespiratoryFailure;
            if (p.CoreTemperatureC <= 29.5f) return DeathCause.Hypothermia;
            if (p.CoreTemperatureC >= 43f) return DeathCause.Hyperthermia;
            if (p.Hydration <= 0.001f && a.KidneyFunction <= 0.18f) return DeathCause.Dehydration;
            if (Mathf.Min(p.EnergyReserve, Mathf.Min(p.ProteinReserve, p.FatReserve))
                    <= 0.001f && a.LiverFunction <= 0.18f)
                return DeathCause.Starvation;
            if (p.SystemicInfection >= 0.99f && a.HeartFunction <= 0.22f) return DeathCause.Sepsis;
            if (p.ToxinLoad >= 0.99f && a.LiverFunction <= 0.22f) return DeathCause.Poisoning;
            var failedOrgans = 0;
            if (a.HeartFunction < 0.2f) failedOrgans++;
            if (a.LungFunction < 0.2f) failedOrgans++;
            if (a.LiverFunction < 0.2f) failedOrgans++;
            if (a.KidneyFunction < 0.2f) failedOrgans++;
            if (failedOrgans >= 2) return DeathCause.MultipleOrganFailure;
            return DeathCause.None;
        }

        private static InjuryType ResolveInjuryType(
            DamageKind kind,
            float severity,
            float fractureResistance)
        {
            return kind switch
            {
                DamageKind.Edged => severity > 0.18f ? InjuryType.Laceration : InjuryType.Abrasion,
                DamageKind.Puncture => InjuryType.Puncture,
                DamageKind.Heat => InjuryType.Burn,
                DamageKind.Fall when severity / fractureResistance > 0.7f => InjuryType.ClosedFracture,
                DamageKind.Blunt when severity / fractureResistance > 0.82f => InjuryType.ClosedFracture,
                DamageKind.Blunt => InjuryType.Contusion,
                _ => severity > 0.45f ? InjuryType.Sprain : InjuryType.Contusion,
            };
        }

        private static void ApplyOrganTrauma(
            AnatomyState anatomy,
            BodyRegion region,
            DamageKind kind,
            float severity)
        {
            var penetration = kind == DamageKind.Puncture || kind == DamageKind.Edged ? 1f : 0.45f;
            var damage = severity * penetration * 0.35f;
            switch (region)
            {
                case BodyRegion.Head:
                    anatomy.BrainFunction = Mathf.Max(0f, anatomy.BrainFunction - damage);
                    break;
                case BodyRegion.Neck:
                    anatomy.BrainFunction = Mathf.Max(0f, anatomy.BrainFunction - damage * 0.25f);
                    anatomy.LeftLungFunction = Mathf.Max(0f, anatomy.LeftLungFunction - damage * 0.12f);
                    anatomy.RightLungFunction = Mathf.Max(0f, anatomy.RightLungFunction - damage * 0.12f);
                    break;
                case BodyRegion.Chest:
                    anatomy.HeartFunction = Mathf.Max(0f, anatomy.HeartFunction - damage * 0.75f);
                    anatomy.LeftLungFunction = Mathf.Max(0f, anatomy.LeftLungFunction - damage);
                    anatomy.RightLungFunction = Mathf.Max(0f, anatomy.RightLungFunction - damage);
                    break;
                case BodyRegion.Abdomen:
                    anatomy.LiverFunction = Mathf.Max(0f, anatomy.LiverFunction - damage * 0.65f);
                    anatomy.KidneyFunction = Mathf.Max(0f, anatomy.KidneyFunction - damage * 0.45f);
                    anatomy.GutFunction = Mathf.Max(0f, anatomy.GutFunction - damage * 0.8f);
                    break;
            }
        }

        private static float RegionFunction(
            AnatomyState anatomy,
            bool average,
            params BodyRegion[] regions)
        {
            var total = 0f;
            foreach (var region in regions)
            {
                var function = 1f;
                foreach (var wound in anatomy.Wounds)
                {
                    if (wound.Region != region) continue;
                    if (wound.Healed)
                    {
                        function -= wound.PermanentImpairment;
                        continue;
                    }
                    var immobilizedFracture = wound.IsFracture && !wound.Splinted ? 0.75f : 0f;
                    function -= wound.TissueDamage * 0.55f
                        + wound.PermanentImpairment
                        + immobilizedFracture;
                }
                total += Mathf.Clamp01(function);
            }
            return average ? total / Mathf.Max(1, regions.Length) : total / Mathf.Max(1, regions.Length);
        }

        private static bool HasActiveBleeding(AnatomyState anatomy)
        {
            foreach (var wound in anatomy.Wounds)
            {
                if (!wound.Healed && wound.Bleeding > 0.01f) return true;
            }
            return false;
        }

        private static string DescribeWound(WoundState wound, float skill)
        {
            if (skill < 0.2f) return "Видна травма; её глубина и опасность неясны.";
            var infection = wound.Infection > 0.3f ? " Есть признаки заражения." : string.Empty;
            var bleeding = wound.Bleeding > 0.3f ? " Кровотечение сильное." : wound.Bleeding > 0.03f ? " Рана кровит." : string.Empty;
            var internalBleeding = skill >= 0.5f && wound.InternalBleedingSeverity > 0.02f
                ? " Болезненность и общие признаки позволяют подозревать внутреннее кровотечение; повязка его не остановит."
                : string.Empty;
            return $"{wound.Type}, область: {wound.Region}.{bleeding}{internalBleeding}{infection}";
        }

        private static float ClampFinite01(float value)
            => float.IsNaN(value) || float.IsInfinity(value) ? 0f : Mathf.Clamp01(value);

        private static float ClampFiniteNonnegative(float value)
            => float.IsNaN(value) || float.IsInfinity(value)
                ? 0f
                : Mathf.Clamp(value, 0f, 4f);

    }
}
