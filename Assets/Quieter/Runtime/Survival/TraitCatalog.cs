using System;
using System.Collections.Generic;
using System.Linq;

namespace Quieter.Survival
{
    public readonly struct TraitDefinition
    {
        public TraitDefinition(
            TraitId id,
            string name,
            int pointValue,
            bool positive,
            TraitId? opposite = null)
        {
            Id = id;
            Name = name;
            PointValue = pointValue;
            Positive = positive;
            Opposite = opposite;
        }

        public TraitId Id { get; }
        public string Name { get; }
        public int PointValue { get; }
        public bool Positive { get; }
        public TraitId? Opposite { get; }
    }

    public readonly struct TraitModifiers
    {
        public TraitModifiers(
            float healing,
            float immunity,
            float learning,
            float aerobic,
            float strength,
            float fineMotor,
            float perception,
            float digestion,
            float sleepNeed,
            float coldTolerance,
            float heatTolerance,
            float painTolerance,
            float coagulation,
            float fractureResistance,
            float thermoregulation,
            float metabolism,
            float lungCapacity)
        {
            Healing = healing;
            Immunity = immunity;
            Learning = learning;
            Aerobic = aerobic;
            Strength = strength;
            FineMotor = fineMotor;
            Perception = perception;
            Digestion = digestion;
            SleepNeed = sleepNeed;
            ColdTolerance = coldTolerance;
            HeatTolerance = heatTolerance;
            PainTolerance = painTolerance;
            Coagulation = coagulation;
            FractureResistance = fractureResistance;
            Thermoregulation = thermoregulation;
            Metabolism = metabolism;
            LungCapacity = lungCapacity;
        }

        public float Healing { get; }
        public float Immunity { get; }
        public float Learning { get; }
        public float Aerobic { get; }
        public float Strength { get; }
        public float FineMotor { get; }
        public float Perception { get; }
        public float Digestion { get; }
        public float SleepNeed { get; }
        public float ColdTolerance { get; }
        public float HeatTolerance { get; }
        public float PainTolerance { get; }
        public float Coagulation { get; }
        public float FractureResistance { get; }
        public float Thermoregulation { get; }
        public float Metabolism { get; }
        public float LungCapacity { get; }

        public static TraitModifiers Default => new(
            1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f,
            1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f);
    }

    public static class TraitCatalog
    {
        public const int InitialPoints = 4;
        public const int MaximumPositiveTraits = 2;
        public const int MaximumNegativeTraits = 2;

        private static readonly TraitDefinition[] Definitions =
        {
            Positive(TraitId.FastHealing, "Быстрое заживление", 6, TraitId.SlowHealing),
            Positive(TraitId.StrongImmunity, "Крепкий иммунитет", 6, TraitId.WeakImmunity),
            Positive(TraitId.FastLearner, "Быстрое обучение", 8, TraitId.SlowLearner),
            Positive(TraitId.Athletic, "Атлетичность", 6),
            Positive(TraitId.StrongBuild, "Сильное сложение", 6),
            Positive(TraitId.Dexterous, "Ловкие руки", 4, TraitId.Clumsy),
            Positive(TraitId.KeenSenses, "Острые чувства", 4, TraitId.Myopia),
            Positive(TraitId.IronStomach, "Крепкий желудок", 4, TraitId.SensitiveDigestion),
            Positive(TraitId.LowSleepNeed, "Низкая потребность во сне", 4, TraitId.Insomnia),
            Positive(TraitId.ColdAdapted, "Холодовая адаптация", 4, TraitId.PoorThermoregulation),
            Positive(TraitId.HeatAdapted, "Жаровая адаптация", 4, TraitId.PoorThermoregulation),
            Positive(TraitId.HighPainThreshold, "Высокий болевой порог", 4, TraitId.ChronicPain),

            Negative(TraitId.SlowHealing, "Медленное заживление", 6, TraitId.FastHealing),
            Negative(TraitId.WeakImmunity, "Слабый иммунитет", 6, TraitId.StrongImmunity),
            Negative(TraitId.SlowLearner, "Медленное обучение", 8, TraitId.FastLearner),
            Negative(TraitId.Asthma, "Астма", 6),
            Negative(TraitId.Coagulopathy, "Нарушение свёртывания", 8),
            Negative(TraitId.FragileBones, "Хрупкие кости", 6),
            Negative(TraitId.RottenTeeth, "Гнилые зубы", 4),
            Negative(TraitId.SensitiveDigestion, "Чувствительное пищеварение", 4, TraitId.IronStomach),
            Negative(TraitId.Insomnia, "Бессонница", 6, TraitId.LowSleepNeed),
            Negative(TraitId.Myopia, "Близорукость", 4, TraitId.KeenSenses),
            Negative(TraitId.Clumsy, "Неуклюжесть", 4, TraitId.Dexterous),
            Negative(TraitId.PoorThermoregulation, "Плохая терморегуляция", 4),
            Negative(TraitId.HighMetabolism, "Высокий обмен веществ", 4),
            Negative(TraitId.ChronicPain, "Хроническая боль", 4, TraitId.HighPainThreshold),
        };

        private static readonly Dictionary<TraitId, TraitDefinition> ById =
            Definitions.ToDictionary(definition => definition.Id);

        public static IReadOnlyList<TraitDefinition> All => Definitions;

        public static TraitDefinition Get(TraitId id)
        {
            if (!ById.TryGetValue(id, out var definition))
            {
                throw new ArgumentOutOfRangeException(nameof(id), id, "Unknown trait.");
            }

            return definition;
        }

        public static bool TryValidate(
            IReadOnlyCollection<TraitId> traits,
            out int remainingPoints,
            out string error)
        {
            remainingPoints = InitialPoints;
            error = string.Empty;
            if (traits == null)
            {
                return true;
            }

            var selected = new HashSet<TraitId>();
            var positiveCount = 0;
            var negativeCount = 0;
            foreach (var id in traits)
            {
                if (!ById.TryGetValue(id, out var definition))
                {
                    error = $"Неизвестная черта: {id}.";
                    return false;
                }

                if (!selected.Add(id))
                {
                    error = $"Черта «{definition.Name}» выбрана повторно.";
                    return false;
                }

                if (definition.Opposite.HasValue
                    && selected.Contains(definition.Opposite.Value))
                {
                    error = $"Черта «{definition.Name}» несовместима с «{Get(definition.Opposite.Value).Name}».";
                    return false;
                }

                if (definition.Positive)
                {
                    positiveCount++;
                    remainingPoints -= definition.PointValue;
                }
                else
                {
                    negativeCount++;
                    remainingPoints += definition.PointValue;
                }
            }

            if (positiveCount > MaximumPositiveTraits)
            {
                error = "Можно выбрать не более двух положительных черт.";
                return false;
            }

            if (negativeCount > MaximumNegativeTraits)
            {
                error = "Можно выбрать не более двух отрицательных черт.";
                return false;
            }

            if (remainingPoints < 0)
            {
                error = $"Не хватает {-remainingPoints} очк. предрасположенностей.";
                return false;
            }

            return true;
        }

        public static TraitModifiers Resolve(IReadOnlyCollection<TraitId> traits)
        {
            var healing = 1f;
            var immunity = 1f;
            var learning = 1f;
            var aerobic = 1f;
            var strength = 1f;
            var fineMotor = 1f;
            var perception = 1f;
            var digestion = 1f;
            var sleepNeed = 1f;
            var coldTolerance = 1f;
            var heatTolerance = 1f;
            var painTolerance = 1f;
            var coagulation = 1f;
            var fractureResistance = 1f;
            var thermoregulation = 1f;
            var metabolism = 1f;
            var lungCapacity = 1f;

            if (traits != null)
            {
                foreach (var trait in traits)
                {
                    switch (trait)
                    {
                        case TraitId.FastHealing: healing *= 1.35f; break;
                        case TraitId.SlowHealing: healing *= 0.65f; break;
                        case TraitId.StrongImmunity: immunity *= 1.35f; break;
                        case TraitId.WeakImmunity: immunity *= 0.65f; break;
                        case TraitId.FastLearner: learning *= 1.3f; break;
                        case TraitId.SlowLearner: learning *= 0.7f; break;
                        case TraitId.Athletic: aerobic *= 1.2f; break;
                        case TraitId.StrongBuild: strength *= 1.2f; break;
                        case TraitId.Dexterous: fineMotor *= 1.2f; break;
                        case TraitId.KeenSenses: perception *= 1.2f; break;
                        case TraitId.IronStomach: digestion *= 1.35f; break;
                        case TraitId.SensitiveDigestion: digestion *= 0.65f; break;
                        case TraitId.LowSleepNeed: sleepNeed *= 0.75f; break;
                        case TraitId.Insomnia: sleepNeed *= 1.25f; break;
                        case TraitId.ColdAdapted: coldTolerance *= 1.35f; break;
                        case TraitId.HeatAdapted: heatTolerance *= 1.35f; break;
                        case TraitId.HighPainThreshold: painTolerance *= 1.35f; break;
                        case TraitId.ChronicPain: painTolerance *= 0.7f; break;
                        case TraitId.Asthma: lungCapacity *= 0.72f; break;
                        case TraitId.Coagulopathy: coagulation *= 0.45f; break;
                        case TraitId.FragileBones: fractureResistance *= 0.55f; break;
                        case TraitId.Myopia: perception *= 0.75f; break;
                        case TraitId.Clumsy: fineMotor *= 0.75f; break;
                        case TraitId.PoorThermoregulation: thermoregulation *= 0.62f; break;
                        case TraitId.HighMetabolism: metabolism *= 1.3f; break;
                    }
                }
            }

            return new TraitModifiers(
                healing, immunity, learning, aerobic, strength, fineMotor,
                perception, digestion, sleepNeed, coldTolerance, heatTolerance,
                painTolerance, coagulation, fractureResistance,
                thermoregulation, metabolism, lungCapacity);
        }

        private static TraitDefinition Positive(
            TraitId id,
            string name,
            int cost,
            TraitId? opposite = null)
            => new(id, name, cost, true, opposite);

        private static TraitDefinition Negative(
            TraitId id,
            string name,
            int refund,
            TraitId? opposite = null)
            => new(id, name, refund, false, opposite);
    }
}
