using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using LiteMonitor.src.Core;

namespace LiteMonitor.src.UI.Controls
{
    // Themed floating menu for LiteComboBox: rounded, shadowed, scales and fades from its anchor.
    // It never takes activation; a message filter routes keys and outside presses while it is open,
    // so focus and the dialog title bar stay put and clicking elsewhere dismisses it first.
    internal sealed class LitePopupMenu : LayeredSprite, IMessageFilter
    {
        private const int WM_KEYDOWN = 0x0100, WM_SYSKEYDOWN = 0x0104, WM_CHAR = 0x0102, WM_SYSCHAR = 0x0106, WM_MOUSEMOVE = 0x0200, WM_LBUTTONDOWN = 0x0201, WM_LBUTTONUP = 0x0202,
            WM_RBUTTONDOWN = 0x0204, WM_MBUTTONDOWN = 0x0207, WM_NCLBUTTONDOWN = 0x00A1, WM_NCRBUTTONDOWN = 0x00A4, WM_MOUSEWHEEL = 0x020A;

        private static LitePopupMenu? _current;
        internal static LitePopupMenu? Current => _current;
        internal static bool IsOpenFor(LiteComboBox anchor) => _current != null && ReferenceEquals(_current._anchor, anchor) && !_current._closing;

        private readonly LiteComboBox _anchor;
        private readonly Form? _form;
        private readonly Action _closed;
        private readonly string[] _items;
        private readonly int _selected;
        private readonly int _itemHeight, _padding, _margin, _radius, _visibleRows;
        private readonly Size _body;
        private readonly bool _above;
        private readonly Point _origin; // top-left of the full (shadowed) frame on screen
        private Bitmap? _shadow, _frame, _scaled;
        private GdiCanvas? _content;
        private int _hover;
        private float _scroll, _scrollTarget;
        private float _open;
        private bool _closing;
        private bool _pressedInside;
        private bool _trackedThroughItem;
        private readonly Action<float> _openFrame;
        private readonly Action<float> _scrollFrame;

        internal int HoverIndex => _hover;
        internal int ItemCount => _items.Length;
        internal int VisibleRows => _visibleRows;
        internal Size BodySize => _body;
        internal bool OpensAbove => _above;

        internal static void Open(LiteComboBox anchor, Action closed)
        {
            _current?.Dismiss(false);
            var menu = new LitePopupMenu(anchor, closed);
            _current = menu;
            menu.Start();
        }

        internal static void CloseFor(LiteComboBox anchor) { if (IsOpenFor(anchor)) _current!.Dismiss(true); }
        internal static void TrackPointer(Point screen) => _current?.Track(screen);
        internal static void ReleasePointer(Point screen) => _current?.ReleaseAfterPress(screen);

        private LitePopupMenu(LiteComboBox anchor, Action closed) : base(clickThrough: false)
        {
            _anchor = anchor;
            _form = anchor.FindForm();
            _closed = closed;
            _items = anchor.ItemTexts();
            _selected = anchor.SelectedIndex;
            _hover = _selected;
            _itemHeight = UIUtils.S(34);
            _padding = UIUtils.S(5);
            _margin = UIUtils.S(26);
            _radius = UIUtils.S(DS.RadiusCard);
            Owner = _form;
            AccessibleRole = AccessibleRole.List;
            AccessibleName = anchor.AccessibleName;
            _openFrame = PresentOpen;
            _scrollFrame = v => { _scroll = v; RenderFrame(); Present(_frame!, _origin, 1); };

            Rectangle screen = anchor.RectangleToScreen(anchor.ClientRectangle);
            Rectangle work = Screen.FromRectangle(screen).WorkingArea;
            var font = UiFonts.Get(TextRole.Body);
            int widest = 0;
            // Very long lists (fonts) are sampled for width; the menu scrolls and ellipsizes anyway.
            int step = Math.Max(1, _items.Length / 200);
            for (int i = 0; i < _items.Length; i += step)
                widest = Math.Max(widest, TextRenderer.MeasureText(_items[i], font, Size.Empty, Ink.TextFlags).Width);
            int chrome = UIUtils.S(14 + 20 + 16);
            int width = Math.Clamp(widest + chrome, Math.Max(screen.Width, UIUtils.S(150)), Math.Max(screen.Width, Math.Min(work.Width / 2, UIUtils.S(420))));
            int gap = UIUtils.S(4);
            int spaceBelow = work.Bottom - screen.Bottom - gap, spaceAbove = screen.Top - work.Top - gap;
            int wanted = _items.Length * _itemHeight + _padding * 2;
            int limit = Math.Min(wanted, _itemHeight * 11 + _padding * 2);
            _above = spaceBelow < limit && spaceAbove > spaceBelow;
            int available = Math.Max(_itemHeight + _padding * 2, _above ? spaceAbove : spaceBelow);
            int height = Math.Min(limit, available);
            _visibleRows = Math.Max(1, (height - _padding * 2) / _itemHeight);
            height = _visibleRows * _itemHeight + _padding * 2;
            _body = new Size(width, height);
            int x = Math.Clamp(screen.Right - width, work.Left, Math.Max(work.Left, work.Right - width));
            int y = _above ? screen.Top - gap - height : screen.Bottom + gap;
            _origin = new Point(x - _margin, y - _margin);
            if (_selected >= _visibleRows) _scroll = _scrollTarget = Math.Min(MaxScroll, (_selected - _visibleRows / 2) * _itemHeight);
        }

        private int MaxScroll => Math.Max(0, _items.Length * _itemHeight - (_body.Height - _padding * 2));
        private Size FrameSize => new(_body.Width + _margin * 2, _body.Height + _margin * 2);

        private void Start()
        {
            _shadow = new Bitmap(FrameSize.Width, FrameSize.Height, PixelFormat.Format32bppPArgb);
            SoftShadow.Paint(_shadow, new Rectangle(_margin, _margin, _body.Width, _body.Height), _radius, UIUtils.S(22), UIUtils.S(7), UIColors.IsDark ? 0.46f : 0.24f);
            _frame = new Bitmap(FrameSize.Width, FrameSize.Height, PixelFormat.Format32bppPArgb);
            _content = new GdiCanvas(_body.Width, _body.Height);
            RenderFrame();
            Application.AddMessageFilter(this);
            if (_form != null)
            {
                _form.Deactivate += FormDismiss;
                _form.Move += FormDismiss;
                _form.Resize += FormDismiss;
            }
            _anchor.VisibleChanged += FormDismiss;
            if (!InterfaceMotion.Enabled) { _open = 1; Present(_frame, _origin, 1); return; }
            _open = 0;
            Present(_frame, _origin, 0); // shows the window; the spring draws from the next frame on
            InterfaceMotion.Spring(this, "open", SpringSpec.Popup, _open, 1, _openFrame);
        }

        private void FormDismiss(object? sender, EventArgs e) => Dismiss(true);

        private void PresentOpen(float value)
        {
            _open = value;
            float opacity = Math.Clamp(value * (_closing ? 1f : 1.7f), 0f, 1f);
            float scale = 0.90f + 0.10f * value;
            if (scale >= 0.995f && scale <= 1.005f) { Present(_frame!, _origin, opacity); return; }
            _scaled ??= new Bitmap(FrameSize.Width, FrameSize.Height, PixelFormat.Format32bppPArgb);
            using (var g = Graphics.FromImage(_scaled))
            {
                g.CompositingMode = CompositingMode.SourceCopy;
                g.Clear(Color.Transparent);
                g.InterpolationMode = InterpolationMode.Bilinear;
                g.PixelOffsetMode = PixelOffsetMode.HighSpeed;
                // The pivot is the corner next to the anchor, so the menu grows out of the control.
                float pivotX = _margin + _body.Width, pivotY = _above ? _margin + _body.Height : _margin;
                float w = FrameSize.Width * scale, h = FrameSize.Height * scale;
                g.DrawImage(_frame!, pivotX - pivotX * scale, pivotY - pivotY * scale, w, h);
            }
            Present(_scaled, _origin, opacity);
        }

        private Rectangle RowBounds(int index) =>
            new(0, _padding + index * _itemHeight - (int)MathF.Round(_scroll), _body.Width, _itemHeight);

        private void RenderFrame()
        {
            if (_content == null || _frame == null || _shadow == null) return;
            Color surface = UIColors.Elevated;
            {
                var g = _content.Graphics;
                g.Clear(surface);
                var font = UiFonts.Get(TextRole.Body);
                int first = Math.Max(0, (int)(_scroll / _itemHeight) - 1);
                int inset = UIUtils.S(5), checkColumn = UIUtils.S(30);
                g.SetClip(new Rectangle(0, _padding, _body.Width, _body.Height - _padding * 2));
                for (int i = first; i < _items.Length; i++)
                {
                    Rectangle row = RowBounds(i);
                    if (row.Top >= _body.Height) break;
                    bool hot = i == _hover;
                    if (hot) Ink.Fill(g, new Rectangle(inset, row.Top, _body.Width - inset * 2, row.Height), UIUtils.S((float)DS.RadiusSmall + 1), UIColors.Blend(surface, UIColors.Primary, UIColors.IsDark ? 0.24f : 0.14f));
                    Color ink = UIColors.HighContrast && hot ? SystemColors.HighlightText : UIColors.TextMain;
                    Ink.Line(g, _items[i], font, new Rectangle(checkColumn, row.Top, Math.Max(0, _body.Width - checkColumn - UIUtils.S(14)), row.Height), ink);
                    if (i == _selected) DrawCheck(g, new PointF(inset + UIUtils.S(13f), row.Top + row.Height / 2f));
                }
                g.ResetClip();
                if (MaxScroll > 0)
                {
                    int track = _body.Height - UIUtils.S(14);
                    int thumb = Math.Max(UIUtils.S(24), (int)(track * (float)_visibleRows / _items.Length));
                    float y = UIUtils.S(7f) + (track - thumb) * Math.Clamp(_scroll / MaxScroll, 0f, 1f);
                    Ink.Fill(g, new RectangleF(_body.Width - UIUtils.S(6f), y, UIUtils.S(3f), thumb), UIUtils.S(1.5f), UIColors.Blend(surface, UIColors.TextMain, 0.30f));
                }
            }
            using (var g = Graphics.FromImage(_frame))
            {
                g.CompositingMode = CompositingMode.SourceCopy;
                g.DrawImageUnscaled(_shadow, 0, 0);
                g.CompositingMode = CompositingMode.SourceOver;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                var body = new RectangleF(_margin, _margin, _body.Width, _body.Height);
                using var texture = new TextureBrush(_content.Pixels, WrapMode.Clamp);
                texture.TranslateTransform(_margin, _margin);
                g.FillPath(texture, Ink.Round(body, _radius));
                Color edge = UIColors.HighContrast ? SystemColors.WindowText : Color.FromArgb(UIColors.IsDark ? 60 : 26, UIColors.IsDark ? Color.White : Color.Black);
                g.DrawPath(Ink.P(edge, 1), Ink.Round(RectangleF.Inflate(body, -0.5f, -0.5f), _radius));
            }
        }

        private static void DrawCheck(Graphics g, PointF c)
        {
            var mode = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float s = UIUtils.S(1f);
            g.DrawLines(Ink.P(UIColors.Primary, Ink.LineWidth(1.9f)), new[] { new PointF(c.X - 4.5f * s, c.Y + 0.3f * s), new PointF(c.X - 1.2f * s, c.Y + 3.4f * s), new PointF(c.X + 4.8f * s, c.Y - 3.6f * s) });
            g.SmoothingMode = mode;
        }

        private void Rerender()
        {
            RenderFrame();
            if (_open >= 0.995f && !_closing) Present(_frame!, _origin, 1);
        }

        private int ItemAt(Point screen)
        {
            var local = new Point(screen.X - _origin.X - _margin, screen.Y - _origin.Y - _margin);
            if (local.X < 0 || local.Y < _padding || local.X >= _body.Width || local.Y >= _body.Height - _padding) return -1;
            int index = (int)((local.Y - _padding + _scroll) / _itemHeight);
            return index >= 0 && index < _items.Length ? index : -1;
        }

        internal bool BodyContains(Point screen) => InsideBody(screen);
        private bool InsideBody(Point screen) => new Rectangle(_origin.X + _margin, _origin.Y + _margin, _body.Width, _body.Height).Contains(screen);

        private void Track(Point screen)
        {
            int index = ItemAt(screen);
            if (index >= 0) _trackedThroughItem = true;
            if (index != _hover && index >= 0) SetHover(index);
        }

        private void SetHover(int index)
        {
            _hover = index;
            Rerender();
            AccessibilityNotifyClients(AccessibleEvents.Focus, index);
        }

        private void ReleaseAfterPress(Point screen)
        {
            int index = ItemAt(screen);
            if (_trackedThroughItem && index >= 0) Commit(index);
        }

        internal void Commit(int index)
        {
            if (_closing) return;
            bool changed = index != _anchor.SelectedIndex;
            var anchor = _anchor;
            Dismiss(true);
            if (changed) anchor.SelectedIndex = index;
        }

        private void MoveHover(int index)
        {
            index = Math.Clamp(index, 0, _items.Length - 1);
            if (index == _hover) return;
            _hover = index;
            float rowTop = index * _itemHeight, viewport = _body.Height - _padding * 2;
            float target = _scrollTarget;
            if (rowTop < target) target = rowTop;
            else if (rowTop + _itemHeight > target + viewport) target = rowTop + _itemHeight - viewport;
            AccessibilityNotifyClients(AccessibleEvents.Focus, index);
            if (target != _scrollTarget) ScrollMenuTo(target); else Rerender();
        }

        private void ScrollMenuTo(float target)
        {
            _scrollTarget = Math.Clamp(target, 0, MaxScroll);
            InterfaceMotion.Spring(this, "scroll", SpringSpec.Scroll, _scroll, _scrollTarget, _scrollFrame);
        }

        // Internal key entry: the filter routes here while open; also driven directly by tests.
        internal bool ProcessKey(Keys key)
        {
            switch (key)
            {
                case Keys.Up: MoveHover(_hover < 0 ? _items.Length - 1 : _hover - 1); return true;
                case Keys.Down: MoveHover(_hover < 0 ? 0 : _hover + 1); return true;
                case Keys.Home: MoveHover(0); return true;
                case Keys.End: MoveHover(_items.Length - 1); return true;
                case Keys.PageUp: MoveHover(_hover - _visibleRows); return true;
                case Keys.PageDown: MoveHover(_hover + _visibleRows); return true;
                case Keys.Enter: case Keys.Space: if (_hover >= 0) Commit(_hover); else Dismiss(true); return true;
                case Keys.Escape: case Keys.F4: case Keys.Tab: Dismiss(true); return true;
            }
            return false;
        }

        public bool PreFilterMessage(ref Message m)
        {
            if (_closing) return false;
            switch (m.Msg)
            {
                case WM_KEYDOWN:
                    ProcessKey((Keys)(m.WParam.ToInt64() & 0xFFFF));
                    return true;
                case WM_SYSKEYDOWN:
                    Dismiss(true);
                    return (Keys)(m.WParam.ToInt64() & 0xFFFF) is Keys.Up or Keys.Down;
                case WM_CHAR:
                    char typed = (char)(m.WParam.ToInt64() & 0xFFFF);
                    if (!char.IsControl(typed) && typed != ' ')
                    {
                        int match = LiteComboBox.FindPrefix(_items, typed, _hover);
                        if (match >= 0) MoveHover(match);
                    }
                    return true;
                case WM_SYSCHAR:
                    return true;
                case WM_MOUSEMOVE:
                    if (m.HWnd == Handle) { Track(Cursor.Position); return true; }
                    return false;
                case WM_LBUTTONDOWN:
                    if (InsideBody(Cursor.Position)) { _pressedInside = true; Track(Cursor.Position); return true; }
                    // Dismiss and consume, so pressing the anchor closes rather than reopens the menu.
                    Dismiss(true);
                    return true;
                case WM_LBUTTONUP:
                    if (m.HWnd != Handle) return false;
                    if (_pressedInside) { _pressedInside = false; int index = ItemAt(Cursor.Position); if (index >= 0) Commit(index); }
                    return true;
                case WM_RBUTTONDOWN: case WM_MBUTTONDOWN: case WM_NCLBUTTONDOWN: case WM_NCRBUTTONDOWN:
                    Dismiss(true);
                    return m.Msg is WM_RBUTTONDOWN or WM_MBUTTONDOWN;
                case WM_MOUSEWHEEL:
                    if (MaxScroll > 0)
                    {
                        int delta = (short)((m.WParam.ToInt64() >> 16) & 0xFFFF);
                        ScrollMenuTo(_scrollTarget - delta / 120f * _itemHeight * 3);
                    }
                    return true;
            }
            return false;
        }

        internal void Dismiss(bool animate)
        {
            if (_closing) return;
            _closing = true;
            Application.RemoveMessageFilter(this);
            if (_form != null)
            {
                _form.Deactivate -= FormDismiss;
                _form.Move -= FormDismiss;
                _form.Resize -= FormDismiss;
            }
            _anchor.VisibleChanged -= FormDismiss;
            if (ReferenceEquals(_current, this)) _current = null;
            if (!_anchor.IsDisposed) _closed();
            InterfaceMotion.Cancel(this);
            if (animate && InterfaceMotion.Enabled && Visible)
                InterfaceMotion.Spring(this, "open", SpringSpec.Fade with { Resolution = 0.02 }, _open, 0, _openFrame, DisposeNow);
            else DisposeNow();
        }

        private void DisposeNow()
        {
            if (IsDisposed) return;
            Hide();
            Dispose();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                InterfaceMotion.Cancel(this);
                _shadow?.Dispose(); _frame?.Dispose(); _scaled?.Dispose(); _content?.Dispose();
                _shadow = _frame = _scaled = null;
                _content = null;
            }
            base.Dispose(disposing);
        }

        protected override AccessibleObject CreateAccessibilityInstance() => new MenuAccessible(this);

        private sealed class MenuAccessible : ControlAccessibleObject
        {
            private readonly LitePopupMenu _menu;
            public MenuAccessible(LitePopupMenu menu) : base(menu) => _menu = menu;
            public override AccessibleRole Role => AccessibleRole.List;
            public override int GetChildCount() => _menu._items.Length;
            public override AccessibleObject? GetChild(int index) => index >= 0 && index < _menu._items.Length ? new ItemAccessible(_menu, this, index) : null;
            public override AccessibleObject? GetFocused() => GetChild(_menu._hover);
            public override AccessibleObject? GetSelected() => GetChild(_menu._selected);
        }

        private sealed class ItemAccessible : AccessibleObject
        {
            private readonly LitePopupMenu _menu;
            private readonly AccessibleObject _parent;
            private readonly int _index;
            public ItemAccessible(LitePopupMenu menu, AccessibleObject parent, int index) { _menu = menu; _parent = parent; _index = index; }
            public override string? Name { get => _menu._items[_index]; set { } }
            public override AccessibleRole Role => AccessibleRole.ListItem;
            public override AccessibleObject? Parent => _parent;
            public override string DefaultAction => "Select";
            public override void DoDefaultAction() => _menu.Commit(_index);
            public override Rectangle Bounds
            {
                get
                {
                    var row = _menu.RowBounds(_index);
                    Point origin = _menu._origin;
                    return new Rectangle(origin.X + _menu._margin + row.X, origin.Y + _menu._margin + row.Y, row.Width, row.Height);
                }
            }
            public override AccessibleStates State =>
                AccessibleStates.Selectable | (_index == _menu._selected ? AccessibleStates.Selected : 0) | (_index == _menu._hover ? AccessibleStates.Focused : 0);
        }
    }

    // Small value label above a slider thumb while dragging.
    internal sealed class ValueBubble : LayeredSprite
    {
        private Bitmap? _frame;
        private string _text = "";
        private Size _size;
        private int _pad;

        internal ValueBubble() : base(clickThrough: true) { }

        internal void ShowValue(string text, Point anchorScreen, Form? owner)
        {
            if (Owner == null && owner != null) Owner = owner;
            bool rendered = text != _text || _frame == null;
            if (rendered) Render(text);
            var at = new Point(anchorScreen.X - _size.Width / 2, anchorScreen.Y - _size.Height + _pad);
            if (Visible && !rendered) Reposition(at, 1); else Present(_frame!, at, 1);
        }

        private void Render(string text)
        {
            _text = text;
            var font = UiFonts.Get(TextRole.FootnoteStrong);
            Size measured = TextRenderer.MeasureText(text, font, Size.Empty, Ink.TextFlags);
            int margin = UIUtils.S(12);
            _pad = margin;
            var body = new Size(measured.Width + UIUtils.S(18), measured.Height + UIUtils.S(10));
            _size = new Size(body.Width + margin * 2, body.Height + margin * 2);
            _frame?.Dispose();
            _frame = new Bitmap(_size.Width, _size.Height, PixelFormat.Format32bppPArgb);
            SoftShadow.Paint(_frame, new Rectangle(margin, margin, body.Width, body.Height), body.Height / 2, UIUtils.S(10), UIUtils.S(2), UIColors.IsDark ? 0.40f : 0.20f);
            using var content = new GdiCanvas(body.Width, body.Height);
            content.Graphics.Clear(UIColors.Elevated);
            Ink.Line(content.Graphics, text, font, new Rectangle(Point.Empty, body), UIColors.TextMain, TextFormatFlags.HorizontalCenter);
            using (var g = Graphics.FromImage(_frame))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using var texture = new TextureBrush(content.Pixels, WrapMode.Clamp);
                texture.TranslateTransform(margin, margin);
                g.FillPath(texture, Ink.Round(new RectangleF(margin, margin, body.Width, body.Height), body.Height / 2f));
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { _frame?.Dispose(); _frame = null; }
            base.Dispose(disposing);
        }
    }

    // An opaque cover of the page colour that fades out over a just-swapped page: a cross-dissolve
    // from the background with no snapshot. It is a separate top-level window, so fading it is one
    // SetLayeredWindowAttributes per frame and the page underneath never repaints for it.
    internal sealed class RevealVeil : Form
    {
        internal RevealVeil()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None;
            MinimumSize = Size.Empty;
            Opacity = 1;
            BackColor = UIColors.MainBg;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.Opaque, true);
        }

        protected override bool ShowWithoutActivation => true;

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.Style = unchecked((int)0x80000000);
                cp.ExStyle |= 0x00080000 | 0x08000000 | 0x00000080 | 0x00000020; // LAYERED | NOACTIVATE | TOOLWINDOW | TRANSPARENT
                return cp;
            }
        }

        private Color _fill = UIColors.MainBg;

        protected override void OnPaint(PaintEventArgs e) => e.Graphics.FillRectangle(Ink.B(_fill), e.ClipRectangle);

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x0021) { m.Result = (IntPtr)3; return; } // WM_MOUSEACTIVATE -> MA_NOACTIVATE
            base.WndProc(ref m);
        }

        internal void ShowCover(Form owner, Color fill, float opacity)
        {
            if (Owner == null) Owner = owner;
            _fill = fill;
            BackColor = fill;
            Opacity = Math.Clamp(opacity, 0f, 1f);
        }

        internal void SetCover(float opacity) { if (IsHandleCreated) Opacity = Math.Clamp(opacity, 0f, 1f); }

        internal void PlaceCover(Rectangle bounds)
        {
            if (bounds.Width <= 0 || bounds.Height <= 0) { if (Visible) Hide(); return; }
            if (Bounds != bounds) Bounds = bounds;
            if (!Visible) Show();
            Update();
        }

        internal void Cover(Control area, Form owner)
        {
            InterfaceMotion.Cancel(this);
            if (Owner == null) Owner = owner;
            _fill = UIColors.MainBg;
            Rectangle bounds = area.RectangleToScreen(area.ClientRectangle);
            Bounds = bounds;
            Opacity = 1;
            if (!Visible) Show();
            Update();
        }

        internal void Reveal()
        {
            InterfaceMotion.Spring(this, "fade", SpringSpec.Fade with { Resolution = 0.015 }, (float)Opacity, 0, v => Opacity = Math.Clamp(v, 0f, 1f), Hide);
        }

        internal void HideNow()
        {
            InterfaceMotion.Cancel(this);
            if (Visible) Hide();
        }
    }
}
