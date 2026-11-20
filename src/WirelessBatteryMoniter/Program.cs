using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Security.Principal;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using WirelessBatteryMoniter.App;
using WirelessBatteryMoniter.Models;
using WirelessBatteryMoniter.Services;
using WirelessBatteryMoniter.UI;

namespace WirelessBatteryMoniter
{
    internal static class Program
    {
        private const string MutexName = @"Local\WirelessBatteryMoniter.SingleInstance";
        private const string ActivateEventName = @"Local\WirelessBatteryMoniter.Activate";

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool GetTokenInformation(IntPtr token, int informationClass,
            IntPtr information, int length, out int required);

        internal static string IntegrityLevel()
        {
            using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
            {
                int required;
                GetTokenInformation(identity.Token, 25, IntPtr.Zero, 0, out required);
                if (required == 0) return "unknown";
                IntPtr buffer = Marshal.AllocHGlobal(required);
                try
                {
                    if (!GetTokenInformation(identity.Token, 25, buffer, required, out required)) return "unknown";
                    return new SecurityIdentifier(Marshal.ReadIntPtr(buffer)).Value;
                }
                finally { Marshal.FreeHGlobal(buffer); }
            }
        }

        [STAThread]
        private static int Main(string[] args)
        {
            if (HasArg(args, "--selftest"))
            {
                return SelfTest.Run();
            }

            if (HasArg(args, "--diagnose-scan"))
            {
                return DiagnoseScan();
            }

            if (HasArg(args, "--diagnose-tray"))
            {
                try { return DiagnoseTray(); }
                catch (Exception ex)
                {
                    AppLog.Error("Tray diagnostic failed.", ex);
                    File.AppendAllText(Path.Combine(Path.GetTempPath(), "WirelessBatteryMoniter-diagnostics", "tray.txt"), ex.ToString());
                    return 1;
                }
            }

            if (HasArg(args, "--export-icons"))
            {
                return ExportIcons();
            }

            if (HasArg(args, "--export-ui"))
            {
                return ExportUi();
            }

            string installed = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                @"Programs\WirelessBatteryMoniter\WirelessBatteryMoniterTray.exe");
            if (!string.Equals(Path.GetFullPath(Application.ExecutablePath), Path.GetFullPath(installed), StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show("请运行发布包中的 Start.cmd 安装或升级，再使用桌面快捷方式启动。\r\n\r\n固定安装路径：" + installed
                    + "\r\n\r\n为避免产生重复托盘记录，开发和发布目录中的 EXE 不直接注册托盘图标。",
                    "WirelessBatteryMoniter", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return 2;
            }

            bool demo = HasArg(args, "--demo");
            bool requestedOpen = HasArg(args, "--open");
            bool requestedMinimized = HasArg(args, "--minimized");

            bool created;
            using (Mutex mutex = new Mutex(true, MutexName, out created))
            {
                if (!created)
                {
                    SignalExistingInstance();
                    return 0;
                }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);

                AppLog.Info(string.Format(
                    "Starting WirelessBatteryMoniter; OS={0}; elevated={1}; demo={2}",
                    Environment.OSVersion.VersionString, IsElevated(), demo));
                AppLog.Info("Process integrity: " + IntegrityLevel());

                Application.ThreadException += (s, e) =>
                {
                    AppLog.Error("Unhandled UI exception.", e.Exception);
                    MessageBox.Show(
                        "程序遇到未处理的错误，已记录日志。\r\n\r\n" + e.Exception.Message,
                        "WirelessBatteryMoniter",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                };
                AppDomain.CurrentDomain.UnhandledException += (s, e) =>
                {
                    AppLog.Error("Unhandled application exception.", e.ExceptionObject as Exception);
                };

                SettingsService settingsService = new SettingsService();
                AppSettings settings = settingsService.Load();
                settings.DemoMode = demo;

                bool showMain = requestedOpen || settings.FirstRun || !settings.StartMinimized;
                if (requestedMinimized) showMain = false;

                using (TrayApplicationContext context = new TrayApplicationContext(
                    settings,
                    demo,
                    showMain,
                    requestedMinimized || settings.StartMinimized))
                {
                    Application.Run(context);
                }

                GC.KeepAlive(mutex);
            }

            return 0;
        }

        private static int DiagnoseTray()
        {
            string directory = Path.Combine(Path.GetTempPath(), "WirelessBatteryMoniter-diagnostics");
            Directory.CreateDirectory(directory);
            using (Bitmap bitmap = TrayIconRenderer.CreateBitmap(3, 18, 20))
            {
                bitmap.Save(Path.Combine(directory, "tray-preview.png"), ImageFormat.Png);
            }
            File.WriteAllLines(Path.Combine(directory, "tray.txt"), new[] {
                "Offline tray rendering diagnostic: PASS",
                "Process integrity: " + IntegrityLevel(),
                "No shell icons registered; no random GUIDs or notification-area records created.",
                "This check does not validate live Explorer registration. Use the installed application for that check."
            });
            return 0;
        }
        private static int DiagnoseScan()
        {
            string directory = Path.Combine(Path.GetTempPath(), "WirelessBatteryMoniter-diagnostics");
            AppLog.UseDiagnosticDirectory(directory);
            string output = Path.Combine(directory, "scan.txt");
            try
            {
                using (var monitor = new BatteryMonitorService())
                using (var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(35)))
                {
                    BatterySnapshot snapshot = monitor.ScanAsync(cancellation.Token).GetAwaiter().GetResult();
                    var lines = snapshot.Devices.Select(d => string.Format("{0} | {1} | {2} | connected={3} | stale={4} | {5}",
                        d.Name, d.Kind, d.LevelText, d.IsConnected, d.IsStale, d.Id)).ToList();
                    lines.Insert(0, "Scan at " + DateTime.Now.ToString("O") + "; count=" + snapshot.Devices.Count);
                    if (!string.IsNullOrEmpty(snapshot.Error)) lines.Add("ERROR: " + snapshot.Error);
                    File.WriteAllLines(output, lines);
                    return string.IsNullOrEmpty(snapshot.Error) ? 0 : 1;
                }
            }
            catch (Exception ex)
            {
                File.WriteAllText(output, ex.ToString());
                return 1;
            }
        }

        private static bool HasArg(string[] args, string name)
        {
            return args != null && args.Any(a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase));
        }

        private static bool IsElevated()
        {
            try
            {
                using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
                {
                    WindowsPrincipal principal = new WindowsPrincipal(identity);
                    return principal.IsInRole(WindowsBuiltInRole.Administrator);
                }
            }
            catch
            {
                return false;
            }
        }

        private static void SignalExistingInstance()
        {
            for (int attempt = 0; attempt < 20; attempt++)
            {
                try
                {
                    using (EventWaitHandle handle = EventWaitHandle.OpenExisting(ActivateEventName))
                    {
                        handle.Set();
                        return;
                    }
                }
                catch (WaitHandleCannotBeOpenedException)
                {
                    Thread.Sleep(100);
                }
                catch (Exception ex)
                {
                    AppLog.Error("Failed to activate existing instance.", ex);
                    break;
                }
            }

            MessageBox.Show(
                "WirelessBatteryMoniter 已在运行，请在系统托盘中查看。",
                "WirelessBatteryMoniter",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }

        private static int ExportUi()
        {
            try
            {
                AppLog.UseDiagnosticDirectory(Path.Combine(Path.GetTempPath(), "WirelessBatteryMoniter-diagnostics"));
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);

                string directory = Path.Combine(Directory.GetCurrentDirectory(), "docs");
                Directory.CreateDirectory(directory);
                AppSettings settings = new AppSettings { FirstRun = false, DemoMode = true };
                BatteryMonitorService monitor = new BatteryMonitorService { DemoMode = true };
                BatterySnapshot snapshot = monitor.ScanAsync(CancellationToken.None).GetAwaiter().GetResult();
                monitor.Dispose();

                using (MainForm main = new MainForm(delegate { }, () => Task.FromResult(0)))
                {
                    main.UpdateSnapshot(snapshot, settings);
                    main.Show();
                    Application.DoEvents();
                    using (Bitmap bitmap = new Bitmap(main.Width, main.Height))
                    {
                        main.DrawToBitmap(bitmap, new Rectangle(0, 0, bitmap.Width, bitmap.Height));
                        bitmap.Save(Path.Combine(directory, "screenshot-main-clean.png"), ImageFormat.Png);
                    }
                    main.Hide();
                }

                using (SettingsForm settingsForm = new SettingsForm(settings, delegate { }, delegate { }))
                {
                    settingsForm.Show();
                    Application.DoEvents();
                    using (Bitmap bitmap = new Bitmap(settingsForm.Width, settingsForm.Height))
                    {
                        settingsForm.DrawToBitmap(bitmap, new Rectangle(0, 0, bitmap.Width, bitmap.Height));
                        bitmap.Save(Path.Combine(directory, "screenshot-settings.png"), ImageFormat.Png);
                    }
                    settingsForm.Hide();
                }

                using (AboutForm about = new AboutForm())
                {
                    about.Show();
                    Application.DoEvents();
                    using (Bitmap bitmap = new Bitmap(about.Width, about.Height))
                    {
                        about.DrawToBitmap(bitmap, new Rectangle(0, 0, bitmap.Width, bitmap.Height));
                        bitmap.Save(Path.Combine(directory, "screenshot-about.png"), ImageFormat.Png);
                    }
                    about.Hide();
                }

                return 0;
            }
            catch (Exception ex)
            {
                AppLog.Error("UI export failed.", ex);
                return 1;
            }
        }

        private static int ExportIcons()
        {
            try
            {
                string directory = Path.Combine(Directory.GetCurrentDirectory(), "docs");
                Directory.CreateDirectory(directory);
                int[] counts = { 0, 3, 12 };
                int?[] levels = { null, 18, 56 };
                for (int i = 0; i < counts.Length; i++)
                {
                    using (Bitmap bitmap = TrayIconRenderer.CreateBitmap(counts[i], levels[i], 20))
                    {
                        bitmap.Save(Path.Combine(directory, "tray-icon-" + counts[i] + ".png"), ImageFormat.Png);
                    }
                }
                if (File.Exists(Path.Combine(Directory.GetCurrentDirectory(), "Assets", "app.ico")))
                {
                    using (Icon icon = new Icon(Path.Combine(Directory.GetCurrentDirectory(), "Assets", "app.ico"), 256, 256))
                    using (Bitmap bitmap = icon.ToBitmap())
                    {
                        bitmap.Save(Path.Combine(directory, "app-icon.png"), ImageFormat.Png);
                    }
                }
                return 0;
            }
            catch (Exception ex)
            {
                AppLog.Error("Icon export failed.", ex);
                return 1;
            }
        }
    }
}


