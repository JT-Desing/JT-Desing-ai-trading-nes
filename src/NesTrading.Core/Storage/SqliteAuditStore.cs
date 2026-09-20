using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.Data.Sqlite;

namespace NesTrading.Core.Storage
{
    public sealed class SqliteAuditStore : IEventStore, IDisposable
    {
        private readonly SqliteConnection _connection;
        private bool _disposed;

        public SqliteAuditStore(string databasePath)
        {
            if (string.IsNullOrWhiteSpace(databasePath)) throw new ArgumentException("databasePath is required.", nameof(databasePath));

            var fullPath = Path.GetFullPath(databasePath);
            var dir = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            _connection = new SqliteConnection("Data Source=" + fullPath + ";Pooling=False");
            _connection.Open();
            InitializeSchema();
        }

        public void Append(TradingEvent evt)
        {
            EnsureNotDisposed();
            if (evt == null) throw new ArgumentNullException(nameof(evt));
            evt.ValidateIntegrity();

            using (var tx = _connection.BeginTransaction())
            using (var cmd = _connection.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = @"INSERT INTO event_ledger
(event_id, session_id, timestamp_utc, event_type, version, payload_json, payload_hash, signal_id, trade_id)
VALUES ($event, $session, $time, $type, $version, $payload, $hash, $signal, $trade);";
                cmd.Parameters.AddWithValue("$event", evt.EventId);
                cmd.Parameters.AddWithValue("$session", evt.SessionId);
                cmd.Parameters.AddWithValue("$time", evt.TimestampUtc.ToString("O", CultureInfo.InvariantCulture));
                cmd.Parameters.AddWithValue("$type", evt.Type);
                cmd.Parameters.AddWithValue("$version", evt.Version);
                cmd.Parameters.AddWithValue("$payload", evt.PayloadJson);
                cmd.Parameters.AddWithValue("$hash", evt.PayloadHash);
                cmd.Parameters.AddWithValue("$signal", (object)evt.SignalId ?? DBNull.Value);
                cmd.Parameters.AddWithValue("$trade", (object)evt.TradeId ?? DBNull.Value);

                try
                {
                    cmd.ExecuteNonQuery();
                    tx.Commit();
                }
                catch (SqliteException ex)
                {
                    try { tx.Rollback(); } catch { }
                    throw new AuditIntegrityException("SQLite audit append failed; no event was committed.", ex);
                }
            }
        }

        public IReadOnlyList<TradingEvent> LoadSession(string sessionId)
        {
            EnsureNotDisposed();
            if (string.IsNullOrWhiteSpace(sessionId)) throw new ArgumentException("sessionId is required.", nameof(sessionId));

            var result = new List<TradingEvent>();
            using (var cmd = _connection.CreateCommand())
            {
                cmd.CommandText = @"SELECT event_id, session_id, event_type, timestamp_utc, version,
payload_json, payload_hash, signal_id, trade_id
FROM event_ledger WHERE session_id = $session
ORDER BY timestamp_utc ASC, rowid ASC;";
                cmd.Parameters.AddWithValue("$session", sessionId);

                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        DateTime timestamp;
                        if (!DateTime.TryParse(reader.GetString(3), CultureInfo.InvariantCulture,
                            DateTimeStyles.RoundtripKind, out timestamp))
                            throw new AuditIntegrityException("Stored event has invalid timestamp.");

                        if (timestamp.Kind != DateTimeKind.Utc)
                            timestamp = timestamp.ToUniversalTime();

                        var evt = new TradingEvent(
                            reader.GetString(0),
                            reader.GetString(1),
                            reader.GetString(2),
                            timestamp,
                            reader.GetInt32(4),
                            reader.GetString(5),
                            reader.GetString(6),
                            reader.IsDBNull(7) ? null : reader.GetString(7),
                            reader.IsDBNull(8) ? null : reader.GetString(8));

                        evt.ValidateIntegrity();
                        result.Add(evt);
                    }
                }
            }

            return result;
        }

        public SessionAuditSnapshot Reconstruct(string sessionId)
        {
            return AuditReconstructor.Reconstruct(LoadSession(sessionId));
        }

        public IReadOnlyList<string> GetTableNames()
        {
            EnsureNotDisposed();
            var names = new List<string>();
            using (var cmd = _connection.CreateCommand())
            {
                cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%' ORDER BY name;";
                using (var reader = cmd.ExecuteReader())
                    while (reader.Read()) names.Add(reader.GetString(0));
            }
            return names;
        }

        public IReadOnlyList<string> GetColumnNames(string table)
        {
            EnsureNotDisposed();
            if (string.IsNullOrWhiteSpace(table)) throw new ArgumentException("table is required.", nameof(table));
            if (!GetTableNames().Contains(table, StringComparer.Ordinal))
                throw new ArgumentException("Unknown table.", nameof(table));

            var columns = new List<string>();
            using (var cmd = _connection.CreateCommand())
            {
                // Table is selected from the schema-derived allow-list above, not user-controlled SQL.
                cmd.CommandText = "PRAGMA table_info([" + table.Replace("]", "]]") + "]);";
                using (var reader = cmd.ExecuteReader())
                    while (reader.Read()) columns.Add(reader.GetString(1));
            }
            return columns;
        }

        private void InitializeSchema()
        {
            var assembly = typeof(SqliteAuditStore).Assembly;
            using (var stream = assembly.GetManifestResourceStream("NesTrading.Core.Storage.Schema.sql"))
            {
                if (stream == null) throw new InvalidOperationException("Embedded Schema.sql was not found.");
                using (var reader = new StreamReader(stream))
                using (var cmd = _connection.CreateCommand())
                {
                    cmd.CommandText = reader.ReadToEnd();
                    cmd.ExecuteNonQuery();
                }
            }
        }

        private void EnsureNotDisposed()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(SqliteAuditStore));
        }

        public void Dispose()
        {
            if (_disposed) return;
            _connection.Dispose();
            _disposed = true;
        }
    }
}
