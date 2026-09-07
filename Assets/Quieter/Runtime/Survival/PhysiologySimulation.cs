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
        public const float RealSecondsPerGameDay = 7200f;
        public const float MaximumStepSeconds = 1f;

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
            character.Sleeping = false;
            character.Physiology.SleepCycleConsolidated = false;
            return CharacterProgression.ConsolidateSleep(character.Progression, quality);
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
            physiology.SystemicInfection = Mathf.Clamp01(
                physiology.SystemicInfection
                + Mathf.Clamp01(biologicalContamination) * amount * 0.035f / digestion);
            physiology.ToxinLoad = Mathf.Clamp01(
                physiology.ToxinLoad + Mathf.Clamp01(toxinContamination) * amount * 0.08f);
        }

        public static void ConsumeFood(
            CharacterSurvivalState character,
            float calories,
            float protein,
            float micronutrients,
            float biologicalContamination,
            float toxinContamination)
        {
            character.EnsureInitialized();
            var physiology = character.Physiology;
            var digestion = TraitCatalog.Resolve(character.Traits).Digestion;
            physiology.StomachFullness = Mathf.Clamp01(
                physiology.StomachFullness + Mathf.Max(0f, calories) / 2600f);
            physiology.EnergyReserve = Mathf.Clamp01(
                physiology.EnergyReserve + Mathf.Max(0f, calories) / 9000f);
            physiology.ProteinReserve = Mathf.Clamp01(
                physiology.ProteinReserve + Mathf.Max(0f, protein) / 450f);
            physiology.MicronutrientReserve = Mathf.Clamp01(
                physiology.MicronutrientReserve + Mathf.Max(0f, micronutrients) * 0.1f);
            physiology.BowelFill = Mathf.Clamp01(
                physiology.BowelFill + Mathf.Max(0f, calories) / 4500f);
            physiology.SystemicInfection = Mathf.Clamp01(
                physiology.SystemicInfection
                + Mathf.Clamp01(biologicalContamination) * 0.05f / digestion);
            physiology.ToxinLoad = Mathf.Clamp01(
                physiology.ToxinLoad + Mathf.Clamp01(toxinContamination) * 0.08f);
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

            var wound = new WoundState
            {
                WoundId = character.Anatomy.NextWoundId++,
                Region = region,
                Type = type,
                Severity = severity,
                TissueDamage = severity,
                Contamination = Mathf.Clamp01(contamination + (type == InjuryType.OpenFracture ? 0.25f : 0f)),
                Bleeding = bleeding,
                Pain = Mathf.Clamp01(severity * 0.9f),
                InternalBleeding = internalDamage
                    || (damageKind == DamageKind.Blunt && severity > 0.68f),
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

            var dirtyHands = 1f - character.Physiology.HandCleanliness;
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

        public static SymptomFlags ObserveSymptoms(CharacterSurvivalState character)
        {
            character.EnsureInitialized();
            var p = character.Physiology;
            var symptoms = SymptomFlags.None;
            if (p.Hydration < 0.78f) symptoms |= SymptomFlags.Thirst;
            if (p.Hydration < 0.55f) symptoms |= SymptomFlags.DryMouth;
            if (p.StomachFullness < 0.35f) symptoms |= SymptomFlags.Hunger;
            if (p.EnergyReserve < 0.4f || p.BloodVolume < 0.65f) symptoms |= SymptomFlags.Weakness;
            if (p.SleepDebt > 0.3f) symptoms |= SymptomFlags.Fatigue;
            if (p.SleepDebt > 0.75f) symptoms |= SymptomFlags.Microsleep;
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
            if (p.Stress > 0.75f) symptoms |= SymptomFlags.Panic;
            if (p.BladderFill > 0.72f) symptoms |= SymptomFlags.BladderPressure;
            if (p.BowelFill > 0.78f) symptoms |= SymptomFlags.BowelPressure;
            if (p.BodyCleanliness < 0.35f) symptoms |= SymptomFlags.Dirty;
            if (p.DentalHealth < 0.45f) symptoms |= SymptomFlags.Toothache;
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
            var energy = Mathf.Clamp01(Mathf.Min(p.Hydration * 1.3f, p.EnergyReserve * 1.4f));
            var consciousness = Mathf.Clamp01(p.Consciousness);
            var painPenalty = Mathf.Lerp(1f, 0.35f, p.Pain);
            var strength = character.Progression.Attributes[(int)CharacterAttributeId.Strength];
            var comfortableMass = 12f + strength * 0.35f;
            var loadRatio = p.CarriedMassKg / Mathf.Max(1f, comfortableMass);
            var loadPenalty = loadRatio <= 1f
                ? 1f
                : Mathf.Clamp01(1f - (loadRatio - 1f) * 0.72f);
            var movement = Mathf.Pow(legs * circulation * respiration * energy, 0.25f)
                * consciousness * painPenalty * loadPenalty;
            var fineMotor = RegionFunction(character.Anatomy, false, BodyRegion.LeftHand, BodyRegion.RightHand)
                * Mathf.Lerp(1f, 0.3f, p.Pain)
                * Mathf.Clamp01(p.Oxygenation * 1.2f);

            return new CharacterCapabilities(
                movement,
                Mathf.Sqrt(movement),
                movement * legs,
                Mathf.Clamp01(p.Hydration * respiration * (1f - p.SleepDebt * 0.6f)),
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
            var internalScale = offlineMetabolismSlowed ? 0.25f : 1f;
            var metabolicSeconds = seconds * internalScale;
            var sleeping = character.Sleeping;

            var metabolism = traits.Metabolism * (1f + exertion * 1.35f);
            p.Hydration -= metabolicSeconds / (9000f / metabolism);
            p.StomachFullness -= metabolicSeconds / (2700f / metabolism);
            p.EnergyReserve -= metabolicSeconds / (72000f / metabolism);
            p.ProteinReserve -= metabolicSeconds / (180000f / metabolism);
            p.MicronutrientReserve -= metabolicSeconds / 300000f;
            p.BladderFill += metabolicSeconds / 3000f * Mathf.Max(0.3f, p.Hydration);
            p.BowelFill += metabolicSeconds / 11000f * Mathf.Max(0.2f, p.StomachFullness);

            if (sleeping)
            {
                var sleepInterruption = Mathf.Clamp01(
                    p.Pain * 0.5f
                    + Mathf.Max(0f, 36f - p.CoreTemperatureC) * 0.3f
                    + Mathf.Max(0f, p.BladderFill - 0.8f)
                    + p.SystemicInfection * 0.4f);
                p.SleepDebt -= metabolicSeconds / (420f * traits.SleepNeed)
                    * (1f - sleepInterruption * 0.85f);
                p.AcuteStamina += metabolicSeconds / 30f;
            }
            else
            {
                p.SleepDebt += metabolicSeconds / (14400f / traits.SleepNeed)
                    * (0.7f + exertion * 0.3f);
                p.AcuteStamina += seconds / 42f * (1f - exertion * 1.5f);
                p.AcuteStamina -= seconds / 22f * exertion;
            }

            SimulateTemperature(p, seconds, environment, exertion, traits);
            SimulateWounds(character, metabolicSeconds, seconds, traits);
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
                * (1f - smoke * 0.96f));
            p.Oxygenation = Mathf.MoveTowards(p.Oxygenation, oxygenTarget, seconds * 0.12f);

            var chronicPain = character.Traits.Contains(TraitId.ChronicPain) ? 0.18f : 0f;
            var eliminationPressure = Mathf.Max(
                Mathf.InverseLerp(0.78f, 1f, p.BladderFill),
                Mathf.InverseLerp(0.82f, 1f, p.BowelFill));
            var woundPain = chronicPain + eliminationPressure * 0.24f;
            foreach (var wound in a.Wounds)
            {
                if (!wound.Healed) woundPain += wound.Pain * 0.35f;
            }
            p.Pain = Mathf.Clamp01(woundPain / traits.PainTolerance);
            var dangerStress = Mathf.Max(
                Mathf.Max(p.Pain, 1f - p.Oxygenation),
                eliminationPressure * 0.75f);
            p.Stress = Mathf.MoveTowards(p.Stress, dangerStress, seconds * 0.02f);

            if (p.BladderFill >= 0.999f)
            {
                RelieveBladder(character, false);
                p.Stress = Mathf.Max(p.Stress, 0.72f);
            }
            if (p.BowelFill >= 0.999f)
            {
                RelieveBowel(character, false);
                p.Stress = Mathf.Max(p.Stress, 0.86f);
            }

            p.AcuteStamina = ClampFinite01(p.AcuteStamina);
            p.Hydration = ClampFinite01(p.Hydration);
            p.ElectrolyteBalance = ClampFinite01(p.ElectrolyteBalance);
            p.StomachFullness = ClampFinite01(p.StomachFullness);
            p.EnergyReserve = ClampFinite01(p.EnergyReserve);
            p.ProteinReserve = ClampFinite01(p.ProteinReserve);
            p.MicronutrientReserve = ClampFinite01(p.MicronutrientReserve);
            p.SleepDebt = ClampFinite01(p.SleepDebt);
            p.BladderFill = ClampFinite01(p.BladderFill);
            p.BowelFill = ClampFinite01(p.BowelFill);
            p.BloodVolume = ClampFinite01(p.BloodVolume);
            p.Oxygenation = ClampFinite01(p.Oxygenation);
            p.SystemicInfection = ClampFinite01(p.SystemicInfection);
            p.ToxinLoad = ClampFinite01(p.ToxinLoad);
            p.SmokeIrritation = ClampFinite01(p.SmokeIrritation);
            p.Consciousness = ClampFinite01(p.Consciousness);

            CharacterProgression.SimulateAttributeAdaptation(
                character.Progression,
                metabolicSeconds,
                Mathf.Min(p.EnergyReserve, p.ProteinReserve),
                1f - Mathf.Max(p.SystemicInfection, p.ToxinLoad),
                sleeping);
            UpdateLifeState(character, seconds);
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

                var hygieneRisk = (1f - p.BodyCleanliness) * 0.2f
                    + (wound.Bandaged ? 0.02f : 0.1f);
                var infectionGrowth = wound.Contamination * hygieneRisk
                    * metabolicSeconds / 7200f / traits.Immunity;
                wound.Infection = Mathf.Clamp01(wound.Infection + infectionGrowth);
                p.SystemicInfection += Mathf.Max(0f, wound.Infection - 0.55f)
                    * metabolicSeconds / 18000f;

                var nutrition = Mathf.Min(p.EnergyReserve, Mathf.Min(p.ProteinReserve, p.Hydration));
                var care = (wound.Washed ? 1.1f : 0.65f)
                    * (wound.Bandaged || !wound.IsOpen ? 1.1f : 0.8f)
                    * (wound.IsFracture && !wound.Splinted ? 0.18f : 1f);
                var recovery = metabolicSeconds / (RealSecondsPerGameDay * 10f)
                    * traits.Healing * nutrition * care
                    * (1f - wound.Infection);
                wound.TissueDamage = Mathf.Max(0f, wound.TissueDamage - recovery);
                wound.Pain = Mathf.Max(0f, wound.Pain - recovery * 0.7f);
                if (wound.TissueDamage <= 0.005f && wound.Infection <= 0.05f)
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
            var immuneRecovery = seconds / 50000f * p.ImmuneReserve * traits.Immunity;
            p.SystemicInfection = Mathf.Max(0f, p.SystemicInfection - immuneRecovery);
            p.ToxinLoad = Mathf.Max(
                0f,
                p.ToxinLoad - seconds / 65000f * a.LiverFunction * a.KidneyFunction);

            var dehydration = Mathf.Max(0f, 0.18f - p.Hydration) / 0.18f;
            var starvation = Mathf.Max(0f, 0.05f - p.EnergyReserve) / 0.05f;
            var sepsis = Mathf.Max(0f, p.SystemicInfection - 0.65f) / 0.35f;
            var toxins = Mathf.Max(0f, p.ToxinLoad - 0.72f) / 0.28f;
            var thermal = Mathf.Max(
                Mathf.InverseLerp(34f, 30f, p.CoreTemperatureC),
                Mathf.InverseLerp(41f, 43f, p.CoreTemperatureC));
            var organStress = Mathf.Max(dehydration, Mathf.Max(starvation, Mathf.Max(sepsis, Mathf.Max(toxins, thermal))));
            if (organStress > 0f)
            {
                var damage = organStress * seconds / 900f;
                a.KidneyFunction = Mathf.Max(0f, a.KidneyFunction - damage * (dehydration + toxins));
                a.LiverFunction = Mathf.Max(0f, a.LiverFunction - damage * (starvation + toxins + sepsis));
                a.HeartFunction = Mathf.Max(0f, a.HeartFunction - damage * (thermal + sepsis));
                a.BrainFunction = Mathf.Max(0f, a.BrainFunction - damage * thermal * 0.8f);
            }

            var organFloor = Mathf.Min(a.BrainFunction, Mathf.Min(a.HeartFunction, Mathf.Min(a.LungFunction, Mathf.Min(a.LiverFunction, a.KidneyFunction))));
            var perfusion = p.BloodVolume * p.Oxygenation * a.HeartFunction;
            var consciousnessTarget = Mathf.Clamp01(
                Mathf.Min(perfusion * 1.7f, organFloor * 1.35f)
                * (1f - p.ToxinLoad * 0.55f)
                * (1f - p.SleepDebt * 0.25f));
            p.Consciousness = Mathf.MoveTowards(p.Consciousness, consciousnessTarget, seconds * 0.18f);
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
            }
            a.BrainFunction = Mathf.Max(0f, a.BrainFunction - brainDamage * seconds);
            a.HeartFunction = Mathf.Max(0f, a.HeartFunction - heartDamage * seconds);
        }

        private static DeathCause ResolveCriticalCause(CharacterSurvivalState character)
        {
            var p = character.Physiology;
            var a = character.Anatomy;
            if (p.BloodVolume <= 0.09f) return DeathCause.BloodLoss;
            if (a.HeartFunction <= 0.08f) return DeathCause.CardiacFailure;
            if (a.BrainFunction <= 0.08f) return DeathCause.BrainFailure;
            if (a.LungFunction <= 0.08f || p.Oxygenation <= 0.08f) return DeathCause.RespiratoryFailure;
            if (p.CoreTemperatureC <= 29.5f) return DeathCause.Hypothermia;
            if (p.CoreTemperatureC >= 43f) return DeathCause.Hyperthermia;
            if (p.Hydration <= 0.001f && a.KidneyFunction <= 0.18f) return DeathCause.Dehydration;
            if (p.EnergyReserve <= 0.001f && a.LiverFunction <= 0.18f) return DeathCause.Starvation;
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
                    if (wound.Region != region || wound.Healed) continue;
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
            return $"{wound.Type}, область: {wound.Region}.{bleeding}{infection}";
        }

        private static float ClampFinite01(float value)
            => float.IsNaN(value) || float.IsInfinity(value) ? 0f : Mathf.Clamp01(value);

    }
}
