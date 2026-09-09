using System;
using System.Collections.Generic;
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

            var cremated = new CorpseState
            {
                DiedAtUtcTicks = died.Ticks,
                BiologicalContamination = 1f,
            };
            LivingWorldSimulation.Cremate(cremated);
            Assert.That(cremated.Stage, Is.EqualTo(CorpseDecayStage.Cremated));
            Assert.That(cremated.OrganicItemsDestroyed, Is.True);
            Assert.That(cremated.BiologicalContamination, Is.Zero);
        }

        [Test]
        public void DeadCharacterState_AlwaysOwnsPersistentCorpseRecord()
        {
            var state = new CharacterSurvivalState
            {
                CharacterId = "dead-character",
                Physiology = new PhysiologyState { LifeState = CharacterLifeState.Dead },
            };
            state.EnsureInitialized();
            Assert.That(state.Corpse, Is.Not.Null);
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
        public void ContractsRewardKeptPromisesAndBreachesCanMakeWorkersLeave()
        {
            var contract = new WorkerContractState
            {
                Active = true, Voluntary = true, DailyRationCalories = 1800f,
                PromisedSafety = 0.7f,
            };
            var relationship = new RelationshipState { Trust = 0.5f, Loyalty = 0.4f };
            Assert.That(LivingWorldSimulation.EvaluateContractDay(
                contract, relationship, 1800f, 0.8f, 0.9f),
                Is.EqualTo(WorkerResponseKind.Stay));
            Assert.That(contract.FulfilledContractGameSeconds,
                Is.EqualTo(LivingWorldSimulation.GameSecondsPerDay));
            Assert.That(relationship.Trust, Is.GreaterThan(0.5f));

            relationship.Resentment = 1f;
            for (var day = 0; day < 3; day++)
                LivingWorldSimulation.EvaluateContractDay(
                    contract, relationship, 0f, 0f, 0.99f);
            Assert.That(LivingWorldSimulation.EvaluateContractDay(
                contract, relationship, 0f, 0f, 0f),
                Is.EqualTo(WorkerResponseKind.Leave));
        }

        [Test]
        public void WorkerBookPriorities_EnableDisableAndSelectTheHighestJob()
        {
            var contract = new WorkerContractState
            {
                Active = true,
                AllowedJobs = new List<WorkerJobKind>
                {
                    WorkerJobKind.Mining,
                    WorkerJobKind.Foraging,
                },
            };
            contract.EnsureInitialized();
            Assert.That(LivingWorldSimulation.AdjustJobPriority(
                contract, WorkerJobKind.Foraging, 2), Is.EqualTo(3));
            Assert.That(LivingWorldSimulation.TrySelectPriorityJob(
                contract, WorkerJobKind.Mining, out var selected), Is.True);
            Assert.That(selected, Is.EqualTo(WorkerJobKind.Foraging));

            Assert.That(LivingWorldSimulation.AdjustJobPriority(
                contract, WorkerJobKind.Foraging, -3), Is.Zero);
            CollectionAssert.DoesNotContain(contract.AllowedJobs, WorkerJobKind.Foraging);
            Assert.That(LivingWorldSimulation.TrySelectPriorityJob(
                contract, WorkerJobKind.Foraging, out selected), Is.True);
            Assert.That(selected, Is.EqualTo(WorkerJobKind.Mining));
        }

        [Test]
        public void MissingPromisedPaymentCountsAsAContractBreach()
        {
            var contract = new WorkerContractState
            {
                Active = true,
                Voluntary = true,
                DailyRationCalories = 1800f,
                PromisedSafety = 0.6f,
                PaymentItemId = 25,
                PaymentQuantity = 2,
            };
            var relationship = new RelationshipState { Trust = 0.7f, Loyalty = 0.5f };

            LivingWorldSimulation.EvaluateContractDay(
                contract, relationship, 2000f, 0.9f, 0.99f,
                paymentDelivered: false);

            Assert.That(contract.ConsecutiveBreaches, Is.EqualTo(1));
            Assert.That(contract.FulfilledContractGameSeconds, Is.Zero);
            Assert.That(relationship.Trust, Is.LessThan(0.7f));
        }

        [Test]
        public void NpcDecisionsPutImmediateNeedsAheadOfScheduledWork()
        {
            var physiology = new PhysiologyState
            {
                Hydration = 0.4f,
                StomachFullness = 0.2f,
                EnergyReserve = 0.3f,
                SleepDebt = 0.9f,
            };
            Assert.That(LivingWorldSimulation.ChooseNpcActivity(
                physiology, false, false, false, true, true),
                Is.EqualTo(NpcActivityKind.SeekWater));
            physiology.Hydration = 0.9f;
            Assert.That(LivingWorldSimulation.ChooseNpcActivity(
                physiology, true, false, false, true, true),
                Is.EqualTo(NpcActivityKind.SeekFood));
            physiology.StomachFullness = 0.8f;
            physiology.EnergyReserve = 0.8f;
            Assert.That(LivingWorldSimulation.ChooseNpcActivity(
                physiology, true, true, false, true, true),
                Is.EqualTo(NpcActivityKind.Rest));
            physiology.SleepDebt = 0.1f;
            Assert.That(LivingWorldSimulation.ChooseNpcActivity(
                physiology, true, true, false, true, true),
                Is.EqualTo(NpcActivityKind.Work));
        }

        [Test]
        public void OvernightWorkSchedulesWrapAcrossMidnight()
        {
            Assert.That(LivingWorldSimulation.IsWithinWorkHours(23f, 20f, 4f), Is.True);
            Assert.That(LivingWorldSimulation.IsWithinWorkHours(2f, 20f, 4f), Is.True);
            Assert.That(LivingWorldSimulation.IsWithinWorkHours(12f, 20f, 4f), Is.False);
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
