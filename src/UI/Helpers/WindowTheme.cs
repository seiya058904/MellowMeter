using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using Microsoft.Win32;
using LiteMonitor.src.Core;
using LiteMonitor.src.UI.Controls;

namespace LiteMonitor.src.UI.Helpers;

internal static class WindowTheme
{
    private sealed class State { public bool Backdrop; public bool Hooked; }
    private static readonly ConditionalWeakTable<Form, State> States = new();
    private static bool _requested = true;
    private static long _lastPolicyCheck;
    internal static bool EffectsAllowed { get; private set; } = true;
    internal static bool HasBackdrop(Form window) => States.TryGetValue(window, out var state) && state.Backdrop;

    internal static void Configure(Settings settings)
    {
        _requested = settings.GlassEffectsEnabled;
        RefreshPolicy(true);
        foreach (var window in Application.OpenForms.Cast<Form>().ToArray()) Apply(window);
    }

    internal static void RefreshPolicy(bool force = false)
    {
        if (!force && Environment.TickCount64 - _lastPolicyCheck < 5000) return;
        _lastPolicyCheck = Environment.TickCount64;
        bool transparency = true;
        try {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            transparency = key?.GetValue("EnableTransparency") is not int value || value != 0;
        } catch { }
        bool saver = GetSystemPowerStatus(out var power) && power.SystemStatusFlag != 0;
        bool allowed = _requested && transparency && !SystemInformation.HighContrast && !saver;
        if (EffectsAllowed == allowed) return;
        EffectsAllowed = allowed;
        GlassSurface.ClearCache();
        foreach (var window in Application.OpenForms.Cast<Form>().ToArray()) {
            Apply(window);
            window.Invalidate(true);
        }
    }

    internal static void Apply(Form window)
    {
        if (!window.IsHandleCreated || window.IsDisposed) return;
        var state = States.GetOrCreateValue(window);
        if (!state.Hooked) {
            state.Hooked = true;
            UserPreferenceChangedEventHandler handler = (_, _) => {
                if (!window.IsDisposed && window.IsHandleCreated)
                    try { window.BeginInvoke(new Action(() => { RefreshPolicy(true); InterfaceMotion.Configure(Settings.Load()); Apply(window); window.Invalidate(true); })); } catch (InvalidOperationException) { }
            };
            SystemEvents.UserPreferenceChanged += handler;
            window.Disposed += (_, _) => SystemEvents.UserPreferenceChanged -= handler;
        }
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000)) return;
        Set(window.Handle, 20, UIColors.IsDark ? 1 : 0);
        Set(window.Handle, 33, 2);
        Set(window.Handle, 35, ColorTranslator.ToWin32(UIColors.MainBg));
        Set(window.Handle, 36, ColorTranslator.ToWin32(UIColors.TextMain));
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22621)) return;
        // Settings use the v0.1 solid native surface again. Explicitly clear any backdrop.
        Set(window.Handle, 38, 1); // DWMSBT_NONE
        var margins = new Margins();
        DwmExtendFrameIntoClientArea(window.Handle, ref margins);
        state.Backdrop = false;
    }

    internal static void StyleAuxiliary(Form window)
    {
        void Style(Control control) {
            if (control is LiteMonitor.ThemeEditor.ThemePreviewControl || Equals(control.Tag, "ThemeSwatch")) return;
            control.ForeColor = UIColors.TextMain;
            control.BackColor = control is TextBoxBase or NumericUpDown or ListBox or ComboBox ? UIColors.FieldBg : UIColors.MainBg;
            if (control is Button button) {
                button.FlatStyle = FlatStyle.Flat;
                button.FlatAppearance.BorderColor = UIColors.Border;
                button.FlatAppearance.MouseOverBackColor = UIColors.NavHover;
            }
            if (control is LinkLabel link) link.LinkColor = link.ActiveLinkColor = link.VisitedLinkColor = UIColors.Primary;
            if (control is LiteTreeView tree) tree.RefreshPalette();
            foreach (Control child in control.Controls) Style(child);
        }
        Style(window);
        Apply(window);
        window.Invalidate(true);
    }

    private static int Set(IntPtr handle, int attribute, int value) => DwmSetWindowAttribute(handle, attribute, ref value, sizeof(int));
    [StructLayout(LayoutKind.Sequential)] private struct Margins { public int Left, Right, Top, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct PowerStatus { public byte ACLineStatus, BatteryFlag, BatteryLifePercent, SystemStatusFlag; public int BatteryLifeTime, BatteryFullLifeTime; }
    [DllImport("kernel32.dll")] private static extern bool GetSystemPowerStatus(out PowerStatus status);
    [DllImport("dwmapi.dll")] private static extern int DwmExtendFrameIntoClientArea(IntPtr handle, ref Margins margins);
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr handle, int attribute, ref int value, int size);
}
