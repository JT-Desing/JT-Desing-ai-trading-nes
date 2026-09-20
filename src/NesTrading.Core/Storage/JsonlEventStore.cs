using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;

namespace NesTrading.Core.Storage
{
    public sealed class JsonlEventStore : IEventStore
    {
        private readonly string _path;
        private readonly object _sync = new object();
        private readonly JsonSerializerSettings _settings = new JsonSerializerSettings
        {
            DateFormatHandling = DateFormatHandling.IsoDateFormat,
            DateTimeZoneHandling = DateTimeZoneHandling.Utc,
            Formatting = Formatting.None
        };

        public JsonlEventStore(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Path is required.", nameof(path));
            _path = path;
            var dir = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        }

        public void Append(TradingEvent evt)
        {
            if (evt == null) throw new ArgumentNullException(nameof(evt));

            lock (_sync)
            {
                var existing = ReadAndValidateAll();
                if (existing.Any(x => string.Equals(x.EventId, evt.EventId, StringComparison.Ordinal)))
                    throw new AuditIntegrityException("Duplicate event ID " + evt.EventId + ".");

                evt.ValidateIntegrity();
                var line = JsonConvert.SerializeObject(evt, _settings);

                using (var stream = new FileStream(_path, FileMode.Append, FileAccess.Write, FileShare.Read))
                using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
                {
                    writer.WriteLine(line);
                    writer.Flush();
                    stream.Flush(true);
                }
            }
        }

        public IReadOnlyList<TradingEvent> LoadSession(string sessionId)
        {
            if (string.IsNullOrWhiteSpace(sessionId)) throw new ArgumentException("sessionId is required.", nameof(sessionId));

            lock (_sync)
            {
                return ReadAndValidateAll()
                    .Where(x => string.Equals(x.SessionId, sessionId, StringComparison.Ordinal))
                    .OrderBy(x => x.TimestampUtc)
                    .ThenBy(x => x.EventId, StringComparer.Ordinal)
                    .ToArray();
            }
        }

        public SessionAuditSnapshot Reconstruct(string sessionId)
        {
            return AuditReconstructor.Reconstruct(LoadSession(sessionId));
        }

        private List<TradingEvent> ReadAndValidateAll()
        {
            var result = new List<TradingEvent>();
            if (!File.Exists(_path)) return result;

            var lineNumber = 0;
            foreach (var line in File.ReadLines(_path, Encoding.UTF8))
            {
                lineNumber++;
                if (string.IsNullOrWhiteSpace(line))
                    throw new AuditIntegrityException("Blank JSONL line at " + lineNumber + ".");

                TradingEvent evt;
                try
                {
                    evt = JsonConvert.DeserializeObject<TradingEvent>(line, _settings);
                }
                catch (Exception ex)
                {
                    throw new AuditIntegrityException("Malformed JSONL at line " + lineNumber + ".", ex);
                }

                if (evt == null)
                    throw new AuditIntegrityException("Null JSONL event at line " + lineNumber + ".");

                evt.ValidateIntegrity();

                if (result.Any(x => string.Equals(x.EventId, evt.EventId, StringComparison.Ordinal)))
                    throw new AuditIntegrityException("Duplicate event ID " + evt.EventId + " in existing ledger.");

                result.Add(evt);
            }

            return result;
        }
    }
}
