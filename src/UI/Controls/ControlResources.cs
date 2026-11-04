using System.ComponentModel;

namespace LiteMonitor.src.UI.Controls;

internal static class ControlResources
{
    // WinForms does not own Font instances assigned by the application. Capture only
    // explicit local fonts; an inherited font belongs to an ancestor, not this tree.
    internal static HashSet<Font> CaptureFonts(Control root)
    {
        var fonts = new HashSet<Font>(ReferenceEqualityComparer.Instance);
        void Visit(Control control) {
            if (TypeDescriptor.GetProperties(control)[nameof(Control.Font)]?.ShouldSerializeValue(control) == true && !control.Font.IsSystemFont && !UiFonts.IsShared(control.Font))
                fonts.Add(control.Font);
            foreach (Control child in control.Controls) Visit(child);
        }
        Visit(root);
        return fonts;
    }

    internal static void ReplaceFont(Control control, Font font)
    {
        var old = control.Font;
        bool owned = TypeDescriptor.GetProperties(control)[nameof(Control.Font)]?.ShouldSerializeValue(control) == true && !old.IsSystemFont && !UiFonts.IsShared(old);
        control.Font = font;
        if (owned && !ReferenceEquals(old, font)) old.Dispose();
    }
}
