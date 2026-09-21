using System;
using NesTrading.Core.Domain;
using NesTrading.Core.Risk;
using NesTrading.Core.Runtime;

namespace NesTrading.Core.Agents.Trade
{
    public sealed class TradeAgentContext
    {
        public TradeAgentContext(
            AgentEnvelope<MarketState> market,
            AgentEnvelope<NewsState> news,
            TradeCandidate candidate,
            AccountRiskState account,
            TradingSessionState session,
            bool systemAllowsTrading)
        {
            Market = market ?? throw new ArgumentNullException(nameof(market));
            News = news ?? throw new ArgumentNullException(nameof(news));
            Candidate = candidate;
            Account = account ?? throw new ArgumentNullException(nameof(account));
            Session = session ?? throw new ArgumentNullException(nameof(session));
            SystemAllowsTrading = systemAllowsTrading;
        }

        public AgentEnvelope<MarketState> Market { get; }
        public AgentEnvelope<NewsState> News { get; }
        public TradeCandidate Candidate { get; }
        public AccountRiskState Account { get; }
        public TradingSessionState Session { get; }
        public bool SystemAllowsTrading { get; }
    }
}
