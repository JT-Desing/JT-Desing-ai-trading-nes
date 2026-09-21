using System;
using System.Threading;
using System.Threading.Tasks;
using NesTrading.Core.Agents.Budget;
using NesTrading.Core.Agents.Providers;
using NesTrading.Core.Domain;

namespace NesTrading.Core.Agents.Trade
{
    public sealed class TradeAgent
    {
        private readonly IAgentTextProvider _provider;
        private readonly AgentStateCache _cache;
        private readonly AiUsageBudget _budget;
        private readonly string _model;
        private readonly TimeSpan _providerTimeout;
        private readonly int _maxOutputTokens;

        public TradeAgent(
            IAgentTextProvider provider,
            AgentStateCache cache,
            AiUsageBudget budget,
            string model,
            TimeSpan providerTimeout,
            int maxOutputTokens)
        {
            _provider = provider ?? throw new ArgumentNullException(nameof(provider));
            _cache = cache ?? throw new ArgumentNullException(nameof(cache));
            _budget = budget ?? throw new ArgumentNullException(nameof(budget));
            if (string.IsNullOrWhiteSpace(model)) throw new ArgumentException("model is required.", nameof(model));
            if (providerTimeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(providerTimeout));
            if (maxOutputTokens <= 0) throw new ArgumentOutOfRangeException(nameof(maxOutputTokens));

            _model = model;
            _providerTimeout = providerTimeout;
            _maxOutputTokens = maxOutputTokens;
        }

        public Task<AgentRefreshResult<TradeDecision>> RefreshAsync(
            TradeAgentContext context,
            DateTime utcNow,
            CancellationToken cancellationToken) =>
            RefreshCoreAsync(context, utcNow, cancellationToken, true);

        public Task<AgentRefreshResult<TradeDecision>> ValidateWithoutPublishAsync(
            TradeAgentContext context,
            DateTime utcNow,
            CancellationToken cancellationToken) =>
            RefreshCoreAsync(context, utcNow, cancellationToken, false);

        private async Task<AgentRefreshResult<TradeDecision>> RefreshCoreAsync(
            TradeAgentContext context,
            DateTime utcNow,
            CancellationToken cancellationToken,
            bool publish)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (utcNow.Kind != DateTimeKind.Utc) throw new ArgumentException("utcNow must be UTC.", nameof(utcNow));

            var guard = Guard(context, utcNow);
            if (guard != null)
                return AgentRefreshResult<TradeDecision>.Skipped(guard);

            var prompt = TradeAgentPrompt.Build(context);
            var reservation = _budget.TryReserve(
                AgentRole.Trade,
                EstimateTokens(prompt),
                _maxOutputTokens,
                utcNow);

            if (!reservation.Granted)
                return AgentRefreshResult<TradeDecision>.Failure("BUDGET_EXHAUSTED");

            var request = new ProviderRequest(
                "trade-agent",
                _model,
                TradeAgentPrompt.Version,
                AgentSchemaVersion.TradeV1,
                TradeAgentSchema.JsonSchema,
                prompt,
                DateTime.UtcNow.Add(_providerTimeout),
                _maxOutputTokens);

            var completion = await AgentProviderExecutor.CompleteAsync(
                _provider,
                request,
                cancellationToken).ConfigureAwait(false);

            var usage = _budget.Settle(reservation, completion);

            var validation = AgentSchemaValidator.ValidateTrade(
                completion.OutputText,
                completion,
                TradeAgentPrompt.Version,
                utcNow,
                context.Candidate.SignalId);

            if (!validation.IsValid)
                return AgentRefreshResult<TradeDecision>.Failure(validation.ReasonCode, completion, usage);

            var envelope = validation.Value;
            if (envelope.GeneratedAtUtc > utcNow.AddSeconds(5))
                return AgentRefreshResult<TradeDecision>.Failure("TIMESTAMP_IN_FUTURE", completion, usage);
            if (envelope.ValidUntilUtc < utcNow)
                return AgentRefreshResult<TradeDecision>.Failure("STATE_EXPIRED_BEFORE_PUBLISH", completion, usage);

            if (envelope.Payload.Action == TradeAction.Long && context.Candidate.Direction != TradeDirection.Long)
                return AgentRefreshResult<TradeDecision>.Failure("TRADE_DIRECTION_CONTRADICTION", completion, usage);
            if (envelope.Payload.Action == TradeAction.Short && context.Candidate.Direction != TradeDirection.Short)
                return AgentRefreshResult<TradeDecision>.Failure("TRADE_DIRECTION_CONTRADICTION", completion, usage);

            if (!publish)
                return AgentRefreshResult<TradeDecision>.ValidatedState(envelope, completion, usage);

            if (!_cache.TryPublishTrade(envelope))
                return AgentRefreshResult<TradeDecision>.Failure("CACHE_REJECTED", completion, usage);

            return AgentRefreshResult<TradeDecision>.PublishedState(envelope, completion, usage);
        }

        private static string Guard(TradeAgentContext context, DateTime utcNow)
        {
            if (!context.SystemAllowsTrading) return "SYSTEM_STATE_LOCKED";
            if (!context.Session.BrokerPositionKnown) return "BROKER_POSITION_UNKNOWN";
            if (context.Session.HasOpenPosition) return "POSITION_ALREADY_OPEN";
            if (context.Candidate == null) return "NO_TECHNICAL_CANDIDATE";
            if (!context.Market.IsFreshAt(utcNow)) return "STALE_MARKET_STATE";
            if (!context.News.IsFreshAt(utcNow)) return "STALE_NEWS_STATE";
            if (context.News.Payload.State == NewsGate.Block) return "NEWS_BLOCK";
            if (context.News.Payload.State == NewsGate.Caution && context.Market.Payload.SetupScore < 85)
                return "NEWS_CAUTION_SCORE_TOO_LOW";
            if (context.Market.Payload.Direction != context.Candidate.Direction)
                return "MARKET_DIRECTION_MISMATCH";
            return null;
        }

        private static int EstimateTokens(string text)
        {
            if (string.IsNullOrEmpty(text)) return 1;
            return Math.Max(1, (text.Length + 3) / 4);
        }
    }
}
