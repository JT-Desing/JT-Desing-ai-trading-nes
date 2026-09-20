using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace NesTrading.Core.Storage
{
    public interface IEventStore
    {
        void Append(TradingEvent evt);
        IReadOnlyList<TradingEvent> LoadSession(string sessionId);
        SessionAuditSnapshot Reconstruct(string sessionId);
    }

    public sealed class AuditIntegrityException : Exception
    {
        public AuditIntegrityException(string message) : base(message) { }
        public AuditIntegrityException(string message, Exception inner) : base(message, inner) { }
    }

    public sealed class TradingEvent
    {
        [JsonConstructor]
        public TradingEvent(
            string eventId,
            string sessionId,
            string type,
            DateTime timestampUtc,
            int version,
            string payloadJson,
            string payloadHash,
            string signalId,
            string tradeId)
        {
            EventId = Require(eventId, nameof(eventId));
            SessionId = Require(sessionId, nameof(sessionId));
            Type = Require(type, nameof(type));
            if (timestampUtc.Kind != DateTimeKind.Utc)
                throw new ArgumentException("timestampUtc must be UTC.", nameof(timestampUtc));
            if (version <= 0) throw new ArgumentOutOfRangeException(nameof(version));

            TimestampUtc = timestampUtc;
            Version = version;
            PayloadJson = payloadJson ?? "{}";
            PayloadHash = Require(payloadHash, nameof(payloadHash));
            SignalId = signalId;
            TradeId = tradeId;
        }

        public string EventId { get; }
        public string SessionId { get; }
        public string Type { get; }
        public DateTime TimestampUtc { get; }
        public int Version { get; }
        public string PayloadJson { get; }
        public string PayloadHash { get; }
        public string SignalId { get; }
        public string TradeId { get; }

        public static TradingEvent Create(string eventId, string sessionId, string type, DateTime timestampUtc,
            string payloadJson, string signalId = null, string tradeId = null)
        {
            var payload = string.IsNullOrWhiteSpace(payloadJson) ? "{}" : payloadJson;
            // Refuse malformed payloads before hashing them.
            try { JToken.Parse(payload); }
            catch (JsonException ex) { throw new ArgumentException("payloadJson must be valid JSON.", nameof(payloadJson), ex); }

            return new TradingEvent(eventId, sessionId, type, timestampUtc, 1, payload,
                ComputePayloadHash(payload), signalId, tradeId);
        }

        public void ValidateIntegrity()
        {
            try { JToken.Parse(PayloadJson); }
            catch (JsonException ex) { throw new AuditIntegrityException("Event payload is not valid JSON.", ex); }

            var expected = ComputePayloadHash(PayloadJson);
            if (!string.Equals(expected, PayloadHash, StringComparison.OrdinalIgnoreCase))
                throw new AuditIntegrityException("Event payload hash mismatch for " + EventId + ".");
        }

        public static string ComputePayloadHash(string payloadJson)
        {
            using (var sha = SHA256.Create())
            {
                var bytes = Encoding.UTF8.GetBytes(payloadJson ?? string.Empty);
                var hash = sha.ComputeHash(bytes);
                var sb = new StringBuilder(hash.Length * 2);
                foreach (var b in hash) sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }

        private static string Require(string value, string name)
        {
            if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("Value is required.", name);
            return value;
        }
    }

    public sealed class SessionAuditSnapshot
    {
        public SessionAuditSnapshot(int tradesToday, int losingTradesToday, bool hasOpenPosition,
            DateTime? lastTradeClosedAtUtc, decimal dailyPnlUsd)
        {
            TradesToday = tradesToday;
            LosingTradesToday = losingTradesToday;
            HasOpenPosition = hasOpenPosition;
            LastTradeClosedAtUtc = lastTradeClosedAtUtc;
            DailyPnlUsd = dailyPnlUsd;
        }

        public int TradesToday { get; }
        public int LosingTradesToday { get; }
        public bool HasOpenPosition { get; }
        public DateTime? LastTradeClosedAtUtc { get; }
        public decimal DailyPnlUsd { get; }
    }

    internal static class AuditReconstructor
    {
        public static SessionAuditSnapshot Reconstruct(IEnumerable<TradingEvent> events)
        {
            var trades = 0;
            var losses = 0;
            var open = false;
            DateTime? lastClose = null;
            var pnl = 0m;

            foreach (var evt in events)
            {
                if (evt.Type == "TRADE_OPENED")
                {
                    trades++;
                    open = true;
                }
                else if (evt.Type == "TRADE_CLOSED")
                {
                    open = false;
                    lastClose = evt.TimestampUtc;
                    var payload = JObject.Parse(evt.PayloadJson);
                    var token = payload["netPnlUsd"];
                    if (token != null)
                    {
                        var value = token.Value<decimal>();
                        pnl += value;
                        if (value < 0m) losses++;
                    }
                }
            }

            return new SessionAuditSnapshot(trades, losses, open, lastClose, pnl);
        }
    }
}
