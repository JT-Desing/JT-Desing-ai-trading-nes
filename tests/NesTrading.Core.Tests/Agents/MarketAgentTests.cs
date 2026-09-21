using System;
using NUnit.Framework;
using NesTrading.Core.Agents.Market;
using NesTrading.Core.Domain;
using NesTrading.Core.Strategy;

namespace NesTrading.Core.Tests.Agents
{
    [TestFixture]
    public class MarketAgentTests
    {
        private static readonly DateTime Now = new DateTime(2026, 9, 21, 14, 0, 0, DateTimeKind.Utc);

        [Test]
        public void Evaluate_ProducesSameStateAsDeterministicStrategy()
        {
            var strategy = new TrendPullbackStrategy(StrategyParameters.V1Defaults());
            var agent = new MarketAgent(strategy);
            var snapshot = Bullish();

            var expected = strategy.EvaluateMarket(snapshot, Now);
            var actual = agent.Evaluate(snapshot, Now);

            Assert.That(actual.Payload.Regime, Is.EqualTo(expected.Regime));
            Assert.That(actual.Payload.Direction, Is.EqualTo(expected.Direction));
            Assert.That(actual.Payload.Setup, Is.EqualTo(expected.Setup));
            Assert.That(actual.Payload.SetupScore, Is.EqualTo(expected.SetupScore));
            Assert.That(actual.GeneratedAtUtc, Is.EqualTo(expected.GeneratedAtUtc));
            Assert.That(actual.ValidUntilUtc, Is.EqualTo(expected.ValidUntilUtc));
        }

        [Test]
        public void Evaluate_IdentifiesDeterministicImplementationMetadata()
        {
            var agent = new MarketAgent(new TrendPullbackStrategy(StrategyParameters.V1Defaults()));

            var result = agent.Evaluate(Bullish(), Now);

            Assert.That(result.AgentName, Is.EqualTo("market-agent"));
            Assert.That(result.SchemaVersion, Is.EqualTo("market.v1"));
            Assert.That(result.ProviderName, Is.EqualTo("deterministic"));
            Assert.That(result.ModelName, Is.EqualTo("trend-pullback-vwap-v1"));
            Assert.That(result.PromptVersion, Is.EqualTo("none"));
        }

        [Test]
        public void Evaluate_RangeRemainsNonTradable()
        {
            var agent = new MarketAgent(new TrendPullbackStrategy(StrategyParameters.V1Defaults()));
            var range = new MarketSnapshot(
                6122m, 6122.05m, 6122.10m, 6122.15m, 0.01m, 5m,
                1000m, 1000m, 6124m, 6120m,
                false, false, false, false, 1m,
                true, true, true, true, Now);

            var result = agent.Evaluate(range, Now);

            Assert.That(result.Payload.Regime, Is.EqualTo(MarketRegime.Range));
            Assert.That(result.Payload.Valid, Is.False);
            Assert.That(result.Payload.Direction, Is.EqualTo(TradeDirection.None));
        }

        [Test]
        public void Evaluate_HighVolatilityRemainsNonTradable()
        {
            var p = StrategyParameters.V1Defaults();
            var agent = new MarketAgent(new TrendPullbackStrategy(p));
            var highVol = new MarketSnapshot(
                6122m, 6120m, 6121m, 6118m, 0.5m, p.MaxAtrPoints + 1m,
                1200m, 1000m, 6125m, 6117m,
                true, true, false, false, 1m,
                false, true, true, true, Now);

            var result = agent.Evaluate(highVol, Now);

            Assert.That(result.Payload.Regime, Is.EqualTo(MarketRegime.HighVolatility));
            Assert.That(result.Payload.Valid, Is.False);
        }

        [Test]
        public void Evaluate_DoesNotExtendStrategyTtl()
        {
            var strategy = new TrendPullbackStrategy(StrategyParameters.V1Defaults());
            var snapshot = Bullish();
            var expected = strategy.EvaluateMarket(snapshot, Now);
            var actual = new MarketAgent(strategy).Evaluate(snapshot, Now);

            Assert.That(actual.ValidUntilUtc, Is.EqualTo(expected.ValidUntilUtc));
        }

        private static MarketSnapshot Bullish() =>
            new MarketSnapshot(
                6122m, 6120.5m, 6121m, 6118m, 0.45m, 6m,
                1200m, 1000m, 6124m, 6118m,
                true, true, false, false, 1m,
                false, true, true, true, Now);
    }
}
