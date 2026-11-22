using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Enumeration;
using Windows.Storage.Streams;
using WirelessBatteryMoniter.Models;

namespace WirelessBatteryMoniter.Services
{
    /// <summary>
    /// Reads the standard Bluetooth GATT Battery Service (0x180F / 0x2A19) from
    /// paired BLE devices and falls back to Windows' cached device battery property
    /// for devices that expose it (often classic Bluetooth peripherals).
    /// </summary>
    public sealed class BluetoothBatteryProvider : IBatteryProvider
    {
        private static readonly Guid BatteryServiceUuid = new Guid("0000180f-0000-1000-8000-00805f9b34fb");
        private static readonly Guid BatteryLevelUuid = new Guid("00002a19-0000-1000-8000-00805f9b34fb");

        private readonly Dictionary<string, DeviceBatteryInfo> _lastKnownLevels =
            new Dictionary<string, DeviceBatteryInfo>(StringComparer.OrdinalIgnoreCase);

        private readonly Dictionary<string, DateTime> _lastLogTimes =
            new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);

        private readonly List<string> _warnings = new List<string>();
        private readonly Dictionary<string, string> _deviceKeys = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public string LastWarning { get { return _warnings.Count == 0 ? null : string.Join("；", _warnings.Distinct()); } }

        private void Warn(string context, Exception ex)
        {
            string reason = ex is OperationCanceledException ? "读取超时" :
                ex is UnauthorizedAccessException ? "访问被拒绝，请检查蓝牙权限" : ex.Message;
            _warnings.Add(context + "：" + reason);
            LogThrottled(context, context + ": " + reason, ex);
        }

        private static DeviceBatteryInfo Cached(DeviceBatteryInfo old, bool connected)
        {
            return new DeviceBatteryInfo(old.Id, old.Name, old.Kind, old.Address, old.Level,
                connected, true, old.LastSeen, old.ReadingSource, old.ChargeState);
        }

        // Timestamp means when a value was observed, not a new device measurement
        // when Windows supplied its cached BatteryLife property.
        internal DeviceBatteryInfo ResolveReading(string key, string name, string kind, string address,
            int? level, bool connected, DateTime observedAt, BatteryReadingSource source = BatteryReadingSource.WindowsProperty)
        {
            DeviceBatteryInfo old;
            if (!level.HasValue)
                return _lastKnownLevels.TryGetValue(key, out old) ? Cached(old, connected) : null;
            if (!connected && _lastKnownLevels.TryGetValue(key, out old) && old.Level == level)
                return Cached(old, false);
            var reading = new DeviceBatteryInfo(key, name, kind, address, level, connected, !connected, observedAt, source);
            _lastKnownLevels[key] = reading;
            return reading;
        }

        private void AddCachedDevice(List<DeviceBatteryInfo> devices, DeviceInformation info)
        {
            string key;
            DeviceBatteryInfo old;
            if (_deviceKeys.TryGetValue(info.Id, out key) && _lastKnownLevels.TryGetValue(key, out old))
                devices.Add(Cached(old, GetBooleanProperty(info.Properties, "System.Devices.Aep.IsConnected") ?? old.IsConnected));
        }

        public string Name
        {
            get { return "Windows Bluetooth"; }
        }

        public async Task<IReadOnlyList<DeviceBatteryInfo>> GetDevicesAsync(CancellationToken cancellationToken)
        {
            List<DeviceBatteryInfo> results = new List<DeviceBatteryInfo>();
            _warnings.Clear();
            foreach (string kind in new[] { "蓝牙 LE", "经典蓝牙" })
            {
                try
                {
                    results.AddRange(kind == "蓝牙 LE"
                        ? await ScanBleAsync(cancellationToken).ConfigureAwait(false)
                        : await ScanClassicAsync(cancellationToken).ConfigureAwait(false));
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    Warn(kind, ex);
                    foreach (var old in _lastKnownLevels.Values.Where(d => d.Kind == kind))
                        results.Add(Cached(old, old.IsConnected));
                }
            }
            List<DeviceBatteryInfo> deduped = BatteryMonitorService.MergeDuplicateDevices(results);

            return deduped;
        }

        private async Task<List<DeviceBatteryInfo>> ScanBleAsync(CancellationToken cancellationToken)
        {
            List<DeviceBatteryInfo> devices = new List<DeviceBatteryInfo>();
            string selector = BluetoothLEDevice.GetDeviceSelectorFromPairingState(true);
            DeviceInformationCollection infos = await DeviceInformation
                .FindAllAsync(selector, new[] { "System.Devices.BatteryLife", "System.Devices.Aep.IsConnected" })
                .AsTask(cancellationToken)
                .ConfigureAwait(false);

            if (infos == null || infos.Count == 0) return devices;

            Stopwatch budget = Stopwatch.StartNew();
            foreach (DeviceInformation info in infos)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (budget.Elapsed > TimeSpan.FromSeconds(18))
                {
                    _warnings.Add("BLE 扫描时间不足，部分设备使用缓存");
                    AddCachedDevice(devices, info);
                    continue;
                }

                try
                {
                    DeviceBatteryInfo device = await ReadBleDeviceAsync(info, cancellationToken).ConfigureAwait(false);
                    if (device != null) devices.Add(device);
                }
                catch (OperationCanceledException)
                {
                    if (cancellationToken.IsCancellationRequested) throw;
                    Warn(info.Name, new TimeoutException("设备读取超时"));
                    AddCachedDevice(devices, info);
                }
                catch (Exception ex)
                {
                    Warn("BLE " + info.Name, ex);
                    AddCachedDevice(devices, info);
                }
            }

            return devices;
        }

        private async Task<DeviceBatteryInfo> ReadBleDeviceAsync(
            DeviceInformation info,
            CancellationToken cancellationToken)
        {
            BluetoothLEDevice device = null;
            try
            {
                using (var openTimeout = CreateTimeout(cancellationToken, 5000))
                    device = await BluetoothLEDevice
                        .FromIdAsync(info.Id)
                        .AsTask(openTimeout.Token)
                        .ConfigureAwait(false);

                if (device == null) throw new InvalidOperationException("无法打开蓝牙设备");

                string name = FirstNonEmpty(info.Name, device.Name, "未知设备");
                string address = FormatAddress(device.BluetoothAddress);
                string key = "bt:" + (device.BluetoothAddress != 0
                    ? device.BluetoothAddress.ToString("X")
                    : info.Id);

                _deviceKeys[info.Id] = key;
                bool connected = device.ConnectionStatus == BluetoothConnectionStatus.Connected;
                int? level = null;
                BatteryReadingSource source = BatteryReadingSource.BluetoothGatt;

                if (connected)
                {
                    using (CancellationTokenSource timeout = CreateTimeout(cancellationToken, 5000))
                    {
                        try { level = await ReadBatteryLevelAsync(device, timeout.Token).ConfigureAwait(false); }
                        catch (OperationCanceledException)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            Warn(name, new TimeoutException("电量读取超时，尝试备用读数"));
                        }
                        catch (Exception ex)
                        {
                            Warn(name, ex);
                        }
                    }
                }

                if (!level.HasValue)
                {
                    level = GetBatteryFromProperties(info.Properties);
                    source = BatteryReadingSource.WindowsProperty;
                }

                if (!level.HasValue && connected && _lastKnownLevels.ContainsKey(key))
                    _warnings.Add(name + "：未取得电量，显示上次读数");
                return ResolveReading(key, name, "蓝牙 LE", address, level, connected, DateTime.Now, source);
            }
            finally
            {
                if (device != null) device.Dispose();
            }
        }

        private static async Task<int?> ReadBatteryLevelAsync(
            BluetoothLEDevice device,
            CancellationToken cancellationToken)
        {
            GattDeviceServicesResult serviceResult = await device
                .GetGattServicesForUuidAsync(BatteryServiceUuid, BluetoothCacheMode.Uncached)
                .AsTask(cancellationToken)
                .ConfigureAwait(false);

            if (serviceResult.Status != GattCommunicationStatus.Success
                || serviceResult.Services == null
                || serviceResult.Services.Count == 0)
            {
                return null;
            }

            foreach (GattDeviceService service in serviceResult.Services)
            {
                try
                {
                    GattCharacteristicsResult characteristicResult = await service
                        .GetCharacteristicsForUuidAsync(BatteryLevelUuid, BluetoothCacheMode.Uncached)
                        .AsTask(cancellationToken)
                        .ConfigureAwait(false);

                    if (characteristicResult.Status != GattCommunicationStatus.Success
                        || characteristicResult.Characteristics == null)
                    {
                        continue;
                    }

                    foreach (GattCharacteristic characteristic in characteristicResult.Characteristics)
                    {
                        GattReadResult readResult = await characteristic
                            .ReadValueAsync(BluetoothCacheMode.Uncached)
                            .AsTask(cancellationToken)
                            .ConfigureAwait(false);

                        if (readResult.Status != GattCommunicationStatus.Success || readResult.Value == null)
                        {
                            continue;
                        }

                        using (DataReader reader = DataReader.FromBuffer(readResult.Value))
                        {
                            if (reader.UnconsumedBufferLength < 1) continue;
                            byte value = reader.ReadByte();
                            if (value <= 100) return value;
                        }
                    }
                }
                finally
                {
                    service.Dispose();
                }
            }

            return null;
        }

        private async Task<List<DeviceBatteryInfo>> ScanClassicAsync(CancellationToken cancellationToken)
        {
            List<DeviceBatteryInfo> devices = new List<DeviceBatteryInfo>();
            string selector = BluetoothDevice.GetDeviceSelectorFromPairingState(true);
            DeviceInformationCollection infos = await DeviceInformation
                .FindAllAsync(selector, new[] { "System.Devices.BatteryLife", "System.Devices.Aep.IsConnected" })
                .AsTask(cancellationToken)
                .ConfigureAwait(false);

            if (infos == null || infos.Count == 0) return devices;

            foreach (DeviceInformation info in infos)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    int? level = GetBatteryFromProperties(info.Properties);
                    // Unsupported classic devices need no active connection query.
                    if (!level.HasValue && !_deviceKeys.ContainsKey(info.Id)) continue;
                    bool connected = GetBooleanProperty(info.Properties, "System.Devices.Aep.IsConnected") ?? false;
                    string key;
                    if (!_deviceKeys.TryGetValue(info.Id, out key)) key = "bt:" + info.Id;
                    using (var timeout = CreateTimeout(cancellationToken, 5000))
                    using (BluetoothDevice device = await BluetoothDevice.FromIdAsync(info.Id).AsTask(timeout.Token).ConfigureAwait(false))
                    {
                        if (device == null) throw new InvalidOperationException("无法打开蓝牙设备");
                        connected = device.ConnectionStatus == BluetoothConnectionStatus.Connected;
                        if (device.BluetoothAddress != 0) key = "bt:" + device.BluetoothAddress.ToString("X");
                    }
                    _deviceKeys[info.Id] = key;
                    var reading = ResolveReading(key, FirstNonEmpty(info.Name, "未知设备"), "经典蓝牙",
                        string.Empty, level, connected, DateTime.Now);
                    if (reading != null)
                    {
                        devices.Add(reading);
                        if (!level.HasValue && connected) _warnings.Add(info.Name + "：未取得电量，显示上次读数");
                    }
                }
                catch (OperationCanceledException ex)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    Warn("经典蓝牙 " + info.Name, ex);
                    AddCachedDevice(devices, info);
                }
                catch (Exception ex)
                {
                    Warn("经典蓝牙 " + info.Name, ex);
                    AddCachedDevice(devices, info);
                }
            }

            return devices;
        }

        private static int? GetBatteryFromProperties(
            IReadOnlyDictionary<string, object> properties)
        {
            if (properties == null) return null;

            string[] candidates =
            {
                "System.Devices.BatteryLife",
                "System.Devices.BatteryLifePercent",
                "System.Devices.Battery.Level"
            };

            foreach (string key in candidates)
            {
                object raw;
                if (!properties.TryGetValue(key, out raw)) continue;
                int? level = ConvertToLevel(raw);
                if (level.HasValue) return level;
            }

            return null;
        }

        private static bool? GetBooleanProperty(
            IReadOnlyDictionary<string, object> properties,
            string key)
        {
            if (properties == null) return null;
            object raw;
            if (!properties.TryGetValue(key, out raw) || raw == null) return null;

            if (raw is bool) return (bool)raw;
            if (raw is byte) return (byte)raw != 0;
            if (raw is int) return (int)raw != 0;
            if (raw is uint) return (uint)raw != 0;

            bool parsed;
            if (bool.TryParse(raw.ToString(), out parsed)) return parsed;
            return null;
        }

        private static int? ConvertToLevel(object raw)
        {
            if (raw == null) return null;

            if (raw is string)
            {
                string text = ((string)raw).Trim().TrimEnd('%');
                int parsed;
                if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed))
                {
                    return parsed >= 0 && parsed <= 100 ? (int?)parsed : null;
                }
                return null;
            }

            try
            {
                int value = Convert.ToInt32(raw, CultureInfo.InvariantCulture);
                return value >= 0 && value <= 100 ? (int?)value : null;
            }
            catch
            {
                return null;
            }
        }

        private static CancellationTokenSource CreateTimeout(CancellationToken cancellationToken, int milliseconds)
        {
            CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(milliseconds);
            return cts;
        }

        private static string FirstNonEmpty(params string[] values)
        {
            foreach (string value in values)
            {
                if (!string.IsNullOrWhiteSpace(value)) return value.Trim();
            }
            return "未知设备";
        }

        private static string FormatAddress(ulong address)
        {
            if (address == 0) return string.Empty;
            string hex = address.ToString("X12", CultureInfo.InvariantCulture);
            List<string> parts = new List<string>(6);
            for (int i = 0; i < 12; i += 2)
            {
                parts.Add(hex.Substring(i, 2));
            }
            return string.Join(":", parts);
        }

        private void LogThrottled(string key, string message, Exception exception)
        {
            DateTime last;
            if (_lastLogTimes.TryGetValue(key, out last)
                && DateTime.UtcNow - last < TimeSpan.FromMinutes(5))
            {
                return;
            }
            _lastLogTimes[key] = DateTime.UtcNow;
            AppLog.Error(message, exception);

            if (_lastLogTimes.Count > 80) _lastLogTimes.Clear();
        }

        public void Dispose()
        {
            _lastKnownLevels.Clear();
            _deviceKeys.Clear();
            _warnings.Clear();
            _lastLogTimes.Clear();
        }
    }
}
