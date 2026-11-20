using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using WirelessBatteryMoniter.Models;

namespace WirelessBatteryMoniter.Services
{
    /// <summary>
    /// Deterministic sample data used by --demo so the UI, tray icon and alerts can be
    /// evaluated on machines without paired Bluetooth peripherals.
    /// </summary>
    public sealed class DemoBatteryProvider : IBatteryProvider
    {
        public string Name
        {
            get { return "Demo"; }
        }

        public Task<IReadOnlyList<DeviceBatteryInfo>> GetDevicesAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            DateTime now = DateTime.Now;
            List<DeviceBatteryInfo> devices = new List<DeviceBatteryInfo>
            {
                new DeviceBatteryInfo("demo:keychron", "Keychron K3 Pro", "蓝牙 LE", "D4:11:22:33:44:02", 56, true, false, now),
                new DeviceBatteryInfo("demo:g304", "罗技 G304（2.4G）", "2.4G", "046D:C54D", 67, true, false, now),
                new DeviceBatteryInfo("demo:sony", "索尼 WH-1000XM5", "经典蓝牙", "D4:11:22:33:44:03", 18, true, false, now),
                new DeviceBatteryInfo("demo:xbox", "Xbox 无线控制器", "蓝牙 LE", "D4:11:22:33:44:04", 45, false, true, now.AddMinutes(-8))
            };
            return Task.FromResult<IReadOnlyList<DeviceBatteryInfo>>(devices.AsReadOnly());
        }

        public void Dispose()
        {
        }
    }
}
