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
    }
}
