using System;
using System.Collections.Generic;
using System.Linq;
using NesTrading.Core.Domain;

namespace NesTrading.Core.Strategy
{
    public sealed class TrendPullbackStrategy
    {
        private readonly StrategyParameters _parameters;
        private readonly SetupScorer _scorer;

        public TrendPullbackStrategy(StrategyParameters parameters)
        {
            _parameters = parameters ?? throw new ArgumentNullException(nameof(parameters));
            _scorer = new SetupScorer(parameters);
        }

        public MarketState EvaluateMarket(MarketSnapshot snapshot, DateTime utcNow)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            if (utcNow.Kind != DateTimeKind.Utc) throw new ArgumentException("utcNow must be UTC.", nameof(utcNow));

            if (snapshot.TimestampUtc > utcNow)
                return State(MarketRegime.InvalidData, TradeDirection.None, "NONE", 0, VolatilityState.Normal, false, snapshot, utcNow, "FUTURE_MARKET_DATA");

            if (snapshot.Atr > _parameters.MaxAtrPoints)
                return State(MarketRegime.HighVolatility, TradeDirection.None, "NONE", 0, VolatilityState.High, false, snapshot, utcNow, "ATR_TOO_HIGH");

            if (snapshot.Atr < _parameters.MinAtrPoints || IsRange(snapshot))
                return State(MarketRegime.Range, TradeDirection.None, "NONE", 0, VolatilityState.Low, false, snapshot, utcNow, "RANGE_FILTER");

            var bullish = snapshot.Price > snapshot.SessionVwap &&
                          snapshot.Ema20 > snapshot.Ema50 &&
                          snapshot.Ema20Slope > 0m &&
                          snapshot.HigherHigh &&
                          snapshot.HigherLow;

            var bearish = snapshot.Price < snapshot.SessionVwap &&
                          snapshot.Ema20 < snapshot.Ema50 &&
                          snapshot.Ema20Slope < 0m &&
                          snapshot.LowerHigh &&
                          snapshot.LowerLow;

            if (bullish)
            {
                var score = _scorer.Score(snapshot, TradeDirection.Long);
                var setup = score >= 75 ? "VWAP_PULLBACK" : "NONE";
                return State(MarketRegime.TrendUp, TradeDirection.Long, setup, score, VolatilityState.Normal, score >= 75, snapshot, utcNow, "BULLISH_TREND");
            }

            if (bearish)
            {
                var score = _scorer.Score(snapshot, TradeDirection.Short);
                var setup = score >= 75 ? "VWAP_PULLBACK" : "NONE";
                return State(MarketRegime.TrendDown, TradeDirection.Short, setup, score, VolatilityState.Normal, score >= 75, snapshot, utcNow, "BEARISH_TREND");
            }

            return State(MarketRegime.Range, TradeDirection.None, "NONE", 0, VolatilityState.Normal, false, snapshot, utcNow, "NO_ALIGNED_TREND");
        }

        public TradeCandidate BuildCandidate(MarketState market, NewsState news, MarketSnapshot snapshot)
        {
            if (market == null) throw new ArgumentNullException(nameof(market));
            if (news == null) throw new ArgumentNullException(nameof(news));
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));

            var now = snapshot.TimestampUtc;
            if (!market.IsFreshAt(now) || !news.IsFreshAt(now)) return null;
            if (!market.Valid || market.Setup != "VWAP_PULLBACK") return null;
            if (market.Regime != MarketRegime.TrendUp && market.Regime != MarketRegime.TrendDown) return null;
            if (news.State == NewsGate.Block) return null;

            var threshold = news.State == NewsGate.Caution ? 85 : 75;
            if (market.SetupScore < threshold) return null;
            if (market.Direction != TradeDirection.Long && market.Direction != TradeDirection.Short) return null;
            if (IsChasing(snapshot)) return null;

            decimal stop;
            decimal target;

            if (market.Direction == TradeDirection.Long)
            {
                stop = snapshot.SwingLow - _parameters.StopBufferPoints;
                var riskPoints = snapshot.Price - stop;
                if (riskPoints <= 0m) return null;
                target = snapshot.Price + riskPoints * _parameters.RewardMultiple;
            }
            else
            {
                stop = snapshot.SwingHigh + _parameters.StopBufferPoints;
                var riskPoints = stop - snapshot.Price;
                if (riskPoints <= 0m) return null;
                target = snapshot.Price - riskPoints * _parameters.RewardMultiple;
                if (target <= 0m) return null;
            }

            var signalId = string.Format("NES-{0:yyyyMMddHHmmssfff}-{1}", now, market.Direction == TradeDirection.Long ? "L" : "S");
            return new TradeCandidate(signalId, "NES", market.Direction, snapshot.Price, stop, target, market.SetupScore, 1, now);
        }

        private bool IsRange(MarketSnapshot s)
        {
            if (s.Atr <= 0m) return true;

            var emaCompressed = Math.Abs(s.Ema20 - s.Ema50) / s.Atr <= _parameters.RangeMaxEmaSeparationAtrRatio;
            var priceNearVwap = Math.Abs(s.Price - s.SessionVwap) / s.Atr <= _parameters.RangeMaxPriceVwapAtrRatio;
            var flatSlope = Math.Abs(s.Ema20Slope) <= _parameters.RangeMaxAbsEma20Slope;
            var noDirectionalStructure = !(s.HigherHigh && s.HigherLow) && !(s.LowerHigh && s.LowerLow);

            return emaCompressed && priceNearVwap && flatSlope && noDirectionalStructure;
        }

        private bool IsChasing(MarketSnapshot s)
        {
            var distances = new List<decimal>();
            if (s.PullbackTouchedVwap) distances.Add(Math.Abs(s.Price - s.SessionVwap));
            if (s.PullbackTouchedEma20) distances.Add(Math.Abs(s.Price - s.Ema20));
            if (s.PullbackTouchedStructure)
            {
                distances.Add(Math.Min(Math.Abs(s.Price - s.SwingLow), Math.Abs(s.Price - s.SwingHigh)));
            }

            if (distances.Count == 0) return true;
            return distances.Min() > _parameters.MaxEntryDistanceFromPullbackPoints;
        }

        private MarketState State(MarketRegime regime, TradeDirection direction, string setup, int score,
            VolatilityState volatility, bool valid, MarketSnapshot snapshot, DateTime now, string reason)
        {
            var generated = snapshot.TimestampUtc <= now ? snapshot.TimestampUtc : now;
            return new MarketState(
                "NES",
                regime,
                direction,
                setup,
                score,
                volatility,
                valid,
                generated,
                generated.AddSeconds(_parameters.MarketStateTtlSeconds),
                new[] { reason });
        }
    }
}
