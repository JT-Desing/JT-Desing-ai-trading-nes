using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NesTrading.Core.Domain;

namespace NesTrading.Core.Agents
{
    public static class AgentSchemaValidator
    {
        private static readonly HashSet<string> NewsProperties = new HashSet<string>(StringComparer.Ordinal)
        {
            "schema_version", "symbol", "state", "directional_bias", "importance", "event_code",
            "generated_at_utc", "valid_until_utc", "confidence", "reason_codes"
        };

        private static readonly HashSet<string> TradeProperties = new HashSet<string>(StringComparer.Ordinal)
        {
            "schema_version", "symbol", "decision", "confidence",
            "generated_at_utc", "valid_until_utc", "reason_codes"
        };

        private static readonly HashSet<string> NewsStates = Set("ALLOW", "CAUTION", "BLOCK");
        private static readonly HashSet<string> Biases = Set("BULLISH", "BEARISH", "NEUTRAL", "UNKNOWN");
        private static readonly HashSet<string> Importances = Set("LOW", "MEDIUM", "HIGH", "CRITICAL");
        private static readonly HashSet<string> TradeActions = Set("LONG", "SHORT", "WAIT", "EXIT_RECOMMENDATION");

        public static AgentValidationResult<NewsState> ValidateNews(
            string rawJson,
            ProviderCompletion completion,
            string promptVersion,
            DateTime receivedAtUtc)
        {
            var providerFailure = ValidateProvider(completion);
            if (providerFailure != null) return AgentValidationResult<NewsState>.Invalid(providerFailure);
            if (receivedAtUtc.Kind != DateTimeKind.Utc) return AgentValidationResult<NewsState>.Invalid("RECEIVED_AT_NOT_UTC");

            JObject obj;
            var parseReason = TryParseStrict(rawJson, NewsProperties, out obj);
            if (parseReason != null) return AgentValidationResult<NewsState>.Invalid(parseReason);

            var required = Require(obj, NewsProperties);
            if (required != null) return AgentValidationResult<NewsState>.Invalid(required);

            if (!StringEquals(obj, "schema_version", AgentSchemaVersion.NewsV1))
                return AgentValidationResult<NewsState>.Invalid("SCHEMA_VERSION_UNSUPPORTED");

            if (!StringEquals(obj, "symbol", "NES"))
                return AgentValidationResult<NewsState>.Invalid("SYMBOL_NOT_SUPPORTED");

            var stateText = StringValue(obj, "state");
            var biasText = StringValue(obj, "directional_bias");
            var importanceText = StringValue(obj, "importance");
            if (!NewsStates.Contains(stateText) || !Biases.Contains(biasText) || !Importances.Contains(importanceText))
                return AgentValidationResult<NewsState>.Invalid("SCHEMA_ENUM_INVALID");

            decimal confidence;
            if (!TryDecimal(obj["confidence"], out confidence))
                return AgentValidationResult<NewsState>.Invalid("SCHEMA_TYPE_INVALID");
            if (confidence < 0m || confidence > 1m)
                return AgentValidationResult<NewsState>.Invalid("CONFIDENCE_OUT_OF_RANGE");

            DateTime generated, validUntil;
            if (!TryUtc(obj["generated_at_utc"], out generated) || !TryUtc(obj["valid_until_utc"], out validUntil))
                return AgentValidationResult<NewsState>.Invalid("TIMESTAMP_INVALID");
            if (validUntil < generated || validUntil - generated > TimeSpan.FromMinutes(15))
                return AgentValidationResult<NewsState>.Invalid("INVALID_VALIDITY_WINDOW");

            string[] reasons;
            if (!TryReasonCodes(obj["reason_codes"], out reasons))
                return AgentValidationResult<NewsState>.Invalid("SCHEMA_TYPE_INVALID");

            var gate = stateText == "ALLOW" ? NewsGate.Allow :
                stateText == "CAUTION" ? NewsGate.Caution : NewsGate.Block;

            var payload = new NewsState("NES", gate, generated, validUntil, confidence, reasons);
            var envelope = new AgentEnvelope<NewsState>(
                "news-agent", AgentSchemaVersion.NewsV1, promptVersion,
                completion.ProviderName, completion.ModelName,
                generated, validUntil, receivedAtUtc, payload, AgentEnvelope<NewsState>.HashRaw(rawJson));

            return AgentValidationResult<NewsState>.Valid(envelope);
        }

        public static AgentValidationResult<TradeDecision> ValidateTrade(
            string rawJson,
            ProviderCompletion completion,
            string promptVersion,
            DateTime receivedAtUtc,
            string signalId)
        {
            var providerFailure = ValidateProvider(completion);
            if (providerFailure != null) return AgentValidationResult<TradeDecision>.Invalid(providerFailure);
            if (string.IsNullOrWhiteSpace(signalId)) return AgentValidationResult<TradeDecision>.Invalid("SIGNAL_ID_REQUIRED");
            if (receivedAtUtc.Kind != DateTimeKind.Utc) return AgentValidationResult<TradeDecision>.Invalid("RECEIVED_AT_NOT_UTC");

            JObject obj;
            var parseReason = TryParseStrict(rawJson, TradeProperties, out obj);
            if (parseReason != null) return AgentValidationResult<TradeDecision>.Invalid(parseReason);

            var required = Require(obj, TradeProperties);
            if (required != null) return AgentValidationResult<TradeDecision>.Invalid(required);

            if (!StringEquals(obj, "schema_version", AgentSchemaVersion.TradeV1))
                return AgentValidationResult<TradeDecision>.Invalid("SCHEMA_VERSION_UNSUPPORTED");
            if (!StringEquals(obj, "symbol", "NES"))
                return AgentValidationResult<TradeDecision>.Invalid("SYMBOL_NOT_SUPPORTED");

            var decisionText = StringValue(obj, "decision");
            if (!TradeActions.Contains(decisionText))
                return AgentValidationResult<TradeDecision>.Invalid("SCHEMA_ENUM_INVALID");

            decimal confidence;
            if (!TryDecimal(obj["confidence"], out confidence))
                return AgentValidationResult<TradeDecision>.Invalid("SCHEMA_TYPE_INVALID");
            if (confidence < 0m || confidence > 1m)
                return AgentValidationResult<TradeDecision>.Invalid("CONFIDENCE_OUT_OF_RANGE");

            DateTime generated, validUntil;
            if (!TryUtc(obj["generated_at_utc"], out generated) || !TryUtc(obj["valid_until_utc"], out validUntil))
                return AgentValidationResult<TradeDecision>.Invalid("TIMESTAMP_INVALID");
            if (validUntil < generated || validUntil - generated > TimeSpan.FromMinutes(1))
                return AgentValidationResult<TradeDecision>.Invalid("INVALID_VALIDITY_WINDOW");

            string[] reasons;
            if (!TryReasonCodes(obj["reason_codes"], out reasons))
                return AgentValidationResult<TradeDecision>.Invalid("SCHEMA_TYPE_INVALID");

            var action = decisionText == "LONG" ? TradeAction.Long :
                decisionText == "SHORT" ? TradeAction.Short :
                decisionText == "WAIT" ? TradeAction.Wait : TradeAction.ExitRecommendation;

            var payload = new TradeDecision("NES", action, confidence, signalId, generated, validUntil, reasons);
            var envelope = new AgentEnvelope<TradeDecision>(
                "trade-agent", AgentSchemaVersion.TradeV1, promptVersion,
                completion.ProviderName, completion.ModelName,
                generated, validUntil, receivedAtUtc, payload, AgentEnvelope<TradeDecision>.HashRaw(rawJson));

            return AgentValidationResult<TradeDecision>.Valid(envelope);
        }

        private static string ValidateProvider(ProviderCompletion completion)
        {
            if (completion == null) return "PROVIDER_COMPLETION_MISSING";
            switch (completion.Status)
            {
                case ProviderCompletionStatus.Completed: return null;
                case ProviderCompletionStatus.Refused: return "PROVIDER_REFUSED";
                case ProviderCompletionStatus.Incomplete: return "PROVIDER_INCOMPLETE";
                case ProviderCompletionStatus.TimedOut: return "PROVIDER_TIMED_OUT";
                case ProviderCompletionStatus.BudgetExhausted: return "BUDGET_EXHAUSTED";
                default: return "PROVIDER_FAILED";
            }
        }

        private static string TryParseStrict(string rawJson, HashSet<string> allowed, out JObject obj)
        {
            obj = null;
            if (string.IsNullOrWhiteSpace(rawJson)) return "SCHEMA_JSON_INVALID";
            try
            {
                obj = JObject.Parse(rawJson);
            }
            catch (JsonException)
            {
                return "SCHEMA_JSON_INVALID";
            }

            foreach (var p in obj.Properties())
                if (!allowed.Contains(p.Name)) return "SCHEMA_UNKNOWN_PROPERTY";

            return null;
        }

        private static string Require(JObject obj, HashSet<string> required)
        {
            foreach (var name in required)
                if (obj.Property(name, StringComparison.Ordinal) == null)
                    return "SCHEMA_REQUIRED_PROPERTY_MISSING";
            return null;
        }

        private static bool StringEquals(JObject obj, string property, string expected) =>
            string.Equals(StringValue(obj, property), expected, StringComparison.Ordinal);

        private static string StringValue(JObject obj, string property)
        {
            var token = obj[property];
            return token != null && token.Type == JTokenType.String ? token.Value<string>() : null;
        }

        private static bool TryDecimal(JToken token, out decimal value)
        {
            value = 0m;
            if (token == null || (token.Type != JTokenType.Float && token.Type != JTokenType.Integer)) return false;
            try { value = token.Value<decimal>(); return true; }
            catch { return false; }
        }

        private static bool TryUtc(JToken token, out DateTime value)
        {
            value = default(DateTime);
            if (token == null) return false;

            if (token.Type == JTokenType.Date)
            {
                var parsed = token.Value<DateTime>();
                value = parsed.Kind == DateTimeKind.Utc ? parsed : parsed.ToUniversalTime();
                return true;
            }

            if (token.Type != JTokenType.String) return false;

            DateTimeOffset dto;
            if (!DateTimeOffset.TryParse(token.Value<string>(), CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out dto))
                return false;

            value = dto.UtcDateTime;
            return true;
        }

        private static bool TryReasonCodes(JToken token, out string[] reasons)
        {
            reasons = null;
            if (token == null || token.Type != JTokenType.Array) return false;
            var array = (JArray)token;
            if (array.Any(x => x.Type != JTokenType.String || string.IsNullOrWhiteSpace(x.Value<string>()))) return false;
            reasons = array.Select(x => x.Value<string>()).ToArray();
            return true;
        }

        private static HashSet<string> Set(params string[] values) =>
            new HashSet<string>(values, StringComparer.Ordinal);
    }
}
