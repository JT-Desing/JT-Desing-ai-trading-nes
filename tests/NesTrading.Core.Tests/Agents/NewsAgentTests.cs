using System;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using NesTrading.Core.Agents;
using NesTrading.Core.Agents.Budget;
using NesTrading.Core.Agents.News;
using NesTrading.Core.Agents.Providers;
using NesTrading.Core.Domain;

namespace NesTrading.Core.Tests.Agents
{
    [TestFixture]
    public class NewsAgentTests
    {
        [Test]
        public async Task ValidAllowResponse_IsValidatedPublishedAndUsageSettled()
        {
            var now = DateTime.UtcNow;
            var json = NewsJson(now, now.AddMinutes(1), "ALLOW", "NES");
            var fake = FakeAgentTextProvider.Return(Completed(json, now));
            var cache = new AgentStateCache();
            var budget = Budget();
            var agent = Agent(fake, cache, budget);

            var result = await agent.RefreshAsync("No scheduled high-impact event nearby.", now, CancellationToken.None);

            Assert.That(result.Success, Is.True);
            Assert.That(result.Published, Is.True);
            Assert.That(result.State.Payload.State, Is.EqualTo(NewsGate.Allow));
            Assert.That(cache.Read(now).News, Is.Not.Null);
            Assert.That(cache.Read(now).News.ProviderName, Is.EqualTo("fake"));
            Assert.That(budget.GetSnapshot().SettledCalls, Is.EqualTo(1));
            Assert.That(fake.CallCount, Is.EqualTo(1));
        }

        [TestCase(ProviderCompletionStatus.Refused, "PROVIDER_REFUSED")]
        [TestCase(ProviderCompletionStatus.Incomplete, "PROVIDER_INCOMPLETE")]
        [TestCase(ProviderCompletionStatus.Failed, "PROVIDER_FAILED")]
        public async Task NonCompletedProviderResponse_DoesNotPublish(ProviderCompletionStatus status, string expectedReason)
        {
            var now = DateTime.UtcNow;
            var fake = FakeAgentTextProvider.Return(ProviderCompletion.ForStatus(status, "ignored"));
            var cache = new AgentStateCache();
            var agent = Agent(fake, cache, Budget());

            var result = await agent.RefreshAsync("context", now, CancellationToken.None);

            Assert.That(result.Success, Is.False);
            Assert.That(result.Published, Is.False);
            Assert.That(result.ReasonCode, Is.EqualTo(expectedReason));
            Assert.That(cache.Read(now).News, Is.Null);
        }

        [Test]
        public async Task ProviderTimeout_DoesNotPublishLateText()
        {
            var now = DateTime.UtcNow;
            var json = NewsJson(now, now.AddMinutes(1), "ALLOW", "NES");
            var fake = FakeAgentTextProvider.Delayed(Completed(json, now), TimeSpan.FromMilliseconds(250));
            var cache = new AgentStateCache();
            var agent = new NewsAgent(fake, cache, Budget(), "test-model", TimeSpan.FromMilliseconds(30), 200);

            var result = await agent.RefreshAsync("context", now, CancellationToken.None);

            Assert.That(result.Success, Is.False);
            Assert.That(result.ReasonCode, Is.EqualTo("PROVIDER_TIMED_OUT"));
            Assert.That(cache.Read(DateTime.UtcNow).News, Is.Null);
        }

        [Test]
        public async Task InvalidJson_DoesNotOverwritePreviouslyPublishedState()
        {
            var now = DateTime.UtcNow;
            var cache = new AgentStateCache();
            var firstJson = NewsJson(now, now.AddMinutes(2), "ALLOW", "NES");
            var firstAgent = Agent(FakeAgentTextProvider.Return(Completed(firstJson, now)), cache, Budget());
            Assert.That((await firstAgent.RefreshAsync("context", now, CancellationToken.None)).Success, Is.True);

            var bad = Agent(FakeAgentTextProvider.Return(Completed("not-json", now.AddSeconds(1))), cache, Budget());
            var result = await bad.RefreshAsync("context", now.AddSeconds(1), CancellationToken.None);

            Assert.That(result.Success, Is.False);
            Assert.That(result.ReasonCode, Is.EqualTo("SCHEMA_JSON_INVALID"));
            Assert.That(cache.Read(now.AddSeconds(1)).News.Payload.State, Is.EqualTo(NewsGate.Allow));
        }

        [Test]
        public async Task UnknownEnum_IsRejected()
        {
            var now = DateTime.UtcNow;
            var json = NewsJson(now, now.AddMinutes(1), "BUY", "NES");
            var result = await Agent(FakeAgentTextProvider.Return(Completed(json, now)), new AgentStateCache(), Budget())
                .RefreshAsync("context", now, CancellationToken.None);

            Assert.That(result.Success, Is.False);
            Assert.That(result.ReasonCode, Is.EqualTo("SCHEMA_ENUM_INVALID"));
        }

        [Test]
        public async Task WrongSymbol_IsRejected()
        {
            var now = DateTime.UtcNow;
            var json = NewsJson(now, now.AddMinutes(1), "ALLOW", "ES");
            var result = await Agent(FakeAgentTextProvider.Return(Completed(json, now)), new AgentStateCache(), Budget())
                .RefreshAsync("context", now, CancellationToken.None);

            Assert.That(result.Success, Is.False);
            Assert.That(result.ReasonCode, Is.EqualTo("SYMBOL_NOT_SUPPORTED"));
        }

        [Test]
        public async Task StateAlreadyExpiredWhenReceived_IsRejected()
        {
            var now = DateTime.UtcNow;
            var json = NewsJson(now.AddMinutes(-2), now.AddSeconds(-1), "ALLOW", "NES");
            var result = await Agent(FakeAgentTextProvider.Return(Completed(json, now)), new AgentStateCache(), Budget())
                .RefreshAsync("context", now, CancellationToken.None);

            Assert.That(result.Success, Is.False);
            Assert.That(result.ReasonCode, Is.EqualTo("STATE_EXPIRED_BEFORE_PUBLISH"));
        }

        [Test]
        public async Task GeneratedTimestampTooFarInFuture_IsRejected()
        {
            var now = DateTime.UtcNow;
            var generated = now.AddMinutes(1);
            var json = NewsJson(generated, generated.AddMinutes(1), "ALLOW", "NES");
            var result = await Agent(FakeAgentTextProvider.Return(Completed(json, now)), new AgentStateCache(), Budget())
                .RefreshAsync("context", now, CancellationToken.None);

            Assert.That(result.Success, Is.False);
            Assert.That(result.ReasonCode, Is.EqualTo("TIMESTAMP_IN_FUTURE"));
        }

        [Test]
        public async Task ExhaustedBudget_DoesNotCallProvider()
        {
            var now = DateTime.UtcNow;
            var policy = new AiUsagePolicy(1, 1, 10000, 1000, 1m, 0m, 0m);
            var budget = new AiUsageBudget(policy);
            var first = budget.TryReserve(AgentRole.News, 1, 1, now);
            budget.Settle(first, ProviderCompletion.CompletedForTests("{}"));

            var fake = FakeAgentTextProvider.Return(Completed(NewsJson(now, now.AddMinutes(1), "ALLOW", "NES"), now));
            var result = await Agent(fake, new AgentStateCache(), budget)
                .RefreshAsync("context", now, CancellationToken.None);

            Assert.That(result.Success, Is.False);
            Assert.That(result.ReasonCode, Is.EqualTo("BUDGET_EXHAUSTED"));
            Assert.That(fake.CallCount, Is.EqualTo(0));
        }

        [Test]
        public void PromptExplicitlyForbidsTradingInstructionsAndSizing()
        {
            var prompt = NewsAgentPrompt.Build("calendar and headlines");

            Assert.That(prompt, Does.Contain("ALLOW"));
            Assert.That(prompt, Does.Contain("CAUTION"));
            Assert.That(prompt, Does.Contain("BLOCK"));
            Assert.That(prompt, Does.Contain("Do not output BUY or SELL"));
            Assert.That(prompt, Does.Contain("Do not choose position size"));
        }

        private static NewsAgent Agent(
            IAgentTextProvider provider,
            AgentStateCache cache,
            AiUsageBudget budget) =>
            new NewsAgent(provider, cache, budget, "test-model", TimeSpan.FromSeconds(2), 200);

        private static AiUsageBudget Budget() =>
            new AiUsageBudget(new AiUsagePolicy(20, 10, 50000, 5000, 10m, 0.01m, 0.02m));

        private static ProviderCompletion Completed(string json, DateTime now) =>
            ProviderCompletion.Completed("fake", "test-model", json, "req-1", 100, 25, 0.001m,
                now.AddMilliseconds(-10), now);

        private static string NewsJson(DateTime generated, DateTime validUntil, string state, string symbol)
        {
            return "{" +
                "\"schema_version\":\"news.v1\"," +
                "\"symbol\":\"" + symbol + "\"," +
                "\"state\":\"" + state + "\"," +
                "\"directional_bias\":\"NEUTRAL\"," +
                "\"importance\":\"LOW\"," +
                "\"event_code\":\"NONE\"," +
                "\"generated_at_utc\":\"" + generated.ToString("O") + "\"," +
                "\"valid_until_utc\":\"" + validUntil.ToString("O") + "\"," +
                "\"confidence\":0.8," +
                "\"reason_codes\":[\"TEST\"]" +
                "}";
        }
    }
}
