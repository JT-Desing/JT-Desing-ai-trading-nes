using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using NesTrading.Core.Domain;
using NesTrading.Core.Execution;
using NesTrading.Core.Risk;
using NesTrading.Core.Runtime;
using NesTrading.Core.Storage;
using NesTrading.Core.Strategy;

namespace NesTrading.Core.Tests.Runtime
{
    [TestFixture]
    public class PaperSessionRunnerTests
    {
        private static readonly DateTime Now = new DateTime(2026, 9, 21, 14, 0, 0, DateTimeKind.Utc);

        private static MarketSnapshot BullishSnapshot() =>
            new MarketSnapshot(
                6122m, 6120.5m, 6121m, 6118m, 0.45m, 6m,
                1200m, 1000m, 6124m, 6118m,
                true, true, false, false, 1m,
                false, true, true, true, Now);

        private static NewsState News(NewsGate gate) =>
            new NewsState("NES", gate, Now.AddSeconds(-5), Now.AddMinutes(1), 0.9m, new[] { "TEST" });

        private static PaperSessionRunner CreateRunner(IEventStore store, IExecutionAdapter execution = null)
        {
            return new PaperSessionRunner(
                "session-1",
                new TrendPullbackStrategy(StrategyParameters.V1Defaults()),
                new RiskEngine(RiskPolicy.V1Defaults()),
                execution ?? new SimulatedExecutionAdapter(new SimulationExecutionSettings(0, 0, 0.50m, false, AckState.Accepted)),
                store,
                new AccountRiskState(150m),
                new CostEstimate(0.25m, 0.25m, 1m));
        }

        [Test]
        public void ProfitableSession_PersistsCandidateApprovalBracketCloseAndFinishesFlat()
        {
            var store = new InMemoryEventStore();
            var runner = CreateRunner(store);

            var result = runner.Process(new PaperStepInput(BullishSnapshot(), News(NewsGate.Allow), closeAtTarget: true, outcomeTimeUtc: Now.AddMinutes(3)));

            Assert.That(result.RiskDecision.IsApproved, Is.True);
            Assert.That(result.Execution.Accepted, Is.True);
            Assert.That(result.Execution.Orders.Count, Is.EqualTo(3));
            Assert.That(result.TradeResult, Is.Not.Null);
            Assert.That(result.TradeResult.NetPnlUsd, Is.GreaterThan(0m));
            Assert.That(result.FinalSystemState, Is.EqualTo(TradingSystemState.Flat));

            var types = store.Events.Select(x => x.Type).ToArray();
            Assert.That(types, Does.Contain("CANDIDATE_EVALUATED"));
            Assert.That(types, Does.Contain("RISK_APPROVED"));
            Assert.That(types, Does.Contain("EXECUTION_ACCEPTED"));
            Assert.That(types, Does.Contain("TRADE_OPENED"));
            Assert.That(types, Does.Contain("TRADE_CLOSED"));

            var audit = store.Reconstruct("session-1");
            Assert.That(audit.TradesToday, Is.EqualTo(1));
            Assert.That(audit.HasOpenPosition, Is.False);
            Assert.That(audit.DailyPnlUsd, Is.EqualTo(result.TradeResult.NetPnlUsd));
        }

        [Test]
        public void NewsBlock_RecordsTechnicalCandidateAndRiskRejectionButNeverExecutes()
        {
            var store = new InMemoryEventStore();
            var spy = new CountingExecutionAdapter();
            var runner = CreateRunner(store, spy);

            var result = runner.Process(new PaperStepInput(BullishSnapshot(), News(NewsGate.Block), false, null));

            Assert.That(result.RiskDecision.IsApproved, Is.False);
            Assert.That(result.RiskDecision.ReasonCode, Is.EqualTo("NEWS_BLOCK"));
            Assert.That(spy.SubmitCalls, Is.EqualTo(0));
            Assert.That(store.Events.Count(x => x.Type == "CANDIDATE_EVALUATED"), Is.EqualTo(1));
            Assert.That(store.Events.Count(x => x.Type == "RISK_REJECTED"), Is.EqualTo(1));
            Assert.That(store.Events.Count(x => x.Type == "EXECUTION_ACCEPTED"), Is.EqualTo(0));
        }

        [Test]
        public void AuditFailureWhilePersistingApproval_FailsClosedAndNeverCallsExecution()
        {
            var store = new InMemoryEventStore { ThrowOnEventType = "RISK_APPROVED" };
            var spy = new CountingExecutionAdapter();
            var runner = CreateRunner(store, spy);

            var result = runner.Process(new PaperStepInput(BullishSnapshot(), News(NewsGate.Allow), false, null));

            Assert.That(result.RiskDecision.IsApproved, Is.False);
            Assert.That(result.RiskDecision.ReasonCode, Is.EqualTo("AUDIT_PERSISTENCE_FAILURE"));
            Assert.That(spy.SubmitCalls, Is.EqualTo(0));
            Assert.That(result.FinalSystemState, Is.EqualTo(TradingSystemState.RiskLock));
        }

        [Test]
        public void AuditFailureWhilePersistingCandidate_FailsClosedBeforeRiskOrExecution()
        {
            var store = new InMemoryEventStore { ThrowOnEventType = "CANDIDATE_EVALUATED" };
            var spy = new CountingExecutionAdapter();
            var runner = CreateRunner(store, spy);

            var result = runner.Process(new PaperStepInput(BullishSnapshot(), News(NewsGate.Allow), false, null));

            Assert.That(result.RiskDecision.IsApproved, Is.False);
            Assert.That(result.RiskDecision.ReasonCode, Is.EqualTo("AUDIT_PERSISTENCE_FAILURE"));
            Assert.That(spy.SubmitCalls, Is.EqualTo(0));
            Assert.That(result.FinalSystemState, Is.EqualTo(TradingSystemState.RiskLock));
        }

        [Test]
        public void NoTechnicalSetup_ReturnsNoTradeWithoutCallingExecution()
        {
            var store = new InMemoryEventStore();
            var spy = new CountingExecutionAdapter();
            var runner = CreateRunner(store, spy);
            var range = new MarketSnapshot(
                6122m, 6122.05m, 6122.10m, 6122.15m, 0.01m, 5m,
                1000m, 1000m, 6124m, 6120m,
                false, false, false, false, 1m,
                true, true, true, true, Now);

            var result = runner.Process(new PaperStepInput(range, News(NewsGate.Allow), false, null));

            Assert.That(result.HasCandidate, Is.False);
            Assert.That(spy.SubmitCalls, Is.EqualTo(0));
            Assert.That(store.Events.Any(x => x.Type == "NO_TECHNICAL_SETUP"), Is.True);
        }

        [Test]
        public void ExecutionReject_DoesNotCreateTradeOpenedEvent()
        {
            var store = new InMemoryEventStore();
            var execution = new RejectingExecutionAdapter();
            var runner = CreateRunner(store, execution);

            var result = runner.Process(new PaperStepInput(BullishSnapshot(), News(NewsGate.Allow), false, null));

            Assert.That(result.RiskDecision.IsApproved, Is.True);
            Assert.That(result.Execution.Accepted, Is.False);
            Assert.That(store.Events.Any(x => x.Type == "TRADE_OPENED"), Is.False);
            Assert.That(store.Events.Any(x => x.Type == "EXECUTION_REJECTED"), Is.True);
        }

        private sealed class InMemoryEventStore : IEventStore
        {
            public readonly List<TradingEvent> Events = new List<TradingEvent>();
            public string ThrowOnEventType { get; set; }

            public void Append(TradingEvent evt)
            {
                if (evt.Type == ThrowOnEventType)
                    throw new AuditIntegrityException("forced storage failure");
                if (Events.Any(x => x.EventId == evt.EventId))
                    throw new AuditIntegrityException("duplicate event");
                Events.Add(evt);
            }

            public IReadOnlyList<TradingEvent> LoadSession(string sessionId) =>
                Events.Where(x => x.SessionId == sessionId).OrderBy(x => x.TimestampUtc).ToArray();

            public SessionAuditSnapshot Reconstruct(string sessionId)
            {
                var events = LoadSession(sessionId);
                var trades = events.Count(x => x.Type == "TRADE_OPENED");
                var closes = events.Where(x => x.Type == "TRADE_CLOSED").ToArray();
                var losses = 0;
                var pnl = 0m;
                DateTime? lastClose = null;

                foreach (var close in closes)
                {
                    var token = Newtonsoft.Json.Linq.JObject.Parse(close.PayloadJson)["netPnlUsd"];
                    var value = token == null ? 0m : token.ToObject<decimal>();
                    pnl += value;
                    if (value < 0m) losses++;
                    lastClose = close.TimestampUtc;
                }

                return new SessionAuditSnapshot(trades, losses, trades > closes.Length, lastClose, pnl);
            }
        }

        private sealed class CountingExecutionAdapter : IExecutionAdapter
        {
            public int SubmitCalls { get; private set; }

            public ExecutionResult SubmitBracket(RiskDecision approvedDecision)
            {
                SubmitCalls++;
                return ExecutionResult.Reject("SPY_REJECT");
            }

            public BrokerSnapshot GetSnapshot() => new BrokerSnapshot(Array.Empty<SimulatedOpenTrade>(), Array.Empty<string>());
            public ReconciliationResult Reconcile(string signalId, BrokerOrderPresence presence) => new ReconciliationResult(false, false, "NOT_USED");
            public CloseResult CloseAtTarget(string tradeId, DateTime closedAtUtc) => new CloseResult(false, "NOT_USED", null, 0m);
        }

        private sealed class RejectingExecutionAdapter : IExecutionAdapter
        {
            public ExecutionResult SubmitBracket(RiskDecision approvedDecision) => ExecutionResult.Reject("BROKER_REJECTED");
            public BrokerSnapshot GetSnapshot() => new BrokerSnapshot(Array.Empty<SimulatedOpenTrade>(), Array.Empty<string>());
            public ReconciliationResult Reconcile(string signalId, BrokerOrderPresence presence) => new ReconciliationResult(false, false, "NOT_USED");
            public CloseResult CloseAtTarget(string tradeId, DateTime closedAtUtc) => new CloseResult(false, "NOT_USED", null, 0m);
        }
    }
}
