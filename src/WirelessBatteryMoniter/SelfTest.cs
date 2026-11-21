using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using WirelessBatteryMoniter.Models;
using WirelessBatteryMoniter.Services;
using WirelessBatteryMoniter.Services.Hid;
using WirelessBatteryMoniter.UI;

namespace WirelessBatteryMoniter
{
    internal static class SelfTest
    {
        private static DeviceBatteryInfo Reading(string id, int level, bool connected = true, bool stale = false)
        {
            return new DeviceBatteryInfo(id, "Same model", "2.4G", "046D:C54D", level,
                connected, stale, new DateTime(2026, 1, 1));
        }

        private static void RunRegressionChecks(Action<string, bool> check)
        {
            var canceled = new TaskCompletionSource<IReadOnlyList<DeviceBatteryInfo>>();
            canceled.SetCanceled();
            var freshTask = Task.FromResult<IReadOnlyList<DeviceBatteryInfo>>(new[] { Reading("fresh", 10) });
            var oldReadings = new[] { Reading("old", 60) };
            var emptyReadings = new DeviceBatteryInfo[0];
            var partial = BatteryMonitorService.CollectResultsAsync(canceled.Task, freshTask,
                oldReadings, emptyReadings, CancellationToken.None).GetAwaiter().GetResult();
            check("timeout preserves successful HID result", partial.Devices.Any(d => d.Id == "fresh" && !d.IsStale && d.Level == 10));
            check("timeout preserves cache timestamp", partial.Devices.Single(d => d.Id == "old").IsStale && partial.Devices.Single(d => d.Id == "old").LastSeen == oldReadings[0].LastSeen);
            check("partial timeout is visible", partial.Error.Contains("蓝牙") && partial.Error.Contains("超时"));
            var reverse = BatteryMonitorService.CollectResultsAsync(freshTask, canceled.Task,
                emptyReadings, oldReadings, CancellationToken.None).GetAwaiter().GetResult();
            check("timeout preserves successful Bluetooth result", reverse.Devices.Any(d => d.Id == "fresh" && !d.IsStale));
            var allTimeout = BatteryMonitorService.CollectResultsAsync(canceled.Task, canceled.Task,
                oldReadings, emptyReadings, CancellationToken.None).GetAwaiter().GetResult();
            check("all timeout preserves only stale data", allTimeout.Devices.Count == 1 && allTimeout.Devices.All(d => d.IsStale) && allTimeout.Error.Contains("2.4G"));
            var recovered = BatteryMonitorService.CollectResultsAsync(freshTask,
                Task.FromResult<IReadOnlyList<DeviceBatteryInfo>>(emptyReadings), oldReadings, oldReadings,
                CancellationToken.None).GetAwaiter().GetResult();
            check("successful scan clears error and obsolete cache", recovered.Error == null && recovered.Devices.Count == 1 && !recovered.Devices[0].IsStale);
            bool shutdownCanceled = false;
            try { BatteryMonitorService.CollectResultsAsync(freshTask, canceled.Task, oldReadings,
                emptyReadings, new CancellationToken(true)).GetAwaiter().GetResult(); }
            catch (OperationCanceledException) { shutdownCanceled = true; }
            check("shutdown still cancels publication", shutdownCanceled);
            var failed = new TaskCompletionSource<IReadOnlyList<DeviceBatteryInfo>>();
            failed.SetException(new InvalidOperationException("test failure"));
            var failure = BatteryMonitorService.CollectResultsAsync(failed.Task, freshTask,
                oldReadings, emptyReadings, CancellationToken.None).GetAwaiter().GetResult();
            check("channel exception preserves healthy results", failure.Devices.Count == 2 && failure.Error.Contains("test failure"));
            int partialAlerts = 0;
            var partialNotifier = new NotificationService(null, new AppSettings { LowBatteryThreshold = 20, NotificationsEnabled = true, NotificationSound = false });
            partialNotifier.ToastPresenter = (devices, threshold) => partialAlerts++;
            partialNotifier.Process(partial);
            partialNotifier.Process(allTimeout);
            check("partial result alerts only fresh low batteries", partialAlerts == 1);

            using (var bluetooth = new BluetoothBatteryProvider())
            {
                DateTime originalTime = new DateTime(2026, 1, 1);
                DateTime nextTime = originalTime.AddMinutes(1);
                var original = bluetooth.ResolveReading("bt:one", "耳机", "经典蓝牙", "", 60, true, originalTime);
                var cachedBluetooth = bluetooth.ResolveReading("BT:ONE", "耳机", "经典蓝牙", "", null, true, nextTime);
                check("Bluetooth missing property preserves cached battery", cachedBluetooth.Level == 60 && cachedBluetooth.IsStale);
                check("Bluetooth fallback preserves original timestamp", cachedBluetooth.LastSeen == originalTime);
                check("Bluetooth cached connection is explicitly uncertain", cachedBluetooth.StateText.Contains("缓存"));
                var offlineBluetooth = bluetooth.ResolveReading("bt:one", "耳机", "经典蓝牙", "", 60, false, nextTime);
                check("Bluetooth disconnected property does not refresh timestamp", !offlineBluetooth.IsConnected && offlineBluetooth.IsStale && offlineBluetooth.LastSeen == originalTime);
                var restoredBluetooth = bluetooth.ResolveReading("bt:one", "耳机", "经典蓝牙", "", 59, true, nextTime);
                check("Bluetooth recovery replaces cached value and time", restoredBluetooth.Level == 59 && !restoredBluetooth.IsStale && restoredBluetooth.LastSeen == nextTime);
                check("Bluetooth unknown missing battery is not invented", bluetooth.ResolveReading("bt:unknown", "unknown", "蓝牙 LE", "", null, true, nextTime) == null);
                bluetooth.ResolveReading("bt:two", "鼠标", "蓝牙 LE", "", 0, true, originalTime);
                var secondBluetooth = bluetooth.ResolveReading("bt:two", "鼠标", "蓝牙 LE", "", null, true, nextTime);
                check("Bluetooth cache isolates devices and preserves zero", secondBluetooth.Level == 0 && secondBluetooth.LastSeen == originalTime);
                bluetooth.Dispose();
                check("Bluetooth dispose clears cache", bluetooth.ResolveReading("bt:one", "耳机", "经典蓝牙", "", null, true, nextTime) == null);
            }

            var xinmeng = new HidDeviceInfo { VendorId = 0x3151, ProductId = 0x5045,
                UsagePage = 0xFFFF, Usage = 2, FeatureReportLength = 65, OutputReportLength = 0 };
            check("XINMENG feature-only interface is discoverable", HidBatteryProvider.IsCandidate(xinmeng));
            xinmeng.Usage = 1;
            check("XINMENG input interface excluded", !HidBatteryProvider.IsCandidate(xinmeng));
            xinmeng.Usage = 2; xinmeng.ProductId = 0xFFFF;
            check("unverified feature receiver excluded", !HidBatteryProvider.IsCandidate(xinmeng));
            var status = new byte[65]; status[3] = 98; status[4] = 1; status[6] = 1; status[7] = 2; status[10] = 1;
            int xinmengLevel;
            check("XINMENG captured online response is 98 percent", HidBatteryProvider.TryParseXinmengBattery(status, out xinmengLevel) && xinmengLevel == 98);
            status[3] = 0;
            check("XINMENG zero battery accepted", HidBatteryProvider.TryParseXinmengBattery(status, out xinmengLevel) && xinmengLevel == 0);
            status[3] = 100; status[7] = 3;
            check("XINMENG combined receiver and full battery accepted", HidBatteryProvider.TryParseXinmengBattery(status, out xinmengLevel) && xinmengLevel == 100);
            status[3] = 101;
            check("XINMENG invalid percentage rejected", !HidBatteryProvider.TryParseXinmengBattery(status, out xinmengLevel));
            status[3] = 98; status[5] = 1;
            check("XINMENG sleeping mouse not reported connected", !HidBatteryProvider.TryParseXinmengBattery(status, out xinmengLevel));
            status[5] = 0; status[9] = 1;
            check("XINMENG boot mode rejected", !HidBatteryProvider.TryParseXinmengBattery(status, out xinmengLevel));
            status[9] = 0; status[7] = 1;
            check("XINMENG keyboard-only response rejected", !HidBatteryProvider.TryParseXinmengBattery(status, out xinmengLevel));
            status[7] = 2; status[0] = 1;
            check("XINMENG unexpected report ID rejected", !HidBatteryProvider.TryParseXinmengBattery(status, out xinmengLevel));
            check("XINMENG empty and short responses rejected", !HidBatteryProvider.TryParseXinmengBattery(null, out xinmengLevel) && !HidBatteryProvider.TryParseXinmengBattery(new byte[9], out xinmengLevel) && !HidBatteryProvider.TryParseXinmengBattery(new byte[65], out xinmengLevel));

            var mouse = new DeviceBatteryInfo("mouse", "PRO X2 SUPERSTRIKE", "2.4G", "", 70, true, false, DateTime.Now);
            string tooltip = TrayTextFormatter.Build(new BatterySnapshot(new[] { mouse }, DateTime.Now, null, false), true);
            check("tray tooltip exact requested format", tooltip == "WirelessBatteryMoniter · 1 台\n[2.4G] 鼠标: PRO X2 SUPERSTRIKE 70%电量");
            check("tray tooltip omits minimum", !tooltip.Contains("最低"));
            var many = Enumerable.Range(0, 12).Select(i => Reading("device-" + i, 70)).ToList();
            tooltip = TrayTextFormatter.Build(new BatterySnapshot(many, DateTime.Now, null, false), true);
            check("tray tooltip native length bound", tooltip.Length <= 127);
            check("tray tooltip overflow is explicit", tooltip.Contains("点击查看") && tooltip.Contains("70%电量"));
            tooltip = TrayTextFormatter.Build(new BatterySnapshot(new[] { mouse, Reading("offline", 30, false, true) }, DateTime.Now, null, false), false);
            check("tray tooltip respects disconnected preference", !tooltip.Contains("未连接") && tooltip.Split('\n').Length == 2);
            check("tray device type unknown is not guessed", TrayTextFormatter.DeviceType("Logitech mystery") == "设备");
            var fourNames = new[] { "VXE NordicMouse 1K Dongle", "PRO X 2", "PRO X2 SUPERSTRIKE", "XINMENG 2.4G 鼠标" };
            var four = fourNames.Select((name, i) => new DeviceBatteryInfo("tip-" + i, name, "2.4G", "", 70, true, false, DateTime.Now)).ToList();
            string fullTip = TrayTextFormatter.BuildFull(new BatterySnapshot(four, DateTime.Now, null, false), true);
            check("full tray text preserves all four complete names", fourNames.All(name => fullTip.Contains("鼠标: " + name + " 70%电量")) && fullTip.Split('\n').Length == 5);
            check("full tray text exceeds shell limit without ellipsis", fullTip.Length > 127 && !fullTip.Contains("…"));
            check("explicit headset beats ambiguous PRO X name", TrayTextFormatter.DeviceType("PRO X 2 headset") == "耳机");
            check("full tray text respects disconnected preference", !TrayTextFormatter.BuildFull(new BatterySnapshot(new[] { mouse, Reading("offline", 30, false, true) }, DateTime.Now, null, false), false).Contains("offline"));
            var first = new HidDeviceInfo { Path = @"\\?\hid#vid_046d&pid_c54d&mi_02&col01#7&aaaa&0&0000#{guid}" };
            var secondCollection = new HidDeviceInfo { Path = @"\\?\hid#vid_046d&pid_c54d&mi_02&col02#7&aaaa&0&0001#{guid}" };
            var otherReceiver = new HidDeviceInfo { Path = @"\\?\hid#vid_046d&pid_c54d&mi_02&col01#7&bbbb&0&0000#{guid}" };
            var otherVendor = new HidDeviceInfo { Path = @"\\?\hid#vid_373b&pid_1220#7&cccc&0&0000#{guid}" };
            check("HID collections share receiver key", first.PhysicalKey == secondCollection.PhysicalKey);
            check("HID identical models retain receiver identity", first.PhysicalKey != otherReceiver.PhysicalKey);
            check("HID vendors remain separate", first.PhysicalKey != otherVendor.PhysicalKey);
            check("HID null path is safe", new HidDeviceInfo().PhysicalKey == "");

            using (var hid = new HidBatteryProvider())
            {
                var compx = HidBatteryProvider.MakeDevice(otherVendor, "VXE NordicMouse 1K Dongle", 15, "compx");
                var narrow = HidBatteryProvider.MakeDevice(otherVendor, compx.Name, 14, "atk17");
                var wide = HidBatteryProvider.MakeDevice(otherVendor, compx.Name, 13, "atk64");
                check("alternate mouse protocols share identity", compx.Id == narrow.Id && narrow.Id == wide.Id);
                hid.ApplyCache(new[] { compx });
                hid.ApplyCache(new DeviceBatteryInfo[0]);
                var wake = hid.ApplyCache(new[] { narrow });
                check("protocol switch during debounce replaces cached row", wake.Count == 1 && wake[0].Level == 14 && !wake[0].IsStale);
                for (int miss = 0; miss < HidBatteryProvider.DisconnectMissThreshold; miss++) hid.ApplyCache(new DeviceBatteryInfo[0]);
                wake = hid.ApplyCache(new[] { wide });
                check("protocol switch after sleep removes disconnected row", wake.Count == 1 && wake[0].IsConnected && !wake[0].IsStale && wake[0].Level == 13);
                wake = hid.ApplyCache(new[] { compx });
                check("reverse protocol switch retains one row", wake.Count == 1 && wake[0].Level == 15);
                var identicalReceiver = new HidDeviceInfo { Path = otherVendor.Path.Replace("cccc", "dddd") };
                var otherMouse = HidBatteryProvider.MakeDevice(identicalReceiver, compx.Name, 80, "compx");
                wake = hid.ApplyCache(new[] { narrow, otherMouse });
                check("same model on separate receivers is not merged", wake.Count == 2 && narrow.Id != otherMouse.Id);
                check("Logitech paired slots remain separate", HidBatteryProvider.MakeDevice(first, "mouse", 50, "logitech-1").Id != HidBatteryProvider.MakeDevice(first, "mouse", 50, "logitech-2").Id);
            }
            var merged = BatteryMonitorService.MergeDuplicateDevices(new List<DeviceBatteryInfo>
            { Reading("one", 50), Reading("two", 50), Reading("one", 50, false, true) });
            check("same-name devices are preserved", merged.Count == 2);
            check("connected reading beats disconnected duplicate", merged.Single(d => d.Id == "one").IsConnected);
            merged = BatteryMonitorService.MergeDuplicateDevices(new List<DeviceBatteryInfo>
            { Reading("one", 20, true, true), Reading("ONE", 30) });
            check("fresh duplicate replaces stale reading", merged.Count == 1 && merged[0].Level == 30);

            using (var hid = new HidBatteryProvider())
            {
                hid.ApplyCache(new[] { Reading("one", 50), Reading("two", 40) });
                var disconnected = hid.ApplyCache(new[] { Reading("two", 39) });
                var cached = disconnected.Single(d => d.Id == "one");
                check("HID first miss retains pending connection", cached.Level == 50 && cached.IsConnected && cached.IsStale);
                check("HID cache preserves reading timestamp", cached.LastSeen == new DateTime(2026, 1, 1));
                var reconnected = hid.ApplyCache(new[] { Reading("one", 49), Reading("two", 38) });
                check("HID reconnect replaces stale state", reconnected.Count == 2 && reconnected.All(d => d.IsConnected && !d.IsStale));
            }

            using (var hid = new HidBatteryProvider())
            {
                int debounceAlerts = 0;
                var debounceNotifier = new NotificationService(null, new AppSettings
                    { LowBatteryThreshold = 20, NotificationsEnabled = true, NotificationSound = false });
                debounceNotifier.ToastPresenter = (devices, threshold) => debounceAlerts++;
                Func<DeviceBatteryInfo[], IReadOnlyList<DeviceBatteryInfo>> scan = readings =>
                {
                    var results = hid.ApplyCache(readings);
                    debounceNotifier.Process(new BatterySnapshot(results, DateTime.Now, null, false));
                    return results;
                };
                scan(new[] { Reading("one", 10), Reading("two", 50) });
                scan(new[] { Reading("two", 49) });
                var secondMiss = scan(new[] { Reading("two", 48) });
                check("HID second miss remains pending", secondMiss.Single(d => d.Id == "one").IsConnected && secondMiss.Single(d => d.Id == "one").IsStale);
                scan(new[] { Reading("ONE", 9), Reading("two", 47) });
                check("brief failure recovery does not repeat alert", debounceAlerts == 1);
                var resetMiss = scan(new[] { Reading("two", 46) });
                check("successful read resets miss count case insensitively", resetMiss.Single(d => d.Id == "ONE").IsConnected);
                scan(new[] { Reading("two", 45) });
                var thirdMiss = scan(new[] { Reading("two", 44) });
                var offline = thirdMiss.Single(d => d.Id == "ONE");
                check("third consecutive miss confirms disconnect", !offline.IsConnected && offline.IsStale && offline.Level == 9);
                check("debounce preserves last successful timestamp", offline.LastSeen == new DateTime(2026, 1, 1));
                check("miss counts are isolated by device", thirdMiss.Single(d => d.Id == "two").IsConnected && !thirdMiss.Single(d => d.Id == "two").IsStale);
                var stillOffline = scan(new[] { Reading("two", 43) });
                check("continued misses remain offline", !stillOffline.Single(d => d.Id == "ONE").IsConnected);
                scan(new[] { Reading("one", 8), Reading("two", 42) });
                check("confirmed disconnect recovery rearms alert", debounceAlerts == 2);
            }

            var settings = new AppSettings { LowBatteryThreshold = 20, NotificationsEnabled = true, NotificationSound = false };
            var notifier = new NotificationService(null, settings);
            int alerts = 0;
            notifier.ToastPresenter = (devices, threshold) => alerts++;
            Action<int, bool, bool> feed = (level, connected, stale) =>
                notifier.Process(new BatterySnapshot(new[] { Reading("one", level, connected, stale) }, DateTime.Now, null, false));
            feed(20, true, false);
            feed(20, true, false);
            check("low battery alerts once", alerts == 1);
            feed(21, true, false);
            feed(20, true, false);
            feed(25, true, false);
            feed(20, true, false);
            check("threshold hysteresis survives 20-21-20 and 20-25-20", alerts == 1);
            feed(26, true, false);
            feed(20, true, false);
            check("recovery above threshold plus five rearms", alerts == 2);
            feed(20, false, true);
            feed(20, true, false);
            check("disconnect and reconnect rearms", alerts == 3);
            notifier.Reset();
            feed(10, true, true);
            check("stale readings do not trigger alerts", alerts == 3);
            feed(10, true, false);
            check("fresh reading after stale triggers alert", alerts == 4);
            settings.NotificationsEnabled = false;
            feed(10, true, false);
            settings.NotificationsEnabled = true;
            feed(10, true, false);
            check("notification toggle rearms", alerts == 5);
            notifier.Process(new BatterySnapshot(new DeviceBatteryInfo[0], DateTime.Now, null, false));
            feed(10, true, false);
            check("disappeared device rearms", alerts == 6);
        }

        public static int Run()
        {
            AppLog.UseDiagnosticDirectory(Path.Combine(Path.GetTempPath(), "WirelessBatteryMoniter-tests"));
            List<string> lines = new List<string>();
            int failures = 0;

            Action<string, bool> check = (name, ok) =>
            {
                if (ok)
                {
                    lines.Add("PASS  " + name);
                }
                else
                {
                    failures++;
                    lines.Add("FAIL  " + name);
                }
            };

            try
            {
                AppSettings settings = new AppSettings
                {
                    PollSeconds = 1,
                    LowBatteryThreshold = 100
                };
                settings.Normalize();
                check("settings clamp poll", settings.PollSeconds == 5);
                check("settings clamp threshold", settings.LowBatteryThreshold == 95);

                check("level normalize low", DeviceBatteryInfo.NormalizeLevel(-1) == null);
                check("level normalize high", DeviceBatteryInfo.NormalizeLevel(101) == null);
                check("level normalize valid", DeviceBatteryInfo.NormalizeLevel(72) == 72);

                DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(AppSettings));
                using (MemoryStream stream = new MemoryStream())
                {
                    serializer.WriteObject(stream, settings);
                    stream.Position = 0;
                    AppSettings roundTrip = (AppSettings)serializer.ReadObject(stream);
                    check("settings json roundtrip", roundTrip != null && roundTrip.PollSeconds == 5);
                }

                string settingsFolder = Path.Combine(Path.GetTempPath(), "WirelessBatteryMoniter-selftest-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(settingsFolder);
                try
                {
                    SettingsService service = new SettingsService(settingsFolder);
                    AppSettings toSave = new AppSettings
                    {
                        LowBatteryThreshold = 35,
                        PollSeconds = 20,
                        NotificationsEnabled = false,
                        StartMinimized = false
                    };
                    string saveError;
                    check("settings save", service.TrySave(toSave, out saveError));
                    AppSettings loaded = service.Load();
                    check("settings load", loaded.LowBatteryThreshold == 35
                        && loaded.PollSeconds == 20
                        && !loaded.NotificationsEnabled
                        && !loaded.StartMinimized);
                }
                finally
                {
                    try { Directory.Delete(settingsFolder, true); }
                    catch { }
                }

                Func<string, AppSettings> parseSettings = json =>
                {
                    using (MemoryStream jsonStream = new MemoryStream(Encoding.UTF8.GetBytes(json)))
                    {
                        DataContractJsonSerializer jsonSerializer = new DataContractJsonSerializer(typeof(AppSettings));
                        return (AppSettings)jsonSerializer.ReadObject(jsonStream);
                    }
                };
                AppSettings sparseSettings = parseSettings("{}");
                check("settings missing members use documented defaults",
                    sparseSettings.LowBatteryThreshold == 20
                    && sparseSettings.PollSeconds == 10
                    && sparseSettings.NotificationsEnabled
                    && sparseSettings.NotificationSound
                    && sparseSettings.ShowDisconnected
                    && sparseSettings.StartMinimized);
                check("existing settings file is not a first run", !sparseSettings.FirstRun);
                AppSettings overriddenSettings = parseSettings("{\"LowBatteryThreshold\":35,\"PollSeconds\":20}");
                check("settings present members override defaults",
                    overriddenSettings.LowBatteryThreshold == 35
                    && overriddenSettings.PollSeconds == 20
                    && overriddenSettings.NotificationsEnabled
                    && !overriddenSettings.FirstRun);

                BatteryMonitorService monitor = new BatteryMonitorService { DemoMode = true };
                BatterySnapshot snapshot = monitor.ScanAsync(CancellationToken.None).GetAwaiter().GetResult();
                monitor.Dispose();
                check("demo device count", snapshot.Devices.Count == 4);
                check("demo connected count", snapshot.ConnectedCount == 3);
                check("demo has 2.4G device", snapshot.Devices.Any(d => d.Kind == "2.4G"));
                check("demo minimum level", snapshot.MinimumLevel == 18);
                check("demo low count", snapshot.LowBatteryCount(20) == 1);
                check("demo sorting lowest first", snapshot.Devices[0].Level == 18);

                check("snapshot low threshold zero", snapshot.LowBatteryCount(10) == 0);
                var pending = new DeviceBatteryInfo("pending", "缓存鼠标", "2.4G", "", 5, true, true, DateTime.Now);
                var fresh = new DeviceBatteryInfo("fresh", "正常鼠标", "2.4G", "", 70, true, false, DateTime.Now);
                var mixed = new BatterySnapshot(new[] { pending, fresh }, DateTime.Now, null, false);
                check("cached device excluded from confirmed count", mixed.ConnectedCount == 1 && mixed.PendingCount == 1);
                check("cached low reading excluded from current statistics", mixed.MinimumLevel == 70 && mixed.LowBatteryCount(20) == 0 && mixed.KnownBatteryCount == 1);
                check("tray includes pending and preserved reading", TrayTextFormatter.BuildFull(mixed, false).Contains("1 台待确认") && TrayTextFormatter.BuildFull(mixed, false).Contains("5%电量（待确认，缓存）"));
                var onlyCached = new BatterySnapshot(new[] { pending }, DateTime.Now, null, false);
                check("all cached has no current battery", onlyCached.ConnectedCount == 0 && onlyCached.PendingCount == 1 && !onlyCached.MinimumLevel.HasValue);
                check("cached tooltip fits native buffer", TrayTextFormatter.Build(onlyCached, false).Length <= TrayTextFormatter.MaximumLength);
                var failed = mixed.WithFailure("测试刷新异常");
                check("unexpected failure retains readings and timestamps", failed.Devices.Count == 2 && failed.Devices.All(d => d.IsStale) && failed.Devices.Single(d => d.Id == fresh.Id).LastSeen == fresh.LastSeen && failed.Devices.Single(d => d.Id == fresh.Id).Level == 70);
                check("unexpected failure invalidates current statistics", failed.ConnectedCount == 0 && failed.PendingCount == 2 && !failed.MinimumLevel.HasValue);
                check("unexpected failure visible in tray", TrayTextFormatter.BuildFull(failed, true).Contains("读取异常") && failed.Error == "测试刷新异常");
                check("first scan failure remains visible", TrayTextFormatter.BuildFull(BatterySnapshot.Empty(false).WithFailure("首次失败"), false).Contains("读取异常"));
                using (Bitmap normal = TrayIconRenderer.CreateBitmap(1, 30, 20))
                using (Bitmap low = TrayIconRenderer.CreateBitmap(1, 30, 40))
                using (Bitmap restored = TrayIconRenderer.CreateBitmap(1, 30, 20))
                    check("threshold changes recolor same reading", normal.GetPixel(5,17) != low.GetPixel(5,17) && normal.GetPixel(5,17) == restored.GetPixel(5,17));



                using (Bitmap bitmap = TrayIconRenderer.CreateBitmap(3, 18, 20))
                {
                    check("tray bitmap size", bitmap.Width == 32 && bitmap.Height == 32);
                    check("tray bitmap has pixels", bitmap.GetPixel(5, 17).A > 0);
                }

                using (Icon icon = TrayIconRenderer.Create(3, 18, 20))
                {
                    check("tray icon created", icon != null && icon.Width > 0);
                }

                check("theme battery color", Theme.BatteryColor(5, 20) == Theme.Red);
                check("log directory", !string.IsNullOrEmpty(AppLog.DirectoryPath));
                RunRegressionChecks(check);

            }
            catch (Exception ex)
            {
                failures++;
                lines.Add("FAIL  exception: " + ex);
            }

            lines.Add(string.Empty);
            lines.Add(failures == 0 ? "RESULT: ALL TESTS PASSED" : "RESULT: " + failures + " FAILURE(S)");

            string path = Path.Combine(Path.GetTempPath(), "WirelessBatteryMoniter-selftest.txt");
            try
            {
                File.WriteAllLines(path, lines, Encoding.UTF8);
            }
            catch
            {
            }

            return failures == 0 ? 0 : 1;
        }
    }
}


