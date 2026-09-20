using System;
using NUnit.Framework;
using NesTrading.Core.Domain;
using NesTrading.Core.Strategy;

namespace NesTrading.Core.Tests.Strategy
{
    [TestFixture]
    public class TrendPullbackStrategyTests
    {
        private static readonly DateTime Now = new DateTime(2026, 9, 20, 14, 15, 0, DateTimeKind.Utc);

        private static MarketSnapshot BullishSnapshot(decimal price = 6122m, decimal volume = 1200m)
        {
            return new MarketSnapshot(
                price: price,
                sessionVwap: 6120.5m,
                ema20: 6121m,
                ema50: 6118m,
                ema20Slope: 0.45m,
                atr: 6m,
                currentVolume: volume,
                referenceVolume: 1000m,
                swingHigh: 6124m,
                swingLow: 6118m,
                higherHigh: true,
                higherLow: true,
                lowerHigh: false,
                lowerLow: false,
                spreadTicks: 1m,
                pullbackTouchedVwap: false,
                pullbackTouchedEma20: true,
                pullbackTouchedStructure: true,
                entryConfirmed: true,
                timestampUtc: Now);
        }

        private static MarketSnapshot BearishSnapshot()
        {
            return new MarketSnapshot(
                price: 6120m,
                sessionVwap: 6122m,
                ema20: 6121m,
                ema50: 6124m,
                ema20Slope: -0.40m,
                atr: 6m,
                currentVolume: 1300m,
                referenceVolume: 1000m,
                swingHigh: 6126m,
                swingLow: 6118m,
                higherHigh: false,
                higherLow: false,
                lowerHigh: true,
                lowerLow: true,
                spreadTicks: 1m,
                pullbackTouchedVwap: false,
                pullbackTouchedEma20: true,
                pullbackTouchedStructure: true,
                entryConfirmed: true,
                timestampUtc: Now);
        }

        private static NewsState News(NewsGate gate) =>
            new NewsState("NES", gate, Now.AddSeconds(-5), Now.AddMinutes(1), 0.9m, new[] { "TEST" });

        [Test]
        public void EvaluateMarket_ValidBullishConditions_ReturnTrendUpWithHighScore()
        {
            var strategy = new TrendPullbackStrategy(StrategyParameters.V1Defaults());
            var state = strategy.EvaluateMarket(BullishSnapshot(), Now);

            Assert.That(state.Regime, Is.EqualTo(MarketRegime.TrendUp));
            Assert.That(state.Direction, Is.EqualTo(TradeDirection.Long));
            Assert.That(state.Setup, Is.EqualTo("VWAP_PULLBACK"));
            Assert.That(state.SetupScore, Is.GreaterThanOrEqualTo(75));
            Assert.That(state.Valid, Is.True);
        }

        [Test]
        public void BuildCandidate_ValidBullishPullbackAndAllowNews_CreatesLongWithStructuralStopAndTwoRTarget()
        {
            var strategy = new TrendPullbackStrategy(StrategyParameters.V1Defaults());
            var snapshot = BullishSnapshot();
            var market = strategy.EvaluateMarket(snapshot, Now);

            var candidate = strategy.BuildCandidate(market, News(NewsGate.Allow), snapshot);

            Assert.That(candidate, Is.Not.Null);
            Assert.That(candidate.Direction, Is.EqualTo(TradeDirection.Long));
            Assert.That(candidate.EntryPrice, Is.EqualTo(snapshot.Price));
            Assert.That(candidate.StructuralStop, Is.EqualTo(snapshot.SwingLow - 0.5m));
            Assert.That(candidate.TargetPrice, Is.EqualTo(snapshot.Price + ((snapshot.Price - candidate.StructuralStop) * 2m)));
            Assert.That(candidate.Contracts, Is.EqualTo(1));
        }

        [Test]
        public void EvaluateMarket_ValidBearishConditions_ReturnTrendDownAndBuildsShort()
        {
            var strategy = new TrendPullbackStrategy(StrategyParameters.V1Defaults());
            var snapshot = BearishSnapshot();
            var market = strategy.EvaluateMarket(snapshot, Now);

            Assert.That(market.Regime, Is.EqualTo(MarketRegime.TrendDown));
            Assert.That(market.Direction, Is.EqualTo(TradeDirection.Short));

            var candidate = strategy.BuildCandidate(market, News(NewsGate.Allow), snapshot);
            Assert.That(candidate, Is.Not.Null);
            Assert.That(candidate.Direction, Is.EqualTo(TradeDirection.Short));
            Assert.That(candidate.StructuralStop, Is.EqualTo(snapshot.SwingHigh + 0.5m));
            Assert.That(candidate.TargetPrice, Is.EqualTo(snapshot.Price - ((candidate.StructuralStop - snapshot.Price) * 2m)));
        }

        [Test]
        public void EvaluateMarket_FlatCompressedConditions_ReturnRangeAndNoCandidate()
        {
            var strategy = new TrendPullbackStrategy(StrategyParameters.V1Defaults());
            var snapshot = new MarketSnapshot(
                6122m, 6122.05m, 6122.10m, 6122.15m, 0.01m, 5m,
                1000m, 1000m, 6124m, 6120m,
                false, false, false, false, 1m,
                true, true, true, true, Now);

            var market = strategy.EvaluateMarket(snapshot, Now);
            var candidate = strategy.BuildCandidate(market, News(NewsGate.Allow), snapshot);

            Assert.That(market.Regime, Is.EqualTo(MarketRegime.Range));
            Assert.That(candidate, Is.Null);
        }

        [Test]
        public void BuildCandidate_NewsBlock_AlwaysReturnsNull()
        {
            var strategy = new TrendPullbackStrategy(StrategyParameters.V1Defaults());
            var snapshot = BullishSnapshot();
            var market = strategy.EvaluateMarket(snapshot, Now);

            Assert.That(strategy.BuildCandidate(market, News(NewsGate.Block), snapshot), Is.Null);
        }

        [Test]
        public void BuildCandidate_CautionRequiresScoreAtLeastEightyFive()
        {
            var strategy = new TrendPullbackStrategy(StrategyParameters.V1Defaults());
            var snapshot = BullishSnapshot();
            var market = new MarketState("NES", MarketRegime.TrendUp, TradeDirection.Long, "VWAP_PULLBACK", 80,
                VolatilityState.Normal, true, Now, Now.AddSeconds(30), new[] { "MANUAL_SCORE_FOR_THRESHOLD_TEST" });

            Assert.That(strategy.BuildCandidate(market, News(NewsGate.Allow), snapshot), Is.Not.Null);
            Assert.That(strategy.BuildCandidate(market, News(NewsGate.Caution), snapshot), Is.Null);
        }

        [Test]
        public void BuildCandidate_ChasingTooFarFromPullbackZone_ReturnsNull()
        {
            var strategy = new TrendPullbackStrategy(StrategyParameters.V1Defaults());
            var snapshot = BullishSnapshot(price: 6130m);
            var market = new MarketState("NES", MarketRegime.TrendUp, TradeDirection.Long, "VWAP_PULLBACK", 95,
                VolatilityState.Normal, true, Now, Now.AddSeconds(30), new[] { "TREND_VALID" });

            var candidate = strategy.BuildCandidate(market, News(NewsGate.Allow), snapshot);

            Assert.That(candidate, Is.Null);
        }

        [Test]
        public void EvaluateMarket_HighAtr_ReturnsHighVolatilityAndNoTradeDirection()
        {
            var p = StrategyParameters.V1Defaults();
            var snapshot = new MarketSnapshot(
                6122m, 6120.5m, 6121m, 6118m, 0.45m, p.MaxAtrPoints + 1m,
                1200m, 1000m, 6124m, 6118m,
                true, true, false, false, 1m,
                false, true, true, true, Now);

            var market = new TrendPullbackStrategy(p).EvaluateMarket(snapshot, Now);

            Assert.That(market.Regime, Is.EqualTo(MarketRegime.HighVolatility));
            Assert.That(market.Direction, Is.EqualTo(TradeDirection.None));
        }

        [Test]
        public void EvaluateMarket_MissingEntryConfirmation_DoesNotProduceTradableSetup()
        {
            var strategy = new TrendPullbackStrategy(StrategyParameters.V1Defaults());
            var s = BullishSnapshot();
            var snapshot = new MarketSnapshot(
                s.Price, s.SessionVwap, s.Ema20, s.Ema50, s.Ema20Slope, s.Atr,
                s.CurrentVolume, s.ReferenceVolume, s.SwingHigh, s.SwingLow,
                s.HigherHigh, s.HigherLow, s.LowerHigh, s.LowerLow, s.SpreadTicks,
                s.PullbackTouchedVwap, s.PullbackTouchedEma20, s.PullbackTouchedStructure, false, s.TimestampUtc);

            var market = strategy.EvaluateMarket(snapshot, Now);

            Assert.That(market.SetupScore, Is.LessThan(75));
            Assert.That(strategy.BuildCandidate(market, News(NewsGate.Allow), snapshot), Is.Null);
        }

        [Test]
        public void MarketSnapshot_RejectsNonUtcTimestamp()
        {
            var local = DateTime.SpecifyKind(Now, DateTimeKind.Local);
            Assert.Throws<ArgumentException>(() => new MarketSnapshot(
                6122m, 6120m, 6121m, 6118m, 0.5m, 6m,
                1000m, 1000m, 6124m, 6118m,
                true, true, false, false, 1m,
                true, true, true, true, local));
        }
    }
}
