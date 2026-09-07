using System;
using NUnit.Framework;
using Quieter.Survival;
using UnityEngine;

namespace Quieter.Tests.EditMode
{
    public sealed class LivingWorldSimulationTests
    {
        [Test]
        public void HeirRequiresVoluntaryLoyaltyContractBedRequestsAndDeed()
        {
            var incomplete = new InheritanceEligibility(
                true, true, true,
                LivingWorldSimulation.RequiredHeirContractGameSeconds,
                3, true, false);
            Assert.That(LivingWorldSimulation.CanRegisterHeir(incomplete, out var error), Is.False);
            StringAssert.Contains("добровольная", error);

            var complete = new InheritanceEligibility(
                true, true, true,
                LivingWorldSimulation.RequiredHeirContractGameSeconds,
                3, true, true);
            Assert.That(LivingWorldSimulation.CanRegisterHeir(complete, out error), Is.True, error);
        }

        [Test]
        public void InheritanceTransfersRightsButHasNoInventoryPayload()
        {
            var estate = new EstateState
            {
                OwnerCharacterId = "dead",
                RegisteredHeirCharacterId = "heir",
                DoorLockIds = { "door" },
                ContainerLockIds = { "chest" },
                WorkerContractIds = { "contract" },
            };
            var transition = LivingWorldSimulation.ResolveOwnerDeath(estate, "dead", true);
            Assert.That(transition.Success, Is.True);
            Assert.That(transition.ControlledCharacterId, Is.EqualTo("heir"));
            Assert.That(transition.DoorLockIds, Is.EquivalentTo(new[] { "door" }));
            Assert.That(typeof(EstateTransition).GetProperty("Inventory"), Is.Null);
        }

        [Test]
        public void CaptureNeedsOneContinuousRealHourAndCasRevision()
        {
            var start = DateTime.UtcNow;
            var capture = new CaptureState { Revision = 4 };
            Assert.That(LivingWorldSimulation.AdvanceCapture(
                capture, start.Ticks, true, true, 4), Is.False);
            Assert.That(capture.Status, Is.EqualTo(CaptureStatus.ConditionsMet));

            Assert.That(LivingWorldSimulation.AdvanceCapture(
                capture, start.AddMinutes(59).Ticks, true, true, 5), Is.False);
            Assert.That(LivingWorldSimulation.AdvanceCapture(
                capture, start.AddHours(1).Ticks, true, true, 5), Is.True);
            Assert.That(capture.Status, Is.EqualTo(CaptureStatus.Completed));

            Assert.That(LivingWorldSimulation.AdvanceCapture(
                capture, start.AddHours(2).Ticks, true, true, 5), Is.False);
        }

        [Test]
        public void BrokenCaptureConditionsResetTheContinuousTimer()
        {
            var start = DateTime.UtcNow;
            var capture = new CaptureState { Revision = 1 };
            LivingWorldSimulation.AdvanceCapture(capture, start.Ticks, true, true, 1);
            Assert.That(LivingWorldSimulation.AdvanceCapture(
                capture, start.AddMinutes(30).Ticks, false, true, 2), Is.False);
            Assert.That(capture.Status, Is.EqualTo(CaptureStatus.Cancelled));
            Assert.That(capture.ContinuousConditionsStartedUtcTicks, Is.Zero);
        }

        [Test]
        public void RainCarriesPitContaminationDownhillAndBoilingLeavesToxins()
        {
            var pit = new SanitationNodeState
            {
                Kind = SanitationNodeKind.UnlinedPit,
                Position = new Vector3(0f, 10f, 0f),
                CapacityLiters = 100f,
            };
            LivingWorldSimulation.DepositWaste(pit, 140f, 1f);
            var well = new SanitationNodeState
            {
                Kind = SanitationNodeKind.Well,
                Position = Vector3.zero,
            };
            LivingWorldSimulation.TransferRainContamination(
                pit, well, 1f, (float)LivingWorldSimulation.GameSecondsPerDay);
            Assert.That(well.BiologicalLoad, Is.GreaterThan(0f));

            var boiled = LivingWorldSimulation.BoilLiquid(well.BiologicalLoad, 0.6f, true);
            Assert.That(boiled.Biological, Is.Zero);
            Assert.That(boiled.Toxins, Is.EqualTo(0.6f));
        }

        [Test]
        public void CorpsePersistsAsRemainsUntilBuriedOrCremated()
        {
            var died = DateTime.UtcNow;
            var corpse = new CorpseState { DiedAtUtcTicks = died.Ticks };
            LivingWorldSimulation.UpdateCorpse(
                corpse,
                died.AddHours(40).Ticks,
                20f);
            Assert.That(corpse.Stage, Is.EqualTo(CorpseDecayStage.DryRemains));

            LivingWorldSimulation.Bury(corpse);
            Assert.That(corpse.Stage, Is.EqualTo(CorpseDecayStage.Buried));
            Assert.That(corpse.ItemsSealedByBurial, Is.True);
        }

        [Test]
        public void WorkEvidenceNarrowsOnlyRelevantSkillEstimate()
        {
            var unseen = LivingWorldSimulation.RevealSkillRange(7, 0);
            var observed = LivingWorldSimulation.RevealSkillRange(7, 21);
            Assert.That(unseen, Is.EqualTo((0, 10)));
            Assert.That(observed, Is.EqualTo((7, 7)));
        }

        [Test]
        public void MapMergeCopiesWrittenKnowledgeToNewPhysicalItem()
        {
            var first = new MapDocumentState { MapId = "a", ItemInstanceId = 1 };
            first.Marks.Add(new MapMarkState
            {
                MarkId = "ore",
                Text = "железо",
                PositionalAccuracy = 0.9f,
            });
            var merged = LivingWorldSimulation.CopyAndMergeMaps(
                first, null, "copy", 99, 1f);
            Assert.That(merged.ItemInstanceId, Is.EqualTo(99));
            Assert.That(merged.Marks, Has.Count.EqualTo(1));
            Assert.That(merged.Marks[0].Text, Is.EqualTo("железо"));
        }
    }
}
