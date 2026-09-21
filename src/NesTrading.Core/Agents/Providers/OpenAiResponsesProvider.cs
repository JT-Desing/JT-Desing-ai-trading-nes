using System;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace NesTrading.Core.Agents.Providers
{
    public sealed class OpenAiResponsesProvider : IAgentTextProvider
    {
        private readonly HttpClient _httpClient;
        private readonly OpenAiProviderOptions _options;

        public OpenAiResponsesProvider(HttpClient httpClient, OpenAiProviderOptions options)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _options = options ?? throw new ArgumentNullException(nameof(options));
        }

        public async Task<ProviderCompletion> CompleteAsync(
            ProviderRequest request,
            CancellationToken cancellationToken)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));

            var started = DateTime.UtcNow;
            var remaining = request.DeadlineUtc - started;
            if (remaining <= TimeSpan.Zero)
                return ProviderCompletion.FromStatus(
                    ProviderCompletionStatus.TimedOut,
                    "openai",
                    request.Model,
                    started,
                    DateTime.UtcNow);

            JToken schema;
            try
            {
                schema = JToken.Parse(request.JsonSchema);
            }
            catch (JsonException)
            {
                return ProviderCompletion.FromStatus(
                    ProviderCompletionStatus.Failed,
                    "openai",
                    request.Model,
                    started,
                    DateTime.UtcNow);
            }

            var body = new JObject
            {
                ["model"] = request.Model,
                ["input"] = request.Input,
                ["max_output_tokens"] = request.MaxOutputTokens,
                ["text"] = new JObject
                {
                    ["format"] = new JObject
                    {
                        ["type"] = "json_schema",
                        ["name"] = request.SchemaName,
                        ["strict"] = true,
                        ["schema"] = schema
                    }
                }
            };

            using (var message = new HttpRequestMessage(HttpMethod.Post, _options.ResponsesEndpoint))
            {
                message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);
                message.Content = new StringContent(
                    body.ToString(Formatting.None),
                    Encoding.UTF8,
                    "application/json");

                using (var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                {
                    linked.CancelAfter(remaining);

                    HttpResponseMessage response;
                    try
                    {
                        response = await _httpClient.SendAsync(message, linked.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        if (cancellationToken.IsCancellationRequested)
                            throw;

                        return ProviderCompletion.FromStatus(
                            ProviderCompletionStatus.TimedOut,
                            "openai",
                            request.Model,
                            started,
                            DateTime.UtcNow);
                    }
                    catch
                    {
                        return ProviderCompletion.FromStatus(
                            ProviderCompletionStatus.Failed,
                            "openai",
                            request.Model,
                            started,
                            DateTime.UtcNow);
                    }

                    using (response)
                    {
                        if (!response.IsSuccessStatusCode)
                        {
                            return ProviderCompletion.FromStatus(
                                ProviderCompletionStatus.Failed,
                                "openai",
                                request.Model,
                                started,
                                DateTime.UtcNow);
                        }

                        string responseText;
                        try
                        {
                            responseText = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                        }
                        catch
                        {
                            return ProviderCompletion.FromStatus(
                                ProviderCompletionStatus.Failed,
                                "openai",
                                request.Model,
                                started,
                                DateTime.UtcNow);
                        }

                        return ParseSuccessfulResponse(
                            responseText,
                            request.Model,
                            started,
                            DateTime.UtcNow);
                    }
                }
            }
        }

        private static ProviderCompletion ParseSuccessfulResponse(
            string responseText,
            string requestedModel,
            DateTime started,
            DateTime finished)
        {
            JObject json;
            try
            {
                json = JObject.Parse(responseText);
            }
            catch (JsonException)
            {
                return ProviderCompletion.FromStatus(
                    ProviderCompletionStatus.Failed,
                    "openai",
                    requestedModel,
                    started,
                    finished);
            }

            var status = (string)json["status"];
            var model = (string)json["model"] ?? requestedModel;
            var requestId = (string)json["id"];
            var inputTokens = SafeInt(json["usage"]?["input_tokens"]);
            var outputTokens = SafeInt(json["usage"]?["output_tokens"]);

            if (string.Equals(status, "incomplete", StringComparison.OrdinalIgnoreCase))
                return ProviderCompletion.FromStatusWithUsage(
                    ProviderCompletionStatus.Incomplete,
                    "openai",
                    model,
                    requestId,
                    inputTokens,
                    outputTokens,
                    null,
                    started,
                    finished);

            var contentItems = json["output"]?
                .Children<JObject>()
                .Where(x => string.Equals((string)x["type"], "message", StringComparison.Ordinal))
                .SelectMany(x => x["content"] == null
                    ? Enumerable.Empty<JToken>()
                    : x["content"].Children())
                .OfType<JObject>()
                .ToArray() ?? new JObject[0];

            if (contentItems.Any(x => string.Equals((string)x["type"], "refusal", StringComparison.Ordinal)))
                return ProviderCompletion.FromStatusWithUsage(
                    ProviderCompletionStatus.Refused,
                    "openai",
                    model,
                    requestId,
                    inputTokens,
                    outputTokens,
                    null,
                    started,
                    finished);

            if (!string.Equals(status, "completed", StringComparison.OrdinalIgnoreCase))
                return ProviderCompletion.FromStatus(
                    ProviderCompletionStatus.Failed,
                    "openai",
                    model,
                    started,
                    finished);

            var outputText = contentItems
                .Where(x => string.Equals((string)x["type"], "output_text", StringComparison.Ordinal))
                .Select(x => (string)x["text"])
                .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));

            if (string.IsNullOrWhiteSpace(outputText))
                return ProviderCompletion.FromStatusWithUsage(
                    ProviderCompletionStatus.Incomplete,
                    "openai",
                    model,
                    requestId,
                    inputTokens,
                    outputTokens,
                    null,
                    started,
                    finished);

            return ProviderCompletion.Completed(
                "openai",
                model,
                outputText,
                requestId,
                inputTokens,
                outputTokens,
                null,
                started,
                finished);
        }

        private static int SafeInt(JToken token)
        {
            if (token == null) return 0;
            int value;
            return int.TryParse(token.ToString(), out value) && value >= 0 ? value : 0;
        }
    }
}
