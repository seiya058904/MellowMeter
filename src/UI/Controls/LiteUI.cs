using System;
using System.Drawing;
using System.Windows.Forms;
using LiteMonitor.src.Core; // 确保引用了 UIUtils
using LiteMonitor.src.UI.SettingsPage;

namespace LiteMonitor.src.UI.Controls
{
    public static class UIColors
    {
        private static string? _accentSource;
        private static string? _accentBackground;
        private static Color _accent;
        public static bool IsDark => ThemeManager.ParseColor(ThemeManager.Current.Color.Background).GetBrightness() < 0.5f;
        public static Color MainBg => ThemeManager.ParseColor(ThemeManager.Current.Color.Background);
        public static Color SidebarBg => MainBg;
        public static Color CardBg => ThemeManager.ParseColor(ThemeManager.Current.Color.GroupBackground);
        public static Color Border => Blend(CardBg, TextSub, 0.22f);
        public static Color Primary
        {
            get
            {
                var colors = ThemeManager.Current.Color;
                if (_accentSource != colors.BarLow || _accentBackground != colors.GroupBackground)
                {
                    _accentSource = colors.BarLow;
                    _accentBackground = colors.GroupBackground;
                    Color source = ThemeManager.ParseColor(colors.BarLow);
                    Color background = CardBg;
                    _accent = EnsureContrast(source, background);
                }
                return _accent;
            }
        }
        public static Color SwatchText(Color background) => Contrast(Color.Black, background) >= Contrast(Color.White, background) ? Color.Black : Color.White;
        public static Color OnPrimary => Contrast(Primary, Color.Black) >= Contrast(Primary, Color.White) ? Color.Black : Color.White;
        public static Color TextMain => ThemeManager.ParseColor(ThemeManager.Current.Color.TextPrimary);
        public static Color TextSub => EnsureContrast(Blend(CardBg, TextMain, 0.66f), CardBg);
        public static Color FieldBg => Blend(CardBg, TextMain, IsDark ? 0.07f : 0.045f);
        public static Color GroupHeader => CardBg;
        public static Color NavSelected => Blend(MainBg, Primary, 0.16f);
        public static Color NavHover => Blend(MainBg, Primary, 0.08f);
        public static Color TextWarn => ThemeManager.ParseColor(ThemeManager.Current.Color.ValueWarn);
        public static Color TextCrit => ThemeManager.ParseColor(ThemeManager.Current.Color.ValueCrit);

        public static Color Blend(Color background, Color foreground, float amount) => Color.FromArgb(
            (int)Math.Round(background.R + (foreground.R - background.R) * amount),
            (int)Math.Round(background.G + (foreground.G - background.G) * amount),
            (int)Math.Round(background.B + (foreground.B - background.B) * amount));

        private static double Luminance(Color color)
        {
            static double Channel(byte channel)
            {
                double value = channel / 255d;
                return value <= 0.04045 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
            }
            return 0.2126 * Channel(color.R) + 0.7152 * Channel(color.G) + 0.0722 * Channel(color.B);
        }

        private static double Contrast(Color first, Color second)
        {
            double a = Luminance(first), b = Luminance(second);
            return (Math.Max(a, b) + 0.05) / (Math.Min(a, b) + 0.05);
        }

        public static Color EnsureContrast(Color source, Color background)
        {
            Color endpoint = Luminance(background) < 0.18 ? Color.White : Color.Black;
            Color result = source;
            for (int step = 1; step <= 20 && Contrast(result, background) < 4.5; step++)
                result = Blend(source, endpoint, step / 20f);
            return result;
        }
    }

    public class LiteActionRow : Panel
    {
        public Label Label { get; private set; }
        public Control RightControl { get; private set; }

        public LiteActionRow(string title, Control rightControl)
        {
            this.Height = UIUtils.S(40);
            this.Margin = new Padding(0); // Full width item
            this.Padding = new Padding(0);

            Label = new Label {
                Text = title,
                AutoSize = true,
                Font = new Font("Segoe UI", 9F),
                ForeColor = UIColors.TextSub, // Slightly gray for descriptions/tips
                TextAlign = ContentAlignment.MiddleLeft
            };

            RightControl = rightControl;

            this.Controls.Add(RightControl);
            this.Controls.Add(Label);

            this.Layout += (s, e) => {
                int mid = this.Height / 2;
                Label.Location = new Point(UIUtils.S(0), mid - Label.Height / 2); // Indent slightly

                // Position RightControl on the right
                if (RightControl.Dock != DockStyle.Fill && RightControl.Dock != DockStyle.Top && RightControl.Dock != DockStyle.Bottom)
                {
                    RightControl.Location = new Point(this.Width - RightControl.Width - UIUtils.S(5), mid - RightControl.Height / 2);
                }
            };
        }
    }

    public class LiteThresholdRow : Panel
    {
        public LiteThresholdRow(SettingsPageBase page, string title, string unit, ValueRange range)
        {
            this.Height = UIUtils.S(40);
            this.Margin = new Padding(0);
            this.Padding = new Padding(0);

            // Title
            var lblTitle = new Label {
                Text = title, AutoSize = true,
                Font = new Font("Segoe UI", 9F), ForeColor = UIColors.TextMain,
                TextAlign = ContentAlignment.MiddleLeft
            };
            this.Controls.Add(lblTitle);

            // Right Container
            var rightBox = new FlowLayoutPanel {
                AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                FlowDirection = FlowDirection.LeftToRight, WrapContents = false,
                BackColor = Color.Transparent, Padding = new Padding(0)
            };

            // Inputs
            var inputWarn = new LiteNumberInput(range.Warn.ToString(), unit, LanguageManager.T("Menu.ValueWarnColor"), 140, UIColors.TextWarn);
            inputWarn.Padding = UIUtils.S(new Padding(0, 5, 0, 1));

            var arrow = new Label { Text = "➜", AutoSize = true, ForeColor = UIColors.Border, Font = new Font("Segoe UI", 9F), Margin = UIUtils.S(new Padding(5, 4, 5, 0)) };

            var inputCrit = new LiteNumberInput(range.Crit.ToString(), unit, LanguageManager.T("Menu.ValueCritColor"), 140, UIColors.TextCrit);
            inputCrit.Padding = UIUtils.S(new Padding(0, 5, 0, 1));

            // Immediate Binding
            inputWarn.Inner.TextChanged += (s, e) => range.Warn = inputWarn.ValueDouble;
            inputCrit.Inner.TextChanged += (s, e) => range.Crit = inputCrit.ValueDouble;

            page.RegisterRefresh(() => {
                inputWarn.Inner.Text = range.Warn.ToString();
                inputCrit.Inner.Text = range.Crit.ToString();
            });

            rightBox.Controls.Add(inputWarn);
            rightBox.Controls.Add(arrow);
            rightBox.Controls.Add(inputCrit);
            this.Controls.Add(rightBox);

            // Layout
            this.Layout += (s, e) => {
                lblTitle.Location = new Point(UIUtils.S(5), (this.Height - lblTitle.Height) / 2);
                rightBox.Location = new Point(this.Width - rightBox.Width - UIUtils.S(5), (this.Height - rightBox.Height) / 2);
            };

            // Separator Line
            this.Paint += (s, e) => {
                using(var p = new Pen(UIColors.Border)) e.Graphics.DrawLine(p, 0, Height-1, Width, Height-1);
            };
        }
    }

    public class LiteHintRow : Panel
    {
        public LiteHintRow(string text, int indent = 0)
        {
            this.Dock = DockStyle.Top;
            this.Margin = new Padding(0);
            this.Padding = UIUtils.S(new Padding(indent, 5, 5, 5));
            this.AutoSize = true;

            var lbl = new Label {
                Text = text,
                AutoSize = true,
                MaximumSize = new Size(UIUtils.S(500), 0), // Limit width to trigger wrap? Actual width set in Layout
                Font = new Font("Segoe UI", 8F),
                ForeColor = UIColors.TextSub,
                Dock = DockStyle.Fill
            };
            this.Controls.Add(lbl);

            // Dynamic Height based on text
            this.Resize += (s, e) => {
                lbl.MaximumSize = new Size(this.Width - this.Padding.Horizontal, 0);
            };
        }
    }

    public static class UIFonts
    {
        public static Font Regular(float size) => new Font("Segoe UI", size, FontStyle.Regular);
        public static Font Bold(float size) => new Font("Segoe UI", size, FontStyle.Bold);
    }

    // =======================================================================
    // 1. 容器组件
    // =======================================================================

    public class LiteSettingsGroup : Panel
    {
        private readonly TableLayoutPanel _layout;
        private readonly Panel _header;
        private readonly Button _titleButton;
        private readonly RoundedGroupBody _body;
        private bool _collapsible;
        private bool _collapsed;

        public bool Collapsible
        {
            get => _collapsible;
            set
            {
                _collapsible = value;
                _titleButton.TabStop = value;
                _titleButton.Cursor = value ? Cursors.Hand : Cursors.Default;
                _titleButton.Padding = UIUtils.S(new Padding(value ? 26 : 12, 0, 0, 0));
                if (!value) Collapsed = false;
                _titleButton.Invalidate();
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
                _body.Visible = !next;
                _titleButton.AccessibleDescription = next ? "Collapsed" : "Expanded";
                _titleButton.Invalidate();
                PerformLayout();
                Parent?.PerformLayout();
            }
        }

        public LiteSettingsGroup(string title)
        {
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            Dock = DockStyle.Top;
            BackColor = UIColors.MainBg;
            Margin = UIUtils.S(new Padding(0, 0, 0, 12));
            _header = new Panel { Dock = DockStyle.Top, Height = UIUtils.S(30), BackColor = UIColors.MainBg };
            _titleButton = new Button {
                Text = title, AccessibleName = title, Dock = DockStyle.Fill,
                FlatStyle = FlatStyle.Flat, TabStop = false, TextAlign = ContentAlignment.MiddleLeft,
                Padding = UIUtils.S(new Padding(12, 0, 0, 0)), BackColor = UIColors.MainBg,
                ForeColor = UIColors.TextSub, Font = new Font("Segoe UI", 9F, FontStyle.Bold)
            };
            _titleButton.FlatAppearance.BorderSize = 0;
            _titleButton.FlatAppearance.MouseOverBackColor = UIColors.MainBg;
            _titleButton.FlatAppearance.MouseDownBackColor = UIColors.MainBg;
            _titleButton.Click += (_, _) => { if (Collapsible) Collapsed = !Collapsed; };
            _titleButton.Paint += (_, e) => {
                if (!Collapsible) return;
                float x = UIUtils.S(14), y = _titleButton.Height / 2f, r = UIUtils.S(3);
                e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                using var pen = new Pen(UIColors.TextSub, Math.Max(1f, UIUtils.ScaleFactor));
                e.Graphics.DrawLines(pen, Collapsed
                    ? new[] { new PointF(x-r/2,y-r), new PointF(x+r/2,y), new PointF(x-r/2,y+r) }
                    : new[] { new PointF(x-r,y-r/2), new PointF(x,y+r/2), new PointF(x+r,y-r/2) });
            };
            _header.Controls.Add(_titleButton);
            _body = new RoundedGroupBody { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = UIUtils.S(new Padding(14, 6, 14, 6)) };
            _layout = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 1, RowCount = 0, GrowStyle = TableLayoutPanelGrowStyle.AddRows,
                Margin = Padding.Empty, Padding = Padding.Empty, BackColor = UIColors.CardBg };
            _layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            _body.Controls.Add(_layout);
            Controls.Add(_body);
            Controls.Add(_header);
        }

        public void AddHeaderAction(Control action)
        {
            var wrapper = new Panel { Dock = DockStyle.Right, Width = action.Width + UIUtils.S(10), BackColor = UIColors.MainBg };
            action.Location = new Point(0, Math.Max(0, (_header.Height - action.Height) / 2));
            wrapper.Controls.Add(action);
            _header.Controls.Add(wrapper);
            _titleButton.BringToFront();
        }

        public void AddItem(Control item)
        {
            item.Dock = DockStyle.Fill;
            item.Margin = Padding.Empty;
            int row = _layout.Controls.Count;
            _layout.RowCount = row + 1;
            _layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            _layout.Controls.Add(item, 0, row);
        }

        public void AddFullItem(Control item) => AddItem(item);

        private sealed class RoundedGroupBody : Panel
        {
            public RoundedGroupBody() { DoubleBuffered = true; BackColor = UIColors.MainBg; }
            protected override void OnPaintBackground(PaintEventArgs e)
            {
                using var outer = new SolidBrush(UIColors.MainBg);
                e.Graphics.FillRectangle(outer, ClientRectangle);
                e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                using var path = UIUtils.RoundRect(new Rectangle(0, 0, Math.Max(0, Width-1), Math.Max(0, Height-1)), UIUtils.S(13));
                using var surface = new SolidBrush(UIColors.CardBg);
                e.Graphics.FillPath(surface, path);
            }
        }
    }

    public class LiteSettingsItem : Panel
    {
        public Label Label { get; private set; }

        public LiteSettingsItem(string text, Control ctrl)
        {
            Height = UIUtils.S(40);
            Margin = Padding.Empty;
            BackColor = UIColors.CardBg;
            Label = new Label { Text = text, AutoSize = false, AutoEllipsis = true,
                Font = new Font("Segoe UI", 9F), ForeColor = UIColors.TextMain,
                TextAlign = ContentAlignment.MiddleLeft };
            if (ctrl is LiteCheck check && !string.IsNullOrWhiteSpace(text))
            {
                check.AccessibleName = text;
                check.Text = "";
                check.Size = check.GetPreferredSize(Size.Empty);
            }
            Controls.Add(Label);
            Controls.Add(ctrl);
            int preferredWidth = ctrl.Width;
            Layout += (_, _) => {
                int gap = UIUtils.S(14);
                int maxControl = Math.Max(UIUtils.S(36), Width * 3 / 5);
                if (!ctrl.AutoSize) ctrl.Width = Math.Min(preferredWidth, maxControl);
                int rightX = Math.Max(0, Width - ctrl.Width);
                ctrl.Location = new Point(rightX, Math.Max(0, (Height - ctrl.Height) / 2));
                Label.Bounds = new Rectangle(0, 0, Math.Max(0, rightX - gap), Math.Max(0, Height - 1));
            };
            Paint += (_, e) => {
                if (Parent is TableLayoutPanel table && table.GetRow(this) == table.RowCount - 1) return;
                using var pen = new Pen(UIColors.Blend(UIColors.CardBg, UIColors.TextMain, 0.10f));
                e.Graphics.DrawLine(pen, 0, Height-1, Width, Height-1);
            };
        }
    }
    public class LiteCard : Panel
    {
        public LiteCard() { DoubleBuffered = true; BackColor = UIColors.CardBg; AutoSize = true; AutoSizeMode = AutoSizeMode.GrowAndShrink; Dock = DockStyle.Top; Padding = UIUtils.S(new Padding(1, 10, 1, 10)); }
        protected override void OnPaintBackground(PaintEventArgs e)
        {
            using var outer = new SolidBrush(UIColors.MainBg);
            e.Graphics.FillRectangle(outer, ClientRectangle);
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using var path = UIUtils.RoundRect(new Rectangle(0, 0, Math.Max(0, Width-1), Math.Max(0, Height-1)), UIUtils.S(13));
            using var inner = new SolidBrush(UIColors.CardBg);
            e.Graphics.FillPath(inner, path);
        }
    }

    // =======================================================================
    // 2. 交互组件
    // =======================================================================

    // ★★★ [优化版] 下划线输入框：支持前置标签 ★★★
    public class LiteUnderlineInput : Panel
    {
        public TextBox Inner;
        private Label? _lblUnit;   // 单位 (右侧)
        private Label? _lblLabel;  // 标签 (左侧)

        private const int EM_SETCUEBANNER = 0x1501;
        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto)]
        private static extern Int32 SendMessage(IntPtr hWnd, int msg, int wParam, [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPWStr)] string lParam);

        public string Placeholder
        {
            set
            {
                if (Inner != null && !Inner.IsDisposed)
                {
                    SendMessage(Inner.Handle, EM_SETCUEBANNER, 0, value);
                }
            }
        }

        public LiteUnderlineInput(string text, string unit = "", string labelPrefix = "", int width = 160, Color? fontColor = null, HorizontalAlignment align = HorizontalAlignment.Left)
        {
            // ★★★ 修改：Size/Padding 缩放
            this.Size = new Size(UIUtils.S(width), UIUtils.S(26));
            this.BackColor = UIColors.CardBg;
            this.Padding = UIUtils.S(new Padding(8, 4, 8, 4));
            this.Cursor = Cursors.IBeam;

            // 1. 创建并添加输入框 (垫底)
            Inner = new TextBox {
                Text = text,
                BorderStyle = BorderStyle.None,
                Dock = DockStyle.Fill,
                BackColor = UIColors.FieldBg,
                Font = new Font("Segoe UI", 9F, FontStyle.Regular),
                ForeColor = fontColor ?? UIColors.TextSub,
                TextAlign = align
            };
            // this.Controls.Add(Inner); // Moved to end to ensure correct Dock order (Inner should be last Docked => Front of Z-Order)

            // 2. 添加单位 (Dock Right, 浮在右边)
            if (!string.IsNullOrEmpty(unit))
            {
                _lblUnit = new Label {
                    Text = unit,
                    AutoSize = true,
                    Dock = DockStyle.Right,
                    Font = new Font("Segoe UI", 8F),
                    ForeColor = UIColors.TextSub,
                    TextAlign = ContentAlignment.MiddleRight,
                    BackColor = UIColors.FieldBg,
                    Padding = Padding.Empty
                };
                this.Controls.Add(_lblUnit);
                _lblUnit.Click += (s, e) => Inner.Focus();
            }

            // 3. 添加前置标签 (Dock Left, 浮在左边)
            if (!string.IsNullOrEmpty(labelPrefix))
            {
                _lblLabel = new Label {
                    Text = labelPrefix,
                    AutoSize = true,
                    Dock = DockStyle.Left,
                    Font = new Font("Segoe UI", 9F),
                    ForeColor = fontColor ?? UIColors.TextSub,
                    TextAlign = ContentAlignment.MiddleLeft,
                    BackColor = UIColors.FieldBg,
                    Padding = UIUtils.S(new Padding(0, 0, 4, 0))
                };
                this.Controls.Add(_lblLabel);
                _lblLabel.Click += (s, e) => Inner.Focus();
            }

            // Add Inner last so it is at the Front of Z-Order (Index 0),
            // which means it is docked LAST (filling remaining space).
            this.Controls.Add(Inner);

            // 事件转发
            Inner.Enter += (s, e) => this.Invalidate();
            Inner.Leave += (s, e) => this.Invalidate();
            this.Click += (s, e) => Inner.Focus();

            // ★★★ Fix: Ensure Inner is at the top of Z-Order so it docks LAST (filling remaining space)
            // ensuring it respects the space taken by previously docked controls (Unit/Label)
            Inner.BringToFront();
        }

        public void SetTextColor(Color c) => Inner.ForeColor = c;
        public void SetBg(Color c) { BackColor = c; }

        protected override void OnPaint(PaintEventArgs e) {
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using var path = UIUtils.RoundRect(new Rectangle(0, 0, Width-1, Height-1), UIUtils.S(8));
            using var fill = new SolidBrush(UIColors.FieldBg);
            e.Graphics.FillPath(fill, path);
            if (Inner.Focused)
            {
                using var pen = new Pen(UIColors.Primary);
                e.Graphics.DrawPath(pen, path);
            }
        }
    }

    // =======================================================================
    // 新增：专门的数字输入框 (继承自下划线输入框)
    // =======================================================================
    public class LiteNumberInput : LiteUnderlineInput
    {
        public LiteNumberInput(
            string text,
            string unit = "",
            string label = "",
            int width = 160,
            Color? color = null,
            int maxLength = 10)
            : base(text, unit, label, width, color, HorizontalAlignment.Center)
        {
            this.Inner.MaxLength = maxLength;

            this.Inner.KeyPress += (s, e) =>
            {
                if (char.IsControl(e.KeyChar) || char.IsDigit(e.KeyChar)) return;
                if (e.KeyChar == '.' && !this.Inner.Text.Contains(".")) return;
                if (e.KeyChar == '-' && this.Inner.SelectionStart == 0 && !this.Inner.Text.Contains("-")) return;
                e.Handled = true;
            };

            this.Inner.Leave += (s, e) =>
            {
                if (string.IsNullOrWhiteSpace(this.Inner.Text) || this.Inner.Text == "." || this.Inner.Text == "-")
                {
                    this.Inner.Text = "0";
                }
            };
        }

        public int ValueInt => int.TryParse(Inner.Text, out int v) ? v : 0;
        public double ValueDouble => double.TryParse(Inner.Text, out double v) ? v : 0.0;
    }

    public class LiteColorInput : Panel
    {
        public LiteUnderlineInput Input;
        public LiteColorPicker Picker;
        public string HexValue { get => Input.Inner.Text; set { Input.Inner.Text = value; Picker.SetHex(value); } }

        public LiteColorInput(string initialHex)
        {
            // ★★★ 修改：Size/Location 缩放
            this.Size = new Size(UIUtils.S(95), UIUtils.S(26));
            Picker = new LiteColorPicker(initialHex) { Size = new Size(UIUtils.S(26), UIUtils.S(22)), Location = new Point(this.Width - UIUtils.S(26), UIUtils.S(3)) };

            // 适配新构造函数
            Input = new LiteUnderlineInput(initialHex, "", "", 60) { Location = new Point(0, 0) };

            Picker.ColorChanged += (s, e) => Input.Inner.Text = $"#{Picker.Value.R:X2}{Picker.Value.G:X2}{Picker.Value.B:X2}";
            Input.Inner.TextChanged += (s, e) => Picker.SetHex(Input.Inner.Text);
            this.Controls.Add(Input);
            this.Controls.Add(Picker);
        }
    }

    public class LiteColorPicker : Control
    {
        private Color _color;
        public event EventHandler? ColorChanged;
        public Color Value { get => _color; set { _color = value; Invalidate(); } }
        // ★★★ 修改：Size 缩放
        public LiteColorPicker(string initialHex) { SetHex(initialHex); this.Size = new Size(UIUtils.S(24), UIUtils.S(24)); this.Cursor = Cursors.Hand; this.DoubleBuffered = true; this.Click += (s, e) => PickColor(); }
        public void SetHex(string hex) { try { _color = ColorTranslator.FromHtml(hex); Invalidate(); } catch {} }
        private void PickColor() { using (var cd = new ColorDialog()) { cd.Color = _color; cd.FullOpen = true; if (cd.ShowDialog() == DialogResult.OK) { _color = cd.Color; ColorChanged?.Invoke(this, EventArgs.Empty); Invalidate(); } } }
        protected override void OnPaint(PaintEventArgs e) { using (var b = new SolidBrush(_color)) e.Graphics.FillRectangle(b, 0, 0, Width - 1, Height - 1); using (var p = new Pen(UIColors.TextSub)) e.Graphics.DrawRectangle(p, 0, 0, Width - 1, Height - 1); }
    }

    // 其他原有组件
    public class LiteNote : Panel { public LiteNote(string text, int indent = 0) { this.Dock = DockStyle.Top; this.Height = UIUtils.S(32); this.Margin = new Padding(0); var lbl = new Label { Text = text, AutoSize = true, Font = new Font("Segoe UI", 8F), ForeColor = UIColors.TextSub, Location = new Point(UIUtils.S(indent), UIUtils.S(10)) }; this.Controls.Add(lbl); } }
    public class LiteComboItem
    {
        public string Text { get; set; } = "";
        public string Value { get; set; } = "";
        public override string ToString() => Text;
    }

    public class NoScrollComboBox : ComboBox
    {
        private bool _hovered;

        public NoScrollComboBox()
        {
            DrawMode = DrawMode.OwnerDrawFixed;
            DropDownStyle = ComboBoxStyle.DropDownList;
            UpdateItemHeight();
        }

        private int ScalePixel(int value) => Math.Max(1, (int)Math.Round(value * DeviceDpi / 96f));
        private void UpdateItemHeight() => ItemHeight = Font.Height + ScalePixel(4);

        protected override void OnFontChanged(EventArgs e) { base.OnFontChanged(e); UpdateItemHeight(); }
        protected override void OnDpiChangedAfterParent(EventArgs e) { base.OnDpiChangedAfterParent(e); UpdateItemHeight(); }
        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); _hovered = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hovered = false; Invalidate(); }
        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
        protected override void OnDropDown(EventArgs e) { base.OnDropDown(e); Invalidate(); }
        protected override void OnDropDownClosed(EventArgs e) { base.OnDropDownClosed(e); Invalidate(); }

        protected override void OnDrawItem(DrawItemEventArgs e)
        {
            bool selected = (e.State & DrawItemState.Selected) != 0;
            Color background = SystemInformation.HighContrast
                ? selected ? SystemColors.Highlight : SystemColors.Window
                : selected ? UIColors.NavSelected : UIColors.CardBg;
            Color foreground = SystemInformation.HighContrast
                ? selected ? SystemColors.HighlightText : SystemColors.WindowText
                : Enabled ? UIColors.TextMain : UIColors.TextSub;
            using var brush = new SolidBrush(background);
            e.Graphics.FillRectangle(brush, e.Bounds);
            string text = (e.Index >= 0 && e.Index < Items.Count ? GetItemText(Items[e.Index]) : Text) ?? "";
            var bounds = Rectangle.Inflate(e.Bounds, -ScalePixel(6), 0);
            TextRenderer.DrawText(e.Graphics, text, Font, bounds, foreground,
                UIRenderer.TextFlags | (RightToLeft == RightToLeft.Yes ? TextFormatFlags.RightToLeft | TextFormatFlags.Right : TextFormatFlags.Left));
            if ((e.State & DrawItemState.Focus) != 0 && ShowFocusCues)
                ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(e.Bounds, -2, -2), foreground, background);
        }

        protected override void WndProc(ref Message m)
        {
            // WM_MOUSEWHEEL = 0x020A
            if (m.Msg == 0x020A && !this.DroppedDown) return;
            base.WndProc(ref m);
            // Keep the native combo/list, keyboard handling and accessibility intact;
            // paint only its closed face after Windows has processed the message.
            if (SystemInformation.HighContrast || IsDisposed || !IsHandleCreated) return;
            if (m.Msg == 0x000F) // WM_PAINT
            {
                using var graphics = CreateGraphics();
                DrawFace(graphics);
            }
            else if (m.WParam != IntPtr.Zero &&
                (m.Msg == 0x0318 || (m.Msg == 0x0317 && (m.LParam.ToInt64() & 0x0004) != 0))) // WM_PRINTCLIENT or WM_PRINT with PRF_CLIENT
            {
                using var graphics = Graphics.FromHdc(m.WParam);
                DrawFace(graphics);
            }
        }

        private void DrawFace(Graphics graphics)
        {
            if (ClientSize.Width < 2 || ClientSize.Height < 2) return;
            using var outer = new SolidBrush(Parent?.BackColor ?? UIColors.CardBg);
            graphics.FillRectangle(outer, ClientRectangle);
            graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            Color background = Enabled && _hovered ? UIColors.NavHover : UIColors.FieldBg;
            using var brush = new SolidBrush(background);
            using var face = UIUtils.RoundRect(new Rectangle(0,0,Width-1,Height-1), ScalePixel(10));
            graphics.FillPath(brush, face);
            if (Enabled && (Focused || DroppedDown))
            {
                using var border = new Pen(UIColors.Primary);
                graphics.DrawPath(border, face);
            }
            int arrowWidth = ScalePixel(22);
            bool rtl = RightToLeft == RightToLeft.Yes;
            var textRect = new Rectangle(rtl ? arrowWidth : ScalePixel(7), 0,
                Math.Max(0, Width - arrowWidth - ScalePixel(7)), Height);
            Color foreground = Enabled ? UIColors.TextMain : UIColors.TextSub;
            TextRenderer.DrawText(graphics, Text, Font, textRect, foreground,
                UIRenderer.TextFlags | (rtl ? TextFormatFlags.RightToLeft | TextFormatFlags.Right : TextFormatFlags.Left));
            float centerX = rtl ? arrowWidth / 2f : Width - arrowWidth / 2f;
            float centerY = Height / 2f;
            float half = ScalePixel(3);
            graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using var caret = new Pen(foreground, Math.Max(1f, DeviceDpi / 96f));
            graphics.DrawLines(caret, new[] {
                new PointF(centerX - half, centerY - half / 2),
                new PointF(centerX, centerY + half / 2),
                new PointF(centerX + half, centerY - half / 2)
            });
            if (Focused && ShowFocusCues)
                ControlPaint.DrawFocusRectangle(graphics, Rectangle.Inflate(textRect, -2, -ScalePixel(4)), foreground, background);
        }
    }

    public class LiteComboBox : Panel
    {
        public ComboBox Inner;

        public LiteComboBox()
        {
            this.Size = new Size(UIUtils.S(160), UIUtils.S(28));
            this.BackColor = UIColors.CardBg;
            this.Padding = Padding.Empty;

            Inner = new NoScrollComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                FlatStyle = FlatStyle.Flat,
                ForeColor = UIColors.TextSub,
                Font = new Font("Segoe UI", 9F),
                // ★★★ 核心修复：移除 Dock.Fill，改为 None，以便我们可以手动居中它
                Dock = DockStyle.None,
                BackColor = UIColors.CardBg,
                Margin = new Padding(0)
            };
            this.Controls.Add(Inner);

            // ★★★ 新增：手动布局以实现垂直居中 ★★★
            this.Layout += (s, e) => {
                Inner.Width = this.Width;
                Inner.Location = new Point(0, (this.Height - Inner.Height) / 2);
            };
        }

        public object? SelectedItem { get => Inner.SelectedItem; set => Inner.SelectedItem = value; }
        public int SelectedIndex { get => Inner.SelectedIndex; set => Inner.SelectedIndex = value; }
        public ComboBox.ObjectCollection Items => Inner.Items;
        [System.Diagnostics.CodeAnalysis.AllowNull]
        public override string Text { get => Inner.Text; set => Inner.Text = value ?? ""; }

        // Helper methods for Key-Value pairs
        public void AddItem(string text, string value)
        {
             Inner.Items.Add(new LiteComboItem { Text = text, Value = value });
             Inner.DisplayMember = "Text";
             Inner.ValueMember = "Value";
        }

        public bool SelectValue(string value)
        {
             for(int i=0; i<Inner.Items.Count; i++)
             {
                 if (Inner.Items[i] is LiteComboItem item && item.Value == value)
                 {
                     Inner.SelectedIndex = i;
                     return true;
                 }
             }
             for(int i=0; i<Inner.Items.Count; i++)
             {
                 if (Inner.Items[i] is LiteComboItem item &&
                     string.Equals(item.Text, value, StringComparison.OrdinalIgnoreCase))
                 {
                     Inner.SelectedIndex = i;
                     return true;
                 }
             }
             if (Inner.Items.Count > 0) Inner.SelectedIndex = 0;
             return false;
        }

        public string SelectedValue
        {
            get => (Inner.SelectedItem as LiteComboItem)?.Value ?? "";
        }
    }
    public class LiteLink : Label
    {
        private Color _normalColor = UIColors.Primary;
        private Color _hoverColor = UIColors.TextMain;

        public LiteLink(string text, Action? onClick = null)
        {
            this.Text = text;
            this.AutoSize = true;
            this.Cursor = Cursors.Hand;
            this.ForeColor = _normalColor;
            this.Font = new Font("Segoe UI", 9F, FontStyle.Underline);

            if (onClick != null)
                this.Click += (s, e) => { if (this.Enabled) onClick(); };

            this.MouseEnter += (s, e) => { if (this.Enabled) this.ForeColor = _hoverColor; };
            this.MouseLeave += (s, e) => { if (this.Enabled) this.ForeColor = _normalColor; };
        }

        public void SetColor(Color normal, Color hover)
        {
            _normalColor = normal;
            _hoverColor = hover;
            if (Enabled) this.ForeColor = normal;
        }

        public new bool Enabled
        {
            get => base.Enabled;
            set
            {
                base.Enabled = value;
                this.Cursor = value ? Cursors.Hand : Cursors.Default;
                this.ForeColor = value ? _normalColor : UIColors.TextSub;
            }
        }
    }

    // =======================================================================
    // 3. 组合/高级组件 (New Standard Components)
    // =======================================================================


    public class LiteCheck : CheckBox
    {
        private bool _hovered;
        public LiteCheck(bool val, string text = "")
        {
            FlatStyle = FlatStyle.Flat;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Opaque, true);
            Checked = val;
            AutoSize = true;
            Cursor = Cursors.Hand;
            Text = text;
            BackColor = UIColors.CardBg;
            ForeColor = UIColors.TextSub;
            Font = new Font("Segoe UI", 9F);
            Size = GetPreferredSize(Size.Empty);
        }
        private int S(int value) => Math.Max(1, (int)Math.Round(value * DeviceDpi / 96f));
        public override Size GetPreferredSize(Size proposedSize)
        {
            int labelWidth = string.IsNullOrEmpty(Text) ? 0 : TextRenderer.MeasureText(Text, Font).Width + S(7);
            return new Size(S(38) + labelWidth, Math.Max(S(24), Font.Height));
        }
        protected override void OnPaintBackground(PaintEventArgs e)
        {
            using var background = new SolidBrush(Parent?.BackColor ?? BackColor);
            e.Graphics.FillRectangle(background, ClientRectangle);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            // Paint an opaque local surface. Never ask CheckBox/base to copy a composited parent.
            var g = e.Graphics;
            Color backgroundColor = Parent?.BackColor ?? BackColor;
            using (var background = new SolidBrush(backgroundColor)) g.FillRectangle(background, ClientRectangle);
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            int trackWidth = S(36), trackHeight = S(21);
            bool rtl = RightToLeft == RightToLeft.Yes;
            var track = new Rectangle(rtl ? Width-trackWidth-S(1) : S(1), (Height-trackHeight)/2, trackWidth, trackHeight);
            Color trackColor = Checked ? UIColors.Primary : UIColors.Blend(backgroundColor, UIColors.TextMain, _hovered ? 0.25f : 0.18f);
            if (!Enabled) trackColor = UIColors.Blend(backgroundColor, trackColor, 0.45f);
            using (var path = UIUtils.RoundRect(track, trackHeight/2))
            using (var fill = new SolidBrush(trackColor)) g.FillPath(fill, path);
            int knob = trackHeight-S(4);
            int knobX = CheckState == CheckState.Indeterminate ? track.Left+(trackWidth-knob)/2
                : (Checked != rtl ? track.Right-knob-S(2) : track.Left+S(2));
            using (var thumb = new SolidBrush(Enabled ? Color.White : UIColors.Blend(backgroundColor, Color.White, 0.6f)))
                g.FillEllipse(thumb, knobX, track.Top+S(2), knob, knob);
            if (!string.IsNullOrEmpty(Text))
            {
                Rectangle label = rtl ? new Rectangle(0, 0, Math.Max(0, track.Left-S(7)), Height)
                    : new Rectangle(track.Right+S(7), 0, Math.Max(0, Width-track.Right-S(7)), Height);
                using var brush = new SolidBrush(Enabled ? ForeColor : UIColors.TextSub);
                using var format = new StringFormat { LineAlignment = StringAlignment.Center,
                    Alignment = rtl ? StringAlignment.Far : StringAlignment.Near,
                    Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap,
                    HotkeyPrefix = ShowKeyboardCues ? System.Drawing.Text.HotkeyPrefix.Show : System.Drawing.Text.HotkeyPrefix.Hide };
                g.DrawString(Text, Font, brush, label, format);
            }
            if (Focused && ShowFocusCues)
            {
                using var focus = new Pen(UIColors.Primary) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dot };
                var bounds = Rectangle.Inflate(track, S(1), S(1));
                using var path = UIUtils.RoundRect(bounds, bounds.Height/2);
                g.DrawPath(focus, path);
            }
        }
        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); _hovered = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hovered = false; Invalidate(); }
        protected override void OnCheckStateChanged(EventArgs e) { base.OnCheckStateChanged(e); Invalidate(); }
        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
    }

    public class LiteButton : Button
    {
        private bool _dashed;
        private bool _primary;
        private bool _hovered;
        private bool _mousePressed;
        private bool _keyPressed;

        public LiteButton(string t, bool p = false, bool dashed = false)
        {
            Text = t;
            _dashed = dashed;
            _primary = p;
            Size = new Size(UIUtils.S(80), UIUtils.S(32));
            FlatStyle = FlatStyle.Flat;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Opaque, true);
            Cursor = Cursors.Hand;
            Font = new Font("Segoe UI", 9F, p ? FontStyle.Bold : FontStyle.Regular);
            FlatAppearance.BorderSize = 0;
            FlatAppearance.MouseOverBackColor = p ? UIColors.Blend(UIColors.Primary, UIColors.OnPrimary, 0.08f) : UIColors.NavHover;
            FlatAppearance.MouseDownBackColor = p ? UIColors.Blend(UIColors.Primary, UIColors.OnPrimary, 0.16f) : UIColors.NavSelected;

            if (p)
            {
                BackColor = UIColors.Primary;
                ForeColor = UIColors.OnPrimary;
                FlatAppearance.BorderSize = 0;
            }
            else if (dashed)
            {
                BackColor = Color.Transparent;
                ForeColor = UIColors.TextSub;
                FlatAppearance.BorderSize = 0;
            }
            else
            {
                BackColor = UIColors.CardBg;
                ForeColor = UIColors.TextMain;
                FlatAppearance.BorderColor = UIColors.Border;
            }
        }

        private Color ResolveBackground()
        {
            for (Control? ancestor = Parent; ancestor != null; ancestor = ancestor.Parent)
                if (ancestor.BackColor.A == 255) return ancestor.BackColor;
            return UIColors.MainBg;
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            using var background = new SolidBrush(ResolveBackground());
            e.Graphics.FillRectangle(background, ClientRectangle);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Color outside = ResolveBackground();
            using (var background = new SolidBrush(outside)) e.Graphics.FillRectangle(background, ClientRectangle);
            if (Width < 2 || Height < 2) return;
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            bool pressed = _keyPressed || (_mousePressed && _hovered);
            Color fill = BackColor.A == 255 ? BackColor : outside;
            if (Enabled && (pressed || _hovered))
                fill = pressed ? FlatAppearance.MouseDownBackColor : FlatAppearance.MouseOverBackColor;
            if (!Enabled) fill = UIColors.Blend(outside, fill, 0.45f);
            using var shape = UIUtils.RoundRect(new Rectangle(0, 0, Width - 1, Height - 1), UIUtils.S(11));
            using (var surface = new SolidBrush(fill)) e.Graphics.FillPath(surface, shape);
            if (!_primary)
            {
                using var pen = new Pen(UIColors.Border) { DashStyle = _dashed ? System.Drawing.Drawing2D.DashStyle.Dash : System.Drawing.Drawing2D.DashStyle.Solid };
                e.Graphics.DrawPath(pen, shape);
            }
            Rectangle textBounds = Rectangle.Inflate(ClientRectangle, -UIUtils.S(6), -UIUtils.S(2));
            if (pressed) textBounds.Offset(0, 1);
            using var textBrush = new SolidBrush(Enabled ? ForeColor : UIColors.Blend(fill, ForeColor, 0.5f));
            using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center,
                Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap,
                HotkeyPrefix = !UseMnemonic ? System.Drawing.Text.HotkeyPrefix.None :
                    ShowKeyboardCues ? System.Drawing.Text.HotkeyPrefix.Show : System.Drawing.Text.HotkeyPrefix.Hide };
            if (RightToLeft == RightToLeft.Yes) format.FormatFlags |= StringFormatFlags.DirectionRightToLeft;
            e.Graphics.DrawString(Text, Font, textBrush, textBounds, format);
            if (Focused && ShowFocusCues)
            {
                using var focusPath = UIUtils.RoundRect(Rectangle.Inflate(ClientRectangle, -UIUtils.S(3), -UIUtils.S(3)), UIUtils.S(8));
                using var focusPen = new Pen(_primary ? UIColors.OnPrimary : UIColors.Primary) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dot };
                e.Graphics.DrawPath(focusPen, focusPath);
            }
        }

        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); _hovered = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hovered = false; Invalidate(); }
        protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); if (e.Button == MouseButtons.Left) _mousePressed = true; Invalidate(); }
        protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); _mousePressed = false; Invalidate(); }
        protected override void OnMouseCaptureChanged(EventArgs e) { base.OnMouseCaptureChanged(e); if (!Capture) _mousePressed = false; Invalidate(); }
        protected override void OnKeyDown(KeyEventArgs e) { base.OnKeyDown(e); if (e.KeyCode == Keys.Space) _keyPressed = true; Invalidate(); }
        protected override void OnKeyUp(KeyEventArgs e) { base.OnKeyUp(e); if (e.KeyCode == Keys.Space) _keyPressed = false; Invalidate(); }
        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); _keyPressed = false; Invalidate(); }
        protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); _keyPressed = _mousePressed = false; Invalidate(); }
    }
    public class LiteNavBtn : Button
    {
        private bool _isActive;
        private readonly Font _regularFont = new Font("Segoe UI", 9F);
        private readonly Font _activeFont = new Font("Segoe UI", 9F, FontStyle.Bold);

        public bool IsActive
        {
            get => _isActive;
            set { if (_isActive == value) return; _isActive = value; Invalidate(); }
        }

        public LiteNavBtn(string text)
        {
            Text = text;
            Size = new Size(UIUtils.S(150), UIUtils.S(40));
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            TextAlign = ContentAlignment.MiddleLeft;
            Font = _regularFont;
            Cursor = Cursors.Hand;
            Margin = UIUtils.S(new Padding(5, 2, 5, 2));
            BackColor = UIColors.SidebarBg;
            ForeColor = UIColors.TextMain;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            bool hover = ClientRectangle.Contains(PointToClient(Cursor.Position));
            Color bg = _isActive ? UIColors.NavSelected : hover ? UIColors.NavHover : UIColors.SidebarBg;
            using var background = new SolidBrush(UIColors.SidebarBg);
            e.Graphics.FillRectangle(background, ClientRectangle);
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using var path = UIUtils.RoundRect(Rectangle.Inflate(ClientRectangle, -2, -2), UIUtils.S(10));
            using var brush = new SolidBrush(bg);
            e.Graphics.FillPath(brush, path);
            string icon = string.IsNullOrEmpty(Text) ? "" : System.Globalization.StringInfo.GetNextTextElement(Text);
            bool hasIcon = icon.Any(char.IsSurrogate) || icon.StartsWith("⚙") || icon.StartsWith("➖");
            string caption = hasIcon ? Text.Substring(icon.Length).TrimStart() : Text;
            if (hasIcon)
            {
                var iconRect = new Rectangle(UIUtils.S(10), (Height-UIUtils.S(24))/2, UIUtils.S(24), UIUtils.S(24));
                using var iconPath = UIUtils.RoundRect(iconRect, UIUtils.S(7));
                using var iconBrush = new SolidBrush(UIColors.Blend(UIColors.CardBg, UIColors.Primary, _isActive ? .24f : .12f));
                e.Graphics.FillPath(iconBrush, iconPath);
                TextRenderer.DrawText(e.Graphics, icon, _regularFont, iconRect, UIColors.TextMain,
                    UIRenderer.TextFlags | TextFormatFlags.HorizontalCenter | TextFormatFlags.PreserveGraphicsClipping | TextFormatFlags.PreserveGraphicsTranslateTransform);
            }
            int left = UIUtils.S(hasIcon ? 43 : 10);
            var textRect = new Rectangle(left, 0, Math.Max(0, Width-left-UIUtils.S(10)), Height);
            TextRenderer.DrawText(e.Graphics, caption, _isActive ? _activeFont : _regularFont,
                textRect, Enabled ? UIColors.TextMain : UIColors.TextSub, UIRenderer.TextFlags |
                (hasIcon ? TextFormatFlags.Left : TextFormatFlags.HorizontalCenter) |
                TextFormatFlags.PreserveGraphicsClipping | TextFormatFlags.PreserveGraphicsTranslateTransform);
            if (Focused && ShowFocusCues)
                ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(ClientRectangle, -3, -3), UIColors.Primary, bg);
        }

        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); Invalidate(); }
        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
        protected override void Dispose(bool disposing)
        {
            if (disposing) { _regularFont.Dispose(); _activeFont.Dispose(); }
            base.Dispose(disposing);
        }
    }
    public class LiteSortBtn : Button { public LiteSortBtn(string txt) { Text = txt; Size = new Size(UIUtils.S(24), UIUtils.S(24)); FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0; BackColor = UIColors.GroupHeader; ForeColor = UIColors.TextSub; Cursor = Cursors.Hand; Font = new Font("Segoe UI", 7F, FontStyle.Bold); Margin = new Padding(0); } }

    // ★★★ New: Header Button (Variable Width) ★★★
    public class LiteHeaderBtn : Button
    {
        public LiteHeaderBtn(string txt)
        {
            Text = txt;
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            BackColor = UIColors.GroupHeader;
            ForeColor = UIColors.TextSub;
            Cursor = Cursors.Hand;
            Font = new Font("Segoe UI", 8F, FontStyle.Bold);
            Margin = new Padding(0);

            // Auto Width
            int w = TextRenderer.MeasureText(txt, Font).Width + UIUtils.S(16);
            Size = new Size(w, UIUtils.S(24));
        }
        public void SetColor(Color c) { ForeColor = c; }
    }


    /// <summary>
    /// 终极防闪烁面板
    /// 1. 开启双缓冲合成
    /// 2. 拦截背景擦除 (消除白屏闪烁的关键)
    /// </summary>
    public class BufferedPanel : Panel
    {
        public BufferedPanel()
        {
            // 开启所有标准的双缓冲标志
            this.DoubleBuffered = true;
            this.SetStyle(ControlStyles.AllPaintingInWmPaint |
                          ControlStyles.UserPaint |
                          ControlStyles.OptimizedDoubleBuffer |
                          ControlStyles.ResizeRedraw |
                          ControlStyles.ContainerControl, true); // 确保它作为容器被优化
            this.UpdateStyles();
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= 0x02000000;
                return cp;
            }
        }

        // ★★★ 核心修复：拦截 WM_ERASEBKGND ★★★
        // Windows 默认会先用背景色清除窗口，这会导致一瞬间的“白屏”或“黑屏”。
        // 我们直接返回 1 (true)，告诉 Windows“我已经擦除过了，你别管”，从而消灭闪烁。
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x0014) // WM_ERASEBKGND
            {
                m.Result = (IntPtr)1;
                return;
            }
            base.WndProc(ref m);
        }
    }
}
