using System;

namespace NesTrading.Core.Agents.Budget
{
    public enum AgentRole
    {
        News,
        Trade
    }

    public sealed class AiUsagePolicy
    {
        public AiUsagePolicy(
            int maxCallsPerSession,
            int maxCallsPerAgentPerSession,
            int maxInputTokensPerSession,
            int maxOutputTokensPerSession,
            decimal maxCostUsdPerSession,
            decimal estimatedInputCostPerThousandTokensUsd,
            decimal estimatedOutputCostPerThousandTokensUsd)
        {
            if (maxCallsPerSession <= 0) throw new ArgumentOutOfRangeException(nameof(maxCallsPerSession));
            if (maxCallsPerAgentPerSession <= 0 || maxCallsPerAgentPerSession > maxCallsPerSession)
                throw new ArgumentOutOfRangeException(nameof(maxCallsPerAgentPerSession));
            if (maxInputTokensPerSession <= 0) throw new ArgumentOutOfRangeException(nameof(maxInputTokensPerSession));
            if (maxOutputTokensPerSession <= 0) throw new ArgumentOutOfRangeException(nameof(maxOutputTokensPerSession));
            if (maxCostUsdPerSession <= 0m) throw new ArgumentOutOfRangeException(nameof(maxCostUsdPerSession));
            if (estimatedInputCostPerThousandTokensUsd < 0m)
                throw new ArgumentOutOfRangeException(nameof(estimatedInputCostPerThousandTokensUsd));
            if (estimatedOutputCostPerThousandTokensUsd < 0m)
                throw new ArgumentOutOfRangeException(nameof(estimatedOutputCostPerThousandTokensUsd));

            MaxCallsPerSession = maxCallsPerSession;
            MaxCallsPerAgentPerSession = maxCallsPerAgentPerSession;
            MaxInputTokensPerSession = maxInputTokensPerSession;
            MaxOutputTokensPerSession = maxOutputTokensPerSession;
            MaxCostUsdPerSession = maxCostUsdPerSession;
            EstimatedInputCostPerThousandTokensUsd = estimatedInputCostPerThousandTokensUsd;
            EstimatedOutputCostPerThousandTokensUsd = estimatedOutputCostPerThousandTokensUsd;
        }

        public int MaxCallsPerSession { get; }
        public int MaxCallsPerAgentPerSession { get; }
        public int MaxInputTokensPerSession { get; }
        public int MaxOutputTokensPerSession { get; }
        public decimal MaxCostUsdPerSession { get; }
        public decimal EstimatedInputCostPerThousandTokensUsd { get; }
        public decimal EstimatedOutputCostPerThousandTokensUsd { get; }

        public decimal EstimateCost(int inputTokens, int outputTokens)
        {
            return inputTokens * EstimatedInputCostPerThousandTokensUsd / 1000m +
                   outputTokens * EstimatedOutputCostPerThousandTokensUsd / 1000m;
        }

        public static AiUsagePolicy ConservativeV1()
        {
            return new AiUsagePolicy(
                maxCallsPerSession: 20,
                maxCallsPerAgentPerSession: 10,
                maxInputTokensPerSession: 50000,
                maxOutputTokensPerSession: 8000,
                maxCostUsdPerSession: 1.00m,
                estimatedInputCostPerThousandTokensUsd: 0.10m,
                estimatedOutputCostPerThousandTokensUsd: 0.40m);
        }
    }
}
