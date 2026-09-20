using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using NesTrading.Core.Storage;

namespace NesTrading.Core.Tests.Storage
{
    [TestFixture]
    public class EventStoreTests
    {
        private string _tempDir;

        [SetUp]
        public void SetUp()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "nes-trading-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, true);
        }

        private static readonly DateTime T0 = new DateTime(2026, 9, 21, 14, 0, 0, DateTimeKind.Utc);

        private static TradingEvent E(string id, string session, string type, DateTime ts, string payload = "{}", string signal = null, string trade = null)
            => TradingEvent.Create(id, session, type, ts, payload, signal, trade);

        [Test]
        public void JsonlAppend_AddsExactlyOneLineAndNeverRewritesExistingLine()
        {
            var path = Path.Combine(_tempDir, "session.jsonl");
            var store = new JsonlEventStore(path);
            var first = E("evt-1", "s1", "SESSION_STARTED", T0);
            var second = E("evt-2", "s1", "CANDIDATE_EVALUATED", T0.AddSeconds(1), "{\"score\":90}", "sig-1");

            store.Append(first);
            var firstBytes = File.ReadAllLines(path).Single();

            store.Append(second);
            var lines = File.ReadAllLines(path);

            Assert.That(lines.Length, Is.EqualTo(2));
            Assert.That(lines[0], Is.EqualTo(firstBytes));
        }

        [Test]
        public void JsonlAppend_MalformedExistingLedger_FailsClosedWithoutTruncating()
        {
            var path = Path.Combine(_tempDir, "session.jsonl");
            File.WriteAllText(path, "{not-valid-json" + Environment.NewLine);
            var before = File.ReadAllBytes(path);

            var store = new JsonlEventStore(path);
            Assert.Throws<AuditIntegrityException>(() =>
                store.Append(E("evt-2", "s1", "CANDIDATE_EVALUATED", T0)));

            Assert.That(File.ReadAllBytes(path), Is.EqualTo(before));
        }

        [Test]
        public void JsonlLoad_TamperedPayloadHash_FailsClosed()
        {
            var path = Path.Combine(_tempDir, "session.jsonl");
            var store = new JsonlEventStore(path);
            store.Append(E("evt-1", "s1", "SESSION_STARTED", T0, "{\"ok\":true}"));

            var text = File.ReadAllText(path).Replace("\"ok\":true", "\"ok\":false");
            File.WriteAllText(path, text);

            Assert.Throws<AuditIntegrityException>(() => store.LoadSession("s1"));
        }

        [Test]
        public void JsonlLoad_FiltersBySessionAndPreservesChronologicalOrder()
        {
            var path = Path.Combine(_tempDir, "session.jsonl");
            var store = new JsonlEventStore(path);
            store.Append(E("evt-2", "s1", "SECOND", T0.AddSeconds(2)));
            store.Append(E("evt-x", "other", "OTHER", T0.AddSeconds(1)));
            store.Append(E("evt-1", "s1", "FIRST", T0.AddSeconds(1)));

            var events = store.LoadSession("s1");

            Assert.That(events.Select(x => x.EventId).ToArray(), Is.EqualTo(new[] { "evt-1", "evt-2" }));
        }

        [Test]
        public void TradingEvent_ContainsRequiredAuditFieldsAndStableHash()
        {
            var evt = E("evt-1", "s1", "RISK_APPROVED", T0, "{\"risk\":2.75}", "sig-1", "trade-1");

            Assert.That(evt.EventId, Is.EqualTo("evt-1"));
            Assert.That(evt.SessionId, Is.EqualTo("s1"));
            Assert.That(evt.TimestampUtc, Is.EqualTo(T0));
            Assert.That(evt.Type, Is.EqualTo("RISK_APPROVED"));
            Assert.That(evt.Version, Is.EqualTo(1));
            Assert.That(evt.PayloadHash, Is.Not.Empty);
            Assert.That(evt.PayloadHash, Is.EqualTo(TradingEvent.ComputePayloadHash(evt.PayloadJson)));
        }

        [Test]
        public void SqliteSchema_ContainsAllRequiredTables()
        {
            var path = Path.Combine(_tempDir, "audit.db");
            using (var store = new SqliteAuditStore(path))
            {
                var tables = store.GetTableNames();

                var expected = new[]
                {
                    "trades", "candidate_trades", "agent_decisions", "news_events", "market_states",
                    "risk_events", "execution_events", "session_metrics", "strategy_versions",
                    "supervisor_reports", "event_ledger"
                };

                foreach (var table in expected)
                    Assert.That(tables, Does.Contain(table), "Missing table " + table);
            }
        }

        [TestCase("trades", "session_id")]
        [TestCase("trades", "signal_id")]
        [TestCase("trades", "trade_id")]
        [TestCase("candidate_trades", "session_id")]
        [TestCase("candidate_trades", "signal_id")]
        [TestCase("risk_events", "session_id")]
        [TestCase("risk_events", "signal_id")]
        [TestCase("execution_events", "session_id")]
        [TestCase("execution_events", "signal_id")]
        [TestCase("execution_events", "trade_id")]
        public void SqliteSchema_UsesConsistentCorrelationIdentifiers(string table, string column)
        {
            var path = Path.Combine(_tempDir, "audit.db");
            using (var store = new SqliteAuditStore(path))
            {
                Assert.That(store.GetColumnNames(table), Does.Contain(column));
            }
        }

        [Test]
        public void SqliteAppend_DuplicateEventId_IsAtomicAndRejected()
        {
            var path = Path.Combine(_tempDir, "audit.db");
            using (var store = new SqliteAuditStore(path))
            {
                var evt = E("evt-1", "s1", "SESSION_STARTED", T0);
                store.Append(evt);

                Assert.Throws<AuditIntegrityException>(() => store.Append(evt));
                Assert.That(store.LoadSession("s1").Count, Is.EqualTo(1));
            }
        }

        [Test]
        public void SqliteRestart_ReconstructsTradeCountsLossesLastCloseAndFlatState()
        {
            var path = Path.Combine(_tempDir, "audit.db");

            using (var store = new SqliteAuditStore(path))
            {
                store.Append(E("e1", "s1", "SESSION_STARTED", T0));
                store.Append(E("e2", "s1", "CANDIDATE_EVALUATED", T0.AddSeconds(1), "{}", "sig-1"));
                store.Append(E("e3", "s1", "RISK_APPROVED", T0.AddSeconds(2), "{}", "sig-1"));
                store.Append(E("e4", "s1", "TRADE_OPENED", T0.AddSeconds(3), "{}", "sig-1", "trade-1"));
                store.Append(E("e5", "s1", "TRADE_CLOSED", T0.AddMinutes(4), "{\"netPnlUsd\":-1.25}", "sig-1", "trade-1"));
            }

            using (var reopened = new SqliteAuditStore(path))
            {
                var snapshot = reopened.Reconstruct("s1");

                Assert.That(snapshot.TradesToday, Is.EqualTo(1));
                Assert.That(snapshot.LosingTradesToday, Is.EqualTo(1));
                Assert.That(snapshot.HasOpenPosition, Is.False);
                Assert.That(snapshot.LastTradeClosedAtUtc, Is.EqualTo(T0.AddMinutes(4)));
            }
        }

        [Test]
        public void SqliteRestart_OpenTradeRemainsOpenUntilExplicitClose()
        {
            var path = Path.Combine(_tempDir, "audit.db");
            using (var store = new SqliteAuditStore(path))
            {
                store.Append(E("e1", "s1", "SESSION_STARTED", T0));
                store.Append(E("e2", "s1", "TRADE_OPENED", T0.AddSeconds(1), "{}", "sig-1", "trade-1"));
            }

            using (var reopened = new SqliteAuditStore(path))
            {
                Assert.That(reopened.Reconstruct("s1").HasOpenPosition, Is.True);
            }
        }

        [Test]
        public void TradingEvent_RejectsMissingIdsAndNonUtcTimestamp()
        {
            Assert.Throws<ArgumentException>(() => TradingEvent.Create("", "s1", "TYPE", T0, "{}"));
            Assert.Throws<ArgumentException>(() => TradingEvent.Create("e1", "", "TYPE", T0, "{}"));
            Assert.Throws<ArgumentException>(() => TradingEvent.Create("e1", "s1", "", T0, "{}"));

            var local = DateTime.SpecifyKind(T0, DateTimeKind.Local);
            Assert.Throws<ArgumentException>(() => TradingEvent.Create("e1", "s1", "TYPE", local, "{}"));
        }
    }
}
