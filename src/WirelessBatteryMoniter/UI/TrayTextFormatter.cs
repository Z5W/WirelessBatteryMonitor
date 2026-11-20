using System;
using System.Linq;
using System.Collections.Generic;
using WirelessBatteryMoniter.Models;

namespace WirelessBatteryMoniter.UI
{
    internal static class TrayTextFormatter
    {
        internal const int MaximumLength = 127;

        internal static string DeviceType(string name)
        {
            string n = (name ?? "").ToLowerInvariant();
            if (new[] { "headphone", "headset", "耳机", "wh-", "airpods", "buds" }.Any(n.Contains)) return "耳机";
            if (new[] { "keyboard", "键盘", "keychron", "rs7", "f87", "k3" }.Any(n.Contains)) return "键盘";
            if (new[] { "xbox", "controller", "手柄", "dualsense", "gamepad" }.Any(n.Contains)) return "手柄";
            if (new[] { "mouse", "鼠标", "mx master", "g502", "g304", "viper", "superstrike", "superlight", "pro x" }.Any(n.Contains)) return "鼠标";
            return "设备";
        }

        private static string Clean(string text)
        {
            return (text ?? "").Replace('\r', ' ').Replace('\n', ' ').Replace('\0', ' ').Trim();
        }

        private static string Shorten(string text, int limit)
        {
            if (text.Length <= limit) return text;
            int take = Math.Max(0, limit - 1);
            if (take > 0 && char.IsHighSurrogate(text[take - 1])) take--;
            return text.Substring(0, take) + "…";
        }

        private static string Line(DeviceBatteryInfo device, int nameLimit)
        {
            string kind = device.Kind == "2.4G" ? "2.4G" : device.Kind == "蓝牙 LE" ? "BLE" : device.Kind.Contains("蓝牙") ? "蓝牙" : Clean(device.Kind);
            string state = !device.IsConnected ? "（未连接）" : device.IsStale ? "（缓存）" : "";
            return "[" + Shorten(kind, 8) + "] " + DeviceType(device.Name) + ": "
                + Shorten(Clean(device.Name), nameLimit) + " " + device.LevelText + "电量" + state;
        }

        internal static string BuildFull(BatterySnapshot snapshot, bool showDisconnected)
        {
            string header = "WirelessBatteryMoniter · " + (snapshot == null ? 0 : snapshot.ConnectedCount) + " 台";
            if (snapshot == null) return header + "\n正在扫描…";
            var devices = snapshot.Devices.Where(d => d.IsConnected || showDisconnected).ToList();
            return header + "\n" + (devices.Count == 0 ? "未发现设备" : string.Join("\n", devices.Select(d => Line(d, int.MaxValue))));
        }

        internal static string Build(BatterySnapshot snapshot, bool showDisconnected)
        {
            string header = "WirelessBatteryMoniter · " + (snapshot == null ? 0 : snapshot.ConnectedCount) + " 台";
            if (snapshot == null) return header + "\n正在扫描…";
            var devices = snapshot.Devices.Where(d => d.IsConnected || showDisconnected).ToList();
            if (devices.Count == 0) return header + "\n未发现设备";
            // Keep all rows if shortening names can fit the native 127-character
            // buffer. Never truncate a percentage or silently drop a device.
            for (int shown = devices.Count; shown >= 0; shown--)
            {
                for (int nameLimit = 80; nameLimit >= 4; nameLimit--)
                {
                    var lines = new List<string> { header };
                    lines.AddRange(devices.Take(shown).Select(d => Line(d, nameLimit)));
                    if (shown < devices.Count) lines.Add("另 " + (devices.Count - shown) + " 台，点击查看");
                    string text = string.Join("\n", lines);
                    if (text.Length <= MaximumLength) return text;
                }
            }
            return header;
        }
    }
}

