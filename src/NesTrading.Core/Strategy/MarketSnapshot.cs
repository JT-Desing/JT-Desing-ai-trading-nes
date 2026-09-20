using System;

namespace NesTrading.Core.Strategy
{
    public sealed class MarketSnapshot
    {
        public MarketSnapshot(
            decimal price,
            decimal sessionVwap,
            decimal ema20,
            decimal ema50,
            decimal ema20Slope,
            decimal atr,
            decimal currentVolume,
            decimal referenceVolume,
            decimal swingHigh,
            decimal swingLow,
            bool higherHigh,
            bool higherLow,
            bool lowerHigh,
            bool lowerLow,
            decimal spreadTicks,
            bool pullbackTouchedVwap,
            bool pullbackTouchedEma20,
            bool pullbackTouchedStructure,
            bool entryConfirmed,
            DateTime timestampUtc)
        {
            if (timestampUtc.Kind != DateTimeKind.Utc) throw new ArgumentException("timestampUtc must be UTC.", nameof(timestampUtc));
            if (price <= 0m) throw new ArgumentOutOfRangeException(nameof(price));
            if (sessionVwap <= 0m) throw new ArgumentOutOfRangeException(nameof(sessionVwap));
            if (ema20 <= 0m || ema50 <= 0m) throw new ArgumentOutOfRangeException("EMA values must be positive.");
            if (atr < 0m) throw new ArgumentOutOfRangeException(nameof(atr));
            if (currentVolume < 0m || referenceVolume < 0m) throw new ArgumentOutOfRangeException("Volume cannot be negative.");
            if (swingHigh <= 0m || swingLow <= 0m || swingHigh < swingLow) throw new ArgumentException("Swing levels are invalid.");
            if (spreadTicks < 0m) throw new ArgumentOutOfRangeException(nameof(spreadTicks));

            Price = price;
            SessionVwap = sessionVwap;
            Ema20 = ema20;
            Ema50 = ema50;
            Ema20Slope = ema20Slope;
            Atr = atr;
            CurrentVolume = currentVolume;
            ReferenceVolume = referenceVolume;
            SwingHigh = swingHigh;
            SwingLow = swingLow;
            HigherHigh = higherHigh;
            HigherLow = higherLow;
            LowerHigh = lowerHigh;
            LowerLow = lowerLow;
            SpreadTicks = spreadTicks;
            PullbackTouchedVwap = pullbackTouchedVwap;
            PullbackTouchedEma20 = pullbackTouchedEma20;
            PullbackTouchedStructure = pullbackTouchedStructure;
            EntryConfirmed = entryConfirmed;
            TimestampUtc = timestampUtc;
        }

        public decimal Price { get; }
        public decimal SessionVwap { get; }
        public decimal Ema20 { get; }
        public decimal Ema50 { get; }
        public decimal Ema20Slope { get; }
        public decimal Atr { get; }
        public decimal CurrentVolume { get; }
        public decimal ReferenceVolume { get; }
        public decimal SwingHigh { get; }
        public decimal SwingLow { get; }
        public bool HigherHigh { get; }
        public bool HigherLow { get; }
        public bool LowerHigh { get; }
        public bool LowerLow { get; }
        public decimal SpreadTicks { get; }
        public bool PullbackTouchedVwap { get; }
        public bool PullbackTouchedEma20 { get; }
        public bool PullbackTouchedStructure { get; }
        public bool EntryConfirmed { get; }
        public DateTime TimestampUtc { get; }
    }

    public sealed class StrategyParameters
    {
        public StrategyParameters(
            decimal minAtrPoints,
            decimal maxAtrPoints,
            decimal minVolumeRatio,
            decimal maxEntryDistanceFromPullbackPoints,
            decimal stopBufferPoints,
            decimal rewardMultiple,
            decimal rangeMaxEmaSeparationAtrRatio,
            decimal rangeMaxPriceVwapAtrRatio,
            decimal rangeMaxAbsEma20Slope,
            int marketStateTtlSeconds)
        {
            if (minAtrPoints < 0m || maxAtrPoints <= minAtrPoints) throw new ArgumentException("ATR range is invalid.");
            if (minVolumeRatio < 0m) throw new ArgumentOutOfRangeException(nameof(minVolumeRatio));
            if (maxEntryDistanceFromPullbackPoints <= 0m) throw new ArgumentOutOfRangeException(nameof(maxEntryDistanceFromPullbackPoints));
            if (stopBufferPoints < 0m) throw new ArgumentOutOfRangeException(nameof(stopBufferPoints));
            if (rewardMultiple <= 0m) throw new ArgumentOutOfRangeException(nameof(rewardMultiple));
            if (rangeMaxEmaSeparationAtrRatio < 0m || rangeMaxPriceVwapAtrRatio < 0m || rangeMaxAbsEma20Slope < 0m)
                throw new ArgumentOutOfRangeException("Range thresholds cannot be negative.");
            if (marketStateTtlSeconds <= 0) throw new ArgumentOutOfRangeException(nameof(marketStateTtlSeconds));

            MinAtrPoints = minAtrPoints;
            MaxAtrPoints = maxAtrPoints;
            MinVolumeRatio = minVolumeRatio;
            MaxEntryDistanceFromPullbackPoints = maxEntryDistanceFromPullbackPoints;
            StopBufferPoints = stopBufferPoints;
            RewardMultiple = rewardMultiple;
            RangeMaxEmaSeparationAtrRatio = rangeMaxEmaSeparationAtrRatio;
            RangeMaxPriceVwapAtrRatio = rangeMaxPriceVwapAtrRatio;
            RangeMaxAbsEma20Slope = rangeMaxAbsEma20Slope;
            MarketStateTtlSeconds = marketStateTtlSeconds;
        }

        public decimal MinAtrPoints { get; }
        public decimal MaxAtrPoints { get; }
        public decimal MinVolumeRatio { get; }
        public decimal MaxEntryDistanceFromPullbackPoints { get; }
        public decimal StopBufferPoints { get; }
        public decimal RewardMultiple { get; }
        public decimal RangeMaxEmaSeparationAtrRatio { get; }
        public decimal RangeMaxPriceVwapAtrRatio { get; }
        public decimal RangeMaxAbsEma20Slope { get; }
        public int MarketStateTtlSeconds { get; }

        public static StrategyParameters V1Defaults()
        {
            return new StrategyParameters(
                minAtrPoints: 1m,
                maxAtrPoints: 20m,
                minVolumeRatio: 0.80m,
                maxEntryDistanceFromPullbackPoints: 3m,
                stopBufferPoints: 0.5m,
                rewardMultiple: 2m,
                rangeMaxEmaSeparationAtrRatio: 0.10m,
                rangeMaxPriceVwapAtrRatio: 0.10m,
                rangeMaxAbsEma20Slope: 0.05m,
                marketStateTtlSeconds: 30);
        }
    }
}
