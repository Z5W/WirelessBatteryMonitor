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
        private readonly StatusPill _pending;
        private bool _hasScan, _layingOut;
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
            MaximizeBox=false;
            SuspendLayout();
            _refresh = refresh;
            Text = "WirelessBatteryMoniter";
            AutoScaleDimensions = new SizeF(96,96); AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(520,720); MinimumSize = SizeFromClientSize(new Size(420,560));
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Theme.Background; ForeColor = Theme.Text; Font = Theme.Font(10.5f);
            DoubleBuffered = true; KeyPreview = true;
            Icon = AppIcon.Load(32);
            _header = new Panel { Width = 520, Dock = DockStyle.Top, Height = 140 };
            var logo = new PictureBox { Location = new Point(24,24), Size = new Size(40,40), SizeMode = PictureBoxSizeMode.Zoom };
            logo.Image = AppIcon.LoadLogo();
            _header.Controls.Add(logo);
            AddLabel(_header,"无线设备电量",76,19,230,30,15,Theme.Text,FontStyle.Bold);
            AddLabel(_header,"WirelessBatteryMoniter",76,51,240,22,10.5f,Theme.TextMuted,FontStyle.Regular);
            var settings = Button(_header,"设置",424,26,76,40); settings.Anchor = AnchorStyles.Top|AnchorStyles.Right; settings.Quiet = false;
            settings.Click += (s,e) => openSettings();
            var summary=new FlowLayoutPanel { Location=new Point(20,92),Size=new Size(480,36),Anchor=AnchorStyles.Top|AnchorStyles.Left|AnchorStyles.Right,WrapContents=false };
            _count=new StatusPill { Text="正在查找设备…",Size=new Size(140,30),Fill=Theme.AccentDark,ForeColor=Theme.Accent,Margin=new Padding(0,0,8,0) };
            _low=new StatusPill { Size=new Size(108,30),Fill=Color.FromArgb(255,240,218),ForeColor=Theme.Amber,Margin=new Padding(0,0,8,0),Visible=false };
            _pending=new StatusPill { Size=new Size(108,30),Margin=Padding.Empty,Visible=false };
            summary.Controls.Add(_count);summary.Controls.Add(_low);summary.Controls.Add(_pending);_header.Controls.Add(summary);
            _footer = new Panel { Width = 520, Dock = DockStyle.Bottom, Height = 116, BackColor = Theme.Background };
            _scan = AddLabel(_footer,"等待首次扫描…",20,15,300,22,9.75f,Theme.TextMuted,FontStyle.Regular);
            AddLabel(_footer,"关闭窗口后继续在托盘运行",20,40,300,22,9.75f,Theme.TextMuted,FontStyle.Regular);
            var about = Button(_footer,"关于",12,72,64,40); about.Quiet = true;
            about.Click += (s,e) => { using(var form = new AboutForm()) form.ShowDialog(this); };
            var exit = Button(_footer,"退出程序",80,72,96,40); exit.Quiet = true;
            exit.Click += (s,e) => { if (ExitRequested != null) ExitRequested(this,EventArgs.Empty); };
            _refreshButton = Button(_footer,"立即刷新",376,70,124,40); _refreshButton.Accent = true;
            _refreshButton.Anchor = AnchorStyles.Top|AnchorStyles.Right;
            _refreshButton.Click += async (s,e) => await RefreshAsync();
            _notice = new RoundedPanel { Width = 520, Dock = DockStyle.Top, Height = 64, Fill = Color.FromArgb(255,240,218),Radius=16, Visible = false };
            _message = AddLabel(_notice,"部分设备读取失败",24,19,260,24,10.5f,Theme.Amber,FontStyle.Regular);
            _details = Button(_notice,"查看详情",400,12,96,40); _details.Quiet = true; _details.Anchor = AnchorStyles.Top|AnchorStyles.Right;
            _details.Click += (s,e) => { using(var form = new ErrorDetailsForm(_snapshot.Error)) form.ShowDialog(this); };
            var body = new Panel { Dock = DockStyle.Fill, Padding = new Padding(20,0,8,0) };
            _list = new FlowLayoutPanel { Dock = DockStyle.Fill, Visible=false, AutoScroll = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = true };
            _empty = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, ForeColor = Theme.TextMuted, Font = Theme.Font(12), Text = "正在查找设备…" };
            body.Controls.Add(_list); body.Controls.Add(_empty);
            Controls.Add(body); Controls.Add(_notice); Controls.Add(_header); Controls.Add(_footer);
            _list.SizeChanged += (s,e) => ResizeRows();
            Shown += (s,e) => { ResizeRows(); };
            VisibleChanged += (s,e) => { if(Visible && _hasScan) { foreach(var d in _snapshot.Devices.Reverse()) { DeviceCard row;if(_rows.TryGetValue(d.Id,out row))_list.Controls.SetChildIndex(row,0); } ResizeRows(); } };
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
            snapshot = (settings ?? _settings).DisplaySnapshot(snapshot);
            _snapshot=snapshot; _settings=settings ?? _settings;
            _hasScan=true;
            int confirmed=snapshot.ConnectedCount;
            int pending=snapshot.PendingCount;
            _count.Text=confirmed+" 台已连接"+(snapshot.IsDemo ? " · 演示" : "");
            int low=snapshot.Devices.Count(d=>d.IsConnected && !d.IsStale && !d.IsSleeping && !d.IsChargingOrCharged && d.Level.HasValue && d.Level.Value<=_settings.ThresholdFor(d.Id));
            _low.Text=low+" 台电量偏低";_low.Visible=low>0;
            _pending.Text=pending+" 台待确认";_pending.Visible=pending>0;
            _notice.Visible=!string.IsNullOrEmpty(snapshot.Error);
            _message.Text=snapshot.HasDevices ? "部分设备读取失败" : "设备读取失败";
            _scan.Text=(string.IsNullOrEmpty(snapshot.Error) ? "上轮扫描完成于 " : "上轮扫描异常于 ")+snapshot.Timestamp.ToString("HH:mm:ss");
            var devices=snapshot.Devices.Where(d=>_settings.ShowDisconnected || !d.IsDisconnected).ToList();
            var ids=new HashSet<string>(devices.Select(d=>d.Id),StringComparer.OrdinalIgnoreCase);
            _list.SuspendLayout();
            foreach(var id in _rows.Keys.Where(id=>!ids.Contains(id)).ToList()) { var row=_rows[id]; _list.Controls.Remove(row);row.Dispose();_rows.Remove(id); }
            // Existing controls keep their position; newly discovered devices are appended.
            foreach(var d in devices) {
                DeviceCard row;
                if(!_rows.TryGetValue(d.Id,out row)) { row=new DeviceCard();row.OpenDetails+=(s,e)=> { var card=(DeviceCard)s;using(var form=new DeviceDetailsForm(card.Device))form.ShowDialog(this);card.Focus(); };_rows.Add(d.Id,row);_list.Controls.Add(row); }
                row.UpdateDevice(d,_settings.ThresholdFor(d.Id));
            }
            _list.ResumeLayout(); ResizeRows();
            _list.Visible=devices.Count>0; _empty.Visible=devices.Count==0;
            _empty.Text="尚未发现支持读取电量的设备\r\n\r\n请检查设备连接，然后重新扫描";
            if(devices.Count==0 && _settings.Devices.Any(d=>d.Hidden))_empty.Text="暂无可显示设备\r\n\r\n可从托盘菜单的逐设备设置恢复隐藏设备";
            SetBusy(_busy);
        }
        private void ResizeRows() {
            if(_layingOut)return;_layingOut=true;
            try {
                int columns=ClientSize.Width>=800*Theme.DpiScale ? 2 : 1;
                _list.FlowDirection=columns==1 ? FlowDirection.TopDown : FlowDirection.LeftToRight;
                _list.WrapContents=columns==2;
                int gap=(int)(12*Theme.DpiScale);
                int width=Math.Max(100,(_list.ClientSize.Width-SystemInformation.VerticalScrollBarWidth)/columns-gap);
                _list.SuspendLayout();foreach(Control row in _list.Controls)row.Width=width;
                if(columns==2) for(int i=0;i+1<_list.Controls.Count;i+=2) { int height=Math.Max(_list.Controls[i].Height,_list.Controls[i+1].Height);_list.Controls[i].Height=height;_list.Controls[i+1].Height=height; }
                _list.ResumeLayout(true);
            } finally {_layingOut=false;}
        }
        public void SetBusy(bool busy) {
            _busy=busy;_refreshButton.Enabled=!busy;_refreshButton.Text=busy ? "正在读取…" : (_rows.Count==0 ? "重新扫描" : "立即刷新");
            if(busy && !_hasScan) {_empty.Text="正在查找设备…";_count.Text="正在查找设备…";}
        }
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




