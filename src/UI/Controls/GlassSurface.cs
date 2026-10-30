using System.Drawing.Drawing2D;
using LiteMonitor.src.Core;
using LiteMonitor.src.UI.Helpers;

namespace LiteMonitor.src.UI.Controls;

/// <summary>Small, cached material painting; never captures or blurs the desktop.</summary>
internal static class GlassSurface
{
    private static readonly Dictionary<(Size, int, int, int), Bitmap> Cache = new();
    private static long _bytes;
    public static bool Enabled => ThemeManager.Current.Material == "glass" && WindowTheme.EffectsAllowed;

    public static void ClearCache()
    {
        foreach (var bitmap in Cache.Values) bitmap.Dispose();
        Cache.Clear();
        _bytes = 0;
    }

    public static void Paint(Graphics graphics, Rectangle bounds, Color color, int radius, bool? glass = null)
    {
        if (bounds.Width < 2 || bounds.Height < 2) return;
        if (!(glass ?? Enabled)) { UIUtils.FillRoundRect(graphics, bounds, radius, color); return; }
        var key = (bounds.Size, color.ToArgb(), radius, color.GetBrightness() < .5f ? 1 : 0);
        if (!Cache.TryGetValue(key, out var bitmap))
        {
            if (Cache.Count >= 24 || _bytes + (long)bounds.Width * bounds.Height * 4 > 8 * 1024 * 1024) ClearCache();
            bitmap = new Bitmap(bounds.Width, bounds.Height);
            using var g = Graphics.FromImage(bitmap);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var rect = new Rectangle(0, 0, bitmap.Width - 1, bitmap.Height - 1);
            using var path = UIUtils.RoundRect(rect, Math.Max(1, radius));
            Color top = UIColors.Blend(color, Color.White, color.GetBrightness() < .5f ? .035f : .48f);
            using var fill = new LinearGradientBrush(rect, top, color, LinearGradientMode.Vertical);
            g.FillPath(fill, path);
            using var edge = new Pen(UIColors.Blend(color, Color.White, color.GetBrightness() < .5f ? .16f : .75f));
            g.DrawPath(edge, path);
            Cache[key] = bitmap;
            _bytes += (long)bitmap.Width * bitmap.Height * 4;
        }
        graphics.DrawImageUnscaled(bitmap, bounds.Location);
    }
}

internal sealed class MaterialSidebar : Panel
{
    public MaterialSidebar() { DoubleBuffered = true; }
    protected override void OnPaintBackground(PaintEventArgs e)
    {
        if (FindForm() is Form form && WindowTheme.HasBackdrop(form))
            e.Graphics.Clear(Color.FromArgb(0, 0, 0, 0)); // Preserve zero alpha for DWM composition.
        else GlassSurface.Paint(e.Graphics, ClientRectangle, UIColors.SidebarBg, 1);
    }
}

internal sealed class MaterialSidebarTitle : Control
{
    public MaterialSidebarTitle() { DoubleBuffered = true; }
    protected override void OnPaintBackground(PaintEventArgs e)
    {
        e.Graphics.Clear(FindForm() is Form form && WindowTheme.HasBackdrop(form) ? Color.FromArgb(0, 0, 0, 0) : UIColors.SidebarBg);
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
        using var brush = new SolidBrush(UIColors.TextMain);
        e.Graphics.DrawString(Text, Font, brush, new PointF(Padding.Left, Padding.Top));
    }
}

internal static class NavigationIcon
{
    public static void Draw(Graphics g, string key, Rectangle bounds, Color color)
    {
        var state = g.Save();
        g.TranslateTransform(bounds.Left, bounds.Top);
        g.ScaleTransform(bounds.Width / 24f, bounds.Height / 24f);
        using var pen = new Pen(color, 1.6f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        switch (key)
        {
            case "MainPanel": g.DrawRectangle(pen, 3, 4, 18, 13); g.DrawLine(pen, 8, 21, 16, 21); g.DrawLine(pen, 12, 17, 12, 21); break;
            case "Taskbar": g.DrawRectangle(pen, 3, 5, 18, 15); g.DrawLine(pen, 3, 16, 21, 16); g.DrawLine(pen, 7, 18, 10, 18); break;
            case "Monitor": g.DrawLine(pen, 4, 20, 4, 12); g.DrawLine(pen, 10, 20, 10, 5); g.DrawLine(pen, 16, 20, 16, 9); g.DrawLine(pen, 22, 20, 22, 2); break;
            case "Threshold": g.DrawArc(pen, 6, 3, 12, 13, 180, 180); g.DrawLines(pen, new[] {new Point(6,9),new Point(5,16),new Point(3,18),new Point(21,18),new Point(19,16),new Point(18,9)}); g.DrawArc(pen, 10, 19, 4, 3, 0, 180); break;
            default:
                for (int i=0;i<3;i++) { int x=5+i*7, y=7+i*4; g.DrawLine(pen,x,3,x,21); g.DrawEllipse(pen,x-2,y-2,4,4); }
                break;
        }
        g.Restore(state);
    }
}
