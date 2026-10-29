using System.Runtime.InteropServices;
using LiteMonitor.src.UI.Controls;

namespace LiteMonitor.src.UI.Helpers;

internal static class WindowTheme
{
    // Keep native dragging, caption buttons and keyboard behavior; let DWM
    // carry the same palette through the Windows 11 frame.
    internal static void Apply(Form window)
    {
        if (!window.IsHandleCreated || window.IsDisposed || SystemInformation.HighContrast ||
            !OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000)) return;

        Set(window.Handle, 20, UIColors.IsDark ? 1 : 0); // DWMWA_USE_IMMERSIVE_DARK_MODE
        Set(window.Handle, 33, 2); // DWMWCP_ROUND
        Set(window.Handle, 35, ColorTranslator.ToWin32(UIColors.MainBg)); // DWMWA_CAPTION_COLOR
        Set(window.Handle, 36, ColorTranslator.ToWin32(UIColors.TextMain)); // DWMWA_TEXT_COLOR
    }

    private static void Set(IntPtr handle, int attribute, int value) =>
        DwmSetWindowAttribute(handle, attribute, ref value, sizeof(int));

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr handle, int attribute, ref int value, int size);
}
