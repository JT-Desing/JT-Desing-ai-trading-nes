using System;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using NesTrading.Core.Agents.Market;
using NesTrading.Core.Agents.News;
using NesTrading.Core.Agents.Trade;
using NesTrading.Core.Domain;
using NesTrading.Core.Storage;
using NesTrading.Core.Strategy;

namespace NesTrading.Core.Agents
{
    public sealed class AgentCoordinator
    {
        private readonly string _sessionId;
        private readonly MarketAgent _marketAgent;
        private readonly NewsAgent _newsAgent;
        private readonly TradeAgent _tradeAgent;
        private readonly TrendPullbackStrategy _strategy;
        private readonly AgentStateCache _cache;
        private readonly IEventStore _eventStore;

        public AgentCoordinator(
            string sessionId,
            MarketAgent marketAgent,
            NewsAgent newsAgent,
            TradeAgent tradeAgent,
            TrendPullbackStrategy strategy,
            AgentStateCache cache,
            IEventStore eventStore)
        {
            if (string.IsNullOrWhiteSpace(sessionId)) throw new ArgumentException("sessionId is required.", nameof(sessionId));
            _sessionId = sessionId;
            _marketAgent = marketAgent ?? throw new ArgumentNullException(nameof(marketAgent));
            _newsAgent = newsAgent ?? throw new ArgumentNullException(nameof(newsAgent));
            _tradeAgent = tradeAgent ?? throw new ArgumentNullException(nameof(tradeAgent));
            _strategy = strategy ?? throw new ArgumentNullException(nameof(strategy));
            _cache = cache ?? throw new ArgumentNullException(nameof(cache));
            _eventStore = eventStore ?? throw new ArgumentNullException(nameof(eventStore));
        }

        public async Task<AgentCycleResult> RefreshAsync(AgentCycleInput input, CancellationToken cancellationToken)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));
            var cycleId = "cycle-" + Guid.NewGuid().ToString("N");
            var now = input.UtcNow;

            var market = _marketAgent.Evaluate(input.MarketSnapshot, now);
            if (!TryAudit("MARKET_AGENT_STATE", now, cycleId, market.Payload.ReasonCodes, market, null))
                return Fail(cycleId, "AUDIT_PERSISTENCE_FAILURE", market, null, null, null);
            if (!_cache.TryPublishMarket(market))
                return Fail(cycleId, "CACHE_REJECTED", market, null, null, null);

            if (!TryAudit("NEWS_AGENT_REQUESTED", now, cycleId, new[] { "REQUESTED" }, null, null))
                return Fail(cycleId, "AUDIT_PERSISTENCE_FAILURE", market, null, null, null);

            var newsRefresh = await _newsAgent.ValidateWithoutPublishAsync(
                input.NewsSourceContext,
                now,
                cancellationToken).ConfigureAwait(false);

            if (!newsRefresh.Success)
            {
                TryAuditFailure("NEWS_AGENT_FAILED", now, cycleId, newsRefresh.ReasonCode);
                return Fail(cycleId, newsRefresh.ReasonCode, market, null, null, null);
            }

            var news = newsRefresh.State;
            if (!TryAudit("NEWS_AGENT_STATE", now, cycleId, news.Payload.ReasonCodes, news, newsRefresh.Completion))
                return Fail(cycleId, "AUDIT_PERSISTENCE_FAILURE", market, null, null, null);
            if (!_cache.TryPublishNews(news))
                return Fail(cycleId, "CACHE_REJECTED", market, null, null, null);

            if (news.Payload.State == NewsGate.Block)
            {
                TryAuditComplete(now, cycleId, "NEWS_BLOCK", false);
                return Fail(cycleId, "NEWS_BLOCK", market, news, null, null);
            }

            var candidate = _strategy.BuildTechnicalCandidate(market.Payload, input.MarketSnapshot);
            if (candidate == null)
            {
                TryAuditComplete(now, cycleId, "NO_TECHNICAL_CANDIDATE", false);
                return Fail(cycleId, "NO_TECHNICAL_CANDIDATE", market, news, null, null);
            }

            if (!TryAudit("TRADE_AGENT_REQUESTED", now, cycleId, new[] { "REQUESTED" }, null, null, candidate.SignalId))
                return Fail(cycleId, "AUDIT_PERSISTENCE_FAILURE", market, news, null, candidate);

            var tradeContext = new TradeAgentContext(
                market,
                news,
                candidate,
                input.Account,
                input.Session,
                input.SystemAllowsTrading);

            var tradeRefresh = await _tradeAgent.ValidateWithoutPublishAsync(
                tradeContext,
                now,
                cancellationToken).ConfigureAwait(false);

            if (!tradeRefresh.Success)
            {
                TryAuditFailure("TRADE_AGENT_FAILED", now, cycleId, tradeRefresh.ReasonCode, candidate.SignalId);
                return Fail(cycleId, tradeRefresh.ReasonCode, market, news, null, candidate);
            }

            var trade = tradeRefresh.State;
            if (!TryAudit("TRADE_AGENT_STATE", now, cycleId, trade.Payload.ReasonCodes, trade, tradeRefresh.Completion, candidate.SignalId))
                return Fail(cycleId, "AUDIT_PERSISTENCE_FAILURE", market, news, null, candidate);
            if (!_cache.TryPublishTrade(trade))
                return Fail(cycleId, "CACHE_REJECTED", market, news, null, candidate);

            var tradable =
                trade.Payload.Action == TradeAction.Long ||
                trade.Payload.Action == TradeAction.Short;

            var reason = tradable ? "AGENT_CYCLE_READY" :
                trade.Payload.Action == TradeAction.Wait ? "TRADE_WAIT" : "EXIT_RECOMMENDATION_ONLY";

            if (!TryAuditComplete(now, cycleId, reason, tradable, candidate.SignalId))
                return Fail(cycleId, "AUDIT_PERSISTENCE_FAILURE", market, news, trade, candidate);

            return new AgentCycleResult(cycleId, tradable, reason, market, news, trade, candidate);
        }

        private AgentCycleResult Fail(
            string cycleId,
            string reason,
            AgentEnvelope<MarketState> market,
            AgentEnvelope<NewsState> news,
            AgentEnvelope<TradeDecision> trade,
            TradeCandidate candidate)
        {
            return new AgentCycleResult(cycleId, false, reason, market, news, trade, candidate);
        }

        private bool TryAuditFailure(string type, DateTime now, string cycleId, string reason, string signalId = null)
        {
            try
            {
                _eventStore.Append(TradingEvent.Create(
                    EventId(), _sessionId, type, now,
                    JsonConvert.SerializeObject(new { cycle_id = cycleId, reason_code = reason }),
                    signalId));
                return true;
            }
            catch { return false; }
        }

        private bool TryAuditComplete(DateTime now, string cycleId, string reason, bool tradable, string signalId = null)
        {
            try
            {
                _eventStore.Append(TradingEvent.Create(
                    EventId(), _sessionId, "AGENT_CYCLE_COMPLETE", now,
                    JsonConvert.SerializeObject(new
                    {
                        cycle_id = cycleId,
                        reason_code = reason,
                        tradable
                    }),
                    signalId));
                return true;
            }
            catch { return false; }
        }

        private bool TryAudit<T>(
            string type,
            DateTime now,
            string cycleId,
            System.Collections.Generic.IEnumerable<string> reasons,
            AgentEnvelope<T> envelope,
            ProviderCompletion completion,
            string signalId = null)
        {
            try
            {
                _eventStore.Append(TradingEvent.Create(
                    EventId(), _sessionId, type, now,
                    JsonConvert.SerializeObject(new
                    {
                        cycle_id = cycleId,
                        agent_name = envelope == null ? null : envelope.AgentName,
                        schema_version = envelope == null ? null : envelope.SchemaVersion,
                        prompt_version = envelope == null ? null : envelope.PromptVersion,
                        provider_name = envelope == null ? null : envelope.ProviderName,
                        model_name = envelope == null ? null : envelope.ModelName,
                        generated_at_utc = envelope == null ? (DateTime?)null : envelope.GeneratedAtUtc,
                        valid_until_utc = envelope == null ? (DateTime?)null : envelope.ValidUntilUtc,
                        status = completion == null ? "DETERMINISTIC_OR_REQUEST" : completion.Status.ToString(),
                        reason_codes = reasons,
                        raw_response_hash = envelope == null ? null : envelope.RawResponseHash,
                        input_tokens = completion == null ? 0 : completion.InputTokens,
                        output_tokens = completion == null ? 0 : completion.OutputTokens,
                        reported_cost_usd = completion == null ? null : completion.ReportedCostUsd
                    }),
                    signalId));
                return true;
            }
            catch { return false; }
        }

        private static string EventId() => "evt-" + Guid.NewGuid().ToString("N");
    }
}
