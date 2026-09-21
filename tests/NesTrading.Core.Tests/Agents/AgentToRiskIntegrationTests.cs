using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using NesTrading.Core.Agents;
using NesTrading.Core.Agents.Budget;
using NesTrading.Core.Agents.Market;
using NesTrading.Core.Agents.News;
using NesTrading.Core.Agents.Providers;
using NesTrading.Core.Agents.Trade;
using NesTrading.Core.Domain;
using NesTrading.Core.Risk;
using NesTrading.Core.Runtime;
using NesTrading.Core.Storage;
using NesTrading.Core.Strategy;

namespace NesTrading.Core.Tests.Agents
{
    [TestFixture]
    public class AgentToRiskIntegrationTests
    {
        // Monday 2026-09-21 10:00 ET = 14:00 UTC.
        private static readonly DateTime Now = new DateTime(2026, 9, 21, 14, 0, 0, DateTimeKind.Utc);

        [Test]
        public async Task AllowLongCycle_RiskEngineRemainsFinalAuthorityAndApprovesOneNes()
        {
            var fixture = BuildFixture(
                NewsCompletion(Now, NewsGate.Allow),
                TradeCompletion(Now, "LONG"));

            var cycle = await fixture.Coordinator.RefreshAsync(Input(Now), CancellationToken.None);

            Assert.That(cycle.Tradable, Is.True);
            Assert.That(cycle.Candidate, Is.Not.Null);
            Assert.That(cycle.Candidate.Contracts, Is.EqualTo(1));
            var originalStop = cycle.Candidate.StructuralStop;

            var snapshot = fixture.Cache.Read(Now);
            Assert.That(snapshot.News, Is.Not.Null);
            Assert.That(snapshot.Market, Is.Not.Null);
            Assert.That(snapshot.Trade, Is.Not.Null);
            Assert.That(snapshot.Trade.Payload.Confidence, Is.EqualTo(0.99m));
            Assert.That(cycle.Candidate.Contracts, Is.EqualTo(1), "AI confidence must not scale V1 size.");

            var risk = new RiskEngine(RiskPolicy.V1Defaults());
            var decision = risk.Evaluate(
                cycle.Candidate,
                new AccountRiskState(150m),
                new CostEstimate(0.25m, 0.25m, 1m),
                new TradingSessionState(),
                snapshot.News.Payload,
                snapshot.Market.Payload,
                snapshot.Trade.Payload,
                TradingStateMachine.Restore(TradingSystemState.TradingEnabled),
                Now);

            Assert.That(decision.IsApproved, Is.True);
            Assert.That(decision.Candidate.Contracts, Is.EqualTo(1));
            Assert.That(decision.Candidate.StructuralStop, Is.EqualTo(originalStop));
        }

        [Test]
        public async Task NewsBlock_StopsBeforeTradeProviderAndCannotProduceExecutableCandidate()
        {
            var fixture = BuildFixture(
                NewsCompletion(Now, NewsGate.Block),
                TradeCompletion(Now, "LONG"));

            var cycle = await fixture.Coordinator.RefreshAsync(Input(Now), CancellationToken.None);

            Assert.That(cycle.Tradable, Is.False);
            Assert.That(cycle.ReasonCode, Is.EqualTo("NEWS_BLOCK"));
            Assert.That(fixture.TradeProvider.CallCount, Is.EqualTo(0));
            Assert.That(fixture.Cache.Read(Now).Trade, Is.Null);
            Assert.That(cycle.Candidate, Is.Null);
        }

        [Test]
        public async Task StateExpiresAfterCycle_RiskEngineRejectsStaleNews()
        {
            var fixture = BuildFixture(
                NewsCompletion(Now, NewsGate.Allow, validSeconds: 20),
                TradeCompletion(Now, "LONG", validSeconds: 15));

            var cycle = await fixture.Coordinator.RefreshAsync(Input(Now), CancellationToken.None);
            Assert.That(cycle.Tradable, Is.True);

            // Use cycle state directly to prove RiskEngine performs its own freshness checks
            // instead of trusting that the coordinator was fresh at an earlier moment.
            var riskTime = Now.AddSeconds(21);
            var decision = new RiskEngine(RiskPolicy.V1Defaults()).Evaluate(
                cycle.Candidate,
                new AccountRiskState(150m),
                new CostEstimate(0.25m, 0.25m, 1m),
                new TradingSessionState(),
                cycle.News.Payload,
                cycle.Market.Payload,
                cycle.Trade.Payload,
                TradingStateMachine.Restore(TradingSystemState.TradingEnabled),
                riskTime);

            Assert.That(decision.IsApproved, Is.False);
            Assert.That(decision.ReasonCode, Is.EqualTo("STALE_NEWS_STATE"));
        }

        [Test]
        public async Task BothModelProvidersFail_MarketStillExistsButNoExecutableTradeStateExists()
        {
            var fixture = BuildFixture(
                ProviderCompletion.ForStatus(ProviderCompletionStatus.Failed, ""),
                ProviderCompletion.ForStatus(ProviderCompletionStatus.Failed, ""));

            var cycle = await fixture.Coordinator.RefreshAsync(Input(Now), CancellationToken.None);

            Assert.That(cycle.Tradable, Is.False);
            Assert.That(cycle.ReasonCode, Is.EqualTo("PROVIDER_FAILED"));
            Assert.That(cycle.Market, Is.Not.Null);
            Assert.That(fixture.Cache.Read(Now).Market, Is.Not.Null);
            Assert.That(fixture.Cache.Read(Now).Trade, Is.Null);
            Assert.That(fixture.TradeProvider.CallCount, Is.EqualTo(0),
                "Trade provider must not run when mandatory News state failed.");
        }

        [Test]
        public async Task ExhaustedBudget_PreventsSecondAgentCycleFromCreatingFreshModelState()
        {
            var policy = new AiUsagePolicy(
                maxCallsPerSession: 2,
                maxCallsPerAgentPerSession: 1,
                maxInputTokensPerSession: 100000,
                maxOutputTokensPerSession: 10000,
                maxCostUsdPerSession: 20m,
                estimatedInputCostPerThousandTokensUsd: 0m,
                estimatedOutputCostPerThousandTokensUsd: 0m);
            var budget = new AiUsageBudget(policy);
            var fixture = BuildFixture(
                NewsCompletion(Now, NewsGate.Allow),
                TradeCompletion(Now, "LONG"),
                budget);

            var first = await fixture.Coordinator.RefreshAsync(Input(Now), CancellationToken.None);
            Assert.That(first.Tradable, Is.True);
            Assert.That(fixture.NewsProvider.CallCount, Is.EqualTo(1));
            Assert.That(fixture.TradeProvider.CallCount, Is.EqualTo(1));
            var originalNewsGenerated = fixture.Cache.Read(Now).News.GeneratedAtUtc;

            var second = await fixture.Coordinator.RefreshAsync(Input(Now.AddSeconds(1)), CancellationToken.None);

            Assert.That(second.Tradable, Is.False);
            Assert.That(second.ReasonCode, Is.EqualTo("BUDGET_EXHAUSTED"));
            Assert.That(fixture.NewsProvider.CallCount, Is.EqualTo(1));
            Assert.That(fixture.TradeProvider.CallCount, Is.EqualTo(1));
            Assert.That(fixture.Cache.Read(Now.AddSeconds(1)).News.GeneratedAtUtc, Is.EqualTo(originalNewsGenerated));
        }

        [Test]
        public async Task OppositeTradeModelDirection_NeverReachesRiskApproval()
        {
            var fixture = BuildFixture(
                NewsCompletion(Now, NewsGate.Allow),
                TradeCompletion(Now, "SHORT"));

            var cycle = await fixture.Coordinator.RefreshAsync(Input(Now), CancellationToken.None);

            Assert.That(cycle.Tradable, Is.False);
            Assert.That(cycle.ReasonCode, Is.EqualTo("TRADE_DIRECTION_CONTRADICTION"));
            Assert.That(fixture.Cache.Read(Now).Trade, Is.Null);
        }

        private static Fixture BuildFixture(
            ProviderCompletion newsCompletion,
            ProviderCompletion tradeCompletion,
            AiUsageBudget budget = null)
        {
            var cache = new AgentStateCache();
            var store = new MemoryStore();
            var strategy = new TrendPullbackStrategy(StrategyParameters.V1Defaults());
            var newsProvider = FakeAgentTextProvider.Return(newsCompletion);
            var tradeProvider = FakeAgentTextProvider.Return(tradeCompletion);
            budget = budget ?? new AiUsageBudget(new AiUsagePolicy(
                20, 10, 100000, 10000, 20m, 0m, 0m));

            var coordinator = new AgentCoordinator(
                "integration-session",
                new MarketAgent(strategy),
                new NewsAgent(newsProvider, cache, budget, "news-model", TimeSpan.FromSeconds(2), 300),
                new TradeAgent(tradeProvider, cache, budget, "trade-model", TimeSpan.FromSeconds(2), 200),
                strategy,
                cache,
                store);

            return new Fixture(coordinator, cache, store, newsProvider, tradeProvider);
        }

        private static AgentCycleInput Input(DateTime now) =>
            new AgentCycleInput(
                new MarketSnapshot(
                    6122m, 6120.5m, 6121m, 6118m, 0.45m, 6m,
                    1200m, 1000m, 6124m, 6118m,
                    true, true, false, false, 1m,
                    false, true, true, true, now),
                "No scheduled high-impact event nearby. No material breaking news.",
                new AccountRiskState(150m),
                new TradingSessionState(),
                true,
                now);

        private static ProviderCompletion NewsCompletion(
            DateTime now,
            NewsGate gate,
            int validSeconds = 30)
        {
            var state = gate == NewsGate.Allow ? "ALLOW" :
                gate == NewsGate.Caution ? "CAUTION" : "BLOCK";
            var json = "{" +
                "\"schema_version\":\"news.v1\"," +
                "\"symbol\":\"NES\"," +
                "\"state\":\"" + state + "\"," +
                "\"directional_bias\":\"NEUTRAL\"," +
                "\"importance\":\"LOW\"," +
                "\"event_code\":\"NONE\"," +
                "\"generated_at_utc\":\"" + now.ToString("O") + "\"," +
                "\"valid_until_utc\":\"" + now.AddSeconds(validSeconds).ToString("O") + "\"," +
                "\"confidence\":0.90," +
                "\"reason_codes\":[\"INTEGRATION_TEST\"]" +
                "}";
            return ProviderCompletion.Completed(
                "fake", "news-model", json, "news-req",
                100, 20, 0.001m, now.AddMilliseconds(-5), now);
        }

        private static ProviderCompletion TradeCompletion(
            DateTime now,
            string action,
            int validSeconds = 15)
        {
            var json = "{" +
                "\"schema_version\":\"trade.v1\"," +
                "\"symbol\":\"NES\"," +
                "\"decision\":\"" + action + "\"," +
                "\"confidence\":0.99," +
                "\"generated_at_utc\":\"" + now.ToString("O") + "\"," +
                "\"valid_until_utc\":\"" + now.AddSeconds(validSeconds).ToString("O") + "\"," +
                "\"reason_codes\":[\"INTEGRATION_TEST\"]" +
                "}";
            return ProviderCompletion.Completed(
                "fake", "trade-model", json, "trade-req",
                100, 20, 0.001m, now.AddMilliseconds(-5), now);
        }

        private sealed class Fixture
        {
            public Fixture(
                AgentCoordinator coordinator,
                AgentStateCache cache,
                MemoryStore store,
                FakeAgentTextProvider newsProvider,
                FakeAgentTextProvider tradeProvider)
            {
                Coordinator = coordinator;
                Cache = cache;
                Store = store;
                NewsProvider = newsProvider;
                TradeProvider = tradeProvider;
            }

            public AgentCoordinator Coordinator { get; }
            public AgentStateCache Cache { get; }
            public MemoryStore Store { get; }
            public FakeAgentTextProvider NewsProvider { get; }
            public FakeAgentTextProvider TradeProvider { get; }
        }

        private sealed class MemoryStore : IEventStore
        {
            private readonly List<TradingEvent> _events = new List<TradingEvent>();

            public void Append(TradingEvent evt) => _events.Add(evt);

            public IReadOnlyList<TradingEvent> LoadSession(string sessionId) =>
                _events.Where(x => x.SessionId == sessionId)
                    .OrderBy(x => x.TimestampUtc)
                    .ToArray();

            public SessionAuditSnapshot Reconstruct(string sessionId) =>
                new SessionAuditSnapshot(0, 0, false, null, 0m);
        }
    }
}
