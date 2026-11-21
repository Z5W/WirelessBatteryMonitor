using System;
using System.Drawing;
using System.Windows.Forms;
using WirelessBatteryMoniter.Models;
using WirelessBatteryMoniter.Services;
namespace WirelessBatteryMoniter.UI
{
    internal sealed class SettingsForm : Form
    {
        private readonly AppSettings _source;
        private readonly Action<AppSettings> _save;
        private readonly NumberStepper _pollSeconds,_threshold;
        private readonly ToggleSwitch _showDisconnected,_notifications,_sound,_startup,_startMinimized;
        private readonly FlatButton _testButton;
        private readonly Label _notificationHint;
        private readonly FlowLayoutPanel _content;
        public SettingsForm(AppSettings settings,Action<AppSettings> save,Action test) {
            SuspendLayout();_source=settings.Clone();_save=save;
            Text="设置 · WirelessBatteryMoniter";AutoScaleMode=AutoScaleMode.Dpi;AutoScaleDimensions=new SizeF(96,96);
            ClientSize=new Size(480,680);MinimumSize=SizeFromClientSize(new Size(420,500));MaximizeBox=false;MinimizeBox=false;ShowInTaskbar=false;
            StartPosition=FormStartPosition.CenterParent;BackColor=Theme.Background;ForeColor=Theme.Text;Font=Theme.Font(10.5f);KeyPreview=true;
            var header=new Panel { Width=480,Dock=DockStyle.Top,Height=88 };
            MainForm.AddLabel(header,"设置",20,18,400,30,15,Theme.Text,FontStyle.Bold);
            MainForm.AddLabel(header,"设备监控、提醒与后台运行",20,52,420,24,10.5f,Theme.TextMuted,FontStyle.Regular);
            var footer=new Panel { Width=480,Dock=DockStyle.Bottom,Height=80 };
            var cancel=MainForm.Button(footer,"取消",232,20,100,40);cancel.Anchor=AnchorStyles.Top|AnchorStyles.Right;cancel.Click+=(s,e)=>{DialogResult=DialogResult.Cancel;Close();};
            var apply=MainForm.Button(footer,"保存设置",344,20,116,40);apply.Accent=true;apply.Anchor=AnchorStyles.Top|AnchorStyles.Right;apply.Click+=(s,e)=>SaveAndClose();
            _content=new FlowLayoutPanel { Dock=DockStyle.Fill,AutoScroll=true,FlowDirection=FlowDirection.TopDown,WrapContents=false,Padding=new Padding(20,0,8,0) };
            Controls.Add(_content);Controls.Add(header);Controls.Add(footer);
            _pollSeconds=Number("刷新间隔",5,120,_source.PollSeconds," 秒");
            _threshold=Number("电量阈值",5,95,_source.LowBatteryThreshold," %");
            _showDisconnected=Toggle("显示未连接设备",_source.ShowDisconnected);
            _notifications=Toggle("启用桌面提醒",_source.NotificationsEnabled);
            _sound=Toggle("提示音",_source.NotificationSound);
            _startup=Toggle("开机自动启动",StartupService.IsEnabled());
            _startMinimized=Toggle("启动时最小化",_source.StartMinimized);
            var monitor=new SettingsGroup("设备监控");
            monitor.AddRow("刷新间隔","两次设备扫描之间的间隔",_pollSeconds);
            monitor.AddRow("显示未连接设备","保留设备与上次有效读数",_showDisconnected);
            var alerts=new SettingsGroup("低电量提醒");
            alerts.AddRow("启用桌面提醒","电量达到阈值时发送提醒",_notifications);
            alerts.AddRow("电量阈值","达到或低于该值时提醒",_threshold);
            alerts.AddRow("提示音","显示通知时播放系统提示音",_sound);
            _testButton=new FlatButton {Text="发送测试提醒",Size=new Size(148,40)};_testButton.Click+=(s,e)=>{if(test!=null)test();};
            _notificationHint=alerts.AddRow("测试提醒","测试使用当前已保存的提醒设置",_testButton);
            var startup=new SettingsGroup("启动与后台");
            startup.AddRow("开机自动启动","登录 Windows 后自动运行",_startup);
            startup.AddRow("启动时最小化","启动后直接进入系统托盘",_startMinimized);
            _content.Controls.Add(monitor);_content.Controls.Add(alerts);_content.Controls.Add(startup);
            _content.SizeChanged+=(s,e)=>ResizeGroups();
            _notifications.CheckedChanged+=(s,e)=>UpdateNotificationState();
            AutoScaleDimensions=new SizeF(96,96);ResumeLayout(true);UpdateNotificationState();
            Shown+=(s,e)=>ResizeGroups();
            KeyDown+=(s,e)=>{if(e.KeyCode==Keys.Escape){DialogResult=DialogResult.Cancel;Close();}};
        }
        private void ResizeGroups() {foreach(Control group in _content.Controls)group.Width=Math.Max(200,_content.ClientSize.Width-_content.Padding.Horizontal-SystemInformation.VerticalScrollBarWidth);}
        private ToggleSwitch Toggle(string name,bool value) {return new ToggleSwitch {AccessibleName=name,Checked=value,Size=new Size(48,44)};}
        private NumberStepper Number(string name,int min,int max,int value,string suffix) {return new NumberStepper {AccessibleName=name,Minimum=min,Maximum=max,Value=value,Suffix=suffix,Size=new Size(148,40)};}
        private void UpdateNotificationState() {
            bool enabled=_notifications.Checked;_threshold.Enabled=_sound.Enabled=_testButton.Enabled=enabled;
            _notificationHint.Text=enabled ? "测试使用当前已保存的提醒设置" : "提醒已关闭，阈值与提示音暂不生效";
            _notificationHint.Parent.PerformLayout();_notificationHint.Parent.Parent.PerformLayout();
        }
        private void SaveAndClose() {
            var updated=_source.Clone();updated.PollSeconds=_pollSeconds.Value;updated.ShowDisconnected=_showDisconnected.Checked;
            updated.NotificationsEnabled=_notifications.Checked;updated.LowBatteryThreshold=_threshold.Value;updated.NotificationSound=_sound.Checked;
            updated.StartMinimized=_startMinimized.Checked;updated.StartWithWindows=_startup.Checked;updated.Normalize();
            try{if(_save!=null)_save(updated);DialogResult=DialogResult.OK;Close();}
            catch(Exception ex){using(var form=new ErrorDetailsForm("保存设置失败：\r\n"+ex.Message))form.ShowDialog(this);}
        }
    }
    internal sealed class SettingsGroup : RoundedPanel
    {
        private readonly Label _title;
        public SettingsGroup(string title) {Width=440;_title=new Label {Text=title,Font=Theme.Font(12,FontStyle.Bold),ForeColor=Theme.Text,BackColor=Color.Transparent};Controls.Add(_title);}
        public Label AddRow(string title,string description,Control editor) {var row=new SettingsRow(title,description,editor);Controls.Add(row);PerformLayout();return row.Description;}
        protected override void OnLayout(LayoutEventArgs e) {
            base.OnLayout(e);if(_title==null)return;int pad=(int)(20*Theme.DpiScale);
            _title.SetBounds(pad,pad,Width-pad*2,(int)(28*Theme.DpiScale));int y=_title.Bottom+(int)(12*Theme.DpiScale);
            foreach(Control control in Controls) {if(control==_title)continue;control.SetBounds(pad,y,Width-pad*2,control.Height);control.PerformLayout();y+=control.Height;}
            Height=y+pad;Margin=new Padding(0,0,0,(int)(16*Theme.DpiScale));
        }
    }
    internal sealed class SettingsRow : Panel
    {
        private readonly Label _title;
        private readonly Control _editor;
        public Label Description {get;private set;}
        public SettingsRow(string title,string description,Control editor) {
            BackColor=Color.Transparent;_editor=editor;
            _title=new Label {Text=title,Font=Theme.Font(10.5f),ForeColor=Theme.Text,BackColor=Color.Transparent};
            Description=new Label {Text=description,Font=Theme.Font(9.75f),ForeColor=Theme.TextMuted,BackColor=Color.Transparent};
            Controls.Add(_title);Controls.Add(Description);Controls.Add(editor);
        }
        protected override void OnLayout(LayoutEventArgs e) {
            base.OnLayout(e);if(Description==null||_editor==null)return;
            int gap=(int)(12*Theme.DpiScale),editorWidth=(int)((_editor is ToggleSwitch ? 48 : 148)*Theme.DpiScale);
            int available=Math.Max(60,Width-editorWidth-gap);
            int titleHeight=TextRenderer.MeasureText(_title.Text,_title.Font,new Size(available,int.MaxValue),TextFormatFlags.WordBreak).Height;
            int descriptionHeight=TextRenderer.MeasureText(Description.Text,Description.Font,new Size(available,int.MaxValue),TextFormatFlags.WordBreak).Height;
            _title.SetBounds(0,0,available,Math.Max((int)(22*Theme.DpiScale),titleHeight));
            Description.SetBounds(0,_title.Bottom+(int)(4*Theme.DpiScale),available,Math.Max((int)(20*Theme.DpiScale),descriptionHeight));
            _editor.SetBounds(Width-editorWidth,0,editorWidth,(int)((_editor is ToggleSwitch ? 44 : 40)*Theme.DpiScale));
            Height=Math.Max((int)(64*Theme.DpiScale),Math.Max(Description.Bottom,_editor.Bottom)+gap);
        }
    }
}
