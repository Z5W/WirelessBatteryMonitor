using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WirelessBatteryMoniter.Models;
namespace WirelessBatteryMoniter.Services
{
    // A single-flight channel. A timed-out native call retains ownership of its
    // buffers and provider until it actually finishes. It never blocks the other channel.
    internal sealed class BatteryChannel : IDisposable
    {
        private readonly object _gate = new object();
        private readonly Func<CancellationToken,Task<IReadOnlyList<DeviceBatteryInfo>>> _read;
        private readonly Func<string> _warning;
        private readonly Action _dispose;
        private readonly Func<DateTime> _now;
        private readonly int _timeoutMs;
        private Task<IReadOnlyList<DeviceBatteryInfo>> _running;
        private CancellationTokenSource _cancel;
        private IReadOnlyList<DeviceBatteryInfo> _cache = new List<DeviceBatteryInfo>();
        private DateTime _due;
        private string _error;
        private int _failures;
        private bool _disposed;
        internal BatteryChannel(Func<CancellationToken,Task<IReadOnlyList<DeviceBatteryInfo>>> read,
            Func<string> warning, Action dispose, Func<DateTime> now=null, int timeoutMs=35000)
        { _read=read; _warning=warning; _dispose=dispose; _now=now ?? (()=>DateTime.UtcNow); _timeoutMs=timeoutMs; }
        internal static int RetrySeconds(int failures, int interval)
        { return Math.Min(300, Math.Max(5,interval) * (1 << Math.Min(5,Math.Max(0,failures)))); }
        private void Harvest(int interval)
        {
            if (_running==null || !_running.IsCompleted) return;
            bool canceled=_cancel.IsCancellationRequested;
            if (_running.Status==TaskStatus.RanToCompletion && !canceled)
            {
                _cache=_running.Result;
                _error=_warning == null ? null : _warning();
            }
            else
            {
                _error=canceled || _running.IsCanceled ? "查询超时" : "读取失败："+_running.Exception.GetBaseException().Message;
                // Observe fault even if timeout cancellation won the race.
                if (_running.IsFaulted) { var observed=_running.Exception; }
                _cache=_cache.Select(d=>d.AsUnconfirmedCache()).ToList();
            }
            _failures=string.IsNullOrEmpty(_error) ? 0 : Math.Min(5,_failures+1);
            _due=_now().AddSeconds(_failures==0 ? interval : RetrySeconds(_failures,interval));
            _running=null; _cancel.Dispose(); _cancel=null;
        }
        internal async Task<BatterySnapshot> PollAsync(int interval, bool force, CancellationToken shutdown, int waitMs=1500)
        {
            Task running;
            lock (_gate)
            {
                if (_disposed) throw new ObjectDisposedException("BatteryChannel");
                Harvest(interval);
                if (_running==null && (force || _now()>=_due))
                {
                    _cancel=new CancellationTokenSource(); _cancel.CancelAfter(_timeoutMs);
                    var token=_cancel.Token;
                    _running=Task.Run(async ()=>await _read(token).ConfigureAwait(false));
                }
                running=_running;
            }
            if (running!=null) await Task.WhenAny(running,Task.Delay(waitMs,shutdown)).ConfigureAwait(false);
            shutdown.ThrowIfCancellationRequested();
            lock (_gate)
            {
                if (_disposed) throw new ObjectDisposedException("BatteryChannel");
                Harvest(interval);
                bool pending=_running!=null;
                string error=pending ? (_cancel.IsCancellationRequested ? "查询超时，等待驱动清理" : "查询进行中，暂用上次读数") : _error;
                if (!pending && _failures>0) error += "（退避重试中）";
                return new BatterySnapshot(pending ? _cache.Select(d=>d.AsUnconfirmedCache()) : _cache,
                    DateTime.Now,error,false);
            }
        }
        public void Dispose()
        {
            Task running; CancellationTokenSource cancel;
            lock (_gate) { if (_disposed)return; _disposed=true; running=_running;cancel=_cancel; }
            // Cancellation callbacks and native cleanup may block: keep them off UI.
            Task.Run(async ()=> {
                try { if(cancel!=null)cancel.Cancel(); if(running!=null)await running.ConfigureAwait(false); }
                catch(Exception) { /* observe shutdown/fault before disposal */ }
                finally { if(cancel!=null)cancel.Dispose(); _dispose(); }
            });
        }
    }
}
