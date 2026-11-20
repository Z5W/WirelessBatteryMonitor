using System;
using System.Runtime.InteropServices;

namespace WirelessBatteryMoniter.UI
{
    internal static class NativeTrayIcon
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct IconData
        {
            public uint Size;
            public IntPtr Window;
            public uint Id, Flags, Callback;
            public IntPtr Icon;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Tip;
            public uint State, StateMask;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Info;
            public uint Version;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string Title;
            public uint InfoFlags;
            public Guid Guid;
            public IntPtr BalloonIcon;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool Shell_NotifyIconW(uint message, ref IconData data);
        internal static bool SetStandardTooltip(IntPtr window, uint id, string text)
        {
            if (text.Length > 127) throw new ArgumentOutOfRangeException("text");
            IconData data = new IconData { Size = (uint)Marshal.SizeOf(typeof(IconData)),
                Window = window, Id = id, Flags = 4, Tip = text };
            return Shell_NotifyIconW(1, ref data);
        }

    }
}

