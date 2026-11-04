using System.Runtime.InteropServices;
using LiteMonitor.src.Core;
using LiteMonitor.src.UI.SettingsPage;

namespace LiteMonitor.src.UI.Controls
{
    // =======================================================================
    // Groups and rows
    // =======================================================================

    // An iOS grouped section. Plain groups have a caption above the card; collapsible groups are a
    // card whose first row is the disclosure header. Body height is the only animated geometry; rows
    // are never laid out during the transition, they are simply revealed by the clipping body.
    public class LiteSettingsGroup : Panel, IHeightForWidth
    {
        private readonly GroupTitleButton _titleButton;
        private readonly GroupBody _body;
        private readonly StackPanel _rows;
        private FooterText? _footer;
        private bool _collapsible;
        private bool _collapsed;
        private bool _animating;
        private int _bodyHeight;
        private bool _layoutPending;
        private RevealVeil? _veil;
        private float _contentCover;
        private readonly MotionValue _chevron;

        internal bool IsAnimatingDisclosure => _animating;

        public bool Collapsible
        {
            get => _collapsible;
            set
            {
                _collapsible = value;
                _titleButton.TabStop = value;
                _titleButton.Cursor = value ? Cursors.Hand : Cursors.Default;
                _body.RoundTop = !value;
                if (!value) Collapsed = false;
                Invalidate(true);
            }
        }

        public bool Collapsed
        {
            get => _collapsed;
            set
            {
                bool next = value && Collapsible;
                if (_collapsed == next) return;
                _collapsed = next;
                _titleButton.AccessibleDescription = next ? "Collapsed" : "Expanded";
                _chevron.To(next ? 0 : 1, SpringSpec.Selection);
                ResizeBody(next);
            }
        }

        public LiteSettingsGroup(string title)
        {
            DoubleBuffered = true;
            BackColor = UIColors.MainBg;
            Dock = DockStyle.Top;
            Margin = Padding.Empty;
            _titleButton = new GroupTitleButton(this) { Text = title, AccessibleName = title, TabStop = false };
            _chevron = new MotionValue(_titleButton, "chevron", SpringSpec.Selection, 1);
            _titleButton.Click += (_, _) => { if (Collapsible) Collapsed = !Collapsed; };
            _rows = new StackPanel { BackColor = UIColors.CardBg, Location = new Point(UIUtils.S(DS.RowInset), UIUtils.S(2)) };
            _rows.ContentHeightChanged += RowsChanged;
            _body = new GroupBody { BackColor = UIColors.CardBg };
            _body.Controls.Add(_rows);
            Controls.Add(_body);
            Controls.Add(_titleButton);
            _titleButton.TabIndex = 0;
            _body.TabIndex = 1;
        }

        internal float ChevronValue => _chevron.Value;
        private int TitleHeight => UIUtils.S(_collapsible ? DS.RowHeight : DS.SectionHeader);
        private int BodyChrome => UIUtils.S(6);
        private int FooterHeight(int width) => _footer == null ? 0 : _footer.HeightForWidth(width);
        private int RowsWidth(int width) => Math.Max(0, width - UIUtils.S(DS.RowInset) * 2);
        private int ExpandedBodyHeight(int width) => _rows.MeasureContent(RowsWidth(width)) + BodyChrome;

        public int HeightForWidth(int width)
        {
            int body = _animating ? _bodyHeight : _collapsed ? 0 : ExpandedBodyHeight(width);
            if (!_animating) _bodyHeight = body;
            return TitleHeight + body + FooterHeight(width) + UIUtils.S(DS.GroupGap);
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            if (_body == null) return;
            if (!Visible) { _layoutPending = true; return; }
            _layoutPending = false;
            int width = ClientSize.Width, title = TitleHeight;
            _titleButton.SetBounds(0, 0, width, title);
            int rowsWidth = RowsWidth(width);
            if (_rows.Width != rowsWidth) _rows.Width = rowsWidth;
            if (_body.Top != title || _body.Width != width || _body.Height != _bodyHeight) _body.SetBounds(0, title, width, _bodyHeight);
            _footer?.SetBounds(0, title + _bodyHeight, width, FooterHeight(width));
            PlaceVeil();
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            if (Visible && _layoutPending) PerformLayout();
        }

        private void RowsChanged()
        {
            if (_animating || _collapsed || !Visible) return;
            int height = _rows.Height + BodyChrome;
            if (height == _bodyHeight) return;
            _bodyHeight = height;
            Reflow();
        }

        private void Reflow()
        {
            if (Parent != null) { Height = HeightForWidth(Width); Parent.PerformLayout(); }
            PerformLayout();
        }

        // Restoring saved view state is not an interaction: no transition.
        internal void RestoreCollapsed(bool value)
        {
            bool next = value && Collapsible;
            if (_collapsed == next) return;
            _collapsed = next;
            _titleButton.AccessibleDescription = next ? "Collapsed" : "Expanded";
            _chevron.Set(next ? 0 : 1);
            InterfaceMotion.Cancel(this, "body");
            _animating = false;
            _body.Visible = !next;
            _bodyHeight = next ? 0 : ExpandedBodyHeight(Width);
            Reflow();
        }

        // One height transition per disclosure change; interrupting continues from the current
        // height and speed. With motion off (or off screen) it is a direct switch.
        private void ResizeBody(bool collapse)
        {
            InterfaceMotion.Cancel(this, "body");
            _titleButton.Invalidate();
            if (!InterfaceMotion.Enabled || !IsHandleCreated || !Visible)
            {
                _animating = false;
                _body.Visible = !collapse;
                _bodyHeight = collapse ? 0 : ExpandedBodyHeight(Width);
                Reflow();
                return;
            }
            int target = collapse ? 0 : ExpandedBodyHeight(Width);
            _animating = true; // before the rows lay out: their height change must not jump the body open
            if (!_body.Visible)
            {
                // First reveal creates the row windows; do it before the first frame.
                _rows.Width = RowsWidth(Width);
                _body.Height = 0;
                _body.Visible = true;
                _rows.PerformLayout();
            }
            StartContentFade(collapse);
            InterfaceMotion.Spring(this, "body", SpringSpec.Disclosure, _bodyHeight, target, height =>
            {
                int next = Math.Max(0, (int)MathF.Round(height));
                if (next == _bodyHeight) return;
                _bodyHeight = next;
                Reflow();
            }, () =>
            {
                _animating = false;
                _body.Visible = !collapse;
                _bodyHeight = collapse ? 0 : ExpandedBodyHeight(Width);
                Reflow();
                EndContentFade();
            });
        }

        // Rows fade while the height moves: a veil of the card colour dissolves over them (or in, when
        // closing). It is the same snapshot-free veil as page transitions, inset past the rounded
        // corners and kept to the visible body, so nothing outside the rows is touched.
        private void StartContentFade(bool collapse)
        {
            if (FindForm() is not SettingsForm form) return;
            _veil = form.DisclosureVeil;
            if (!collapse && !InterfaceMotion.IsAnimating(this, "cover")) _contentCover = 1;
            _veil.ShowCover(form, UIColors.CardBg, _contentCover);
            PlaceVeil();
            InterfaceMotion.Spring(this, "cover", collapse ? SpringSpec.Selection : SpringSpec.Disclosure with { Resolution = 0.01 }, _contentCover, collapse ? 1 : 0, value =>
            {
                _contentCover = Math.Clamp(value, 0f, 1f);
                _veil?.SetCover(_contentCover);
            });
        }

        private void PlaceVeil()
        {
            if (_veil == null || !IsHandleCreated) return;
            int inset = UIUtils.S(DS.RowInset) / 2;
            Rectangle area = _body.RectangleToScreen(new Rectangle(inset, 0, Math.Max(0, _body.Width - inset * 2), Math.Max(0, _bodyHeight - UIUtils.S(DS.RadiusCard))));
            for (Control? c = Parent; c != null; c = c.Parent)
                if (c is ScrollSurface surface) { area.Intersect(surface.RectangleToScreen(surface.ClientRectangle)); break; }
            _veil.PlaceCover(area);
        }

        private void EndContentFade()
        {
            InterfaceMotion.Cancel(this, "cover");
            _veil?.HideNow();
            _veil = null;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) EndContentFade();
            base.Dispose(disposing);
        }

        public void AddItem(Control item)
        {
            item.Margin = Padding.Empty;
            _rows.Controls.Add(item);
            _rows.Controls.SetChildIndex(item, 0);
            if (_rows.Controls.Count > 1) _rows.Controls[1].Invalidate(); // it is no longer the last row
        }

        public void AddFullItem(Control item) => AddItem(item);

        // Explanatory text belongs under the card, like an iOS section footer.
        internal void AddFooter(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            text = UiText.StripLeadingSymbol(text);
            if (_footer == null)
            {
                _footer = new FooterText { Text = text };
                Controls.Add(_footer);
            }
            else _footer.Text = _footer.Text + "\n" + text;
        }

        protected override void OnPaintBackground(PaintEventArgs e) => e.Graphics.FillRectangle(Ink.B(UIColors.MainBg), e.ClipRectangle);

        private sealed class GroupTitleButton : Button
        {
            private readonly LiteSettingsGroup _group;
            private readonly MotionValue _hover, _press;

            public GroupTitleButton(LiteSettingsGroup group)
            {
                _group = group;
                _hover = new MotionValue(this, "hover", SpringSpec.Hover);
                _press = new MotionValue(this, "press", SpringSpec.PressOut);
                FlatStyle = FlatStyle.Flat;
                FlatAppearance.BorderSize = 0;
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.Opaque, true);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                Color page = UIColors.MainBg;
                g.FillRectangle(Ink.B(page), ClientRectangle);
                if (!_group.Collapsible)
                {
                    int inset = UIUtils.S(DS.RowInset);
                    var caption = new Rectangle(inset, 0, Width - inset * 2, Height - UIUtils.S(6));
                    TextRenderer.DrawText(g, Text, UiFonts.Get(TextRole.FootnoteStrong), caption, UIColors.TextSubOnPage,
                        Ink.TextFlags | TextFormatFlags.SingleLine | TextFormatFlags.Bottom | TextFormatFlags.EndEllipsis);
                    return;
                }
                float radius = UIUtils.S((float)DS.RadiusCard);
                // The card continues below while the body is open; only its top corners round here.
                float open = _group._bodyHeight > 0 ? 1 : 0;
                var card = new RectangleF(0, 0, Width, Height + open * radius * 2);
                Color fill = UIColors.Blend(UIColors.CardBg, UIColors.FieldHover, Math.Clamp(_hover.Value * 0.6f + _press.Value * 0.8f, 0f, 1f));
                Ink.Fill(g, card, radius, fill);
                if (UIColors.HighContrast) Ink.Outline(g, card, radius, SystemColors.WindowText);
                int left = UIUtils.S(DS.RowInset);
                var text = new Rectangle(left, 0, Width - left - UIUtils.S(44), Height);
                Ink.Line(g, Text, UiFonts.Get(TextRole.Headline), text, UIColors.TextMain);
                var center = new PointF(Width - UIUtils.S(24f), Height / 2f);
                // A trailing chevron: right when closed, turned down as the group opens.
                Ink.Chevron(g, center, UIUtils.S(4.5f), 90 * _group.ChevronValue, UIColors.TextTertiary, Ink.LineWidth(1.6f));
                if (open > 0)
                    g.DrawLine(Ink.P(UIColors.Separator, 1), left, Height - 1, Width - left, Height - 1);
                if (Focused && ShowFocusCues) Ink.FocusRing(g, RectangleF.Inflate(new RectangleF(0, 0, Width, Height), -UIUtils.S(2f), -UIUtils.S(2f)), radius - UIUtils.S(2f));
            }

            protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); if (_group.Collapsible) _hover.To(1); }
            protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hover.To(0); }
            protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); if (e.Button == MouseButtons.Left && _group.Collapsible) _press.Press(); }
            protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); _press.To(0, SpringSpec.PressOut); }
            protected override void OnMouseCaptureChanged(EventArgs e) { base.OnMouseCaptureChanged(e); if (!Capture) _press.To(0, SpringSpec.PressOut); }
            protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
            protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
        }

        private sealed class GroupBody : Panel
        {
            internal bool RoundTop { get; set; } = true;

            public GroupBody()
            {
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.Opaque, true);
            }

            // Children are placed by LiteSettingsGroup; a size change must not arrange them.
            protected override void OnLayout(LayoutEventArgs e) { }

            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                g.FillRectangle(Ink.B(UIColors.MainBg), e.ClipRectangle);
                float radius = UIUtils.S((float)DS.RadiusCard), top = RoundTop ? 0 : -radius * 2;
                Ink.Fill(g, new RectangleF(0, top, Width, Height - top), radius, UIColors.CardBg);
                if (UIColors.HighContrast) Ink.Outline(g, new RectangleF(0, top, Width, Height - top), radius, SystemColors.WindowText);
            }
        }
    }

    // Wrapping secondary text under a group, or a status line inside one.
    internal sealed class FooterText : Control, IHeightForWidth
    {
        private int _measuredWidth = -1, _measuredHeight;
        private const TextFormatFlags Flags = Ink.TextFlags | TextFormatFlags.WordBreak | TextFormatFlags.Left;

        public FooterText()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.Opaque, true);
            SetStyle(ControlStyles.Selectable, false);
            TabStop = false;
            Font = UiFonts.Get(TextRole.Footnote);
            AccessibleRole = AccessibleRole.StaticText;
        }

        internal bool Inset { get; set; } = true;

        protected override void OnTextChanged(EventArgs e)
        {
            base.OnTextChanged(e);
            AccessibleName = Text;
            _measuredWidth = -1;
            if (Parent != null && Visible) { Parent.PerformLayout(); Parent.Parent?.PerformLayout(); }
            Invalidate();
        }

        private int SideInset => Inset ? UIUtils.S(DS.RowInset) : 0;

        public int HeightForWidth(int width)
        {
            if (width == _measuredWidth) return _measuredHeight;
            _measuredWidth = width;
            if (string.IsNullOrEmpty(Text)) return _measuredHeight = 0;
            int text = TextRenderer.MeasureText(Text, Font, new Size(Math.Max(1, width - SideInset * 2), int.MaxValue), Flags).Height;
            return _measuredHeight = text + UIUtils.S(DS.FooterTop) + UIUtils.S(Inset ? 2 : 6);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Color back = Ink.Back(this);
            e.Graphics.FillRectangle(Ink.B(back), ClientRectangle);
            var area = new Rectangle(SideInset, UIUtils.S(DS.FooterTop), Math.Max(0, Width - SideInset * 2), Math.Max(0, Height - UIUtils.S(DS.FooterTop)));
            TextRenderer.DrawText(e.Graphics, Text, Font, area, Inset ? UIColors.TextSubOnPage : UIColors.TextSub, Flags);
        }
    }

    public sealed class RowLabel
    {
        private string _text;
        internal Action? Changed;
        internal RowLabel(string text) => _text = text;
        public string Text { get => _text; set { if (_text == value) return; _text = value; Changed?.Invoke(); } }
    }

    // A list row: leading title (owner-drawn, no child window) and one trailing control.
    public class LiteSettingsItem : Panel, IHeightForWidth
    {
        public RowLabel Label { get; }
        private readonly Control _control;
        private readonly int _preferredWidth;
        private int _measuredWidth = -1, _measuredHeight, _titleHeight;
        private const TextFormatFlags TitleFlags = Ink.TextFlags | TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis;

        public LiteSettingsItem(string text, Control ctrl)
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.Opaque, true);
            Margin = Padding.Empty;
            BackColor = UIColors.CardBg;
            _control = ctrl;
            Label = new RowLabel(text);
            Label.Changed = () => { _measuredWidth = -1; SyncControlName(); Parent?.PerformLayout(); Invalidate(); };
            if (ctrl is LiteCheck check && !string.IsNullOrWhiteSpace(text))
            {
                check.Text = "";
                check.Size = check.GetPreferredSize(Size.Empty);
            }
            SyncControlName();
            _preferredWidth = ctrl.Width; // before Add: adding runs a layout pass that uses it
            Controls.Add(ctrl);
            ctrl.EnabledChanged += (_, _) => Invalidate();
            Height = UIUtils.S(DS.RowHeight);
        }

        private void SyncControlName() { if (!string.IsNullOrWhiteSpace(Label.Text)) _control.AccessibleName = Label.Text; }

        private bool Rtl => RightToLeft == RightToLeft.Yes;

        private int ControlWidth(int rowWidth)
        {
            int limit = Math.Max(UIUtils.S(36), rowWidth * 3 / 5);
            return _control.AutoSize ? _control.Width : Math.Min(_preferredWidth, limit);
        }

        public int HeightForWidth(int width)
        {
            if (width == _measuredWidth) return _measuredHeight;
            _measuredWidth = width;
            int available = Math.Max(1, width - ControlWidth(width) - UIUtils.S(DS.RowGap));
            _titleHeight = TextRenderer.MeasureText(Label.Text, UiFonts.Get(TextRole.Body), new Size(available, int.MaxValue), TitleFlags).Height;
            return _measuredHeight = Math.Max(UIUtils.S(DS.RowHeight), Math.Max(_titleHeight, _control.Height) + UIUtils.S(12));
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            if (!Visible) return;
            int width = ControlWidth(Width);
            if (!_control.AutoSize && _control.Width != width) _control.Width = width;
            int x = Rtl ? 0 : Math.Max(0, Width - _control.Width);
            var target = new Point(x, Math.Max(0, (Height - _control.Height) / 2));
            if (_control.Location != target) _control.Location = target;
            HeightForWidth(Width);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.FillRectangle(Ink.B(UIColors.CardBg), e.ClipRectangle);
            HeightForWidth(Width);
            int gap = UIUtils.S(DS.RowGap), controlWidth = _control.Width;
            int textWidth = Math.Max(0, Width - controlWidth - gap);
            var title = new Rectangle(Rtl ? controlWidth + gap : 0, Math.Max(0, (Height - _titleHeight) / 2), textWidth, Math.Min(_titleHeight, Height));
            Color color = _control.Enabled ? UIColors.TextMain : UIColors.TextSub;
            TextRenderer.DrawText(g, Label.Text, UiFonts.Get(TextRole.Body), title, color,
                TitleFlags | (Rtl ? TextFormatFlags.Right | TextFormatFlags.RightToLeft : TextFormatFlags.Left));
            if (!IsLastRow) g.DrawLine(Ink.P(UIColors.Separator, 1), 0, Height - 1, Width, Height - 1);
        }

        private bool IsLastRow => Parent is { } parent && parent.Controls.Count > 0 && ReferenceEquals(parent.Controls[0], this);

        protected override void OnResize(EventArgs e) { base.OnResize(e); Invalidate(); }
    }

    public class LiteThresholdRow : Panel, IHeightForWidth
    {
        private readonly string _title;
        private readonly LiteNumberInput _warn, _crit;

        public LiteThresholdRow(SettingsPageBase page, string title, string unit, ValueRange range)
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.Opaque, true);
            _title = title;
            Margin = Padding.Empty;
            BackColor = UIColors.CardBg;
            double step = unit switch { "MB" => 100, "MB/s" => 1, _ => 5 };
            _warn = new LiteNumberInput(range.Warn.ToString(), unit, LanguageManager.T("Menu.ValueWarnColor"), 100, UIColors.TextWarnOnField) { AccessibleName = title + " · " + LanguageManager.T("Menu.ValueWarnColor") };
            _crit = new LiteNumberInput(range.Crit.ToString(), unit, LanguageManager.T("Menu.ValueCritColor"), 100, UIColors.TextCritOnField) { AccessibleName = title + " · " + LanguageManager.T("Menu.ValueCritColor") };
            _warn.EnableStepper(step, 0, 100000);
            _crit.EnableStepper(step, 0, 100000);
            _warn.Inner.TextChanged += (_, _) => range.Warn = _warn.ValueDouble;
            _crit.Inner.TextChanged += (_, _) => range.Crit = _crit.ValueDouble;
            page.RegisterRefresh(() => {
                _warn.Inner.Text = range.Warn.ToString();
                _crit.Inner.Text = range.Crit.ToString();
            });
            Controls.Add(_warn);
            Controls.Add(_crit);
            Height = UIUtils.S(DS.RowHeight);
        }

        public int HeightForWidth(int width) => UIUtils.S(DS.RowHeight);

        protected override void OnLayout(LayoutEventArgs e)
        {
            if (!Visible) return;
            int gap = UIUtils.S(22), y = Math.Max(0, (Height - _crit.Height) / 2);
            _crit.Location = new Point(Math.Max(0, Width - _crit.Width), y);
            _warn.Location = new Point(Math.Max(0, _crit.Left - gap - _warn.Width), y);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.FillRectangle(Ink.B(UIColors.CardBg), e.ClipRectangle);
            Ink.Line(g, _title, UiFonts.Get(TextRole.Body), new Rectangle(0, 0, Math.Max(0, _warn.Left - UIUtils.S(DS.RowGap)), Height), UIColors.TextMain);
            float mid = _warn.Right + (_crit.Left - _warn.Right) / 2f;
            Ink.Chevron(g, new PointF(mid, Height / 2f), UIUtils.S(3.5f), 0, UIColors.TextTertiary, Ink.LineWidth(1.4f));
            bool last = Parent is { } parent && ReferenceEquals(parent.Controls[0], this);
            if (!last) g.DrawLine(Ink.P(UIColors.Separator, 1), 0, Height - 1, Width, Height - 1);
        }
    }

    // =======================================================================
    // Text inputs
    // =======================================================================

    // Native edit control whose caret takes the theme tint. GDI inverts caret pixels by XOR, so the
    // bitmap holds accent XOR background; the selection highlight remains the system colour.
    internal sealed class ThemedTextBox : TextBox
    {
        [DllImport("user32.dll")] private static extern bool CreateCaret(IntPtr hwnd, IntPtr bitmap, int width, int height);
        [DllImport("user32.dll")] private static extern bool ShowCaret(IntPtr hwnd);
        [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
        private IntPtr _caretBitmap;

        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            if (m.Msg == 0x0007) ApplyCaret(); // WM_SETFOCUS
            else if (m.Msg == 0x0008) ReleaseCaret(); // WM_KILLFOCUS
        }

        private void ApplyCaret()
        {
            if (UIColors.HighContrast) return;
            ReleaseCaret();
            int width = Math.Max(2, (int)Math.Round(UIUtils.ScaleFactor * 1.5f)), height = Math.Max(1, PreferredHeight - 2);
            Color tint = UIColors.Primary, back = BackColor;
            using var bitmap = new Bitmap(width, height);
            using (var g = Graphics.FromImage(bitmap))
                g.Clear(Color.FromArgb(255, tint.R ^ back.R, tint.G ^ back.G, tint.B ^ back.B));
            _caretBitmap = bitmap.GetHbitmap();
            if (CreateCaret(Handle, _caretBitmap, 0, 0)) ShowCaret(Handle);
        }

        private void ReleaseCaret()
        {
            if (_caretBitmap == IntPtr.Zero) return;
            DeleteObject(_caretBitmap);
            _caretBitmap = IntPtr.Zero;
        }

        protected override void Dispose(bool disposing)
        {
            ReleaseCaret();
            base.Dispose(disposing);
        }
    }

    // Rounded field: prefix and unit are painted, only the edit box is a child window.
    public class LiteUnderlineInput : Panel
    {
        public TextBox Inner;
        private readonly string _unit, _prefix;
        private readonly Color? _accent;
        private readonly MotionValue _focus, _error;
        private int _prefixWidth, _unitWidth;
        private bool _showClear;
        private bool _clearHot;
        private const int WM_SETCUEBANNER = 0x1501;

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, string lParam);

        public string Placeholder
        {
            set { if (Inner != null && !Inner.IsDisposed) SendMessage(Inner.Handle, WM_SETCUEBANNER, IntPtr.Zero, value); }
        }

        public LiteUnderlineInput(string text, string unit = "", string labelPrefix = "", int width = 160, Color? fontColor = null, HorizontalAlignment align = HorizontalAlignment.Left)
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.Opaque, true);
            _unit = unit; _prefix = labelPrefix; _accent = fontColor;
            BackColor = UIColors.CardBg;
            Cursor = Cursors.IBeam;
            var font = UiFonts.Get(TextRole.Body);
            Inner = new ThemedTextBox
            {
                Text = text,
                BorderStyle = BorderStyle.None,
                BackColor = UIColors.FieldBg,
                Font = font,
                ForeColor = fontColor ?? UIColors.TextMain,
                TextAlign = align
            };
            _prefixWidth = string.IsNullOrEmpty(_prefix) ? 0 : TextRenderer.MeasureText(_prefix, UiFonts.Get(TextRole.Footnote), Size.Empty, Ink.TextFlags).Width + UIUtils.S(6);
            _unitWidth = string.IsNullOrEmpty(_unit) ? 0 : TextRenderer.MeasureText(_unit, UiFonts.Get(TextRole.Footnote), Size.Empty, Ink.TextFlags).Width + UIUtils.S(4);
            _focus = new MotionValue(this, "focus", SpringSpec.Fade);
            _error = new MotionValue(this, "error", SpringSpec.PressOut);
            Inner.Enter += (_, _) => { _focus.To(1); RefreshClear(); };
            Inner.Leave += (_, _) => { _focus.To(0); RefreshClear(); };
            Inner.TextChanged += (_, _) => RefreshClear();
            Controls.Add(Inner);
            int minimum = UIUtils.S(8) * 2 + _prefixWidth + _unitWidth + UIUtils.S(30);
            Size = new Size(Math.Max(UIUtils.S(width), minimum), UIUtils.S(DS.ControlHeight));
        }

        // A small clear affordance while editing a non-empty text field.
        internal bool ClearButtonEnabled { get; set; }

        private void RefreshClear()
        {
            bool show = ClearButtonEnabled && Inner.Focused && !Inner.ReadOnly && Inner.TextLength > 0;
            if (show == _showClear) return;
            _showClear = show;
            PerformLayout();
            Invalidate();
        }

        internal int ContentRightReserve { get; set; }
        private Rectangle ClearZone => new(Width - UIUtils.S(24) - ContentRightReserve, 0, UIUtils.S(24), Height);

        protected override void OnLayout(LayoutEventArgs e)
        {
            if (Inner == null) return;
            int pad = UIUtils.S(8);
            int left = pad + _prefixWidth;
            int right = Width - pad - _unitWidth - ContentRightReserve - (_showClear ? UIUtils.S(18) : 0);
            int h = Inner.PreferredHeight;
            Inner.SetBounds(left, (Height - h) / 2, Math.Max(UIUtils.S(8), right - left), h);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            bool hot = _showClear && ClearZone.Contains(e.Location);
            if (hot != _clearHot) { _clearHot = hot; Cursor = hot ? Cursors.Hand : Cursors.IBeam; Invalidate(); }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (_showClear && ClearZone.Contains(e.Location)) { Inner.Clear(); Inner.Focus(); return; }
            if (Enabled) Inner.Focus();
        }

        public void SetTextColor(Color c) => Inner.ForeColor = c;
        public void SetBg(Color c) { BackColor = c; }

        // Rejected input: a short spring shake and a brief critical tint.
        internal void SignalInvalid()
        {
            _error.Set(1);
            _error.To(0, SpringSpec.Disclosure with { Resolution = 0.004 });
            if (!InterfaceMotion.Enabled || !Visible) return;
            int home = InterfaceMotion.IsAnimating(this, "shake") ? _shakeHome : Left;
            _shakeHome = home;
            InterfaceMotion.Spring(this, "shake", new SpringSpec(0.13, 0.32, 0.5), 0, 0, v => Left = home + (int)MathF.Round(v), () => Left = home, UIUtils.S(190f));
        }
        private int _shakeHome;

        protected override void OnEnabledChanged(EventArgs e)
        {
            base.OnEnabledChanged(e);
            Inner.BackColor = UIColors.FieldBg;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.FillRectangle(Ink.B(Ink.Back(this)), e.ClipRectangle);
            float radius = UIUtils.S((float)DS.RadiusField);
            var shape = new RectangleF(0, 0, Width, Height);
            Ink.Fill(g, shape, radius, UIColors.FieldBg);
            float focus = Math.Clamp(_focus.Value, 0f, 1f), error = Math.Clamp(_error.Value, 0f, 1f);
            if (focus > 0.01f || error > 0.01f)
            {
                Color ring = UIColors.Blend(UIColors.Blend(UIColors.FieldBg, UIColors.Primary, focus), UIColors.TextCrit, error);
                Ink.Outline(g, shape, radius, ring, Math.Max(1f, UIUtils.ScaleFactor));
            }
            else if (UIColors.HighContrast) Ink.Outline(g, shape, radius, SystemColors.WindowText);
            Color label = Enabled ? UIColors.TextSub : UIColors.Blend(UIColors.FieldBg, UIColors.TextSub, 0.5f);
            int pad = UIUtils.S(8);
            if (_prefixWidth > 0)
                Ink.Line(g, _prefix, UiFonts.Get(TextRole.Footnote), new Rectangle(pad, 0, _prefixWidth, Height), Enabled ? _accent ?? label : label);
            if (_unitWidth > 0)
                Ink.Line(g, _unit, UiFonts.Get(TextRole.Footnote), new Rectangle(Width - pad - _unitWidth - ContentRightReserve, 0, _unitWidth, Height), label, TextFormatFlags.Right);
            if (_showClear)
            {
                var zone = ClearZone;
                float cx = zone.Left + zone.Width / 2f, cy = Height / 2f, r = UIUtils.S(6.5f), x = UIUtils.S(2.6f);
                var mode = g.SmoothingMode;
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                g.FillEllipse(Ink.B(_clearHot ? UIColors.TextSub : UIColors.TextTertiary), cx - r, cy - r, r * 2, r * 2);
                var pen = Ink.P(UIColors.FieldBg, Ink.LineWidth(1.3f));
                g.DrawLine(pen, cx - x, cy - x, cx + x, cy + x);
                g.DrawLine(pen, cx - x, cy + x, cx + x, cy - x);
                g.SmoothingMode = mode;
            }
            PaintExtras(g);
        }

        private protected virtual void PaintExtras(Graphics g) { }
    }

    // Numeric field with an optional stepper: drag-free -/+ zones, hold to repeat, arrow keys and
    // the wheel while focused. Out-of-range or rejected input signals instead of silently failing.
    public class LiteNumberInput : LiteUnderlineInput
    {
        private double _step, _min = double.MinValue, _max = double.MaxValue;
        private bool _stepper;
        private int _pressedZone; // -1 minus, 1 plus, 0 none
        private int _hotZone;
        private readonly System.Windows.Forms.Timer _repeat = new() { Interval = 380 };

        public LiteNumberInput(string text, string unit = "", string label = "", int width = 160, Color? color = null, int maxLength = 10)
            : base(text, unit, label, width, color, HorizontalAlignment.Center)
        {
            Inner.MaxLength = maxLength;
            Inner.KeyPress += (_, e) =>
            {
                if (char.IsControl(e.KeyChar) || char.IsDigit(e.KeyChar)) return;
                if (e.KeyChar == '.' && !Inner.Text.Contains('.')) return;
                if (e.KeyChar == '-' && Inner.SelectionStart == 0 && !Inner.Text.Contains('-')) return;
                e.Handled = true;
                SignalInvalid();
            };
            Inner.Leave += (_, _) =>
            {
                if (string.IsNullOrWhiteSpace(Inner.Text) || Inner.Text == "." || Inner.Text == "-") Inner.Text = "0";
            };
            Inner.KeyDown += (_, e) =>
            {
                if (!_stepper) return;
                if (e.KeyCode == Keys.Up) { StepBy(1); e.Handled = e.SuppressKeyPress = true; }
                else if (e.KeyCode == Keys.Down) { StepBy(-1); e.Handled = e.SuppressKeyPress = true; }
            };
            Inner.MouseWheel += (_, e) =>
            {
                if (!_stepper || !Inner.Focused) return;
                StepBy(Math.Sign(e.Delta));
                if (e is HandledMouseEventArgs handled) handled.Handled = true;
            };
            _repeat.Tick += (_, _) => { _repeat.Interval = 70; if (_pressedZone != 0) StepBy(_pressedZone); };
        }

        public int ValueInt => int.TryParse(Inner.Text, out int v) ? v : 0;
        public double ValueDouble => double.TryParse(Inner.Text, out double v) ? v : 0.0;

        internal void EnableStepper(double step, double min, double max)
        {
            _step = step; _min = min; _max = max; _stepper = true;
            ContentRightReserve = UIUtils.S(46);
            Width += ContentRightReserve;
            PerformLayout();
        }

        internal void StepBy(int direction)
        {
            double next = Math.Round(ValueDouble + direction * _step, 6);
            if (next < _min || next > _max)
            {
                next = Math.Clamp(next, _min, _max);
                if (next == ValueDouble) { SignalInvalid(); return; }
            }
            string text = next.ToString(System.Globalization.CultureInfo.CurrentCulture);
            if (Inner.Text != text) Inner.Text = text;
        }

        private Rectangle MinusZone => new(Width - UIUtils.S(44), UIUtils.S(4), UIUtils.S(21), Height - UIUtils.S(8));
        private Rectangle PlusZone => new(Width - UIUtils.S(23), UIUtils.S(4), UIUtils.S(21), Height - UIUtils.S(8));

        private int ZoneAt(Point point) => !_stepper || !Enabled ? 0 : MinusZone.Contains(point) ? -1 : PlusZone.Contains(point) ? 1 : 0;

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int zone = ZoneAt(e.Location);
            if (zone != _hotZone) { _hotZone = zone; Cursor = zone != 0 ? Cursors.Hand : Cursors.IBeam; Invalidate(); }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (_hotZone != 0) { _hotZone = 0; Invalidate(); }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            int zone = e.Button == MouseButtons.Left ? ZoneAt(e.Location) : 0;
            if (zone == 0) { base.OnMouseDown(e); return; }
            Inner.Focus();
            _pressedZone = zone;
            StepBy(zone);
            Invalidate(); Update();
            _repeat.Interval = 380;
            _repeat.Start();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            StopRepeat();
        }

        protected override void OnMouseCaptureChanged(EventArgs e)
        {
            base.OnMouseCaptureChanged(e);
            if (!Capture) StopRepeat();
        }

        private void StopRepeat()
        {
            _repeat.Stop();
            if (_pressedZone != 0) { _pressedZone = 0; Invalidate(); }
        }

        private protected override void PaintExtras(Graphics g)
        {
            if (!_stepper) return;
            float radius = UIUtils.S((float)DS.RadiusSmall);
            Color glyph = Enabled ? UIColors.TextSub : UIColors.Blend(UIColors.FieldBg, UIColors.TextSub, 0.45f);
            void Zone(Rectangle zone, int id)
            {
                Color fill = _pressedZone == id ? UIColors.FieldPressed : _hotZone == id ? UIColors.FieldHover : UIColors.FieldBg;
                if (fill != UIColors.FieldBg) Ink.Fill(g, zone, radius, fill);
                float cx = zone.Left + zone.Width / 2f, cy = zone.Top + zone.Height / 2f, r = UIUtils.S(4f);
                var mode = g.SmoothingMode;
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                var pen = Ink.P(glyph, Ink.LineWidth(1.4f));
                g.DrawLine(pen, cx - r, cy, cx + r, cy);
                if (id > 0) g.DrawLine(pen, cx, cy - r, cx, cy + r);
                g.SmoothingMode = mode;
            }
            Zone(MinusZone, -1);
            Zone(PlusZone, 1);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _repeat.Dispose();
            base.Dispose(disposing);
        }
    }

    public class LiteColorInput : Panel
    {
        public LiteUnderlineInput Input;
        public LiteColorPicker Picker;
        public string HexValue { get => Input.Inner.Text; set { Input.Inner.Text = value; Picker.SetHex(value); } }

        public LiteColorInput(string initialHex)
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            BackColor = UIColors.CardBg;
            Size = new Size(UIUtils.S(124), UIUtils.S(DS.ControlHeight));
            Picker = new LiteColorPicker(initialHex) { Size = new Size(UIUtils.S(30), UIUtils.S(22)), Location = new Point(Width - UIUtils.S(30), UIUtils.S(4)) };
            Input = new LiteUnderlineInput(initialHex, "", "", 86, null) { Location = Point.Empty };
            Picker.ColorChanged += (_, _) => Input.Inner.Text = $"#{Picker.Value.R:X2}{Picker.Value.G:X2}{Picker.Value.B:X2}";
            Input.Inner.TextChanged += (_, _) => Picker.SetHex(Input.Inner.Text);
            Controls.Add(Input);
            Controls.Add(Picker);
        }
    }

    public class LiteColorPicker : Control
    {
        private Color _color;
        public event EventHandler? ColorChanged;
        public Color Value { get => _color; set { _color = value; Invalidate(); } }

        public LiteColorPicker(string initialHex)
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.Opaque, true);
            SetHex(initialHex);
            Size = new Size(UIUtils.S(30), UIUtils.S(22));
            Cursor = Cursors.Hand;
            AccessibleRole = AccessibleRole.PushButton;
            AccessibleName = LanguageManager.T("Menu.ScreenColorPicker");
            Click += (_, _) => PickColor();
        }

        public void SetHex(string hex) { try { _color = ColorTranslator.FromHtml(hex); Invalidate(); } catch { } }

        private void PickColor()
        {
            using var dialog = new ColorDialog { Color = _color, FullOpen = true };
            if (dialog.ShowDialog() != DialogResult.OK) return;
            _color = dialog.Color;
            ColorChanged?.Invoke(this, EventArgs.Empty);
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.FillRectangle(Ink.B(Ink.Back(this)), ClientRectangle);
            float radius = UIUtils.S((float)DS.RadiusSmall);
            var shape = new RectangleF(0, 0, Width, Height);
            Color swatch = Enabled ? _color : UIColors.Blend(UIColors.CardBg, _color, 0.45f);
            Ink.Fill(g, shape, radius, swatch);
            Ink.Outline(g, shape, radius, UIColors.Border);
        }

        protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }
    }

    // =======================================================================
    // Picker
    // =======================================================================

    public class LiteComboItem
    {
        public string Text { get; set; } = "";
        public string Value { get; set; } = "";
        public override string ToString() => Text;
    }

    // A value picker: one owner-drawn window that opens a themed menu. It keeps the small part of
    // the ComboBox surface the pages use, and replaces the native combo (and its hidden list window).
    public class LiteComboBox : Control
    {
        public sealed class ItemCollection : IEnumerable<object>
        {
            private readonly List<object> _items = new();
            private readonly LiteComboBox _owner;
            internal ItemCollection(LiteComboBox owner) => _owner = owner;
            public int Count => _items.Count;
            public object this[int index] => _items[index];
            public void Add(object item) { _items.Add(item); _owner.ItemsChanged(); }
            public void AddRange(IEnumerable<object> items) { _items.AddRange(items); _owner.ItemsChanged(); }
            public void Clear() { _items.Clear(); _owner.SelectSilently(-1); _owner.ItemsChanged(); }
            public bool Contains(object? item) => item != null && _items.Contains(item);
            public int IndexOf(object? item) => item == null ? -1 : _items.IndexOf(item);
            public IEnumerator<object> GetEnumerator() => _items.GetEnumerator();
            System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => _items.GetEnumerator();
        }

        public LiteComboBox Inner => this;
        public ItemCollection Items { get; }
        public string DisplayMember { get; set; } = "";
        public string ValueMember { get; set; } = "";
        public event EventHandler? SelectedIndexChanged;
        private int _selectedIndex = -1;
        private int _updateDepth;
        private readonly MotionValue _hover, _press, _open;
        internal bool PopupOpen => LitePopupMenu.IsOpenFor(this);
        private bool _pressOpened;

        public LiteComboBox()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.Opaque | ControlStyles.Selectable, true);
            Items = new ItemCollection(this);
            _hover = new MotionValue(this, "hover", SpringSpec.Hover);
            _press = new MotionValue(this, "press", SpringSpec.PressOut);
            _open = new MotionValue(this, "open", SpringSpec.Selection);
            Size = new Size(UIUtils.S(208), UIUtils.S(DS.ControlHeight));
            BackColor = UIColors.CardBg;
            Font = UiFonts.Get(TextRole.Body);
            TabStop = true;
            Cursor = Cursors.Hand;
            AccessibleRole = AccessibleRole.ComboBox;
        }

        public void BeginUpdate() => _updateDepth++;
        public void EndUpdate() { if (--_updateDepth <= 0) { _updateDepth = 0; Invalidate(); } }

        private void ItemsChanged()
        {
            LitePopupMenu.CloseFor(this);
            if (_updateDepth == 0) Invalidate();
        }

        private void SelectSilently(int index) => _selectedIndex = index;

        public int SelectedIndex
        {
            get => _selectedIndex;
            set
            {
                int next = value >= 0 && value < Items.Count ? value : -1;
                if (next == _selectedIndex) return;
                _selectedIndex = next;
                base.Text = TextOf(next);
                if (_updateDepth == 0) Invalidate();
                AccessibilityNotifyClients(AccessibleEvents.ValueChange, -1);
                SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public object? SelectedItem
        {
            get => _selectedIndex >= 0 && _selectedIndex < Items.Count ? Items[_selectedIndex] : null;
            set => SelectedIndex = Items.IndexOf(value);
        }

        private string TextOf(int index) => index >= 0 && index < Items.Count ? Items[index]?.ToString() ?? "" : "";

        [System.Diagnostics.CodeAnalysis.AllowNull]
        public override string Text
        {
            get => TextOf(_selectedIndex);
            set
            {
                for (int i = 0; i < Items.Count; i++)
                    if (TextOf(i) == value) { SelectedIndex = i; return; }
            }
        }

        public void AddItem(string text, string value) => Items.Add(new LiteComboItem { Text = text, Value = value });

        public bool SelectValue(string value)
        {
            for (int i = 0; i < Items.Count; i++)
                if (Items[i] is LiteComboItem item && item.Value == value) { SelectedIndex = i; return true; }
            for (int i = 0; i < Items.Count; i++)
                if (Items[i] is LiteComboItem item && string.Equals(item.Text, value, StringComparison.OrdinalIgnoreCase)) { SelectedIndex = i; return true; }
            if (Items.Count > 0) SelectedIndex = 0;
            return false;
        }

        public string SelectedValue => (SelectedItem as LiteComboItem)?.Value ?? "";

        internal string[] ItemTexts()
        {
            var texts = new string[Items.Count];
            for (int i = 0; i < texts.Length; i++) texts[i] = TextOf(i);
            return texts;
        }

        internal void OpenMenu()
        {
            if (!Enabled || !Visible || Items.Count == 0 || PopupOpen) return;
            if (CanFocus) Focus();
            _open.To(1);
            LitePopupMenu.Open(this, () => _open.To(0));
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
            if (e.KeyCode == Keys.F4 || e.KeyCode == Keys.Space || (e.Alt && e.KeyCode is Keys.Down or Keys.Up)) { OpenMenu(); e.Handled = true; return; }
            int last = Items.Count - 1;
            int? next = e.KeyCode switch
            {
                Keys.Up or Keys.Left => Math.Max(0, _selectedIndex - 1),
                Keys.Down or Keys.Right => Math.Min(last, _selectedIndex + 1),
                Keys.Home => 0,
                Keys.End => last,
                Keys.PageUp => Math.Max(0, _selectedIndex - 5),
                Keys.PageDown => Math.Min(last, _selectedIndex + 5),
                _ => null
            };
            if (next is int index && last >= 0) { SelectedIndex = index; e.Handled = true; }
        }

        protected override void OnKeyPress(KeyPressEventArgs e)
        {
            base.OnKeyPress(e);
            if (char.IsControl(e.KeyChar) || e.KeyChar == ' ' || Items.Count == 0) return;
            int match = FindPrefix(ItemTexts(), e.KeyChar, _selectedIndex);
            if (match >= 0) SelectedIndex = match;
            e.Handled = true;
        }

        internal static int FindPrefix(string[] texts, char key, int after)
        {
            for (int offset = 1; offset <= texts.Length; offset++)
            {
                int i = (Math.Max(after, -1) + offset) % texts.Length;
                if (texts[i].Length > 0 && char.ToUpperInvariant(texts[i][0]) == char.ToUpperInvariant(key)) return i;
            }
            return -1;
        }

        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); if (Enabled) _hover.To(1); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hover.To(0); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left || !Enabled) return;
            _press.Press();
            if (PopupOpen) { LitePopupMenu.CloseFor(this); return; }
            OpenMenu();
            _pressOpened = PopupOpen;
        }

        // Press, drag onto an item and release selects it, like an iOS pull-down.
        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_pressOpened && Capture) LitePopupMenu.TrackPointer(PointToScreen(e.Location));
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            _press.To(0, SpringSpec.PressOut);
            if (_pressOpened) { _pressOpened = false; LitePopupMenu.ReleasePointer(PointToScreen(e.Location)); }
        }

        protected override void OnMouseCaptureChanged(EventArgs e)
        {
            base.OnMouseCaptureChanged(e);
            if (!Capture) { _press.To(0, SpringSpec.PressOut); _pressOpened = false; }
        }

        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
        protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Cursor = Enabled ? Cursors.Hand : Cursors.Default; Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Color back = Ink.Back(this);
            g.FillRectangle(Ink.B(back), ClientRectangle);
            if (Width < 2 || Height < 2) return;
            float radius = UIUtils.S((float)DS.RadiusControl);
            var shape = new RectangleF(0, 0, Width, Height);
            float hover = Enabled ? Math.Clamp(_hover.Value, 0f, 1f) : 0, press = Enabled ? Math.Clamp(_press.Value, 0f, 1f) : 0;
            float emphasis = Math.Max(Math.Clamp(_open.Value, 0f, 1f) * 0.6f, press);
            if (hover > 0.01f || emphasis > 0.01f)
                Ink.Fill(g, shape, radius, UIColors.Blend(UIColors.Blend(back, UIColors.FieldBg, hover), UIColors.FieldPressed, emphasis));
            else if (UIColors.HighContrast) Ink.Outline(g, shape, radius, SystemColors.WindowText);
            bool rtl = RightToLeft == RightToLeft.Yes;
            int glyphZone = UIUtils.S(22), pad = UIUtils.S(8);
            var text = new Rectangle(rtl ? glyphZone : pad, 0, Math.Max(0, Width - glyphZone - pad), Height);
            Color foreground = Enabled ? UIColors.TextSub : UIColors.Blend(back, UIColors.TextSub, 0.5f);
            Ink.Line(g, Text, Font, text, foreground, rtl ? TextFormatFlags.Left : TextFormatFlags.Right);
            // The up/down chevron pair of an iOS pull-down; it closes together while the menu is open.
            float cx = rtl ? glyphZone * 0.55f : Width - glyphZone * 0.55f, cy = Height / 2f, half = UIUtils.S(3.2f);
            float spread = UIUtils.S(3.4f) * (1 - 0.35f * Math.Clamp(_open.Value, 0f, 1f));
            Color glyph = Enabled ? UIColors.TextTertiary : UIColors.Blend(back, UIColors.TextTertiary, 0.5f);
            Ink.Chevron(g, new PointF(cx, cy - spread), half, -90, glyph, Ink.LineWidth(1.4f));
            Ink.Chevron(g, new PointF(cx, cy + spread), half, 90, glyph, Ink.LineWidth(1.4f));
            if (Focused && ShowFocusCues) Ink.FocusRing(g, shape, radius);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) LitePopupMenu.CloseFor(this);
            base.Dispose(disposing);
        }

        protected override AccessibleObject CreateAccessibilityInstance() => new PickerAccessible(this);

        private sealed class PickerAccessible : ControlAccessibleObject
        {
            private readonly LiteComboBox _picker;
            public PickerAccessible(LiteComboBox picker) : base(picker) => _picker = picker;
            public override AccessibleRole Role => AccessibleRole.ComboBox;
            public override string? Value { get => _picker.Text; set { if (value != null) _picker.Text = value; } }
            public override string DefaultAction => "Open";
            public override void DoDefaultAction() => _picker.OpenMenu();
            public override AccessibleStates State => base.State | AccessibleStates.HasPopup | (_picker.PopupOpen ? AccessibleStates.Expanded : AccessibleStates.Collapsed);
        }
    }

    // =======================================================================
    // Switch
    // =======================================================================

    // iOS switch: tap toggles, the thumb can be dragged or flicked, and it stretches while held.
    // Bindings still change once per action through CheckBox.CheckedChanged.
    public class LiteCheck : CheckBox
    {
        private float _position;
        private readonly MotionValue _grip, _hoverMotion;
        private readonly Action<float> _positionFrame;
        private int _pressX;
        private float _pressPosition;
        private bool _tracking, _dragging;
        private bool? _dragResult;
        private float _velocity;
        private long _lastMove;
        private float _lastPosition;

        internal float Position => _position;
        internal bool IsDragging => _dragging;

        public LiteCheck(bool val, string text = "")
        {
            _grip = new MotionValue(this, "grip", SpringSpec.Stretch);
            _hoverMotion = new MotionValue(this, "hover", SpringSpec.Hover);
            _positionFrame = value => { _position = value; Invalidate(); };
            FlatStyle = FlatStyle.Flat;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Opaque, true);
            Checked = val;
            _position = val ? 1 : 0;
            AutoSize = true;
            Cursor = Cursors.Hand;
            Text = text;
            BackColor = UIColors.CardBg;
            ForeColor = UIColors.TextSub;
            Font = UiFonts.Get(TextRole.Body);
            Size = GetPreferredSize(Size.Empty);
        }

        private int S(int value) => UIUtils.S(value);
        private float Travel => Math.Max(1, S(DS.SwitchWidth) - S(DS.SwitchInset) * 2 - (S(DS.SwitchHeight) - S(DS.SwitchInset) * 2));
        private bool Rtl => RightToLeft == RightToLeft.Yes;

        public override Size GetPreferredSize(Size proposedSize)
        {
            int labelWidth = string.IsNullOrEmpty(Text) ? 0 : TextRenderer.MeasureText(Text, Font, Size.Empty, Ink.TextFlags).Width + S(8);
            return new Size(S(DS.SwitchWidth) + labelWidth, Math.Max(S(DS.SwitchHeight) + S(2), Font.Height));
        }

        protected override void OnPaintBackground(PaintEventArgs e) => e.Graphics.FillRectangle(Ink.B(Ink.Back(this)), ClientRectangle);

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Color back = Ink.Back(this);
            g.FillRectangle(Ink.B(back), ClientRectangle);
            int trackWidth = S(DS.SwitchWidth), trackHeight = S(DS.SwitchHeight), inset = S(DS.SwitchInset);
            var track = new RectangleF(Rtl ? Width - trackWidth : 0, (Height - trackHeight) / 2f, trackWidth, trackHeight);
            float tint = Math.Clamp(_position, 0f, 1f);
            Color offColor = UIColors.Blend(back, UIColors.TextMain, 0.18f + 0.06f * Math.Clamp(_hoverMotion.Value, 0f, 1f));
            Color trackColor = UIColors.Blend(offColor, UIColors.Primary, tint);
            if (!Enabled) trackColor = UIColors.Blend(back, trackColor, 0.45f);
            Ink.Fill(g, track, trackHeight / 2f, trackColor);
            bool contrast = UIColors.HighContrast;
            if (contrast) Ink.Outline(g, track, trackHeight / 2f, SystemColors.WindowText);
            // Overshoot past either end compresses into a thumb stretch instead of leaving the track.
            float knob = trackHeight - inset * 2f;
            float over = _position > 1 ? _position - 1 : _position < 0 ? -_position : 0;
            float width = knob + S(5) * Math.Clamp(_grip.Value, 0f, 1f) + over * S(16);
            float travel = trackWidth - inset * 2f - width;
            float along = Rtl ? 1 - tint : tint;
            float x = CheckState == CheckState.Indeterminate
                ? track.Left + (trackWidth - width) / 2f
                : track.Left + inset + travel * along;
            var thumb = new RectangleF(x, track.Top + inset, width, knob);
            if (Enabled && !contrast)
            {
                Ink.Fill(g, new RectangleF(thumb.X - 0.5f, thumb.Y + S(1) * 0.5f, thumb.Width + 1, thumb.Height + 0.5f), knob / 2f, Color.FromArgb(UIColors.IsDark ? 46 : 22, 0, 0, 0));
                Ink.Fill(g, new RectangleF(thumb.X, thumb.Y + S(1), thumb.Width, thumb.Height), knob / 2f, Color.FromArgb(UIColors.IsDark ? 70 : 34, 0, 0, 0));
            }
            Color thumbColor = contrast ? (tint > 0.5f ? SystemColors.HighlightText : SystemColors.WindowText)
                : Enabled ? Color.White : UIColors.Blend(back, Color.White, 0.6f);
            Ink.Fill(g, thumb, knob / 2f, thumbColor);
            if (!string.IsNullOrEmpty(Text))
            {
                int gap = S(8);
                var label = Rtl ? new Rectangle(0, 0, Math.Max(0, (int)track.Left - gap), Height)
                    : new Rectangle((int)track.Right + gap, 0, Math.Max(0, Width - (int)track.Right - gap), Height);
                Ink.Line(g, Text, Font, label, Enabled ? ForeColor : UIColors.TextTertiary, Rtl ? TextFormatFlags.Right : TextFormatFlags.Left);
            }
            if (Focused && ShowFocusCues) Ink.FocusRing(g, RectangleF.Inflate(track, S(2), S(2)), (trackHeight + S(4)) / 2f);
        }

        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); _hoverMotion.To(1); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hoverMotion.To(0); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left || !Enabled) return;
            _tracking = true; _dragging = false; _dragResult = null;
            _pressX = e.X; _pressPosition = Math.Clamp(_position, 0f, 1f);
            _velocity = 0; _lastMove = System.Diagnostics.Stopwatch.GetTimestamp(); _lastPosition = _position;
            _grip.Press(0.6f);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!_tracking || !Enabled) return;
            int dx = e.X - _pressX;
            if (!_dragging && Math.Abs(dx) < S(3)) return;
            if (!_dragging)
            {
                _dragging = true;
                InterfaceMotion.Cancel(this, "switch");
            }
            float direction = Rtl ? -1 : 1;
            float next = Math.Clamp(_pressPosition + direction * dx / Travel, 0f, 1f);
            long now = System.Diagnostics.Stopwatch.GetTimestamp();
            double dt = System.Diagnostics.Stopwatch.GetElapsedTime(_lastMove, now).TotalSeconds;
            if (dt > 0.001) { _velocity = _velocity * 0.4f + (float)((next - _lastPosition) / dt) * 0.6f; _lastMove = now; _lastPosition = next; }
            _position = next;
            Invalidate();
            Update(); // the thumb stays under the pointer within the input event
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (_tracking && _dragging)
            {
                // A flick decides by velocity; a slow release by which half the thumb is in.
                _dragResult = Math.Abs(_velocity) > 4f ? _velocity > 0 : _position >= 0.5f;
            }
            _tracking = false;
            base.OnMouseUp(e); // runs OnClick when released over the control
            if (_dragResult != null) OnClick(EventArgs.Empty); // released elsewhere: the drag still decides
            _dragging = false;
            _grip.To(0, SpringSpec.PressOut);
        }

        protected override void OnMouseCaptureChanged(EventArgs e)
        {
            base.OnMouseCaptureChanged(e);
            if (Capture) return;
            if (_tracking && _dragging) AnimatePosition(); // lost capture mid-drag: back to the true state
            _tracking = _dragging = false;
            _grip.To(0, SpringSpec.PressOut);
        }

        protected override void OnClick(EventArgs e)
        {
            if (_dragResult is not bool target) { base.OnClick(e); return; }
            _dragResult = null;
            bool auto = AutoCheck;
            AutoCheck = false; // CheckBox.OnClick must not toggle a drag's decided value
            try
            {
                if (Checked != target) Checked = target; else AnimatePosition();
                base.OnClick(e);
            }
            finally { AutoCheck = auto; }
        }

        private void AnimatePosition()
        {
            float target = CheckState == CheckState.Indeterminate ? 0.5f : Checked ? 1 : 0;
            InterfaceMotion.Spring(this, "switch", SpringSpec.Toggle, _position, target, _positionFrame);
        }

        protected override void OnCheckStateChanged(EventArgs e)
        {
            base.OnCheckStateChanged(e);
            AnimatePosition();
        }

        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
    }

    // =======================================================================
    // Buttons
    // =======================================================================

    public class LiteButton : Button
    {
        private readonly bool _dashed;
        private readonly bool _primary;
        private bool _hovered;
        private bool _mousePressed;
        private bool _keyPressed;
        private readonly MotionValue _hoverMotion, _pressMotion;
        private float PressAmount => Math.Clamp(_pressMotion.Value, 0f, 1f);

        public LiteButton(string t, bool p = false, bool dashed = false)
        {
            Text = t;
            _pressMotion = new MotionValue(this, "press", SpringSpec.PressOut);
            _hoverMotion = new MotionValue(this, "hover", SpringSpec.Hover);
            _dashed = dashed;
            _primary = p;
            Size = new Size(UIUtils.S(DS.ButtonMinWidth), UIUtils.S(DS.ButtonHeight));
            FlatStyle = FlatStyle.Flat;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Opaque, true);
            Cursor = Cursors.Hand;
            Font = UiFonts.Get(p ? TextRole.BodyStrong : TextRole.Body);
            FlatAppearance.BorderSize = 0;
            FlatAppearance.MouseOverBackColor = p ? UIColors.Blend(UIColors.Primary, UIColors.OnPrimary, 0.08f) : UIColors.ButtonHover;
            FlatAppearance.MouseDownBackColor = p ? UIColors.Blend(UIColors.Primary, UIColors.OnPrimary, 0.16f) : UIColors.ButtonPressed;
            if (p)
            {
                BackColor = UIColors.Primary;
                ForeColor = UIColors.OnPrimary;
            }
            else if (dashed)
            {
                BackColor = Color.Transparent;
                ForeColor = UIColors.TextSub;
            }
            else
            {
                BackColor = UIColors.ButtonFill;
                ForeColor = UIColors.TextMain;
                FlatAppearance.BorderColor = UIColors.Border;
            }
        }

        protected override void OnPaintBackground(PaintEventArgs e) => e.Graphics.FillRectangle(Ink.B(Ink.Back(this)), ClientRectangle);

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Color outside = Ink.Back(this);
            g.FillRectangle(Ink.B(outside), ClientRectangle);
            if (Width < 2 || Height < 2) return;
            Color fill = BackColor.A == 255 ? BackColor : outside;
            float press = 0;
            if (Enabled)
            {
                press = PressAmount;
                fill = UIColors.Blend(fill, FlatAppearance.MouseOverBackColor, Math.Clamp(_hoverMotion.Value, 0f, 1f));
                fill = UIColors.Blend(fill, FlatAppearance.MouseDownBackColor, press);
            }
            else fill = UIColors.Blend(outside, fill, 0.45f);
            // iOS-style press: the shape sinks slightly toward its centre. Text stays put, so its
            // hinted GDI glyphs do not change weight mid-press.
            float inset = Math.Min(Width, Height) * 0.035f * press + 0.0f;
            float sinkX = Width * 0.0175f * press;
            var shape = new RectangleF(sinkX, inset / 2f, Width - sinkX * 2, Height - inset);
            float radius = UIUtils.S((float)DS.RadiusControl + 1);
            Ink.Fill(g, shape, radius, fill);
            if (_dashed)
            {
                var mode = g.SmoothingMode;
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                using var pen = new Pen(UIColors.Border) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dash };
                g.DrawPath(pen, Ink.Round(RectangleF.Inflate(shape, -0.5f, -0.5f), radius));
                g.SmoothingMode = mode;
            }
            else if (UIColors.HighContrast) Ink.Outline(g, shape, radius, SystemColors.WindowText);
            Color text = Enabled ? ForeColor : UIColors.Blend(fill, ForeColor, 0.5f);
            var flags = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis |
                TextFormatFlags.PreserveGraphicsClipping | (UseMnemonic && ShowKeyboardCues ? 0 : TextFormatFlags.HidePrefix);
            if (!UseMnemonic) flags |= TextFormatFlags.NoPrefix;
            TextRenderer.DrawText(g, Text, Font, Rectangle.Inflate(ClientRectangle, -UIUtils.S(6), 0), text, flags);
            if (Focused && ShowFocusCues)
            {
                float ring = UIUtils.S(2f);
                if (_primary)
                {
                    var mode = g.SmoothingMode;
                    g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                    g.DrawPath(Ink.P(Color.FromArgb(190, UIColors.OnPrimary), Math.Max(1.5f, UIUtils.ScaleFactor * 1.5f)), Ink.Round(RectangleF.Inflate(shape, -ring, -ring), radius - ring));
                    g.SmoothingMode = mode;
                }
                else Ink.FocusRing(g, RectangleF.Inflate(shape, -ring / 2f, -ring / 2f), radius);
            }
        }

        private void UpdatePress()
        {
            bool pressed = Enabled && (_keyPressed || (_mousePressed && _hovered));
            if (pressed) _pressMotion.Press(); else _pressMotion.To(0, SpringSpec.PressOut);
        }

        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); _hovered = true; _hoverMotion.To(1); UpdatePress(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hovered = false; _hoverMotion.To(0); UpdatePress(); }
        protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); if (e.Button == MouseButtons.Left) { _mousePressed = true; _hovered = true; } UpdatePress(); }
        protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); _mousePressed = false; UpdatePress(); }
        protected override void OnMouseCaptureChanged(EventArgs e) { base.OnMouseCaptureChanged(e); if (!Capture) _mousePressed = false; UpdatePress(); }
        protected override void OnKeyDown(KeyEventArgs e) { base.OnKeyDown(e); if (e.KeyCode == Keys.Space) _keyPressed = true; UpdatePress(); }
        protected override void OnKeyUp(KeyEventArgs e) { base.OnKeyUp(e); if (e.KeyCode == Keys.Space) _keyPressed = false; UpdatePress(); }
        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); _keyPressed = false; UpdatePress(); Invalidate(); }
        protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); _keyPressed = _mousePressed = false; UpdatePress(); Invalidate(); }
    }

    // Quiet move up/down command. Reveal lets a row keep them calm until hover or keyboard focus.
    public class LiteSortBtn : Button
    {
        private bool _pressed;
        private readonly MotionValue _hover;
        internal float Reveal { get; set; } = 1;

        public LiteSortBtn(string text)
        {
            Text = text;
            _hover = new MotionValue(this, "hover", SpringSpec.Hover);
            AccessibleName = text == "▲" ? LanguageManager.T("Menu.MoveUp") : text == "▼" ? LanguageManager.T("Menu.MoveDown") : LanguageManager.T("Menu.ScreenColorPicker");
            Size = UIUtils.S(new Size(26, 26));
            FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0;
            BackColor = UIColors.CardBg; ForeColor = UIColors.TextSub;
            Cursor = Cursors.Hand; Margin = Padding.Empty;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.Opaque, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Color background = Ink.Back(this);
            g.FillRectangle(Ink.B(background), ClientRectangle);
            float reveal = Focused ? 1 : Math.Clamp(Reveal, 0f, 1f);
            if (reveal <= 0.01f) return;
            var shape = RectangleF.Inflate(new RectangleF(0, 0, Width, Height), -1, -1);
            if (Enabled && (_hover.Value > 0.01f || _pressed))
                Ink.Fill(g, shape, UIUtils.S((float)DS.RadiusSmall), _pressed ? UIColors.FieldPressed : UIColors.Blend(background, UIColors.FieldHover, Math.Clamp(_hover.Value, 0f, 1f)));
            Color ink = Enabled ? UIColors.TextSub : UIColors.Blend(background, UIColors.TextMain, .22f);
            ink = UIColors.Blend(background, ink, reveal);
            var center = new PointF(Width / 2f, Height / 2f);
            if (Text is "▲" or "▼")
                Ink.Chevron(g, center, UIUtils.S(3.6f), Text == "▲" ? -90 : 90, ink, Ink.LineWidth(1.5f));
            else DrawDropper(g, center, ink);
            if (Focused && ShowFocusCues) Ink.FocusRing(g, shape, UIUtils.S((float)DS.RadiusSmall));
        }

        private static void DrawDropper(Graphics g, PointF c, Color ink)
        {
            var mode = g.SmoothingMode;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            var pen = Ink.P(ink, Ink.LineWidth(1.5f));
            float s = UIUtils.S(1f);
            g.DrawLine(pen, c.X - 5 * s, c.Y + 5 * s, c.X + 2 * s, c.Y - 2 * s);
            g.DrawLine(pen, c.X - 1 * s, c.Y - 5 * s, c.X + 5 * s, c.Y + 1 * s);
            g.DrawEllipse(pen, c.X + 1 * s, c.Y - 5 * s, 4 * s, 4 * s);
            g.SmoothingMode = mode;
        }

        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); _hover.To(1); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hover.To(0); }
        protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); _pressed = e.Button == MouseButtons.Left; Invalidate(); Update(); }
        protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); _pressed = false; Invalidate(); }
        protected override void OnMouseCaptureChanged(EventArgs e) { base.OnMouseCaptureChanged(e); if (!Capture) _pressed = false; Invalidate(); }
        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); _pressed = false; Invalidate(); }
        protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); _pressed = false; Invalidate(); }
    }
}
