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
            bool blocked)
        {
            var multiplier = attack == MeleeAttackKind.Heavy
                ? Mathf.Lerp(1.2f, 1.85f, Mathf.Clamp01(heavyCharge))
                : 0.78f;
            if (blocked) multiplier *= 0.27f;
            return Mathf.Clamp01(weapon.BaseImpact * multiplier);
        }

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
