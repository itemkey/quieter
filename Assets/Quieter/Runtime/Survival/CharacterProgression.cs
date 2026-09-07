using UnityEngine;

namespace Quieter.Survival
{
    public static class CharacterProgression
    {
        public const float ImmediateLearningShare = 0.7f;
        public const float SleepConsolidationShare = 0.3f;

        // Cumulative relevant practice. Early competence arrives quickly, while
        // the last levels deliberately make mastery a long-lived achievement.
        private static readonly float[] LevelThresholdHours =
        {
            0f, 0.75f, 1.75f, 3.5f, 6f, 9f,
            13f, 18f, 24f, 31f, 40f,
        };

        public static float RegisterPractice(
            CharacterProgressionState state,
            SkillId skill,
            float realSeconds,
            float challenge,
            float outcomeQuality,
            float repetition,
            TraitModifiers traits)
        {
            state.EnsureInitialized();
            if (skill >= SkillId.Count || !float.IsFinite(realSeconds) || realSeconds <= 0f
                || !float.IsFinite(challenge) || !float.IsFinite(outcomeQuality)
                || !float.IsFinite(repetition))
            {
                return 0f;
            }

            var challengeWeight = Mathf.Clamp01(challenge);
            var outcomeWeight = Mathf.Lerp(0.2f, 1f, Mathf.Clamp01(outcomeQuality));
            // Repetition below 0.2 is still novel practice.  SmoothStep's first
            // two arguments are output values, not thresholds, so remap the
            // repetition range explicitly before applying the falloff.
            var repetitionWeight = 1f - Mathf.SmoothStep(
                0f,
                1f,
                Mathf.InverseLerp(0.2f, 1f, Mathf.Clamp01(repetition)));
            var currentLevel = GetSkillLevel(state, skill);
            var difficultyFloor = Mathf.InverseLerp(0f, 10f, currentLevel) * 0.45f;
            if (challengeWeight < difficultyFloor)
            {
                repetitionWeight *= 0.1f;
            }

            var relevantHours = realSeconds / 3600f
                * challengeWeight
                * outcomeWeight
                * repetitionWeight;
            var effectiveHours = relevantHours * traits.Learning;
            var index = (int)skill;
            state.RelevantPracticeHours[index] += relevantHours;
            state.SkillPracticeHours[index] += effectiveHours * ImmediateLearningShare;
            state.PendingConsolidationHours[index] += effectiveHours * SleepConsolidationShare;
            return effectiveHours;
        }

        public static float ConsolidateSleep(
            CharacterProgressionState state,
            float sleepQuality)
        {
            state.EnsureInitialized();
            var quality = Mathf.Clamp01(sleepQuality);
            var consolidated = 0f;
            for (var index = 0; index < state.PendingConsolidationHours.Length; index++)
            {
                var amount = state.PendingConsolidationHours[index] * quality;
                state.PendingConsolidationHours[index] -= amount;
                state.SkillPracticeHours[index] += amount;
                consolidated += amount;
            }

            return consolidated;
        }

        public static int GetSkillLevel(CharacterProgressionState state, SkillId skill)
        {
            state.EnsureInitialized();
            var hours = state.SkillPracticeHours[(int)skill];
            for (var level = LevelThresholdHours.Length - 1; level >= 0; level--)
            {
                if (hours >= LevelThresholdHours[level])
                {
                    return level == 10 && state.RelevantPracticeHours[(int)skill] < 40f
                        ? 9 : level;
                }
            }

            return 0;
        }

        public static float GetSkillLevelProgress(
            CharacterProgressionState state,
            SkillId skill)
        {
            var level = GetSkillLevel(state, skill);
            if (level >= 10)
            {
                return 1f;
            }

            var hours = state.SkillPracticeHours[(int)skill];
            if (level == 9) hours = Mathf.Min(hours, state.RelevantPracticeHours[(int)skill]);
            return Mathf.InverseLerp(
                LevelThresholdHours[level],
                LevelThresholdHours[level + 1],
                hours);
        }

        public static int GetAttributeGrade(
            CharacterProgressionState state,
            CharacterAttributeId attribute)
        {
            state.EnsureInitialized();
            return Mathf.Clamp(
                Mathf.FloorToInt(state.Attributes[(int)attribute] / 10f) + 1,
                1,
                10);
        }

        public static void RegisterPhysicalLoad(
            CharacterProgressionState state,
            CharacterAttributeId attribute,
            float realSeconds,
            float intensity)
        {
            state.EnsureInitialized();
            state.AttributeTrainingLoad[(int)attribute] += realSeconds
                * Mathf.Clamp01(intensity) / 3600f;
        }

        public static void SimulateAttributeAdaptation(
            CharacterProgressionState state,
            float realSeconds,
            float nourishment,
            float health,
            bool sleeping)
        {
            state.EnsureInitialized();
            var days = realSeconds / 7200f;
            var recovery = Mathf.Clamp01(nourishment) * Mathf.Clamp01(health);
            for (var index = 0; index < state.Attributes.Length; index++)
            {
                var load = state.AttributeTrainingLoad[index];
                if (sleeping && load > 0f)
                {
                    var adaptation = Mathf.Min(load, days * 0.12f) * recovery;
                    state.Attributes[index] = Mathf.Clamp(
                        state.Attributes[index] + adaptation,
                        0f,
                        100f);
                    state.AttributeTrainingLoad[index] = Mathf.Max(0f, load - adaptation);
                }

                if (load < 0.02f && (index <= (int)CharacterAttributeId.Coordination))
                {
                    var deprivation = 1f - recovery;
                    state.Attributes[index] = Mathf.Max(
                        0f,
                        state.Attributes[index] - days * (0.004f + deprivation * 0.018f));
                }
            }
        }
    }
}
