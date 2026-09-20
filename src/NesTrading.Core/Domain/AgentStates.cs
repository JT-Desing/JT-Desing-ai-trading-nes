using System;
using System.Collections.Generic;
using System.Linq;

namespace NesTrading.Core.Domain
{
    public sealed class NewsState
    {
        public NewsState(string symbol, NewsGate state, DateTime generatedAtUtc, DateTime validUntilUtc, decimal confidence, IEnumerable<string> reasonCodes)
        {
            Symbol = Guard.NesSymbol(symbol);
            Guard.UtcWindow(generatedAtUtc, validUntilUtc);
            Guard.UnitInterval(confidence, nameof(confidence));

            State = state;
            GeneratedAtUtc = generatedAtUtc;
            ValidUntilUtc = validUntilUtc;
            Confidence = confidence;
            ReasonCodes = Guard.ReasonCodes(reasonCodes);
        }

        public string Symbol { get; }
        public NewsGate State { get; }
        public DateTime GeneratedAtUtc { get; }
        public DateTime ValidUntilUtc { get; }
        public decimal Confidence { get; }
        public IReadOnlyList<string> ReasonCodes { get; }
        public bool IsFreshAt(DateTime utcNow) => utcNow >= GeneratedAtUtc && utcNow <= ValidUntilUtc;
    }

    public sealed class MarketState
    {
        public MarketState(string symbol, MarketRegime regime, TradeDirection direction, string setup, int setupScore,
            VolatilityState volatility, bool valid, DateTime generatedAtUtc, DateTime validUntilUtc, IEnumerable<string> reasonCodes)
        {
            Symbol = Guard.NesSymbol(symbol);
            Guard.UtcWindow(generatedAtUtc, validUntilUtc);
            if (setupScore < 0 || setupScore > 100) throw new ArgumentOutOfRangeException(nameof(setupScore));

            Regime = regime;
            Direction = direction;
            Setup = setup ?? string.Empty;
            SetupScore = setupScore;
            Volatility = volatility;
            Valid = valid;
            GeneratedAtUtc = generatedAtUtc;
            ValidUntilUtc = validUntilUtc;
            ReasonCodes = Guard.ReasonCodes(reasonCodes);
        }

        public string Symbol { get; }
        public MarketRegime Regime { get; }
        public TradeDirection Direction { get; }
        public string Setup { get; }
        public int SetupScore { get; }
        public VolatilityState Volatility { get; }
        public bool Valid { get; }
        public DateTime GeneratedAtUtc { get; }
        public DateTime ValidUntilUtc { get; }
        public IReadOnlyList<string> ReasonCodes { get; }
        public bool IsFreshAt(DateTime utcNow) => utcNow >= GeneratedAtUtc && utcNow <= ValidUntilUtc;
    }

    public sealed class TradeDecision
    {
        public TradeDecision(string symbol, TradeAction action, decimal confidence, string signalId, DateTime generatedAtUtc,
            DateTime validUntilUtc, IEnumerable<string> reasonCodes)
        {
            Symbol = Guard.NesSymbol(symbol);
            SignalId = Guard.Required(signalId, nameof(signalId));
            Guard.UtcWindow(generatedAtUtc, validUntilUtc);
            Guard.UnitInterval(confidence, nameof(confidence));

            Action = action;
            Confidence = confidence;
            GeneratedAtUtc = generatedAtUtc;
            ValidUntilUtc = validUntilUtc;
            ReasonCodes = Guard.ReasonCodes(reasonCodes);
        }

        public string Symbol { get; }
        public TradeAction Action { get; }
        public decimal Confidence { get; }
        public string SignalId { get; }
        public DateTime GeneratedAtUtc { get; }
        public DateTime ValidUntilUtc { get; }
        public IReadOnlyList<string> ReasonCodes { get; }
        public bool IsFreshAt(DateTime utcNow) => utcNow >= GeneratedAtUtc && utcNow <= ValidUntilUtc;
    }

    internal static class Guard
    {
        internal static string Required(string value, string name)
        {
            if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("Value is required.", name);
            return value;
        }

        internal static string NesSymbol(string symbol)
        {
            if (!string.Equals(symbol, "NES", StringComparison.Ordinal))
                throw new ArgumentException("V1 supports NES only.", nameof(symbol));
            return symbol;
        }

        internal static void UtcWindow(DateTime generatedAtUtc, DateTime validUntilUtc)
        {
            if (generatedAtUtc.Kind != DateTimeKind.Utc || validUntilUtc.Kind != DateTimeKind.Utc)
                throw new ArgumentException("State timestamps must be UTC.");
            if (validUntilUtc < generatedAtUtc)
                throw new ArgumentException("validUntilUtc cannot precede generatedAtUtc.");
        }

        internal static void UnitInterval(decimal value, string name)
        {
            if (value < 0m || value > 1m) throw new ArgumentOutOfRangeException(name);
        }

        internal static IReadOnlyList<string> ReasonCodes(IEnumerable<string> reasonCodes)
        {
            return (reasonCodes ?? Enumerable.Empty<string>()).Where(x => !string.IsNullOrWhiteSpace(x)).ToArray();
        }
    }
}
