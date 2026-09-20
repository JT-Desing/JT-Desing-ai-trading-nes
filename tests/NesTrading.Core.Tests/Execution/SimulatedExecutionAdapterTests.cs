using System;
using NUnit.Framework;
using NesTrading.Core.Domain;
using NesTrading.Core.Execution;

namespace NesTrading.Core.Tests.Execution
{
    [TestFixture]
    public class SimulatedExecutionAdapterTests
    {
        private static readonly DateTime Now = new DateTime(2026, 9, 21, 14, 0, 0, DateTimeKind.Utc);

        private static TradeCandidate Candidate(string signalId = "sig-exec-1", TradeDirection direction = TradeDirection.Long)
        {
            if (direction == TradeDirection.Long)
                return new TradeCandidate(signalId, "NES", direction, 6122m, 6118m, 6130m, 90, 1, Now);

            return new TradeCandidate(signalId, "NES", direction, 6122m, 6126m, 6114m, 90, 1, Now);
        }

        private static RiskDecision Approved(string signalId = "sig-exec-1", TradeDirection direction = TradeDirection.Long)
            => RiskDecision.Approve(Candidate(signalId, direction), 2.5m, 2.5m / 150m);

        [Test]
        public void SubmitBracket_ApprovedDecisionCreatesEntryStopAndTargetLinkedByOneTradeId()
        {
            var adapter = new SimulatedExecutionAdapter(SimulationExecutionSettings.Defaults());

            var result = adapter.SubmitBracket(Approved());

            Assert.That(result.Accepted, Is.True);
            Assert.That(result.TradeId, Is.Not.Null.And.Not.Empty);
            Assert.That(result.Orders.Count, Is.EqualTo(3));
            Assert.That(result.Orders[0].Role, Is.EqualTo(OrderRole.Entry));
            Assert.That(result.Orders[1].Role, Is.EqualTo(OrderRole.Stop));
            Assert.That(result.Orders[2].Role, Is.EqualTo(OrderRole.Target));
            Assert.That(result.Orders, Has.All.Property("TradeId").EqualTo(result.TradeId));
            Assert.That(result.Orders, Has.All.Property("SignalId").EqualTo("sig-exec-1"));
        }

        [Test]
        public void SubmitBracket_RejectedRiskDecisionNeverCreatesOrders()
        {
            var adapter = new SimulatedExecutionAdapter(SimulationExecutionSettings.Defaults());
            var rejected = RiskDecision.Reject("sig-exec-1", "TEST_REJECT", Candidate());

            var result = adapter.SubmitBracket(rejected);

            Assert.That(result.Accepted, Is.False);
            Assert.That(result.ReasonCode, Is.EqualTo("RISK_NOT_APPROVED"));
            Assert.That(result.Orders, Is.Empty);
            Assert.That(adapter.GetSnapshot().OpenTrades, Is.Empty);
        }

        [Test]
        public void SubmitBracket_DuplicateSignalCreatesOneTradeAndRejectsSecondSubmission()
        {
            var adapter = new SimulatedExecutionAdapter(SimulationExecutionSettings.Defaults());

            var first = adapter.SubmitBracket(Approved());
            var second = adapter.SubmitBracket(Approved());

            Assert.That(first.Accepted, Is.True);
            Assert.That(second.Accepted, Is.False);
            Assert.That(second.ReasonCode, Is.EqualTo("DUPLICATE_SIGNAL"));
            Assert.That(adapter.GetSnapshot().OpenTrades.Count, Is.EqualTo(1));
        }

        [Test]
        public void UnknownAck_PreventsBlindResubmissionUntilReconcile()
        {
            var settings = new SimulationExecutionSettings(
                entrySlippageTicks: 0,
                exitSlippageTicks: 0,
                roundTripFeesUsd: 0m,
                forceMissedFill: false,
                nextAckState: AckState.Unknown);

            var adapter = new SimulatedExecutionAdapter(settings);
            var first = adapter.SubmitBracket(Approved());

            Assert.That(first.Accepted, Is.False);
            Assert.That(first.ReasonCode, Is.EqualTo("ACK_UNKNOWN"));
            Assert.That(adapter.GetSnapshot().UnresolvedSignals, Does.Contain("sig-exec-1"));

            var retryBeforeReconcile = adapter.SubmitBracket(Approved());
            Assert.That(retryBeforeReconcile.Accepted, Is.False);
            Assert.That(retryBeforeReconcile.ReasonCode, Is.EqualTo("ACK_RECONCILIATION_REQUIRED"));

            var reconciliation = adapter.Reconcile("sig-exec-1", BrokerOrderPresence.Absent);
            Assert.That(reconciliation.Resolved, Is.True);
            Assert.That(reconciliation.MayRetry, Is.True);

            adapter.SetNextAckState(AckState.Accepted);
            var retryAfterReconcile = adapter.SubmitBracket(Approved());
            Assert.That(retryAfterReconcile.Accepted, Is.True);
        }

        [Test]
        public void ReconcileUnknownAck_WhenBrokerHasOrderMarksSignalAsConsumed()
        {
            var settings = new SimulationExecutionSettings(0, 0, 0m, false, AckState.Unknown);
            var adapter = new SimulatedExecutionAdapter(settings);

            adapter.SubmitBracket(Approved());
            var reconciliation = adapter.Reconcile("sig-exec-1", BrokerOrderPresence.Present);
            adapter.SetNextAckState(AckState.Accepted);
            var retry = adapter.SubmitBracket(Approved());

            Assert.That(reconciliation.Resolved, Is.True);
            Assert.That(reconciliation.MayRetry, Is.False);
            Assert.That(retry.Accepted, Is.False);
            Assert.That(retry.ReasonCode, Is.EqualTo("DUPLICATE_SIGNAL"));
        }

        [Test]
        public void ReconcilePresent_BlocksAnyDifferentSignalUntilExternalExposureIsResolved()
        {
            var settings = new SimulationExecutionSettings(0, 0, 0m, false, AckState.Unknown);
            var adapter = new SimulatedExecutionAdapter(settings);

            adapter.SubmitBracket(Approved("sig-unknown"));
            var reconciliation = adapter.Reconcile("sig-unknown", BrokerOrderPresence.Present);
            adapter.SetNextAckState(AckState.Accepted);

            var differentSignal = adapter.SubmitBracket(Approved("sig-new"));

            Assert.That(reconciliation.Resolved, Is.True);
            Assert.That(adapter.GetSnapshot().ExternalExposurePresent, Is.True);
            Assert.That(differentSignal.Accepted, Is.False);
            Assert.That(differentSignal.ReasonCode, Is.EqualTo("BROKER_EXPOSURE_PRESENT"));
        }

        [Test]
        public void ForcedMissedFill_DoesNotCreateOpenTrade()
        {
            var settings = new SimulationExecutionSettings(0, 0, 0m, true, AckState.Accepted);
            var adapter = new SimulatedExecutionAdapter(settings);

            var result = adapter.SubmitBracket(Approved());

            Assert.That(result.Accepted, Is.False);
            Assert.That(result.ReasonCode, Is.EqualTo("MISSED_FILL"));
            Assert.That(result.Orders.Count, Is.EqualTo(3));
            Assert.That(adapter.GetSnapshot().OpenTrades, Is.Empty);
        }

        [Test]
        public void EntrySlippageMovesLongEntryAgainstTraderByConfiguredTicks()
        {
            var settings = new SimulationExecutionSettings(2, 0, 0m, false, AckState.Accepted);
            var adapter = new SimulatedExecutionAdapter(settings);

            var result = adapter.SubmitBracket(Approved());

            Assert.That(result.Accepted, Is.True);
            Assert.That(result.EntryFillPrice, Is.EqualTo(6123.0m)); // NES tick size 0.50 x 2.
        }

        [Test]
        public void EntrySlippageMovesShortEntryAgainstTraderByConfiguredTicks()
        {
            var settings = new SimulationExecutionSettings(2, 0, 0m, false, AckState.Accepted);
            var adapter = new SimulatedExecutionAdapter(settings);

            var result = adapter.SubmitBracket(Approved(direction: TradeDirection.Short));

            Assert.That(result.Accepted, Is.True);
            Assert.That(result.EntryFillPrice, Is.EqualTo(6121.0m));
        }

        [Test]
        public void CloseAtTarget_AppliesExitSlippageAndRoundTripFees()
        {
            var settings = new SimulationExecutionSettings(0, 1, 0.50m, false, AckState.Accepted);
            var adapter = new SimulatedExecutionAdapter(settings);
            var opened = adapter.SubmitBracket(Approved());

            var closed = adapter.CloseAtTarget(opened.TradeId, Now.AddMinutes(3));

            // Long target 6130; one adverse NES tick => 6129.50.
            // 7.50 points * $0.50 = $3.75 gross - $0.50 fees = $3.25.
            Assert.That(closed.Closed, Is.True);
            Assert.That(closed.ExitFillPrice, Is.EqualTo(6129.50m));
            Assert.That(closed.NetPnlUsd, Is.EqualTo(3.25m));
            Assert.That(adapter.GetSnapshot().OpenTrades, Is.Empty);
        }

        [Test]
        public void CannotOpenSecondTradeWhileOneSimulatedTradeIsOpen()
        {
            var adapter = new SimulatedExecutionAdapter(SimulationExecutionSettings.Defaults());
            Assert.That(adapter.SubmitBracket(Approved("sig-1")).Accepted, Is.True);

            var second = adapter.SubmitBracket(Approved("sig-2"));

            Assert.That(second.Accepted, Is.False);
            Assert.That(second.ReasonCode, Is.EqualTo("POSITION_ALREADY_OPEN"));
        }

        [Test]
        public void SettingsRejectNegativeSlippageFeesOrTicks()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new SimulationExecutionSettings(-1, 0, 0m, false, AckState.Accepted));
            Assert.Throws<ArgumentOutOfRangeException>(() => new SimulationExecutionSettings(0, -1, 0m, false, AckState.Accepted));
            Assert.Throws<ArgumentOutOfRangeException>(() => new SimulationExecutionSettings(0, 0, -0.01m, false, AckState.Accepted));
        }
    }
}
