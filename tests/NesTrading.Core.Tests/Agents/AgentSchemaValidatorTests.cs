using System;
using NUnit.Framework;
using NesTrading.Core.Agents;
using NesTrading.Core.Domain;

namespace NesTrading.Core.Tests.Agents
{
    [TestFixture]
    public class AgentSchemaValidatorTests
    {
        private const string ValidNewsJson = @"{
  ""schema_version"":""news.v1"",
  ""symbol"":""NES"",
  ""state"":""ALLOW"",
  ""directional_bias"":""NEUTRAL"",
  ""importance"":""LOW"",
  ""event_code"":""NONE"",
  ""generated_at_utc"":""2026-09-21T14:00:00Z"",
  ""valid_until_utc"":""2026-09-21T14:01:00Z"",
  ""confidence"":0.8,
  ""reason_codes"":[""NO_MAJOR_EVENT""]
}";

        private const string ValidTradeJson = @"{
  ""schema_version"":""trade.v1"",
  ""symbol"":""NES"",
  ""decision"":""LONG"",
  ""confidence"":0.81,
  ""generated_at_utc"":""2026-09-21T14:00:00Z"",
  ""valid_until_utc"":""2026-09-21T14:00:15Z"",
  ""reason_codes"":[""MARKET_ALIGNED"",""NEWS_ALLOW""]
}";

        [Test]
        public void NewsPayload_ValidStrictShape_IsAccepted()
        {
            var completion = ProviderCompletion.Completed("openai", "test-model", ValidNewsJson, "req-1",
                100, 20, 0.001m, Utc(13,59,59), Utc(14,0,1));

            var result = AgentSchemaValidator.ValidateNews(ValidNewsJson, completion, "news.prompt.v1", Utc(14,0,1));

            Assert.That(result.IsValid, Is.True);
            Assert.That(result.Value.Payload.Symbol, Is.EqualTo("NES"));
            Assert.That(result.Value.Payload.State, Is.EqualTo(NewsGate.Allow));
            Assert.That(result.Value.SchemaVersion, Is.EqualTo("news.v1"));
            Assert.That(result.Value.ProviderName, Is.EqualTo("openai"));
            Assert.That(result.Value.ModelName, Is.EqualTo("test-model"));
            Assert.That(result.Value.RawResponseHash, Is.Not.Empty);
        }

        [TestCase("BUY", "SCHEMA_ENUM_INVALID")]
        [TestCase("SELL", "SCHEMA_ENUM_INVALID")]
        public void NewsPayload_UnknownState_IsRejected(string state, string expected)
        {
            var json = ValidNewsJson.Replace(@"""ALLOW""", @"""" + state + @"""");
            var result = AgentSchemaValidator.ValidateNews(json, ProviderCompletion.CompletedForTests(json), "p1", Utc(14,0,1));

            Assert.That(result.IsValid, Is.False);
            Assert.That(result.ReasonCode, Is.EqualTo(expected));
        }

        [Test]
        public void NewsPayload_WrongSymbol_IsRejected()
        {
            var json = ValidNewsJson.Replace(@"""NES""", @"""ES""");
            var result = AgentSchemaValidator.ValidateNews(json, ProviderCompletion.CompletedForTests(json), "p1", Utc(14,0,1));

            Assert.That(result.IsValid, Is.False);
            Assert.That(result.ReasonCode, Is.EqualTo("SYMBOL_NOT_SUPPORTED"));
        }

        [Test]
        public void NewsPayload_ConfidenceOutsideUnitInterval_IsRejected()
        {
            var json = ValidNewsJson.Replace(@"""confidence"":0.8", @"""confidence"":1.2");
            var result = AgentSchemaValidator.ValidateNews(json, ProviderCompletion.CompletedForTests(json), "p1", Utc(14,0,1));

            Assert.That(result.IsValid, Is.False);
            Assert.That(result.ReasonCode, Is.EqualTo("CONFIDENCE_OUT_OF_RANGE"));
        }

        [Test]
        public void NewsPayload_ValidityBeforeGeneration_IsRejected()
        {
            var json = ValidNewsJson.Replace("2026-09-21T14:01:00Z", "2026-09-21T13:59:00Z");
            var result = AgentSchemaValidator.ValidateNews(json, ProviderCompletion.CompletedForTests(json), "p1", Utc(14,0,1));

            Assert.That(result.IsValid, Is.False);
            Assert.That(result.ReasonCode, Is.EqualTo("INVALID_VALIDITY_WINDOW"));
        }

        [Test]
        public void NewsPayload_UnsupportedSchemaVersion_IsRejected()
        {
            var json = ValidNewsJson.Replace("news.v1", "news.v99");
            var result = AgentSchemaValidator.ValidateNews(json, ProviderCompletion.CompletedForTests(json), "p1", Utc(14,0,1));

            Assert.That(result.IsValid, Is.False);
            Assert.That(result.ReasonCode, Is.EqualTo("SCHEMA_VERSION_UNSUPPORTED"));
        }

        [Test]
        public void NewsPayload_UnknownTopLevelProperty_IsRejected()
        {
            var json = ValidNewsJson.Replace(@"""reason_codes"":", @"""contracts"":5,""reason_codes"":");
            var result = AgentSchemaValidator.ValidateNews(json, ProviderCompletion.CompletedForTests(json), "p1", Utc(14,0,1));

            Assert.That(result.IsValid, Is.False);
            Assert.That(result.ReasonCode, Is.EqualTo("SCHEMA_UNKNOWN_PROPERTY"));
        }

        [Test]
        public void NewsPayload_MissingReasonCodes_IsRejected()
        {
            var json = ValidNewsJson.Replace(@",""reason_codes"":[""NO_MAJOR_EVENT""]", "");
            var result = AgentSchemaValidator.ValidateNews(json, ProviderCompletion.CompletedForTests(json), "p1", Utc(14,0,1));

            Assert.That(result.IsValid, Is.False);
            Assert.That(result.ReasonCode, Is.EqualTo("SCHEMA_REQUIRED_PROPERTY_MISSING"));
        }

        [TestCase(ProviderCompletionStatus.Refused, "PROVIDER_REFUSED")]
        [TestCase(ProviderCompletionStatus.Incomplete, "PROVIDER_INCOMPLETE")]
        [TestCase(ProviderCompletionStatus.TimedOut, "PROVIDER_TIMED_OUT")]
        [TestCase(ProviderCompletionStatus.Failed, "PROVIDER_FAILED")]
        [TestCase(ProviderCompletionStatus.BudgetExhausted, "BUDGET_EXHAUSTED")]
        public void NewsPayload_NonCompletedProviderStatus_IsRejectedBeforeParsing(ProviderCompletionStatus status, string expected)
        {
            var completion = ProviderCompletion.ForStatus(status, "not-json");
            var result = AgentSchemaValidator.ValidateNews("not-json", completion, "p1", Utc(14,0,1));

            Assert.That(result.IsValid, Is.False);
            Assert.That(result.ReasonCode, Is.EqualTo(expected));
        }

        [Test]
        public void TradePayload_ValidLong_IsAccepted()
        {
            var completion = ProviderCompletion.CompletedForTests(ValidTradeJson);
            var result = AgentSchemaValidator.ValidateTrade(ValidTradeJson, completion, "trade.prompt.v1", Utc(14,0,1), "sig-1");

            Assert.That(result.IsValid, Is.True);
            Assert.That(result.Value.Payload.Action, Is.EqualTo(TradeAction.Long));
            Assert.That(result.Value.Payload.SignalId, Is.EqualTo("sig-1"));
        }

        [Test]
        public void TradePayload_UnsupportedDecision_IsRejected()
        {
            var json = ValidTradeJson.Replace(@"""LONG""", @"""BUY""");
            var result = AgentSchemaValidator.ValidateTrade(json, ProviderCompletion.CompletedForTests(json), "p1", Utc(14,0,1), "sig-1");

            Assert.That(result.IsValid, Is.False);
            Assert.That(result.ReasonCode, Is.EqualTo("SCHEMA_ENUM_INVALID"));
        }

        [Test]
        public void TradePayload_UnknownPropertyThatCouldChangeRisk_IsRejected()
        {
            var json = ValidTradeJson.Replace(@"""reason_codes"":", @"""contracts"":5,""reason_codes"":");
            var result = AgentSchemaValidator.ValidateTrade(json, ProviderCompletion.CompletedForTests(json), "p1", Utc(14,0,1), "sig-1");

            Assert.That(result.IsValid, Is.False);
            Assert.That(result.ReasonCode, Is.EqualTo("SCHEMA_UNKNOWN_PROPERTY"));
        }

        private static DateTime Utc(int hour, int minute, int second) =>
            new DateTime(2026, 9, 21, hour, minute, second, DateTimeKind.Utc);
    }
}
