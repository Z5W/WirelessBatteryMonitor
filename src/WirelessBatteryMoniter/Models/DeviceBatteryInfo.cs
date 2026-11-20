using System;

namespace WirelessBatteryMoniter.Models
{
    /// <summary>
    /// One paired wireless device and its last known battery reading.
    /// </summary>
    public sealed class DeviceBatteryInfo
    {
        public DeviceBatteryInfo(
            string id,
            string name,
            string kind,
            string address,
            int? level,
            bool isConnected,
            bool isStale,
            DateTime lastSeen)
        {
            Id = id ?? string.Empty;
            Name = string.IsNullOrWhiteSpace(name) ? "未知设备" : name.Trim();
            Kind = kind ?? "蓝牙";
            Address = address ?? string.Empty;
            Level = NormalizeLevel(level);
            IsConnected = isConnected;
            IsStale = isStale;
            LastSeen = lastSeen;
        }

        public string Id { get; private set; }

        public string Name { get; private set; }

        public string Kind { get; private set; }

        public string Address { get; private set; }

        public int? Level { get; private set; }

        public bool IsConnected { get; private set; }

        /// <summary>True when the device is disconnected and the level is a cached reading.</summary>
        public bool IsStale { get; private set; }

        public DateTime LastSeen { get; private set; }

        public bool HasLevel
        {
            get { return Level.HasValue; }
        }

        public string LevelText
        {
            get { return Level.HasValue ? Level.Value + "%" : "--"; }
        }

        public string StateText
        {
            get
            {
                if (IsConnected) return IsStale ? "连接待确认 · 缓存读数" : "已连接";
                return IsStale ? "未连接 · 上次读数" : "未连接";
            }
        }

        public static int? NormalizeLevel(int? level)
        {
            if (!level.HasValue) return null;
            int value = level.Value;
            if (value < 0 || value > 100) return null;
            return value;
        }
    }
}
