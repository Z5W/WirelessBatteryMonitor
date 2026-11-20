using System;
using System.Drawing;
using System.Windows.Forms;
using WirelessBatteryMoniter.Models;

namespace WirelessBatteryMoniter.UI
{
    // A list row, with real controls for wrapping, accessibility and DPI scaling.
    internal sealed class DeviceCard : UserControl
    {
        private DeviceBatteryInfo _device;
        private readonly Label _name, _level, _state, _time;
        private readonly ToolTip _tip = new ToolTip { AutoPopDelay = 30000 };
        private int _threshold;
        public DeviceCard()
        {
            DoubleBuffered = true;
            BackColor = Theme.Background;
            Margin = Padding.Empty;
            AccessibleRole = AccessibleRole.ListItem;
            _name = MakeLabel(12, FontStyle.Bold);
            _level = MakeLabel(21, FontStyle.Bold);
            _level.TextAlign = ContentAlignment.TopRight;
            _state = MakeLabel(10.5f, FontStyle.Regular);
            _time = MakeLabel(9.75f, FontStyle.Regular);
            _state.ForeColor = _time.ForeColor = Theme.TextMuted;
        }
        private Label MakeLabel(float size, FontStyle style)
        {
            Label label = new Label { Font = Theme.Font(size, style), ForeColor = Theme.Text, UseMnemonic = false };
            Controls.Add(label); return label;
        }
        public DeviceBatteryInfo Device { get { return _device; } }
        public void UpdateDevice(DeviceBatteryInfo device, int threshold)
        {
            _device = device; _threshold = threshold;
            _name.Text = device.Name;
            _level.Text = device.HasLevel ? device.LevelText : "—";
            _level.ForeColor = device.IsStale || !device.IsConnected ? Theme.TextMuted : Theme.BatteryColor(device.Level, threshold);
            _state.Text = device.Kind + " · " + TrayTextFormatter.DeviceType(device.Name) + " · " + device.StateText;
            if (!device.HasLevel) _state.Text += " · 暂未获取电量";
            else if (!device.IsStale && device.IsConnected && device.Level <= threshold) _state.Text += " · 电量偏低";
            _time.Text = device.HasLevel ? (device.IsStale ? "上次读数 " + device.LevelText + " · " : "") + "最近成功读取：" + device.LastSeen.ToString("HH:mm:ss") : "等待首次有效读数";
            AccessibleName = device.Name + " " + _level.Text + " " + _state.Text + " " + _time.Text;
            _tip.SetToolTip(_name, device.Name);
            PerformLayout(); Invalidate();
        }
        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            if (_time == null) return;
            float s = Theme.DpiScale;
            int pad = (int)(16*s), gap = (int)(8*s), levelWidth = (int)(100*s);
            int nameWidth = Math.Max(80, Width - pad*2 - levelWidth - gap);
            int nameHeight = Math.Max((int)(28*s), TextRenderer.MeasureText(_name.Text, _name.Font, new Size(nameWidth, int.MaxValue), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix).Height);
            _name.SetBounds(pad, pad, nameWidth, nameHeight);
            _level.SetBounds(Width-pad-levelWidth, pad-3, levelWidth, (int)(40*s));
            int y = pad + Math.Max(nameHeight, (int)(36*s)) + gap;
            int stateHeight = Math.Max((int)(22*s), TextRenderer.MeasureText(_state.Text, _state.Font, new Size(Width-pad*2, int.MaxValue), TextFormatFlags.WordBreak).Height);
            _state.SetBounds(pad,y,Width-pad*2,stateHeight);
            _time.SetBounds(pad,y+stateHeight+(int)(17*s),Width-pad*2,(int)(22*s));
            Height = y+stateHeight+(int)(54*s);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (_device == null) return;
            float s = Theme.DpiScale;
            Theme.DrawBatteryBar(e.Graphics, new RectangleF(16*s,_state.Bottom+7*s,Width-32*s,4*s), _device.Level,
                _device.IsStale || !_device.IsConnected ? Theme.TextMuted : Theme.BatteryColor(_device.Level,_threshold), Theme.Border);
            using (Pen pen = new Pen(Theme.Border)) e.Graphics.DrawLine(pen,16*s,Height-1,Width-16*s,Height-1);
        }
        protected override void Dispose(bool disposing) { if (disposing) _tip.Dispose(); base.Dispose(disposing); }
    }
}
