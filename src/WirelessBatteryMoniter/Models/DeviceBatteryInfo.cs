using System;

namespace WirelessBatteryMoniter.Models
{
    public enum DeviceActivityState { Unknown, Awake, Asleep }

    public enum BatteryChargeState { Unknown, NotCharging, Charging, Charged }

    public enum BatteryReadingSource { Unknown, DeviceReported, BluetoothGatt, WindowsProperty, VoltageEstimate, CoarseLevel, Demo }

    public enum DeviceConnectionState { Unknown, Connected, Disconnected }

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
            DateTime lastSeen, BatteryReadingSource readingSource = BatteryReadingSource.Unknown, BatteryChargeState chargeState = BatteryChargeState.Unknown, DeviceActivityState activityState = DeviceActivityState.Unknown)
            : this(id, name, kind, address, level,
                isConnected ? DeviceConnectionState.Connected : DeviceConnectionState.Disconnected,
                isStale, lastSeen, readingSource, chargeState, activityState)
        {
        }

        public DeviceBatteryInfo(string id, string name, string kind, string address,
            int? level, DeviceConnectionState connectionState, bool isStale, DateTime lastSeen, BatteryReadingSource readingSource = BatteryReadingSource.Unknown, BatteryChargeState chargeState = BatteryChargeState.Unknown, DeviceActivityState activityState = DeviceActivityState.Unknown)
        {
            Id = id ?? string.Empty;
            Name = string.IsNullOrWhiteSpace(name) ? "未知设备" : name.Trim();
            Kind = kind ?? "蓝牙";
            Address = address ?? string.Empty;
            Level = NormalizeLevel(level);
            ConnectionState = connectionState;
            IsStale = isStale;
            LastSeen = lastSeen;
            ReadingSource = readingSource;
            ChargeState = chargeState;
            ActivityState = activityState;
            OriginalName = Name;
        }

        public string Id { get; private set; }

        public string Name { get; private set; }
        public string OriginalName { get; private set; }
        public DeviceActivityState ActivityState { get; private set; }
        public bool IsSleeping { get { return ActivityState == DeviceActivityState.Asleep; } }
        public bool IsLowBattery(int threshold)
        {
            return IsConnected && !IsStale && !IsSleeping && !IsChargingOrCharged && Level.HasValue && Level.Value <= threshold;
        }
        public string ActivityStateText { get { return IsSleeping ? "休眠" : ActivityState == DeviceActivityState.Awake ? "活动中" : "未知"; } }
        public DeviceBatteryInfo WithDisplayName(string name)
        {
            var copy = (DeviceBatteryInfo)MemberwiseClone();
            copy.Name = name;
            return copy;
        }

        public string Kind { get; private set; }

        public string Address { get; private set; }

        public int? Level { get; private set; }

        public DeviceConnectionState ConnectionState { get; private set; }

        public bool IsConnected { get { return ConnectionState == DeviceConnectionState.Connected; } }

        public bool IsDisconnected { get { return ConnectionState == DeviceConnectionState.Disconnected; } }

        public bool IsConnectionUnknown { get { return ConnectionState == DeviceConnectionState.Unknown; } }

        /// <summary>Whether the reading is cached, independently of link state.</summary>
        public bool IsStale { get; private set; }

        public DateTime LastSeen { get; private set; }

        public BatteryChargeState ChargeState { get; private set; }

        // Cached charging telemetry is historical, never proof of current charging.
        public bool IsChargingOrCharged { get { return IsConnected && !IsStale && !IsSleeping &&
            (ChargeState == BatteryChargeState.Charging || ChargeState == BatteryChargeState.Charged); } }

        public string ChargeStateText
        {
            get
            {
                switch (ChargeState)
                {
                    case BatteryChargeState.Charging: return "正在充电";
                    case BatteryChargeState.Charged: return "充电完成";
                    case BatteryChargeState.NotCharging: return "未在充电";
                    default: return "未知";
                }
            }
        }

        public BatteryReadingSource ReadingSource { get; private set; }

        public bool IsEstimated { get { return ReadingSource == BatteryReadingSource.VoltageEstimate || ReadingSource == BatteryReadingSource.CoarseLevel; } }

        public string ReadingSourceText
        {
            get
            {
                switch (ReadingSource)
                {
                    case BatteryReadingSource.DeviceReported: return "设备上报百分比";
                    case BatteryReadingSource.BluetoothGatt: return "蓝牙 GATT 电池服务";
                    case BatteryReadingSource.WindowsProperty: return "Windows 电量属性";
                    case BatteryReadingSource.VoltageEstimate: return "电压曲线换算";
                    case BatteryReadingSource.CoarseLevel: return "电量档位换算";
                    case BatteryReadingSource.Demo: return "演示数据";
                    default: return "来源未知";
                }
            }
        }

        public string ReadingPrecisionText
        {
            get
            {
                if (!HasLevel) return "暂无有效读数";
                if (ReadingSource == BatteryReadingSource.VoltageEstimate) return "估算值，随电池特性和负载变化";
                if (ReadingSource == BatteryReadingSource.CoarseLevel) return "粗略档位，不代表精确百分比";
                if (ReadingSource == BatteryReadingSource.WindowsProperty) return "系统提供值，可能缓存，原始精度未知";
                if (ReadingSource == BatteryReadingSource.Unknown) return "精度未知";
                if (ReadingSource == BatteryReadingSource.Demo) return "模拟值";
                return "直接采用上报值，实际精度取决于设备";
            }
        }

        public bool HasLevel
        {
            get { return Level.HasValue; }
        }

        public string LevelText
        {
            get { return Level.HasValue ? (IsEstimated ? "约 " : "") + Level.Value + "%" : "--"; }
        }

        public string StateText
        {
            get
            {
                if (IsSleeping) return "休眠 · 上次读数";
                if (IsConnectionUnknown) return IsStale ? "连接状态未知 · 缓存读数" : "连接状态未知";
                if (IsConnected) return IsStale ? "连接待确认 · 缓存读数" : "已连接";
                return IsStale ? "未连接 · 上次读数" : "未连接";
            }
        }

        public DeviceBatteryInfo AsUnconfirmedCache()
        {
            return new DeviceBatteryInfo(Id, OriginalName, Kind, Address, Level,
                IsDisconnected ? DeviceConnectionState.Disconnected : DeviceConnectionState.Unknown,
                true, LastSeen, ReadingSource, ChargeState, DeviceActivityState.Unknown).WithDisplayName(Name);
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
