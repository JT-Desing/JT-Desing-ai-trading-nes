using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using NesTrading.Core.Agents;
using NesTrading.Core.Agents.Providers;

namespace NesTrading.Core.Tests.Agents
{
    [TestFixture]
    public class OpenAiResponsesProviderTests
    {
        [Test]
        public async Task CompleteAsync_SendsStructuredOutputRequestWithoutLeakingKeyIntoBody()
        {
            var handler = new RecordingHandler(HttpStatusCode.OK, CompletedBody("{\"state\":\"ALLOW\"}"));
            var http = new HttpClient(handler);
            var options = new OpenAiProviderOptions("sk-test-secret", new Uri("https://api.openai.com/v1/responses"));
            var provider = new OpenAiResponsesProvider(http, options);

            var result = await provider.CompleteAsync(Request(), CancellationToken.None);

            Assert.That(result.Status, Is.EqualTo(ProviderCompletionStatus.Completed));
            Assert.That(handler.LastRequest.Method, Is.EqualTo(HttpMethod.Post));
            Assert.That(handler.LastRequest.RequestUri.Scheme, Is.EqualTo("https"));
            Assert.That(handler.AuthorizationScheme, Is.EqualTo("Bearer"));
            Assert.That(handler.AuthorizationParameter, Is.EqualTo("sk-test-secret"));

            var body = JObject.Parse(handler.LastBody);
            Assert.That((string)body["model"], Is.EqualTo("gpt-5.6-luna"));
            Assert.That((int)body["max_output_tokens"], Is.EqualTo(200));
            Assert.That((string)body["text"]["format"]["type"], Is.EqualTo("json_schema"));
            Assert.That((bool)body["text"]["format"]["strict"], Is.True);
            Assert.That((string)body["text"]["format"]["name"], Is.EqualTo("news.v1"));
            Assert.That(body["text"]["format"]["schema"], Is.Not.Null);
            Assert.That(handler.LastBody, Does.Not.Contain("sk-test-secret"));
            Assert.That(result.OutputText, Is.EqualTo("{\"state\":\"ALLOW\"}"));
        }

        [Test]
        public async Task CompleteAsync_CompletedResponseExtractsUsageAndRequestId()
        {
            var handler = new RecordingHandler(HttpStatusCode.OK,
                "{\"id\":\"resp_123\",\"status\":\"completed\",\"model\":\"gpt-5.6-luna\",\"output\":[{\"type\":\"message\",\"content\":[{\"type\":\"output_text\",\"text\":\"{\\\"ok\\\":true}\"}]}],\"usage\":{\"input_tokens\":81,\"output_tokens\":11}}");
            var provider = Provider(handler);

            var result = await provider.CompleteAsync(Request(), CancellationToken.None);

            Assert.That(result.Status, Is.EqualTo(ProviderCompletionStatus.Completed));
            Assert.That(result.RequestId, Is.EqualTo("resp_123"));
            Assert.That(result.InputTokens, Is.EqualTo(81));
            Assert.That(result.OutputTokens, Is.EqualTo(11));
            Assert.That(result.OutputText, Is.EqualTo("{\"ok\":true}"));
        }

        [Test]
        public async Task CompleteAsync_RefusalMapsToRefusedAndDoesNotExposeRefusalAsTradableText()
        {
            var handler = new RecordingHandler(HttpStatusCode.OK,
                "{\"id\":\"resp_r\",\"status\":\"completed\",\"model\":\"gpt-5.6-luna\",\"output\":[{\"type\":\"message\",\"content\":[{\"type\":\"refusal\",\"refusal\":\"cannot comply\"}]}],\"usage\":{\"input_tokens\":10,\"output_tokens\":4}}");
            var provider = Provider(handler);

            var result = await provider.CompleteAsync(Request(), CancellationToken.None);

            Assert.That(result.Status, Is.EqualTo(ProviderCompletionStatus.Refused));
            Assert.That(result.OutputText, Is.Empty);
            Assert.That(result.InputTokens, Is.EqualTo(10));
            Assert.That(result.OutputTokens, Is.EqualTo(4));
        }

        [Test]
        public async Task CompleteAsync_IncompleteResponseMapsToIncomplete()
        {
            var handler = new RecordingHandler(HttpStatusCode.OK,
                "{\"id\":\"resp_i\",\"status\":\"incomplete\",\"incomplete_details\":{\"reason\":\"max_output_tokens\"},\"model\":\"gpt-5.6-luna\",\"output\":[],\"usage\":{\"input_tokens\":10,\"output_tokens\":200}}");
            var provider = Provider(handler);

            var result = await provider.CompleteAsync(Request(), CancellationToken.None);

            Assert.That(result.Status, Is.EqualTo(ProviderCompletionStatus.Incomplete));
            Assert.That(result.OutputText, Is.Empty);
            Assert.That(result.InputTokens, Is.EqualTo(10));
            Assert.That(result.OutputTokens, Is.EqualTo(200));
        }

        [TestCase((HttpStatusCode)429)]
        [TestCase(HttpStatusCode.InternalServerError)]
        [TestCase(HttpStatusCode.BadGateway)]
        public async Task CompleteAsync_HttpFailureMapsToFailedWithoutThrowing(HttpStatusCode status)
        {
            var handler = new RecordingHandler(status, "{\"error\":{\"message\":\"provider detail\"}}");
            var provider = Provider(handler);

            var result = await provider.CompleteAsync(Request(), CancellationToken.None);

            Assert.That(result.Status, Is.EqualTo(ProviderCompletionStatus.Failed));
            Assert.That(result.OutputText, Is.Empty);
        }

        [Test]
        public async Task CompleteAsync_MalformedSuccessfulBodyMapsToFailed()
        {
            var provider = Provider(new RecordingHandler(HttpStatusCode.OK, "not-json"));

            var result = await provider.CompleteAsync(Request(), CancellationToken.None);

            Assert.That(result.Status, Is.EqualTo(ProviderCompletionStatus.Failed));
        }

        [Test]
        public async Task CompleteAsync_CompletedWithoutOutputTextMapsToIncomplete()
        {
            var provider = Provider(new RecordingHandler(HttpStatusCode.OK,
                "{\"id\":\"resp_x\",\"status\":\"completed\",\"model\":\"gpt-5.6-luna\",\"output\":[],\"usage\":{\"input_tokens\":1,\"output_tokens\":0}}"));

            var result = await provider.CompleteAsync(Request(), CancellationToken.None);

            Assert.That(result.Status, Is.EqualTo(ProviderCompletionStatus.Incomplete));
            Assert.That(result.OutputText, Is.Empty);
        }

        [Test]
        public void OptionsRejectNonHttpsEndpointAndMissingApiKey()
        {
            Assert.Throws<ArgumentException>(() =>
                new OpenAiProviderOptions("", new Uri("https://api.openai.com/v1/responses")));
            Assert.Throws<ArgumentException>(() =>
                new OpenAiProviderOptions("key", new Uri("http://api.openai.com/v1/responses")));
        }

        private static OpenAiResponsesProvider Provider(RecordingHandler handler) =>
            new OpenAiResponsesProvider(
                new HttpClient(handler),
                new OpenAiProviderOptions("sk-test-secret", new Uri("https://api.openai.com/v1/responses")));

        private static ProviderRequest Request() =>
            new ProviderRequest(
                "news-agent",
                "gpt-5.6-luna",
                "news.prompt.v1",
                "news.v1",
                "{\"type\":\"object\",\"additionalProperties\":false,\"properties\":{},\"required\":[]}",
                "context",
                DateTime.UtcNow.AddSeconds(5),
                200);

        private static string CompletedBody(string output) =>
            "{\"id\":\"resp_1\",\"status\":\"completed\",\"model\":\"gpt-5.6-luna\",\"output\":[{\"type\":\"message\",\"content\":[{\"type\":\"output_text\",\"text\":" + JsonConvert.ToString(output) + "}]}],\"usage\":{\"input_tokens\":10,\"output_tokens\":5}}";

        private sealed class RecordingHandler : HttpMessageHandler
        {
            private readonly HttpStatusCode _status;
            private readonly string _body;

            public RecordingHandler(HttpStatusCode status, string body)
            {
                _status = status;
                _body = body;
            }

            public HttpRequestMessage LastRequest { get; private set; }
            public string LastBody { get; private set; }
            public string AuthorizationScheme { get; private set; }
            public string AuthorizationParameter { get; private set; }

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                LastRequest = request;
                AuthorizationScheme = request.Headers.Authorization == null ? null : request.Headers.Authorization.Scheme;
                AuthorizationParameter = request.Headers.Authorization == null ? null : request.Headers.Authorization.Parameter;
                LastBody = request.Content == null ? "" : await request.Content.ReadAsStringAsync();

                return new HttpResponseMessage(_status)
                {
                    Content = new StringContent(_body, Encoding.UTF8, "application/json")
                };
            }
        }
    }
}
