using System;
using System.Drawing;
using System.Windows.Forms;
using WirelessBatteryMoniter.Models;

namespace WirelessBatteryMoniter.UI
{
    internal sealed class DeviceCard : UserControl
    {
        private DeviceBatteryInfo _device;
        private readonly Label _name, _level, _percent, _kind, _icon, _time;
        private readonly StatusPill _state;
        private readonly ToolTip _tip=new ToolTip { AutoPopDelay=30000 };
        private int _threshold, _barY;
        private readonly UiMotion _motion;
        public event EventHandler OpenDetails;
        public DeviceCard() {
            _motion=new UiMotion(()=>Invalidate(true));
            DoubleBuffered=true;BackColor=Theme.Surface;TabStop=true;Cursor=Cursors.Hand;
            SetStyle(ControlStyles.Selectable,true);
            AccessibleRole=AccessibleRole.PushButton;
            _name=Label(12,FontStyle.Bold);_name.AutoEllipsis=true;
            _level=Label(24,FontStyle.Bold);_level.TextAlign=ContentAlignment.MiddleRight;
            _percent=Label(12,FontStyle.Regular);_percent.Text="%";
            _kind=Label(9.75f,FontStyle.Regular);_kind.ForeColor=Theme.TextMuted;
            _icon=Label(16.5f,FontStyle.Regular);_icon.Font=Theme.IconFont(16.5f);_icon.ForeColor=Theme.Accent;_icon.TextAlign=ContentAlignment.MiddleCenter;
            _state=new StatusPill();Controls.Add(_state);
            _time=Label(9.75f,FontStyle.Regular);_time.ForeColor=Theme.TextMuted;
            foreach(Control child in Controls) {
                child.MouseClick+=(s,e)=>{if(e.Button==MouseButtons.Left)ActivateCard();};
                child.MouseEnter+=(s,e)=>SetHover(true);child.MouseLeave+=(s,e)=>SetHover(ClientRectangle.Contains(PointToClient(Cursor.Position)));
            }
        }
        private Label Label(float size,FontStyle style) {var l=new Label { Font=Theme.Font(size,style),ForeColor=Theme.Text,BackColor=Color.Transparent,UseMnemonic=false };Controls.Add(l);return l;}
        public DeviceBatteryInfo Device { get { return _device; } }
        public void UpdateDevice(DeviceBatteryInfo device,int threshold) {
            _device=device;_threshold=threshold;
            _name.Text=device.Name;
            _level.Text=device.HasLevel ? device.Level.Value.ToString() : "—";_percent.Visible=device.HasLevel;
            bool cached=device.IsStale || !device.IsConnected;
            Color color=cached ? Theme.TextMuted : Theme.BatteryColor(device.Level,threshold);
            _level.ForeColor=_percent.ForeColor=color;
            _kind.Text=device.Kind;
            string type=TrayTextFormatter.DeviceType(device.Name);
            _icon.Text=type=="鼠标" ? "\uE962" : type=="键盘" ? "\uE765" : type=="耳机" ? "\uE7F6" : type=="手柄" ? "\uE7FC" : "\uE770";
            _state.Text=!device.IsConnected ? "未连接" : device.IsStale ? "连接待确认" : !device.HasLevel ? "暂未获取电量" : device.Level<=10 ? "电量很低" : device.Level<=threshold ? "电量偏低" : "已连接";
            if(cached && device.HasLevel) _state.Text+=" · 缓存读数";
            _state.Fill=cached ? Theme.SurfaceRaised : device.HasLevel && device.Level<=10 ? Color.FromArgb(252,232,230) : device.HasLevel && device.Level<=threshold ? Color.FromArgb(255,240,218) : Theme.Surface;
            _state.ForeColor=cached ? Theme.TextMuted : device.HasLevel && device.Level<=threshold ? color : Theme.TextMuted;
            _time.Text=device.HasLevel ? (cached ? "上次读数 "+device.LevelText+" · " : "")+"读取于 "+device.LastSeen.ToString("HH:mm:ss") : "等待首次有效读数";
            _tip.SetToolTip(_name,device.Name);_tip.SetToolTip(this,"查看设备详情");
            AccessibleName=device.Name+" "+device.LevelText+" "+_state.Text+" "+_time.Text;
            PerformLayout();Invalidate();
        }
        protected override void OnLayout(LayoutEventArgs e) {
            base.OnLayout(e);if(_time==null)return;
            float s=Theme.DpiScale;int pad=(int)(20*s),gap=(int)(12*s),icon=(int)(40*s);
            int valueWidth=(int)(100*s), nameX=pad+icon+gap;
            int nameWidth=Math.Max(40,Width-pad-nameX-valueWidth);
            int nameHeight=Math.Min((int)(48*s),Math.Max((int)(24*s),TextRenderer.MeasureText(_name.Text,_name.Font,new Size(nameWidth,int.MaxValue),TextFormatFlags.WordBreak|TextFormatFlags.NoPrefix).Height));
            _icon.SetBounds(pad,pad,icon,icon);
            _name.SetBounds(nameX,pad,nameWidth,nameHeight);
            _kind.SetBounds(nameX,_name.Bottom+(int)(3*s),nameWidth,(int)(22*s));
            _level.SetBounds(Width-pad-valueWidth,pad-2,valueWidth-(int)(22*s),(int)(40*s));
            _percent.SetBounds(_level.Right,pad+(int)(14*s),(int)(22*s),(int)(24*s));
            _barY=Math.Max(_kind.Bottom,_icon.Bottom)+(int)(14*s);
            int y=_barY+(int)(20*s);
            int stateWidth=Math.Min(Width-pad*2,TextRenderer.MeasureText(_state.Text,_state.Font).Width+(int)(20*s));
            int timeWidth=TextRenderer.MeasureText(_time.Text,_time.Font).Width;
            bool stack=Width<440*s || stateWidth+timeWidth+gap>Width-pad*2;
            _state.SetBounds(pad,y,stateWidth,(int)(28*s));
            _time.TextAlign=stack ? ContentAlignment.MiddleLeft : ContentAlignment.MiddleRight;
            _time.SetBounds(stack ? pad : pad+stateWidth+gap,stack ? _state.Bottom+(int)(6*s) : y,stack ? Width-pad*2 : Width-pad*2-stateWidth-gap,(int)(28*s));
            Height=Math.Max((int)(144*s),_time.Bottom+pad);
            Margin=new Padding(0,0,(int)(12*s),(int)(12*s));
        }
        protected override void OnPaintBackground(PaintEventArgs e) {
            e.Graphics.Clear(Theme.Background);Theme.Prepare(e.Graphics);
            using(var b=new SolidBrush(UiMotion.Blend(Theme.Surface,Theme.SurfaceHover,_motion==null ? 0 : _motion.Value))) Theme.FillRounded(e.Graphics,b,new RectangleF(3,3,Width-6,Height-6),24*Theme.DpiScale);
        }
        protected override void OnPaint(PaintEventArgs e) {
            base.OnPaint(e);if(_device==null)return;float s=Theme.DpiScale;Theme.Prepare(e.Graphics);
            using(var b=new SolidBrush(Theme.SurfaceRaised)) Theme.FillRounded(e.Graphics,b,_icon.Bounds,14*s);
            Theme.DrawBatteryBar(e.Graphics,new RectangleF(20*s,_barY,Width-40*s,8*s),_device.Level,_device.IsStale||!_device.IsConnected ? Theme.TextMuted : Theme.BatteryColor(_device.Level,_threshold),Theme.SurfaceRaised);
            if(Focused && ShowFocusCues) using(var p=new Pen(Theme.Accent,2*s)) Theme.DrawRounded(e.Graphics,p,new RectangleF(2*s,2*s,Width-4*s,Height-4*s),24*s);
        }
        protected override AccessibleObject CreateAccessibilityInstance() {return new CardAccessibility(this);}
        private sealed class CardAccessibility : ControlAccessibleObject {
            private readonly DeviceCard _owner;
            public CardAccessibility(DeviceCard owner):base(owner){_owner=owner;}
            public override string DefaultAction {get{return "查看设备详情";}}
            public override void DoDefaultAction(){_owner.ActivateCard();}
        }
        private void SetHover(bool hover) {_motion.Set(hover ? 1 : 0);}
        private void ActivateCard() {Focus();if(OpenDetails!=null)OpenDetails(this,EventArgs.Empty);}
        protected override void OnMouseClick(MouseEventArgs e) {base.OnMouseClick(e);if(e.Button==MouseButtons.Left)ActivateCard();}
        protected override void OnMouseEnter(EventArgs e) {base.OnMouseEnter(e);SetHover(true);}
        protected override void OnMouseLeave(EventArgs e) {base.OnMouseLeave(e);SetHover(ClientRectangle.Contains(PointToClient(Cursor.Position)));}
        protected override void OnGotFocus(EventArgs e) {base.OnGotFocus(e);Invalidate();}
        protected override void OnLostFocus(EventArgs e) {base.OnLostFocus(e);Invalidate();}
        protected override void OnKeyDown(KeyEventArgs e) {if(e.KeyCode==Keys.Enter||e.KeyCode==Keys.Space){e.Handled=true;e.SuppressKeyPress=true;ActivateCard();}base.OnKeyDown(e);}
        protected override void Dispose(bool disposing) {if(disposing){_tip.Dispose();_motion.Dispose();}base.Dispose(disposing);}
    }
}




