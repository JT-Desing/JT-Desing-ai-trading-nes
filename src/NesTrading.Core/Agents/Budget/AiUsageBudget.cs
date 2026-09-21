using System;
using System.Collections.Generic;

namespace NesTrading.Core.Agents.Budget
{
    public sealed class AiUsageBudget
    {
        private readonly object _sync = new object();
        private readonly AiUsagePolicy _policy;
        private readonly Dictionary<string, BudgetReservation> _pending =
            new Dictionary<string, BudgetReservation>(StringComparer.Ordinal);
        private readonly Dictionary<AgentRole, int> _callsByRole =
            new Dictionary<AgentRole, int>
            {
                [AgentRole.News] = 0,
                [AgentRole.Trade] = 0
            };

        private int _grantedCalls;
        private int _settledCalls;
        private int _settledInputTokens;
        private int _settledOutputTokens;
        private decimal _settledCostUsd;

        public AiUsageBudget(AiUsagePolicy policy)
        {
            _policy = policy ?? throw new ArgumentNullException(nameof(policy));
        }

        public BudgetReservation TryReserve(
            AgentRole role,
            int estimatedInputTokens,
            int maxOutputTokens,
            DateTime utcNow)
        {
            if (estimatedInputTokens < 0)
                throw new ArgumentOutOfRangeException(nameof(estimatedInputTokens));
            if (maxOutputTokens <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxOutputTokens));
            if (utcNow.Kind != DateTimeKind.Utc)
                throw new ArgumentException("utcNow must be UTC.", nameof(utcNow));

            lock (_sync)
            {
                if (_grantedCalls >= _policy.MaxCallsPerSession)
                    return BudgetReservation.Reject(role, estimatedInputTokens, maxOutputTokens, "MAX_SESSION_CALLS", utcNow);

                if (_callsByRole[role] >= _policy.MaxCallsPerAgentPerSession)
                    return BudgetReservation.Reject(role, estimatedInputTokens, maxOutputTokens, "MAX_AGENT_CALLS", utcNow);

                var pendingInput = 0;
                var pendingOutput = 0;
                var pendingCost = 0m;
                foreach (var reservation in _pending.Values)
                {
                    pendingInput += reservation.EstimatedInputTokens;
                    pendingOutput += reservation.MaxOutputTokens;
                    pendingCost += reservation.ReservedCostUsd;
                }

                if (_settledInputTokens + pendingInput + estimatedInputTokens > _policy.MaxInputTokensPerSession)
                    return BudgetReservation.Reject(role, estimatedInputTokens, maxOutputTokens, "MAX_INPUT_TOKENS", utcNow);

                if (_settledOutputTokens + pendingOutput + maxOutputTokens > _policy.MaxOutputTokensPerSession)
                    return BudgetReservation.Reject(role, estimatedInputTokens, maxOutputTokens, "MAX_OUTPUT_TOKENS", utcNow);

                var estimatedCost = _policy.EstimateCost(estimatedInputTokens, maxOutputTokens);
                if (_settledCostUsd + pendingCost + estimatedCost > _policy.MaxCostUsdPerSession)
                    return BudgetReservation.Reject(role, estimatedInputTokens, maxOutputTokens, "MAX_SESSION_COST", utcNow);

                var granted = BudgetReservation.Grant(
                    role,
                    estimatedInputTokens,
                    maxOutputTokens,
                    estimatedCost,
                    utcNow);

                _pending.Add(granted.ReservationId, granted);
                _grantedCalls++;
                _callsByRole[role]++;
                return granted;
            }
        }

        public AiUsageRecord Settle(BudgetReservation reservation, ProviderCompletion completion)
        {
            if (reservation == null) throw new ArgumentNullException(nameof(reservation));
            if (completion == null) throw new ArgumentNullException(nameof(completion));
            if (!reservation.Granted)
                throw new InvalidOperationException("Rejected reservations cannot be settled.");

            lock (_sync)
            {
                BudgetReservation pending;
                if (reservation.ReservationId == null ||
                    !_pending.TryGetValue(reservation.ReservationId, out pending))
                    throw new InvalidOperationException("Reservation is unknown or already settled.");

                _pending.Remove(reservation.ReservationId);

                var input = completion.InputTokens;
                var output = completion.OutputTokens;
                var cost = completion.ReportedCostUsd ??
                           _policy.EstimateCost(input, output);

                _settledCalls++;
                _settledInputTokens += input;
                _settledOutputTokens += output;
                _settledCostUsd += cost;

                return new AiUsageRecord(
                    reservation.ReservationId,
                    reservation.Role,
                    completion.Status,
                    input,
                    output,
                    cost);
            }
        }

        public AiUsageSnapshot GetSnapshot()
        {
            lock (_sync)
            {
                return new AiUsageSnapshot(
                    _settledCalls,
                    _settledInputTokens,
                    _settledOutputTokens,
                    _settledCostUsd,
                    _callsByRole);
            }
        }
    }
}
