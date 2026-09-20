using System;

namespace NesTrading.Core.Runtime
{
    public sealed class TradingSessionState
    {
        public TradingSessionState(
            int tradesToday = 0,
            int losingTradesToday = 0,
            bool hasOpenPosition = false,
            decimal dailyPnlUsd = 0m,
            DateTime? lastTradeClosedAtUtc = null,
            bool brokerPositionKnown = true)
        {
            if (tradesToday < 0) throw new ArgumentOutOfRangeException(nameof(tradesToday));
            if (losingTradesToday < 0 || losingTradesToday > tradesToday) throw new ArgumentOutOfRangeException(nameof(losingTradesToday));
            if (lastTradeClosedAtUtc.HasValue && lastTradeClosedAtUtc.Value.Kind != DateTimeKind.Utc)
                throw new ArgumentException("lastTradeClosedAtUtc must be UTC.", nameof(lastTradeClosedAtUtc));

            TradesToday = tradesToday;
            LosingTradesToday = losingTradesToday;
            HasOpenPosition = hasOpenPosition;
            DailyPnlUsd = dailyPnlUsd;
            LastTradeClosedAtUtc = lastTradeClosedAtUtc;
            BrokerPositionKnown = brokerPositionKnown;
        }

        public int TradesToday { get; }
        public int LosingTradesToday { get; }
        public bool HasOpenPosition { get; }
        public decimal DailyPnlUsd { get; }
        public DateTime? LastTradeClosedAtUtc { get; }
        public bool BrokerPositionKnown { get; }
    }
}
