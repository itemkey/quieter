using Quieter.Survival;
using UnityEngine;

namespace Quieter.Combat
{
    public enum MeleeAttackKind : byte
    {
        Light,
        Heavy,
    }

    public readonly struct MeleeWeaponProfile
    {
        public MeleeWeaponProfile(
            DamageKind damageKind,
            float reach,
            float minimumReach,
            float arcDegrees,
            float baseImpact,
            float staminaCost,
            SkillId skill)
        {
            DamageKind = damageKind;
            Reach = reach;
            MinimumReach = minimumReach;
            ArcDegrees = arcDegrees;
            BaseImpact = baseImpact;
            StaminaCost = staminaCost;
            Skill = skill;
        }

        public DamageKind DamageKind { get; }
        public float Reach { get; }
        public float MinimumReach { get; }
        public float ArcDegrees { get; }
        public float BaseImpact { get; }
        public float StaminaCost { get; }
        public SkillId Skill { get; }
    }

    public readonly struct CombatPerformance
    {
        public CombatPerformance(
            float impact, float staminaCost, float actionSpeed,
            float blockQuality, float dodgeSpeed)
        {
            Impact = Mathf.Clamp(impact, 0.25f, 1.35f);
            StaminaCost = Mathf.Clamp(staminaCost, 0.65f, 2.5f);
            ActionSpeed = Mathf.Clamp(actionSpeed, 0.2f, 1.3f);
            BlockQuality = Mathf.Clamp01(blockQuality);
            DodgeSpeed = Mathf.Clamp(dodgeSpeed, 0.25f, 1.15f);
        }

        public float Impact { get; }
        public float StaminaCost { get; }
        public float ActionSpeed { get; }
        public float BlockQuality { get; }
        public float DodgeSpeed { get; }
    }

    public static class MeleeCombatRules
    {
        public static MeleeWeaponProfile ResolveWeapon(ushort itemId) => itemId switch
        {
            4 => new MeleeWeaponProfile(DamageKind.Edged, 1.65f, 0.2f, 82f, 0.68f, 0.22f, SkillId.EdgedWeapons),
            5 => new MeleeWeaponProfile(DamageKind.Puncture, 1.45f, 0.25f, 65f, 0.62f, 0.25f, SkillId.Mining),
            22 => new MeleeWeaponProfile(DamageKind.Blunt, 1.7f, 0.2f, 75f, 0.45f, 0.2f, SkillId.BluntWeapons),
            42 => new MeleeWeaponProfile(DamageKind.Blunt, 1.75f, 0.15f, 88f, 0.58f, 0.2f, SkillId.BluntWeapons),
            43 => new MeleeWeaponProfile(DamageKind.Puncture, 2.7f, 0.75f, 50f, 0.7f, 0.24f, SkillId.Polearms),
            _ => new MeleeWeaponProfile(DamageKind.Blunt, 1.2f, 0f, 95f, 0.23f, 0.14f, SkillId.UnarmedCombat),
        };

        public static bool IsGeometricHit(
            Vector3 attackerPosition,
            Vector3 attackerForward,
            Vector3 targetPosition,
            MeleeWeaponProfile weapon)
        {
            var offset = targetPosition - attackerPosition;
            var planar = new Vector3(offset.x, 0f, offset.z);
            var distance = planar.magnitude;
            if (distance < weapon.MinimumReach || distance > weapon.Reach
                || Mathf.Abs(offset.y) > 2.2f || distance < 0.001f)
                return false;
            var facing = new Vector3(attackerForward.x, 0f, attackerForward.z).normalized;
            return Vector3.Angle(facing, planar / distance) <= weapon.ArcDegrees * 0.5f;
        }

        public static float ResolveImpact(
            MeleeWeaponProfile weapon,
            MeleeAttackKind attack,
            float heavyCharge,
            bool blocked,
            float attackerEfficiency = 1f,
            float blockQuality = 1f)
        {
            var multiplier = attack == MeleeAttackKind.Heavy
                ? Mathf.Lerp(1.2f, 1.85f, Mathf.Clamp01(heavyCharge))
                : 0.78f;
            if (blocked)
            {
                multiplier *= Mathf.Lerp(
                    0.68f, 0.2f, Mathf.Clamp01(blockQuality));
            }
            return Mathf.Clamp01(weapon.BaseImpact * multiplier
                * Mathf.Clamp(attackerEfficiency, 0.25f, 1.35f));
        }

        public static CombatPerformance ResolvePerformance(
            CharacterSurvivalState character, SkillId weaponSkill)
        {
            if (character == null)
                return new CombatPerformance(0.25f, 2.5f, 0.2f, 0f, 0.25f);
            character.EnsureInitialized();
            var p = character.Physiology;
            var progression = character.Progression;
            var technique = CharacterProgression.GetSkillLevel(
                progression, weaponSkill) / 10f;
            var defence = CharacterProgression.GetSkillLevel(
                progression, SkillId.Defence) / 10f;
            var strength = Attribute(progression, CharacterAttributeId.Strength);
            var endurance = Attribute(
                progression, CharacterAttributeId.MuscularEndurance);
            var aerobic = Attribute(progression, CharacterAttributeId.AerobicCapacity);
            var balance = Attribute(progression, CharacterAttributeId.Balance);
            var coordination = Attribute(progression, CharacterAttributeId.Coordination);
            var capabilities = PhysiologySimulation.CalculateCapabilities(character);
            var circulation = Mathf.Min(
                Mathf.Clamp01(p.Oxygenation * 1.15f),
                Mathf.Clamp01(p.BloodVolume * 1.3f));
            var painControl = Mathf.Lerp(1f, 0.42f, p.Pain);
            var alertness = Mathf.Lerp(0.5f, 1f, p.Consciousness)
                * Mathf.Lerp(1f, 0.55f, p.CircadianFatigue);
            var physiological = circulation * painControl * alertness;
            var impact = (0.56f + strength * 0.34f + technique * 0.22f)
                * Mathf.Lerp(0.62f, 1f, capabilities.FineMotor)
                * Mathf.Lerp(0.48f, 1f, physiological);
            var staminaCost = Mathf.Lerp(1.5f, 0.78f, endurance)
                * Mathf.Lerp(1.25f, 0.86f, technique)
                * Mathf.Lerp(1.55f, 1f, circulation)
                * Mathf.Lerp(1.25f, 1f, aerobic);
            var actionSpeed = Mathf.Lerp(0.48f, 1.12f,
                    (coordination + technique) * 0.5f)
                * Mathf.Lerp(0.45f, 1f, capabilities.FineMotor)
                * Mathf.Lerp(0.55f, 1f, p.AcuteStamina)
                * Mathf.Lerp(0.55f, 1f, physiological);
            var block = Mathf.Lerp(0.18f, 0.92f,
                    defence * 0.7f + coordination * 0.3f)
                * physiological * Mathf.Lerp(0.6f, 1f, p.AcuteStamina);
            var dodge = Mathf.Lerp(0.48f, 1.08f,
                    (balance + coordination) * 0.5f)
                * capabilities.MovementSpeed
                * Mathf.Lerp(0.45f, 1f, p.AcuteStamina)
                * Mathf.Lerp(0.55f, 1f, physiological);
            return new CombatPerformance(
                impact, staminaCost, actionSpeed, block, dodge);
        }

        private static float Attribute(
            CharacterProgressionState progression, CharacterAttributeId id)
            => Mathf.Clamp01(progression.Attributes[(int)id] / 100f);

        public static BodyRegion ResolveRegion(uint attackId)
        {
            var roll = attackId * 2654435761u;
            return (roll % 10u) switch
            {
                0 => BodyRegion.Head,
                1 => BodyRegion.Neck,
                2 => BodyRegion.Abdomen,
                3 => BodyRegion.LeftForearm,
                4 => BodyRegion.RightForearm,
                5 => BodyRegion.LeftThigh,
                6 => BodyRegion.RightThigh,
                _ => BodyRegion.Chest,
            };
        }
    }
}
