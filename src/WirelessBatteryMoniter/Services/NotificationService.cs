using System;
using System.Collections.Generic;
using System.Linq;
using System.Media;
using System.Windows.Forms;
using WirelessBatteryMoniter.Models;

namespace WirelessBatteryMoniter.Services
{
    /// <summary>
    /// Emits one desktop notification per low-battery episode. A device is re-armed
    /// after its level recovers above threshold + 5 or after it disconnects.
    /// </summary>
    public sealed class NotificationService
    {
        private readonly NotifyIcon _notifyIcon;
        private readonly HashSet<string> _alertedDeviceIds =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public NotificationService(NotifyIcon notifyIcon, AppSettings settings)
        {
            _notifyIcon = notifyIcon;
            Settings = settings;
        }

        public AppSettings Settings { get; set; }

        /// <summary>
        /// Optional desktop reminder presenter. When set, it replaces the shell balloon
        /// (which Focus Assist or notification settings may suppress).
        /// </summary>
        public Action<IList<DeviceBatteryInfo>, int> ToastPresenter { get; set; }

        public void Process(BatterySnapshot snapshot)
        {
            if (snapshot == null) return;

            if (Settings == null || !Settings.NotificationsEnabled)
            {
                _alertedDeviceIds.Clear();
                return;
            }

            int threshold = Settings.LowBatteryThreshold;
            List<DeviceBatteryInfo> newlyLow = new List<DeviceBatteryInfo>();
            HashSet<string> connectedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (DeviceBatteryInfo device in snapshot.Devices)
            {
                if (!device.IsConnected) continue;
                connectedIds.Add(device.Id);
                if (!device.Level.HasValue || device.IsStale) continue;

                if (device.Level.Value <= threshold)
                {
                    if (!_alertedDeviceIds.Contains(device.Id))
                    {
                        _alertedDeviceIds.Add(device.Id);
                        newlyLow.Add(device);
                    }
                }
                else if (device.Level.Value > threshold + 5)
                {
                    _alertedDeviceIds.Remove(device.Id);
                }
            }

            // Re-arm devices that disappeared or disconnected, so reconnecting at a low
            // level produces a fresh reminder.
            _alertedDeviceIds.RemoveWhere(id => !connectedIds.Contains(id));

            if (newlyLow.Count == 0) return;
            ShowLowBatteryToast(newlyLow, threshold);
        }

        public void ShowTest()
        {
            int threshold = Settings != null ? Settings.LowBatteryThreshold : 20;
            List<DeviceBatteryInfo> sample = new List<DeviceBatteryInfo>
            {
                new DeviceBatteryInfo("test", "测试设备", "演示", string.Empty, Math.Max(1, threshold - 2), true, false, DateTime.Now)
            };
            ShowLowBatteryToast(sample, threshold);
        }

        private void ShowLowBatteryToast(IList<DeviceBatteryInfo> devices, int threshold)
        {
            if (devices == null || devices.Count == 0) return;

            string title = devices.Count == 1
                ? devices[0].Name + " 电量偏低"
                : "无线设备电量偏低";

            IEnumerable<string> parts = devices
                .Take(3)
                .Select(d => string.Format("{0} {1}%", d.Name, d.Level.HasValue ? d.Level.Value : 0));
            string summary = string.Join(" · ", parts);
            if (devices.Count > 3)
            {
                summary += string.Format(" 等 {0} 台设备", devices.Count);
            }

            string body = summary + Environment.NewLine
                + string.Format("电量已低于 {0}%，请及时充电。", threshold);

            try
            {
                if (ToastPresenter != null)
                {
                    ToastPresenter(devices, threshold);
                }
                else
                {
                    _notifyIcon.ShowBalloonTip(8000, title, body, ToolTipIcon.Warning);
                }

                if (Settings == null || Settings.NotificationSound)
                {
                    SystemSounds.Exclamation.Play();
                }
                AppLog.Info("Low battery notification: " + summary);
            }
            catch (Exception ex)
            {
                AppLog.Error("Failed to show low battery notification.", ex);
            }
        }

        public void Reset()
        {
            _alertedDeviceIds.Clear();
        }
    }
}
