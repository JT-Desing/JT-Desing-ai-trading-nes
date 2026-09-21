using System;

namespace NesTrading.Core.Agents.Providers
{
    public sealed class OpenAiProviderOptions
    {
        public OpenAiProviderOptions(string apiKey, Uri responsesEndpoint)
        {
            if (string.IsNullOrWhiteSpace(apiKey))
                throw new ArgumentException("apiKey is required.", nameof(apiKey));
            if (responsesEndpoint == null || !responsesEndpoint.IsAbsoluteUri)
                throw new ArgumentException("responsesEndpoint must be absolute.", nameof(responsesEndpoint));
            if (!string.Equals(responsesEndpoint.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("OpenAI endpoint must use HTTPS.", nameof(responsesEndpoint));

            ApiKey = apiKey;
            ResponsesEndpoint = responsesEndpoint;
        }

        internal string ApiKey { get; }
        public Uri ResponsesEndpoint { get; }

        public static OpenAiProviderOptions FromEnvironment(string variableName = "OPENAI_API_KEY")
        {
            if (string.IsNullOrWhiteSpace(variableName))
                throw new ArgumentException("variableName is required.", nameof(variableName));

            var key = Environment.GetEnvironmentVariable(variableName);
            if (string.IsNullOrWhiteSpace(key))
                throw new InvalidOperationException("OpenAI API key environment variable is not configured.");

            return new OpenAiProviderOptions(
                key,
                new Uri("https://api.openai.com/v1/responses"));
        }
    }
}
