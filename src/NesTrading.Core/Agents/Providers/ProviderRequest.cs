using System;

namespace NesTrading.Core.Agents.Providers
{
    public sealed class ProviderRequest
    {
        public ProviderRequest(
            string agentName,
            string model,
            string promptVersion,
            string schemaName,
            string jsonSchema,
            string input,
            DateTime deadlineUtc,
            int maxOutputTokens)
        {
            if (string.IsNullOrWhiteSpace(agentName)) throw new ArgumentException("agentName is required.", nameof(agentName));
            if (string.IsNullOrWhiteSpace(model)) throw new ArgumentException("model is required.", nameof(model));
            if (string.IsNullOrWhiteSpace(promptVersion)) throw new ArgumentException("promptVersion is required.", nameof(promptVersion));
            if (string.IsNullOrWhiteSpace(schemaName)) throw new ArgumentException("schemaName is required.", nameof(schemaName));
            if (string.IsNullOrWhiteSpace(jsonSchema)) throw new ArgumentException("jsonSchema is required.", nameof(jsonSchema));
            if (input == null) throw new ArgumentNullException(nameof(input));
            if (deadlineUtc.Kind != DateTimeKind.Utc) throw new ArgumentException("deadlineUtc must be UTC.", nameof(deadlineUtc));
            if (maxOutputTokens <= 0) throw new ArgumentOutOfRangeException(nameof(maxOutputTokens));

            AgentName = agentName;
            Model = model;
            PromptVersion = promptVersion;
            SchemaName = schemaName;
            JsonSchema = jsonSchema;
            Input = input;
            DeadlineUtc = deadlineUtc;
            MaxOutputTokens = maxOutputTokens;
        }

        public string AgentName { get; }
        public string Model { get; }
        public string PromptVersion { get; }
        public string SchemaName { get; }
        public string JsonSchema { get; }
        public string Input { get; }
        public DateTime DeadlineUtc { get; }
        public int MaxOutputTokens { get; }
    }
}
