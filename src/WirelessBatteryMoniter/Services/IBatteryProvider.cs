using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using WirelessBatteryMoniter.Models;

namespace WirelessBatteryMoniter.Services
{
    public interface IBatteryProvider : IDisposable
    {
        string Name { get; }

        Task<IReadOnlyList<DeviceBatteryInfo>> GetDevicesAsync(CancellationToken cancellationToken);
    }
}
