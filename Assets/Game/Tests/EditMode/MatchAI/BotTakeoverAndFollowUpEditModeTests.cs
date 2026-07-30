using System;
using System.Linq;
using ArknoNights.Match;
using NUnit.Framework;

namespace ArknoNights.MatchAI.Tests
{
    public sealed class BotTakeoverAndFollowUpEditModeTests
    {
        [Test]
        public void DeployFollowUp_UsesFinalSurvivor_NotConsumedPurchaseUnit()
        {
            var authority = MatchAITestData.CreateAuthority();
            authority.TryEnterPreparation(1, 0);
            Assert.That(authority.TryProjectForBot(
                "player-2",
                out var snapshot,
                out var diagnostic), Is.True, diagnostic);
            var acquisition = new MatchAcquisitionResult(
                "purchased-consumed",
                "existing-survivor",
                Array.Empty<MatchFusionStep>(),
                new[] { "purchased-consumed" },
                MatchUnitZone.Staging,
                1,
                1);

            var created = BotOperationAdapter.TryCreateDeployFollowUp(
                snapshot,
                acquisition,
                4,
                out var operation,
                out diagnostic);

            Assert.That(created, Is.True, diagnostic);
            var deploy = operation.Payload as DeployUnitCommand;
            Assert.That(deploy, Is.Not.Null);
            Assert.That(deploy.UnitId, Is.EqualTo("existing-survivor"));
            Assert.That(
                deploy.TargetFormation,
                Is.EqualTo(new MatchFormationPosition(5, 2)));
            Assert.That(
                operation.ActionId.ActionPart,
                Is.EqualTo(MatchBotActionPart.DeployFollowUp));
        }

        [Test]
        public void DeployFollowUp_FinalAlreadyDeployed_PerformsNoOperation()
        {
            var authority = MatchAITestData.CreateAuthority();
            authority.TryEnterPreparation(1, 0);
            authority.TryProjectForBot("player-2", out var snapshot, out _);
            var acquisition = new MatchAcquisitionResult(
                "unit",
                "unit",
                Array.Empty<MatchFusionStep>(),
                Array.Empty<string>(),
                MatchUnitZone.Deployed,
                0,
                1);

            var created = BotOperationAdapter.TryCreateDeployFollowUp(
                snapshot,
                acquisition,
                0,
                out var operation,
                out var diagnostic);

            Assert.That(created, Is.False);
            Assert.That(operation, Is.Null);
            Assert.That(
                diagnostic,
                Is.EqualTo("match.ai.adapter.deploy.alreadyDeployed"));
        }

        [Test]
        public void PreparationDisconnect_TakesOverAtNextPreparationEntry()
        {
            var controller = new BotController();
            var authority = MatchAITestData.CreateAuthority(
                controller,
                MatchAITestData.TwoHumanSeats());
            authority = MatchAITestData.Rebuild(
                authority,
                controller,
                MatchPhase.Preparation,
                5);

            var disconnected = controller.NotifyDisconnected(
                authority,
                "player-2");

            Assert.That(disconnected.Accepted, Is.True, disconnected.DiagnosticCode);
            Assert.That(
                controller.PendingTakeovers.Single().EffectivePreparationRound,
                Is.EqualTo(6));
            var grace = authority.ProjectBotControlSeats()
                .Single(seat => seat.PlayerId == "player-2");
            Assert.That(grace.ControllerKind, Is.EqualTo(MatchControllerKind.Human));
            Assert.That(
                grace.ConnectionState,
                Is.EqualTo(MatchConnectionState.DisconnectedGrace));

            authority = MatchAITestData.Rebuild(
                authority,
                controller,
                MatchPhase.Settlement,
                5);
            var entered = authority.TryEnterPreparation(6, 60000);

            Assert.That(entered.Accepted, Is.True, entered.DiagnosticCode);
            var active = authority.ProjectBotControlSeats()
                .Single(seat => seat.PlayerId == "player-2");
            Assert.That(
                active.ControllerKind,
                Is.EqualTo(MatchControllerKind.TakeoverBot));
            Assert.That(controller.PendingTakeovers, Is.Empty);
            Assert.That(
                controller.RuntimeStates.Single(state =>
                    state.PlayerId == "player-2").NextDecisionOrdinal,
                Is.EqualTo(1));
        }

        [Test]
        public void BattleDisconnect_LeavesFullRoundGrace_ThenTakesOver()
        {
            var controller = new BotController();
            var authority = MatchAITestData.CreateAuthority(
                controller,
                MatchAITestData.TwoHumanSeats());
            authority = MatchAITestData.Rebuild(
                authority,
                controller,
                MatchPhase.Battle,
                5);

            var disconnected = controller.NotifyDisconnected(
                authority,
                "player-2");

            Assert.That(disconnected.Accepted, Is.True, disconnected.DiagnosticCode);
            Assert.That(
                controller.PendingTakeovers.Single().EffectivePreparationRound,
                Is.EqualTo(7));

            authority = MatchAITestData.Rebuild(
                authority,
                controller,
                MatchPhase.Settlement,
                5);
            Assert.That(authority.TryEnterPreparation(6, 60000).Accepted, Is.True);
            Assert.That(
                authority.ProjectBotControlSeats()
                    .Single(seat => seat.PlayerId == "player-2")
                    .ControllerKind,
                Is.EqualTo(MatchControllerKind.Human));

            authority = MatchAITestData.Rebuild(
                authority,
                controller,
                MatchPhase.Settlement,
                6);
            Assert.That(authority.TryEnterPreparation(7, 120000).Accepted, Is.True);
            Assert.That(
                authority.ProjectBotControlSeats()
                    .Single(seat => seat.PlayerId == "player-2")
                    .ControllerKind,
                Is.EqualTo(MatchControllerKind.TakeoverBot));
        }

        [Test]
        public void PreparationQuit_IsImmediate_AndQuitterCannotReconnect()
        {
            var controller = new BotController();
            var authority = MatchAITestData.CreateAuthority(
                controller,
                MatchAITestData.TwoHumanSeats());
            authority.TryEnterPreparation(1, 0);

            var quit = controller.NotifyVoluntaryQuit(authority, "player-2");

            Assert.That(quit.Accepted, Is.True, quit.DiagnosticCode);
            var active = authority.ProjectBotControlSeats()
                .Single(seat => seat.PlayerId == "player-2");
            Assert.That(
                active.ControllerKind,
                Is.EqualTo(MatchControllerKind.TakeoverBot));
            Assert.That(
                controller.RuntimeStates.Single(state =>
                    state.PlayerId == "player-2").NextDecisionOrdinal,
                Is.EqualTo(1));
            var restored = controller.RestoreHumanControl(authority, "player-2");

            Assert.That(restored.Accepted, Is.False);
            var stillActive = authority.ProjectBotControlSeats()
                .Single(seat => seat.PlayerId == "player-2");
            Assert.That(
                stillActive.ControllerKind,
                Is.EqualTo(MatchControllerKind.TakeoverBot));
        }

        [Test]
        public void DisconnectReconnect_BeforeTakeoverCancelsPending()
        {
            var controller = new BotController();
            var authority = MatchAITestData.CreateAuthority(
                controller,
                MatchAITestData.TwoHumanSeats());
            authority.TryEnterPreparation(1, 0);
            Assert.That(
                controller.NotifyDisconnected(authority, "player-2").Accepted,
                Is.True);

            var restored = controller.RestoreHumanControl(authority, "player-2");

            Assert.That(restored.Accepted, Is.True, restored.DiagnosticCode);
            Assert.That(controller.PendingTakeovers, Is.Empty);
            var human = authority.ProjectBotControlSeats()
                .Single(seat => seat.PlayerId == "player-2");
            Assert.That(human.ControllerKind, Is.EqualTo(MatchControllerKind.Human));
            Assert.That(
                human.ConnectionState,
                Is.EqualTo(MatchConnectionState.Connected));
            Assert.That(human.Ready, Is.False);
        }

        [Test]
        public void DisconnectReconnect_AfterTakeoverPreservesAiStateAndRejectsDelayedAction()
        {
            var controller = new BotController();
            var authority = MatchAITestData.CreateAuthority(
                controller,
                MatchAITestData.TwoHumanSeats());
            authority = MatchAITestData.Rebuild(
                authority,
                controller,
                MatchPhase.Preparation,
                5);
            controller.NotifyDisconnected(authority, "player-2");
            authority = MatchAITestData.Rebuild(
                authority,
                controller,
                MatchPhase.Settlement,
                5);
            authority.TryEnterPreparation(6, 60000);
            var active = authority.ProjectBotControlSeats()
                .Single(seat => seat.PlayerId == "player-2");
            var ownerAfterAi = authority.ProjectForPlayer("player-2")
                .Owner.CanonicalSummary;
            var delayed = new MatchBotOperationEnvelope(
                new MatchBotActionId(
                    active.SessionId,
                    active.SeatIndex,
                    active.ControllerGeneration,
                    active.RoundNumber,
                    20,
                    MatchBotActionPart.Primary),
                active.PlayerId,
                new RefreshShopCommand());

            var restored = controller.RestoreHumanControl(authority, "player-2");
            var delayedResult = authority.ExecuteBotOperation(delayed);

            Assert.That(restored.Accepted, Is.True, restored.DiagnosticCode);
            var human = authority.ProjectBotControlSeats()
                .Single(seat => seat.PlayerId == "player-2");
            Assert.That(human.ControllerKind, Is.EqualTo(MatchControllerKind.Human));
            Assert.That(human.Ready, Is.False);
            Assert.That(
                authority.ProjectForPlayer("player-2").Owner.CanonicalSummary,
                Is.EqualTo(ownerAfterAi));
            Assert.That(delayedResult.Accepted, Is.False);
            Assert.That(delayedResult.ChangedState, Is.False);
        }

        [Test]
        public void BattleQuit_TakesOverAtNextPreparation_AndHostNeverDoes()
        {
            var controller = new BotController();
            var authority = MatchAITestData.CreateAuthority(
                controller,
                MatchAITestData.TwoHumanSeats());
            authority = MatchAITestData.Rebuild(
                authority,
                controller,
                MatchPhase.Battle,
                5);

            var hostQuit = controller.NotifyVoluntaryQuit(authority, "player-1");
            var playerQuit = controller.NotifyVoluntaryQuit(authority, "player-2");

            Assert.That(hostQuit.Accepted, Is.False);
            Assert.That(playerQuit.Accepted, Is.True, playerQuit.DiagnosticCode);
            Assert.That(
                controller.PendingTakeovers.Single().EffectivePreparationRound,
                Is.EqualTo(6));

            authority = MatchAITestData.Rebuild(
                authority,
                controller,
                MatchPhase.Settlement,
                5);
            Assert.That(authority.TryEnterPreparation(6, 60000).Accepted, Is.True);
            Assert.That(
                authority.ProjectBotControlSeats()
                    .Single(seat => seat.PlayerId == "player-2")
                    .ControllerKind,
                Is.EqualTo(MatchControllerKind.TakeoverBot));
            Assert.That(
                authority.ProjectBotControlSeats()
                    .Single(seat => seat.PlayerId == "player-1")
                    .ControllerKind,
                Is.EqualTo(MatchControllerKind.Human));
        }

        [Test]
        public void NativeBot_CannotBeClaimedAsHuman()
        {
            var controller = new BotController();
            var authority = MatchAITestData.CreateAuthority(controller);
            authority.TryEnterPreparation(1, 0);

            var restored = controller.RestoreHumanControl(authority, "player-2");

            Assert.That(restored.Accepted, Is.False);
            Assert.That(
                authority.ProjectBotControlSeats()
                    .Single(seat => seat.PlayerId == "player-2")
                    .ControllerKind,
                Is.EqualTo(MatchControllerKind.NativeBot));
        }
    }
}
