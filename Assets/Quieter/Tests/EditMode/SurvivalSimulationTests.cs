using System.Collections.Generic;
using NUnit.Framework;
using Quieter.Survival;
using UnityEngine;

namespace Quieter.Tests.EditMode
{
    public sealed class SurvivalSimulationTests
    {
        [Test]
        public void TraitBudget_EnforcesLimitsCostsAndOpposites()
        {
            Assert.That(TraitCatalog.TryValidate(
                new[] { TraitId.FastLearner, TraitId.SlowHealing },
                out var remaining,
                out var error), Is.True, error);
            Assert.That(remaining, Is.EqualTo(2));

            Assert.That(TraitCatalog.TryValidate(
                new[] { TraitId.FastHealing, TraitId.SlowHealing },
                out _,
                out error), Is.False);
            StringAssert.Contains("несовместима", error);

            Assert.That(TraitCatalog.TryValidate(
                new[] { TraitId.FastLearner },
                out _,
                out error), Is.False);
            StringAssert.Contains("Не хватает", error);
        }

        [Test]
        public void Progression_RequiresSleepConsolidationForFinalShare()
        {
            var state = new CharacterProgressionState();
            state.EnsureInitialized();

            var credited = CharacterProgression.RegisterPractice(
                state,
                SkillId.Carpentry,
                40f * 3600f,
                1f,
                1f,
                0f,
                TraitModifiers.Default);

            Assert.That(credited, Is.EqualTo(40f).Within(0.001f));
            Assert.That(CharacterProgression.GetSkillLevel(state, SkillId.Carpentry), Is.LessThan(10));
            Assert.That(state.PendingConsolidationHours[(int)SkillId.Carpentry], Is.EqualTo(12f).Within(0.001f));

            CharacterProgression.ConsolidateSleep(state, 1f);
            Assert.That(CharacterProgression.GetSkillLevel(state, SkillId.Carpentry), Is.EqualTo(10));
        }

        [Test]
        public void BriefLogoutCannotConsolidatePendingPracticeAsAFullSleep()
        {
            var brief = NewCharacter();
            var full = NewCharacter();
            CharacterProgression.RegisterPractice(
                brief.Progression, SkillId.Carpentry, 3600f, 1f, 1f, 0f,
                TraitModifiers.Default);
            CharacterProgression.RegisterPractice(
                full.Progression, SkillId.Carpentry, 3600f, 1f, 1f, 0f,
                TraitModifiers.Default);
            brief.Sleeping = true;
            full.Sleeping = true;
            brief.Physiology.CurrentSleepSeconds = 30f;
            full.Physiology.CurrentSleepSeconds = 300f;

            var briefGain = PhysiologySimulation.EndSleep(brief, 1f);
            var fullGain = PhysiologySimulation.EndSleep(full, 1f);

            Assert.That(briefGain, Is.Zero);
            Assert.That(brief.Progression.PendingConsolidationHours[(int)SkillId.Carpentry],
                Is.EqualTo(0.3f).Within(0.0001f));
            Assert.That(fullGain, Is.EqualTo(0.3f).Within(0.0001f));
            Assert.That(full.Progression.PendingConsolidationHours[(int)SkillId.Carpentry],
                Is.Zero.Within(0.0001f));
        }

        [Test]
        public void SafeRepetition_StopsProducingMeaningfulExperience()
        {
            var state = new CharacterProgressionState();
            state.EnsureInitialized();
            var credited = CharacterProgression.RegisterPractice(
                state,
                SkillId.Firekeeping,
                3600f,
                0.1f,
                1f,
                1f,
                TraitModifiers.Default);

            Assert.That(credited, Is.LessThan(0.005f));
        }

        [Test]
        public void FastLearner_AdvancesEarlierButCannotMasterBeforeFortyRelevantHours()
        {
            var fast = new CharacterProgressionState();
            var ordinary = new CharacterProgressionState();
            var trait = TraitCatalog.Resolve(new[] { TraitId.FastLearner });
            CharacterProgression.RegisterPractice(fast, SkillId.Carpentry, 39f * 3600f,
                1f, 1f, 0f, trait);
            CharacterProgression.RegisterPractice(ordinary, SkillId.Carpentry, 39f * 3600f,
                1f, 1f, 0f, TraitModifiers.Default);
            CharacterProgression.ConsolidateSleep(fast, 1f);
            Assert.That(fast.SkillPracticeHours[(int)SkillId.Carpentry],
                Is.GreaterThan(ordinary.SkillPracticeHours[(int)SkillId.Carpentry]));
            Assert.That(CharacterProgression.GetSkillLevel(fast, SkillId.Carpentry), Is.EqualTo(9));
            CharacterProgression.RegisterPractice(fast, SkillId.Carpentry, 3600f, 1f, 1f, 0f, trait);
            CharacterProgression.ConsolidateSleep(fast, 1f);
            Assert.That(CharacterProgression.GetSkillLevel(fast, SkillId.Carpentry), Is.EqualTo(10));
        }

        [Test]
        public void Physiology_IsStableAcrossDifferentCallerStepSizes()
        {
            var singleCall = NewCharacter();
            var repeated = NewCharacter();

            PhysiologySimulation.Simulate(
                singleCall,
                600f,
                SurvivalEnvironment.Temperate,
                0.45f);
            for (var index = 0; index < 60; index++)
            {
                PhysiologySimulation.Simulate(
                    repeated,
                    10f,
                    SurvivalEnvironment.Temperate,
                    0.45f);
            }

            Assert.That(singleCall.Physiology.Hydration,
                Is.EqualTo(repeated.Physiology.Hydration).Within(0.0001f));
            Assert.That(singleCall.Physiology.CoreTemperatureC,
                Is.EqualTo(repeated.Physiology.CoreTemperatureC).Within(0.0001f));
            Assert.That(singleCall.Physiology.EnergyReserve,
                Is.EqualTo(repeated.Physiology.EnergyReserve).Within(0.0001f));
        }

        [Test]
        public void BloodLoss_ProducesPhysiologicalDeathCause()
        {
            var character = NewCharacter();
            PhysiologySimulation.AddInjury(
                character,
                BodyRegion.Neck,
                DamageKind.Edged,
                1f,
                0.4f);

            PhysiologySimulation.Simulate(
                character,
                3600f,
                SurvivalEnvironment.Temperate,
                0f);

            Assert.That(character.Physiology.LifeState, Is.EqualTo(CharacterLifeState.Dead));
            Assert.That(character.Physiology.DeathCause, Is.EqualTo(DeathCause.BloodLoss));
        }

        [Test]
        public void Treatment_RejectsClosingDirtyActivelyBleedingWound()
        {
            var character = NewCharacter();
            var wound = PhysiologySimulation.AddInjury(
                character,
                BodyRegion.LeftForearm,
                DamageKind.Edged,
                0.7f,
                0.8f);
            var context = new TreatmentContext(
                0.7f,
                0.9f,
                hasWater: true,
                hasDisinfectant: true,
                hasNeedleAndThread: true,
                hasBandage: true);

            var earlySuture = PhysiologySimulation.Treat(
                character,
                wound.WoundId,
                MedicalActionType.Suture,
                context);
            Assert.That(earlySuture.Success, Is.False);

            Assert.That(PhysiologySimulation.Treat(
                character, wound.WoundId, MedicalActionType.ApplyPressure, context).Success, Is.True);
            Assert.That(PhysiologySimulation.Treat(
                character, wound.WoundId, MedicalActionType.Wash, context).Success, Is.True);
            Assert.That(PhysiologySimulation.Treat(
                character, wound.WoundId, MedicalActionType.Disinfect, context).Success, Is.True);
            Assert.That(PhysiologySimulation.Treat(
                character, wound.WoundId, MedicalActionType.Suture, context).Success, Is.True);
        }

        [Test]
        public void OfflineSlowdown_DoesNotSlowExternalBleeding()
        {
            var online = NewCharacter();
            var offline = NewCharacter();
            PhysiologySimulation.AddInjury(
                online, BodyRegion.RightThigh, DamageKind.Edged, 0.55f);
            PhysiologySimulation.AddInjury(
                offline, BodyRegion.RightThigh, DamageKind.Edged, 0.55f);

            PhysiologySimulation.Simulate(
                online, 300f, SurvivalEnvironment.Temperate, 0f, false);
            PhysiologySimulation.Simulate(
                offline, 300f, SurvivalEnvironment.Temperate, 0f, true);

            Assert.That(offline.Physiology.BloodVolume,
                Is.EqualTo(online.Physiology.BloodVolume).Within(0.0001f));
            Assert.That(offline.Physiology.Hydration, Is.GreaterThan(online.Physiology.Hydration));
        }

        [Test]
        public void InternalBleeding_IsHiddenFromSurfacePressureAndWorsenedByExertion()
        {
            var resting = NewCharacter();
            var moving = NewCharacter();
            var restingWound = PhysiologySimulation.AddInjury(
                resting, BodyRegion.Abdomen, DamageKind.Blunt, 0.78f);
            var movingWound = PhysiologySimulation.AddInjury(
                moving, BodyRegion.Abdomen, DamageKind.Blunt, 0.78f);
            var context = new TreatmentContext(
                1f, 1f, hasWater: true, hasDisinfectant: true,
                hasNeedleAndThread: true, hasBandage: true);

            var pressure = PhysiologySimulation.Treat(
                resting, restingWound.WoundId, MedicalActionType.ApplyPressure, context);
            Assert.That(pressure.Success, Is.False,
                "A closed internal injury has no surface wound to compress.");
            Assert.That(restingWound.InternalBleedingSeverity, Is.GreaterThan(0f));

            PhysiologySimulation.Simulate(
                resting, 600f, SurvivalEnvironment.Temperate, 0f);
            PhysiologySimulation.Simulate(
                moving, 600f, SurvivalEnvironment.Temperate, 0.9f);

            Assert.That(moving.Physiology.BloodVolume,
                Is.LessThan(resting.Physiology.BloodVolume));
            Assert.That(movingWound.InternalBleedingSeverity,
                Is.GreaterThan(restingWound.InternalBleedingSeverity));
        }

        [Test]
        public void SevereInternalBleeding_CanCauseBloodLossDeathWithoutVisibleBleeding()
        {
            var character = NewCharacter();
            var wound = PhysiologySimulation.AddInjury(
                character, BodyRegion.Abdomen, DamageKind.Blunt, 1f);

            Assert.That(wound.Bleeding, Is.Zero,
                "A closed abdominal impact must not create visible blood loss.");
            Assert.That(wound.InternalBleedingSeverity, Is.GreaterThan(0f));

            PhysiologySimulation.Simulate(
                character, 3600f, SurvivalEnvironment.Temperate, 0.85f);

            Assert.That(character.Physiology.LifeState, Is.EqualTo(CharacterLifeState.Dead));
            Assert.That(character.Physiology.DeathCause, Is.EqualTo(DeathCause.BloodLoss));
        }

        [Test]
        public void ActiveSleep_AdvancesPhysiologyButNotOfflineSafeMetabolism()
        {
            var awake = NewCharacter();
            var sleeping = NewCharacter();
            var offline = NewCharacter();
            awake.Physiology.SleepDebt = 0.9f;
            sleeping.Physiology.SleepDebt = 0.9f;
            offline.Physiology.SleepDebt = 0.9f;
            PhysiologySimulation.BeginSleep(sleeping);
            offline.Offline = true;
            PhysiologySimulation.BeginSleep(offline);

            PhysiologySimulation.Simulate(
                awake, 300f, SurvivalEnvironment.Temperate, 0f);
            PhysiologySimulation.Simulate(
                sleeping, 300f, SurvivalEnvironment.Temperate, 0f);
            PhysiologySimulation.Simulate(
                offline, 300f, SurvivalEnvironment.Temperate, 0f,
                offlineMetabolismSlowed: true);

            Assert.That(sleeping.Physiology.SleepDebt,
                Is.LessThan(awake.Physiology.SleepDebt));
            Assert.That(sleeping.Physiology.Hydration,
                Is.LessThan(awake.Physiology.Hydration),
                "Active sleep must advance several physiological hours.");
            Assert.That(offline.Physiology.Hydration,
                Is.GreaterThan(awake.Physiology.Hydration),
                "Stable offline sleep keeps its fourfold metabolism slowdown.");
        }

        [Test]
        public void CircadianClock_MakesNightSleepMoreRestorativeThanMiddaySleep()
        {
            var night = NewCharacter();
            var midday = NewCharacter();
            night.Physiology.SleepDebt = 0.9f;
            midday.Physiology.SleepDebt = 0.9f;
            PhysiologySimulation.BeginSleep(night);
            PhysiologySimulation.BeginSleep(midday);
            var nightEnvironment = new SurvivalEnvironment(
                18f, 1f, 0.5f, 0f, 0.25f, 0f, true,
                dayFraction: 0.125f);
            var middayEnvironment = new SurvivalEnvironment(
                18f, 1f, 0.5f, 0f, 0.25f, 0f, true,
                dayFraction: 0.5f);

            PhysiologySimulation.Simulate(night, 600f, nightEnvironment, 0f);
            PhysiologySimulation.Simulate(midday, 600f, middayEnvironment, 0f);

            Assert.That(night.Physiology.CircadianFatigue,
                Is.GreaterThan(midday.Physiology.CircadianFatigue));
            Assert.That(night.Physiology.SleepDebt,
                Is.LessThan(midday.Physiology.SleepDebt));
            Assert.That(PhysiologySimulation.CalculateCircadianSleepPressure(0.125f),
                Is.GreaterThan(PhysiologySimulation.CalculateCircadianSleepPressure(0.5f)));
        }

        [Test]
        public void SeededWeather_ContinuesAcrossRestartAndDrivesDayNightLight()
        {
            const long seed = 981723451L;
            const double gameSeconds = 17.25d * 86400d;
            var beforeRestart = WorldWeatherService.Sample(
                seed, new Vector3(140f, 18f, -90f), gameSeconds);
            var afterRestart = WorldWeatherService.Sample(
                seed, new Vector3(140f, 18f, -90f), gameSeconds);

            Assert.That(afterRestart.TemperatureC,
                Is.EqualTo(beforeRestart.TemperatureC).Within(0.00001f));
            Assert.That(afterRestart.Precipitation,
                Is.EqualTo(beforeRestart.Precipitation).Within(0.00001f));
            Assert.That(afterRestart.WindDirection,
                Is.EqualTo(beforeRestart.WindDirection));
            Assert.That(WorldWeatherService.CalculateDaylight(0f, 0f),
                Is.LessThan(0.05f));
            Assert.That(WorldWeatherService.CalculateDaylight(0.5f, 0f),
                Is.GreaterThan(0.95f));
            Assert.That(WorldWeatherService.CalculateDaylight(0.5f, 1f),
                Is.LessThan(WorldWeatherService.CalculateDaylight(0.5f, 0f)));
        }

        [Test]
        public void EveryIrreversibleDeathCarriesCause()
        {
            var character = NewCharacter();
            character.Anatomy.BrainFunction = 0f;
            PhysiologySimulation.Simulate(
                character, 1f, SurvivalEnvironment.Temperate, 0f);

            Assert.That(character.Physiology.LifeState, Is.EqualTo(CharacterLifeState.Dead));
            Assert.That(character.Physiology.DeathCause, Is.Not.EqualTo(DeathCause.None));
        }

        [Test]
        public void ContaminatedFoodAndWater_AffectTheOrganismWithoutHiddenHp()
        {
            var character = NewCharacter();
            character.Physiology.Hydration = 0.35f;
            var infectionBefore = character.Physiology.SystemicInfection;

            PhysiologySimulation.ConsumeFood(
                character, 400f, 20f, 0.8f, 0.75f, 0.2f);
            PhysiologySimulation.ConsumeWater(
                character, 0.25f, 0.7f, 0.15f);

            Assert.That(character.Physiology.Hydration, Is.GreaterThan(0.35f));
            Assert.That(character.Physiology.SystemicInfection, Is.GreaterThan(infectionBefore));
            Assert.That(character.Physiology.ToxinLoad, Is.GreaterThan(0f));
        }

        [Test]
        public void RottenTeethPredispositionAcceleratesDecayButExtractionLeavesConsequences()
        {
            var ordinary = NewCharacter();
            var predisposed = NewCharacter();
            predisposed.Traits.Add(TraitId.RottenTeeth);
            PhysiologySimulation.Simulate(
                ordinary, PhysiologySimulation.RealSecondsPerGameDay,
                SurvivalEnvironment.Temperate, 0f);
            PhysiologySimulation.Simulate(
                predisposed, PhysiologySimulation.RealSecondsPerGameDay,
                SurvivalEnvironment.Temperate, 0f);
            Assert.That(predisposed.Physiology.DentalHealth,
                Is.LessThan(ordinary.Physiology.DentalHealth));

            predisposed.Physiology.DentalHealth = 0.2f;
            predisposed.Physiology.DentalInfection = 0.65f;
            var result = PhysiologySimulation.ExtractTooth(
                predisposed,
                new TreatmentContext(
                    0.8f, 0.95f, hasWater: true,
                    hasBandage: true, practitionerHandCleanliness: 0.95f));
            Assert.That(result.Success, Is.True);
            Assert.That(predisposed.Physiology.MissingTeeth, Is.EqualTo(1));
            Assert.That(predisposed.Physiology.DentalInfection, Is.LessThan(0.65f));
            Assert.That(predisposed.Anatomy.Wounds.Exists(
                wound => wound.Region == BodyRegion.Head && !wound.Healed), Is.True);
            Assert.That(predisposed.Anatomy.BrainFunction, Is.EqualTo(1f),
                "A tooth socket must not be treated as penetrating brain trauma.");
            Assert.That(predisposed.Traits, Does.Contain(TraitId.RottenTeeth),
                "Extraction treats the tooth, not the inherited predisposition.");
        }

        [Test]
        public void GastrointestinalDisease_HasSpecificFluidAndElectrolyteConsequences()
        {
            var sick = NewCharacter();
            var control = NewCharacter();
            sick.Conditions.WaterborneInfection = 0.72f;

            PhysiologySimulation.Simulate(
                sick, 600f, SurvivalEnvironment.Temperate, 0f);
            PhysiologySimulation.Simulate(
                control, 600f, SurvivalEnvironment.Temperate, 0f);

            Assert.That(sick.Physiology.Hydration, Is.LessThan(control.Physiology.Hydration));
            Assert.That(sick.Physiology.ElectrolyteBalance,
                Is.LessThan(control.Physiology.ElectrolyteBalance));
            Assert.That(PhysiologySimulation.ObserveSymptoms(sick)
                    .HasFlag(SymptomFlags.Diarrhea), Is.True);
        }

        [Test]
        public void Parasites_DrainNutritionWithoutBecomingAbstractDamage()
        {
            var sick = NewCharacter();
            var control = NewCharacter();
            sick.Conditions.ParasiteLoad = 0.8f;

            PhysiologySimulation.Simulate(
                sick, 1800f, SurvivalEnvironment.Temperate, 0f);
            PhysiologySimulation.Simulate(
                control, 1800f, SurvivalEnvironment.Temperate, 0f);

            Assert.That(sick.Physiology.EnergyReserve,
                Is.LessThan(control.Physiology.EnergyReserve));
            Assert.That(sick.Physiology.MicronutrientReserve,
                Is.LessThan(control.Physiology.MicronutrientReserve));
            Assert.That(PhysiologySimulation.ObserveSymptoms(sick)
                    .HasFlag(SymptomFlags.ParasiteSigns), Is.True);
        }

        [Test]
        public void VariedNutrition_TracksFatVitaminsAndMineralsSeparately()
        {
            var character = NewCharacter();
            character.Physiology.FatReserve = 0.1f;
            character.Physiology.MicronutrientReserve = 0.1f;
            character.Physiology.MineralReserve = 0.1f;

            PhysiologySimulation.ConsumeFood(
                character,
                500f,
                12f,
                0.8f,
                0f,
                0f,
                fat: 30f,
                minerals: 0.7f);

            Assert.That(character.Physiology.FatReserve, Is.EqualTo(0.1f));
            Assert.That(character.Physiology.DigestingFat, Is.GreaterThan(0f));
            Assert.That(character.Physiology.DigestingMicronutrients, Is.GreaterThan(0f));
            PhysiologySimulation.Simulate(
                character, 600f, SurvivalEnvironment.Temperate, 0f);
            Assert.That(character.Physiology.FatReserve, Is.GreaterThan(0.1f));
            Assert.That(character.Physiology.MicronutrientReserve, Is.GreaterThan(0.1f));
            Assert.That(character.Physiology.MineralReserve, Is.GreaterThan(0.1f));
        }

        [Test]
        public void FoodborneRisk_IsAbsorbedGraduallyThroughTheGut()
        {
            var character = NewCharacter();
            PhysiologySimulation.ConsumeFood(
                character, 300f, 5f, 0.2f, 0.8f, 0.65f);

            Assert.That(character.Conditions.FoodborneInfection, Is.Zero);
            Assert.That(character.Physiology.ToxinLoad, Is.Zero);
            Assert.That(character.Physiology.DigestingBiologicalContamination,
                Is.GreaterThan(0f));
            PhysiologySimulation.Simulate(
                character, 300f, SurvivalEnvironment.Temperate, 0f);

            Assert.That(character.Conditions.FoodborneInfection, Is.GreaterThan(0f));
            Assert.That(character.Physiology.ToxinLoad, Is.GreaterThan(0f));
            Assert.That(character.Physiology.DigestingBiologicalContamination,
                Is.GreaterThan(0f), "One short step must not absorb the whole meal.");
        }

        [Test]
        public void MicronutrientDeficiency_IsObservableWithoutExposingPercentages()
        {
            var character = NewCharacter();
            character.Physiology.MicronutrientReserve = 0.2f;
            character.Physiology.MineralReserve = 0.7f;

            var symptoms = PhysiologySimulation.ObserveSymptoms(character);

            Assert.That(symptoms.HasFlag(SymptomFlags.NutritionalDeficiency), Is.True);
            Assert.That(symptoms.HasFlag(SymptomFlags.Weakness), Is.True);
        }

        [Test]
        public void RespiratoryInfection_IsObservableAndReducesOxygenation()
        {
            var character = NewCharacter();
            PhysiologySimulation.ExposeRespiratoryInfection(character, 1f);
            character.Conditions.RespiratoryInfection = 0.7f;

            PhysiologySimulation.Simulate(
                character, 30f, SurvivalEnvironment.Temperate, 0f);

            Assert.That(character.Physiology.Oxygenation, Is.LessThan(0.9f));
            Assert.That(PhysiologySimulation.ObserveSymptoms(character)
                    .HasFlag(SymptomFlags.Cough), Is.True);
        }

        [Test]
        public void SupportiveCare_RequiresMaterialsAndTreatsSymptomsNotCauses()
        {
            var character = NewCharacter();
            character.Physiology.Hydration = 0.42f;
            character.Physiology.ElectrolyteBalance = 0.35f;
            character.Conditions.WaterborneInfection = 0.55f;
            var beforeDisease = character.Conditions.WaterborneInfection;

            var missingSalt = PhysiologySimulation.ApplySupportiveTreatment(
                character, MedicalActionType.OralRehydration,
                new TreatmentContext(0.5f, 1f, hasWater: true));
            Assert.That(missingSalt.Success, Is.False);

            var treated = PhysiologySimulation.ApplySupportiveTreatment(
                character, MedicalActionType.OralRehydration,
                new TreatmentContext(0.5f, 1f, hasWater: true, hasSalt: true));
            Assert.That(treated.Success, Is.True);
            Assert.That(character.Physiology.Hydration, Is.GreaterThan(0.42f));
            Assert.That(character.Physiology.ElectrolyteBalance, Is.GreaterThan(0.35f));
            Assert.That(character.Conditions.WaterborneInfection,
                Is.EqualTo(beforeDisease), "The drink supports recovery but is not a cure.");
        }

        [Test]
        public void AntiparasiticCourse_WorksOverGameDaysAndCarriesTreatmentRisk()
        {
            var character = NewCharacter();
            character.Conditions.ParasiteLoad = 0.65f;
            var result = PhysiologySimulation.ApplySupportiveTreatment(
                character,
                MedicalActionType.AntiparasiticCourse,
                new TreatmentContext(
                    0.7f, 0.9f, hasWater: true, hasHerbs: true));

            Assert.That(result.Success, Is.True);
            Assert.That(character.Conditions.ParasiteLoad, Is.EqualTo(0.65f),
                "Starting a course must not cure the patient instantly.");
            Assert.That(character.Conditions.AntiparasiticCourseSeconds,
                Is.EqualTo(PhysiologySimulation.RealSecondsPerGameDay * 2f));

            PhysiologySimulation.Simulate(
                character,
                PhysiologySimulation.RealSecondsPerGameDay,
                SurvivalEnvironment.Temperate,
                0f);
            Assert.That(character.Conditions.ParasiteLoad, Is.LessThan(0.65f));
            Assert.That(character.Conditions.ParasiteLoad, Is.GreaterThan(0.15f),
                "One day of a two-day course must not erase a heavy infestation.");
            Assert.That(character.Conditions.AntiparasiticCourseSeconds,
                Is.GreaterThan(0f));
        }

        [Test]
        public void HerbalInfusion_ReducesSymptomsWithoutRepairingTheWound()
        {
            var character = NewCharacter();
            var wound = PhysiologySimulation.AddInjury(
                character, BodyRegion.LeftHand, DamageKind.Edged, 0.55f);
            PhysiologySimulation.Simulate(
                character, 1f, SurvivalEnvironment.Temperate, 0f);
            var painBefore = character.Physiology.Pain;
            var damageBefore = wound.TissueDamage;

            PhysiologySimulation.ConsumeHerbalInfusion(character, 0.25f);
            PhysiologySimulation.Simulate(
                character, 1f, SurvivalEnvironment.Temperate, 0f);

            Assert.That(character.Physiology.Pain, Is.LessThan(painBefore));
            Assert.That(wound.TissueDamage,
                Is.EqualTo(damageBefore).Within(0.001f),
                "An infusion may ease pain but must not close a wound.");
            Assert.That(character.Conditions.HerbalAnalgesiaSeconds, Is.GreaterThan(0f));
        }

        [Test]
        public void SaltWater_WorsensHydrationAndStillFillsBladder()
        {
            var character = NewCharacter();
            character.Physiology.Hydration = 0.6f;
            var bladderBefore = character.Physiology.BladderFill;

            PhysiologySimulation.ConsumeWater(
                character,
                0.25f,
                0f,
                0f,
                electrolyteContent: 1f,
                hydrationEfficiency: -0.35f);

            Assert.That(character.Physiology.Hydration, Is.LessThan(0.6f));
            Assert.That(character.Physiology.BladderFill, Is.GreaterThan(bladderBefore));
        }

        [Test]
        public void ExcessiveCarriedMass_DisablesSprintAndEventuallyMovement()
        {
            var character = NewCharacter();
            character.Progression.Attributes[(int)CharacterAttributeId.Strength] = 50f;
            character.Physiology.CarriedMassKg = 42f;
            var overloaded = PhysiologySimulation.CalculateCapabilities(character);
            Assert.That(overloaded.CanSprint, Is.False);
            Assert.That(overloaded.CanMove, Is.True);

            character.Physiology.CarriedMassKg = 70f;
            Assert.That(PhysiologySimulation.CalculateCapabilities(character).CanMove, Is.False);
        }

        [Test]
        public void HealedSevereInjury_LeavesFunctionalDeficitThatNeedsLongRehabilitation()
        {
            var character = NewCharacter();
            var wound = new WoundState
            {
                Region = BodyRegion.LeftThigh,
                Type = InjuryType.ClosedFracture,
                Severity = 1f,
                Healed = true,
                PermanentImpairment = 0.3f,
            };
            character.Anatomy.Wounds.Add(wound);
            var impaired = PhysiologySimulation.CalculateCapabilities(character).MovementSpeed;
            Assert.That(impaired, Is.LessThan(1f));

            PhysiologySimulation.Simulate(
                character, 600f, SurvivalEnvironment.Temperate, 0.75f);
            Assert.That(wound.PermanentImpairment, Is.LessThan(0.3f));
            Assert.That(wound.PermanentImpairment, Is.GreaterThan(0.05f),
                "Ten minutes of movement must not erase a severe lasting injury.");
            var rehabilitatedImpairment = wound.PermanentImpairment;
            wound.PermanentImpairment = 0.3f;
            var sameMomentWithoutRehabilitation =
                PhysiologySimulation.CalculateCapabilities(character).MovementSpeed;
            wound.PermanentImpairment = rehabilitatedImpairment;
            Assert.That(PhysiologySimulation.CalculateCapabilities(character).MovementSpeed,
                Is.GreaterThan(sameMomentWithoutRehabilitation));
        }

        [Test]
        public void IgnoredBladderNeed_CausesDirtyStressfulAccident()
        {
            var character = NewCharacter();
            character.Physiology.BladderFill = 0.999f;
            character.Physiology.BodyCleanliness = 1f;

            PhysiologySimulation.Simulate(
                character, 1f, SurvivalEnvironment.Temperate, 0f);

            Assert.That(character.Physiology.BladderFill, Is.Zero);
            Assert.That(character.Physiology.BodyCleanliness, Is.LessThan(1f));
            Assert.That(character.Physiology.Stress, Is.GreaterThanOrEqualTo(0.72f));
            Assert.That(character.Physiology.EliminationAccidentRevision, Is.EqualTo(1));
            Assert.That(character.Physiology.LastEliminationAccidentWasBowel, Is.False);
        }

        [Test]
        public void ClosedTrauma_CannotBeWashedSuturedOrBandaged()
        {
            var character = NewCharacter();
            var wound = PhysiologySimulation.AddInjury(
                character, BodyRegion.LeftForearm, DamageKind.Blunt, 0.35f);
            var context = new TreatmentContext(
                0.8f,
                1f,
                hasWater: true,
                hasNeedleAndThread: true,
                hasBandage: true);

            Assert.That(PhysiologySimulation.Treat(
                character, wound.WoundId, MedicalActionType.Wash, context).Success, Is.False);
            Assert.That(PhysiologySimulation.Treat(
                character, wound.WoundId, MedicalActionType.Suture, context).Success, Is.False);
            Assert.That(PhysiologySimulation.Treat(
                character, wound.WoundId, MedicalActionType.Bandage, context).Success, Is.False);
        }

        [Test]
        public void TreatmentContamination_ComesFromPractitionerHandsNotPatientHands()
        {
            var cleanCare = NewCharacter();
            var dirtyCare = NewCharacter();
            cleanCare.Physiology.HandCleanliness = 0f;
            dirtyCare.Physiology.HandCleanliness = 1f;
            var cleanWound = PhysiologySimulation.AddInjury(
                cleanCare, BodyRegion.LeftHand, DamageKind.Edged, 0.4f, 0.45f);
            var dirtyWound = PhysiologySimulation.AddInjury(
                dirtyCare, BodyRegion.LeftHand, DamageKind.Edged, 0.4f, 0.45f);

            PhysiologySimulation.Treat(cleanCare, cleanWound.WoundId, MedicalActionType.Wash,
                new TreatmentContext(0.5f, 1f, hasWater: true,
                    practitionerHandCleanliness: 1f));
            PhysiologySimulation.Treat(dirtyCare, dirtyWound.WoundId, MedicalActionType.Wash,
                new TreatmentContext(0.5f, 1f, hasWater: true,
                    practitionerHandCleanliness: 0f));

            Assert.That(dirtyWound.Contamination, Is.GreaterThan(cleanWound.Contamination));
        }

        [Test]
        public void DryInsulatingClothing_SlowsColdExposure()
        {
            var exposed = NewCharacter();
            var insulated = NewCharacter();
            var coldWind = new SurvivalEnvironment(
                -8f, 8f, 0.75f, 0f, 0.05f, 0f, false);
            var protectedColdWind = new SurvivalEnvironment(
                -8f, 8f, 0.75f, 0f, 0.64f, 0f, false);

            PhysiologySimulation.Simulate(exposed, 900f, coldWind, 0f);
            PhysiologySimulation.Simulate(insulated, 900f, protectedColdWind, 0f);

            Assert.That(insulated.Physiology.CoreTemperatureC,
                Is.GreaterThan(exposed.Physiology.CoreTemperatureC));
        }

        [Test]
        public void DenseSmoke_CausesObservableIrritationAndRespiratoryDeath()
        {
            var character = NewCharacter();
            var smokeFilledRoom = new SurvivalEnvironment(
                18f, 0f, 0.5f, 0f, 0.4f, 0f, true, 1f);

            PhysiologySimulation.Simulate(character, 45f, smokeFilledRoom, 0f);

            Assert.That(character.Physiology.SmokeIrritation, Is.GreaterThan(0.8f));
            Assert.That(character.Physiology.LifeState, Is.EqualTo(CharacterLifeState.Dead));
            Assert.That(character.Physiology.DeathCause,
                Is.EqualTo(DeathCause.RespiratoryFailure));
            Assert.That(PhysiologySimulation.ObserveSymptoms(character)
                    .HasFlag(SymptomFlags.SmokeIrritation),
                Is.True);
        }

        private static CharacterSurvivalState NewCharacter()
        {
            var character = new CharacterSurvivalState
            {
                CharacterId = "test-character",
                Traits = new List<TraitId>(),
            };
            character.EnsureInitialized();
            return character;
        }

        [Test]
        public void PendingMedicalActivity_PreservesRemainingWorkAndStablePatientAcrossRestart()
        {
            var character = NewCharacter();
            character.MedicalActivity.Active = true;
            character.MedicalActivity.WoundId = 42;
            character.MedicalActivity.Action = MedicalActionType.Suture;
            character.MedicalActivity.RemainingSeconds = 17.25f;
            character.MedicalActivity.TargetCharacterId = "patient-after-restart";

            var restored = JsonUtility.FromJson<CharacterSurvivalState>(
                JsonUtility.ToJson(character));
            restored.EnsureInitialized();

            Assert.That(restored.MedicalActivity.Active, Is.True);
            Assert.That(restored.MedicalActivity.WoundId, Is.EqualTo(42));
            Assert.That(restored.MedicalActivity.Action,
                Is.EqualTo(MedicalActionType.Suture));
            Assert.That(restored.MedicalActivity.RemainingSeconds,
                Is.EqualTo(17.25f).Within(0.001f));
            Assert.That(restored.MedicalActivity.TargetCharacterId,
                Is.EqualTo("patient-after-restart"));
        }

        [Test]
        public void FictionalSilenceRite_RequiresRepeatedAttemptsAndEndsInPhysiologicalAgony()
        {
            var novice = NewCharacter();
            novice.Progression.Attributes[(int)CharacterAttributeId.Willpower] = 0f;
            novice.Physiology.Stress = 1f;
            var practiced = NewCharacter();
            practiced.Progression.Attributes[(int)CharacterAttributeId.Willpower] = 100f;
            practiced.Physiology.Stress = 0f;
            practiced.VoluntaryPassing.TechniqueFamiliarity = 0.8f;

            Assert.That(PhysiologySimulation.CompleteFictionalSilenceAttempt(
                novice, out var noviceGain), Is.False);
            Assert.That(PhysiologySimulation.CompleteFictionalSilenceAttempt(
                practiced, out var practicedGain), Is.False);
            Assert.That(practicedGain, Is.GreaterThan(noviceGain));
            Assert.That(novice.VoluntaryPassing.PassageProgress, Is.LessThan(1f));

            var attempts = 1;
            while (novice.Physiology.LifeState != CharacterLifeState.Agonal
                && attempts++ < 12)
            {
                PhysiologySimulation.CompleteFictionalSilenceAttempt(novice, out _);
            }
            Assert.That(attempts, Is.GreaterThan(2));
            Assert.That(novice.Physiology.LifeState, Is.EqualTo(CharacterLifeState.Agonal));
            Assert.That(novice.Physiology.CriticalCause,
                Is.EqualTo(DeathCause.VoluntaryPassing));

            PhysiologySimulation.Simulate(
                novice, 60f, SurvivalEnvironment.Temperate, 0f);
            Assert.That(novice.Physiology.LifeState, Is.EqualTo(CharacterLifeState.Dead));
            Assert.That(novice.Physiology.DeathCause,
                Is.EqualTo(DeathCause.VoluntaryPassing));
            Assert.That(novice.Anatomy.HeartFunction, Is.LessThanOrEqualTo(0.02f));
        }

        [Test]
        public void Agony_HasCauseDependentTissueDamageAndCanEndBeforeIrreversibleFailure()
        {
            var cold = NewCharacter();
            cold.Physiology.CoreTemperatureC = 29f;
            var coldRoom = new SurvivalEnvironment(-15f, 0f, 0.5f, 0f, 0.2f, 0f, true);
            PhysiologySimulation.Simulate(cold, 25f, coldRoom, 0f);
            Assert.That(cold.Physiology.LifeState, Is.EqualTo(CharacterLifeState.Agonal));
            Assert.That(cold.Physiology.CriticalCause, Is.EqualTo(DeathCause.Hypothermia));
            Assert.That(cold.Physiology.DeathCause, Is.EqualTo(DeathCause.None));
            Assert.That(cold.Anatomy.HeartFunction, Is.LessThan(1f));
            cold.Physiology.CoreTemperatureC = 37f;
            PhysiologySimulation.Simulate(cold, 10f, SurvivalEnvironment.Temperate, 0f);
            Assert.That(cold.Physiology.LifeState, Is.LessThan(CharacterLifeState.Agonal));
            Assert.That(cold.Physiology.CriticalCause, Is.EqualTo(DeathCause.None));

            var respiratory = NewCharacter();
            respiratory.Anatomy.LeftLungFunction = 0.02f;
            respiratory.Anatomy.RightLungFunction = 0.02f;
            PhysiologySimulation.Simulate(respiratory, 60f, SurvivalEnvironment.Temperate, 0f);
            Assert.That(respiratory.Physiology.LifeState, Is.EqualTo(CharacterLifeState.Dead));
            Assert.That(respiratory.Physiology.DeathCause, Is.EqualTo(DeathCause.RespiratoryFailure));
            Assert.That(respiratory.Anatomy.BrainFunction, Is.LessThanOrEqualTo(0.02f));
        }
    }
}
