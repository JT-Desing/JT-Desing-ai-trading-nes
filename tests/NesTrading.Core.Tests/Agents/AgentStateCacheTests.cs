using System;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using NesTrading.Core.Agents;
using NesTrading.Core.Domain;

namespace NesTrading.Core.Tests.Agents
{
    [TestFixture]
    public class AgentStateCacheTests
    {
        private static readonly DateTime BaseTime = new DateTime(2026, 9, 21, 14, 0, 0, DateTimeKind.Utc);

        [Test]
        public void FreshStates_AreReturnedBySnapshot()
        {
            var cache = new AgentStateCache();
            Assert.That(cache.TryPublishNews(NewsEnvelope(BaseTime, BaseTime.AddMinutes(1), BaseTime.AddSeconds(1))), Is.True);
            Assert.That(cache.TryPublishMarket(MarketEnvelope(BaseTime, BaseTime.AddSeconds(30), BaseTime.AddSeconds(1))), Is.True);
            Assert.That(cache.TryPublishTrade(TradeEnvelope(BaseTime, BaseTime.AddSeconds(15), BaseTime.AddSeconds(1))), Is.True);

            var snapshot = cache.Read(BaseTime.AddSeconds(5));

            Assert.That(snapshot.News, Is.Not.Null);
            Assert.That(snapshot.Market, Is.Not.Null);
            Assert.That(snapshot.Trade, Is.Not.Null);
            Assert.That(snapshot.IsTradableSet, Is.True);
        }

        [Test]
        public void ExpiredState_IsUnavailableRatherThanReturnedAsStale()
        {
            var cache = new AgentStateCache();
            cache.TryPublishNews(NewsEnvelope(BaseTime, BaseTime.AddSeconds(10), BaseTime.AddSeconds(1)));

            var snapshot = cache.Read(BaseTime.AddSeconds(11));

            Assert.That(snapshot.News, Is.Null);
            Assert.That(snapshot.HasFreshNews, Is.False);
            Assert.That(snapshot.IsTradableSet, Is.False);
        }

        [Test]
        public void NewCache_AfterRestart_StartsEmptyAndFailClosed()
        {
            var first = new AgentStateCache();
            first.TryPublishNews(NewsEnvelope(BaseTime, BaseTime.AddMinutes(1), BaseTime.AddSeconds(1)));

            var restarted = new AgentStateCache();
            var snapshot = restarted.Read(BaseTime.AddSeconds(2));

            Assert.That(snapshot.News, Is.Null);
            Assert.That(snapshot.Market, Is.Null);
            Assert.That(snapshot.Trade, Is.Null);
            Assert.That(snapshot.IsTradableSet, Is.False);
        }

        [Test]
        public void OlderGeneratedState_CannotOverwriteNewerNewsState()
        {
            var cache = new AgentStateCache();
            var newer = NewsEnvelope(BaseTime.AddSeconds(10), BaseTime.AddMinutes(1), BaseTime.AddSeconds(11));
            var older = NewsEnvelope(BaseTime.AddSeconds(5), BaseTime.AddMinutes(1), BaseTime.AddSeconds(12));

            Assert.That(cache.TryPublishNews(newer), Is.True);
            Assert.That(cache.TryPublishNews(older), Is.False);

            Assert.That(cache.Read(BaseTime.AddSeconds(15)).News.GeneratedAtUtc, Is.EqualTo(BaseTime.AddSeconds(10)));
        }

        [Test]
        public void ResponseReceivedAfterItsOwnExpiry_CannotBePublished()
        {
            var cache = new AgentStateCache();
            var late = NewsEnvelope(BaseTime, BaseTime.AddSeconds(10), BaseTime.AddSeconds(11));

            Assert.That(cache.TryPublishNews(late), Is.False);
            Assert.That(cache.Read(BaseTime.AddSeconds(11)).News, Is.Null);
        }

        [Test]
        public void EqualGenerationTime_DoesNotReplaceExistingState()
        {
            var cache = new AgentStateCache();
            var first = NewsEnvelope(BaseTime, BaseTime.AddMinutes(1), BaseTime.AddSeconds(1), NewsGate.Allow);
            var second = NewsEnvelope(BaseTime, BaseTime.AddMinutes(1), BaseTime.AddSeconds(2), NewsGate.Block);

            Assert.That(cache.TryPublishNews(first), Is.True);
            Assert.That(cache.TryPublishNews(second), Is.False);
            Assert.That(cache.Read(BaseTime.AddSeconds(3)).News.Payload.State, Is.EqualTo(NewsGate.Allow));
        }

        [Test]
        public void ConcurrentNewsPublishes_NeverEndWithOlderGeneration()
        {
            var cache = new AgentStateCache();

            Parallel.For(0, 50, i =>
            {
                var generated = BaseTime.AddMilliseconds(i);
                var envelope = NewsEnvelope(generated, BaseTime.AddMinutes(1), generated.AddMilliseconds(1));
                cache.TryPublishNews(envelope);
            });

            var snapshot = cache.Read(BaseTime.AddSeconds(1));

            Assert.That(snapshot.News, Is.Not.Null);
            Assert.That(snapshot.News.GeneratedAtUtc, Is.EqualTo(BaseTime.AddMilliseconds(49)));
        }

        [Test]
        public void RolesAreIndependent_NewerNewsDoesNotReplaceMarketOrTrade()
        {
            var cache = new AgentStateCache();
            var market = MarketEnvelope(BaseTime, BaseTime.AddSeconds(30), BaseTime.AddSeconds(1));
            var trade = TradeEnvelope(BaseTime, BaseTime.AddSeconds(15), BaseTime.AddSeconds(1));
            cache.TryPublishMarket(market);
            cache.TryPublishTrade(trade);

            cache.TryPublishNews(NewsEnvelope(BaseTime.AddSeconds(5), BaseTime.AddMinutes(1), BaseTime.AddSeconds(6)));

            var snapshot = cache.Read(BaseTime.AddSeconds(7));
            Assert.That(snapshot.Market, Is.SameAs(market));
            Assert.That(snapshot.Trade, Is.SameAs(trade));
        }

        private static AgentEnvelope<NewsState> NewsEnvelope(DateTime generated, DateTime validUntil, DateTime received, NewsGate gate = NewsGate.Allow)
        {
            var payload = new NewsState("NES", gate, generated, validUntil, 0.8m, new[] { "TEST" });
            return new AgentEnvelope<NewsState>(
                "news-agent", AgentSchemaVersion.NewsV1, "news.prompt.v1", "test", "test-model",
                generated, validUntil, received, payload, AgentEnvelope<NewsState>.HashRaw(gate.ToString()));
        }

        private static AgentEnvelope<MarketState> MarketEnvelope(DateTime generated, DateTime validUntil, DateTime received)
        {
            var payload = new MarketState("NES", MarketRegime.TrendUp, TradeDirection.Long, "VWAP_PULLBACK", 90,
                VolatilityState.Normal, true, generated, validUntil, new[] { "TEST" });
            return new AgentEnvelope<MarketState>(
                "market-agent", AgentSchemaVersion.MarketV1, "deterministic.v1", "deterministic", "nes-core",
                generated, validUntil, received, payload, AgentEnvelope<MarketState>.HashRaw("market"));
        }

        private static AgentEnvelope<TradeDecision> TradeEnvelope(DateTime generated, DateTime validUntil, DateTime received)
        {
            var payload = new TradeDecision("NES", TradeAction.Long, 0.8m, "sig-1", generated, validUntil, new[] { "TEST" });
            return new AgentEnvelope<TradeDecision>(
                "trade-agent", AgentSchemaVersion.TradeV1, "trade.prompt.v1", "test", "test-model",
                generated, validUntil, received, payload, AgentEnvelope<TradeDecision>.HashRaw("trade"));
        }
    }
}
