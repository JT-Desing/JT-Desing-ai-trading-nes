using NesTrading.Core.Domain;

namespace NesTrading.Core.Strategy
{
    public sealed class SetupScorer
    {
        private readonly StrategyParameters _parameters;

        public SetupScorer(StrategyParameters parameters)
        {
            _parameters = parameters;
        }

        public int Score(MarketSnapshot s, TradeDirection direction)
        {
            if (!s.EntryConfirmed) return 0;
            if (!(s.PullbackTouchedVwap || s.PullbackTouchedEma20 || s.PullbackTouchedStructure)) return 0;

            var score = 0;

            if (direction == TradeDirection.Long)
            {
                if (s.Ema20Slope > 0m) score += 20;
                if (s.Price > s.SessionVwap) score += 15;
                if (s.Ema20 > s.Ema50) score += 15;
                if (s.HigherHigh && s.HigherLow) score += 20;
            }
            else if (direction == TradeDirection.Short)
            {
                if (s.Ema20Slope < 0m) score += 20;
                if (s.Price < s.SessionVwap) score += 15;
                if (s.Ema20 < s.Ema50) score += 15;
                if (s.LowerHigh && s.LowerLow) score += 20;
            }

            score += 15; // confirmed pullback quality

            if (s.ReferenceVolume > 0m && s.CurrentVolume / s.ReferenceVolume >= _parameters.MinVolumeRatio)
                score += 10;

            if (s.Atr >= _parameters.MinAtrPoints && s.Atr <= _parameters.MaxAtrPoints)
                score += 5;

            return score;
        }
    }
}
