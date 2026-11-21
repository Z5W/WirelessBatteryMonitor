using System;
using System.Collections.Generic;
using System.Linq;

namespace WirelessBatteryMoniter.Models
{
    /// <summary>
    /// Immutable result of one scan, plus a few derived values used by the tray and UI.
    /// </summary>
    public sealed class BatterySnapshot
    {
        public BatterySnapshot(IEnumerable<DeviceBatteryInfo> devices, DateTime timestamp, string error, bool isDemo)
        {
            Devices = (devices ?? Enumerable.Empty<DeviceBatteryInfo>())
                .OrderByDescending(d => d.IsConnected)
                .ThenBy(d => d.Level.HasValue ? d.Level.Value : int.MaxValue)
                .ThenBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList()
                .AsReadOnly();
            Timestamp = timestamp;
            Error = error;
            IsDemo = isDemo;
        }

        public IReadOnlyList<DeviceBatteryInfo> Devices { get; private set; }

        public DateTime Timestamp { get; private set; }

        public string Error { get; private set; }

        public bool IsDemo { get; private set; }

        public int ConnectedCount
        {
            get { return Devices.Count(d => d.IsConnected && !d.IsStale); }
        }

        public int PendingCount
        {
            get { return Devices.Count(d => d.IsConnected && d.IsStale); }
        }

        public int DisconnectedCount
        {
            get { return Devices.Count(d => !d.IsConnected); }
        }

        public int KnownBatteryCount
        {
            get { return Devices.Count(d => d.IsConnected && !d.IsStale && d.Level.HasValue); }
        }

        public int? MinimumLevel
        {
            get
            {
                int? min = null;
                foreach (DeviceBatteryInfo device in Devices)
                {
                    if (!device.IsConnected || device.IsStale || !device.Level.HasValue) continue;
                    if (!min.HasValue || device.Level.Value < min.Value) min = device.Level.Value;
                }
                return min;
            }
        }

        public int LowBatteryCount(int threshold)
        {
            return Devices.Count(d => d.IsConnected && !d.IsStale && d.Level.HasValue && d.Level.Value <= threshold);
        }

        public bool HasDevices
        {
            get { return Devices.Count > 0; }
        }

        public BatterySnapshot WithFailure(string error)
        {
            return new BatterySnapshot(Devices.Select(d => new DeviceBatteryInfo(
                d.Id, d.Name, d.Kind, d.Address, d.Level, d.IsConnected, true, d.LastSeen)),
                DateTime.Now, error, IsDemo);
        }

        public static BatterySnapshot Empty(bool isDemo)
        {
            return new BatterySnapshot(Enumerable.Empty<DeviceBatteryInfo>(), DateTime.Now, null, isDemo);
        }
    }
}
