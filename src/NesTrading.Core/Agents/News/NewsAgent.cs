using System;
using System.Threading;
using System.Threading.Tasks;
using NesTrading.Core.Agents.Budget;
using NesTrading.Core.Agents.Providers;
using NesTrading.Core.Domain;

namespace NesTrading.Core.Agents.News
{
    public sealed class NewsAgent
    {
        private readonly IAgentTextProvider _provider;
        private readonly AgentStateCache _cache;
        private readonly AiUsageBudget _budget;
        private readonly string _model;
        private readonly TimeSpan _providerTimeout;
        private readonly int _maxOutputTokens;

        public NewsAgent(
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

        public Task<AgentRefreshResult<NewsState>> RefreshAsync(
            string sourceContext,
            DateTime utcNow,
            CancellationToken cancellationToken) =>
            RefreshCoreAsync(sourceContext, utcNow, cancellationToken, true);

        public Task<AgentRefreshResult<NewsState>> ValidateWithoutPublishAsync(
            string sourceContext,
            DateTime utcNow,
            CancellationToken cancellationToken) =>
            RefreshCoreAsync(sourceContext, utcNow, cancellationToken, false);

        private async Task<AgentRefreshResult<NewsState>> RefreshCoreAsync(
            string sourceContext,
            DateTime utcNow,
            CancellationToken cancellationToken,
            bool publish)
        {
            if (sourceContext == null) throw new ArgumentNullException(nameof(sourceContext));
            if (utcNow.Kind != DateTimeKind.Utc) throw new ArgumentException("utcNow must be UTC.", nameof(utcNow));

            var prompt = NewsAgentPrompt.Build(sourceContext);
            var estimatedInputTokens = EstimateTokens(prompt);
            var reservation = _budget.TryReserve(
                AgentRole.News,
                estimatedInputTokens,
                _maxOutputTokens,
                utcNow);

            if (!reservation.Granted)
                return AgentRefreshResult<NewsState>.Failure("BUDGET_EXHAUSTED");

            var request = new ProviderRequest(
                "news-agent",
                _model,
                NewsAgentPrompt.Version,
                AgentSchemaVersion.NewsV1,
                NewsAgentSchema.JsonSchema,
                prompt,
                DateTime.UtcNow.Add(_providerTimeout),
                _maxOutputTokens);

            var completion = await AgentProviderExecutor.CompleteAsync(
                _provider,
                request,
                cancellationToken).ConfigureAwait(false);

            var usage = _budget.Settle(reservation, completion);

            var validation = AgentSchemaValidator.ValidateNews(
                completion.OutputText,
                completion,
                NewsAgentPrompt.Version,
                utcNow);

            if (!validation.IsValid)
                return AgentRefreshResult<NewsState>.Failure(validation.ReasonCode, completion, usage);

            var envelope = validation.Value;

            if (envelope.GeneratedAtUtc > utcNow.AddSeconds(5))
                return AgentRefreshResult<NewsState>.Failure("TIMESTAMP_IN_FUTURE", completion, usage);

            if (envelope.ValidUntilUtc < utcNow)
                return AgentRefreshResult<NewsState>.Failure("STATE_EXPIRED_BEFORE_PUBLISH", completion, usage);

            if (!publish)
                return AgentRefreshResult<NewsState>.ValidatedState(envelope, completion, usage);

            if (!_cache.TryPublishNews(envelope))
                return AgentRefreshResult<NewsState>.Failure("CACHE_REJECTED", completion, usage);

            return AgentRefreshResult<NewsState>.PublishedState(envelope, completion, usage);
        }

        private static int EstimateTokens(string text)
        {
            if (string.IsNullOrEmpty(text)) return 1;
            return Math.Max(1, (text.Length + 3) / 4);
        }
    }
}
