using LiteMonitor.src.Core;

namespace LiteMonitor.src.UI.Controls
{
    // One window drawing a list of choices with a single spring-driven selection pill: the settings
    // sidebar (vertical) and segmented controls (horizontal). Sharing one surface means the pill can
    // travel across items without seams and costs one window instead of one per item. Each item is
    // exposed to assistive technology as a child object with a default action.
    public sealed class LiteSelector : Control
    {
        internal sealed record Entry(string Key, string Text, string? Icon);

        private readonly bool _vertical;
        private readonly List<Entry> _items = new();
        private readonly List<MotionValue> _hover = new();
        private readonly MotionValue _press;
        private readonly Action<float> _indicatorFrame;
        private int[] _widths = Array.Empty<int>();
        private int _selected = -1, _hot = -1, _pressed = -1;
        private float _indicator;
        private bool _keyboardCues;
        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern short GetKeyState(int key);

        internal event Action<int>? SelectionCommitted;
        internal int SelectedIndex => _selected;
        internal string? SelectedKey => _selected >= 0 ? _items[_selected].Key : null;
        internal float IndicatorPosition => _indicator;
        internal int Count => _items.Count;

        private int ItemHeight => UIUtils.S(_vertical ? 40 : DS.ControlHeight - 4);
        private int ItemGap => UIUtils.S(_vertical ? 2 : 0);
        private int TrackInset => _vertical ? 0 : UIUtils.S(2);
        private float Radius => UIUtils.S(_vertical ? 10f : (float)DS.RadiusControl - 2f);

        internal LiteSelector(bool vertical)
        {
            _vertical = vertical;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.Opaque | ControlStyles.Selectable | ControlStyles.ResizeRedraw, true);
            _press = new MotionValue(this, "press", SpringSpec.PressOut);
            _indicatorFrame = value => { _indicator = value; Invalidate(); };
            TabStop = true;
            Cursor = Cursors.Hand;
            BackColor = UIColors.MainBg;
            Font = UiFonts.Get(TextRole.Body);
            AccessibleRole = vertical ? AccessibleRole.List : AccessibleRole.PageTabList;
        }

        internal void Add(string key, string text, string? icon = null)
        {
            _items.Add(new Entry(key, text, icon));
            _hover.Add(new MotionValue(this, "hover" + _hover.Count, SpringSpec.Hover));
            Remeasure();
        }

        private void Remeasure()
        {
            if (_vertical) { Height = _items.Count * ItemHeight + Math.Max(0, _items.Count - 1) * ItemGap; return; }
            _widths = new int[_items.Count];
            int total = TrackInset * 2;
            var strong = UiFonts.Get(TextRole.FootnoteStrong);
            for (int i = 0; i < _items.Count; i++)
            {
                _widths[i] = TextRenderer.MeasureText(_items[i].Text, strong, Size.Empty, Ink.TextFlags).Width + UIUtils.S(28);
                total += _widths[i];
            }
            Size = new Size(total, ItemHeight + TrackInset * 2);
        }

        // Equal share of any extra or missing width, so a narrow window ellipsizes rather than clips.
        private Rectangle ItemBounds(int index)
        {
            if (_vertical) return new Rectangle(0, index * (ItemHeight + ItemGap), Width, ItemHeight);
            int natural = _widths.Sum();
            float scale = natural <= 0 ? 1 : (Width - TrackInset * 2) / (float)natural;
            int x = TrackInset;
            for (int i = 0; i < index; i++) x += (int)(_widths[i] * scale);
            return new Rectangle(x, TrackInset, (int)(_widths[index] * scale), Height - TrackInset * 2);
        }

        private RectangleF IndicatorBounds()
        {
            if (_items.Count == 1) return ItemBounds(0);
            int a = Math.Clamp((int)MathF.Floor(_indicator), 0, _items.Count - 2);
            float t = _indicator - a; // may leave 0..1 while a spring overshoots
            RectangleF first = ItemBounds(a), second = ItemBounds(a + 1);
            return new RectangleF(first.X + (second.X - first.X) * t, first.Y + (second.Y - first.Y) * t,
                first.Width + (second.Width - first.Width) * t, first.Height + (second.Height - first.Height) * t);
        }

        internal void SetSelected(int index, bool animate = true)
        {
            if (index < 0 || index >= _items.Count || index == _selected) return;
            bool first = _selected < 0;
            _selected = index;
            AccessibilityNotifyClients(AccessibleEvents.Selection, index);
            if (first || !animate) { InterfaceMotion.Cancel(this, "indicator"); _indicator = index; Invalidate(); return; }
            InterfaceMotion.Spring(this, "indicator", SpringSpec.Indicator with { Resolution = 0.002 }, _indicator, index, _indicatorFrame);
            Invalidate();
        }

        internal void SetSelectedKey(string key, bool animate = true) => SetSelected(_items.FindIndex(x => x.Key == key), animate);

        // The user-action path: same effect as clicking the item.
        internal void PerformSelect(int index)
        {
            if (!Enabled || index < 0 || index >= _items.Count || index == _selected) return;
            SetSelected(index);
            if (IsHandleCreated) Update(); // the pill starts moving before any page work begins
            SelectionCommitted?.Invoke(index);
        }

        private int HitTest(Point point)
        {
            for (int i = 0; i < _items.Count; i++) if (ItemBounds(i).Contains(point)) return i;
            return -1;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            SetHot(Enabled ? HitTest(e.Location) : -1);
        }

        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); SetHot(-1); }

        private void SetHot(int index)
        {
            if (_hot == index) return;
            if (_hot >= 0 && _hot < _hover.Count) _hover[_hot].To(0);
            _hot = index;
            if (_hot >= 0) _hover[_hot].To(1);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left || !Enabled) return;
            if (CanFocus) Focus();
            _keyboardCues = false;
            _pressed = HitTest(e.Location);
            if (_pressed < 0) return;
            _press.Press();
            // A sidebar selects on press, like a macOS source list; a segment selects on release.
            if (_vertical) PerformSelect(_pressed);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button != MouseButtons.Left) return;
            int pressed = _pressed;
            _pressed = -1;
            _press.To(0, SpringSpec.PressOut);
            if (!_vertical && pressed >= 0 && pressed == HitTest(e.Location)) PerformSelect(pressed);
        }

        protected override void OnMouseCaptureChanged(EventArgs e)
        {
            base.OnMouseCaptureChanged(e);
            if (!Capture) { _pressed = -1; _press.To(0, SpringSpec.PressOut); }
        }

        protected override bool IsInputKey(Keys keyData)
        {
            switch (keyData & Keys.KeyCode)
            {
                case Keys.Up: case Keys.Down: case Keys.Left: case Keys.Right:
                    return true;
            }
            return base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.Handled) return;
            int next = e.KeyCode switch
            {
                Keys.Up or Keys.Left => _selected - 1,
                Keys.Down or Keys.Right => _selected + 1,
                _ => int.MinValue
            };
            if (next == int.MinValue) return;
            _keyboardCues = true;
            PerformSelect(Math.Clamp(next, 0, _items.Count - 1));
            Invalidate();
            e.Handled = true;
        }

        protected override void OnGotFocus(EventArgs e)
        {
            base.OnGotFocus(e);
            _keyboardCues = (GetKeyState((int)Keys.Tab) & 0x8000) != 0;
            Invalidate();
        }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); _keyboardCues = false; Invalidate(); }
        protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Cursor = Enabled ? Cursors.Hand : Cursors.Default; SetHot(-1); Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Color back = Ink.Back(this);
            g.FillRectangle(Ink.B(back), ClientRectangle);
            if (_items.Count == 0) return;
            bool contrast = UIColors.HighContrast;
            Color pill, hoverFill;
            if (_vertical)
            {
                pill = UIColors.NavSelected;
                hoverFill = UIColors.NavHover;
            }
            else
            {
                Color track = UIColors.Blend(back, UIColors.TextMain, UIColors.IsDark ? 0.10f : 0.07f);
                Ink.Fill(g, new RectangleF(0, 0, Width, Height), Radius + TrackInset, track);
                if (contrast) Ink.Outline(g, new RectangleF(0, 0, Width, Height), Radius + TrackInset, SystemColors.WindowText);
                back = track;
                pill = contrast ? SystemColors.Highlight : UIColors.IsDark ? UIColors.Blend(track, UIColors.TextMain, 0.16f) : UIColors.Blend(track, Color.White, 0.92f);
                hoverFill = UIColors.Blend(track, UIColors.TextMain, 0.06f);
            }
            float press = Math.Clamp(_press.Value, 0f, 1f);
            for (int i = 0; i < _items.Count; i++)
            {
                if (i == _selected) continue;
                float hover = Math.Clamp(_hover[i].Value, 0f, 1f);
                float amount = Math.Clamp(hover + (i == _pressed ? press * 0.8f : 0), 0f, 1.6f);
                if (amount <= 0.01f || !Enabled) continue;
                Color fill = amount <= 1 ? UIColors.Blend(back, hoverFill, amount) : UIColors.Blend(hoverFill, pill, amount - 1);
                Ink.Fill(g, ItemBounds(i), Radius, fill);
            }
            RectangleF bounds = IndicatorBounds();
            if (!_vertical && !contrast)
                Ink.Fill(g, new RectangleF(bounds.X, bounds.Y + UIUtils.S(0.8f), bounds.Width, bounds.Height), Radius, Color.FromArgb(UIColors.IsDark ? 44 : 20, 0, 0, 0));
            // Selecting a pressed pill dips it a little, the same language as buttons.
            float dip = _pressed == _selected ? press * UIUtils.S(0.8f) : 0;
            Color pillColor = Enabled ? pill : UIColors.Blend(back, pill, 0.5f);
            Ink.Fill(g, RectangleF.Inflate(bounds, -dip, -dip), Radius, pillColor);
            for (int i = 0; i < _items.Count; i++) DrawItem(g, i, back, contrast);
            if (Focused && _keyboardCues && _selected >= 0)
                Ink.FocusRing(g, RectangleF.Inflate(ItemBounds(_selected), -UIUtils.S(1f), -UIUtils.S(1f)), Radius);
        }

        private Color _trackInkSurface = Color.Empty, _trackInk;
        private Color TrackInk(Color track)
        {
            if (track != _trackInkSurface)
            {
                _trackInkSurface = track;
                _trackInk = UIColors.EnsureContrast(UIColors.TextSub, track);
            }
            return _trackInk;
        }

        private void DrawItem(Graphics g, int index, Color back, bool contrast)
        {
            var entry = _items[index];
            Rectangle area = ItemBounds(index);
            float selected = 1 - Math.Clamp(MathF.Abs(_indicator - index), 0f, 1f);
            bool strong = selected > 0.5f;
            Color ink = contrast ? (strong ? SystemColors.HighlightText : SystemColors.WindowText)
                : !Enabled ? UIColors.TextTertiary
                : _vertical ? UIColors.TextMain : UIColors.Blend(TrackInk(back), UIColors.TextMain, selected);
            if (_vertical)
            {
                int icon = UIUtils.S(18);
                if (entry.Icon != null)
                {
                    Color iconInk = contrast ? ink : UIColors.Blend(UIColors.TextSub, UIColors.Primary, selected);
                    NavigationIcon.Draw(g, entry.Icon, new Rectangle(area.Left + UIUtils.S(12), area.Top + (area.Height - icon) / 2, icon, icon), iconInk);
                }
                int left = UIUtils.S(entry.Icon != null ? 42 : 12);
                Ink.Line(g, entry.Text, UiFonts.Get(strong ? TextRole.BodyStrong : TextRole.Body),
                    new Rectangle(area.Left + left, area.Top, Math.Max(0, area.Width - left - UIUtils.S(8)), area.Height), ink);
                return;
            }
            Ink.Line(g, entry.Text, UiFonts.Get(strong ? TextRole.FootnoteStrong : TextRole.Footnote),
                Rectangle.Inflate(area, -UIUtils.S(4), 0), ink, TextFormatFlags.HorizontalCenter);
        }

        protected override AccessibleObject CreateAccessibilityInstance() => new SelectorAccessible(this);

        private sealed class SelectorAccessible : ControlAccessibleObject
        {
            private readonly LiteSelector _selector;
            public SelectorAccessible(LiteSelector selector) : base(selector) => _selector = selector;
            public override int GetChildCount() => _selector._items.Count;
            public override AccessibleObject? GetChild(int index) => index >= 0 && index < _selector._items.Count ? new ItemAccessible(_selector, this, index) : null;
            public override AccessibleObject? GetSelected() => GetChild(_selector._selected);
            public override AccessibleObject? GetFocused() => _selector.Focused ? GetChild(_selector._selected) : base.GetFocused();
            public override AccessibleObject? HitTest(int x, int y)
            {
                int hit = _selector.HitTest(_selector.PointToClient(new Point(x, y)));
                return hit >= 0 ? GetChild(hit) : base.HitTest(x, y);
            }
        }

        private sealed class ItemAccessible : AccessibleObject
        {
            private readonly LiteSelector _selector;
            private readonly AccessibleObject _parent;
            private readonly int _index;
            public ItemAccessible(LiteSelector selector, AccessibleObject parent, int index) { _selector = selector; _parent = parent; _index = index; }
            public override string? Name { get => _selector._items[_index].Text; set { } }
            public override AccessibleRole Role => _selector._vertical ? AccessibleRole.ListItem : AccessibleRole.PageTab;
            public override AccessibleObject? Parent => _parent;
            public override Rectangle Bounds => _selector.RectangleToScreen(_selector.ItemBounds(_index));
            public override string DefaultAction => "Select";
            public override void DoDefaultAction() => _selector.PerformSelect(_index);
            public override void Select(AccessibleSelection flags) { if ((flags & (AccessibleSelection.TakeSelection | AccessibleSelection.AddSelection)) != 0) _selector.PerformSelect(_index); }
            public override AccessibleStates State
            {
                get
                {
                    var state = AccessibleStates.Selectable | AccessibleStates.Focusable;
                    if (_selector._selected == _index) state |= AccessibleStates.Selected | (_selector.Focused ? AccessibleStates.Focused : 0);
                    if (!_selector.Enabled) state |= AccessibleStates.Unavailable;
                    return state;
                }
            }
            public override AccessibleObject? Navigate(AccessibleNavigation direction) => direction switch
            {
                AccessibleNavigation.Next or AccessibleNavigation.Down or AccessibleNavigation.Right => _parent.GetChild(_index + 1),
                AccessibleNavigation.Previous or AccessibleNavigation.Up or AccessibleNavigation.Left => _parent.GetChild(_index - 1),
                _ => base.Navigate(direction)
            };
        }
    }
}
