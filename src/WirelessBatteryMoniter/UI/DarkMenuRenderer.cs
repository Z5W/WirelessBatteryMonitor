using System.Drawing;
using System.Windows.Forms;

namespace WirelessBatteryMoniter.UI
{
    internal sealed class DarkColorTable : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground { get { return Theme.Surface; } }
        public override Color MenuItemSelected { get { return Theme.SurfaceHover; } }
        public override Color MenuItemSelectedGradientBegin { get { return Theme.SurfaceHover; } }
        public override Color MenuItemSelectedGradientEnd { get { return Theme.SurfaceHover; } }
        public override Color MenuItemBorder { get { return Theme.Border; } }
        public override Color MenuBorder { get { return Theme.Border; } }
        public override Color ImageMarginGradientBegin { get { return Theme.Surface; } }
        public override Color ImageMarginGradientMiddle { get { return Theme.Surface; } }
        public override Color ImageMarginGradientEnd { get { return Theme.Surface; } }
        public override Color SeparatorDark { get { return Theme.BorderSoft; } }
        public override Color SeparatorLight { get { return Theme.BorderSoft; } }
        public override Color CheckBackground { get { return Theme.AccentDark; } }
        public override Color CheckSelectedBackground { get { return Theme.AccentDark; } }
    }

    internal sealed class DarkMenuRenderer : ToolStripProfessionalRenderer
    {
        public DarkMenuRenderer()
            : base(new DarkColorTable())
        {
            RoundedEdges = false;
        }

        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        {
            e.Graphics.Clear(Theme.Surface);
        }

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            Rectangle rect = new Rectangle(0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
            using (Pen pen = new Pen(Theme.Border))
            {
                e.Graphics.DrawRectangle(pen, rect);
            }
        }

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            if (!e.Item.Selected || !e.Item.Enabled) return;
            Rectangle rect = new Rectangle(3, 1, e.Item.Width - 6, e.Item.Height - 2);
            using (SolidBrush brush = new SolidBrush(Theme.SurfaceHover))
            using (System.Drawing.Drawing2D.GraphicsPath path = Theme.RoundedRect(rect, 7f))
            {
                e.Graphics.FillPath(brush, path);
            }
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Enabled ? Theme.Text : Theme.TextFaint;
            base.OnRenderItemText(e);
        }

        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
            Rectangle rect = e.ImageRectangle;
            using (Pen pen = new Pen(Theme.Accent, 2f))
            {
                pen.StartCap = System.Drawing.Drawing2D.LineCap.Round;
                pen.EndCap = System.Drawing.Drawing2D.LineCap.Round;
                Point p1 = new Point(rect.Left + 2, rect.Top + rect.Height / 2);
                Point p2 = new Point(rect.Left + 6, rect.Bottom - 5);
                Point p3 = new Point(rect.Right - 2, rect.Top + 5);
                e.Graphics.DrawLines(pen, new[] { p1, p2, p3 });
            }
        }

        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            using (Pen pen = new Pen(Theme.BorderSoft))
            {
                int y = e.Item.Height / 2;
                e.Graphics.DrawLine(pen, 10, y, e.Item.Width - 10, y);
            }
        }

        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
        {
            e.ArrowColor = Theme.TextMuted;
            base.OnRenderArrow(e);
        }
    }
}
