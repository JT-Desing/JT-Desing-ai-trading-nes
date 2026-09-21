using System;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using NesTrading.Core.Agents;
using NesTrading.Core.Agents.Budget;
using NesTrading.Core.Agents.Providers;
using NesTrading.Core.Agents.Trade;
using NesTrading.Core.Domain;
using NesTrading.Core.Risk;
using NesTrading.Core.Runtime;

namespace NesTrading.Core.Tests.Agents
{
    [TestFixture]
    public class TradeAgentTests
    {
        [TestCase("NEWS_BLOCK")]
        [TestCase("SYSTEM_STATE_LOCKED")]
        [TestCase("NO_TECHNICAL_CANDIDATE")]
        [TestCase("BROKER_POSITION_UNKNOWN")]
        [TestCase("POSITION_ALREADY_OPEN")]
        public async Task PreProviderGuard_DoesNotCallProvider(string scenario)
        {
            var now = DateTime.UtcNow;
            var fake = FakeAgentTextProvider.Return(Completed(TradeJson(now, "LONG"), now));
            var cache = new AgentStateCache();
            var agent = Agent(fake, cache, Budget());
            var context = Context(now);

            if (scenario == "NEWS_BLOCK")
                context = Context(now, newsGate: NewsGate.Block);
            else if (scenario == "SYSTEM_STATE_LOCKED")
                context = Context(now, systemAllowsTrading: false);
            else if (scenario == "NO_TECHNICAL_CANDIDATE")
                context = Context(now, candidate: null, explicitlyNullCandidate: true);
            else if (scenario == "BROKER_POSITION_UNKNOWN")
                context = Context(now, session: new TradingSessionState(brokerPositionKnown: false));
            else if (scenario == "POSITION_ALREADY_OPEN")
                context = Context(now, session: new TradingSessionState(hasOpenPosition: true));

            var result = await agent.RefreshAsync(context, now, CancellationToken.None);

            Assert.That(result.Success, Is.False);
            Assert.That(result.ReasonCode, Is.EqualTo(scenario));
            Assert.That(fake.CallCount, Is.EqualTo(0));
            Assert.That(cache.Read(now).Trade, Is.Null);
        }

        [Test]
        public async Task StaleMarket_DoesNotCallProvider()
        {
            var now = DateTime.UtcNow;
            var fake = FakeAgentTextProvider.Return(Completed(TradeJson(now, "LONG"), now));
            var agent = Agent(fake, new AgentStateCache(), Budget());
            var context = Context(now, marketValidUntil: now.AddSeconds(-1));

            var result = await agent.RefreshAsync(context, now, CancellationToken.None);

            Assert.That(result.ReasonCode, Is.EqualTo("STALE_MARKET_STATE"));
            Assert.That(fake.CallCount, Is.EqualTo(0));
        }

        [Test]
        public async Task StaleNews_DoesNotCallProvider()
        {
            var now = DateTime.UtcNow;
            var fake = FakeAgentTextProvider.Return(Completed(TradeJson(now, "LONG"), now));
            var agent = Agent(fake, new AgentStateCache(), Budget());
            var context = Context(now, newsValidUntil: now.AddSeconds(-1));

            var result = await agent.RefreshAsync(context, now, CancellationToken.None);

            Assert.That(result.ReasonCode, Is.EqualTo("STALE_NEWS_STATE"));
            Assert.That(fake.CallCount, Is.EqualTo(0));
        }

        [Test]
        public async Task MarketCandidateDirectionMismatch_DoesNotCallProvider()
        {
            var now = DateTime.UtcNow;
            var fake = FakeAgentTextProvider.Return(Completed(TradeJson(now, "LONG"), now));
            var agent = Agent(fake, new AgentStateCache(), Budget());
            var context = Context(now, marketDirection: TradeDirection.Short);

            var result = await agent.RefreshAsync(context, now, CancellationToken.None);

            Assert.That(result.ReasonCode, Is.EqualTo("MARKET_DIRECTION_MISMATCH"));
            Assert.That(fake.CallCount, Is.EqualTo(0));
        }

        [Test]
        public async Task NewsCautionScoreBelowEightyFive_DoesNotCallProvider()
        {
            var now = DateTime.UtcNow;
            var fake = FakeAgentTextProvider.Return(Completed(TradeJson(now, "LONG"), now));
            var agent = Agent(fake, new AgentStateCache(), Budget());
            var context = Context(now, newsGate: NewsGate.Caution, setupScore: 80);

            var result = await agent.RefreshAsync(context, now, CancellationToken.None);

            Assert.That(result.ReasonCode, Is.EqualTo("NEWS_CAUTION_SCORE_TOO_LOW"));
            Assert.That(fake.CallCount, Is.EqualTo(0));
        }

        [Test]
        public async Task ValidLongResponse_IsPublishedForLongCandidate()
        {
            var now = DateTime.UtcNow;
            var fake = FakeAgentTextProvider.Return(Completed(TradeJson(now, "LONG"), now));
            var cache = new AgentStateCache();
            var agent = Agent(fake, cache, Budget());

            var result = await agent.RefreshAsync(Context(now), now, CancellationToken.None);

            Assert.That(result.Success, Is.True);
            Assert.That(result.State.Payload.Action, Is.EqualTo(TradeAction.Long));
            Assert.That(result.State.Payload.SignalId, Is.EqualTo("sig-1"));
            Assert.That(cache.Read(now).Trade, Is.Not.Null);
            Assert.That(fake.CallCount, Is.EqualTo(1));
        }

        [Test]
        public async Task ValidShortResponse_IsPublishedForShortCandidate()
        {
            var now = DateTime.UtcNow;
            var fake = FakeAgentTextProvider.Return(Completed(TradeJson(now, "SHORT"), now));
            var cache = new AgentStateCache();
            var agent = Agent(fake, cache, Budget());

            var result = await agent.RefreshAsync(Context(now, candidateDirection: TradeDirection.Short, marketDirection: TradeDirection.Short),
                now, CancellationToken.None);

            Assert.That(result.Success, Is.True);
            Assert.That(result.State.Payload.Action, Is.EqualTo(TradeAction.Short));
        }

        [Test]
        public async Task OppositeProviderDirection_IsRejectedAndNotPublished()
        {
            var now = DateTime.UtcNow;
            var fake = FakeAgentTextProvider.Return(Completed(TradeJson(now, "SHORT"), now));
            var cache = new AgentStateCache();

            var result = await Agent(fake, cache, Budget()).RefreshAsync(Context(now), now, CancellationToken.None);

            Assert.That(result.Success, Is.False);
            Assert.That(result.ReasonCode, Is.EqualTo("TRADE_DIRECTION_CONTRADICTION"));
            Assert.That(cache.Read(now).Trade, Is.Null);
        }

        [Test]
        public async Task WaitDecision_IsValidButCannotChangePositionSize()
        {
            var now = DateTime.UtcNow;
            var fake = FakeAgentTextProvider.Return(Completed(TradeJson(now, "WAIT"), now));
            var cache = new AgentStateCache();

            var result = await Agent(fake, cache, Budget()).RefreshAsync(Context(now), now, CancellationToken.None);

            Assert.That(result.Success, Is.True);
            Assert.That(result.State.Payload.Action, Is.EqualTo(TradeAction.Wait));
            Assert.That(Context(now).Candidate.Contracts, Is.EqualTo(1));
        }

        [Test]
        public async Task BudgetExhaustion_DoesNotCallProvider()
        {
            var now = DateTime.UtcNow;
            var policy = new AiUsagePolicy(1, 1, 10000, 1000, 1m, 0m, 0m);
            var budget = new AiUsageBudget(policy);
            var first = budget.TryReserve(AgentRole.Trade, 1, 1, now);
            budget.Settle(first, ProviderCompletion.CompletedForTests("{}"));
            var fake = FakeAgentTextProvider.Return(Completed(TradeJson(now, "LONG"), now));

            var result = await Agent(fake, new AgentStateCache(), budget).RefreshAsync(Context(now), now, CancellationToken.None);

            Assert.That(result.ReasonCode, Is.EqualTo("BUDGET_EXHAUSTED"));
            Assert.That(fake.CallCount, Is.EqualTo(0));
        }

        [Test]
        public void PromptExplicitlyLeavesSizingStopsAndFinalAuthorityToDeterministicCode()
        {
            var now = DateTime.UtcNow;
            var prompt = TradeAgentPrompt.Build(Context(now));

            Assert.That(prompt, Does.Contain("LONG"));
            Assert.That(prompt, Does.Contain("SHORT"));
            Assert.That(prompt, Does.Contain("WAIT"));
            Assert.That(prompt, Does.Contain("Do not change position size"));
            Assert.That(prompt, Does.Contain("Do not change stop or target"));
            Assert.That(prompt, Does.Contain("Risk Engine"));
        }

        private static TradeAgent Agent(IAgentTextProvider provider, AgentStateCache cache, AiUsageBudget budget) =>
            new TradeAgent(provider, cache, budget, "test-model", TimeSpan.FromSeconds(2), 200);

        private static AiUsageBudget Budget() =>
            new AiUsageBudget(new AiUsagePolicy(20, 10, 50000, 5000, 10m, 0.01m, 0.02m));

        private static ProviderCompletion Completed(string json, DateTime now) =>
            ProviderCompletion.Completed("fake", "test-model", json, "req-1", 100, 25, 0.001m,
                now.AddMilliseconds(-10), now);

        private static TradeAgentContext Context(
            DateTime now,
            NewsGate newsGate = NewsGate.Allow,
            TradeDirection candidateDirection = TradeDirection.Long,
            TradeDirection marketDirection = TradeDirection.Long,
            int setupScore = 90,
            DateTime? marketValidUntil = null,
            DateTime? newsValidUntil = null,
            bool systemAllowsTrading = true,
            TradingSessionState session = null,
            TradeCandidate candidate = null,
            bool explicitlyNullCandidate = false)
        {
            var market = new MarketState(
                "NES",
                marketDirection == TradeDirection.Long ? MarketRegime.TrendUp : MarketRegime.TrendDown,
                marketDirection,
                "VWAP_PULLBACK",
                setupScore,
                VolatilityState.Normal,
                true,
                now.AddSeconds(-5),
                marketValidUntil ?? now.AddSeconds(30),
                new[] { "TEST" });

            var news = new NewsState(
                "NES",
                newsGate,
                now.AddSeconds(-5),
                newsValidUntil ?? now.AddSeconds(30),
                0.9m,
                new[] { "TEST" });

            var marketEnvelope = new AgentEnvelope<MarketState>(
                "market-agent", "market.v1", "none", "deterministic", "v1",
                market.GeneratedAtUtc, market.ValidUntilUtc, now, market, AgentEnvelope<MarketState>.HashRaw("market"));

            var newsEnvelope = new AgentEnvelope<NewsState>(
                "news-agent", "news.v1", "news.prompt.v1", "fake", "model",
                news.GeneratedAtUtc, news.ValidUntilUtc, now, news, AgentEnvelope<NewsState>.HashRaw("news"));

            TradeCandidate effectiveCandidate;
            if (explicitlyNullCandidate)
                effectiveCandidate = null;
            else
                effectiveCandidate = candidate ?? (candidateDirection == TradeDirection.Long
                    ? new TradeCandidate("sig-1", "NES", candidateDirection, 6122m, 6118m, 6130m, setupScore, 1, now)
                    : new TradeCandidate("sig-1", "NES", candidateDirection, 6122m, 6126m, 6114m, setupScore, 1, now));

            return new TradeAgentContext(
                marketEnvelope,
                newsEnvelope,
                effectiveCandidate,
                new AccountRiskState(150m),
                session ?? new TradingSessionState(),
                systemAllowsTrading);
        }

        private static string TradeJson(DateTime now, string decision)
        {
            return "{" +
                "\"schema_version\":\"trade.v1\"," +
                "\"symbol\":\"NES\"," +
                "\"decision\":\"" + decision + "\"," +
                "\"confidence\":0.81," +
                "\"generated_at_utc\":\"" + now.ToString("O") + "\"," +
                "\"valid_until_utc\":\"" + now.AddSeconds(15).ToString("O") + "\"," +
                "\"reason_codes\":[\"TEST\"]" +
                "}";
        }
    }
}
