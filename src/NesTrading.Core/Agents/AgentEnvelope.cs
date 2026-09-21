using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

namespace NesTrading.Core.Agents
{
    public sealed class AgentEnvelope<T>
    {
        public AgentEnvelope(
            string agentName,
            string schemaVersion,
            string promptVersion,
            string providerName,
            string modelName,
            DateTime generatedAtUtc,
            DateTime validUntilUtc,
            DateTime receivedAtUtc,
            T payload,
            string rawResponseHash)
        {
            if (string.IsNullOrWhiteSpace(agentName)) throw new ArgumentException("agentName is required.", nameof(agentName));
            if (string.IsNullOrWhiteSpace(schemaVersion)) throw new ArgumentException("schemaVersion is required.", nameof(schemaVersion));
            if (string.IsNullOrWhiteSpace(promptVersion)) throw new ArgumentException("promptVersion is required.", nameof(promptVersion));
            if (string.IsNullOrWhiteSpace(providerName)) throw new ArgumentException("providerName is required.", nameof(providerName));
            if (string.IsNullOrWhiteSpace(modelName)) throw new ArgumentException("modelName is required.", nameof(modelName));
            RequireUtc(generatedAtUtc, nameof(generatedAtUtc));
            RequireUtc(validUntilUtc, nameof(validUntilUtc));
            RequireUtc(receivedAtUtc, nameof(receivedAtUtc));
            if (validUntilUtc < generatedAtUtc) throw new ArgumentException("validUntilUtc cannot precede generatedAtUtc.");
            if (payload == null) throw new ArgumentNullException(nameof(payload));
            if (string.IsNullOrWhiteSpace(rawResponseHash)) throw new ArgumentException("rawResponseHash is required.", nameof(rawResponseHash));

            AgentName = agentName;
            SchemaVersion = schemaVersion;
            PromptVersion = promptVersion;
            ProviderName = providerName;
            ModelName = modelName;
            GeneratedAtUtc = generatedAtUtc;
            ValidUntilUtc = validUntilUtc;
            ReceivedAtUtc = receivedAtUtc;
            Payload = payload;
            RawResponseHash = rawResponseHash;
        }

        public string AgentName { get; }
        public string SchemaVersion { get; }
        public string PromptVersion { get; }
        public string ProviderName { get; }
        public string ModelName { get; }
        public DateTime GeneratedAtUtc { get; }
        public DateTime ValidUntilUtc { get; }
        public DateTime ReceivedAtUtc { get; }
        public T Payload { get; }
        public string RawResponseHash { get; }

        public bool IsFreshAt(DateTime utcNow)
        {
            RequireUtc(utcNow, nameof(utcNow));
            return utcNow >= GeneratedAtUtc && utcNow <= ValidUntilUtc;
        }

        public static string HashRaw(string raw)
        {
            using (var sha = SHA256.Create())
            {
                var bytes = Encoding.UTF8.GetBytes(raw ?? string.Empty);
                var hash = sha.ComputeHash(bytes);
                var sb = new StringBuilder(hash.Length * 2);
                foreach (var b in hash) sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }

        private static void RequireUtc(DateTime value, string name)
        {
            if (value.Kind != DateTimeKind.Utc) throw new ArgumentException(name + " must be UTC.", name);
        }
    }

    public sealed class AgentValidationResult<T>
    {
        private AgentValidationResult(bool isValid, string reasonCode, AgentEnvelope<T> value)
        {
            IsValid = isValid;
            ReasonCode = reasonCode;
            Value = value;
        }

        public bool IsValid { get; }
        public string ReasonCode { get; }
        public AgentEnvelope<T> Value { get; }

        public static AgentValidationResult<T> Valid(AgentEnvelope<T> value) =>
            new AgentValidationResult<T>(true, "VALID", value);

        public static AgentValidationResult<T> Invalid(string reasonCode) =>
            new AgentValidationResult<T>(false, reasonCode, null);
    }

    public sealed class ProviderCompletion
    {
        private ProviderCompletion(
            ProviderCompletionStatus status,
            string providerName,
            string modelName,
            string outputText,
            string requestId,
            int inputTokens,
            int outputTokens,
            decimal? reportedCostUsd,
            DateTime startedAtUtc,
            DateTime finishedAtUtc)
        {
            if (inputTokens < 0) throw new ArgumentOutOfRangeException(nameof(inputTokens));
            if (outputTokens < 0) throw new ArgumentOutOfRangeException(nameof(outputTokens));
            if (reportedCostUsd.HasValue && reportedCostUsd.Value < 0m) throw new ArgumentOutOfRangeException(nameof(reportedCostUsd));
            if (startedAtUtc.Kind != DateTimeKind.Utc || finishedAtUtc.Kind != DateTimeKind.Utc)
                throw new ArgumentException("Provider timestamps must be UTC.");
            if (finishedAtUtc < startedAtUtc) throw new ArgumentException("Provider finish cannot precede start.");

            Status = status;
            ProviderName = string.IsNullOrWhiteSpace(providerName) ? "unknown" : providerName;
            ModelName = string.IsNullOrWhiteSpace(modelName) ? "unknown" : modelName;
            OutputText = outputText ?? string.Empty;
            RequestId = requestId;
            InputTokens = inputTokens;
            OutputTokens = outputTokens;
            ReportedCostUsd = reportedCostUsd;
            StartedAtUtc = startedAtUtc;
            FinishedAtUtc = finishedAtUtc;
        }

        public ProviderCompletionStatus Status { get; }
        public string ProviderName { get; }
        public string ModelName { get; }
        public string OutputText { get; }
        public string RequestId { get; }
        public int InputTokens { get; }
        public int OutputTokens { get; }
        public decimal? ReportedCostUsd { get; }
        public DateTime StartedAtUtc { get; }
        public DateTime FinishedAtUtc { get; }

        public static ProviderCompletion Completed(
            string providerName,
            string modelName,
            string outputText,
            string requestId,
            int inputTokens,
            int outputTokens,
            decimal? reportedCostUsd,
            DateTime startedAtUtc,
            DateTime finishedAtUtc) =>
            new ProviderCompletion(ProviderCompletionStatus.Completed, providerName, modelName, outputText, requestId,
                inputTokens, outputTokens, reportedCostUsd, startedAtUtc, finishedAtUtc);

        public static ProviderCompletion CompletedForTests(string outputText)
        {
            var t = new DateTime(2026, 9, 21, 14, 0, 1, DateTimeKind.Utc);
            return Completed("test-provider", "test-model", outputText, "test-request", 1, 1, 0m, t.AddMilliseconds(-100), t);
        }

        public static ProviderCompletion ForStatus(ProviderCompletionStatus status, string outputText)
        {
            var t = new DateTime(2026, 9, 21, 14, 0, 1, DateTimeKind.Utc);
            return new ProviderCompletion(status, "test-provider", "test-model", outputText, "test-request",
                0, 0, 0m, t.AddMilliseconds(-100), t);
        }

        public static ProviderCompletion FromStatus(
            ProviderCompletionStatus status,
            string providerName,
            string modelName,
            DateTime startedAtUtc,
            DateTime finishedAtUtc)
        {
            return new ProviderCompletion(
                status,
                providerName,
                modelName,
                string.Empty,
                null,
                0,
                0,
                null,
                startedAtUtc,
                finishedAtUtc);
        }
    }
}
