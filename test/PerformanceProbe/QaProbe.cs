using System.Drawing.Imaging;
using System.Reflection;
using System.Runtime.InteropServices;
using LiteMonitor;
using LiteMonitor.src.Core;
using LiteMonitor.src.Core.Actions;
using LiteMonitor.src.UI;
using LiteMonitor.src.UI.Controls;
using LiteMonitor.src.UI.SettingsPage;

// Visual QA matrix for the settings window: themes, languages, pages and interaction states, taken
// from the real window on the isolated probe profile. `--qa-scale 1.0|1.5|2.0` renders at a synthetic
// scale on the host monitor; that checks layout and glyph sizes, not a physical DPI transition.
internal static partial class Program
{
    [DllImport("user32.dll")] private static extern bool PrintWindow(IntPtr hwnd, IntPtr dc, uint flags);

    private static int QaProbe(string root, string[] args)
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        InterfaceMotion.SystemPreferenceOverride = true;
        int scaleArg = Array.IndexOf(args, "--qa-scale");
        if (scaleArg >= 0) SettingsForm.ScaleOverride = float.Parse(args[scaleArg + 1], System.Globalization.CultureInfo.InvariantCulture);
        string only = Array.IndexOf(args, "--qa-only") is int o and >= 0 ? args[o + 1] : "";
        var cfg = Settings.Load();
        cfg.AdvancedSensorsEnabled = false; cfg.AutoStart = false; cfg.ShowTaskbar = false;
        cfg.HideTrayIcon = true; cfg.TopMost = false; cfg.HistoryEnabled = false;
        cfg.Language = "en"; cfg.Skin = "MellowLight"; cfg.InterfaceAnimationsEnabled = true; cfg.Save();
        using var main = new MainForm();
        int exit = 1;
        var pagesKeys = new[] { "MainPanel", "Taskbar", "Monitor", "Threshold", "System" };

        void SaveWindow(Form form, string name)
        {
            using var bitmap = new Bitmap(form.Width, form.Height, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bitmap))
            {
                IntPtr dc = g.GetHdc();
                PrintWindow(form.Handle, dc, 2);
                g.ReleaseHdc(dc);
            }
            bitmap.Save(Path.Combine(root, name + ".png"), ImageFormat.Png);
        }

        // For floating sprites, which only exist on screen.
        void SaveScreen(Rectangle area, string name)
        {
            using var bitmap = new Bitmap(area.Width, area.Height, PixelFormat.Format32bppRgb);
            using (var g = Graphics.FromImage(bitmap)) g.CopyFromScreen(area.Location, Point.Empty, area.Size);
            bitmap.Save(Path.Combine(root, name + ".png"), ImageFormat.Png);
        }

        main.Shown += async (_, _) =>
        {
            try
            {
                var ui = Get<UIController>(main, "_ui");
                await Task.Delay(800);

                async Task Settle() { for (int i = 0; i < 400 && InterfaceMotion.ActiveCount > 0; i++) await Task.Delay(10); await Task.Delay(60); }

                void Theme(string skin, string language)
                {
                    cfg.Skin = skin; cfg.Language = language;
                    LanguageManager.Load(language);
                    AppActions.ApplyThemeAndLayout(cfg, ui, main);
                }

                async Task<SettingsForm> Open()
                {
                    var settings = new SettingsForm(cfg, ui, main) { TopMost = true };
                    settings.Show(main);
                    await Task.Delay(250);
                    return settings;
                }

                static IEnumerable<LiteSettingsGroup> Groups(SettingsPageBase page) => Children(page).OfType<LiteSettingsGroup>().Where(g => g.Collapsible);

                async Task AllPages(string tag, bool expanded)
                {
                    using var settings = await Open();
                    foreach (var key in pagesKeys)
                    {
                        settings.SwitchPage(key);
                        await Task.Delay(200);
                        await Settle();
                        SaveWindow(settings, $"{tag}_{key}");
                        var page = Get<Dictionary<string, SettingsPageBase>>(settings, "_pages")[key];
                        if (expanded && Groups(page).Any())
                        {
                            foreach (var group in Groups(page)) group.Collapsed = false;
                            await Settle();
                            SaveWindow(settings, $"{tag}_{key}_expanded");
                            page.Surface?.ScrollTo(page.Surface.MaxScroll);
                            await Settle();
                            SaveWindow(settings, $"{tag}_{key}_expanded_end");
                        }
                        if (key == "Monitor" && expanded)
                        {
                            Get<LiteSelector>(page, "_tabs").PerformSelect(1);
                            await Settle();
                            SaveWindow(settings, $"{tag}_Monitor_taskbar");
                            Get<LiteCheck>(page, "_chkEditLabels").Checked = true;
                            await Settle();
                            SaveWindow(settings, $"{tag}_Monitor_taskbar_labels");
                            Get<LiteSelector>(page, "_tabs").PerformSelect(0);
                            await Settle();
                            SaveWindow(settings, $"{tag}_Monitor_panel_labels");
                            Get<LiteCheck>(page, "_chkEditLabels").Checked = false;
                        }
                    }
                    settings.Close();
                }

                async Task States(string tag)
                {
                    using var settings = await Open();
                    var pages = Get<Dictionary<string, SettingsPageBase>>(settings, "_pages");
                    Rectangle Frame() => Rectangle.Inflate(settings.Bounds, 40, 40);

                    // Picker menu: open, then keyboard highlight.
                    var picker = Children(pages["MainPanel"]).OfType<LiteComboBox>().First(c => c.Visible);
                    picker.OpenMenu();
                    await Task.Delay(30); SaveScreen(Frame(), $"{tag}_menu_opening");
                    await Settle(); SaveScreen(Frame(), $"{tag}_menu_open");
                    LitePopupMenu.Current?.ProcessKey(Keys.Down); LitePopupMenu.Current?.ProcessKey(Keys.Down);
                    await Settle(); SaveScreen(Frame(), $"{tag}_menu_keyboard");
                    LitePopupMenu.Current?.ProcessKey(Keys.Escape);
                    await Settle();

                    // Slider mid-drag with its value bubble.
                    var slider = Children(pages["MainPanel"]).OfType<LiteSlider>().First(c => c.Visible);
                    slider.BeginProbeDrag(0.62f);
                    await Task.Delay(40); SaveScreen(Frame(), $"{tag}_slider_drag");
                    slider.EndProbeDrag();
                    await Settle();

                    // Switch mid-drag and mid-spring.
                    var toggle = Children(pages["MainPanel"]).OfType<LiteCheck>().First(c => c.Visible);
                    toggle.Checked = !toggle.Checked;
                    await Task.Delay(55); SaveWindow(settings, $"{tag}_switch_midspring");
                    await Settle();
                    toggle.Checked = !toggle.Checked;
                    await Settle();

                    // Disclosure mid-transition.
                    var mainGroup = Groups(pages["MainPanel"]).First();
                    mainGroup.Collapsed = false;
                    await Task.Delay(70); SaveWindow(settings, $"{tag}_disclosure_mid");
                    await Settle();
                    mainGroup.Collapsed = true;
                    await Settle();

                    // Page transition: sidebar pill mid-flight, reveal veil on screen.
                    settings.SwitchPage("Monitor");
                    await Task.Delay(45); SaveScreen(Frame(), $"{tag}_page_transition");
                    await Settle();

                    // Scrolled: compact title on a titled page, overscroll at the bottom.
                    settings.SwitchPage("Taskbar");
                    await Settle();
                    foreach (var g in Groups(pages["Taskbar"])) g.Collapsed = false;
                    await Settle();
                    pages["Taskbar"].Surface!.ScrollTo(UIUtils.S(120f));
                    await Settle(); SaveWindow(settings, $"{tag}_compact_title");
                    pages["Taskbar"].Surface!.Fling(UIUtils.S(4000f));
                    await Task.Delay(180); SaveWindow(settings, $"{tag}_overscroll");
                    await Settle();

                    // Drag-to-reorder mid-gesture.
                    settings.SwitchPage("Monitor");
                    await Settle();
                    var monitor = pages["Monitor"];
                    var viewport = monitor.Surface!.RectangleToScreen(monitor.Surface.ClientRectangle);
                    var row = Children(monitor).OfType<MonitorItemRow>().Where(r => r.Visible && viewport.Contains(r.RectangleToScreen(r.ClientRectangle))).OrderBy(r => r.PointToScreen(Point.Empty).Y).Skip(1).First();
                    var rowHost = (StackPanel)row.Parent!;
                    int y = row.PointToScreen(new Point(0, row.Height / 2)).Y;
                    var session = new ReorderSession(rowHost, row, monitor.Surface!, y, (_, _) => { }, () => { }, followPointer: false);
                    await Settle();
                    session.MoveTo(y + row.Height + row.Height / 2);
                    await Task.Delay(50); SaveScreen(Frame(), $"{tag}_reorder_mid");
                    await Settle(); SaveScreen(Frame(), $"{tag}_reorder_settled");
                    session.Cancel();
                    await Settle();

                    // Keyboard focus rings across the common control types.
                    settings.SwitchPage("MainPanel");
                    await Settle();
                    SendKeys.SendWait("{TAB}");
                    await Task.Delay(120); SaveWindow(settings, $"{tag}_focus_1");
                    SendKeys.SendWait("{TAB}{TAB}");
                    await Task.Delay(120); SaveWindow(settings, $"{tag}_focus_3");

                    // Disabled controls: custom taskbar layout is off by default.
                    settings.SwitchPage("Taskbar");
                    await Settle();
                    pages["Taskbar"].Surface!.ScrollTo(UIUtils.S(420f));
                    await Settle(); SaveWindow(settings, $"{tag}_disabled");

                    // A rejected value: the field's critical tint.
                    settings.SwitchPage("Threshold");
                    await Settle();
                    var number = Children(pages["Threshold"]).OfType<LiteNumberInput>().First();
                    number.SignalInvalid();
                    await Task.Delay(60); SaveWindow(settings, $"{tag}_invalid");
                    await Settle();
                    settings.Close();
                }

                if (only == "" || only == "scale")
                {
                    Theme("MellowLight", "en");
                    await AllPages("light_en", expanded: true);
                    await States("light");
                    Theme("DarkFlat_Classic", "en");
                    await AllPages("dark_en", expanded: true);
                    await States("dark");
                }
                if (only == "" || only == "contrast")
                {
                    UIColors.HighContrastOverride = true;
                    Theme("MellowLight", "en");
                    await AllPages("contrast", expanded: true);
                    UIColors.HighContrastOverride = null;
                }
                if (only == "")
                {
                    Theme("MellowDark", "zh");
                    await AllPages("mellowdark_zh", expanded: true);
                    Theme("MellowLight", "zh");
                    await AllPages("light_zh", expanded: false);
                    foreach (var language in new[] { "de", "ru", "fr", "ja" })
                    {
                        Theme("MellowLight", language);
                        await AllPages("light_" + language, expanded: true);
                    }
                    foreach (var skin in new[] { "CyberBlue_Neon", "AuroraDream_Purple", "SolarAmber_Warm", "MatrixGreen_Terminal", "LightPure_Elegant" })
                    {
                        Theme(skin, "en");
                        using var settings = await Open();
                        var pages = Get<Dictionary<string, SettingsPageBase>>(settings, "_pages");
                        SaveWindow(settings, $"{skin}_main");
                        settings.SwitchPage("Monitor");
                        await Settle();
                        SaveWindow(settings, $"{skin}_monitor");
                        settings.Close();
                    }
                }
                exit = 0;
            }
            catch (Exception ex) { File.WriteAllText(Path.Combine(root, "error.txt"), ex.ToString()); Console.Error.WriteLine(ex); }
            finally { main.Close(); }
        };
        Application.Run(main);
        return exit;
    }
}
