using System;
using System.Collections.Generic;
using Quieter.Inventory;
using Quieter.Player;
using Quieter.Survival;
using Unity.Netcode;
using UnityEngine;

namespace Quieter.Combat
{
    [RequireComponent(typeof(NetworkPlayer), typeof(PlayerSurvival), typeof(PlayerInventory))]
    public sealed class PlayerCombat : NetworkBehaviour
    {
        private readonly struct PoseSample
        {
            public PoseSample(double time, Vector3 position, Vector3 forward)
            {
                Time = time;
                Position = position;
                Forward = forward;
            }
            public double Time { get; }
            public Vector3 Position { get; }
            public Vector3 Forward { get; }
        }

        private readonly NetworkVariable<bool> blocking = new(
            false,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);
        private readonly List<PoseSample> history = new(48);
        private NetworkPlayer player;
        private PlayerSurvival survival;
        private PlayerInventory inventory;
        private uint observedAttackPressId;
        private uint observedAttackReleaseId;
        private uint observedDodgePressId;
        private double attackPressedAt;
        private double nextAttackAt;
        private double nextDodgeAt;
        private bool attackArmed;

        public bool IsBlocking => blocking.Value;

        private void Awake()
        {
            player = GetComponent<NetworkPlayer>();
            survival = GetComponent<PlayerSurvival>();
            inventory = GetComponent<PlayerInventory>();
        }

        private void FixedUpdate()
        {
            if (!IsSpawned || !IsServer || NetworkManager == null) return;
            history.Add(new PoseSample(
                NetworkManager.ServerTime.Time,
                transform.position,
                transform.forward));
            while (history.Count > 48) history.RemoveAt(0);
        }

        public void ServerProcessInput(PlayerInputFrame input)
        {
            if (!IsServer || NetworkManager == null || survival == null) return;
            var now = NetworkManager.ServerTime.Time;
            blocking.Value = input.BlockHeld
                && survival.CanPerformServerAction()
                && survival.ServerState.Physiology.AcuteStamina > 0.05f;

            if (input.AttackPressId != observedAttackPressId)
            {
                observedAttackPressId = input.AttackPressId;
                if (now >= nextAttackAt && survival.CanPerformServerAction())
                {
                    attackPressedAt = now;
                    attackArmed = true;
                }
            }

            if (input.AttackReleaseId != observedAttackReleaseId)
            {
                observedAttackReleaseId = input.AttackReleaseId;
                if (attackArmed)
                {
                    ResolveAttack(input, now);
                    attackArmed = false;
                }
            }

            if (input.DodgePressId != observedDodgePressId)
            {
                observedDodgePressId = input.DodgePressId;
                if (now >= nextDodgeAt && input.DodgeDirection.sqrMagnitude > 0.01f
                    && survival.ServerTrySpendStamina(0.2f))
                {
                    player.ServerApplyDodge(input.DodgeDirection, 9.5f);
                    nextDodgeAt = now + 0.75d;
                }
            }
        }

        private void ResolveAttack(PlayerInputFrame input, double now)
        {
            var heldSeconds = Math.Max(0d, now - attackPressedAt);
            var kind = heldSeconds >= 0.28d ? MeleeAttackKind.Heavy : MeleeAttackKind.Light;
            var charge = Mathf.InverseLerp(0.28f, 1.2f, (float)heldSeconds);
            var active = inventory.ServerActiveStack;
            var weapon = MeleeCombatRules.ResolveWeapon(active.IsEmpty ? (ushort)0 : active.ItemId);
            var stamina = weapon.StaminaCost * (kind == MeleeAttackKind.Heavy
                ? Mathf.Lerp(1.25f, 1.8f, charge)
                : 0.75f);
            if (!survival.ServerTrySpendStamina(stamina)) return;
            nextAttackAt = now + (kind == MeleeAttackKind.Heavy ? 0.9d : 0.42d);

            var requestedTime = Math.Clamp(
                input.SampledServerTime,
                now - 0.25d,
                now + 0.03d);
            SamplePose(requestedTime, out var attackerPosition, out var attackerForward);
            PlayerCombat best = null;
            var bestDistance = float.MaxValue;
            var all = FindObjectsByType<PlayerCombat>();
            foreach (var target in all)
            {
                if (target == this || !target.IsSpawned || !target.IsServer
                    || target.survival == null || target.survival.ServerIsDead)
                    continue;
                target.SamplePose(requestedTime, out var targetPosition, out _);
                if (!MeleeCombatRules.IsGeometricHit(
                        attackerPosition, attackerForward, targetPosition, weapon))
                    continue;
                var distance = Vector3.Distance(attackerPosition, targetPosition);
                if (distance >= bestDistance || IsOccluded(attackerPosition, targetPosition, target))
                    continue;
                best = target;
                bestDistance = distance;
            }

            if (best == null)
            {
                survival.ServerRegisterPractice(weapon.Skill, 0.5f, 0.15f, 0f, 0.8f);
                return;
            }

            best.SamplePose(requestedTime, out var bestPosition, out var bestForward);
            var incoming = (attackerPosition - bestPosition).normalized;
            var guarded = best.blocking.Value
                && Vector3.Dot(bestForward.normalized, incoming) > 0.35f
                && best.survival.ServerTrySpendStamina(0.14f + weapon.BaseImpact * 0.08f);
            var impact = MeleeCombatRules.ResolveImpact(weapon, kind, charge, guarded);
            var contamination = active.IsEmpty ? 0.18f : 1f - active.Cleanliness / 10000f;
            best.survival.ServerApplyDamage(
                MeleeCombatRules.ResolveRegion(observedAttackReleaseId),
                weapon.DamageKind,
                impact,
                contamination,
                weapon.DamageKind == DamageKind.Blunt && impact > 0.72f);
            survival.ServerRegisterPractice(weapon.Skill, 2f, impact, 1f, 0f);
            best.survival.ServerRegisterPractice(SkillId.Defence, 1f, impact, guarded ? 1f : 0.2f, 0f);
        }

        private bool IsOccluded(Vector3 from, Vector3 to, PlayerCombat target)
        {
            var start = from + Vector3.up * 1.15f;
            var end = to + Vector3.up * 1.05f;
            if (!Physics.Linecast(start, end, out var hit, Physics.DefaultRaycastLayers,
                    QueryTriggerInteraction.Ignore))
                return false;
            return hit.collider.GetComponentInParent<PlayerCombat>() != target;
        }

        private void SamplePose(double time, out Vector3 position, out Vector3 forward)
        {
            if (history.Count == 0)
            {
                position = transform.position;
                forward = transform.forward;
                return;
            }
            var selected = history[0];
            for (var index = 1; index < history.Count; index++)
            {
                if (history[index].Time > time) break;
                selected = history[index];
            }
            position = selected.Position;
            forward = selected.Forward;
        }
    }
}
