using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using WirelessBatteryMoniter.Models;

namespace WirelessBatteryMoniter.Services
{
    public sealed class BatteryMonitorService : IDisposable
    {
        private readonly BluetoothBatteryProvider _bluetoothProvider;
        private readonly HidBatteryProvider _hidProvider;
        private readonly DemoBatteryProvider _demoProvider;

        public BatteryMonitorService()
        {
            _bluetoothProvider = new BluetoothBatteryProvider();
            _hidProvider = new HidBatteryProvider();
            _demoProvider = new DemoBatteryProvider();
        }

        private IReadOnlyList<DeviceBatteryInfo> _bluetoothCache = new List<DeviceBatteryInfo>();
        private IReadOnlyList<DeviceBatteryInfo> _hidCache = new List<DeviceBatteryInfo>();

        public bool DemoMode { get; set; }

        public async Task<BatterySnapshot> ScanAsync(CancellationToken cancellationToken)
        {
            DateTime started = DateTime.Now;
            if (DemoMode)
            {
                IReadOnlyList<DeviceBatteryInfo> demoDevices = await _demoProvider
                    .GetDevicesAsync(cancellationToken)
                    .ConfigureAwait(false);
                return BuildSnapshot(demoDevices, started, null, true);
            }

            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                timeout.CancelAfter(TimeSpan.FromSeconds(35));
                var bluetoothTask = _bluetoothProvider.GetDevicesAsync(timeout.Token);
                var hidTask = _hidProvider.GetDevicesAsync(timeout.Token);
                BatterySnapshot snapshot = await CollectResultsAsync(bluetoothTask, hidTask,
                    _bluetoothCache, _hidCache, cancellationToken).ConfigureAwait(false);
                if (bluetoothTask.Status == TaskStatus.RanToCompletion) _bluetoothCache = bluetoothTask.Result;
                if (hidTask.Status == TaskStatus.RanToCompletion) _hidCache = hidTask.Result;
                string warning = bluetoothTask.Status == TaskStatus.RanToCompletion ? _bluetoothProvider.LastWarning : null;
                string error = string.Join("；", new[] { snapshot.Error, warning }.Where(e => !string.IsNullOrEmpty(e)));
                return BuildSnapshot(snapshot.Devices, started, string.IsNullOrEmpty(error) ? null : error, false);
            }
        }

        // Wait for cancellation cleanup before allowing another scan or disposing providers.
        // The caller token represents shutdown; provider cancellation represents scan timeout.
        internal static async Task<BatterySnapshot> CollectResultsAsync(
            Task<IReadOnlyList<DeviceBatteryInfo>> bluetoothTask,
            Task<IReadOnlyList<DeviceBatteryInfo>> hidTask,
            IReadOnlyList<DeviceBatteryInfo> bluetoothCache,
            IReadOnlyList<DeviceBatteryInfo> hidCache,
            CancellationToken shutdown)
        {
            try { await Task.WhenAll(bluetoothTask, hidTask).ConfigureAwait(false); }
            catch { /* Inspect each completed channel independently. */ }
            shutdown.ThrowIfCancellationRequested();
            var devices = new List<DeviceBatteryInfo>();
            var errors = new List<string>();
            AddChannelResult(bluetoothTask, bluetoothCache, "蓝牙", devices, errors);
            AddChannelResult(hidTask, hidCache, "2.4G", devices, errors);
            return new BatterySnapshot(MergeDuplicateDevices(devices), DateTime.Now,
                errors.Count == 0 ? null : string.Join("；", errors), false);
        }

        private static void AddChannelResult(Task<IReadOnlyList<DeviceBatteryInfo>> task,
            IReadOnlyList<DeviceBatteryInfo> cache, string channel,
            List<DeviceBatteryInfo> devices, List<string> errors)
        {
            if (task.Status == TaskStatus.RanToCompletion)
            {
                devices.AddRange(task.Result);
                return;
            }
            string error = task.IsCanceled ? "扫描超时" : ToFriendlyError(task.Exception.GetBaseException());
            errors.Add(channel + "：" + error + "（旧读数已标记缓存）");
            AppLog.Warn(channel + " scan incomplete: " + error);
            foreach (var old in cache)
                devices.Add(new DeviceBatteryInfo(old.Id, old.Name, old.Kind, old.Address,
                    old.Level, old.IsConnected, true, old.LastSeen));
        }

        internal static List<DeviceBatteryInfo> MergeDuplicateDevices(List<DeviceBatteryInfo> devices)
        {
            var byId = new Dictionary<string, DeviceBatteryInfo>(StringComparer.OrdinalIgnoreCase);
            foreach (DeviceBatteryInfo device in devices)
            {
                DeviceBatteryInfo existing;
                if (!byId.TryGetValue(device.Id, out existing)
                    || (!existing.IsConnected && device.IsConnected)
                    || (existing.IsConnected == device.IsConnected && existing.IsStale && !device.IsStale))
                    byId[device.Id] = device;
            }
            return byId.Values.ToList();
        }

        private static BatterySnapshot BuildSnapshot(
            IEnumerable<DeviceBatteryInfo> devices,
            DateTime started,
            string error,
            bool isDemo)
        {
            BatterySnapshot snapshot = new BatterySnapshot(devices, DateTime.Now, error, isDemo);
            AppLog.Info(string.Format(
                "Scan complete in {0} ms: {1} device(s), {2} connected, min={3}",
                (int)(DateTime.Now - started).TotalMilliseconds,
                snapshot.Devices.Count,
                snapshot.ConnectedCount,
                snapshot.MinimumLevel.HasValue ? snapshot.MinimumLevel.Value + "%" : "n/a"));
            return snapshot;
        }

        private static string ToFriendlyError(Exception exception)
        {
            if (exception is UnauthorizedAccessException)
            {
                return "没有权限访问无线设备。请确认蓝牙已开启，并尝试以当前用户重新运行。";
            }
            if (exception is COMException)
            {
                return "Windows 蓝牙服务暂时不可用。请检查蓝牙开关或稍后重试。";
            }
            if (exception is InvalidOperationException)
            {
                return exception.Message;
            }
            return "读取无线设备失败：" + exception.Message;
        }

        public void Dispose()
        {
            _bluetoothProvider.Dispose();
            _hidProvider.Dispose();
            _demoProvider.Dispose();
        }
    }
}
