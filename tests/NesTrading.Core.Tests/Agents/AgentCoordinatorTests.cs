using System;
using System.Collections.Generic;
using System.IO;
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
    public class AgentCoordinatorTests
    {
        [Test]
        public async Task SuccessfulCycle_PersistsOrderedCorrelatedEventsAndPublishesAllStates()
        {
            var now = DateTime.UtcNow;
            var store = new MemoryStore();
            var cache = new AgentStateCache();
            var newsProvider = FakeAgentTextProvider.Return(NewsCompletion(now, NewsGate.Allow));
            var tradeProvider = FakeAgentTextProvider.Return(TradeCompletion(now, "LONG"));
            var coordinator = Coordinator(store, cache, newsProvider, tradeProvider);

            var result = await coordinator.RefreshAsync(Input(now), CancellationToken.None);

            Assert.That(result.Tradable, Is.True);
            Assert.That(result.ReasonCode, Is.EqualTo("AGENT_CYCLE_READY"));
            Assert.That(result.CycleId, Is.Not.Null.And.Not.Empty);
            Assert.That(result.Candidate, Is.Not.Null);
            Assert.That(result.Trade.Payload.Action, Is.EqualTo(TradeAction.Long));

            var types = store.Events.Select(x => x.Type).ToArray();
            Assert.That(types, Is.EqualTo(new[]
            {
                "MARKET_AGENT_STATE",
                "NEWS_AGENT_REQUESTED",
                "NEWS_AGENT_STATE",
                "TRADE_AGENT_REQUESTED",
                "TRADE_AGENT_STATE",
                "AGENT_CYCLE_COMPLETE"
            }));

            foreach (var evt in store.Events)
                Assert.That(evt.PayloadJson, Does.Contain(result.CycleId));

            var snapshot = cache.Read(now);
            Assert.That(snapshot.Market, Is.Not.Null);
            Assert.That(snapshot.News, Is.Not.Null);
            Assert.That(snapshot.Trade, Is.Not.Null);
            Assert.That(newsProvider.CallCount, Is.EqualTo(1));
            Assert.That(tradeProvider.CallCount, Is.EqualTo(1));
        }

        [Test]
        public async Task NewsFailure_DoesNotInvokeTradeAndCycleIsNonTradable()
        {
            var now = DateTime.UtcNow;
            var store = new MemoryStore();
            var cache = new AgentStateCache();
            var newsProvider = FakeAgentTextProvider.Return(
                ProviderCompletion.ForStatus(ProviderCompletionStatus.TimedOut, ""));
            var tradeProvider = FakeAgentTextProvider.Return(TradeCompletion(now, "LONG"));
            var coordinator = Coordinator(store, cache, newsProvider, tradeProvider);

            var result = await coordinator.RefreshAsync(Input(now), CancellationToken.None);

            Assert.That(result.Tradable, Is.False);
            Assert.That(result.ReasonCode, Is.EqualTo("PROVIDER_TIMED_OUT"));
            Assert.That(tradeProvider.CallCount, Is.EqualTo(0));
            Assert.That(cache.Read(now).Trade, Is.Null);
            Assert.That(store.Events.Select(x => x.Type), Does.Contain("NEWS_AGENT_FAILED"));
            Assert.That(store.Events.Select(x => x.Type), Does.Not.Contain("TRADE_AGENT_REQUESTED"));
        }

        [Test]
        public async Task TradeFailure_LeavesMarketAndNewsButNoTradeState()
        {
            var now = DateTime.UtcNow;
            var store = new MemoryStore();
            var cache = new AgentStateCache();
            var newsProvider = FakeAgentTextProvider.Return(NewsCompletion(now, NewsGate.Allow));
            var tradeProvider = FakeAgentTextProvider.Return(
                ProviderCompletion.ForStatus(ProviderCompletionStatus.Failed, ""));
            var coordinator = Coordinator(store, cache, newsProvider, tradeProvider);

            var result = await coordinator.RefreshAsync(Input(now), CancellationToken.None);

            Assert.That(result.Tradable, Is.False);
            Assert.That(result.ReasonCode, Is.EqualTo("PROVIDER_FAILED"));
            Assert.That(cache.Read(now).Market, Is.Not.Null);
            Assert.That(cache.Read(now).News, Is.Not.Null);
            Assert.That(cache.Read(now).Trade, Is.Null);
            Assert.That(store.Events.Select(x => x.Type), Does.Contain("TRADE_AGENT_FAILED"));
        }

        [Test]
        public async Task AuditFailureBeforeNewsPublish_DoesNotPublishNewsOrInvokeTrade()
        {
            var now = DateTime.UtcNow;
            var store = new ThrowingStore("NEWS_AGENT_STATE");
            var cache = new AgentStateCache();
            var newsProvider = FakeAgentTextProvider.Return(NewsCompletion(now, NewsGate.Allow));
            var tradeProvider = FakeAgentTextProvider.Return(TradeCompletion(now, "LONG"));
            var coordinator = Coordinator(store, cache, newsProvider, tradeProvider);

            var result = await coordinator.RefreshAsync(Input(now), CancellationToken.None);

            Assert.That(result.Tradable, Is.False);
            Assert.That(result.ReasonCode, Is.EqualTo("AUDIT_PERSISTENCE_FAILURE"));
            Assert.That(cache.Read(now).News, Is.Null);
            Assert.That(cache.Read(now).Trade, Is.Null);
            Assert.That(tradeProvider.CallCount, Is.EqualTo(0));
        }

        [Test]
        public async Task NewsBlock_IsPersistedButTradeProviderIsNotCalled()
        {
            var now = DateTime.UtcNow;
            var store = new MemoryStore();
            var cache = new AgentStateCache();
            var newsProvider = FakeAgentTextProvider.Return(NewsCompletion(now, NewsGate.Block));
            var tradeProvider = FakeAgentTextProvider.Return(TradeCompletion(now, "LONG"));
            var coordinator = Coordinator(store, cache, newsProvider, tradeProvider);

            var result = await coordinator.RefreshAsync(Input(now), CancellationToken.None);

            Assert.That(result.Tradable, Is.False);
            Assert.That(result.ReasonCode, Is.EqualTo("NEWS_BLOCK"));
            Assert.That(cache.Read(now).News.Payload.State, Is.EqualTo(NewsGate.Block));
            Assert.That(tradeProvider.CallCount, Is.EqualTo(0));
        }

        [Test]
        public void SqliteAgentDecisionSchema_ContainsRequiredAgentMetadataColumns()
        {
            var path = Path.Combine(Path.GetTempPath(), "nes-agent-schema-" + Guid.NewGuid().ToString("N") + ".db");
            try
            {
                using (var store = new SqliteAuditStore(path))
                {
                    var columns = store.GetColumnNames("agent_decisions");
                    foreach (var required in new[]
                    {
                        "cycle_id","agent_name","schema_version","prompt_version","provider_name","model_name",
                        "generated_at_utc","valid_until_utc","status","reason_code","raw_response_hash",
                        "input_tokens","output_tokens","reported_cost_usd"
                    })
                        Assert.That(columns, Does.Contain(required), "Missing " + required);
                }
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }

        private static AgentCoordinator Coordinator(
            IEventStore store,
            AgentStateCache cache,
            IAgentTextProvider newsProvider,
            IAgentTextProvider tradeProvider)
        {
            var strategy = new TrendPullbackStrategy(StrategyParameters.V1Defaults());
            var budget = new AiUsageBudget(new AiUsagePolicy(50, 25, 100000, 10000, 20m, 0m, 0m));
            return new AgentCoordinator(
                "session-agents",
                new MarketAgent(strategy),
                new NewsAgent(newsProvider, cache, budget, "news-model", TimeSpan.FromSeconds(2), 300),
                new TradeAgent(tradeProvider, cache, budget, "trade-model", TimeSpan.FromSeconds(2), 200),
                strategy,
                cache,
                store);
        }

        private static AgentCycleInput Input(DateTime now) =>
            new AgentCycleInput(
                Bullish(now),
                "No scheduled high-impact event. No material breaking news.",
                new AccountRiskState(150m),
                new TradingSessionState(),
                true,
                now);

        private static MarketSnapshot Bullish(DateTime now) =>
            new MarketSnapshot(
                6122m, 6120.5m, 6121m, 6118m, 0.45m, 6m,
                1200m, 1000m, 6124m, 6118m,
                true, true, false, false, 1m,
                false, true, true, true, now);

        private static ProviderCompletion NewsCompletion(DateTime now, NewsGate gate)
        {
            var state = gate == NewsGate.Allow ? "ALLOW" : gate == NewsGate.Caution ? "CAUTION" : "BLOCK";
            var json = "{" +
                "\"schema_version\":\"news.v1\"," +
                "\"symbol\":\"NES\"," +
                "\"state\":\"" + state + "\"," +
                "\"directional_bias\":\"NEUTRAL\"," +
                "\"importance\":\"LOW\"," +
                "\"event_code\":\"NONE\"," +
                "\"generated_at_utc\":\"" + now.ToString("O") + "\"," +
                "\"valid_until_utc\":\"" + now.AddSeconds(30).ToString("O") + "\"," +
                "\"confidence\":0.9," +
                "\"reason_codes\":[\"TEST\"]" +
                "}";
            return ProviderCompletion.Completed("fake","news-model",json,"news-req",100,20,0.001m,now.AddMilliseconds(-5),now);
        }

        private static ProviderCompletion TradeCompletion(DateTime now, string action)
        {
            var json = "{" +
                "\"schema_version\":\"trade.v1\"," +
                "\"symbol\":\"NES\"," +
                "\"decision\":\"" + action + "\"," +
                "\"confidence\":0.85," +
                "\"generated_at_utc\":\"" + now.ToString("O") + "\"," +
                "\"valid_until_utc\":\"" + now.AddSeconds(15).ToString("O") + "\"," +
                "\"reason_codes\":[\"TEST\"]" +
                "}";
            return ProviderCompletion.Completed("fake","trade-model",json,"trade-req",100,20,0.001m,now.AddMilliseconds(-5),now);
        }

        private class MemoryStore : IEventStore
        {
            public readonly List<TradingEvent> Events = new List<TradingEvent>();

            public virtual void Append(TradingEvent evt)
            {
                if (evt == null) throw new ArgumentNullException(nameof(evt));
                Events.Add(evt);
            }

            public IReadOnlyList<TradingEvent> LoadSession(string sessionId) =>
                Events.Where(x => x.SessionId == sessionId).OrderBy(x => x.TimestampUtc).ToArray();

            public SessionAuditSnapshot Reconstruct(string sessionId) =>
                new SessionAuditSnapshot(0, 0, false, null, 0m);
        }

        private sealed class ThrowingStore : MemoryStore
        {
            private readonly string _type;
            public ThrowingStore(string type) { _type = type; }

            public override void Append(TradingEvent evt)
            {
                if (evt.Type == _type) throw new AuditIntegrityException("Injected failure.");
                base.Append(evt);
            }
        }
    }
}
