using System;
using System.Runtime.Serialization;
namespace WirelessBatteryMoniter.Models
{
    [DataContract]
    public sealed class DevicePreference
    {
        [DataMember] public string Id { get; set; }
        [DataMember] public string Alias { get; set; }
        [DataMember] public bool Hidden { get; set; }
        [DataMember] public int? LowBatteryThreshold { get; set; }
        [DataMember] public bool DisableNotifications { get; set; }
        public DevicePreference Clone() { return (DevicePreference)MemberwiseClone(); }
        public void Normalize()
        {
            Alias = (Alias ?? "").Trim();
            Alias = Alias.Replace("\r", " ").Replace("\n", " ").Replace("\t", " ");
            if (Alias.Length > 64) Alias = Alias.Substring(0,64);
            if (LowBatteryThreshold.HasValue) LowBatteryThreshold = Math.Max(5, Math.Min(95, LowBatteryThreshold.Value));
        }
    }
}
