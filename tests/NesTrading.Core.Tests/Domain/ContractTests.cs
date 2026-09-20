using System;
using NUnit.Framework;
using NesTrading.Core.Domain;

namespace NesTrading.Core.Tests.Domain
{
    [TestFixture]
    public class ContractTests
    {
        private static readonly DateTime Now = new DateTime(2026, 9, 20, 14, 0, 0, DateTimeKind.Utc);

        [Test]
        public void NewsState_ExposesFreshnessWindowAndAllowedState()
        {
            var state = new NewsState("NES", NewsGate.Allow, Now, Now.AddMinutes(1), 0.8m, new[] { "NO_MAJOR_EVENT" });
            Assert.That(state.Symbol, Is.EqualTo("NES"));
            Assert.That(state.State, Is.EqualTo(NewsGate.Allow));
            Assert.That(state.GeneratedAtUtc, Is.EqualTo(Now));
            Assert.That(state.ValidUntilUtc, Is.EqualTo(Now.AddMinutes(1)));
        }

        [Test]
        public void MarketState_ExposesRegimeDirectionScoreAndFreshness()
        {
            var state = new MarketState("NES", MarketRegime.TrendUp, TradeDirection.Long, "VWAP_PULLBACK", 82, VolatilityState.Normal, true, Now, Now.AddSeconds(30), new[] { "ABOVE_VWAP" });
            Assert.That(state.Regime, Is.EqualTo(MarketRegime.TrendUp));
            Assert.That(state.SetupScore, Is.EqualTo(82));
            Assert.That(state.GeneratedAtUtc, Is.EqualTo(Now));
        }

        [Test]
        public void TradeDecision_ExposesActionConfidenceSignalAndFreshness()
        {
            var decision = new TradeDecision("NES", TradeAction.Long, 0.8m, "sig-1", Now, Now.AddSeconds(15), new[] { "SETUP_VALID" });
            Assert.That(decision.Action, Is.EqualTo(TradeAction.Long));
            Assert.That(decision.SignalId, Is.EqualTo("sig-1"));
            Assert.That(decision.Confidence, Is.EqualTo(0.8m));
        }

        [Test]
        public void NesContractSpecs_MatchCmeE_NanoSp500Contract()
        {
            Assert.That(NesContractSpecs.UsdPerPoint, Is.EqualTo(0.50m));
            Assert.That(NesContractSpecs.TickSizePoints, Is.EqualTo(0.50m));
            Assert.That(NesContractSpecs.UsdPerTick, Is.EqualTo(0.25m));
        }

        [TestCase(6122.25)]
        [TestCase(6122.75)]
        public void TradeCandidate_RejectsPricesThatAreNotAlignedToNesHalfPointTick(double invalidPrice)
        {
            Assert.Throws<ArgumentException>(() =>
                new TradeCandidate(
                    "sig-tick",
                    "NES",
                    TradeDirection.Long,
                    (decimal)invalidPrice,
                    6118m,
                    6130m,
                    90,
                    1,
                    Now));
        }

        [Test]
        public void TradeCandidate_V1_UsesExactlyOneNesContract()
        {
            var candidate = new TradeCandidate("sig-1", "NES", TradeDirection.Long, 6122m, 6117.5m, 6131m, 82, 1, Now);
            Assert.That(candidate.Symbol, Is.EqualTo("NES"));
            Assert.That(candidate.Contracts, Is.EqualTo(1));
            Assert.That(candidate.StructuralStop, Is.EqualTo(6117.5m));
        }

        [TestCase("")]
        [TestCase("ES")]
        [TestCase("MES")]
        public void TradeCandidate_RejectsAnySymbolOtherThanNes(string symbol)
        {
            Assert.Throws<ArgumentException>(() => new TradeCandidate("sig-1", symbol, TradeDirection.Long, 6122m, 6118m, 6130m, 80, 1, Now));
        }

        [TestCase(0)]
        [TestCase(2)]
        [TestCase(10)]
        public void TradeCandidate_RejectsContractCountOtherThanOne(int contracts)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new TradeCandidate("sig-1", "NES", TradeDirection.Long, 6122m, 6118m, 6130m, 80, contracts, Now));
        }

        [TestCase(-1)]
        [TestCase(101)]
        public void MarketState_RejectsSetupScoreOutsideZeroToOneHundred(int score)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new MarketState("NES", MarketRegime.TrendUp, TradeDirection.Long, "VWAP_PULLBACK", score, VolatilityState.Normal, true, Now, Now.AddSeconds(30), Array.Empty<string>()));
        }

        [TestCase(-0.01)]
        [TestCase(1.01)]
        public void TradeDecision_RejectsConfidenceOutsideZeroToOne(double confidence)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new TradeDecision("NES", TradeAction.Wait, (decimal)confidence, "sig-1", Now, Now.AddSeconds(10), Array.Empty<string>()));
        }

        [Test]
        public void AgentState_RejectsValidityEndingBeforeGeneration()
        {
            Assert.Throws<ArgumentException>(() => new NewsState("NES", NewsGate.Allow, Now, Now.AddSeconds(-1), 0.5m, Array.Empty<string>()));
        }

        [Test]
        public void TradeCandidate_RejectsEmptySignalId()
        {
            Assert.Throws<ArgumentException>(() => new TradeCandidate("", "NES", TradeDirection.Long, 6122m, 6118m, 6130m, 80, 1, Now));
        }
    }
}
