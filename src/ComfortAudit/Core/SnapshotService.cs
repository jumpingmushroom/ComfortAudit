using ComfortAudit.Model;
using UnityEngine;

namespace ComfortAudit.Core
{
    /// <summary>
    /// Owns the cached snapshot, so the panel and the status-effect tooltip share one scan
    /// instead of each running their own. Callers ask for a maximum acceptable age; a scan only
    /// happens if the cache is older than that.
    /// </summary>
    public static class SnapshotService
    {
        private static ComfortSnapshot _current = new ComfortSnapshot();
        private static float _lastScan = -999f;

        public static ComfortSnapshot Current => _current;

        /// <summary>Monotonic counter bumped on every rescan, for cheap change detection.</summary>
        public static int Revision { get; private set; }

        public static ComfortSnapshot Get(float maxAgeSeconds)
        {
            Player player = Player.m_localPlayer;
            if (player == null)
                return _current;

            if (Time.time - _lastScan < maxAgeSeconds)
                return _current;

            _current = ComfortScanner.Scan(player);
            _lastScan = Time.time;
            Revision++;
            return _current;
        }

        public static void Clear()
        {
            _current = new ComfortSnapshot();
            _lastScan = -999f;
            Revision++;
        }
    }
}
