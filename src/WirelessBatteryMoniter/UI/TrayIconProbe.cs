using System;
using System.Drawing;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace WirelessBatteryMoniter.UI
{
    /// <summary>
    /// Reads the shell's rectangle for a NotifyIcon. This is used as a startup
    /// diagnostic. A failed query alone cannot distinguish hidden icons from
    /// registration failure; preserve the failure details for troubleshooting.
    /// </summary>
    internal static class TrayIconProbe
    {
        internal static bool TrySetText(NotifyIcon icon, string text)
        {
            // .NET Framework limits Text to 63 characters; Shell supports 127.
            // Leave a useful managed fallback for Explorer recreation, then update
            // the same registered icon with the full multi-line native text.
            icon.Text = text.Split('\n')[0];
            try
            {
                var flags = BindingFlags.NonPublic | BindingFlags.Instance;
                var windowField = typeof(NotifyIcon).GetField("window", flags);
                var idField = typeof(NotifyIcon).GetField("id", flags);
                if (windowField == null || idField == null) return false;
                var window = windowField.GetValue(icon) as NativeWindow;
                return window != null && window.Handle != IntPtr.Zero
                    && NativeTrayIcon.SetStandardTooltip(window.Handle, (uint)(int)idField.GetValue(icon), text);
            }
            catch (Exception) { return false; }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NOTIFYICONIDENTIFIER
        {
            public uint cbSize;
            public IntPtr hWnd;
            public uint uID;
            public Guid guidItem;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [DllImport("shell32.dll")]
        private static extern int Shell_NotifyIconGetRect(ref NOTIFYICONIDENTIFIER identifier, out RECT rect);

        public static bool TryGetRect(NotifyIcon icon, out Rectangle bounds, out string diagnostic)
        {
            bounds = Rectangle.Empty;
            diagnostic = "NotifyIcon is null";
            if (icon == null) return false;

            try
            {
                FieldInfo windowField = typeof(NotifyIcon).GetField(
                    "window", BindingFlags.NonPublic | BindingFlags.Instance);
                FieldInfo idField = typeof(NotifyIcon).GetField(
                    "id", BindingFlags.NonPublic | BindingFlags.Instance);
                diagnostic = "NotifyIcon reflection fields unavailable; runtime=" + Environment.Version;
                if (windowField == null || idField == null) return false;

                NativeWindow window = windowField.GetValue(icon) as NativeWindow;
                diagnostic = "NotifyIcon native window is not created";
                if (window == null || window.Handle == IntPtr.Zero) return false;

                NOTIFYICONIDENTIFIER identifier = new NOTIFYICONIDENTIFIER
                {
                    cbSize = (uint)Marshal.SizeOf(typeof(NOTIFYICONIDENTIFIER)),
                    hWnd = window.Handle,
                    uID = (uint)(int)idField.GetValue(icon),
                    guidItem = Guid.Empty
                };
                RECT rect;
                int result = Shell_NotifyIconGetRect(ref identifier, out rect);
                diagnostic = string.Format("HRESULT=0x{0:X8}; hwnd=0x{1:X}; id={2}; visible={3}; rect={4},{5},{6},{7}",
                    result, window.Handle.ToInt64(), identifier.uID, icon.Visible,
                    rect.Left, rect.Top, rect.Right, rect.Bottom);
                FieldInfo addedField = typeof(NotifyIcon).GetField("added", BindingFlags.NonPublic | BindingFlags.Instance);
                diagnostic += "; frameworkAdded=" + (addedField == null ? "unknown" : addedField.GetValue(icon).ToString());
                // S_FALSE (1) can accompany a valid overflow-area rectangle.
                // Only a negative HRESULT denotes failure; a rectangle does not
                // prove the icon has been promoted to the visible taskbar.
                if (result < 0) return false;

                bounds = Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);
                return bounds.Width > 0 && bounds.Height > 0;
            }
            catch (Exception ex)
            {
                diagnostic = ex.GetType().Name + ": " + ex.Message;
                return false;
            }
        }
    }
}
