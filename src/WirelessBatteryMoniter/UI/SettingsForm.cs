using System;
using System.Drawing;
using System.Windows.Forms;
using WirelessBatteryMoniter.Interop;
using WirelessBatteryMoniter.Models;
using WirelessBatteryMoniter.Services;
namespace WirelessBatteryMoniter.UI
{
    internal sealed class SettingsForm : Form
    {
        private readonly AppSettings _source;
        private readonly Action<AppSettings> _save;
        private readonly NumberStepper _pollSeconds, _threshold;
        private readonly ToggleSwitch _showDisconnected, _notifications, _sound, _startup, _startMinimized;
        private readonly FlatButton _testButton;
        private readonly Label _notificationHint;
        private readonly Panel _content;
        public SettingsForm(AppSettings settings,Action<AppSettings> save,Action test)
        {
            SuspendLayout();
            _source=settings.Clone();_save=save;
            Text="设置 · WirelessBatteryMoniter";
            AutoScaleDimensions=new SizeF(96,96);AutoScaleMode=AutoScaleMode.Dpi;
            ClientSize=new Size(520,760);MinimumSize=new Size(500,500);
            MaximizeBox=false;MinimizeBox=false;ShowInTaskbar=false;
            StartPosition=FormStartPosition.CenterParent;
            BackColor=Theme.Background;ForeColor=Theme.Text;Font=Theme.Font(10.5f);KeyPreview=true;
            var header=new Panel { Width=520,Dock=DockStyle.Top,Height=82 };
            MainForm.AddLabel(header,"设置",24,20,420,30,15,Theme.Text,FontStyle.Bold);
            MainForm.AddLabel(header,"调整设备监控、提醒与后台运行方式",24,52,440,22,10.5f,Theme.TextMuted,FontStyle.Regular);
            var footer=new Panel { Width=520,Dock=DockStyle.Bottom,Height=72,BackColor=Theme.Surface };
            var cancel=MainForm.Button(footer,"取消",272,16,96,40);cancel.Anchor=AnchorStyles.Top|AnchorStyles.Right;
            cancel.Click+=(s,e)=> { DialogResult=DialogResult.Cancel;Close(); };
            var apply=MainForm.Button(footer,"保存设置",380,16,116,40);apply.Accent=true;apply.Anchor=AnchorStyles.Top|AnchorStyles.Right;
            apply.Click+=(s,e)=>SaveAndClose();
            var scroll=new Panel { Width=520, Dock=DockStyle.Fill,AutoScroll=true };
            _content=new Panel { Width=520,Dock=DockStyle.Top,Height=594 };
            scroll.Controls.Add(_content);
            Controls.Add(scroll);Controls.Add(header);Controls.Add(footer);
            Section("设备监控",12);
            Row("刷新间隔","两次设备扫描之间的间隔",44);
            _pollSeconds=Number("刷新间隔",44,5,120,_source.PollSeconds," 秒");
            Row("显示未连接设备","保留设备与上次有效读数",106);
            _showDisconnected=Toggle("显示未连接设备",112,_source.ShowDisconnected);
            Section("低电量提醒",180);
            Row("启用桌面提醒","电量达到阈值时发送提醒",212);
            _notifications=Toggle("启用桌面提醒",218,_source.NotificationsEnabled);
            Row("电量阈值","达到或低于该值时提醒",274);
            _threshold=Number("电量阈值",274,5,95,_source.LowBatteryThreshold," %");
            Row("提示音","显示通知时播放系统提示音",336);
            _sound=Toggle("提示音",342,_source.NotificationSound);
            _testButton=MainForm.Button(_content,"发送测试提醒",24,396,140,36);
            _testButton.Click+=(s,e)=> { if(test!=null) test(); };
            _notificationHint=MainForm.AddLabel(_content,"",176,396,310,40,9.75f,Theme.TextMuted,FontStyle.Regular);
            Section("启动与后台",450);
            Row("开机自动启动","登录 Windows 后自动运行",482);
            _startup=Toggle("开机自动启动",488,StartupService.IsEnabled());
            Row("启动时最小化","启动后直接进入系统托盘",544);
            _startMinimized=Toggle("启动时最小化",550,_source.StartMinimized);
            _notifications.CheckedChanged+=(s,e)=>UpdateNotificationState();
            AutoScaleDimensions = new SizeF(96,96); ResumeLayout(true);
            UpdateNotificationState();
            Shown+=(s,e)=>NativeMethods.ApplyDarkWindow(Handle);
            KeyDown+=(s,e)=> { if(e.KeyCode==Keys.Escape) { DialogResult=DialogResult.Cancel;Close(); } };
        }
        private void Section(string text,int y) {
            var line=new Panel { Location=new Point(24,y-8),Size=new Size(454,1),BackColor=Theme.Border,Anchor=AnchorStyles.Top|AnchorStyles.Left|AnchorStyles.Right };
            _content.Controls.Add(line);
            MainForm.AddLabel(_content,text,24,y,440,26,12,Theme.Text,FontStyle.Bold);
        }
        private void Row(string title,string description,int y) {
            MainForm.AddLabel(_content,title,24,y,280,24,10.5f,Theme.Text,FontStyle.Regular);
            MainForm.AddLabel(_content,description,24,y+25,280,22,9.75f,Theme.TextMuted,FontStyle.Regular);
        }
        private ToggleSwitch Toggle(string name,int y,bool value) {
            var toggle=new ToggleSwitch { Location=new Point(430,y),Checked=value,AccessibleName=name,Anchor=AnchorStyles.Top|AnchorStyles.Right,TabIndex=_content.Controls.Count };
            _content.Controls.Add(toggle);return toggle;
        }
        private NumberStepper Number(string name,int y,int min,int max,int value,string suffix) {
            var number=new NumberStepper { Location=new Point(326,y),Size=new Size(152,40),Minimum=min,Maximum=max,Value=value,Suffix=suffix,AccessibleName=name,Anchor=AnchorStyles.Top|AnchorStyles.Right,TabIndex=_content.Controls.Count };
            _content.Controls.Add(number);return number;
        }
        private void UpdateNotificationState() {
            bool enabled=_notifications.Checked;
            _threshold.Enabled=_sound.Enabled=_testButton.Enabled=enabled;
            _notificationHint.Text=enabled ? "测试使用当前已保存的提醒设置" : "提醒已关闭，阈值与提示音暂不生效";
        }
        private void SaveAndClose() {
            var updated=_source.Clone();
            updated.PollSeconds=_pollSeconds.Value;updated.ShowDisconnected=_showDisconnected.Checked;
            updated.NotificationsEnabled=_notifications.Checked;updated.LowBatteryThreshold=_threshold.Value;
            updated.NotificationSound=_sound.Checked;updated.StartMinimized=_startMinimized.Checked;updated.StartWithWindows=_startup.Checked;updated.Normalize();
            try { if(_save!=null) _save(updated);DialogResult=DialogResult.OK;Close(); }
            catch(Exception ex) { using(var form=new ErrorDetailsForm("保存设置失败：\r\n"+ex.Message)) form.ShowDialog(this); }
        }
    }
}
