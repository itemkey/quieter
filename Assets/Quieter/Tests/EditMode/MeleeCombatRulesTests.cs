using NUnit.Framework;
using Quieter.Combat;
using Quieter.Survival;
using UnityEngine;

namespace Quieter.Tests.EditMode
{
    public sealed class MeleeCombatRulesTests
    {
        [Test]
        public void SpearHasReachButCannotHitInsideItsMinimumDistance()
        {
            var spear = MeleeCombatRules.ResolveWeapon(43);
            Assert.That(MeleeCombatRules.IsGeometricHit(
                Vector3.zero, Vector3.forward, Vector3.forward * 2.4f, spear), Is.True);
            Assert.That(MeleeCombatRules.IsGeometricHit(
                Vector3.zero, Vector3.forward, Vector3.forward * 0.3f, spear), Is.False);
        }

        [Test]
        public void ServerGeometryRejectsTargetsBehindAttacker()
        {
            var club = MeleeCombatRules.ResolveWeapon(42);
            Assert.That(MeleeCombatRules.IsGeometricHit(
                Vector3.zero, Vector3.forward, Vector3.back, club), Is.False);
        }

        [Test]
        public void BlockReducesTraumaButDoesNotEraseIt()
        {
            var axe = MeleeCombatRules.ResolveWeapon(4);
            var normal = MeleeCombatRules.ResolveImpact(axe, MeleeAttackKind.Heavy, 1f, false);
            var blocked = MeleeCombatRules.ResolveImpact(axe, MeleeAttackKind.Heavy, 1f, true);
            Assert.That(blocked, Is.GreaterThan(0f));
            Assert.That(blocked, Is.LessThan(normal));
            Assert.That(axe.DamageKind, Is.EqualTo(DamageKind.Edged));
        }

        [Test]
        public void CombatPerformance_UsesLearnedSkillAndCurrentPhysiology()
        {
            var novice = new CharacterSurvivalState { CreationCompleted = true };
            var veteran = new CharacterSurvivalState { CreationCompleted = true };
            novice.EnsureInitialized();
            veteran.EnsureInitialized();
            veteran.Progression.SkillPracticeHours[(int)SkillId.EdgedWeapons] = 60f;
            veteran.Progression.RelevantPracticeHours[(int)SkillId.EdgedWeapons] = 60f;
            veteran.Progression.SkillPracticeHours[(int)SkillId.Defence] = 60f;
            veteran.Progression.RelevantPracticeHours[(int)SkillId.Defence] = 60f;
            veteran.Progression.Attributes[(int)CharacterAttributeId.Strength] = 78f;
            veteran.Progression.Attributes[(int)CharacterAttributeId.MuscularEndurance] = 76f;
            veteran.Progression.Attributes[(int)CharacterAttributeId.Coordination] = 74f;

            var novicePerformance = MeleeCombatRules.ResolvePerformance(
                novice, SkillId.EdgedWeapons);
            var veteranPerformance = MeleeCombatRules.ResolvePerformance(
                veteran, SkillId.EdgedWeapons);
            Assert.That(veteranPerformance.Impact, Is.GreaterThan(novicePerformance.Impact));
            Assert.That(veteranPerformance.StaminaCost,
                Is.LessThan(novicePerformance.StaminaCost));
            Assert.That(veteranPerformance.BlockQuality,
                Is.GreaterThan(novicePerformance.BlockQuality));

            veteran.Physiology.Oxygenation = 0.28f;
            veteran.Physiology.BloodVolume = 0.42f;
            veteran.Physiology.Pain = 0.8f;
            var impaired = MeleeCombatRules.ResolvePerformance(
                veteran, SkillId.EdgedWeapons);
            Assert.That(impaired.Impact, Is.LessThan(veteranPerformance.Impact));
            Assert.That(impaired.ActionSpeed, Is.LessThan(veteranPerformance.ActionSpeed));
            Assert.That(impaired.StaminaCost, Is.GreaterThan(veteranPerformance.StaminaCost));
        }
    }
}
