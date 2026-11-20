using System;
using System.Runtime.InteropServices;

namespace WirelessBatteryMoniter.Interop
{
    internal static class NativeMethods
    {
        [DllImport("user32.dll", SetLastError = true)]
        internal static extern bool DestroyIcon(IntPtr hIcon);

        [DllImport("user32.dll")]
        internal static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        internal static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        private static extern int SetWindowTheme(IntPtr hWnd, string subAppName, string subIdList);

        internal const int SwRestore = 9;

        internal static void ApplyDarkWindow(IntPtr handle)
        {
            if (handle == IntPtr.Zero) return;
            try
            {
                int enabled = 1;
                // 20 = DWMWA_USE_IMMERSIVE_DARK_MODE on Windows 10 2004+, 19 on older builds.
                if (DwmSetWindowAttribute(handle, 20, ref enabled, sizeof(int)) != 0)
                {
                    DwmSetWindowAttribute(handle, 19, ref enabled, sizeof(int));
                }
                SetWindowTheme(handle, "DarkMode_Explorer", null);
            }
            catch
            {
                // Older Windows versions simply keep the default title bar.
            }
        }
    }
}
