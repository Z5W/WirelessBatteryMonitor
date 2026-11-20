using System;
using System.Windows.Forms;
using Microsoft.Win32;

namespace WirelessBatteryMoniter.Services
{
    public static class StartupService
    {
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "WirelessBatteryMoniter";

        public static bool IsEnabled()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKey, false))
                {
                    if (key == null) return false;
                    object value = key.GetValue(ValueName);
                    return value != null && !string.IsNullOrWhiteSpace(value.ToString());
                }
            }
            catch (Exception ex)
            {
                AppLog.Error("Failed to read startup registration.", ex);
                return false;
            }
        }

        public static bool TrySetEnabled(bool enabled, out string error)
        {
            error = null;
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKey))
                {
                    if (key == null)
                    {
                        error = "无法打开当前用户的启动项注册表。";
                        return false;
                    }

                    if (enabled)
                    {
                        string exe = Application.ExecutablePath;
                        string command = "\"" + exe + "\" --minimized";
                        key.SetValue(ValueName, command, RegistryValueKind.String);
                    }
                    else
                    {
                        if (key.GetValue(ValueName) != null) key.DeleteValue(ValueName, false);
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                AppLog.Error("Failed to update startup registration.", ex);
                return false;
            }
        }
    }
}
