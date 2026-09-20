using System;
using NUnit.Framework;
using NesTrading.Core.Domain;
using NesTrading.Core.Risk;
using NesTrading.Core.Runtime;

namespace NesTrading.Core.Tests.Risk
{
    [TestFixture]
    public class RiskEngineTests
    {
        // Monday 2026-09-21 10:00 ET (EDT) = 14:00 UTC.
        private static readonly DateTime Now = new DateTime(2026, 9, 21, 14, 0, 0, DateTimeKind.Utc);

        private static TradeCandidate Candidate(
            string signalId = "sig-risk-1",
            TradeDirection direction = TradeDirection.Long,
            decimal entry = 6122m,
            decimal stop = 6117.5m,
            decimal target = 6131m)
        {
            return new TradeCandidate(signalId, "NES", direction, entry, stop, target, 90, 1, Now);
        }

        private static NewsState News(NewsGate gate = NewsGate.Allow, DateTime? validUntil = null) =>
            new NewsState("NES", gate, Now.AddSeconds(-5), validUntil ?? Now.AddMinutes(1), 0.9m, new[] { "TEST" });

        private static MarketState Market(DateTime? validUntil = null, MarketRegime regime = MarketRegime.TrendUp) =>
            new MarketState("NES", regime,
                regime == MarketRegime.TrendDown ? TradeDirection.Short : TradeDirection.Long,
                "VWAP_PULLBACK", 90, VolatilityState.Normal, true,
                Now.AddSeconds(-5), validUntil ?? Now.AddSeconds(30), new[] { "TEST" });

        private static TradeDecision Decision(string signalId = "sig-risk-1", TradeAction action = TradeAction.Long, DateTime? validUntil = null) =>
            new TradeDecision("NES", action, 0.9m, signalId, Now.AddSeconds(-2), validUntil ?? Now.AddSeconds(15), new[] { "TEST" });

        private static TradingStateMachine EnabledMachine()
        {
            return TradingStateMachine.Restore(TradingSystemState.TradingEnabled);
        }

        private static TradingSessionState Session(
            int trades = 0,
            int losses = 0,
            bool open = false,
            decimal pnl = 0m,
            DateTime? lastClose = null,
            bool brokerKnown = true)
        {
            return new TradingSessionState(trades, losses, open, pnl, lastClose, brokerKnown);
        }

        private static AccountRiskState Account(decimal equity = 150m) => new AccountRiskState(equity);

        private static CostEstimate Costs(decimal fees = 0.25m, decimal slippage = 0.25m, decimal spreadTicks = 1m) =>
            new CostEstimate(fees, slippage, spreadTicks);

        private static RiskDecision Evaluate(
            RiskEngine engine,
            TradeCandidate candidate = null,
            AccountRiskState account = null,
            CostEstimate costs = null,
            TradingSessionState session = null,
            NewsState news = null,
            MarketState market = null,
            TradeDecision trade = null,
            TradingStateMachine machine = null,
            DateTime? now = null)
        {
            return engine.Evaluate(
                candidate ?? Candidate(),
                account ?? Account(),
                costs ?? Costs(),
                session ?? Session(),
                news ?? News(),
                market ?? Market(),
                trade ?? Decision(),
                machine ?? EnabledMachine(),
                now ?? Now);
        }

        [Test]
        public void RiskMath_IncludesPriceRiskFeesAndExpectedSlippage()
        {
            var engine = new RiskEngine(RiskPolicy.V1Defaults());
            var result = Evaluate(engine);

            // 4.5 points * $0.50 = $2.25, plus $0.25 fees + $0.25 slippage = $2.75.
            Assert.That(result.IsApproved, Is.True);
            Assert.That(result.TotalRiskUsd, Is.EqualTo(2.75m));
            Assert.That(result.RiskPercent, Is.EqualTo(2.75m / 150m));
        }

        [Test]
        public void StructuralStopExceedingTwoPercentTotalRisk_IsRejectedWithoutChangingStop()
        {
            var engine = new RiskEngine(RiskPolicy.V1Defaults());
            var candidate = Candidate(stop: 6116m); // 6 points = $3 before costs.

            var result = Evaluate(engine, candidate: candidate);

            Assert.That(result.IsApproved, Is.False);
            Assert.That(result.ReasonCode, Is.EqualTo("RISK_PER_TRADE_HARD_MAX"));
            Assert.That(result.Candidate.StructuralStop, Is.EqualTo(6116m));
        }

        [Test]
        public void DuplicateSignal_IsRejectedOnSecondEvaluation()
        {
            var engine = new RiskEngine(RiskPolicy.V1Defaults());

            Assert.That(Evaluate(engine).IsApproved, Is.True);
            var second = Evaluate(engine);

            Assert.That(second.IsApproved, Is.False);
            Assert.That(second.ReasonCode, Is.EqualTo("DUPLICATE_SIGNAL"));
        }

        [TestCase(NewsGate.Block, "NEWS_BLOCK")]
        public void NewsBlock_IsAlwaysRejected(NewsGate gate, string expectedReason)
        {
            var result = Evaluate(new RiskEngine(RiskPolicy.V1Defaults()), news: News(gate));
            Assert.That(result.IsApproved, Is.False);
            Assert.That(result.ReasonCode, Is.EqualTo(expectedReason));
        }

        [Test]
        public void StaleNews_IsRejected()
        {
            var stale = new NewsState("NES", NewsGate.Allow, Now.AddMinutes(-2), Now.AddSeconds(-1), 0.8m, new[] { "STALE" });
            var result = Evaluate(new RiskEngine(RiskPolicy.V1Defaults()), news: stale);
            Assert.That(result.ReasonCode, Is.EqualTo("STALE_NEWS_STATE"));
        }

        [Test]
        public void StaleMarket_IsRejected()
        {
            var stale = new MarketState("NES", MarketRegime.TrendUp, TradeDirection.Long, "VWAP_PULLBACK", 90,
                VolatilityState.Normal, true, Now.AddMinutes(-2), Now.AddSeconds(-1), new[] { "STALE" });
            var result = Evaluate(new RiskEngine(RiskPolicy.V1Defaults()), market: stale);
            Assert.That(result.ReasonCode, Is.EqualTo("STALE_MARKET_STATE"));
        }

        [Test]
        public void StaleTradeDecision_IsRejected()
        {
            var stale = new TradeDecision("NES", TradeAction.Long, 0.8m, "sig-risk-1", Now.AddMinutes(-2), Now.AddSeconds(-1), new[] { "STALE" });
            var result = Evaluate(new RiskEngine(RiskPolicy.V1Defaults()), trade: stale);
            Assert.That(result.ReasonCode, Is.EqualTo("STALE_TRADE_STATE"));
        }

        [TestCase(2, 0, false, "MAX_TRADES_REACHED")]
        [TestCase(1, 2, false, "MAX_LOSSES_REACHED")]
        public void DailyCountLimits_AreFailClosed(int trades, int losses, bool open, string expected)
        {
            // losses cannot exceed trades; normalize the first field when needed.
            if (losses > trades) trades = losses;
            var result = Evaluate(new RiskEngine(RiskPolicy.V1Defaults()), session: Session(trades, losses, open));
            Assert.That(result.ReasonCode, Is.EqualTo(expected));
        }

        [Test]
        public void ExistingOpenPosition_IsRejected()
        {
            var result = Evaluate(new RiskEngine(RiskPolicy.V1Defaults()), session: Session(open: true));
            Assert.That(result.ReasonCode, Is.EqualTo("POSITION_ALREADY_OPEN"));
        }

        [Test]
        public void UnknownBrokerPosition_IsRejected()
        {
            var result = Evaluate(new RiskEngine(RiskPolicy.V1Defaults()), session: Session(brokerKnown: false));
            Assert.That(result.ReasonCode, Is.EqualTo("BROKER_POSITION_UNKNOWN"));
        }

        [Test]
        public void SoftDailyLossAtTwoPercent_PreventsNewEntriesBeforeHardLimit()
        {
            var result = Evaluate(new RiskEngine(RiskPolicy.V1Defaults()), session: Session(pnl: -3.00m));
            Assert.That(result.ReasonCode, Is.EqualTo("DAILY_SOFT_LOSS_LOCK"));
        }

        [Test]
        public void HardDailyLossAtThreePercent_UsesHardLockReason()
        {
            var result = Evaluate(new RiskEngine(RiskPolicy.V1Defaults()), session: Session(pnl: -4.50m));
            Assert.That(result.ReasonCode, Is.EqualTo("DAILY_HARD_LOSS_LOCK"));
        }

        [Test]
        public void CooldownWithinTenMinutes_IsRejected()
        {
            var result = Evaluate(new RiskEngine(RiskPolicy.V1Defaults()), session: Session(lastClose: Now.AddMinutes(-5)));
            Assert.That(result.ReasonCode, Is.EqualTo("COOLDOWN_ACTIVE"));
        }

        [Test]
        public void CooldownExactlyTenMinutes_IsAllowed()
        {
            var result = Evaluate(new RiskEngine(RiskPolicy.V1Defaults()), session: Session(lastClose: Now.AddMinutes(-10)));
            Assert.That(result.IsApproved, Is.True);
        }

        [TestCase(13, 39, "OUTSIDE_SESSION")]
        [TestCase(15, 31, "OUTSIDE_SESSION")]
        public void OutsideEasternSession_IsRejected(int utcHour, int utcMinute, string expected)
        {
            // On this date ET is UTC-4; 13:39 UTC = 09:39 ET and 15:31 UTC = 11:31 ET.
            var utc = new DateTime(2026, 9, 21, utcHour, utcMinute, 0, DateTimeKind.Utc);
            var result = Evaluate(new RiskEngine(RiskPolicy.V1Defaults()), now: utc);
            Assert.That(result.ReasonCode, Is.EqualTo(expected));
        }

        [TestCase(13, 40)]
        [TestCase(15, 30)]
        public void SessionBoundaries_AreInclusive(int utcHour, int utcMinute)
        {
            var utc = new DateTime(2026, 9, 21, utcHour, utcMinute, 0, DateTimeKind.Utc);
            var news = new NewsState("NES", NewsGate.Allow, utc.AddSeconds(-5), utc.AddMinutes(1), 0.8m, new[] { "TEST" });
            var market = new MarketState("NES", MarketRegime.TrendUp, TradeDirection.Long, "VWAP_PULLBACK", 90,
                VolatilityState.Normal, true, utc.AddSeconds(-5), utc.AddSeconds(30), new[] { "TEST" });
            var trade = new TradeDecision("NES", TradeAction.Long, 0.8m, "sig-risk-1", utc.AddSeconds(-2), utc.AddSeconds(15), new[] { "TEST" });

            var result = Evaluate(new RiskEngine(RiskPolicy.V1Defaults()), news: news, market: market, trade: trade, now: utc);
            Assert.That(result.IsApproved, Is.True);
        }

        [Test]
        public void WeekendSession_IsRejectedEvenIfClockTimeMatches()
        {
            var sunday = new DateTime(2026, 9, 20, 14, 0, 0, DateTimeKind.Utc);
            var news = new NewsState("NES", NewsGate.Allow, sunday.AddSeconds(-5), sunday.AddMinutes(1), 0.8m, new[] { "TEST" });
            var market = new MarketState("NES", MarketRegime.TrendUp, TradeDirection.Long, "VWAP_PULLBACK", 90,
                VolatilityState.Normal, true, sunday.AddSeconds(-5), sunday.AddSeconds(30), new[] { "TEST" });
            var trade = new TradeDecision("NES", TradeAction.Long, 0.8m, "sig-risk-1", sunday.AddSeconds(-2), sunday.AddSeconds(15), new[] { "TEST" });

            var result = Evaluate(new RiskEngine(RiskPolicy.V1Defaults()), news: news, market: market, trade: trade, now: sunday);
            Assert.That(result.ReasonCode, Is.EqualTo("OUTSIDE_SESSION"));
        }

        [Test]
        public void StateMachineThatDisallowsOrders_IsRejected()
        {
            var result = Evaluate(new RiskEngine(RiskPolicy.V1Defaults()), machine: TradingStateMachine.Restore(TradingSystemState.DataStale));
            Assert.That(result.ReasonCode, Is.EqualTo("SYSTEM_STATE_LOCKED"));
        }

        [Test]
        public void AbnormalSpread_IsRejected()
        {
            var result = Evaluate(new RiskEngine(RiskPolicy.V1Defaults()), costs: Costs(spreadTicks: 5m));
            Assert.That(result.ReasonCode, Is.EqualTo("SPREAD_TOO_WIDE"));
        }

        [Test]
        public void ExcessiveExpectedSlippage_IsRejected()
        {
            var result = Evaluate(new RiskEngine(RiskPolicy.V1Defaults()), costs: Costs(slippage: 1.25m));
            Assert.That(result.ReasonCode, Is.EqualTo("SLIPPAGE_TOO_HIGH"));
        }

        [Test]
        public void NonPositiveEquity_IsRejected()
        {
            var result = Evaluate(new RiskEngine(RiskPolicy.V1Defaults()), account: Account(0m));
            Assert.That(result.ReasonCode, Is.EqualTo("INVALID_ACCOUNT_EQUITY"));
        }

        [Test]
        public void RangeMarket_IsRejectedEvenIfOtherInputsLookValid()
        {
            var range = new MarketState("NES", MarketRegime.Range, TradeDirection.None, "NONE", 0,
                VolatilityState.Low, false, Now.AddSeconds(-5), Now.AddSeconds(30), new[] { "RANGE" });
            var result = Evaluate(new RiskEngine(RiskPolicy.V1Defaults()), market: range);
            Assert.That(result.ReasonCode, Is.EqualTo("MARKET_NOT_TRADABLE"));
        }

        [Test]
        public void WaitTradeDecision_IsRejected()
        {
            var result = Evaluate(new RiskEngine(RiskPolicy.V1Defaults()), trade: Decision(action: TradeAction.Wait));
            Assert.That(result.ReasonCode, Is.EqualTo("TRADE_DECISION_NOT_EXECUTABLE"));
        }

        [Test]
        public void TradeSignalIdMismatch_IsRejected()
        {
            var result = Evaluate(new RiskEngine(RiskPolicy.V1Defaults()), trade: Decision(signalId: "different-signal"));
            Assert.That(result.ReasonCode, Is.EqualTo("SIGNAL_ID_MISMATCH"));
        }

        [Test]
        public void DirectionMismatch_IsRejected()
        {
            var shortDecision = Decision(action: TradeAction.Short);
            var result = Evaluate(new RiskEngine(RiskPolicy.V1Defaults()), trade: shortDecision);
            Assert.That(result.ReasonCode, Is.EqualTo("DIRECTION_MISMATCH"));
        }

        [Test]
        public void RewardRiskBelowConfiguredMinimum_IsRejected()
        {
            var candidate = Candidate(target: 6126m); // reward 4 points vs 4.5 point risk.
            var result = Evaluate(new RiskEngine(RiskPolicy.V1Defaults()), candidate: candidate);
            Assert.That(result.ReasonCode, Is.EqualTo("REWARD_RISK_TOO_LOW"));
        }

        [Test]
        public void CostEstimate_RejectsNegativeValues()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new CostEstimate(-1m, 0m, 0m));
            Assert.Throws<ArgumentOutOfRangeException>(() => new CostEstimate(0m, -1m, 0m));
            Assert.Throws<ArgumentOutOfRangeException>(() => new CostEstimate(0m, 0m, -1m));
        }
    }
}
