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
        public BatterySnapshot(IEnumerable<DeviceBatteryInfo> devices, DateTime timestamp, string error, bool isDemo, bool isScanning = false)
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
            IsScanning = isScanning;
        }

        public IReadOnlyList<DeviceBatteryInfo> Devices { get; private set; }

        public DateTime Timestamp { get; private set; }

        public bool IsScanning { get; private set; }

        public string ScanStatusText
        {
            get
            {
                string status = IsScanning ? "正在查询" : !string.IsNullOrEmpty(Error) ? "等待重试" : "等待下次扫描";
                return status + (Timestamp == DateTime.MinValue ? " · 尚无已完成扫描" : " · 最近查询结束于 " + Timestamp.ToString("HH:mm:ss"));
            }
        }

        public string Error { get; private set; }

        public bool IsDemo { get; private set; }

        public int ConnectedCount
        {
            get { return Devices.Count(d => d.IsConnected && !d.IsStale && !d.IsSleeping); }
        }

        public int PendingCount
        {
            get { return Devices.Count(d => d.IsSleeping || d.IsConnectionUnknown || (d.IsConnected && d.IsStale)); }
        }

        public int DisconnectedCount
        {
            get { return Devices.Count(d => d.IsDisconnected); }
        }

        public int KnownBatteryCount
        {
            get { return Devices.Count(d => d.IsConnected && !d.IsStale && !d.IsSleeping && d.Level.HasValue); }
        }

        public int? MinimumLevel
        {
            get
            {
                int? min = null;
                foreach (DeviceBatteryInfo device in Devices)
                {
                    if (!device.IsConnected || device.IsStale || device.IsSleeping || !device.Level.HasValue) continue;
                    if (!min.HasValue || device.Level.Value < min.Value) min = device.Level.Value;
                }
                return min;
            }
        }

        public int LowBatteryCount(int threshold)
        {
            return Devices.Count(d => d.IsLowBattery(threshold));
        }

        public bool HasDevices
        {
            get { return Devices.Count > 0; }
        }

        public BatterySnapshot WithFailure(string error)
        {
            return new BatterySnapshot(Devices.Select(d => d.AsUnconfirmedCache()),
                Timestamp, error, IsDemo, IsScanning);
        }

        public static BatterySnapshot Empty(bool isDemo)
        {
            return new BatterySnapshot(Enumerable.Empty<DeviceBatteryInfo>(), DateTime.MinValue, null, isDemo);
        }
    }
}
