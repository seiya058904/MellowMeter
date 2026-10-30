using System.Drawing.Drawing2D;

namespace LiteMonitor.src.UI.Controls;

// Reused vector navigation icons. The retired material surfaces are no longer compiled.
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
