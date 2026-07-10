using System;
using System.Collections.Generic;

namespace AnywhereWinUI.Services
{
    /// <summary>
    /// Accumulates only traffic that left via a proxy outbound (not direct/block).
    /// Clash downloadTotal/uploadTotal includes every byte that entered sing-box
    /// (system proxy / TUN), including China-direct downloads — those are excluded here.
    /// </summary>
    public sealed class ProxyTrafficTracker
    {
        private readonly Dictionary<string, (long upload, long download)> _lastById = new(StringComparer.Ordinal);
        private long _sessionDownload;
        private long _sessionUpload;

        public long SessionDownload => _sessionDownload;
        public long SessionUpload => _sessionUpload;

        public void Reset()
        {
            _lastById.Clear();
            _sessionDownload = 0;
            _sessionUpload = 0;
        }

        /// <summary>
        /// Remember current per-connection counters without counting them into the session.
        /// Call once when stats polling starts so open connections do not spike.
        /// </summary>
        public void Seed(ClashConnectionsMessage message)
        {
            _lastById.Clear();
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

        /// <summary>
        /// Apply one /connections snapshot. Returns absolute proxy-session totals and
        /// per-interval proxy speeds (bytes/sec) for the elapsed window.
        /// </summary>
        public (long down, long up, long downSpeed, long upSpeed) Apply(
            ClashConnectionsMessage message,
            double elapsedSeconds)
        {
            if (elapsedSeconds <= 0) elapsedSeconds = 1;

            long deltaDown = 0;
            long deltaUp = 0;
            var seen = new HashSet<string>(StringComparer.Ordinal);

            var connections = message.Connections;
            if (connections != null)
            {
                foreach (var conn in connections)
                {
                    if (conn == null || string.IsNullOrEmpty(conn.Id))
                        continue;

                    seen.Add(conn.Id);
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
                        // Brand-new proxy connection since last poll: count current totals.
                        deltaDown += Math.Max(0, conn.Download);
                        deltaUp += Math.Max(0, conn.Upload);
                    }

                    _lastById[conn.Id] = (conn.Upload, conn.Download);
                }
            }

            // Drop closed connections (their bytes were already counted via deltas).
            if (_lastById.Count > seen.Count)
            {
                var stale = new List<string>();
                foreach (var id in _lastById.Keys)
                {
                    if (!seen.Contains(id))
                        stale.Add(id);
                }
                foreach (var id in stale)
                    _lastById.Remove(id);
            }

            _sessionDownload += deltaDown;
            _sessionUpload += deltaUp;

            long downSpeed = (long)(deltaDown / elapsedSeconds);
            long upSpeed = (long)(deltaUp / elapsedSeconds);

            return (_sessionDownload, _sessionUpload, downSpeed, upSpeed);
        }

        /// <summary>
        /// Clash/sing-box chains: first entry is the final outbound hop used.
        /// </summary>
        internal static bool IsProxyOutbound(ClashConnectionNode conn)
        {
            var chains = conn.Chains;
            if (chains == null || chains.Count == 0)
                return false;

            var finalHop = chains[0];
            if (string.IsNullOrWhiteSpace(finalHop))
                return false;

            // Non-proxy outbounds produced by ConfigBuilder / sing-box defaults
            if (finalHop.Equals("direct", StringComparison.OrdinalIgnoreCase)) return false;
            if (finalHop.Equals("block", StringComparison.OrdinalIgnoreCase)) return false;
            if (finalHop.Equals("dns-out", StringComparison.OrdinalIgnoreCase)) return false;
            if (finalHop.Equals("dns_out", StringComparison.OrdinalIgnoreCase)) return false;
            if (finalHop.Equals("Reject", StringComparison.OrdinalIgnoreCase)) return false;
            if (finalHop.Equals("reject", StringComparison.OrdinalIgnoreCase)) return false;

            // proxy / urltest / selector / concrete node tag → proxy traffic
            return true;
        }
    }
}
