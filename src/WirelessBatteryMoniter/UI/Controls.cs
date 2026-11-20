using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace WirelessBatteryMoniter.UI
{
    internal sealed class DoubleBufferedPanel : Panel
    {
        public DoubleBufferedPanel()
        {
            SetStyle(
                ControlStyles.AllPaintingInWmPaint
                | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.UserPaint
                | ControlStyles.ResizeRedraw,
                true);
            BackColor = Theme.Background;
        }
    }

    internal sealed class FlatButton : Control
    {
        private bool _hover;
        private bool _pressed;

        public FlatButton()
        {
            SetStyle(
                ControlStyles.AllPaintingInWmPaint
                | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.UserPaint
                | ControlStyles.ResizeRedraw
                | ControlStyles.SupportsTransparentBackColor,
                true);
            BackColor = Color.Transparent;
            ForeColor = Theme.Text;
            Font = Theme.Font(9.5f, FontStyle.Regular);
            Cursor = Cursors.Hand;
            TabStop = true;
            Size = new Size(92, 36);
            AccessibleRole = AccessibleRole.PushButton;
            // Mouse activation is handled once in OnMouseUp, for the left button only.
            SetStyle(ControlStyles.StandardClick | ControlStyles.StandardDoubleClick, false);
        }

        public bool Accent { get; set; }

        public bool Quiet { get; set; }

        protected override void OnPaint(PaintEventArgs e)
        {
            Theme.Prepare(e.Graphics);

            RectangleF rect = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);
            Color fill;
            Color border;
            Color text;

            if (!Enabled)
            {
                fill = Theme.WithAlpha(Theme.SurfaceRaised, 110);
                border = Theme.BorderSoft;
                text = Theme.TextFaint;
            }
            else if (Accent)
            {
                fill = _pressed ? Color.FromArgb(45, 166, 224) : (_hover ? Color.FromArgb(110, 207, 255) : Theme.Accent);
                border = fill;
                text = Theme.BadgeText;
            }
            else if (Quiet)
            {
                fill = _pressed ? Theme.SurfaceRaised : (_hover ? Theme.SurfaceHover : Color.Transparent);
                border = Color.Transparent;
                text = _hover ? Theme.Text : Theme.TextMuted;
            }
            else
            {
                fill = _pressed ? Theme.SurfaceRaised : (_hover ? Theme.SurfaceHover : Theme.Surface);
                border = _hover ? Theme.Border : Theme.BorderSoft;
                text = Theme.Text;
            }

            if (fill.A > 0)
            {
                using (SolidBrush brush = new SolidBrush(fill))
                {
                    Theme.FillRounded(e.Graphics, brush, rect, 9f);
                }
            }
            if (border.A > 0)
            {
                using (Pen pen = new Pen(border))
                {
                    Theme.DrawRounded(e.Graphics, pen, rect, 9f);
                }
            }

            if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(ClientRectangle, -2, -2), Theme.Accent, BackColor);
            Theme.DrawText(e.Graphics, Text, Font, text, ClientRectangle, Theme.CenterMiddle);
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            _hover = true;
            Invalidate();
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _hover = false;
            _pressed = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                _pressed = true;
                Focus();
                Invalidate();
            }
            base.OnMouseDown(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            bool invoke = _pressed && e.Button == MouseButtons.Left && ClientRectangle.Contains(e.Location);
            _pressed = false;
            Invalidate();
            base.OnMouseUp(e);
            if (invoke) OnClick(EventArgs.Empty);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter)
            {
                e.Handled = true;
                OnClick(EventArgs.Empty);
            }
            base.OnKeyDown(e);
        }

        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
        protected override void OnEnabledChanged(EventArgs e)
        {
            Invalidate();
            base.OnEnabledChanged(e);
        }

        protected override void OnTextChanged(EventArgs e)
        {
            AccessibleName = Text;
            Invalidate();
            base.OnTextChanged(e);
        }
    }

    internal sealed class ToggleSwitch : Control
    {
        private bool _checked;

        public ToggleSwitch()
        {
            SetStyle(
                ControlStyles.AllPaintingInWmPaint
                | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.UserPaint
                | ControlStyles.ResizeRedraw
                | ControlStyles.SupportsTransparentBackColor,
                true);
            BackColor = Color.Transparent;
            Size = new Size(48, 26);
            Cursor = Cursors.Hand;
            TabStop = true;
            AccessibleRole = AccessibleRole.CheckButton;
        }

        public event EventHandler CheckedChanged;

        public bool Checked
        {
            get { return _checked; }
            set
            {
                if (_checked == value) return;
                _checked = value;
                Invalidate();
                EventHandler handler = CheckedChanged;
                if (handler != null) handler(this, EventArgs.Empty);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Theme.Prepare(e.Graphics);

            RectangleF track = new RectangleF(1f, 3f, Width - 2f, Height - 6f);
            Color trackColor = _checked ? Theme.Accent : Theme.SurfaceRaised;
            using (SolidBrush brush = new SolidBrush(Enabled ? trackColor : Theme.WithAlpha(trackColor, 90)))
            {
                Theme.FillRounded(e.Graphics, brush, track, track.Height / 2f);
            }

            if (!_checked)
            {
                using (Pen pen = new Pen(Theme.Border))
                {
                    Theme.DrawRounded(e.Graphics, pen, track, track.Height / 2f);
                }
            }

            if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(ClientRectangle, -2, -2), Theme.Accent, BackColor);
            float knobSize = Height - 10f * Theme.DpiScale;
            float knobX = _checked ? Width - knobSize - 5f : 5f;
            using (SolidBrush knob = new SolidBrush(Enabled ? Color.White : Color.FromArgb(190, 200, 210)))
            {
                e.Graphics.FillEllipse(knob, knobX, 5f, knobSize, knobSize);
            }
        }

        protected override void OnClick(EventArgs e)
        {
            if (!Enabled) return;
            Checked = !Checked;
            base.OnClick(e);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter)
            {
                e.Handled = true;
                Checked = !Checked;
            }
            base.OnKeyDown(e);
        }

        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
        protected override void OnEnabledChanged(EventArgs e)
        {
            Invalidate();
            base.OnEnabledChanged(e);
        }
    }

    internal sealed class NumberStepper : Control
    {
        private int _value;
        private int _minimum;
        private int _maximum = 100;
        private string _suffix;
        private bool _minusHover;
        private bool _plusHover;

        public NumberStepper()
        {
            SetStyle(
                ControlStyles.AllPaintingInWmPaint
                | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.UserPaint
                | ControlStyles.ResizeRedraw
                | ControlStyles.SupportsTransparentBackColor,
                true);
            BackColor = Color.Transparent;
            Size = new Size(126, 38);
            Font = Theme.Font(10f, FontStyle.Bold);
            Cursor = Cursors.Hand;
            TabStop = true;
            AccessibleRole = AccessibleRole.SpinButton;
        }

        public event EventHandler ValueChanged;

        public int Value
        {
            get { return _value; }
            set { SetValue(value, true); }
        }

        public int Minimum
        {
            get { return _minimum; }
            set
            {
                _minimum = value;
                if (_value < _minimum) SetValue(_minimum, true);
                else Invalidate();
            }
        }

        public int Maximum
        {
            get { return _maximum; }
            set
            {
                _maximum = value;
                if (_value > _maximum) SetValue(_maximum, true);
                else Invalidate();
            }
        }

        public string Suffix
        {
            get { return _suffix; }
            set
            {
                _suffix = value;
                Invalidate();
            }
        }

        public void SetValue(int value, bool raiseEvent)
        {
            int clamped = Math.Max(_minimum, Math.Min(_maximum, value));
            if (clamped == _value)
            {
                Invalidate();
                return;
            }
            _value = clamped;
            Invalidate();
            if (raiseEvent)
            {
                EventHandler handler = ValueChanged;
                if (handler != null) handler(this, EventArgs.Empty);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Theme.Prepare(e.Graphics);

            RectangleF rect = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);
            using (SolidBrush brush = new SolidBrush(Theme.SurfaceRaised))
            {
                Theme.FillRounded(e.Graphics, brush, rect, 9f);
            }
            using (Pen border = new Pen(Theme.Border))
            {
                Theme.DrawRounded(e.Graphics, border, rect, 9f);
            }

            float buttonWidth = 34f * Theme.DpiScale;
            using (Pen separator = new Pen(Theme.BorderSoft))
            {
                e.Graphics.DrawLine(separator, buttonWidth, 7f, buttonWidth, Height - 7f);
                e.Graphics.DrawLine(separator, Width - buttonWidth, 7f, Width - buttonWidth, Height - 7f);
            }

            if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(ClientRectangle, -2, -2), Theme.Accent, BackColor);
            Color minusColor = Enabled ? (_minusHover ? Theme.Accent : Theme.TextMuted) : Theme.TextFaint;
            Color plusColor = Enabled ? (_plusHover ? Theme.Accent : Theme.TextMuted) : Theme.TextFaint;
            Theme.DrawText(e.Graphics, "\u2212", Theme.Font(12f, FontStyle.Regular), minusColor,
                new Rectangle(0, 0, (int)buttonWidth, Height), Theme.CenterMiddle);
            Theme.DrawText(e.Graphics, "+", Theme.Font(12f, FontStyle.Regular), plusColor,
                new Rectangle(Width - (int)buttonWidth, 0, (int)buttonWidth, Height), Theme.CenterMiddle);

            string text = _value.ToString() + (_suffix ?? string.Empty);
            Theme.DrawText(e.Graphics, text, Font, Enabled ? Theme.Text : Theme.TextFaint,
                new Rectangle((int)buttonWidth, 0, Width - (int)(buttonWidth * 2), Height),
                Theme.CenterMiddle);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (!Enabled) return;
            if (e.X <= 34 * Theme.DpiScale)
            {
                SetValue(_value - 1, true);
            }
            else if (e.X >= Width - 34 * Theme.DpiScale)
            {
                SetValue(_value + 1, true);
            }
            base.OnMouseDown(e);
            Focus();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            bool minus = e.X <= 34 * Theme.DpiScale;
            bool plus = e.X >= Width - 34 * Theme.DpiScale;
            if (minus != _minusHover || plus != _plusHover)
            {
                _minusHover = minus;
                _plusHover = plus;
                Invalidate();
            }
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _minusHover = false;
            _plusHover = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override bool IsInputKey(Keys keyData) { return (keyData & Keys.KeyCode) == Keys.Up || (keyData & Keys.KeyCode) == Keys.Down || (keyData & Keys.KeyCode) == Keys.Left || (keyData & Keys.KeyCode) == Keys.Right || base.IsInputKey(keyData); }
        protected override void OnMouseWheel(MouseEventArgs e)
        {
            if (!Enabled) return;
            SetValue(_value + (e.Delta > 0 ? 1 : -1), true);
            base.OnMouseWheel(e);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (!Enabled) return;
            if (e.KeyCode == Keys.Up || e.KeyCode == Keys.Right)
            {
                e.Handled = true;
                SetValue(_value + 1, true);
            }
            else if (e.KeyCode == Keys.Down || e.KeyCode == Keys.Left)
            {
                e.Handled = true;
                SetValue(_value - 1, true);
            }
            base.OnKeyDown(e);
        }

        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
        protected override void OnEnabledChanged(EventArgs e)
        {
            Invalidate();
            base.OnEnabledChanged(e);
        }
    }
}
