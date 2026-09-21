using NesTrading.Core.Domain;

namespace NesTrading.Core.Agents
{
    public sealed class AgentCycleResult
    {
        public AgentCycleResult(
            string cycleId,
            bool tradable,
            string reasonCode,
            AgentEnvelope<MarketState> market,
            AgentEnvelope<NewsState> news,
            AgentEnvelope<TradeDecision> trade,
            TradeCandidate candidate)
        {
            CycleId = cycleId;
            Tradable = tradable;
            ReasonCode = reasonCode;
            Market = market;
            News = news;
            Trade = trade;
            Candidate = candidate;
        }

        public string CycleId { get; }
        public bool Tradable { get; }
        public string ReasonCode { get; }
        public AgentEnvelope<MarketState> Market { get; }
        public AgentEnvelope<NewsState> News { get; }
        public AgentEnvelope<TradeDecision> Trade { get; }
        public TradeCandidate Candidate { get; }
    }
}
