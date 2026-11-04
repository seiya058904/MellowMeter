using System.Reflection;
using System.Runtime.InteropServices;
using LiteMonitor;
using LiteMonitor.src.Core;
using LiteMonitor.src.Core.Actions;
using LiteMonitor.src.UI;
using LiteMonitor.src.UI.Controls;
using LiteMonitor.src.UI.SettingsPage;

namespace DesktopSmoke;

// Motion and interaction coverage of the settings design system, run with motion genuinely on: the
// host's Windows animation preference is overridden for this process only, because a machine with
// animation effects off would otherwise skip every spring and prove nothing about them.
internal static partial class SmokeSuite
{
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hwnd, int msg, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr hwnd, int msg, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] private static extern uint GetGuiResources(IntPtr process, uint flag);
    [DllImport("user32.dll")] private static extern bool GetUpdateRect(IntPtr hwnd, IntPtr rect, bool erase);
    private const int WM_LBUTTONDOWN = 0x0201, WM_LBUTTONUP = 0x0202, WM_MOUSEMOVE = 0x0200, WM_MOUSEWHEEL = 0x020A, WM_KEYDOWN = 0x0100;

    private static IntPtr Point32(int x, int y) => new((y << 16) | (x & 0xFFFF));

    private static async Task MotionIdle(int limitMs = 2500)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (InterfaceMotion.ActiveCount > 0 && clock.ElapsedMilliseconds < limitMs) await Task.Delay(10);
        await Task.Delay(20);
    }

    private static async Task TestFlagshipSettings(MainForm main, Settings cfg, UIController ui)
    {
        _stage = "motion engine";
        InterfaceMotion.SystemPreferenceOverride = true;
        cfg.InterfaceAnimationsEnabled = true;
        cfg.Skin = "MellowLight";
        AppActions.ApplyThemeAndLayout(cfg, ui, main);
        Check(InterfaceMotion.Enabled, "Motion runs for the flagship checks regardless of the host preference");
        TestSpringMath();
        TestThemeContrast(main, cfg, ui);
        await TestPages(main, cfg, ui);
        InterfaceMotion.SystemPreferenceOverride = false;
        InterfaceMotion.RefreshSystemPreference();
        using (var settings = new SettingsForm(cfg, ui, main))
        {
            settings.Show(main);
            await Task.Delay(30);
            var page = Field<Dictionary<string, SettingsPageBase>>(settings, "_pages")["MainPanel"];
            Check(!InterfaceMotion.Enabled && InterfaceMotion.SystemAnimationsOff, "A system animation-off preference disables motion");
            var footer = Descendants(page).OfType<FooterText>().FirstOrDefault(f => f.Visible && f.Text == LanguageManager.T("Menu.InterfaceAnimationsSystemOff"));
            Check(footer != null, "Appearance explains that Windows animation effects turned motion off");
            var toggle = Descendants(page).OfType<LiteCheck>().First(c => c.Visible);
            bool original = toggle.Checked;
            toggle.Checked = !original;
            Check(InterfaceMotion.ActiveCount == 0 && !InterfaceMotion.ClockRunning && toggle.Position == (toggle.Checked ? 1 : 0), "With motion off a switch jumps and schedules no frames");
            toggle.Checked = original;
            settings.CancelButton!.PerformClick();
        }
        InterfaceMotion.SystemPreferenceOverride = null;
        InterfaceMotion.RefreshSystemPreference();
        cfg.InterfaceAnimationsEnabled = false;
        AppActions.ApplyThemeAndLayout(cfg, ui, main);
    }

    // Every text role must stay readable on every surface it is drawn on, in every shipped theme.
    private static void TestThemeContrast(MainForm main, Settings cfg, UIController ui)
    {
        string original = cfg.Skin;
        var themes = ThemeManager.GetAvailableThemes().ToArray();
        Check(themes.Length == 12, "All 12 themes are available for the contrast check");
        foreach (string skin in themes)
        {
            cfg.Skin = skin;
            AppActions.ApplyThemeAndLayout(cfg, ui, main);
            foreach (var (name, ink, surface) in new (string, Color, Color)[] {
                ("label on card", UIColors.TextMain, UIColors.CardBg),
                ("label on page", UIColors.TextMain, UIColors.MainBg),
                ("secondary label on card", UIColors.TextSub, UIColors.CardBg),
                ("caption and footer on page", UIColors.TextSubOnPage, UIColors.MainBg),
                ("secondary label in a field", UIColors.TextSub, UIColors.FieldBg),
                ("text on the primary button", UIColors.OnPrimary, UIColors.Primary),
                ("accent on card", UIColors.Primary, UIColors.CardBg),
                ("label on a secondary button", UIColors.TextMain, UIColors.ButtonFill),
                ("label on the selected sidebar item", UIColors.TextMain, UIColors.NavSelected),
                ("label in the menu", UIColors.TextMain, UIColors.Elevated),
                ("warning value in a field", UIColors.TextWarnOnField, UIColors.FieldBg),
                ("critical value in a field", UIColors.TextCritOnField, UIColors.FieldBg),
                ("unselected segment on its track", UIColors.EnsureContrast(UIColors.TextSub, UIColors.Blend(UIColors.CardBg, UIColors.TextMain, UIColors.IsDark ? 0.10f : 0.07f)), UIColors.Blend(UIColors.CardBg, UIColors.TextMain, UIColors.IsDark ? 0.10f : 0.07f)) })
            {
                double ratio = ContrastRatio(ink, surface);
                Check(ratio >= 4.5, $"{skin}: {name} has readable contrast ({ratio:F2}:1)");
            }
        }
        cfg.Skin = original;
        AppActions.ApplyThemeAndLayout(cfg, ui, main);
    }

    private static void TestSpringMath()
    {
        foreach (var spec in new[] { SpringSpec.Toggle, SpringSpec.Disclosure, SpringSpec.Indicator, SpringSpec.Scroll, SpringSpec.Popup })
        {
            var spring = new MotionSpring(0, 100, 0, spec);
            Check(MathF.Abs(spring.Sample(0).Value) < 0.001f, spec + ": a spring starts at its origin");
            Check(MathF.Abs(spring.Sample(5).Value - 100) < 0.01f && spring.Settled(5), spec + ": a spring settles at its target");
            // Continuity: the numerical derivative of the sampled value equals the reported velocity.
            const double t = 0.05, h = 1e-4;
            double numeric = (spring.Sample(t + h).Value - spring.Sample(t - h).Value) / (2 * h);
            Check(Math.Abs(numeric - spring.Sample(t).Velocity) < 8, spec + ": reported velocity matches the motion");
            var interrupted = spring.Sample(0.03);
            var retarget = new MotionSpring(interrupted.Value, 0, interrupted.Velocity, spec);
            var start = retarget.Sample(0);
            Check(MathF.Abs(start.Value - interrupted.Value) < 0.001f && MathF.Abs(start.Velocity - interrupted.Velocity) < 0.05f, spec + ": interrupting keeps position and velocity");
        }
        var critical = new MotionSpring(0, 100, 0, SpringSpec.Disclosure);
        double peak = 0;
        for (double t = 0; t < 2; t += 0.004) peak = Math.Max(peak, critical.Sample(t).Value);
        Check(peak <= 100.01, "A critically damped spring never overshoots");
        var bouncy = new MotionSpring(0, 1, 0, SpringSpec.Toggle);
        peak = 0;
        for (double t = 0; t < 2; t += 0.004) peak = Math.Max(peak, bouncy.Sample(t).Value);
        Check(peak > 1.005 && peak < 1.2, "The toggle spring has a small, bounded overshoot");
    }

    private static async Task TestPages(MainForm main, Settings cfg, UIController ui)
    {
        using var settings = new SettingsForm(cfg, ui, main);
        settings.Show(main);
        settings.Activate();
        await MotionIdle();
        var pages = Field<Dictionary<string, SettingsPageBase>>(settings, "_pages");
        FrameClock.TraceEnabled = true;

        _stage = "selector and page transition";
        var nav = Field<LiteSelector>(settings, "_nav");
        Check(nav.Count == 5 && nav.SelectedKey == "MainPanel", "Sidebar lists five categories with the first selected");
        int navChanges = 0;
        nav.SelectionCommitted += _ => navChanges++;
        FrameClock.Trace.Clear();
        nav.PerformSelect(3);
        Check(navChanges == 1 && Field<string>(settings, "_currentKey") == "Threshold", "Selecting a sidebar item switches the page once, synchronously");
        Check(nav.IndicatorPosition < 3 && InterfaceMotion.IsAnimating(nav, "indicator"), "The sidebar pill travels instead of jumping");
        var veil = Field<RevealVeil>(settings, "_veil");
        Check(veil.Visible, "A page-coloured veil covers the swap while it dissolves");
        var host = Field<Control>(settings, "_pnlContent");
        Check(host.Controls.Cast<Control>().Count(c => c.Visible) == 1, "Exactly one page is visible during a transition");
        await MotionIdle();
        Check(nav.IndicatorPosition == 3, "The sidebar pill lands exactly on the selected item");
        Check(!veil.Visible && InterfaceMotion.ActiveCount == 0 && !InterfaceMotion.ClockRunning, "The veil hides and the clock stops after the transition");
        Check(FrameClock.Trace.Count > 3, "Motion frames actually ran on the clock");
        nav.PerformSelect(3);
        Check(navChanges == 1, "Selecting the current item does nothing");
        nav.Focus();
        SendMessage(nav.Handle, WM_KEYDOWN, new IntPtr((int)Keys.Up), IntPtr.Zero);
        Check(Field<string>(settings, "_currentKey") == "Monitor", "Arrow keys move the sidebar selection");
        var navAccess = nav.AccessibilityObject;
        Check(navAccess.GetChildCount() == 5 && navAccess.GetChild(2)!.Name == LanguageManager.T("Menu.MonitorItemDisplay") && (navAccess.GetChild(2)!.State & AccessibleStates.Selected) != 0,
            "Sidebar items are exposed with names and selection state");
        navAccess.GetChild(0)!.DoDefaultAction();
        Check(Field<string>(settings, "_currentKey") == "MainPanel", "An assistive-technology invoke selects a sidebar item");
        await MotionIdle();

        _stage = "scrolling surface";
        settings.SwitchPage("Monitor");
        await MotionIdle();
        var surface = pages["Monitor"].Surface!;
        Check(surface.MaxScroll > 0 && surface.IndicatorVisible && !surface.IndicatorRevealed, "The overlay indicator is present but quiet on an overflowing page at rest");
        Check(surface.Content.Controls.Cast<Control>().All(c => c.Bounds.Right <= surface.ClientSize.Width), "Page content fits the viewport with no horizontal overflow");
        int contentHandlesBefore = surface.Content.Controls.Count;
        float start = surface.Offset;
        PostMessage(surface.Handle, WM_MOUSEWHEEL, new IntPtr(-120 << 16), IntPtr.Zero);
        await Task.Delay(40);
        float during = surface.Offset;
        Check(during > start && InterfaceMotion.IsAnimating(surface, "scroll"), "A wheel notch glides rather than jumps");
        Check(surface.IndicatorRevealed, "Scrolling reveals the indicator");
        await MotionIdle();
        float settled = surface.Offset;
        Check(settled > during, "The glide carries on to its destination");
        Check(surface.Content.Top == -(int)MathF.Round(settled), "Scrolling moves the single content window");
        await Task.Delay(1500);
        await MotionIdle();
        Check(!surface.IndicatorRevealed, "The indicator fades out at rest");
        surface.ScrollTo(surface.MaxScroll);
        PostMessage(surface.Handle, WM_MOUSEWHEEL, new IntPtr(-120 << 16), IntPtr.Zero);
        float peak = surface.Offset;
        for (int i = 0; i < 40; i++) { await Task.Delay(10); peak = Math.Max(peak, surface.Offset); }
        Check(peak > surface.MaxScroll + 0.5f, "Scrolling past the end overshoots like a rubber band");
        await MotionIdle();
        Check(MathF.Abs(surface.Offset - surface.MaxScroll) < 0.5f, "The overshoot returns exactly to the end");
        surface.Fling(-UIUtils.S(6000f));
        await MotionIdle();
        Check(surface.Offset >= -0.5f && surface.Offset < surface.MaxScroll, "A fling releases with momentum and stays in range");
        surface.ScrollTo(0);
        settings.Focus();
        Check(surface.HandleKey(Keys.PageDown), "PageDown is a scrolling key on the surface");
        await MotionIdle();
        Check(surface.Offset > 0, "PageDown scrolls the page");
        Check(surface.HandleKey(Keys.Home), "Home is a scrolling key on the surface");
        await MotionIdle();
        Check(surface.Offset == 0, "Home returns to the top");
        var far = Descendants(surface.Content).OfType<LiteCheck>().Where(c => c.Visible).OrderBy(c => c.PointToScreen(Point.Empty).Y).Last();
        surface.EnsureVisible(far);
        await MotionIdle();
        var rect = surface.RectangleToClient(far.RectangleToScreen(far.ClientRectangle));
        Check(rect.Top >= 0 && rect.Bottom <= surface.ClientSize.Height, "Keyboard focus reveal brings an off-screen control into view");
        surface.ScrollTo(0);

        _stage = "button press";
        var ok = (LiteButton)settings.AcceptButton!;
        var press = Field<MotionValue>(ok, "_pressMotion");
        var before = press.Value;
        SendMessage(ok.Handle, WM_LBUTTONDOWN, new IntPtr(1), Point32(4, 4));
        // The update region is empty straight after the message: the pressed frame is already drawn.
        Check(press.Value >= 0.7f && before < 0.7f && !GetUpdateRect(ok.Handle, IntPtr.Zero, false), "Pressing a button paints the pressed state inside the input event");
        ok.Capture = false; // ends the gesture without a click
        await MotionIdle();
        Check(press.Value == 0 && InterfaceMotion.ActiveCount == 0, "A cancelled press springs back and stops");

        _stage = "switch";
        settings.SwitchPage("MainPanel");
        await MotionIdle();
        var row = Descendants(pages["MainPanel"]).OfType<LiteSettingsItem>().First(i => i.Visible && Descendants(i).OfType<LiteCheck>().Any());
        var toggle = Descendants(row).OfType<LiteCheck>().Single();
        int changes = 0, clicks = 0;
        EventHandler changed = (_, _) => changes++, clicked = (_, _) => clicks++;
        toggle.CheckedChanged += changed;
        toggle.Click += clicked;
        bool initial = toggle.Checked;
        int height = toggle.Height / 2;
        int x = initial ? toggle.Width - UIUtils.S(10) : UIUtils.S(10);
        int direction = initial ? -1 : 1;
        SendMessage(toggle.Handle, WM_LBUTTONDOWN, new IntPtr(1), Point32(x, height));
        await Task.Delay(40);
        SendMessage(toggle.Handle, WM_MOUSEMOVE, new IntPtr(1), Point32(x + direction * UIUtils.S(6), height));
        Check(toggle.IsDragging && toggle.Position > 0 && toggle.Position < 1, "Moving the pressed thumb drags it");
        await Task.Delay(120);
        SendMessage(toggle.Handle, WM_MOUSEMOVE, new IntPtr(1), Point32(x + direction * UIUtils.S(30), height));
        await Task.Delay(120); // a slow release decides by position
        SendMessage(toggle.Handle, WM_MOUSEMOVE, new IntPtr(1), Point32(x + direction * UIUtils.S(30), height));
        Check(changes == 0 && toggle.Checked == initial, "A drag changes nothing until it is released");
        SendMessage(toggle.Handle, WM_LBUTTONUP, IntPtr.Zero, Point32(x + direction * UIUtils.S(30), height));
        Check(changes == 1 && toggle.Checked != initial && clicks == 1, "Releasing past the middle flips the switch exactly once");
        await MotionIdle();
        Check(toggle.Position == (toggle.Checked ? 1 : 0) && !toggle.IsDragging, "The thumb settles on its side");
        x = toggle.Checked ? toggle.Width - UIUtils.S(10) : UIUtils.S(10);
        direction = toggle.Checked ? -1 : 1;
        SendMessage(toggle.Handle, WM_LBUTTONDOWN, new IntPtr(1), Point32(x, height));
        await Task.Delay(40);
        SendMessage(toggle.Handle, WM_MOUSEMOVE, new IntPtr(1), Point32(x + direction * UIUtils.S(6), height));
        await Task.Delay(120);
        SendMessage(toggle.Handle, WM_MOUSEMOVE, new IntPtr(1), Point32(x - direction * UIUtils.S(2), height));
        await Task.Delay(120);
        SendMessage(toggle.Handle, WM_MOUSEMOVE, new IntPtr(1), Point32(x - direction * UIUtils.S(2), height));
        SendMessage(toggle.Handle, WM_LBUTTONUP, IntPtr.Zero, Point32(x - direction * UIUtils.S(2), height));
        Check(changes == 1, "Dragging back before release keeps the value");
        await MotionIdle();
        bool preFlick = toggle.Checked;
        x = toggle.Checked ? toggle.Width - UIUtils.S(10) : UIUtils.S(10);
        direction = toggle.Checked ? -1 : 1;
        SendMessage(toggle.Handle, WM_LBUTTONDOWN, new IntPtr(1), Point32(x, height));
        await Task.Delay(30);
        SendMessage(toggle.Handle, WM_MOUSEMOVE, new IntPtr(1), Point32(x + direction * UIUtils.S(4), height));
        await Task.Delay(5);
        SendMessage(toggle.Handle, WM_MOUSEMOVE, new IntPtr(1), Point32(x + direction * UIUtils.S(7), height));
        SendMessage(toggle.Handle, WM_LBUTTONUP, IntPtr.Zero, Point32(x + direction * UIUtils.S(7), height));
        Check(toggle.Checked != preFlick && changes == 2, "A quick flick toggles by velocity before the midpoint");
        await MotionIdle();
        toggle.CheckedChanged -= changed;
        toggle.Click -= clicked;
        toggle.Checked = initial;
        await MotionIdle();

        _stage = "picker menu";
        var picker = Descendants(pages["MainPanel"]).OfType<LiteComboBox>().First(c => c.Visible);
        Check(!Descendants(settings).OfType<ComboBox>().Any(), "No native combo box or its unthemed list exists in settings");
        var access = picker.AccessibilityObject;
        Check(access.Role == AccessibleRole.ComboBox && access.Value == picker.Text && !string.IsNullOrEmpty(access.Name), "The picker has an accessible role, name and value");
        int picks = 0;
        EventHandler picked = (_, _) => picks++;
        picker.SelectedIndexChanged += picked;
        int originalIndex = picker.SelectedIndex;
        picker.OpenMenu();
        var menu = LitePopupMenu.Current;
        Check(menu != null && picker.PopupOpen && menu.ItemCount == picker.Items.Count, "Opening the picker shows its themed menu");
        Check(menu!.Visible && Form.ActiveForm != menu, "The menu never takes activation from the dialog");
        Check(menu.AccessibilityObject.Role == AccessibleRole.List && menu.AccessibilityObject.GetChildCount() == menu.ItemCount, "The menu exposes its items");
        await MotionIdle();
        Rectangle menuAnchor = picker.RectangleToScreen(picker.ClientRectangle);
        menu.ProcessKey(Keys.Down);
        menu.ProcessKey(Keys.Up);
        menu.ProcessKey(Keys.Escape);
        Check(picks == 0 && picker.SelectedIndex == originalIndex && !picker.PopupOpen && LitePopupMenu.Current == null, "Escape closes the menu without changing the value");
        await MotionIdle();
        picker.OpenMenu();
        menu = LitePopupMenu.Current!;
        int target = originalIndex == 0 ? 1 : 0;
        menu.ProcessKey(originalIndex == 0 ? Keys.Down : Keys.Home);
        menu.ProcessKey(Keys.Enter);
        Check(picks == 1 && picker.SelectedIndex == target, "Keyboard selection in the menu commits exactly once");
        await MotionIdle();
        // Press, drag onto an item and release, like an iOS pull-down.
        picker.SelectedIndex = originalIndex;
        picks = 0;
        picker.OpenMenu();
        menu = LitePopupMenu.Current!;
        await MotionIdle();
        int dragTarget = originalIndex == 0 ? 1 : 0;
        Point itemCenter = Rectangle.Round(menu.AccessibilityObject.GetChild(dragTarget)!.Bounds) is var itemBounds ? new Point(itemBounds.Left + itemBounds.Width / 2, itemBounds.Top + itemBounds.Height / 2) : Point.Empty;
        LitePopupMenu.TrackPointer(itemCenter);
        Check(menu.HoverIndex == dragTarget, "Dragging over the menu highlights the item under the pointer");
        LitePopupMenu.ReleasePointer(itemCenter);
        Check(picks == 1 && picker.SelectedIndex == dragTarget && !picker.PopupOpen, "Releasing on an item selects it once and closes the menu");
        await MotionIdle();
        picker.SelectedIndex = originalIndex;
        picks = 0;
        picker.OpenMenu();
        menu = LitePopupMenu.Current!;
        var pressAnchor = Message.Create(picker.Handle, WM_LBUTTONDOWN, new IntPtr(1), Point32(2, 2));
        // The filter reads the live pointer, which is outside the menu unless someone parked it there.
        if (!menu.BodyContains(Cursor.Position))
        {
            Check(menu.PreFilterMessage(ref pressAnchor) && !picker.PopupOpen, "A press outside closes the menu and is consumed, so the anchor cannot reopen it");
        }
        else menu.ProcessKey(Keys.Escape);
        await MotionIdle();
        picker.Focus();
        SendMessage(picker.Handle, WM_KEYDOWN, new IntPtr((int)Keys.Down), IntPtr.Zero);
        Check(picks == 1 && picker.PopupOpen == false, "Arrow keys on a closed picker step the value like a native list");
        picker.SelectedIndex = originalIndex;
        SendMessage(picker.Handle, WM_MOUSEWHEEL, new IntPtr(-120 << 16), IntPtr.Zero);
        Check(picker.SelectedIndex == originalIndex, "The wheel over a closed picker never changes its value");
        picker.SelectedIndexChanged -= picked;
        await MotionIdle();

        _stage = "slider and stepper";
        var draft = Field<Settings>(settings, "_draftCfg");
        var slider = Descendants(pages["MainPanel"]).OfType<LiteSlider>().First(s => s.AccessibleName == LanguageManager.T("Menu.Width"));
        Check(slider.AccessibilityObject.Role == AccessibleRole.Slider && slider.AccessibilityObject.Value == $"{draft.PanelWidth} px", "The slider exposes its role and current value");
        draft.PanelWidth = 277;
        pages["MainPanel"].OnShow();
        Check(slider.Value == 277 && draft.PanelWidth == 277, "A profile value between stops is shown and left untouched");
        int commits = 0;
        Action<double> onCommit = _ => commits++;
        slider.ValueCommitted += onCommit;
        slider.BeginProbeDrag(0.5f);
        slider.MoveProbeDrag(0.3f);
        slider.MoveProbeDrag(0.6f);
        Check(commits == 0 && slider.IsDragging && draft.PanelWidth == 277, "Dragging a slider does not touch the draft until release");
        Check(slider.BubbleVisible, "A value bubble follows the thumb during a drag");
        slider.EndProbeDrag();
        Check(commits == 1 && !slider.IsDragging && draft.PanelWidth == (int)slider.Value && draft.PanelWidth != 277, "Releasing commits exactly one value from the allowed stops");
        Check(!slider.BubbleVisible, "The value bubble hides on release");
        await MotionIdle();
        Check(slider.ThumbPosition == slider.StopIndex, "The thumb snaps to its stop");
        slider.StepBy(1);
        Check(commits == 2, "A keyboard step commits once");
        slider.Focus();
        SendMessage(slider.Handle, WM_KEYDOWN, new IntPtr((int)Keys.Home), IntPtr.Zero);
        Check(draft.PanelWidth == 180 && commits == 3, "Home moves to the lowest stop");
        SendMessage(slider.Handle, WM_MOUSEWHEEL, new IntPtr(120 << 16), IntPtr.Zero);
        Check(draft.PanelWidth == 200, "The wheel adjusts a focused slider");
        slider.ValueCommitted -= onCommit;
        await MotionIdle();
        settings.SwitchPage("Threshold");
        await MotionIdle();
        var field = Descendants(pages["Threshold"]).OfType<LiteNumberInput>().First(n => n.Visible);
        double value = field.ValueDouble;
        field.StepBy(1);
        Check(field.ValueDouble > value, "The stepper raises the value");
        field.StepBy(-1);
        Check(field.ValueDouble == value, "The stepper lowers it back");
        int fieldLeft = field.Left;
        field.SignalInvalid();
        await Task.Delay(30);
        await MotionIdle();
        Check(field.Left == fieldLeft && InterfaceMotion.ActiveCount == 0, "The invalid shake returns the field to its place");
        settings.SwitchPage("MainPanel");
        await MotionIdle();

        _stage = "keyboard order";
        settings.SwitchPage("MainPanel");
        await MotionIdle();
        var mainPage = pages["MainPanel"];
        int lastY = -100000;
        var visited = new List<Control>();
        for (Control? c = mainPage.GetNextControl(null, true); c != null && visited.Count < 400; c = mainPage.GetNextControl(c, true))
            if (c.Visible && c.CanSelect && c.TabStop) visited.Add(c);
        bool ordered = true;
        foreach (var c in visited)
        {
            int cy = c.PointToScreen(Point.Empty).Y;
            if (cy < lastY - 1) ordered = false;
            lastY = cy;
        }
        Check(visited.Count >= 6 && ordered, "Tab order follows the visual top-to-bottom order of the page");

        _stage = "disclosure";
        var group = Descendants(pages["MainPanel"]).OfType<LiteSettingsGroup>().First(g => g.Collapsible && g.Visible);
        int closed = group.Height;
        group.Collapsed = false;
        Check(group.IsAnimatingDisclosure, "Opening a group animates its height");
        Check(settings.DisclosureVeil.Opacity > 0.5, "Rows start covered by the fading veil when a group opens");
        await Task.Delay(60);
        int partial = group.Height;
        Check(partial > closed, "The body grows during the transition");
        group.Collapsed = true; // interrupt
        await Task.Delay(40);
        group.Collapsed = false; // and reverse again
        await MotionIdle();
        int open = group.Height;
        Check(!settings.DisclosureVeil.Visible, "The disclosure veil is gone once the group has settled");
        Check(!group.IsAnimatingDisclosure && open > partial && Descendants(group).OfType<LiteSettingsItem>().All(r => r.Width > 0 && r.Parent!.ClientRectangle.Contains(r.Bounds)), "An interrupted disclosure settles fully open with rows in place");
        group.Collapsed = true;
        await MotionIdle();
        Check(group.Height == closed && InterfaceMotion.ActiveCount == 0, "Closing returns to the collapsed height and stops");

        _stage = "drag to reorder";
        await TestReorder(settings, pages["Monitor"], draft);

        _stage = "resizable window";
        settings.SwitchPage("Monitor");
        await MotionIdle();
        var normal = settings.Size;
        settings.Size = settings.MinimumSize;
        await Task.Delay(40);
        Check(settings.Width <= settings.MinimumSize.Width + 1 && settings.Height <= settings.MinimumSize.Height + 1, "The window accepts its minimum size");
        foreach (var key in new[] { "MainPanel", "Taskbar", "Monitor", "Threshold", "System" })
        {
            settings.SwitchPage(key);
            await MotionIdle();
            var active = pages[key];
            CheckSettingsRowGeometry(active, "minimum window " + key);
            Check(Descendants(settings).OfType<LiteButton>().Where(b => b.Parent?.GetType().Name == "ActionBar").All(b => settings.ClientRectangle.Contains(settings.RectangleToClient(b.RectangleToScreen(b.ClientRectangle)))),
                "minimum window " + key + ": all actions stay inside the window");
        }
        settings.Size = new Size(normal.Width + UIUtils.S(200), normal.Height + UIUtils.S(120));
        await Task.Delay(40);
        var mainGroup = Descendants(pages["System"]).OfType<LiteSettingsGroup>().First(g => g.Visible);
        Check(mainGroup.Width > UIUtils.S(500), "Groups follow a larger window");
        settings.Size = normal;
        await Task.Delay(30);

        _stage = "theme and language rebuild";
        settings.SwitchPage("Taskbar");
        await MotionIdle();
        var taskbarGroup = Descendants(pages["Taskbar"]).OfType<LiteSettingsGroup>().First(g => g.Collapsible && g.Visible);
        taskbarGroup.Collapsed = false;
        await MotionIdle();
        pages["Taskbar"].Surface!.ScrollTo(UIUtils.S(100f));
        cfg.Skin = "MellowDark";
        AppActions.ApplyThemeAndLayout(cfg, ui, main);
        pages = Field<Dictionary<string, SettingsPageBase>>(settings, "_pages");
        Check(Descendants(pages["Taskbar"]).OfType<LiteSettingsGroup>().First(g => g.Collapsible && g.Visible).Collapsed == false, "A theme rebuild keeps disclosure");
        Check(MathF.Abs(pages["Taskbar"].Surface!.Offset - UIUtils.S(100f)) <= 1, "A theme rebuild keeps the scroll position");
        Check(Field<LiteSelector>(settings, "_nav").SelectedKey == "Taskbar", "A theme rebuild keeps the selection");
        await MotionIdle();
        Check(InterfaceMotion.ActiveCount == 0 && !InterfaceMotion.ClockRunning, "Nothing keeps animating after a rebuild");
        cfg.Skin = "MellowLight";
        AppActions.ApplyThemeAndLayout(cfg, ui, main);
        await MotionIdle();

        settings.CancelButton!.PerformClick();
        FrameClock.TraceEnabled = false;
        FrameClock.Trace.Clear();
        await MotionIdle();
        Check(!InterfaceMotion.ClockRunning, "Closing settings stops the frame clock");

        _stage = "synthetic scales";
        foreach (float scale in new[] { 1f, 2f })
        {
            SettingsForm.ScaleOverride = scale;
            try
            {
                using var scaled = new SettingsForm(cfg, ui, main);
                scaled.Show(main);
                foreach (var key in new[] { "MainPanel", "Taskbar", "Monitor", "Threshold", "System" })
                {
                    scaled.SwitchPage(key);
                    await MotionIdle();
                    CheckSettingsRowGeometry(Field<Dictionary<string, SettingsPageBase>>(scaled, "_pages")[key], $"synthetic {scale:P0} {key}");
                }
                scaled.Close();
            }
            finally
            {
                SettingsForm.ScaleOverride = null;
                UIUtils.ScaleFactor = main.DeviceDpi / 96f;
            }
        }

        _stage = "high contrast palette";
        UIColors.HighContrastOverride = true;
        try
        {
            Check(!InterfaceMotion.Enabled, "High contrast disables motion");
            Check(UIColors.MainBg == SystemColors.Window && UIColors.TextMain == SystemColors.WindowText && UIColors.Primary == SystemColors.Highlight, "High contrast uses system colours");
            using var contrast = new SettingsForm(cfg, ui, main);
            contrast.Show(main);
            foreach (var key in new[] { "MainPanel", "Monitor", "Threshold" }) { contrast.SwitchPage(key); Check(InterfaceMotion.ActiveCount == 0, "High contrast navigation schedules no frames: " + key); }
            contrast.Close();
        }
        finally { UIColors.HighContrastOverride = null; }
        InterfaceMotion.SystemPreferenceOverride = true;

        _stage = "resource release";
        await MotionIdle();
        using var process = System.Diagnostics.Process.GetCurrentProcess();
        uint baselineUser = GetGuiResources(process.Handle, 1), baselineGdi = GetGuiResources(process.Handle, 0);
        for (int i = 0; i < 6; i++)
        {
            using var transient = new SettingsForm(cfg, ui, main);
            transient.Show(main);
            foreach (var key in new[] { "Monitor", "Taskbar", "System" }) transient.SwitchPage(key);
            await MotionIdle();
            transient.Close();
        }
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        await Task.Delay(40);
        uint user = GetGuiResources(process.Handle, 1), gdi = GetGuiResources(process.Handle, 0);
        Console.WriteLine($"resources after 6 opens: USER {baselineUser}->{user}, GDI {baselineGdi}->{gdi}");
        Check(user <= baselineUser + 6, $"Closing settings releases window handles ({baselineUser} -> {user})");
        Check(gdi <= baselineGdi + 40, $"Closing settings releases GDI objects ({baselineGdi} -> {gdi})");
    }

    private static async Task TestReorder(SettingsForm settings, SettingsPageBase page, Settings draft)
    {
        settings.SwitchPage("Monitor");
        await MotionIdle();
        var surface = page.Surface!;
        surface.ScrollTo(0);
        await MotionIdle();

        string[] DraftPanelOrder() { page.Save(); return draft.MonitorItems.OrderBy(x => x.SortIndex).Select(x => x.Key).ToArray(); }
        var firstBlockRows = Descendants(page).OfType<MonitorItemRow>().Where(r => r.Visible).OrderBy(r => r.PointToScreen(Point.Empty).Y).Take(4).ToArray();
        var rowsHost = (StackPanel)firstBlockRows[0].Parent!;
        var moved = rowsHost.Controls.OfType<MonitorItemRow>().Reverse().First();
        string[] before = DraftPanelOrder();
        int y = moved.PointToScreen(new Point(0, moved.Height / 2)).Y;
        int finished = 0;
        var commit = (Action<Control, int>)Delegate.CreateDelegate(typeof(Action<Control, int>), page, page.GetType().GetMethod("CommitReorder", BindingFlags.Instance | BindingFlags.NonPublic)!);

        // Cancel: nothing changes and the row returns.
        var session = new ReorderSession(rowsHost, moved, surface, y, commit, () => finished++, followPointer: false);
        Check(!moved.Visible && ReorderSession.Current == session, "Lifting a row replaces it with a floating sprite");
        await Task.Delay(30);
        session.MoveTo(y + moved.Height * 2);
        await MotionIdle();
        Check(session.InsertionIndex == 2, "Dragging over neighbours moves the insertion point");
        var neighbours = rowsHost.Controls.OfType<MonitorItemRow>().Reverse().Where(r => r != moved).ToArray();
        Check(neighbours[0].Top == moved.Top && neighbours[1].Top == moved.Top + moved.Height && neighbours[2].Top == moved.Top + moved.Height * 3, "Neighbours spring aside to open a gap at the insertion slot");
        Check(DraftPanelOrder().SequenceEqual(before), "The draft order does not change during a drag");
        session.Cancel();
        await MotionIdle();
        Check(finished == 1 && moved.Visible && ReorderSession.Current == null, "Cancelling a drag restores the row");
        Check(DraftPanelOrder().SequenceEqual(before), "Cancelling a drag keeps the order");

        // Esc and the button release reach the session through its message filter.
        session = new ReorderSession(rowsHost, moved, surface, y, commit, () => finished++, followPointer: false);
        await Task.Delay(20);
        session.MoveTo(y + moved.Height * 2);
        var escape = Message.Create(settings.Handle, WM_KEYDOWN, new IntPtr((int)Keys.Escape), IntPtr.Zero);
        Check(session.PreFilterMessage(ref escape) && ReorderSession.Current == null, "Esc cancels a drag and consumes the key");
        await MotionIdle();
        Check(finished == 2 && moved.Visible && DraftPanelOrder().SequenceEqual(before), "Esc leaves the order unchanged");
        session = new ReorderSession(rowsHost, moved, surface, y, commit, () => finished++, followPointer: false);
        await Task.Delay(20);
        var release = Message.Create(settings.Handle, WM_LBUTTONUP, IntPtr.Zero, IntPtr.Zero);
        Check(session.PreFilterMessage(ref release) && ReorderSession.Current == null, "Releasing the button ends the drag");
        await MotionIdle();
        Check(finished == 3 && moved.Visible && DraftPanelOrder().SequenceEqual(before), "Releasing without moving changes nothing");
        finished = 1;

        // Drop: exactly the same result as Move down twice.
        session = new ReorderSession(rowsHost, moved, surface, y, commit, () => finished++, followPointer: false);
        await Task.Delay(20);
        session.MoveTo(y + moved.Height * 2);
        session.Drop();
        await MotionIdle();
        Check(finished == 2 && moved.Visible, "Dropping finishes the gesture");
        string[] dropped = DraftPanelOrder();
        Check(!dropped.SequenceEqual(before) && dropped.Length == before.Length && dropped.Order().SequenceEqual(before.Order()), "A drop reorders without losing any metric");
        int from = Array.IndexOf(before, moved.Config.Key);
        Check(Array.IndexOf(dropped, moved.Config.Key) == from + 2, "The dropped row moved exactly two places in the saved order");
        var up = moved.Controls.OfType<LiteSortBtn>().Single(b => b.Text == "▲");
        up.PerformClick(); up.PerformClick();
        await MotionIdle();
        Check(DraftPanelOrder().SequenceEqual(before), "Move up commands exactly undo a drop: both paths share one order");

        // Holding the row near the bottom edge scrolls the list under it.
        float scrollBefore = surface.Offset;
        var edgeRow = rowsHost.Controls.OfType<MonitorItemRow>().Reverse().First();
        int edgeY = edgeRow.PointToScreen(new Point(0, edgeRow.Height / 2)).Y;
        session = new ReorderSession(rowsHost, edgeRow, surface, edgeY, commit, () => finished++, followPointer: false);
        session.MoveTo(surface.RectangleToScreen(surface.ClientRectangle).Bottom - UIUtils.S(6));
        await Task.Delay(260);
        Check(surface.Offset > scrollBefore, "Dragging near the edge scrolls the list");
        session.MoveTo(edgeY);
        await Task.Delay(30);
        session.Cancel();
        await MotionIdle();
        surface.ScrollTo(0);
        Check(edgeRow.Visible && DraftPanelOrder().SequenceEqual(before), "A cancelled edge scroll leaves the order alone");

        // Group blocks use the same gesture.
        var blocks = surface.Content.Controls.Cast<Control>().Where(c => c.GetType().Name == "GroupBlock").Reverse().ToArray();
        var block = (StackPanel)blocks[0];
        int blockY = block.PointToScreen(new Point(0, UIUtils.S(20))).Y;
        session = new ReorderSession(surface.Content, block, surface, blockY, commit, () => finished++, followPointer: false);
        await Task.Delay(20);
        session.MoveTo(blockY + blocks[0].Height + blocks[1].Height / 2 + UIUtils.S(4));
        Check(session.InsertionIndex == 1, "A group can be dragged past its neighbour");
        session.Drop();
        await MotionIdle();
        var reorderedGroups = DraftPanelOrder();
        Check(!reorderedGroups.SequenceEqual(before) && reorderedGroups.Order().SequenceEqual(before.Order()), "Dropping a group reorders its metrics together");
        var upGroup = Descendants(block).OfType<MonitorGroupHeader>().Single().Controls.OfType<LiteSortBtn>().Single(b => b.Text == "▲");
        upGroup.PerformClick();
        await MotionIdle();
        Check(DraftPanelOrder().SequenceEqual(before), "Moving the group back restores the saved order");

        // Filtered taskbar list: hidden entries keep their slots.
        SelectTab(page, 1);
        await MotionIdle();
        var only = Field<LiteCheck>(page, "_chkOnlyVisible");
        only.Checked = true;
        await MotionIdle();
        string[] TaskbarOrder() { page.Save(); return draft.MonitorItems.OrderBy(x => x.TaskbarSortIndex).ThenBy(x => x.SortIndex).Select(x => x.Key).ToArray(); }
        string[] Hidden(string[] order) => order.Where(k => !draft.MonitorItems.Single(m => m.Key == k).VisibleInTaskbar).ToArray();
        var fullBefore = TaskbarOrder();
        var card = Field<Control>(page, "_taskbarCard");
        var list = (StackPanel)card.GetType().GetProperty("RowsPanel")!.GetValue(card)!;
        var visibleRows = list.Controls.OfType<MonitorItemRow>().Reverse().ToArray();
        Check(visibleRows.Length >= 3, "The filtered list has rows to reorder");
        var flat = visibleRows[0];
        int flatY = flat.PointToScreen(new Point(0, flat.Height / 2)).Y;
        session = new ReorderSession(list, flat, surface, flatY, commit, () => finished++, followPointer: false);
        await Task.Delay(20);
        session.MoveTo(flatY + flat.Height + UIUtils.S(4));
        session.Drop();
        await MotionIdle();
        var fullAfter = TaskbarOrder();
        Check(fullAfter.Order().SequenceEqual(fullBefore.Order()), "A filtered drop keeps every metric");
        Check(Hidden(fullAfter).SequenceEqual(Hidden(fullBefore)), "A filtered drop leaves hidden metrics in their relative order");
        Check(fullAfter.Where(k => !Hidden(fullBefore).Contains(k)).First() != fullBefore.Where(k => !Hidden(fullBefore).Contains(k)).First(), "A filtered drop changes the visible order");
        var flatUp = flat.Controls.OfType<LiteSortBtn>().Single(b => b.Text == "▲");
        flatUp.PerformClick();
        await MotionIdle();
        only.Checked = false;
        await MotionIdle();
        SelectTab(page, 0);
        await MotionIdle();
        Check(InterfaceMotion.ActiveCount == 0, "Reordering leaves no running tracks");
        // Accessibility of the gesture path.
        var grip = Descendants(page).OfType<LiteDragGrip>().First(g => g.Visible);
        Check(grip.AccessibilityObject.Role == AccessibleRole.Grip && grip.AccessibilityObject.Name == LanguageManager.T("Menu.DragToReorder"), "The drag handle has an accessible name");
    }
}
