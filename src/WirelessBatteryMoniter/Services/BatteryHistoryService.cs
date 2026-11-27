using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using WirelessBatteryMoniter.Models;

namespace WirelessBatteryMoniter.Services
{
    [DataContract]
    public sealed class BatteryHistoryPoint
    {
        [DataMember] public string Id { get; set; }
        [DataMember] public string Name { get; set; }
        [DataMember] public DateTime Utc { get; set; }
        [DataMember] public int Level { get; set; }
        [DataMember] public BatteryReadingSource Source { get; set; }
        [DataMember] public BatteryChargeState Charge { get; set; }
        // Every process session has a separate identity: graphs never bridge downtime.
        [DataMember] public string Session { get; set; }
        public string LevelText { get { return (Source==BatteryReadingSource.VoltageEstimate || Source==BatteryReadingSource.CoarseLevel ? "约 " : "")+Level+"%"; } }
    }

    public sealed class BatteryHistoryService
    {
        private readonly string _path;
        private readonly Func<DateTime> _now;
        private readonly string _session=Guid.NewGuid().ToString("N");
        private readonly Dictionary<string,string> _segments=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        private readonly object _gate=new object();
        private List<BatteryHistoryPoint> _points=new List<BatteryHistoryPoint>();
        private bool _dirty, _loadFailed;
        private DateTime _nextSave;
        public string Error { get; private set; }
        public BatteryHistoryService(string directory) : this(directory,()=>DateTime.UtcNow) { }
        internal BatteryHistoryService(string directory,Func<DateTime> now)
        {
            _path=Path.Combine(directory,"battery-history.json"); _now=now;
            Load();
        }
        private void Load()
        {
            try
            {
                var loaded=new List<BatteryHistoryPoint>();
                if (File.Exists(_path))
                    using(var stream=File.OpenRead(_path))
                        loaded=(List<BatteryHistoryPoint>)new DataContractJsonSerializer(typeof(List<BatteryHistoryPoint>)).ReadObject(stream);
                if (loaded==null || loaded.Any(p=>p==null || string.IsNullOrEmpty(p.Id) || p.Level<0 || p.Level>100))
                    throw new InvalidDataException("历史文件内容无效");
                _points=loaded;
                _loadFailed=false; Error=null; Trim();
            }
            catch(Exception ex) { _loadFailed=true; Error="无法读取历史记录，原文件未覆盖："+ex.Message; }
        }
        private void Trim()
        {
            // Bounded file size even with a noisy device; oldest points are discarded.
            var retained=_points.Where(p=>p.Utc>=_now().AddDays(-7) && p.Utc<=_now().AddMinutes(5))
                .OrderByDescending(p=>p.Utc).Take(50000).OrderBy(p=>p.Utc).ToList();
            if (retained.Count!=_points.Count) _dirty=true;
            _points=retained;
        }
        public List<BatteryHistoryPoint> Read()
        {
            lock(_gate) { Trim(); return _points.ToList(); }
        }
        public void Record(BatterySnapshot snapshot)
        {
            lock(_gate)
            {
                if (_loadFailed) return;
                if (snapshot.IsDemo) { _segments.Clear(); return; }
                var present=new HashSet<string>(snapshot.Devices.Select(d=>d.Id),StringComparer.OrdinalIgnoreCase);
                foreach(var id in _segments.Keys.Where(id=>!present.Contains(id)).ToList()) _segments.Remove(id);
                foreach(var d in snapshot.Devices)
                {
                    if (!d.IsConnected || d.IsStale || d.IsSleeping || !d.Level.HasValue || d.ReadingSource==BatteryReadingSource.Demo) { _segments.Remove(d.Id); continue; }
                    DateTime utc=d.LastSeen.ToUniversalTime();
                    utc=new DateTime(utc.Ticks-utc.Ticks%TimeSpan.TicksPerMillisecond,DateTimeKind.Utc);
                    if (utc<_now().AddDays(-7) || utc>_now().AddMinutes(5)) continue;
                    string segment;
                    if (!_segments.TryGetValue(d.Id,out segment)) _segments[d.Id]=segment=_session+Guid.NewGuid().ToString("N");
                    var last=_points.LastOrDefault(p=>string.Equals(p.Id,d.Id,StringComparison.OrdinalIgnoreCase));
                    if (last!=null && (utc<=last.Utc || (last.Session==segment && last.Level==d.Level.Value && last.Source==d.ReadingSource && last.Charge==d.ChargeState && utc-last.Utc<TimeSpan.FromMinutes(10)))) continue;
                    _points.Add(new BatteryHistoryPoint {Id=d.Id,Name=d.OriginalName,Utc=utc,Level=d.Level.Value,Source=d.ReadingSource,Charge=d.ChargeState,Session=segment});
                    _dirty=true;
                }
                Trim();
                if (_dirty && _now()>=_nextSave) Save();
            }
        }
        private void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path));
                string temp=_path+".tmp";
                using(var stream=new FileStream(temp,FileMode.Create,FileAccess.Write,FileShare.None))
                { new DataContractJsonSerializer(typeof(List<BatteryHistoryPoint>)).WriteObject(stream,_points); stream.Flush(true); }
                if (File.Exists(_path)) File.Replace(temp,_path,null); else File.Move(temp,_path);
                _dirty=false; Error=null;
            }
            catch(Exception ex) { Error="历史记录保存失败，将自动重试："+ex.Message; }
            _nextSave=_now().AddSeconds(30);
        }
        public void RetrySave()
        {
            lock(_gate) { if (_loadFailed) Load(); if (!_loadFailed && _dirty) Save(); }
        }
    }
}
