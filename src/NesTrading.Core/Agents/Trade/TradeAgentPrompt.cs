using System;
using Newtonsoft.Json;

namespace NesTrading.Core.Agents.Trade
{
    public static class TradeAgentPrompt
    {
        public const string Version = "trade.prompt.v1";

        public static string Build(TradeAgentContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));

            var payload = JsonConvert.SerializeObject(new
            {
                symbol = "NES",
                market = new
                {
                    regime = context.Market.Payload.Regime.ToString(),
                    direction = context.Market.Payload.Direction.ToString(),
                    setup = context.Market.Payload.Setup,
                    setup_score = context.Market.Payload.SetupScore
                },
                news = new
                {
                    state = context.News.Payload.State.ToString(),
                    confidence = context.News.Payload.Confidence
                },
                candidate = context.Candidate == null ? null : new
                {
                    signal_id = context.Candidate.SignalId,
                    direction = context.Candidate.Direction.ToString(),
                    entry = context.Candidate.EntryPrice,
                    structural_stop = context.Candidate.StructuralStop,
                    target = context.Candidate.TargetPrice,
                    contracts = context.Candidate.Contracts
                },
                account_equity_usd = context.Account.EquityUsd,
                session = new
                {
                    context.Session.TradesToday,
                    context.Session.LosingTradesToday,
                    context.Session.HasOpenPosition,
                    context.Session.BrokerPositionKnown
                }
            });

            return
                "You are the constrained Trade Agent for NES V1. " +
                "Choose exactly one decision: LONG, SHORT, WAIT, or EXIT_RECOMMENDATION. " +
                "Do not change position size. Do not change stop or target. " +
                "Do not invent market state. If uncertain, choose WAIT. " +
                "The deterministic Risk Engine has final authority and may reject any recommendation. " +
                "Return only JSON matching trade.v1. Context: " + payload;
        }
    }
}
