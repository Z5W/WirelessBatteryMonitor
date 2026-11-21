using System;
using System.Drawing;
using System.Windows.Forms;

namespace WirelessBatteryMoniter.UI
{
    internal class RoundedPanel : Panel
    {
        public Color Fill { get; set; }
        public int Radius { get; set; }
        public RoundedPanel() { DoubleBuffered=true; Fill=Theme.Surface; Radius=24; BackColor=Theme.Background; }
        protected override void OnPaintBackground(PaintEventArgs e) {
            e.Graphics.Clear(Parent==null ? Theme.Background : Parent.BackColor);
            Theme.Prepare(e.Graphics);
            using(var brush=new SolidBrush(Fill)) Theme.FillRounded(e.Graphics,brush,new RectangleF(0,0,Width,Height),Radius*Theme.DpiScale);
        }
    }
    internal sealed class StatusPill : Label
    {
        public Color Fill { get; set; }
        public StatusPill() { Fill=Theme.SurfaceRaised;ForeColor=Theme.TextMuted;Font=Theme.Font(9.75f);TextAlign=ContentAlignment.MiddleCenter;UseMnemonic=false; }
        protected override void OnPaint(PaintEventArgs e) {
            e.Graphics.Clear(Parent==null ? Theme.Background : Parent.BackColor);
            Theme.Prepare(e.Graphics);
            using(var brush=new SolidBrush(Fill)) Theme.FillRounded(e.Graphics,brush,new RectangleF(0,0,Width,Height),Height/2f);
            Theme.DrawText(e.Graphics,Text,Font,ForeColor,ClientRectangle,Theme.CenterMiddle);
        }
    }
}
