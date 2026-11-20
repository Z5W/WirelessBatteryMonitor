using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Automation;
using System.Windows.Forms;

namespace WirelessBatteryMoniter.Services
{
    public static class SystemSettings
    {
        private static int _opening;
        private const string IconLink = "SystemSettings_Taskbar_SelectIconsToAppearOnTaskbar_HyperlinkButton";
        private const string IconPage = "SystemSettings_Notifications_ShowIconsOnTaskbar_ToggleSwitch";

        public static void OpenNotificationArea()
        {
            if (Interlocked.CompareExchange(ref _opening, 1, 0) != 0) return;
            try
            {
                // This is a supported page URI. The Windows 10 child page has no
                // corresponding taskbar-selectwhichicon URI (it opens Settings Home).
                using (Process process = Process.Start(new ProcessStartInfo("ms-settings:taskbar") { UseShellExecute = true })) { }
                if (Environment.OSVersion.Version.Build >= 22000)
                {
                    Interlocked.Exchange(ref _opening, 0);
                    return;
                }
                // UI Automation runs off the UI thread, so a slow Settings launch
                // cannot freeze monitoring, the tray menu, or the main window.
                Task.Run((Action)OpenIconList);
            }
            catch (Exception ex)
            {
                Interlocked.Exchange(ref _opening, 0);
                AppLog.Error("Could not open taskbar settings.", ex);
                MessageBox.Show("无法打开 Windows 设置。请打开 设置 → 个性化 → 任务栏。",
                    "WirelessBatteryMoniter", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        private static void OpenIconList()
        {
            try
            {
                if (NavigateToIconList())
                {
                    AppLog.Info("Verified notification icon settings page.");
                    return;
                }
                AppLog.Warn("Taskbar settings opened, but notification icon subpage was not verified.");
                MessageBox.Show("已打开任务栏设置，但未能自动进入图标列表。请在“通知区域”中点击“选择哪些图标显示在任务栏上”。",
                    "WirelessBatteryMoniter", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                AppLog.Error("Could not navigate to notification icon settings.", ex);
                MessageBox.Show("未能自动进入图标列表。请在 设置 → 个性化 → 任务栏 → 通知区域中点击“选择哪些图标显示在任务栏上”。",
                    "WirelessBatteryMoniter", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            finally { Interlocked.Exchange(ref _opening, 0); }
        }

        private static AutomationElement FindById(AutomationElement window, string id)
        {
            return window.FindFirst(TreeScope.Descendants,
                new PropertyCondition(AutomationElement.AutomationIdProperty, id));
        }

        private static bool NavigateToIconList()
        {
            Stopwatch elapsed = Stopwatch.StartNew();
            bool invoked = false;
            while (elapsed.ElapsedMilliseconds < 8000)
            {
                AutomationElementCollection windows = AutomationElement.RootElement.FindAll(
                    TreeScope.Children, System.Windows.Automation.Condition.TrueCondition);
                foreach (AutomationElement window in windows)
                {
                    try
                    {
                        using (Process process = Process.GetProcessById(window.Current.ProcessId))
                        {
                            if (process.ProcessName != "ApplicationFrameHost" && process.ProcessName != "SystemSettings") continue;
                        }
                        // Check a unique control on the actual destination page;
                        // successfully starting SystemSettings is not proof of navigation.
                        if (invoked && FindById(window, IconPage) != null) return true;
                        if (invoked) continue;
                        AutomationElement link = FindById(window, IconLink);
                        object pattern;
                        if (link != null && link.Current.IsEnabled && link.TryGetCurrentPattern(InvokePattern.Pattern, out pattern))
                        {
                            ((InvokePattern)pattern).Invoke();
                            invoked = true;
                        }
                    }
                    catch (ElementNotAvailableException) { }
                    catch (InvalidOperationException) { }
                    catch (ArgumentException) { }
                }
                Thread.Sleep(200);
            }
            return false;
        }
    }
}


