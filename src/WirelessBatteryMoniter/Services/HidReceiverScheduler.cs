using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WirelessBatteryMoniter.Models;
using WirelessBatteryMoniter.Services.Hid;

namespace WirelessBatteryMoniter.Services
{
    // Each physical receiver owns its protocol state, buffers and single-flight channel.
    internal sealed class HidReceiverScheduler : IDisposable
    {
        private sealed class Receiver
        {
            internal List<HidDeviceInfo> Interfaces;
            internal HidBatteryProvider Provider;
            internal BatteryChannel Channel;
        }
        private readonly Dictionary<string,Receiver> _receivers=new Dictionary<string,Receiver>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string,DeviceBatteryInfo> _cache=new Dictionary<string,DeviceBatteryInfo>(StringComparer.OrdinalIgnoreCase);
        private readonly Func<List<HidDeviceInfo>> _enumerate;
        private readonly Func<List<HidDeviceInfo>,List<DeviceBatteryInfo>> _read;
        private readonly Func<DateTime> _now;
        private readonly int _timeoutMs;
        private readonly SemaphoreSlim _gate=new SemaphoreSlim(1,1);
        private List<IGrouping<string,HidDeviceInfo>> _groups=new List<IGrouping<string,HidDeviceInfo>>();
        private Task<List<HidDeviceInfo>> _discovery;
        private DateTime _discoveryStarted,_nextDiscovery,_completed;
        private string _discoveryError;
        private bool _disposed;
        internal HidReceiverScheduler(Func<List<HidDeviceInfo>> enumerate=null,Func<List<HidDeviceInfo>,List<DeviceBatteryInfo>> read=null,Func<DateTime> now=null,int timeoutMs=35000)
        { _enumerate=enumerate ?? HidEnumerator.Enumerate;_read=read;_now=now ?? (()=>DateTime.UtcNow);_timeoutMs=timeoutMs; }

        internal async Task<BatterySnapshot> PollAsync(int interval,bool force,CancellationToken cancellation,bool waitForCompletion=false,int waitMs=1500)
        {
            await _gate.WaitAsync(cancellation).ConfigureAwait(false);
            try
            {
                if(_disposed)throw new ObjectDisposedException("HidReceiverScheduler");
                if(_discovery==null && (force || _now()>=_nextDiscovery))
                { _discoveryStarted=_now();_discovery=Task.Run(_enumerate); }
                if(_discovery!=null)
                {
                    await Task.WhenAny(_discovery,Task.Delay(waitForCompletion ? _timeoutMs : waitMs,cancellation)).ConfigureAwait(false);
                    cancellation.ThrowIfCancellationRequested();
                    if(_discovery.IsCompleted)
                    {
                        if(_discovery.Status==TaskStatus.RanToCompletion)
                        {
                            _groups=_discovery.Result.Where(HidBatteryProvider.IsCandidate).GroupBy(i=>i.PhysicalKey,StringComparer.OrdinalIgnoreCase).ToList();
                            _discoveryError=null;
                        }
                        else { _discoveryError="HID 枚举失败："+_discovery.Exception.GetBaseException().Message; }
                        _completed=_now().ToLocalTime();_nextDiscovery=_now().AddSeconds(interval);_discovery=null;
                    }
                    else if(_now()-_discoveryStarted>=TimeSpan.FromMilliseconds(_timeoutMs))_discoveryError="HID 枚举超时，继续查询已知接收器";
                }
                var tasks=new List<Task<BatterySnapshot>>();
                foreach(var group in _groups)
                {
                    Receiver receiver;
                    if(!_receivers.TryGetValue(group.Key,out receiver))
                    {
                        receiver=new Receiver();
                        var owner=receiver;
                        owner.Provider=new HidBatteryProvider(()=>owner.Interfaces,_read,_now);
                        owner.Channel=new BatteryChannel(owner.Provider.GetDevicesAsync,()=>owner.Provider.LastWarning,owner.Provider.Dispose,_now,_timeoutMs,backoffOnWarning:false);
                        _receivers.Add(group.Key,owner);
                    }
                    receiver.Interfaces=group.ToList();receiver.Provider.PollSeconds=interval;
                    if(force)receiver.Provider.RequestRefresh();
                    tasks.Add(receiver.Channel.PollAsync(interval,force,cancellation,waitMs,waitForCompletion));
                }
                var snapshots=await Task.WhenAll(tasks).ConfigureAwait(false);
                cancellation.ThrowIfCancellationRequested();
                var readings=snapshots.SelectMany(s=>s.Devices).ToList();
                var ids=new HashSet<string>(readings.Select(d=>d.Id),StringComparer.OrdinalIgnoreCase);
                foreach(var d in readings)_cache[d.Id]=d;
                readings.AddRange(_cache.Values.Where(d=>!ids.Contains(d.Id)).Select(d=>d.AsUnconfirmedCache()));
                var errors=new[]{_discoveryError}.Concat(snapshots.Select(s=>s.Error)).Where(e=>!string.IsNullOrEmpty(e)).Distinct();
                string error=string.Join("；",errors);
                DateTime completed=snapshots.Select(s=>s.Timestamp).Concat(new[]{_completed}).Max();
                return new BatterySnapshot(readings,completed,string.IsNullOrEmpty(error)?null:error,false,_discovery!=null || snapshots.Any(s=>s.IsScanning));
            }
            finally { _gate.Release(); }
        }

        public void Dispose()
        {
            _gate.Wait();
            try
            {
                if(_disposed)return;_disposed=true;
                foreach(var receiver in _receivers.Values)receiver.Channel.Dispose();
                // Never dispose native resources under a running receiver. Channels
                // retain ownership through cleanup, including remove/reinsert races.
                if(_discovery!=null)_discovery.ContinueWith(t=>{var error=t.Exception;},TaskContinuationOptions.OnlyOnFaulted);
            }
            finally { _gate.Release(); }
        }
    }
}
