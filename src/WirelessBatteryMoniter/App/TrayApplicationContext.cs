using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using WirelessBatteryMoniter.Interop;
using WirelessBatteryMoniter.Models;
using WirelessBatteryMoniter.Services;
using WirelessBatteryMoniter.UI;
using WinFormsTimer = System.Windows.Forms.Timer;

namespace WirelessBatteryMoniter.App
{
    internal sealed class TrayApplicationContext : ApplicationContext
    {
        private readonly AppSettings _settings;
        private readonly SettingsService _settingsService;
        private readonly BatteryMonitorService _monitor;
        private readonly NotificationService _notifier;
        private readonly NotifyIcon _notifyIcon;
        private readonly TrayTextTip _textTip;
        private readonly ContextMenuStrip _menu;
        private readonly Control _dispatcher;
        private readonly EventWaitHandle _activateEvent;
        private readonly Thread _activateThread;
        private readonly WinFormsTimer _pollTimer;
        private readonly WinFormsTimer _trayCheckTimer;
        private int _trayCheckAttempts;
        private bool _trayRegistered;
        private bool _trayRecoveryWindowShown;
        private bool _disposed;
        private readonly CancellationTokenSource _shutdown = new CancellationTokenSource();
        private Task _activeScan;

        private readonly ToolStripMenuItem _openItem;
        private readonly ToolStripMenuItem _refreshItem;
        private readonly ToolStripMenuItem _notificationsItem;
        private readonly ToolStripMenuItem _thresholdMenu;
        private readonly ToolStripMenuItem _startupItem;
        private readonly ToolStripMenuItem _settingsItem;
        private readonly ToolStripMenuItem _aboutItem;
        private readonly ToolStripMenuItem _exitItem;

        private MainForm _mainForm;
        private BatterySnapshot _snapshot;
        private Icon _staticIcon;
        private Icon _currentIcon;
        private readonly List<Icon> _retiredIcons = new List<Icon>();
        private bool _refreshing;
        private bool _exiting;
        private bool _trayHintShown;

        public TrayApplicationContext(
            AppSettings settings,
            bool demoMode,
            bool showMainWindow,
            bool startMinimized)
        {
            _settings = settings;
            _settings.DemoMode = demoMode;
            _settings.StartWithWindows = StartupService.IsEnabled();
            _settingsService = new SettingsService();
            _monitor = new BatteryMonitorService { DemoMode = demoMode };

            _dispatcher = new Control();
            _dispatcher.CreateControl();
            IntPtr unused = _dispatcher.Handle;

            _staticIcon = LoadStaticIcon();
            _notifyIcon = new NotifyIcon
            {
                Text = "",
                Icon = _staticIcon,
                Visible = false
            };
            _currentIcon = _staticIcon;
            _textTip = new TrayTextTip(_notifyIcon);
            _notifier = new NotificationService(_notifyIcon, _settings);
            _notifier.ToastPresenter = delegate(IList<DeviceBatteryInfo> devices, int threshold)
            {
                LowBatteryToastForm.ShowToast(devices, threshold, ShowMainWindow);
            };

            _menu = BuildMenu(
                out _openItem,
                out _refreshItem,
                out _notificationsItem,
                out _thresholdMenu,
                out _startupItem,
                out _settingsItem,
                out _aboutItem,
                out _exitItem);
            _notifyIcon.ContextMenuStrip = _menu;
            // Register the icon only after its icon and menu exist. Some Windows 10
            // shells ignore an icon that was marked visible before it had an image.
            _notifyIcon.Visible = true;
            Application.Idle += OnApplicationIdle;

            _pollTimer = new WinFormsTimer { Interval = Math.Max(5, _settings.PollSeconds) * 1000 };
            _pollTimer.Tick += async (s, e) => await RefreshAsync(false);

            _trayCheckTimer = new WinFormsTimer { Interval = 1500 };
            _trayCheckTimer.Tick += TrayCheckTimer_Tick;
            _trayCheckTimer.Start();

            _notifyIcon.MouseClick += NotifyIcon_MouseClick;
            _notifyIcon.BalloonTipClicked += (s, e) => ShowMainWindow();

            _activateEvent = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\WirelessBatteryMoniter.Activate");
            _activateThread = new Thread(ActivationLoop)
            {
                IsBackground = true,
                Name = "WirelessBatteryMoniter.Activate"
            };
            _activateThread.Start();

            _pollTimer.Start();

            if (showMainWindow || !startMinimized)
            {
                ShowMainWindow();
                if (_settings.FirstRun)
                {
                    _settings.FirstRun = false;
                    string ignored;
                    _settingsService.TrySave(_settings, out ignored);
                }
            }

            var ignoredTask = RefreshAsync(false);
        }

        private ContextMenuStrip BuildMenu(
            out ToolStripMenuItem openItem,
            out ToolStripMenuItem refreshItem,
            out ToolStripMenuItem notificationsItem,
            out ToolStripMenuItem thresholdMenu,
            out ToolStripMenuItem startupItem,
            out ToolStripMenuItem settingsItem,
            out ToolStripMenuItem aboutItem,
            out ToolStripMenuItem exitItem)
        {
            ContextMenuStrip menu = new ContextMenuStrip
            {
                Renderer = new DarkMenuRenderer(),
                BackColor = Theme.Surface,
                ForeColor = Theme.Text,
                Font = Theme.Font(9f),
                ShowImageMargin = false,
                ShowCheckMargin = true,
                Padding = new Padding(4),
                DropShadowEnabled = true
            };

            openItem = MenuItem("打开主界面", delegate { ShowMainWindow(); });
            openItem.Font = Theme.Font(9f, FontStyle.Bold);

            refreshItem = MenuItem("立即刷新", async delegate { await RefreshAsync(true); });

            menu.Items.Add(openItem);
            menu.Items.Add(refreshItem);
            menu.Items.Add(new ToolStripSeparator());

            notificationsItem = MenuItem("低电量提醒", delegate { ToggleNotifications(); });
            menu.Items.Add(notificationsItem);

            thresholdMenu = new ToolStripMenuItem("低电量阈值");
            StyleMenuItem(thresholdMenu);
            foreach (int value in new[] { 10, 15, 20, 25, 30, 40, 50 })
            {
                int threshold = value;
                ToolStripMenuItem item = new ToolStripMenuItem(threshold + "%", null, delegate { ChangeThreshold(threshold); });
                item.Tag = threshold;
                StyleMenuItem(item);
                thresholdMenu.DropDownItems.Add(item);
            }
            thresholdMenu.DropDown.Renderer = new DarkMenuRenderer();
            menu.Items.Add(thresholdMenu);

            startupItem = MenuItem("开机自动启动", delegate { ToggleStartup(); });
            menu.Items.Add(startupItem);
            menu.Items.Add(new ToolStripSeparator());

            settingsItem = MenuItem("设置…", delegate { ShowSettings(); });
            menu.Items.Add(MenuItem("托盘图标设置…", delegate { SystemSettings.OpenNotificationArea(); }));
            aboutItem = MenuItem("关于", delegate { ShowAbout(); });
            exitItem = MenuItem("退出", delegate { ExitApplication(); });

            menu.Items.Add(settingsItem);
            menu.Items.Add(aboutItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(exitItem);

            menu.Opening += (s, e) =>
            {
                UpdateMenuState();
            };
            return menu;
        }

        private static ToolStripMenuItem MenuItem(string text, EventHandler handler)
        {
            ToolStripMenuItem item = new ToolStripMenuItem(text);
            StyleMenuItem(item);
            item.Click += handler;
            return item;
        }

        private static void StyleMenuItem(ToolStripMenuItem item)
        {
            item.Padding = new Padding(10, 5, 16, 5);
            item.ForeColor = Theme.Text;
        }

        private void UpdateMenuState()
        {
            _notificationsItem.Checked = _settings.NotificationsEnabled;
            _startupItem.Checked = StartupService.IsEnabled();
            _settings.StartWithWindows = _startupItem.Checked;

            foreach (ToolStripItem item in _thresholdMenu.DropDownItems)
            {
                ToolStripMenuItem threshold = item as ToolStripMenuItem;
                if (threshold == null || !(threshold.Tag is int)) continue;
                threshold.Checked = (int)threshold.Tag == _settings.LowBatteryThreshold;
            }
        }

        private async Task RefreshAsync(bool manual)
        {
            if (_refreshing || _exiting) return;
            _refreshing = true;
            if (manual && _mainForm != null) _mainForm.SetBusy(true);

            try
            {
                using (CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(_shutdown.Token))
                {
                    Task<BatterySnapshot> scan = _monitor.ScanAsync(cancellation.Token);
                    _activeScan = scan;
                    BatterySnapshot snapshot = await scan;
                    if (_exiting) return;
                    _snapshot = snapshot;
                    ApplySnapshot();
                    _notifier.Process(snapshot);
                }
            }
            catch (OperationCanceledException)
            {
                AppLog.Warn("Battery scan timed out.");
                if (_mainForm != null)
                {
                    _mainForm.SetBusy(false);
                }
            }
            catch (Exception ex)
            {
                AppLog.Error("Unexpected refresh failure.", ex);
            }
            finally
            {
                _refreshing = false;
                if (!_exiting && _mainForm != null && !_mainForm.IsDisposed) _mainForm.SetBusy(false);
            }
        }

        private void ApplySnapshot()
        {
            if (_snapshot == null) return;

            int count = _snapshot.ConnectedCount;
            int? minimum = _snapshot.MinimumLevel;
            Icon newIcon = null;
            try
            {
                newIcon = TrayIconRenderer.Create(count, minimum, _settings.LowBatteryThreshold);
            }
            catch (Exception ex)
            {
                AppLog.Error("Dynamic tray icon failed; using the static application icon.", ex);
            }
            if (newIcon == null) newIcon = _staticIcon;

            Icon oldIcon = _currentIcon;
            try
            {
                _notifyIcon.Icon = newIcon;
                _notifyIcon.Visible = true;
            }
            catch (Exception ex)
            {
                AppLog.Error("Could not assign the tray icon.", ex);
            }
            _currentIcon = newIcon;
            if (oldIcon != null && !ReferenceEquals(oldIcon, _staticIcon) && !ReferenceEquals(oldIcon, newIcon))
            {
                // Keep a few retired icon handles alive after NIM_MODIFY. Disposing one
                // immediately can leave a blank tray slot on older shells; a small cap
                // avoids leaking a handle every refresh.
                _retiredIcons.Add(oldIcon);
                while (_retiredIcons.Count > 8)
                {
                    _retiredIcons[0].Dispose();
                    _retiredIcons.RemoveAt(0);
                }
            }

            UpdateTrayText();

            if (_mainForm != null && !_mainForm.IsDisposed)
            {
                _mainForm.UpdateSnapshot(_snapshot, _settings);
            }

        }

        private string BuildTrayText()
        {
            return TrayTextFormatter.BuildFull(_snapshot, _settings.ShowDisconnected);
        }

        private void UpdateTrayText()
        {
            _textTip.Content = BuildTrayText();
        }

        private void TrayCheckTimer_Tick(object sender, EventArgs e)
        {
            _trayCheckTimer.Stop();
            if (_exiting) return;
            Rectangle bounds;
            string diagnostic;
            if (TrayIconProbe.TryGetRect(_notifyIcon, out bounds, out diagnostic))
            {
                if (!_trayRegistered) AppLog.Info("Tray rectangle available: " + diagnostic);
                _trayRegistered = true;
                _trayCheckAttempts = 0;
                _trayCheckTimer.Interval = 30000;
            }
            else
            {
                _trayRegistered = false;
                _trayCheckAttempts++;
                if (_trayCheckAttempts <= 3)
                {
                    AppLog.Warn("Tray rectangle query failed (" + _trayCheckAttempts + "/3): " + diagnostic);
                    try
                    {
                        _notifyIcon.Visible = false;
                        _notifyIcon.Visible = true;
                    }
                    catch (Exception ex) { AppLog.Error("Tray re-registration failed.", ex); }
                }
                if (_trayCheckAttempts == 3 && !_trayRecoveryWindowShown)
                {
                    _trayRecoveryWindowShown = true;
                    AppLog.Warn("Tray visibility is unconfirmed; opening the main window for recovery. No system cache was changed.");
                    ShowMainWindow();
                }
                _trayCheckTimer.Interval = _trayCheckAttempts < 3 ? 1500 : 30000;
            }
            // NotifyIcon handles TaskbarCreated itself. Continue checking so
            // later Explorer restarts can be diagnosed as well as startup.
            UpdateTrayText();
            _trayCheckTimer.Start();
        }

        private void OnApplicationIdle(object sender, EventArgs e)
        {
            Application.Idle -= OnApplicationIdle;
            try
            {
                _notifyIcon.Visible = true;
            }
            catch (Exception ex)
            {
                AppLog.Error("Could not show the tray icon after startup.", ex);
            }
        }

        private static Icon LoadStaticIcon()
        {
            try
            {
                using (Icon icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath))
                {
                    if (icon != null) return (Icon)icon.Clone();
                }
            }
            catch
            {
            }
            return (Icon)SystemIcons.Application.Clone();
        }

        private void NotifyIcon_MouseClick(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                ShowMainWindow();
            }
        }

        private void ShowMainWindow()
        {
            if (_exiting) return;
            LowBatteryToastForm.CloseCurrent();

            if (_mainForm == null || _mainForm.IsDisposed)
            {
                _mainForm = new MainForm(ShowSettings, () => RefreshAsync(true));
                _mainForm.ExitRequested += (s, e) => ExitApplication();
                _mainForm.MinimizedToTray += (s, e) =>
                {
                    if (_trayHintShown) return;
                    _trayHintShown = true;
                    _notifyIcon.ShowBalloonTip(2500, "仍在后台运行",
                        "WirelessBatteryMoniter 已最小化到系统托盘，悬停图标可查看设备电量。",
                        ToolTipIcon.Info);
                };
                if (_snapshot != null) _mainForm.UpdateSnapshot(_snapshot, _settings);
            }

            try
            {
                if (!_mainForm.Visible) _mainForm.Show();
                if (_mainForm.WindowState == FormWindowState.Minimized)
                {
                    _mainForm.WindowState = FormWindowState.Normal;
                }
                _mainForm.Activate();
                _mainForm.BringToFront();
                NativeMethods.SetForegroundWindow(_mainForm.Handle);
            }
            catch (Exception ex)
            {
                AppLog.Error("Failed to show main window.", ex);
            }
        }

        private void ShowSettings()
        {
            LowBatteryToastForm.CloseCurrent();
            using (SettingsForm form = new SettingsForm(_settings, ApplySettings, _notifier.ShowTest))
            {
                if (_mainForm != null && _mainForm.Visible) form.ShowDialog(_mainForm);
                else form.ShowDialog();
            }
        }

        private void ShowAbout()
        {
            using (AboutForm form = new AboutForm())
            {
                if (_mainForm != null && _mainForm.Visible) form.ShowDialog(_mainForm);
                else form.ShowDialog();
            }
        }

        private void ToggleNotifications()
        {
            AppSettings updated = _settings.Clone();
            updated.NotificationsEnabled = !_settings.NotificationsEnabled;
            ApplySettings(updated);
        }

        private void ChangeThreshold(int threshold)
        {
            AppSettings updated = _settings.Clone();
            updated.LowBatteryThreshold = threshold;
            ApplySettings(updated);
        }

        private void ToggleStartup()
        {
            AppSettings updated = _settings.Clone();
            updated.StartWithWindows = !StartupService.IsEnabled();
            ApplySettings(updated);
        }

        private void ApplySettings(AppSettings updated)
        {
            if (updated == null) return;

            bool desiredStartup = updated.StartWithWindows;
            bool currentStartup = StartupService.IsEnabled();
            if (desiredStartup || currentStartup)
            {
                string error;
                if (!StartupService.TrySetEnabled(desiredStartup, out error))
                {
                    MessageBox.Show(
                        "无法更新开机启动设置：" + error,
                        "WirelessBatteryMoniter",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                }
            }

            bool thresholdChanged = updated.LowBatteryThreshold != _settings.LowBatteryThreshold;
            _settings.LowBatteryThreshold = updated.LowBatteryThreshold;
            _settings.NotificationsEnabled = updated.NotificationsEnabled;
            _settings.NotificationSound = updated.NotificationSound;
            _settings.PollSeconds = updated.PollSeconds;
            _settings.StartMinimized = updated.StartMinimized;
            _settings.ShowDisconnected = updated.ShowDisconnected;
            _settings.FirstRun = updated.FirstRun;
            _settings.StartWithWindows = StartupService.IsEnabled();
            _settings.Normalize();

            _notifier.Settings = _settings;
            if (thresholdChanged || !_settings.NotificationsEnabled) _notifier.Reset();

            _pollTimer.Interval = Math.Max(5, _settings.PollSeconds) * 1000;
            UpdateTrayText();

            if (_mainForm != null && !_mainForm.IsDisposed && _snapshot != null)
            {
                _mainForm.UpdateSnapshot(_snapshot, _settings);
            }

            string saveError;
            if (!_settingsService.TrySave(_settings, out saveError))
            {
                AppLog.Warn("Settings could not be saved: " + saveError);
            }

            UpdateMenuState();
        }

        private void DisposeIcons()
        {
            foreach (Icon icon in _retiredIcons)
            {
                if (icon != null) icon.Dispose();
            }
            _retiredIcons.Clear();

            if (_currentIcon != null && !ReferenceEquals(_currentIcon, _staticIcon))
            {
                _currentIcon.Dispose();
            }
            if (_staticIcon != null)
            {
                _staticIcon.Dispose();
            }
            _currentIcon = null;
            _staticIcon = null;
        }

        private void ActivationLoop()
        {
            while (!_exiting)
            {
                try
                {
                    if (!_activateEvent.WaitOne(1000)) continue;
                    if (_exiting) return;
                    if (_dispatcher != null && !_dispatcher.IsDisposed && _dispatcher.IsHandleCreated)
                    {
                        _dispatcher.BeginInvoke(new Action(ShowMainWindow));
                    }
                }
                catch (ObjectDisposedException)
                {
                    return;
                }
                catch (Exception ex)
                {
                    AppLog.Error("Activation listener failed.", ex);
                    return;
                }
            }
        }

        private void ExitApplication()
        {
            if (_exiting) return;
            _exiting = true;
            _shutdown.Cancel();
            if (_mainForm != null && !_mainForm.IsDisposed) _mainForm.CloseForExit();
            ExitThread();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && !_disposed)
            {
                _disposed = true;
                _exiting = true;
                _shutdown.Cancel();
                _pollTimer.Dispose();
                _trayCheckTimer.Dispose();
                Application.Idle -= OnApplicationIdle;
                LowBatteryToastForm.CloseCurrent();
                if (_mainForm != null) _mainForm.Dispose();
                _notifyIcon.Visible = false;
                _textTip.Dispose();
                _notifyIcon.Dispose();
                _menu.Dispose();
                DisposeIcons();
                _activateEvent.Set();
                _activateThread.Join(1500);
                _activateEvent.Dispose();
                _dispatcher.Dispose();
                // Providers own mutable caches; dispose only when both scans have ended.
                if (_activeScan != null && !_activeScan.IsCompleted)
                    _activeScan.ContinueWith(t => { _monitor.Dispose(); _shutdown.Dispose(); }, TaskScheduler.Default);
                else
                {
                    _monitor.Dispose();
                    _shutdown.Dispose();
                }
            }
            base.Dispose(disposing);
        }
    }
}

