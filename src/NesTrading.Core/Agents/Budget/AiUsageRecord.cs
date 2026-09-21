using System;
using System.Collections.Generic;

namespace NesTrading.Core.Agents.Budget
{
    public sealed class BudgetReservation
    {
        private BudgetReservation(
            bool granted,
            string reasonCode,
            string reservationId,
            AgentRole role,
            int estimatedInputTokens,
            int maxOutputTokens,
            decimal reservedCostUsd,
            DateTime reservedAtUtc)
        {
            Granted = granted;
            ReasonCode = reasonCode;
            ReservationId = reservationId;
            Role = role;
            EstimatedInputTokens = estimatedInputTokens;
            MaxOutputTokens = maxOutputTokens;
            ReservedCostUsd = reservedCostUsd;
            ReservedAtUtc = reservedAtUtc;
        }

        public bool Granted { get; }
        public string ReasonCode { get; }
        public string ReservationId { get; }
        public AgentRole Role { get; }
        public int EstimatedInputTokens { get; }
        public int MaxOutputTokens { get; }
        public decimal ReservedCostUsd { get; }
        public DateTime ReservedAtUtc { get; }

        internal static BudgetReservation Grant(
            AgentRole role,
            int inputTokens,
            int outputTokens,
            decimal reservedCostUsd,
            DateTime utcNow)
        {
            return new BudgetReservation(
                true,
                "RESERVED",
                Guid.NewGuid().ToString("N"),
                role,
                inputTokens,
                outputTokens,
                reservedCostUsd,
                utcNow);
        }

        internal static BudgetReservation Reject(
            AgentRole role,
            int inputTokens,
            int outputTokens,
            string reasonCode,
            DateTime utcNow)
        {
            return new BudgetReservation(
                false,
                reasonCode,
                null,
                role,
                inputTokens,
                outputTokens,
                0m,
                utcNow);
        }
    }

    public sealed class AiUsageRecord
    {
        public AiUsageRecord(
            string reservationId,
            AgentRole role,
            ProviderCompletionStatus completionStatus,
            int inputTokens,
            int outputTokens,
            decimal costUsd)
        {
            ReservationId = reservationId;
            Role = role;
            CompletionStatus = completionStatus;
            InputTokens = inputTokens;
            OutputTokens = outputTokens;
            CostUsd = costUsd;
        }

        public string ReservationId { get; }
        public AgentRole Role { get; }
        public ProviderCompletionStatus CompletionStatus { get; }
        public int InputTokens { get; }
        public int OutputTokens { get; }
        public decimal CostUsd { get; }
    }

    public sealed class AiUsageSnapshot
    {
        public AiUsageSnapshot(
            int settledCalls,
            int inputTokens,
            int outputTokens,
            decimal costUsd,
            IDictionary<AgentRole, int> callsByRole)
        {
            SettledCalls = settledCalls;
            InputTokens = inputTokens;
            OutputTokens = outputTokens;
            CostUsd = costUsd;
            CallsByRole = new Dictionary<AgentRole, int>(callsByRole);
        }

        public int SettledCalls { get; }
        public int InputTokens { get; }
        public int OutputTokens { get; }
        public decimal CostUsd { get; }
        public IReadOnlyDictionary<AgentRole, int> CallsByRole { get; }
    }
}
