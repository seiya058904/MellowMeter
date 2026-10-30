using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Reflection;
using System.Text.Json;
using LiteMonitor;
using LiteMonitor.src.Core;
using LiteMonitor.src.Core.Actions;
using LiteMonitor.src.UI;

internal static partial class Program
{
    private static int Preview(string root, bool baseline = false)
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        var cfg = Settings.Load();
        cfg.Skin = baseline ? "DarkFlat_Classic" : "GlassLight"; cfg.Language = "en"; cfg.TopMost = true;
        cfg.ShowTaskbar = false; cfg.HideTrayIcon = true; cfg.HistoryEnabled = false; cfg.Save();
        using var backdrop = new Form { Text = "MellowMeter visual test background", FormBorderStyle = FormBorderStyle.None,
            Bounds = Screen.PrimaryScreen!.WorkingArea, ShowInTaskbar = false, TopMost = true };
        int pattern = 0;
        backdrop.Paint += (_, e) => {
            if (pattern < 2) e.Graphics.Clear(pattern == 0 ? Color.FromArgb(242,244,249) : Color.FromArgb(25,28,38));
            else {
                using var fill = new LinearGradientBrush(backdrop.ClientRectangle, Color.SteelBlue, Color.MistyRose, 35);
                e.Graphics.FillRectangle(fill, backdrop.ClientRectangle);
                using var pen = new Pen(Color.FromArgb(100, Color.White), 4);
                for (int x=0;x<backdrop.Width;x+=90) e.Graphics.DrawEllipse(pen,x,backdrop.Height/3,250,450);
            }
        };
        int exit = 1;
        backdrop.Shown += async (_, _) => {
            MainForm? main = null;
            var results = new List<object>();
            try {
                main = new MainForm(); main.Show(backdrop);
                var ui = (UIController)typeof(MainForm).GetField("_ui", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(main)!;
                await Task.Delay(700);
                using var settings = new SettingsForm(cfg, ui, main) { TopMost = true };
                settings.Show(backdrop);
                var policy = typeof(Settings).Assembly.GetType("LiteMonitor.src.UI.Helpers.WindowTheme");
                foreach (string theme in baseline ? new[] { "DarkFlat_Classic", "MellowLight" } : new[] { "GlassLight", "GlassDark" }) {
                    cfg.Skin = theme; AppActions.ApplyThemeAndLayout(cfg, ui, main);
                    for (pattern=0;pattern<3;pattern++) {
                        backdrop.Refresh(); settings.Activate();
                        await Task.Delay(300);
                        Snapshot(settings, Path.Combine(root,$"{theme}-background-{pattern}.png"));
                    }
                    results.Add(new { theme, nativeBackdrop = policy?.GetMethod("HasBackdrop",BindingFlags.NonPublic|BindingFlags.Static)?.Invoke(null,new object[]{settings}), dpi=settings.DeviceDpi });
                    settings.SwitchPage("Monitor"); await Task.Delay(150);
                    Snapshot(settings, Path.Combine(root,$"{theme}-metrics.png"));
                    settings.SwitchPage("MainPanel");
                    settings.Hide(); main.Location = new Point(150,150); main.Activate();
                    await Task.Delay(150); Snapshot(main,Path.Combine(root,$"{theme}-panel.png"));
                    cfg.HorizontalMode = false;
                    AppActions.ApplyThemeAndLayout(cfg, ui, main);
                    await Task.Delay(150); Snapshot(main,Path.Combine(root,$"{theme}-expanded.png"));
                    cfg.HorizontalMode = true;
                    AppActions.ApplyThemeAndLayout(cfg, ui, main);
                    main.ToggleTaskbar(true);
                    await Task.Delay(300);
                    var taskbar = Application.OpenForms.OfType<TaskbarForm>().FirstOrDefault();
                    if (taskbar != null) Snapshot(taskbar, Path.Combine(root,$"{theme}-taskbar.png"));
                    main.ToggleTaskbar(false);
                    settings.Show(backdrop);
                }
                typeof(Settings).GetProperty("GlassEffectsEnabled")?.SetValue(cfg, false);
                AppActions.ApplyThemeAndLayout(cfg,ui,main);
                await Task.Delay(200);
                if (!baseline) Snapshot(settings,Path.Combine(root,"GlassDark-effects-off.png"));
                results.Add(new { effectsOffBackdrop = policy?.GetMethod("HasBackdrop",BindingFlags.NonPublic|BindingFlags.Static)?.Invoke(null,new object[]{settings}) });
                settings.Close();
                using (var history = new HardwareTrendForm(cfg) { TopMost = true }) {
                    history.Show(backdrop); history.Activate(); history.Size = new Size(1100,820); await Task.Delay(250);
                    Snapshot(history,Path.Combine(root,"history-small.png"));
                    history.Close();
                }
                using (var info = new HardwareInfoForm() { TopMost = true }) {
                    info.Show(backdrop); info.Activate(); await Task.Delay(200); Snapshot(info,Path.Combine(root,"hardware.png")); info.Close();
                }
                using (var about = new AboutForm() { TopMost = true }) {
                    about.Show(backdrop); about.Activate(); await Task.Delay(150); Snapshot(about,Path.Combine(root,"about.png")); about.Close();
                }
                File.WriteAllText(Path.Combine(root,"capture-info.json"),JsonSerializer.Serialize(new { results,
                    method="Actual visible desktop pixels via CopyFromScreen; isolated app profile and synthetic bright/dark/patterned background windows. These are not wallpaper changes or DrawToBitmap captures." },new JsonSerializerOptions{WriteIndented=true}));
                exit = 0;
            } catch(Exception ex) { File.WriteAllText(Path.Combine(root,"error.txt"),ex.ToString()); }
            finally { if(main!=null) { main.Close(); await Task.Delay(300); main.Dispose(); } backdrop.Close(); }
        };
        Application.Run(backdrop);
        return exit;
    }

    private static void Snapshot(Form form, string path)
    {
        form.Refresh();
        var bounds = form is TaskbarForm ? form.RectangleToScreen(form.ClientRectangle) : form.Bounds;
        using var image = new Bitmap(bounds.Width,bounds.Height);
        using var g = Graphics.FromImage(image);
        g.CopyFromScreen(bounds.Location,Point.Empty,bounds.Size);
        image.Save(path,ImageFormat.Png);
    }
}
