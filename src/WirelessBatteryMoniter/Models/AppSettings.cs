using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;

namespace WirelessBatteryMoniter.Models
{
    /// <summary>
    /// User-facing configuration. Persisted as JSON under %APPDATA%\WirelessBatteryMoniter.
    /// </summary>
    [DataContract]
    public sealed class AppSettings
    {
        [DataMember(Order = 1)]
        public int LowBatteryThreshold { get; set; }

        [DataMember(Order = 2)]
        public bool NotificationsEnabled { get; set; }

        [DataMember(Order = 3)]
        public bool NotificationSound { get; set; }

        [DataMember(Order = 4)]
        public int PollSeconds { get; set; }

        [DataMember(Order = 5)]
        public bool StartMinimized { get; set; }

        [DataMember(Order = 6)]
        public bool ShowDisconnected { get; set; }

        [DataMember(Order = 7)]
        public bool FirstRun { get; set; }

        [DataMember(Order = 8)]
        public List<DevicePreference> Devices { get; set; }

        /// <summary>Runtime-only, always synchronized from the registry on load.</summary>
        [IgnoreDataMember]
        public bool StartWithWindows { get; set; }

        /// <summary>Runtime-only, enabled by --demo.</summary>
        [IgnoreDataMember]
        public bool DemoMode { get; set; }

        public AppSettings()
        {
            SetDefaults();
        }

        /// <summary>
        /// Defaults for a new installation.
        /// </summary>
        private void SetDefaults()
        {
            Devices = new List<DevicePreference>();
            LowBatteryThreshold = 20;
            NotificationsEnabled = true;
            NotificationSound = true;
            PollSeconds = 10;
            StartMinimized = true;
            ShowDisconnected = true;
            FirstRun = true;
        }

        /// <summary>
        /// DataContractJsonSerializer creates the object without running the
        /// constructor, so a settings file written by an older version (or one
        /// missing a member) would otherwise leave absent fields at 0/false.
        /// Defaults are applied here first and then overwritten by the members
        /// that are actually present in the file.
        /// </summary>
        [OnDeserializing]
        private void OnDeserializing(StreamingContext context)
        {
            SetDefaults();
            // The file exists, so this is not a first installation even when an
            // older version did not persist the flag.
            FirstRun = false;
        }

        public void Normalize()
        {
            Devices = (Devices ?? new List<DevicePreference>()).Where(d => d != null && !string.IsNullOrWhiteSpace(d.Id))
                .GroupBy(d => d.Id, StringComparer.OrdinalIgnoreCase).Select(g => g.Last()).ToList();
            foreach (var device in Devices) device.Normalize();
            if (LowBatteryThreshold < 5) LowBatteryThreshold = 5;
            if (LowBatteryThreshold > 95) LowBatteryThreshold = 95;
            if (PollSeconds < 5) PollSeconds = 5;
            if (PollSeconds > 120) PollSeconds = 120;
        }

        public DevicePreference PreferenceFor(string id)
        {
            return (Devices ?? new List<DevicePreference>()).FirstOrDefault(d => string.Equals(d.Id,id,StringComparison.OrdinalIgnoreCase));
        }
        public int ThresholdFor(string id) { var p=PreferenceFor(id); return p != null && p.LowBatteryThreshold.HasValue ? p.LowBatteryThreshold.Value : LowBatteryThreshold; }
        public bool IsHidden(string id) { var p=PreferenceFor(id); return p != null && p.Hidden; }
        public DeviceBatteryInfo DisplayDevice(DeviceBatteryInfo device)
        {
            var p=PreferenceFor(device.Id);
            return p != null && !string.IsNullOrWhiteSpace(p.Alias) ? device.WithDisplayName(p.Alias) : device;
        }
        public BatterySnapshot DisplaySnapshot(BatterySnapshot snapshot)
        {
            return new BatterySnapshot(snapshot.Devices.Where(d=>!IsHidden(d.Id)).Select(DisplayDevice),snapshot.Timestamp,snapshot.Error,snapshot.IsDemo,snapshot.IsScanning);
        }
        public AppSettings Clone()
        {
            return new AppSettings
            {
                LowBatteryThreshold = LowBatteryThreshold,
                NotificationsEnabled = NotificationsEnabled,
                NotificationSound = NotificationSound,
                PollSeconds = PollSeconds,
                StartMinimized = StartMinimized,
                ShowDisconnected = ShowDisconnected,
                FirstRun = FirstRun,
                StartWithWindows = StartWithWindows,
                DemoMode = DemoMode,
                Devices = (Devices ?? new List<DevicePreference>()).Select(d => d.Clone()).ToList()
            };
        }
    }
}
