using System;
using System.Collections.Generic;
using System.Linq;

namespace WirelessBatteryMoniter.Services
{
    // Owned by the single-flight HID scan; keys are physical receivers or paired slots.
    internal sealed class DeviceRetrySchedule
    {
        private sealed class Retry { internal int Failures; internal DateTime Due; }
        private readonly Dictionary<string, Retry> _entries = new Dictionary<string, Retry>(StringComparer.OrdinalIgnoreCase);
        private readonly Func<DateTime> _now;
        internal DeviceRetrySchedule(Func<DateTime> now = null) { _now = now ?? (() => DateTime.UtcNow); }
        internal bool CanRead(string key, bool force)
        {
            Retry retry;
            return force || !_entries.TryGetValue(key, out retry) || _now() >= retry.Due;
        }
        internal void Record(string key, bool success, int interval)
        {
            if (success) { _entries.Remove(key); return; }
            Retry retry;
            if (!_entries.TryGetValue(key, out retry)) _entries[key] = retry = new Retry();
            retry.Failures = Math.Min(5, retry.Failures + 1);
            retry.Due = _now().AddSeconds(BatteryChannel.RetrySeconds(retry.Failures, interval));
        }
        internal void Retain(IEnumerable<string> keys)
        {
            var current = new HashSet<string>(keys, StringComparer.OrdinalIgnoreCase);
            foreach (var key in _entries.Keys.Where(k => !current.Contains(k)).ToList()) _entries.Remove(key);
        }
    }
}
