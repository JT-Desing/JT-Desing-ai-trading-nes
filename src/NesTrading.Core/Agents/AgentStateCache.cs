using System;
using NesTrading.Core.Domain;

namespace NesTrading.Core.Agents
{
    public sealed class AgentStateCache
    {
        private readonly object _newsSync = new object();
        private readonly object _marketSync = new object();
        private readonly object _tradeSync = new object();

        private AgentEnvelope<NewsState> _news;
        private AgentEnvelope<MarketState> _market;
        private AgentEnvelope<TradeDecision> _trade;

        public bool TryPublishNews(AgentEnvelope<NewsState> state)
        {
            if (!CanPublish(state)) return false;
            lock (_newsSync)
            {
                if (_news != null && state.GeneratedAtUtc <= _news.GeneratedAtUtc) return false;
                _news = state;
                return true;
            }
        }

        public bool TryPublishMarket(AgentEnvelope<MarketState> state)
        {
            if (!CanPublish(state)) return false;
            lock (_marketSync)
            {
                if (_market != null && state.GeneratedAtUtc <= _market.GeneratedAtUtc) return false;
                _market = state;
                return true;
            }
        }

        public bool TryPublishTrade(AgentEnvelope<TradeDecision> state)
        {
            if (!CanPublish(state)) return false;
            lock (_tradeSync)
            {
                if (_trade != null && state.GeneratedAtUtc <= _trade.GeneratedAtUtc) return false;
                _trade = state;
                return true;
            }
        }

        public AgentStateSnapshot Read(DateTime utcNow)
        {
            if (utcNow.Kind != DateTimeKind.Utc)
                throw new ArgumentException("utcNow must be UTC.", nameof(utcNow));

            AgentEnvelope<NewsState> news;
            AgentEnvelope<MarketState> market;
            AgentEnvelope<TradeDecision> trade;

            lock (_newsSync) news = IsFresh(_news, utcNow) ? _news : null;
            lock (_marketSync) market = IsFresh(_market, utcNow) ? _market : null;
            lock (_tradeSync) trade = IsFresh(_trade, utcNow) ? _trade : null;

            return new AgentStateSnapshot(news, market, trade);
        }

        private static bool CanPublish<T>(AgentEnvelope<T> state)
        {
            if (state == null) return false;
            if (state.ReceivedAtUtc > state.ValidUntilUtc) return false;
            if (state.ReceivedAtUtc < state.GeneratedAtUtc) return false;
            return true;
        }

        private static bool IsFresh<T>(AgentEnvelope<T> state, DateTime utcNow)
        {
            return state != null &&
                   utcNow >= state.GeneratedAtUtc &&
                   utcNow <= state.ValidUntilUtc;
        }
    }
}
