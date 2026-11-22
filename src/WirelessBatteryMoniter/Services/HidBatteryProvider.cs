using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using WirelessBatteryMoniter.Models;
using WirelessBatteryMoniter.Services.Hid;

namespace WirelessBatteryMoniter.Services
{
    /// <summary>
    /// Reads 2.4 GHz receivers through HID. Covers the common ATK/VXE/CompX
    /// report-8 family and Logitech HID++ 2.0 / HID++ 1.0 receivers. A vendor
    /// response is accepted only when its echo/checksum and battery range agree.
    /// </summary>
    public sealed class HidBatteryProvider : IBatteryProvider
    {
        private readonly HashSet<string> _logged = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, DeviceBatteryInfo> _lastKnown =
            new Dictionary<string, DeviceBatteryInfo>(StringComparer.OrdinalIgnoreCase);
        private CancellationToken _scanToken;
        public string LastWarning { get; private set; }

        public string Name
        {
            get { return "HID 2.4G"; }
        }

        public Task<IReadOnlyList<DeviceBatteryInfo>> GetDevicesAsync(CancellationToken cancellationToken)
        {
            return Task.Run<IReadOnlyList<DeviceBatteryInfo>>(
                delegate { return Scan(cancellationToken); },
                cancellationToken);
        }

        private IReadOnlyList<DeviceBatteryInfo> Scan(CancellationToken cancellationToken)
        {
            _scanToken = cancellationToken;
            LastWarning = null;
            List<DeviceBatteryInfo> result = new List<DeviceBatteryInfo>();
            List<HidDeviceInfo> interfaces;
            try
            {
                interfaces = HidEnumerator.Enumerate();
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                AppLog.Error("Could not enumerate HID devices.", ex);
                throw;
            }

            Dictionary<string, List<HidDeviceInfo>> groups = new Dictionary<string, List<HidDeviceInfo>>(StringComparer.OrdinalIgnoreCase);
            foreach (HidDeviceInfo info in interfaces)
            {
                if (!IsCandidate(info)) continue;
                string key = info.PhysicalKey;
                List<HidDeviceInfo> group;
                if (!groups.TryGetValue(key, out group))
                {
                    group = new List<HidDeviceInfo>();
                    groups[key] = group;
                }
                group.Add(info);
            }

            foreach (KeyValuePair<string, List<HidDeviceInfo>> entry in groups)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    List<DeviceBatteryInfo> found = new List<DeviceBatteryInfo>();
                    if (entry.Value[0].VendorId == 0x046D)
                        found.AddRange(TryLogitech(entry.Value));
                    else
                    {
                        DeviceBatteryInfo device = IsXinmengReceiver(entry.Value[0])
                            ? TryXinmeng(entry.Value)
                            : TryCompx(entry.Value) ?? TryAtk(entry.Value);
                        if (device != null) found.Add(device);
                    }
                    if(found.Count==0) LastWarning="部分 HID 接收器未返回有效读数";
                    foreach (DeviceBatteryInfo device in found)
                    {
                        if (result.Any(d => string.Equals(d.Id, device.Id, StringComparison.OrdinalIgnoreCase))) continue;
                        result.Add(device);
                        LogOnce("hid-found-" + device.Id,
                            string.Format("HID 2.4G device: {0} {1}; id={2}", device.Name, device.LevelText, device.Id));
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    LastWarning="部分 HID 设备查询失败";
                    LogOnce("hid-group-" + entry.Key, "HID 2.4G read failed for " + entry.Key + ": " + ex.Message);
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
            return ApplyCache(result);
        }

        internal static bool IsCandidate(HidDeviceInfo info)
        {
            // This receiver has only a Feature Report on its vendor interface.
            if (IsXinmengReceiver(info)) return true;
            if (info.OutputReportLength <= 0) return false;

            bool atk = (info.VendorId == 0x373B || info.VendorId == 0x3554)
                && ((info.UsagePage == 0xFF05 && info.OutputReportLength >= 64)
                    || (info.UsagePage == 0xFF02 && info.OutputReportLength >= 17));

            bool compx = info.UsagePage == 0xFF02
                && info.Usage == 0x0002
                && info.OutputReportLength >= 17
                && (info.VendorId == 0x3554 || info.VendorId == 0x373B
                    || info.VendorId == 0x320F || info.VendorId == 0x3151
                    || info.VendorId == 0x25A7 || info.VendorId == 0x3367);

            bool logitech = info.VendorId == 0x046D
                && info.UsagePage == 0xFF00
                && (info.Usage == 1 || info.Usage == 2)
                && info.OutputReportLength >= 7;

            return atk || compx || logitech;
        }

        private static bool IsXinmengReceiver(HidDeviceInfo info)
        {
            // Restrict to the interface and receiver verified against hardware.
            return info.VendorId == 0x3151 && info.ProductId == 0x5045
                && info.UsagePage == 0xFFFF && info.Usage == 2
                && info.FeatureReportLength == 65;
        }

        internal static bool TryParseXinmengBattery(byte[] report, out int level)
        {
            level = 0;
            // Native buffer includes report ID 0. F7 payload: mouse battery at 2,
            // mouse sleep flag at 4, paired-device mask at 6, RF boot flag at 8.
            // Readiness at payload[0] is not connectivity (observed as 0 online).
            if (report == null || report.Length != 65 || report[0] != 0) return false;
            if (report[7] != 2 && report[7] != 3) return false;
            if (report[5] != 0 || report[9] != 0 || report[3] > 100) return false;
            level = report[3];
            return true;
        }

        private DeviceBatteryInfo TryXinmeng(List<HidDeviceInfo> group)
        {
            HidDeviceInfo info = group.FirstOrDefault(IsXinmengReceiver);
            if (info == null) return null;
            // Query status only. Never send F6 target selection, pairing or settings.
            byte[] query = new byte[65];
            query[1] = 0xF7;
            try
            {
                using (HidDevice device = new HidDevice(info.Path, _scanToken))
                {
                    for (int attempt = 0; attempt < 2; attempt++)
                    {
                        _scanToken.ThrowIfCancellationRequested();
                        if (!device.SetFeature(query)) continue;
                        if (_scanToken.WaitHandle.WaitOne(30)) _scanToken.ThrowIfCancellationRequested();
                        byte[] reply = device.GetFeature(65);
                        _scanToken.ThrowIfCancellationRequested();
                        int level;
                        if (TryParseXinmengBattery(reply, out level))
                            return MakeDevice(info, "XINMENG 2.4G 鼠标", level, "xinmeng-f7");
                    }
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                LogOnce("xinmeng-" + info.Path, "XINMENG feature battery read failed: " + ex.Message);
            }
            return null;
        }

        private DeviceBatteryInfo TryAtk(List<HidDeviceInfo> group)
        {
            HidDeviceInfo wide = group.FirstOrDefault(i =>
                (i.VendorId == 0x373B || i.VendorId == 0x3554)
                && i.UsagePage == 0xFF05 && i.OutputReportLength >= 64);
            if (wide != null)
            {
                DeviceBatteryInfo device = ReadAtkWide(wide);
                if (device != null) return device;
            }

            HidDeviceInfo narrow = group.FirstOrDefault(i =>
                (i.VendorId == 0x373B || i.VendorId == 0x3554)
                && i.UsagePage == 0xFF02 && i.OutputReportLength >= 17);
            if (narrow != null) return ReadAtkNarrow(narrow);
            return null;
        }

        private DeviceBatteryInfo ReadAtkWide(HidDeviceInfo info)
        {
            byte[] command = new byte[Math.Max(64, info.OutputReportLength)];
            command[0] = 0x08;
            byte[] payload = { 0x7D, 0x72, 0x02, 0x00, 0x01, 0x07, 0x01 };
            Array.Copy(payload, 0, command, 1, payload.Length);

            try
            {
                using (HidDevice device = new HidDevice(info.Path, _scanToken))
                {
                    for (int attempt = 0; attempt < 3; attempt++)
                    {
                        if (!device.TryWrite(command, 400)) continue;
                        Thread.Sleep(100);
                        for (int read = 0; read < 5; read++)
                        {
                            byte[] reply = device.Read(info.InputReportLength, 150);
                            if (reply == null || reply.Length < 8) continue;
                            if (reply[1] == 0x72 && reply[5] == 0x07)
                            {
                                int level = reply[7];
                                if (level >= 0 && level <= 100)
                                {
                                    return MakeDevice(info, FriendlyName(info, "ATK / VXE 2.4G 鼠标"), level, "atk64");
                                }
                            }
                        }
                    }
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                LogOnce("atk64-" + info.Path, "ATK wide HID read failed: " + ex.Message);
            }
            return null;
        }

        private DeviceBatteryInfo ReadAtkNarrow(HidDeviceInfo info)
        {
            byte[] command = new byte[Math.Max(17, info.OutputReportLength)];
            command[0] = 0x08;
            command[1] = 0x04;
            command[command.Length - 1] = 0x49;

            try
            {
                using (HidDevice device = new HidDevice(info.Path, _scanToken))
                {
                    for (int attempt = 0; attempt < 3; attempt++)
                    {
                        if (!device.TryWrite(command, 400)) continue;
                        Thread.Sleep(100);
                        for (int read = 0; read < 5; read++)
                        {
                            byte[] reply = device.Read(info.InputReportLength, 150);
                            if (reply == null || reply.Length < 7) continue;
                            if (reply[0] != 0x08 || reply[1] != 0x04) continue;
                            int level = reply[6];
                            if (level >= 0 && level <= 100)
                            {
                                return MakeDevice(info, FriendlyName(info, "VXE / VGN 2.4G 鼠标"), level, "atk17");
                            }
                        }
                    }
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                LogOnce("atk17-" + info.Path, "ATK narrow HID read failed: " + ex.Message);
            }
            return null;
        }

        private DeviceBatteryInfo TryCompx(List<HidDeviceInfo> group)
        {
            HidDeviceInfo info = group.FirstOrDefault(i =>
                i.UsagePage == 0xFF02 && i.Usage == 0x0002 && i.OutputReportLength >= 17);
            if (info == null) return null;

            byte[] frame = new byte[17];
            frame[0] = 0x08;
            frame[1] = 0x04;
            frame[16] = CompxChecksum(frame);
            try
            {
                using (HidDevice device = new HidDevice(info.Path, _scanToken))
                {
                    for (int attempt = 0; attempt < 4; attempt++)
                    {
                        if (!device.TryWrite(frame, 400)) continue;
                        Thread.Sleep(120);
                        for (int read = 0; read < 4; read++)
                        {
                            byte[] reply = device.Read(info.InputReportLength, 180);
                            if (reply == null || reply.Length < 17) continue;
                            if (reply[0] != 0x08 || reply[1] != 0x04) continue;
                            if (reply[16] != CompxChecksum(reply)) continue;
                            int level = reply[6];
                            if (level >= 0 && level <= 100)
                            {
                                return MakeDevice(info, FriendlyName(info, "2.4G 无线鼠标"), level, "compx");
                            }
                        }
                    }
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                LogOnce("compx-" + info.Path, "CompX HID read failed: " + ex.Message);
            }
            return null;
        }

        private static byte CompxChecksum(byte[] frame)
        {
            int sum = 0;
            for (int i = 0; i < 16; i++) sum += frame[i];
            return (byte)((0x55 - sum) & 0xFF);
        }

        private List<DeviceBatteryInfo> TryLogitech(List<HidDeviceInfo> group)
        {
            List<DeviceBatteryInfo> results = new List<DeviceBatteryInfo>();
            HidDeviceInfo longInfo = group.FirstOrDefault(i =>
                i.UsagePage == 0xFF00 && i.Usage == 2 && i.OutputReportLength >= 20);
            HidDeviceInfo shortInfo = group.FirstOrDefault(i =>
                i.UsagePage == 0xFF00 && i.Usage == 1 && i.OutputReportLength >= 7);
            if (longInfo == null) return results;
            string groupKey = group[0].PhysicalKey;
            try
            {
                using (HidDevice longDevice = new HidDevice(longInfo.Path, _scanToken))
                using (HidDevice shortDevice = shortInfo == null ? null : new HidDevice(shortInfo.Path, _scanToken))
                {
                    LogitechChannel channel = new LogitechChannel(longDevice, shortDevice);
                    channel.LongInfo = longInfo;
                    for (int slot = 1; slot <= 6; slot++)
                    {
                        _scanToken.ThrowIfCancellationRequested();
                        DeviceBatteryInfo device = ReadLogitechSlot(channel, groupKey, slot);
                        if (device != null) results.Add(device);
                    }
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                LogOnce("logitech-" + groupKey, "Logitech HID read failed: " + ex.Message);
            }
            return results;
        }

        internal IReadOnlyList<DeviceBatteryInfo> ApplyCache(IEnumerable<DeviceBatteryInfo> readings)
        {
            List<DeviceBatteryInfo> result = readings.ToList();
            HashSet<string> current = new HashSet<string>(result.Select(d => d.Id), StringComparer.OrdinalIgnoreCase);
            foreach (DeviceBatteryInfo device in result)
            {
                _lastKnown[device.Id] = device;
            }
            foreach (DeviceBatteryInfo old in _lastKnown.Values)
            {
                if (!current.Contains(old.Id))
                {
                    // Missing telemetry cannot distinguish sleep, query failure and
                    // disconnection, regardless of how many polls have missed.
                    result.Add(old.AsUnconfirmedCache());
                }
            }
            return result.AsReadOnly();
        }

        private DeviceBatteryInfo ReadLogitechSlot(LogitechChannel channel, string groupKey, int slot)
        {
            LogitechReply ping = channel.Request(slot, 0x00, 0x01, new byte[0], 1500);
            if (!ping.Success)
            {
                if (ping.Error && (ping.ErrorCode == 0x08 || ping.ErrorCode == 0x09 || ping.ErrorCode == 0x04))
                {
                    return null;
                }
                if (!ping.Error) return null;
                // error 0x01 means HID++ 1.0: fall through to register reads
            }

            string name = ReadLogitechName(channel, slot);
            if (string.IsNullOrWhiteSpace(name)) name = FriendlyName(channel.LongInfo, "Logitech 2.4G 无线设备");

            int unified = channel.FindFeature(slot, 0x1004);
            if (unified > 0)
            {
                LogitechReply reply = channel.Request(slot, unified, 0x01, new byte[0], 500);
                if (reply.Success && reply.Params.Length >= 3)
                {
                    int level = reply.Params[0];
                    if (level > 0 && level <= 100)
                    {
                        return MakeDevice(channel.LongInfo, name, level, "logitech-" + slot, BatteryReadingSource.DeviceReported, ParseLogitechChargeState(reply.Params));
                    }
                    if (level == 0 && reply.Params[1] != 0)
                    {
                        int approximate;
                        if (ApproximateLevels.TryGetValue(reply.Params[1], out approximate))
                        {
                            return MakeDevice(channel.LongInfo, name, approximate, "logitech-" + slot, BatteryReadingSource.CoarseLevel, ParseLogitechChargeState(reply.Params));
                        }
                    }
                }
            }

            int status = channel.FindFeature(slot, 0x1000);
            if (status > 0)
            {
                LogitechReply reply = channel.Request(slot, status, 0x00, new byte[0], 500);
                if (reply.Success && reply.Params.Length >= 1)
                {
                    int level = reply.Params[0];
                    if (level > 0 && level <= 100)
                    {
                        return MakeDevice(channel.LongInfo, name, level, "logitech-" + slot, BatteryReadingSource.DeviceReported, ParseLogitechChargeState(reply.Params));
                    }
                }
            }

            int voltage = channel.FindFeature(slot, 0x1001);
            if (voltage > 0)
            {
                LogitechReply reply = channel.Request(slot, voltage, 0x00, new byte[0], 500);
                if (reply.Success && reply.Params.Length >= 2)
                {
                    int mv = (reply.Params[0] << 8) | reply.Params[1];
                    if (mv >= 2500 && mv <= 5000)
                    {
                        return MakeDevice(channel.LongInfo, name, VoltageToPercent(mv), "logitech-" + slot, BatteryReadingSource.VoltageEstimate);
                    }
                }
            }

            int registerLevel;
            BatteryReadingSource registerSource;
            if (channel.TryReadBatteryRegister(slot, out registerLevel, out registerSource) && registerLevel >= 0 && registerLevel <= 100)
            {
                return MakeDevice(channel.LongInfo, name, registerLevel, "logitech-" + slot, registerSource);
            }

            return null;
        }

        // HID++ 0x1000 / 0x1004: third parameter is the battery status enum.
        // Protocol cross-check: pwr-Solaar/Solaar hidpp20.py and common.BatteryStatus.
        // Do not reuse this decoder for voltage flags or HID++ 1.0 registers.
        internal static BatteryChargeState ParseLogitechChargeState(byte[] parameters)
        {
            if (parameters == null || parameters.Length < 3) return BatteryChargeState.Unknown;
            switch (parameters[2])
            {
                case 0: return BatteryChargeState.NotCharging;
                case 1:
                case 2:
                case 4: return BatteryChargeState.Charging;
                case 3: return BatteryChargeState.Charged;
                default: return BatteryChargeState.Unknown; // Errors and reserved states.
            }
        }

        private string ReadLogitechName(LogitechChannel channel, int slot)
        {
            int feature = channel.FindFeature(slot, 0x0005);
            if (feature <= 0) return null;
            LogitechReply lengthReply = channel.Request(slot, feature, 0x00, new byte[0], 500);
            if (!lengthReply.Success || lengthReply.Params.Length < 1) return null;
            int length = lengthReply.Params[0];
            if (length <= 0 || length > 128) return null;

            StringBuilder name = new StringBuilder();
            for (int offset = 0; offset < length; offset += 16)
            {
                LogitechReply chunk = channel.Request(slot, feature, 0x01,
                    new byte[] { (byte)offset }, 250);
                if (!chunk.Success) break;
                int count = Math.Min(16, length - offset);
                for (int i = 0; i < count && i < chunk.Params.Length; i++)
                {
                    byte value = chunk.Params[i];
                    if (value == 0) break;
                    if (value >= 0x20) name.Append((char)value);
                }
            }
            return name.ToString().Trim();
        }

        private static readonly Dictionary<int, int> ApproximateLevels = new Dictionary<int, int>
        {
            { 8, 90 }, { 4, 50 }, { 2, 20 }, { 1, 5 }
        };

        private static int VoltageToPercent(int mv)
        {
            int[,] curve =
            {
                { 4186, 100 }, { 4067, 90 }, { 3989, 80 }, { 3922, 70 },
                { 3859, 60 }, { 3811, 50 }, { 3778, 40 }, { 3751, 30 },
                { 3717, 20 }, { 3671, 10 }, { 3646, 5 }, { 3579, 2 }, { 3500, 0 }
            };
            if (mv >= curve[0, 0]) return 100;
            for (int i = 0; i < curve.GetLength(0) - 1; i++)
            {
                int highMv = curve[i, 0];
                int lowMv = curve[i + 1, 0];
                if (mv >= lowMv)
                {
                    int highPercent = curve[i, 1];
                    int lowPercent = curve[i + 1, 1];
                    return (int)Math.Round(lowPercent + (mv - lowMv) * (highPercent - lowPercent) / (double)(highMv - lowMv));
                }
            }
            return 0;
        }

        private static string FriendlyName(HidDeviceInfo info, string fallback)
        {
            string name = info.DisplayName;
            if (string.IsNullOrWhiteSpace(name)
                || name.IndexOf("receiver", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("USB Input Device", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return fallback;
            }
            return name;
        }

        internal static DeviceBatteryInfo MakeDevice(HidDeviceInfo info, string name, int level, string suffix, BatteryReadingSource readingSource = BatteryReadingSource.DeviceReported, BatteryChargeState chargeState = BatteryChargeState.Unknown)
        {
            // These are alternate battery queries for the same single mouse,
            // not device identities. Keep receiver instances and Logitech slots distinct.
            if (suffix == "compx" || suffix == "atk17" || suffix == "atk64") suffix = "mouse";
            string id = "hid:" + info.PhysicalKey + ":" + suffix;
            return new DeviceBatteryInfo(
                id,
                name,
                "2.4G",
                string.Format("{0:X4}:{1:X4}", info.VendorId, info.ProductId),
                level,
                true,
                false,
                DateTime.Now, readingSource, chargeState);
        }

        private void LogOnce(string key, string message)
        {
            if (!_logged.Add(key)) return;
            AppLog.Info(message);
        }

        public void Dispose()
        {
            _logged.Clear();
            _lastKnown.Clear();
        }

        internal sealed class LogitechReply
        {
            public bool Success;
            public bool Error;
            public int ErrorCode;
            public byte[] Params = new byte[0];
        }

        internal sealed class LogitechChannel
        {
            private readonly HidDevice _long;
            private readonly HidDevice _short;

            public LogitechChannel(HidDevice longDevice, HidDevice shortDevice)
            {
                _long = longDevice;
                _short = shortDevice;
            }

            public HidDeviceInfo LongInfo { get; set; }

            public LogitechReply Request(int slot, int feature, int function, byte[] parameters, int timeoutMs)
            {
                byte functionByte = (byte)(((function & 0x0F) << 4) | 0x0A);
                byte[] request = new byte[20];
                request[0] = 0x11;
                request[1] = (byte)slot;
                request[2] = (byte)feature;
                request[3] = functionByte;
                if (parameters != null && parameters.Length > 0)
                {
                    Array.Copy(parameters, 0, request, 4, Math.Min(parameters.Length, 16));
                }

                try
                {
                    _long.Write(request, 400);
                }
                catch (OperationCanceledException) { throw; }
                catch
                {
                    return new LogitechReply();
                }

                DateTime deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
                while (DateTime.UtcNow < deadline)
                {
                    LogitechReply reply = CheckReply(_short == null ? null : _short.Read(7, 60), slot, feature, functionByte);
                    if (reply != null) return reply;
                    reply = CheckReply(_long.Read(20, 60), slot, feature, functionByte);
                    if (reply != null) return reply;
                    Thread.Sleep(5);
                }
                return new LogitechReply();
            }

            private static LogitechReply CheckReply(byte[] data, int slot, int feature, byte functionByte)
            {
                if (data == null || data.Length < 4) return null;
                if (data[1] != slot) return null;

                if ((data[2] == 0x8F || data[2] == 0xFF) && data.Length >= 6
                    && data[3] == feature && data[4] == functionByte)
                {
                    return new LogitechReply { Error = true, ErrorCode = data[5] };
                }
                if (data[2] == feature && data[3] == functionByte)
                {
                    byte[] parameters = new byte[Math.Max(0, data.Length - 4)];
                    if (parameters.Length > 0) Array.Copy(data, 4, parameters, 0, parameters.Length);
                    return new LogitechReply { Success = true, Params = parameters };
                }
                return null;
            }

            public int FindFeature(int slot, int featureId)
            {
                byte[] parameters = { (byte)(featureId >> 8), (byte)(featureId & 0xFF) };
                LogitechReply reply = Request(slot, 0x00, 0x00, parameters, 200);
                if (!reply.Success || reply.Params.Length < 1) return 0;
                return reply.Params[0];
            }

            public bool TryReadBatteryRegister(int slot, out int level, out BatteryReadingSource source)
            {
                level = -1;
                source = BatteryReadingSource.Unknown;
                if (_short == null) return false;

                byte[] request = { 0x10, (byte)slot, 0x81, 0x0D, 0x00, 0x00, 0x00 };
                try
                {
                    _short.Write(request, 300);
                }
                catch (OperationCanceledException) { throw; }
                catch
                {
                    return false;
                }

                DateTime deadline = DateTime.UtcNow.AddMilliseconds(250);
                while (DateTime.UtcNow < deadline)
                {
                    byte[] shortReply = _short.Read(7, 60);
                    if (TryParseRegister(shortReply, slot, 0x0D, out level, out source)) return true;
                    byte[] longReply = _long.Read(20, 60);
                    if (TryParseRegister(longReply, slot, 0x0D, out level, out source)) return true;
                    Thread.Sleep(5);
                }

                byte[] second = { 0x10, (byte)slot, 0x81, 0x07, 0x00, 0x00, 0x00 };
                try
                {
                    _short.Write(second, 300);
                }
                catch (OperationCanceledException) { throw; }
                catch
                {
                    return false;
                }

                deadline = DateTime.UtcNow.AddMilliseconds(250);
                while (DateTime.UtcNow < deadline)
                {
                    byte[] shortReply = _short.Read(7, 60);
                    if (TryParseRegister(shortReply, slot, 0x07, out level, out source)) return true;
                    byte[] longReply = _long.Read(20, 60);
                    if (TryParseRegister(longReply, slot, 0x07, out level, out source)) return true;
                    Thread.Sleep(5);
                }
                return false;
            }

            internal static bool TryParseRegister(byte[] data, int slot, int register, out int level, out BatteryReadingSource source)
            {
                level = -1;
                source = BatteryReadingSource.Unknown;
                if (data == null || data.Length < 5) return false;
                if (data[1] != slot || data[2] != 0x81 || data[3] != register) return false;

                if (register == 0x0D)
                {
                    int value = data[4];
                    if (value > 0 && value <= 100)
                    {
                        level = value;
                        source = BatteryReadingSource.DeviceReported;
                        return true;
                    }
                    return false;
                }

                if (register == 0x07)
                {
                    int value = data[4];
                    if (value == 7) level = 90;
                    else if (value == 5) level = 50;
                    else if (value == 3) level = 20;
                    else if (value == 1) level = 5;
                    else return false;
                    source = BatteryReadingSource.CoarseLevel;
                    return true;
                }
                return false;
            }
        }
    }
}

