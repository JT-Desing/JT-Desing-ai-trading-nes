using NesTrading.Core.Domain;

namespace NesTrading.Core.Agents
{
    public sealed class AgentStateSnapshot
    {
        public AgentStateSnapshot(
            AgentEnvelope<NewsState> news,
            AgentEnvelope<MarketState> market,
            AgentEnvelope<TradeDecision> trade)
        {
            News = news;
            Market = market;
            Trade = trade;
        }

        public AgentEnvelope<NewsState> News { get; }
        public AgentEnvelope<MarketState> Market { get; }
        public AgentEnvelope<TradeDecision> Trade { get; }

        public bool HasFreshNews => News != null;
        public bool HasFreshMarket => Market != null;
        public bool HasFreshTrade => Trade != null;
        public bool IsTradableSet => HasFreshNews && HasFreshMarket && HasFreshTrade;
    }
}
