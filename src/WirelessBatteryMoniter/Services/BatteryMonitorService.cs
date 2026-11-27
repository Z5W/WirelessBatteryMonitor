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
        private readonly HidReceiverScheduler _hidScheduler;
        private readonly DemoBatteryProvider _demoProvider;
        private readonly BatteryChannel _bluetoothChannel, _hidChannel;
        public int PollSeconds { get; set; }

        public BatteryMonitorService()
        {
            PollSeconds=10;
            _bluetoothProvider = new BluetoothBatteryProvider();
            _hidScheduler = new HidReceiverScheduler();
            _demoProvider = new DemoBatteryProvider();
            _bluetoothChannel = new BatteryChannel(_bluetoothProvider.GetDevicesAsync, ()=>_bluetoothProvider.LastWarning, _bluetoothProvider.Dispose);
        }

        public bool DemoMode { get; set; }

        internal BatteryMonitorService(BatteryChannel bluetoothChannel, BatteryChannel hidChannel)
        {
            PollSeconds=10;
            _bluetoothChannel=bluetoothChannel;
            _hidChannel=hidChannel;
        }

        public async Task<BatterySnapshot> ScanAsync(CancellationToken cancellationToken, bool force = false, bool waitForCompletion = false)
        {
            DateTime started = DateTime.Now;
            if (DemoMode)
            {
                IReadOnlyList<DeviceBatteryInfo> demoDevices = await _demoProvider
                    .GetDevicesAsync(cancellationToken)
                    .ConfigureAwait(false);
                return BuildSnapshot(demoDevices, started, null, true);
            }

            int interval=Math.Max(5,PollSeconds);
            var channels=await Task.WhenAll(_bluetoothChannel.PollAsync(interval,force,cancellationToken,waitForCompletion:waitForCompletion),
                _hidScheduler!=null ? _hidScheduler.PollAsync(interval,force,cancellationToken,waitForCompletion) :
                    _hidChannel.PollAsync(interval,force,cancellationToken,waitForCompletion:waitForCompletion)).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            string error=string.Join("；",channels.Select((s,i)=>string.IsNullOrEmpty(s.Error) ? null : (i==0 ? "蓝牙：" : "2.4G：")+s.Error).Where(e=>e!=null));
            return new BatterySnapshot(MergeDuplicateDevices(channels.SelectMany(s=>s.Devices).ToList()),
                channels.Max(s=>s.Timestamp),string.IsNullOrEmpty(error) ? null : error,false,channels.Any(s=>s.IsScanning));
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
                devices.Add(old.AsUnconfirmedCache());
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
            _bluetoothChannel.Dispose();
            if(_hidScheduler!=null)_hidScheduler.Dispose();
            if(_hidChannel!=null)_hidChannel.Dispose();
            if (_demoProvider!=null) _demoProvider.Dispose();
        }
    }
}
