using System.Drawing.Drawing2D;
using LiteMonitor.src.Core;

namespace LiteMonitor.src.UI.Controls
{
    // Colour roles derived from the active theme. The theme stays authoritative; high contrast
    // falls back to system colours. Results are cached because paint paths read them per frame.
    public static class UIColors
    {
        private sealed class Palette
        {
            public readonly ColorConfig Source;
            public readonly string Background, Group, Text, Low, Warn, Crit;
            public readonly bool HighContrast;
            public readonly Color SecondaryOnPage, WarningOnField, CriticalOnField, Main, Card, Label, Secondary, Tertiary, Separator, BorderLine, Tint, OnTint, Fill, FillHover, FillPressed, Button, ButtonHover, ButtonPressed, Elevated, SelectedNav, HoverNav, Warning, Critical;
            public readonly bool Dark;

            public Palette(ColorConfig c, bool highContrast)
            {
                Source = c; HighContrast = highContrast;
                Background = c.Background; Group = c.GroupBackground; Text = c.TextPrimary; Low = c.BarLow; Warn = c.ValueWarn; Crit = c.ValueCrit;
                Main = ThemeManager.ParseColor(c.Background);
                Card = ThemeManager.ParseColor(c.GroupBackground);
                Label = ThemeManager.ParseColor(c.TextPrimary);
                Warning = ThemeManager.ParseColor(c.ValueWarn);
                Critical = ThemeManager.ParseColor(c.ValueCrit);
                Dark = Main.GetBrightness() < 0.5f;
                if (highContrast)
                {
                    Main = Card = Fill = FillHover = Button = ButtonHover = Elevated = SystemColors.Window;
                    Label = Secondary = SecondaryOnPage = Tertiary = Separator = BorderLine = Warning = Critical = WarningOnField = CriticalOnField = SystemColors.WindowText;
                    Tint = SelectedNav = FillPressed = ButtonPressed = SystemColors.Highlight;
                    OnTint = SystemColors.HighlightText;
                    HoverNav = SystemColors.Window;
                    Dark = Main.GetBrightness() < 0.5f;
                    return;
                }
                // Fields are the hardest surface secondary text sits on; passing there passes on the card.
                Fill = Blend(Card, Label, Dark ? 0.07f : 0.045f);
                Secondary = EnsureContrast(Blend(Card, Label, 0.66f), Fill);
                SecondaryOnPage = EnsureContrast(Blend(Main, Label, 0.66f), Main);
                Tertiary = Blend(Card, Label, 0.42f);
                Separator = Blend(Card, Label, 0.10f);
                BorderLine = Blend(Card, Secondary, 0.22f);
                Tint = EnsureContrast(ThemeManager.ParseColor(c.BarLow), Card);
                OnTint = Contrast(Tint, Color.Black) >= Contrast(Tint, Color.White) ? Color.Black : Color.White;
                WarningOnField = EnsureContrast(Warning, Fill);
                CriticalOnField = EnsureContrast(Critical, Fill);
                FillHover = Blend(Card, Label, Dark ? 0.11f : 0.075f);
                FillPressed = Blend(Card, Label, Dark ? 0.16f : 0.11f);
                // Buttons sit on the page surface, not on a card.
                Button = Blend(Main, Label, Dark ? 0.10f : 0.07f);
                ButtonHover = Blend(Main, Label, Dark ? 0.15f : 0.11f);
                ButtonPressed = Blend(Main, Label, Dark ? 0.20f : 0.15f);
                Elevated = Dark ? Blend(Card, Label, 0.07f) : Blend(Card, Color.White, 0.6f);
                SelectedNav = Blend(Main, Tint, 0.16f);
                HoverNav = Blend(Main, Tint, 0.08f);
            }
        }

        // Diagnostics only: renders the high-contrast palette for tests and visual checks.
        internal static bool? HighContrastOverride { get; set; }
        internal static bool HighContrast => HighContrastOverride ?? SystemInformation.HighContrast;

        internal static void InvalidatePalette() => _palette = null;

        private static Palette? _palette;
        private static Palette P
        {
            get
            {
                var c = ThemeManager.Current.Color;
                bool highContrast = HighContrast;
                var p = _palette;
                if (p == null || !ReferenceEquals(p.Source, c) || p.HighContrast != highContrast || p.Background != c.Background ||
                    p.Group != c.GroupBackground || p.Text != c.TextPrimary || p.Low != c.BarLow || p.Warn != c.ValueWarn || p.Crit != c.ValueCrit)
                    _palette = p = new Palette(c, highContrast);
                return p;
            }
        }

        public static bool IsDark => P.Dark;
        public static Color MainBg => P.Main;
        public static Color SidebarBg => P.Main;
        public static Color CardBg => P.Card;
        public static Color Border => P.BorderLine;
        public static Color Primary => P.Tint;
        public static Color SwatchText(Color background) => Contrast(Color.Black, background) >= Contrast(Color.White, background) ? Color.Black : Color.White;
        public static Color OnPrimary => P.OnTint;
        public static Color TextMain => P.Label;
        public static Color TextSub => P.Secondary;
        public static Color TextSubOnPage => P.SecondaryOnPage;
        public static Color TextWarnOnField => P.WarningOnField;
        public static Color TextCritOnField => P.CriticalOnField;
        public static Color TextTertiary => P.Tertiary;
        public static Color Separator => P.Separator;
        public static Color FieldBg => P.Fill;
        public static Color FieldHover => P.FillHover;
        public static Color FieldPressed => P.FillPressed;
        public static Color ButtonFill => P.Button;
        public static Color ButtonHover => P.ButtonHover;
        public static Color ButtonPressed => P.ButtonPressed;
        public static Color Elevated => P.Elevated;
        public static Color GroupHeader => P.Card;
        public static Color NavSelected => P.SelectedNav;
        public static Color NavHover => P.HoverNav;
        public static Color TextWarn => P.Warning;
        public static Color TextCrit => P.Critical;

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

    // The settings type scale. Sizes are points at 100 %; each resolves to a pixel font for the
    // scale in use, so text is correct on any monitor regardless of the process system DPI.
    internal enum TextRole { LargeTitle, Title, Headline, Body, BodyStrong, Footnote, FootnoteStrong, Caption, CaptionStrong }

    // Shared immutable fonts with process lifetime: bounded by roles x scales and never disposed
    // by controls (ControlResources skips them), which removes per-control font churn.
    internal static class UiFonts
    {
        private static readonly Dictionary<(TextRole, int, bool), Font> Cache = new();
        private static readonly HashSet<Font> Shared = new(ReferenceEqualityComparer.Instance);
        private static string? _text, _textStrong, _displayStrong;

        internal static bool IsShared(Font font) => Shared.Contains(font);

        internal static Font Get(TextRole role) => Get(role, UIUtils.ScaleFactor);

        internal static Font Get(TextRole role, float scale)
        {
            // GDI font linking resolves the Display semibold face to a serif CJK fallback, so CJK
            // languages take the Text cut, whose link table reaches the proper UI font.
            bool cjk = LanguageManager.CurrentLang.StartsWith("zh", StringComparison.OrdinalIgnoreCase) ||
                LanguageManager.CurrentLang.StartsWith("ja", StringComparison.OrdinalIgnoreCase) ||
                LanguageManager.CurrentLang.StartsWith("ko", StringComparison.OrdinalIgnoreCase);
            var key = (role, (int)Math.Round(scale * 100), cjk);
            if (Cache.TryGetValue(key, out var font)) return font;
            ResolveFamilies();
            var (points, family) = role switch
            {
                TextRole.LargeTitle => (19.5f, cjk ? _textStrong! : _displayStrong!),
                TextRole.Title => (15f, cjk ? _textStrong! : _displayStrong!),
                TextRole.Headline => (10f, _textStrong!),
                TextRole.BodyStrong => (10f, _textStrong!),
                TextRole.FootnoteStrong => (8.5f, _textStrong!),
                TextRole.Footnote => (8.5f, _text!),
                TextRole.CaptionStrong => (8f, _textStrong!),
                TextRole.Caption => (8f, _text!),
                _ => (10f, _text!),
            };
            font = new Font(family, points * 96f / 72f * scale, FontStyle.Regular, GraphicsUnit.Pixel);
            Cache[key] = font;
            Shared.Add(font);
            return font;
        }

        private static void ResolveFamilies()
        {
            if (_text != null) return;
            static string First(params string[] names)
            {
                foreach (var name in names)
                {
                    try { using var family = new FontFamily(name); return name; }
                    catch (ArgumentException) { }
                }
                return names[^1];
            }
            _text = First("Segoe UI Variable Text", "Segoe UI");
            _textStrong = First("Segoe UI Variable Text Semibold", "Segoe UI Semibold", "Segoe UI");
            _displayStrong = First("Segoe UI Variable Display Semib", "Segoe UI Semibold", "Segoe UI");
        }
    }

    internal static class UiText
    {
        // Language strings prefix some names and tips with a decorative symbol; settings rows show
        // the plain text while the stored value stays untouched.
        internal static string StripLeadingSymbol(string text)
        {
            int i = 0;
            while (i < text.Length && (!char.IsLetterOrDigit(text, i) || (text[i] >= '\u2100' && text[i] <= '\u2BFF')))
                i += char.IsSurrogatePair(text, i) ? 2 : 1;
            return i < text.Length ? text[i..].Trim() : text;
        }
    }

    // Layout and shape tokens in logical pixels; scale with UIUtils.S at the point of use.
    internal static class DS
    {
        internal const int RowHeight = 44;           // minimum hit height of a list row
        internal const int RowInset = 16;            // leading/trailing inset inside a group
        internal const int RowGap = 12;              // label to trailing control
        internal const int PageGutter = 20;          // page edge to group
        internal const int PageTop = 10;
        internal const int PageBottom = 24;
        internal const int GroupGap = 18;
        internal const int SectionHeader = 26;
        internal const int FooterTop = 6;
        internal const int RadiusCard = 12;
        internal const int RadiusControl = 9;
        internal const int RadiusField = 8;
        internal const int RadiusSmall = 6;
        internal const int ControlHeight = 30;
        internal const int SwitchWidth = 40;
        internal const int SwitchHeight = 24;
        internal const int SwitchInset = 2;
        internal const int ButtonHeight = 34;
        internal const int ButtonMinWidth = 86;
        internal const int SliderWidth = 176;
        internal const int SliderThumb = 20;
        internal const int SidebarWidth = 196;
        internal const int WindowWidth = 880;
        internal const int WindowHeight = 620;
        internal const int WindowMinWidth = 720;
        internal const int WindowMinHeight = 460;
        internal const int BottomBar = 62;
    }

    // Allocation-free painting: one reusable brush, pen and path (UI thread only, valid until the
    // next call). TextRenderer is the text policy; GDI+ text only where a transform requires it.
    internal static class Ink
    {
        private static readonly SolidBrush Brush = new(Color.Black);
        private static readonly Pen Stroke = new(Color.Black) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        private static readonly GraphicsPath Scratch = new();
        internal const TextFormatFlags TextFlags = TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding |
            TextFormatFlags.PreserveGraphicsClipping | TextFormatFlags.PreserveGraphicsTranslateTransform;
        internal const TextFormatFlags LineFlags = TextFlags | TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis;

        internal static SolidBrush B(Color color) { Brush.Color = color; return Brush; }

        // The nearest opaque ancestor colour, for controls drawing an opaque local surface.
        internal static Color Back(Control control)
        {
            for (Control? c = control.Parent; c != null; c = c.Parent)
                if (c.BackColor.A == 255) return c.BackColor;
            return UIColors.MainBg;
        }

        internal static Pen P(Color color, float width = 1f)
        {
            Stroke.Color = color;
            if (Stroke.Width != width) Stroke.Width = width;
            return Stroke;
        }

        // A continuous-corner rounded rectangle: one flatter Bezier per corner reaches further along
        // each edge than a circular arc, which reads as the softer iOS squircle at every radius.
        internal static GraphicsPath Round(RectangleF r, float radius)
        {
            Scratch.Reset();
            if (r.Width <= 0 || r.Height <= 0) return Scratch;
            float e = Math.Min(radius * 1.28f, Math.Min(r.Width, r.Height) / 2f);
            if (e <= 0.01f) { Scratch.AddRectangle(r); return Scratch; }
            float c = e * 0.30f; // control-point distance from the geometric corner
            float l = r.Left, t = r.Top, x = r.Right, b = r.Bottom;
            Scratch.AddBezier(l, t + e, l, t + c, l + c, t, l + e, t);
            Scratch.AddLine(l + e, t, x - e, t);
            Scratch.AddBezier(x - e, t, x - c, t, x, t + c, x, t + e);
            Scratch.AddLine(x, t + e, x, b - e);
            Scratch.AddBezier(x, b - e, x, b - c, x - c, b, x - e, b);
            Scratch.AddLine(x - e, b, l + e, b);
            Scratch.AddBezier(l + e, b, l + c, b, l, b - c, l, b - e);
            Scratch.CloseFigure();
            return Scratch;
        }

        internal static void Fill(Graphics g, RectangleF r, float radius, Color color)
        {
            var mode = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.FillPath(B(color), Round(r, radius));
            g.SmoothingMode = mode;
        }

        internal static void Outline(Graphics g, RectangleF r, float radius, Color color, float width = 1f)
        {
            var mode = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float inset = width / 2f;
            g.DrawPath(P(color, width), Round(RectangleF.Inflate(r, -inset, -inset), Math.Max(0, radius - inset)));
            g.SmoothingMode = mode;
        }

        // Soft keyboard ring, shown only with keyboard cues; high contrast gets a solid outline.
        internal static void FocusRing(Graphics g, RectangleF r, float radius)
        {
            Color ring = UIColors.HighContrast ? SystemColors.WindowText : Color.FromArgb(150, UIColors.Primary);
            Outline(g, r, radius, ring, Math.Max(1.5f, UIUtils.ScaleFactor * 1.5f));
        }

        internal static void Line(Graphics g, string text, Font font, Rectangle bounds, Color color, TextFormatFlags alignment = TextFormatFlags.Left)
        {
            if (bounds.Width <= 0 || bounds.Height <= 0 || string.IsNullOrEmpty(text)) return;
            TextRenderer.DrawText(g, text, font, bounds, color, LineFlags | alignment);
        }

        internal static void Chevron(Graphics g, PointF center, float half, float angleDegrees, Color color, float width)
        {
            var state = g.Save();
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TranslateTransform(center.X, center.Y);
            g.RotateTransform(angleDegrees);
            // Points right at 0 degrees.
            g.DrawLines(P(color, width), new[] { new PointF(-half / 2, -half), new PointF(half / 2, 0), new PointF(-half / 2, half) });
            g.Restore(state);
        }

        internal static float LineWidth(float logical = 1.5f) => Math.Max(1.25f, logical * UIUtils.ScaleFactor);
    }

    internal interface IHeightForWidth { int HeightForWidth(int width); }

    // A reorderable element whose visible card is shorter than its layout slot (trailing gap).
    internal interface IReorderVisual { int VisualHeight { get; } }

    // A light vertical stack replacing TableLayoutPanel and dock chains. Children are arranged in
    // reverse child order (the same convention as Dock=Top, so existing z-order based ordering keeps
    // working), layout runs only while actually visible, and the panel sizes itself to its content.
    public class StackPanel : Panel, IHeightForWidth
    {
        private bool _layoutPending;
        private bool _arranging;
        internal bool FitContentHeight { get; set; } = true;
        internal event Action? ContentHeightChanged;

        public StackPanel()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            if (_arranging) return;
            if (!Visible) { _layoutPending = true; return; }
            _arranging = true;
            try
            {
                _layoutPending = false;
                int width = Math.Max(0, ClientSize.Width - Padding.Horizontal), y = Padding.Top, order = 0;
                for (int i = Controls.Count - 1; i >= 0; i--)
                {
                    Control child = Controls[i];
                    if (child.TabIndex != order) child.TabIndex = order;
                    order++;
                    if (!child.Visible) continue;
                    int height = MeasureChild(child, width);
                    if (child.Left != Padding.Left || child.Top != y || child.Width != width || child.Height != height)
                        child.SetBounds(Padding.Left, y, width, height);
                    y += child.Height;
                }
                y += Padding.Bottom;
                if (FitContentHeight && Height != y) { Height = y; ContentHeightChanged?.Invoke(); }
            }
            finally { _arranging = false; }
        }

        internal static int MeasureChild(Control child, int width) =>
            child is IHeightForWidth flexible ? flexible.HeightForWidth(width)
            : child.AutoSize ? child.GetPreferredSize(new Size(width, 0)).Height
            : child.Height;

        public int HeightForWidth(int width) => MeasureContent(width);

        internal int MeasureContent(int width)
        {
            int inner = Math.Max(0, width - Padding.Horizontal), total = Padding.Vertical;
            // Only used for bodies that are hidden as a whole, where every row counts.
            foreach (Control child in Controls) total += MeasureChild(child, inner);
            return total;
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            if (Visible && _layoutPending) PerformLayout();
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            Color fill = BackColor.A == 255 ? BackColor : UIColors.MainBg;
            e.Graphics.FillRectangle(Ink.B(fill), e.ClipRectangle);
        }
    }
}
