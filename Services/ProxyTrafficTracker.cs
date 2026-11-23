using System;
using System.Collections.Generic;
using System.Text.Json;

namespace AnywhereWinUI.Services
{
    /// <summary>
    /// Accumulates only traffic that left via a proxy outbound (not direct/block).
    /// Clash downloadTotal/uploadTotal includes every byte that entered sing-box
    /// (system proxy / TUN), including China-direct downloads — those are excluded here.
    /// Includes a zero-allocation Utf8JsonReader path for background/tray hibernation polling.
    /// </summary>
    public sealed class ProxyTrafficTracker
    {
        private readonly object _sync = new();
        private readonly Dictionary<string, (long upload, long download)> _lastById = new(StringComparer.Ordinal);
        private readonly HashSet<string> _seen = new(StringComparer.Ordinal);
        private readonly List<string> _stale = new();
        private long _sessionDownload;
        private long _sessionUpload;

        public long SessionDownload { get { lock (_sync) return _sessionDownload; } }
        public long SessionUpload { get { lock (_sync) return _sessionUpload; } }

        public void Reset()
        {
            lock (_sync)
            {
                _lastById.Clear();
                _seen.Clear();
                _stale.Clear();
                _sessionDownload = 0;
                _sessionUpload = 0;
            }
        }

        /// <summary>
        /// Remember current per-connection counters without counting them into the session.
        /// Call once when stats polling starts so open connections do not spike.
        /// </summary>
        public void Seed(ClashConnectionsMessage message)
        {
            lock (_sync)
            {
                _lastById.Clear();
                _seen.Clear();
                _stale.Clear();
                _sessionDownload = 0;
                _sessionUpload = 0;

                var connections = message.Connections;
                if (connections == null) return;

                foreach (var conn in connections)
                {
                    if (conn == null || string.IsNullOrEmpty(conn.Id))
                        continue;
                    _lastById[conn.Id] = (conn.Upload, conn.Download);
                }
            }
        }

        /// <summary>
        /// Zero-object-allocation streaming parser over raw UTF-8 /connections JSON.
        /// Skips metadata/rules/timestamps completely and reuses existing dictionary keys
        /// via .NET AlternateLookup&lt;ReadOnlySpan&lt;char&gt;&gt; so steady-state polling allocates 0 bytes.
        /// </summary>
        public (long down, long up, long downSpeed, long upSpeed) ApplyUtf8Json(
            ReadOnlySpan<byte> utf8Json,
            bool seedOnly,
            double elapsedSeconds)
        {
            if (elapsedSeconds <= 0) elapsedSeconds = 1;

            lock (_sync)
            {
                if (seedOnly)
                {
                    _lastById.Clear();
                    _seen.Clear();
                    _stale.Clear();
                    _sessionDownload = 0;
                    _sessionUpload = 0;
                }
                else
                {
                    _seen.Clear();
                }

                long deltaDown = 0;
                long deltaUp = 0;

                var dictLookup = _lastById.GetAlternateLookup<ReadOnlySpan<char>>();
                var reader = new Utf8JsonReader(utf8Json);

                Span<char> idBuf = stackalloc char[128];
                Span<char> hopBuf = stackalloc char[64];

                while (reader.Read())
                {
                    if (reader.TokenType != JsonTokenType.PropertyName)
                        continue;

                    if (!reader.ValueTextEquals("connections"u8))
                    {
                        if (!reader.Read()) break;
                        reader.Skip();
                        continue;
                    }

                    if (!reader.Read()) break;
                    if (reader.TokenType != JsonTokenType.StartArray)
                    {
                        reader.Skip();
                        continue;
                    }

                    while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                    {
                        if (reader.TokenType != JsonTokenType.StartObject)
                        {
                            reader.Skip();
                            continue;
                        }

                        int idLen = 0;
                        string? fallbackLongId = null;
                        long upload = 0;
                        long download = 0;
                        bool isProxy = false;

                        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
                        {
                            if (reader.TokenType != JsonTokenType.PropertyName)
                                continue;

                            if (reader.ValueTextEquals("id"u8))
                            {
                                if (!reader.Read()) break;
                                if (reader.TokenType == JsonTokenType.String)
                                {
                                    if (!reader.HasValueSequence && reader.ValueSpan.Length <= 120)
                                    {
                                        idLen = reader.CopyString(idBuf);
                                    }
                                    else
                                    {
                                        fallbackLongId = reader.GetString();
                                    }
                                }
                                else
                                {
                                    reader.Skip();
                                }
                            }
                            else if (reader.ValueTextEquals("upload"u8))
                            {
                                if (!reader.Read()) break;
                                if (reader.TokenType == JsonTokenType.Number)
                                    reader.TryGetInt64(out upload);
                                else
                                    reader.Skip();
                            }
                            else if (reader.ValueTextEquals("download"u8))
                            {
                                if (!reader.Read()) break;
                                if (reader.TokenType == JsonTokenType.Number)
                                    reader.TryGetInt64(out download);
                                else
                                    reader.Skip();
                            }
                            else if (reader.ValueTextEquals("chains"u8))
                            {
                                if (!reader.Read()) break;
                                if (reader.TokenType == JsonTokenType.StartArray)
                                {
                                    bool firstChecked = false;
                                    while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                                    {
                                        if (!firstChecked)
                                        {
                                            firstChecked = true;
                                            if (reader.TokenType == JsonTokenType.String)
                                            {
                                                if (!reader.HasValueSequence && reader.ValueSpan.Length <= 60)
                                                {
                                                    int hopLen = reader.CopyString(hopBuf);
                                                    isProxy = IsProxyHopSpan(hopBuf.Slice(0, hopLen));
                                                }
                                                else
                                                {
                                                    isProxy = IsProxyHopSpan((reader.GetString() ?? string.Empty).AsSpan());
                                                }
                                            }
                                            else
                                            {
                                                reader.Skip();
                                            }
                                        }
                                        else
                                        {
                                            reader.Skip();
                                        }
                                    }
                                }
                                else
                                {
                                    reader.Skip();
                                }
                            }
                            else
                            {
                                // Skip metadata, start, rule, rulePayload without allocating anything
                                if (!reader.Read()) break;
                                reader.Skip();
                            }
                        }

                        ReadOnlySpan<char> idSpan = fallbackLongId != null
                            ? fallbackLongId.AsSpan()
                            : idBuf.Slice(0, idLen);

                        if (idSpan.IsEmpty)
                            continue;

                        if (seedOnly)
                        {
                            string key = fallbackLongId ?? new string(idSpan);
                            _lastById[key] = (upload, download);
                            continue;
                        }

                        if (dictLookup.TryGetValue(idSpan, out string? existingKey, out var prev))
                        {
                            _seen.Add(existingKey);
                            if (isProxy)
                            {
                                deltaDown += Math.Max(0, download - prev.download);
                                deltaUp += Math.Max(0, upload - prev.upload);
                            }
                            _lastById[existingKey] = (upload, download);
                        }
                        else
                        {
                            string newKey = fallbackLongId ?? new string(idSpan);
                            _seen.Add(newKey);
                            if (isProxy)
                            {
                                deltaDown += Math.Max(0, download);
                                deltaUp += Math.Max(0, upload);
                            }
                            _lastById[newKey] = (upload, download);
                        }
                    }
                }

                if (seedOnly)
                {
                    return (0, 0, 0, 0);
                }

                PruneStaleLocked();

                _sessionDownload += deltaDown;
                _sessionUpload += deltaUp;

                long downSpeed = (long)(deltaDown / elapsedSeconds);
                long upSpeed = (long)(deltaUp / elapsedSeconds);

                return (_sessionDownload, _sessionUpload, downSpeed, upSpeed);
            }
        }

        /// <summary>
        /// Apply one /connections snapshot. Returns absolute proxy-session totals and
        /// per-interval proxy speeds (bytes/sec) for the elapsed window.
        /// </summary>
        public (long down, long up, long downSpeed, long upSpeed) Apply(
            ClashConnectionsMessage message,
            double elapsedSeconds)
        {
            if (elapsedSeconds <= 0) elapsedSeconds = 1;

            lock (_sync)
            {
                long deltaDown = 0;
                long deltaUp = 0;
                _seen.Clear();

                var connections = message.Connections;
                if (connections != null)
                {
                    foreach (var conn in connections)
                    {
                        if (conn == null || string.IsNullOrEmpty(conn.Id))
                            continue;

                        _seen.Add(conn.Id);
                        bool isProxy = IsProxyOutbound(conn);

                        if (_lastById.TryGetValue(conn.Id, out var prev))
                        {
                            long dDown = Math.Max(0, conn.Download - prev.download);
                            long dUp = Math.Max(0, conn.Upload - prev.upload);
                            if (isProxy)
                            {
                                deltaDown += dDown;
                                deltaUp += dUp;
                            }
                        }
                        else if (isProxy)
                        {
                            deltaDown += Math.Max(0, conn.Download);
                            deltaUp += Math.Max(0, conn.Upload);
                        }

                        _lastById[conn.Id] = (conn.Upload, conn.Download);
                    }
                }

                PruneStaleLocked();

                _sessionDownload += deltaDown;
                _sessionUpload += deltaUp;

                long downSpeed = (long)(deltaDown / elapsedSeconds);
                long upSpeed = (long)(deltaUp / elapsedSeconds);

                return (_sessionDownload, _sessionUpload, downSpeed, upSpeed);
            }
        }

        private void PruneStaleLocked()
        {
            if (_lastById.Count > _seen.Count)
            {
                _stale.Clear();
                foreach (var id in _lastById.Keys)
                {
                    if (!_seen.Contains(id))
                        _stale.Add(id);
                }
                foreach (var id in _stale)
                    _lastById.Remove(id);
            }
        }

        private static bool IsProxyHopSpan(ReadOnlySpan<char> finalHop)
        {
            finalHop = finalHop.Trim();
            if (finalHop.IsEmpty)
                return false;

            if (finalHop.Equals("direct".AsSpan(), StringComparison.OrdinalIgnoreCase)) return false;
            if (finalHop.Equals("block".AsSpan(), StringComparison.OrdinalIgnoreCase)) return false;
            if (finalHop.Equals("dns-out".AsSpan(), StringComparison.OrdinalIgnoreCase)) return false;
            if (finalHop.Equals("dns_out".AsSpan(), StringComparison.OrdinalIgnoreCase)) return false;
            if (finalHop.Equals("reject".AsSpan(), StringComparison.OrdinalIgnoreCase)) return false;

            return true;
        }

        /// <summary>
        /// Clash/sing-box chains: first entry is the final outbound hop used.
        /// </summary>
        internal static bool IsProxyOutbound(ClashConnectionNode conn)
        {
            var chains = conn.Chains;
            if (chains == null || chains.Count == 0)
                return false;

            return IsProxyHopSpan(chains[0].AsSpan());
        }
    }
}
