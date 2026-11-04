using System.Drawing.Imaging;
using LiteMonitor.src.Core;

namespace LiteMonitor.src.UI.Controls
{
    // Discrete-stop slider: the stops are exactly the values the setting accepts. The thumb follows
    // the pointer continuously, snaps on release, and bindings change once per completed action.
    // A profile value between stops is shown as it is and left untouched until the user acts.
    public sealed class LiteSlider : Control
    {
        private readonly double[] _stops;
        private readonly Func<double, string> _format;
        private readonly MotionValue _press, _hover;
        private readonly Action<float> _thumbFrame;
        private readonly int _labelWidth;
        private int _index;
        private double _value;
        private float _thumb;
        private bool _dragging;
        private int _pressIndex;
        private int _grabOffset;
        private ValueBubble? _bubble;

        internal event Action<double>? ValueCommitted;
        internal int StopIndex => _index;
        internal double Value => _value;
        internal float ThumbPosition => _thumb;
        internal bool IsDragging => _dragging;
        internal bool BubbleVisible => _bubble is { Visible: true };

        internal LiteSlider(IEnumerable<double> stops, Func<double, string> format)
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.Opaque | ControlStyles.Selectable | ControlStyles.ResizeRedraw, true);
            _stops = stops.OrderBy(x => x).ToArray();
            _format = format;
            _press = new MotionValue(this, "press", SpringSpec.Stretch);
            _hover = new MotionValue(this, "hover", SpringSpec.Hover);
            _thumbFrame = v => { _thumb = v; Invalidate(); };
            var font = UiFonts.Get(TextRole.Body);
            Font = font;
            BackColor = UIColors.CardBg;
            TabStop = true;
            Cursor = Cursors.Hand;
            AccessibleRole = AccessibleRole.Slider;
            foreach (double stop in _stops)
                _labelWidth = Math.Max(_labelWidth, TextRenderer.MeasureText(format(stop), font, Size.Empty, Ink.TextFlags).Width);
            // One label column for every slider, so tracks line up from row to row.
            _labelWidth = Math.Max(_labelWidth + UIUtils.S(2), UIUtils.S(60));
            Size = new Size(UIUtils.S(DS.SliderWidth) + _labelWidth + UIUtils.S(DS.RowGap), UIUtils.S(DS.ControlHeight));
        }

        private bool Rtl => RightToLeft == RightToLeft.Yes;
        private int ThumbSize => UIUtils.S(DS.SliderThumb);
        private int TrackLeft => (Rtl ? _labelWidth + UIUtils.S(DS.RowGap) : 0) + ThumbSize / 2;
        private int TrackRight => Width - (Rtl ? 0 : _labelWidth + UIUtils.S(DS.RowGap)) - ThumbSize / 2;
        private int TrackLength => Math.Max(1, TrackRight - TrackLeft);

        private float PositionToX(float position)
        {
            float ratio = _stops.Length <= 1 ? 0 : position / (_stops.Length - 1);
            if (Rtl) ratio = 1 - ratio;
            return TrackLeft + ratio * TrackLength;
        }

        private float XToPosition(float x)
        {
            float ratio = Math.Clamp((x - TrackLeft) / TrackLength, 0f, 1f);
            if (Rtl) ratio = 1 - ratio;
            return ratio * (_stops.Length - 1);
        }

        private int Nearest(double value)
        {
            int best = 0;
            for (int i = 1; i < _stops.Length; i++)
                if (Math.Abs(_stops[i] - value) < Math.Abs(_stops[best] - value)) best = i;
            return best;
        }

        // Programmatic value from the profile; never raises ValueCommitted.
        internal void SetValue(double value)
        {
            _value = value;
            _index = Nearest(value);
            if (!_dragging) { InterfaceMotion.Cancel(this, "thumb"); _thumb = _index; }
            UpdateAccessibleValue();
            Invalidate();
        }

        private void UpdateAccessibleValue() => AccessibilityNotifyClients(AccessibleEvents.ValueChange, -1);

        private void MoveToStop(int index, bool animate)
        {
            index = Math.Clamp(index, 0, _stops.Length - 1);
            _index = index;
            _value = _stops[index];
            UpdateAccessibleValue();
            if (animate) InterfaceMotion.Spring(this, "thumb", SpringSpec.Indicator with { Resolution = 0.004 }, _thumb, index, _thumbFrame);
            else { InterfaceMotion.Cancel(this, "thumb"); _thumb = index; }
            Invalidate();
        }

        // One step of a keyboard, wheel or test action; commits immediately.
        internal void StepBy(int delta)
        {
            if (!Enabled) return;
            int next = Math.Clamp(_index + delta, 0, _stops.Length - 1);
            bool changed = next != _index || _stops[next] != _value;
            MoveToStop(next, true);
            if (changed) ValueCommitted?.Invoke(_value);
        }

        protected override bool IsInputKey(Keys keyData)
        {
            switch (keyData & Keys.KeyCode)
            {
                case Keys.Up: case Keys.Down: case Keys.Left: case Keys.Right:
                case Keys.Home: case Keys.End: case Keys.PageUp: case Keys.PageDown:
                    return true;
            }
            return base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.Handled || !Enabled) return;
            int page = Math.Max(1, _stops.Length / 5);
            bool rtlFlip = Rtl;
            int? delta = e.KeyCode switch
            {
                Keys.Right => rtlFlip ? -1 : 1,
                Keys.Left => rtlFlip ? 1 : -1,
                Keys.Up => 1,
                Keys.Down => -1,
                Keys.PageUp => page,
                Keys.PageDown => -page,
                Keys.Home => -_stops.Length,
                Keys.End => _stops.Length,
                _ => null
            };
            if (delta is int step) { StepBy(step); e.Handled = true; }
        }

        // The wheel only adjusts a focused slider; otherwise it keeps scrolling the page.
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x020A && Focused && Enabled)
            {
                StepBy(Math.Sign((short)((m.WParam.ToInt64() >> 16) & 0xFFFF)));
                m.Result = IntPtr.Zero;
                return;
            }
            base.WndProc(ref m);
        }

        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); if (Enabled) _hover.To(1); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hover.To(0); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left || !Enabled) return;
            if (CanFocus) Focus();
            _pressIndex = _index;
            float thumbX = PositionToX(_thumb);
            bool onThumb = Math.Abs(e.X - thumbX) <= ThumbSize * 0.8f;
            _grabOffset = onThumb ? (int)(e.X - thumbX) : 0;
            InterfaceMotion.Cancel(this, "thumb");
            _dragging = true;
            _press.Press(0.6f);
            DragTo(e.X, !onThumb);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_dragging) DragTo(e.X, false);
        }

        private void DragTo(int x, bool glide)
        {
            float position = XToPosition(x - _grabOffset);
            int stop = (int)MathF.Round(position);
            if (stop != _index || _stops[stop] != _value)
            {
                _index = stop;
                _value = _stops[stop];
                UpdateAccessibleValue();
            }
            if (glide) InterfaceMotion.Spring(this, "thumb", SpringSpec.Indicator with { Resolution = 0.004 }, _thumb, position, _thumbFrame);
            else { InterfaceMotion.Cancel(this, "thumb"); _thumb = position; }
            ShowBubble();
            Invalidate();
            Update(); // the thumb stays under the pointer within the input event
        }

        // Harness entry: the same path as a pointer drag, without input.
        internal void BeginProbeDrag(float ratio)
        {
            _pressIndex = _index;
            _dragging = true;
            _press.Press(0.6f);
            DragTo((int)(TrackLeft + ratio * TrackLength), false);
        }
        internal void MoveProbeDrag(float ratio) => DragTo((int)(TrackLeft + ratio * TrackLength), false);
        internal void EndProbeDrag() => EndDrag(true);

        private void ShowBubble()
        {
            if (!IsHandleCreated || !Visible) return;
            _bubble ??= new ValueBubble();
            Point anchor = PointToScreen(new Point((int)PositionToX(_thumb), (Height - ThumbSize) / 2 - UIUtils.S(4)));
            _bubble.ShowValue(_format(_value), anchor, FindForm());
        }

        private void EndDrag(bool commit)
        {
            if (!_dragging) return;
            _dragging = false;
            _press.To(0, SpringSpec.PressOut);
            _bubble?.Hide();
            MoveToStop(_index, true);
            if (commit && _index != _pressIndex) ValueCommitted?.Invoke(_value);
        }

        protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); EndDrag(true); }
        protected override void OnMouseCaptureChanged(EventArgs e) { base.OnMouseCaptureChanged(e); if (!Capture) EndDrag(true); }
        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
        protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Cursor = Enabled ? Cursors.Hand : Cursors.Default; Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Color back = Ink.Back(this);
            g.FillRectangle(Ink.B(back), ClientRectangle);
            bool contrast = UIColors.HighContrast;
            float cy = Height / 2f, thickness = UIUtils.S(4f);
            float x = PositionToX(_thumb);
            Color rest = UIColors.Blend(back, UIColors.TextMain, 0.16f + 0.05f * Math.Clamp(_hover.Value, 0f, 1f));
            Color tint = Enabled ? UIColors.Primary : UIColors.Blend(back, UIColors.TextMain, 0.26f);
            float left = TrackLeft, right = TrackRight;
            Ink.Fill(g, new RectangleF(left - thickness / 2, cy - thickness / 2, right - left + thickness, thickness), thickness / 2, contrast ? SystemColors.WindowText : rest);
            float fillFrom = Rtl ? x : left, fillTo = Rtl ? right : x;
            if (fillTo > fillFrom) Ink.Fill(g, new RectangleF(fillFrom - thickness / 2, cy - thickness / 2, fillTo - fillFrom + thickness, thickness), thickness / 2, tint);
            float press = Math.Clamp(_press.Value, 0f, 1f);
            float size = ThumbSize * (1 + 0.10f * press);
            var thumb = new RectangleF(x - size / 2, cy - size / 2, size, size);
            if (Enabled && !contrast)
            {
                Ink.Fill(g, new RectangleF(thumb.X - 0.5f, thumb.Y + UIUtils.S(0.5f), thumb.Width + 1, thumb.Height + 0.5f), size / 2, Color.FromArgb(UIColors.IsDark ? 50 : 24, 0, 0, 0));
                Ink.Fill(g, new RectangleF(thumb.X, thumb.Y + UIUtils.S(1f), thumb.Width, thumb.Height), size / 2, Color.FromArgb(UIColors.IsDark ? 74 : 36, 0, 0, 0));
            }
            Ink.Fill(g, thumb, size / 2, contrast ? SystemColors.Highlight : Enabled ? Color.White : UIColors.Blend(back, Color.White, 0.6f));
            if (!contrast) Ink.Outline(g, thumb, size / 2, Color.FromArgb(UIColors.IsDark ? 0 : 26, 0, 0, 0));
            int labelX = Rtl ? 0 : Width - _labelWidth;
            Ink.Line(g, _format(_value), Font, new Rectangle(labelX, 0, _labelWidth, Height), Enabled ? UIColors.TextSub : UIColors.TextTertiary, Rtl ? TextFormatFlags.Left : TextFormatFlags.Right);
            if (Focused && ShowFocusCues) Ink.FocusRing(g, RectangleF.Inflate(thumb, UIUtils.S(2f), UIUtils.S(2f)), (size + UIUtils.S(4f)) / 2);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { _bubble?.Dispose(); _bubble = null; }
            base.Dispose(disposing);
        }

        protected override AccessibleObject CreateAccessibilityInstance() => new SliderAccessible(this);

        private sealed class SliderAccessible : ControlAccessibleObject
        {
            private readonly LiteSlider _slider;
            public SliderAccessible(LiteSlider slider) : base(slider) => _slider = slider;
            public override AccessibleRole Role => AccessibleRole.Slider;
            public override string? Value { get => _slider._format(_slider._value); set { } }
        }
    }

    // Trailing drag handle of a reorderable row. Pressing and moving it starts the reorder session;
    // keyboard users have the Move up / Move down commands on the same row.
    public sealed class LiteDragGrip : Control
    {
        private readonly MotionValue _hover;
        internal event Action<MouseEventArgs>? DragStarted;
        private Point _press;
        private bool _armed;

        public LiteDragGrip()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.Opaque, true);
            SetStyle(ControlStyles.Selectable, false);
            TabStop = false;
            _hover = new MotionValue(this, "hover", SpringSpec.Hover);
            Size = UIUtils.S(new Size(26, 28));
            Cursor = Cursors.SizeNS;
            BackColor = UIColors.CardBg;
            AccessibleRole = AccessibleRole.Grip;
            AccessibleName = LanguageManager.T("Menu.DragToReorder");
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Color back = Ink.Back(this);
            g.FillRectangle(Ink.B(back), ClientRectangle);
            Color ink = Enabled ? UIColors.Blend(UIColors.TextTertiary, UIColors.TextSub, Math.Clamp(_hover.Value, 0f, 1f)) : UIColors.Blend(back, UIColors.TextTertiary, 0.4f);
            float cx = Width / 2f, cy = Height / 2f, half = UIUtils.S(6.5f), spacing = UIUtils.S(4f);
            var mode = g.SmoothingMode;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            var pen = Ink.P(ink, Ink.LineWidth(1.5f));
            for (int i = -1; i <= 1; i++) g.DrawLine(pen, cx - half, cy + i * spacing, cx + half, cy + i * spacing);
            g.SmoothingMode = mode;
        }

        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); _hover.To(1); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hover.To(0); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left || !Enabled) return;
            _press = e.Location; _armed = true;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!_armed) return;
            if (Math.Abs(e.Y - _press.Y) < UIUtils.S(3) && Math.Abs(e.X - _press.X) < UIUtils.S(3)) return;
            _armed = false; // the reorder session takes over the pointer from here
            DragStarted?.Invoke(new MouseEventArgs(MouseButtons.Left, 0, _press.X, _press.Y, 0));
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            _armed = false;
            _hover.To(ClientRectangle.Contains(e.Location) ? 1 : 0);
        }

        protected override void OnMouseCaptureChanged(EventArgs e)
        {
            base.OnMouseCaptureChanged(e);
            if (Capture) return;
            _armed = false;
            _hover.To(0);
        }
    }
}
