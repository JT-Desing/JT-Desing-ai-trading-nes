using System;
using Newtonsoft.Json;
using NesTrading.Core.Domain;
using NesTrading.Core.Strategy;

namespace NesTrading.Core.Agents.Market
{
    public sealed class MarketAgent
    {
        private readonly TrendPullbackStrategy _strategy;

        public MarketAgent(TrendPullbackStrategy strategy)
        {
            _strategy = strategy ?? throw new ArgumentNullException(nameof(strategy));
        }

        public AgentEnvelope<MarketState> Evaluate(MarketSnapshot snapshot, DateTime utcNow)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            if (utcNow.Kind != DateTimeKind.Utc) throw new ArgumentException("utcNow must be UTC.", nameof(utcNow));

            var state = _strategy.EvaluateMarket(snapshot, utcNow);
            var raw = JsonConvert.SerializeObject(new
            {
                state.Symbol,
                regime = state.Regime.ToString(),
                direction = state.Direction.ToString(),
                state.Setup,
                state.SetupScore,
                volatility = state.Volatility.ToString(),
                state.Valid,
                state.GeneratedAtUtc,
                state.ValidUntilUtc,
                state.ReasonCodes
            });

            return new AgentEnvelope<MarketState>(
                "market-agent",
                AgentSchemaVersion.MarketV1,
                "none",
                "deterministic",
                "trend-pullback-vwap-v1",
                state.GeneratedAtUtc,
                state.ValidUntilUtc,
                utcNow,
                state,
                AgentEnvelope<MarketState>.HashRaw(raw));
        }
    }
}
