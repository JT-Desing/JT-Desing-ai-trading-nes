using System;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using NesTrading.Core.Agents;
using NesTrading.Core.Agents.Budget;

namespace NesTrading.Core.Tests.Agents
{
    [TestFixture]
    public class AiUsageBudgetTests
    {
        private static readonly DateTime Now = new DateTime(2026, 9, 21, 14, 0, 0, DateTimeKind.Utc);

        private static AiUsagePolicy Policy(
            int maxCalls = 4,
            int maxCallsPerAgent = 3,
            int maxInput = 1000,
            int maxOutput = 400,
            decimal maxCost = 1.00m)
        {
            return new AiUsagePolicy(
                maxCalls,
                maxCallsPerAgent,
                maxInput,
                maxOutput,
                maxCost,
                estimatedInputCostPerThousandTokensUsd: 0.10m,
                estimatedOutputCostPerThousandTokensUsd: 0.40m);
        }

        [Test]
        public void Reserve_WithinAllCaps_IsGranted()
        {
            var budget = new AiUsageBudget(Policy());

            var r = budget.TryReserve(AgentRole.News, 100, 50, Now);

            Assert.That(r.Granted, Is.True);
            Assert.That(r.ReasonCode, Is.EqualTo("RESERVED"));
            Assert.That(r.EstimatedInputTokens, Is.EqualTo(100));
            Assert.That(r.MaxOutputTokens, Is.EqualTo(50));
        }

        [Test]
        public void Reserve_MaxSessionCallsReached_IsRejected()
        {
            var budget = new AiUsageBudget(Policy(maxCalls: 1, maxCallsPerAgent: 1));
            Assert.That(budget.TryReserve(AgentRole.News, 10, 10, Now).Granted, Is.True);

            var second = budget.TryReserve(AgentRole.Trade, 10, 10, Now);

            Assert.That(second.Granted, Is.False);
            Assert.That(second.ReasonCode, Is.EqualTo("MAX_SESSION_CALLS"));
        }

        [Test]
        public void Reserve_MaxCallsPerAgentReached_IsRejected()
        {
            var budget = new AiUsageBudget(Policy(maxCallsPerAgent: 1));
            Assert.That(budget.TryReserve(AgentRole.News, 10, 10, Now).Granted, Is.True);

            var second = budget.TryReserve(AgentRole.News, 10, 10, Now);

            Assert.That(second.Granted, Is.False);
            Assert.That(second.ReasonCode, Is.EqualTo("MAX_AGENT_CALLS"));
        }

        [Test]
        public void Reserve_InputTokenCapUsesPendingReservationsAtomically()
        {
            var budget = new AiUsageBudget(Policy(maxInput: 100));
            Assert.That(budget.TryReserve(AgentRole.News, 70, 10, Now).Granted, Is.True);

            var second = budget.TryReserve(AgentRole.Trade, 40, 10, Now);

            Assert.That(second.Granted, Is.False);
            Assert.That(second.ReasonCode, Is.EqualTo("MAX_INPUT_TOKENS"));
        }

        [Test]
        public void Reserve_OutputTokenCapUsesMaximumRequestedOutput()
        {
            var budget = new AiUsageBudget(Policy(maxOutput: 100));
            Assert.That(budget.TryReserve(AgentRole.News, 10, 70, Now).Granted, Is.True);

            var second = budget.TryReserve(AgentRole.Trade, 10, 40, Now);

            Assert.That(second.Granted, Is.False);
            Assert.That(second.ReasonCode, Is.EqualTo("MAX_OUTPUT_TOKENS"));
        }

        [Test]
        public void Reserve_EstimatedCostCapPreventsProviderCallBeforeOvercommit()
        {
            var budget = new AiUsageBudget(Policy(maxCost: 0.020m));

            var r = budget.TryReserve(AgentRole.News, 100, 50, Now);
            // 100 * .10/1000 + 50 * .40/1000 = .03.
            Assert.That(r.Granted, Is.False);
            Assert.That(r.ReasonCode, Is.EqualTo("MAX_SESSION_COST"));
        }

        [Test]
        public void Settle_UsesProviderReportedCostWhenAvailable()
        {
            var budget = new AiUsageBudget(Policy(maxCost: 0.50m));
            var reservation = budget.TryReserve(AgentRole.News, 100, 50, Now);
            var completion = ProviderCompletion.Completed(
                "fake", "m", "{}", "r", 80, 20, 0.123m,
                Now, Now.AddMilliseconds(10));

            budget.Settle(reservation, completion);
            var snapshot = budget.GetSnapshot();

            Assert.That(snapshot.SettledCalls, Is.EqualTo(1));
            Assert.That(snapshot.InputTokens, Is.EqualTo(80));
            Assert.That(snapshot.OutputTokens, Is.EqualTo(20));
            Assert.That(snapshot.CostUsd, Is.EqualTo(0.123m));
        }

        [Test]
        public void FailedProviderCallStillConsumesCallCount()
        {
            var budget = new AiUsageBudget(Policy());
            var reservation = budget.TryReserve(AgentRole.Trade, 100, 50, Now);

            budget.Settle(reservation, ProviderCompletion.ForStatus(ProviderCompletionStatus.Failed, ""));

            var snapshot = budget.GetSnapshot();
            Assert.That(snapshot.SettledCalls, Is.EqualTo(1));
            Assert.That(snapshot.CallsByRole[AgentRole.Trade], Is.EqualTo(1));
        }

        [Test]
        public void ConcurrentReservations_CannotTogetherExceedRemainingInputBudget()
        {
            var budget = new AiUsageBudget(Policy(maxCalls: 10, maxCallsPerAgent: 10, maxInput: 100));
            var results = new BudgetReservation[2];

            Parallel.Invoke(
                () => results[0] = budget.TryReserve(AgentRole.News, 60, 10, Now),
                () => results[1] = budget.TryReserve(AgentRole.Trade, 60, 10, Now));

            Assert.That(results.Count(x => x.Granted), Is.EqualTo(1));
            Assert.That(results.Count(x => !x.Granted), Is.EqualTo(1));
        }

        [Test]
        public void RejectedReservation_CannotBeSettled()
        {
            var budget = new AiUsageBudget(Policy(maxCalls: 1, maxCallsPerAgent: 1));
            var first = budget.TryReserve(AgentRole.News, 10, 10, Now);
            var rejected = budget.TryReserve(AgentRole.Trade, 10, 10, Now);
            budget.Settle(first, ProviderCompletion.CompletedForTests("{}"));

            Assert.Throws<InvalidOperationException>(() =>
                budget.Settle(rejected, ProviderCompletion.CompletedForTests("{}")));
        }

        [Test]
        public void Reserve_RejectsInvalidTokenInputsAndNonUtcTime()
        {
            var budget = new AiUsageBudget(Policy());
            Assert.Throws<ArgumentOutOfRangeException>(() => budget.TryReserve(AgentRole.News, -1, 10, Now));
            Assert.Throws<ArgumentOutOfRangeException>(() => budget.TryReserve(AgentRole.News, 10, 0, Now));
            Assert.Throws<ArgumentException>(() => budget.TryReserve(
                AgentRole.News, 10, 10, DateTime.SpecifyKind(Now, DateTimeKind.Local)));
        }
    }
}
