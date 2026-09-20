using System;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using NesTrading.Core.Agents;
using NesTrading.Core.Agents.Providers;

namespace NesTrading.Core.Tests.Agents
{
    [TestFixture]
    public class ProviderSafetyTests
    {
        [Test]
        public async Task CompletedProviderResponse_IsReturnedWithUsageMetadata()
        {
            var completion = ProviderCompletion.Completed(
                "fake", "model-x", "{\"ok\":true}", "req-1", 120, 30, 0.002m,
                DateTime.UtcNow.AddMilliseconds(-10), DateTime.UtcNow);
            var fake = FakeAgentTextProvider.Return(completion);
            var request = Request(DateTime.UtcNow.AddSeconds(2));

            var result = await AgentProviderExecutor.CompleteAsync(fake, request, CancellationToken.None);

            Assert.That(result.Status, Is.EqualTo(ProviderCompletionStatus.Completed));
            Assert.That(result.InputTokens, Is.EqualTo(120));
            Assert.That(result.OutputTokens, Is.EqualTo(30));
            Assert.That(result.ReportedCostUsd, Is.EqualTo(0.002m));
            Assert.That(fake.CallCount, Is.EqualTo(1));
        }

        [TestCase(ProviderCompletionStatus.Refused)]
        [TestCase(ProviderCompletionStatus.Incomplete)]
        [TestCase(ProviderCompletionStatus.Failed)]
        [TestCase(ProviderCompletionStatus.BudgetExhausted)]
        public async Task NonCompletedProviderStatus_IsPreserved(ProviderCompletionStatus status)
        {
            var fake = FakeAgentTextProvider.Return(ProviderCompletion.ForStatus(status, "payload"));

            var result = await AgentProviderExecutor.CompleteAsync(fake, Request(DateTime.UtcNow.AddSeconds(2)), CancellationToken.None);

            Assert.That(result.Status, Is.EqualTo(status));
        }

        [Test]
        public async Task ProviderThatFinishesAfterDeadline_ReturnsTimedOutAndIgnoresLateText()
        {
            var fake = FakeAgentTextProvider.Delayed(
                ProviderCompletion.CompletedForTests("{\"late\":true}"),
                TimeSpan.FromMilliseconds(250));

            var result = await AgentProviderExecutor.CompleteAsync(
                fake,
                Request(DateTime.UtcNow.AddMilliseconds(40)),
                CancellationToken.None);

            Assert.That(result.Status, Is.EqualTo(ProviderCompletionStatus.TimedOut));
            Assert.That(result.OutputText, Is.Empty);
        }

        [Test]
        public async Task DeadlineAlreadyExpired_DoesNotCallProvider()
        {
            var fake = FakeAgentTextProvider.Return(ProviderCompletion.CompletedForTests("{}"));

            var result = await AgentProviderExecutor.CompleteAsync(
                fake,
                Request(DateTime.UtcNow.AddSeconds(-1)),
                CancellationToken.None);

            Assert.That(result.Status, Is.EqualTo(ProviderCompletionStatus.TimedOut));
            Assert.That(fake.CallCount, Is.EqualTo(0));
        }

        [Test]
        public async Task ProviderException_IsMappedToFailedWithoutLeakingExceptionText()
        {
            var fake = FakeAgentTextProvider.Throw(new InvalidOperationException("secret-ish provider detail"));

            var result = await AgentProviderExecutor.CompleteAsync(
                fake,
                Request(DateTime.UtcNow.AddSeconds(2)),
                CancellationToken.None);

            Assert.That(result.Status, Is.EqualTo(ProviderCompletionStatus.Failed));
            Assert.That(result.OutputText, Is.Empty);
            Assert.That(result.RequestId, Is.Null);
        }

        [Test]
        public async Task ExternalCancellation_IsMappedToCancelled()
        {
            var fake = FakeAgentTextProvider.Delayed(
                ProviderCompletion.CompletedForTests("{}"),
                TimeSpan.FromSeconds(1));
            using (var cts = new CancellationTokenSource())
            {
                cts.Cancel();

                var result = await AgentProviderExecutor.CompleteAsync(
                    fake,
                    Request(DateTime.UtcNow.AddSeconds(2)),
                    cts.Token);

                Assert.That(result.Status, Is.EqualTo(ProviderCompletionStatus.Cancelled));
            }
        }

        [Test]
        public void ProviderRequest_RejectsNonUtcDeadlineAndInvalidTokenCap()
        {
            var local = DateTime.SpecifyKind(DateTime.Now, DateTimeKind.Local);

            Assert.Throws<ArgumentException>(() => new ProviderRequest(
                "news-agent", "model", "prompt.v1", "news.v1", "{}", "input", local, 200));

            Assert.Throws<ArgumentOutOfRangeException>(() => new ProviderRequest(
                "news-agent", "model", "prompt.v1", "news.v1", "{}", "input", DateTime.UtcNow.AddSeconds(1), 0));
        }

        [Test]
        public void ProviderRequest_CarriesStrictSchemaAndPromptMetadata()
        {
            var deadline = DateTime.UtcNow.AddSeconds(2);
            var request = new ProviderRequest(
                "trade-agent", "model-x", "trade.prompt.v1", "trade.v1",
                "{\"type\":\"object\"}", "context", deadline, 180);

            Assert.That(request.AgentName, Is.EqualTo("trade-agent"));
            Assert.That(request.Model, Is.EqualTo("model-x"));
            Assert.That(request.PromptVersion, Is.EqualTo("trade.prompt.v1"));
            Assert.That(request.SchemaName, Is.EqualTo("trade.v1"));
            Assert.That(request.JsonSchema, Does.Contain("object"));
            Assert.That(request.MaxOutputTokens, Is.EqualTo(180));
        }

        private static ProviderRequest Request(DateTime deadline) =>
            new ProviderRequest(
                "news-agent",
                "test-model",
                "news.prompt.v1",
                "news.v1",
                "{\"type\":\"object\"}",
                "test input",
                deadline,
                200);
    }
}
