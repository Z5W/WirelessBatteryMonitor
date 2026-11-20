using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using WirelessBatteryMoniter.Interop;
using WirelessBatteryMoniter.Models;

namespace WirelessBatteryMoniter.UI
{
    internal sealed class MainForm : Form
    {
        private readonly Func<Task> _refresh;
        private readonly Panel _header, _footer, _notice;
        private readonly Label _count, _low, _scan, _message;
        private readonly FlowLayoutPanel _list;
        private readonly Label _empty;
        private readonly FlatButton _refreshButton, _details;
        private readonly Dictionary<string, DeviceCard> _rows = new Dictionary<string, DeviceCard>(StringComparer.OrdinalIgnoreCase);
        private BatterySnapshot _snapshot = BatterySnapshot.Empty(false);
        private AppSettings _settings = new AppSettings();
        private bool _allowClose, _busy, _trayHintShown;
        public event EventHandler MinimizedToTray;
        public event EventHandler ExitRequested;
        public MainForm(Action openSettings, Func<Task> refresh)
        {
            SuspendLayout();
            _refresh = refresh;
            Text = "WirelessBatteryMoniter";
            AutoScaleDimensions = new SizeF(96,96); AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(520,680); MinimumSize = new Size(480,540);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Theme.Background; ForeColor = Theme.Text; Font = Theme.Font(10.5f);
            DoubleBuffered = true; KeyPreview = true;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
            _header = new Panel { Width = 520, Dock = DockStyle.Top, Height = 124 };
            var logo = new PictureBox { Location = new Point(24,24), Size = new Size(40,40), SizeMode = PictureBoxSizeMode.Zoom };
            if (Icon != null) logo.Image = Icon.ToBitmap();
            _header.Controls.Add(logo);
            AddLabel(_header,"无线设备电量",80,19,310,30,15,Theme.Text,FontStyle.Bold);
            AddLabel(_header,"WirelessBatteryMoniter",80,51,310,22,10.5f,Theme.TextMuted,FontStyle.Regular);
            var settings = Button(_header,"设置",424,26,72,36); settings.Anchor = AnchorStyles.Top|AnchorStyles.Right; settings.Quiet = true;
            settings.Click += (s,e) => openSettings();
            _count = AddLabel(_header,"等待首次扫描",24,92,290,24,12,Theme.Text,FontStyle.Bold);
            _low = AddLabel(_header,"",320,92,176,24,10.5f,Theme.Amber,FontStyle.Regular);
            _low.Anchor = AnchorStyles.Top|AnchorStyles.Right; _low.TextAlign = ContentAlignment.MiddleRight;
            _footer = new Panel { Width = 520, Dock = DockStyle.Bottom, Height = 116, BackColor = Theme.Surface };
            _scan = AddLabel(_footer,"等待首次扫描…",24,15,335,22,9.75f,Theme.TextMuted,FontStyle.Regular);
            AddLabel(_footer,"关闭窗口后继续在托盘运行",24,40,340,22,9.75f,Theme.TextMuted,FontStyle.Regular);
            var about = Button(_footer,"关于",16,72,64,32); about.Quiet = true;
            about.Click += (s,e) => { using(var form = new AboutForm()) form.ShowDialog(this); };
            var exit = Button(_footer,"退出",84,72,64,32); exit.Quiet = true;
            exit.Click += (s,e) => { if (ExitRequested != null) ExitRequested(this,EventArgs.Empty); };
            _refreshButton = Button(_footer,"立即刷新",376,38,120,40); _refreshButton.Accent = true;
            _refreshButton.Anchor = AnchorStyles.Top|AnchorStyles.Right;
            _refreshButton.Click += async (s,e) => await RefreshAsync();
            _notice = new Panel { Width = 520, Dock = DockStyle.Top, Height = 44, BackColor = Theme.Surface, Visible = false };
            _message = AddLabel(_notice,"部分设备读取失败",24,10,340,24,10.5f,Theme.Amber,FontStyle.Regular);
            _details = Button(_notice,"查看详情",400,4,96,36); _details.Quiet = true; _details.Anchor = AnchorStyles.Top|AnchorStyles.Right;
            _details.Click += (s,e) => { using(var form = new ErrorDetailsForm(_snapshot.Error)) form.ShowDialog(this); };
            var body = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8,0,8,0) };
            _list = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };
            _empty = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, ForeColor = Theme.TextMuted, Font = Theme.Font(12), Text = "尚未发现支持读取电量的设备\r\n\r\n连接设备后，点击下方“重新扫描”" };
            body.Controls.Add(_list); body.Controls.Add(_empty);
            Controls.Add(body); Controls.Add(_notice); Controls.Add(_header); Controls.Add(_footer);
            _list.SizeChanged += (s,e) => ResizeRows();
            Shown += (s,e) => { NativeMethods.ApplyDarkWindow(Handle); ResizeRows(); };
            FormClosing += (s,e) => {
                if (_allowClose || e.CloseReason == CloseReason.WindowsShutDown || e.CloseReason == CloseReason.TaskManagerClosing) return;
                e.Cancel = true; Hide();
                if (!_trayHintShown) { _trayHintShown = true; if (MinimizedToTray != null) MinimizedToTray(this,EventArgs.Empty); }
            };
            KeyDown += (s,e) => { if (e.KeyCode == Keys.Escape) Close(); };
            AutoScaleDimensions = new SizeF(96,96); ResumeLayout(true);
            Disposed += (s,e) => { if (logo.Image != null) logo.Image.Dispose(); };
        }
        internal static Label AddLabel(Control parent,string text,int x,int y,int w,int h,float size,Color color,FontStyle style)
        {
            var label = new Label { Text=text, Location=new Point(x,y),Size=new Size(w,h),Font=Theme.Font(size,style),ForeColor=color,UseMnemonic=false };
            parent.Controls.Add(label); return label;
        }
        internal static FlatButton Button(Control parent,string text,int x,int y,int w,int h)
        {
            var b = new FlatButton { Text=text,Location=new Point(x,y),Size=new Size(w,h),TabIndex=parent.Controls.Count };
            parent.Controls.Add(b); return b;
        }
        public void UpdateSnapshot(BatterySnapshot snapshot,AppSettings settings)
        {
            if(snapshot == null) return;
            _snapshot=snapshot; _settings=settings ?? _settings;
            _count.Text=snapshot.ConnectedCount+" 台已连接"+(snapshot.IsDemo ? " · 演示" : "");
            int low=snapshot.Devices.Count(d=>d.IsConnected && !d.IsStale && d.Level.HasValue && d.Level.Value<=_settings.LowBatteryThreshold);
            _low.Text=low>0 ? low+" 台电量偏低" : "电量状态良好";
            _low.ForeColor=low>0 ? Theme.Amber : Theme.TextMuted;
            if(low==0 && snapshot.Devices.Any(d=>d.IsConnected && (d.IsStale || !d.HasLevel))) _low.Text="部分读数待更新";
            if(snapshot.KnownBatteryCount==0) _low.Text="等待有效读数";
            _notice.Visible=!string.IsNullOrEmpty(snapshot.Error);
            _message.Text=snapshot.HasDevices ? "部分设备读取失败" : "设备读取失败";
            _scan.Text="上轮扫描完成于 "+snapshot.Timestamp.ToString("HH:mm:ss");
            var devices=snapshot.Devices.Where(d=>_settings.ShowDisconnected || d.IsConnected).ToList();
            var ids=new HashSet<string>(devices.Select(d=>d.Id),StringComparer.OrdinalIgnoreCase);
            _list.SuspendLayout();
            foreach(var id in _rows.Keys.Where(id=>!ids.Contains(id)).ToList()) { var row=_rows[id]; _list.Controls.Remove(row);row.Dispose();_rows.Remove(id); }
            // Existing controls keep their position; newly discovered devices are appended.
            foreach(var d in devices) {
                DeviceCard row;
                if(!_rows.TryGetValue(d.Id,out row)) { row=new DeviceCard();_rows.Add(d.Id,row);_list.Controls.Add(row); }
                row.UpdateDevice(d,_settings.LowBatteryThreshold);
            }
            _list.ResumeLayout(); ResizeRows();
            _list.Visible=devices.Count>0; _empty.Visible=devices.Count==0;
            SetBusy(_busy);
        }
        private void ResizeRows() {
            int width=Math.Max(100,_list.ClientSize.Width-SystemInformation.VerticalScrollBarWidth-2);
            foreach(var row in _rows.Values) row.Width=width;
        }
        public void SetBusy(bool busy) { _busy=busy;_refreshButton.Enabled=!busy;_refreshButton.Text=busy ? "正在读取…" : (_rows.Count==0 ? "重新扫描" : "立即刷新"); }
        private async Task RefreshAsync() {
            if(_busy) return;
            SetBusy(true);
            try { await _refresh(); }
            catch(Exception ex) { Services.AppLog.Error("Manual refresh failed.",ex); using(var f=new ErrorDetailsForm(ex.Message)) f.ShowDialog(this); }
            finally { if(!IsDisposed) SetBusy(false); }
        }
        public void CloseForExit() { _allowClose=true; Close(); }
    }
}
