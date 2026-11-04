using System;
using System.Drawing;
using System.Windows.Forms;
using System.Linq;
using LiteMonitor.src.Core;
using LiteMonitor.src.UI.Controls;

namespace LiteMonitor.src.UI.SettingsPage
{
    public class MainPanelPage : SettingsPageBase
    {
        private bool _isLoaded = false;

        public MainPanelPage()
        {
            CreateSurface(LanguageManager.T("Menu.MainFormSettings"));
        }

        public override void OnShow()
        {
            base.OnShow();
            if (Config == null || _isLoaded) return;

            Surface!.Content.SuspendLayout();
            CreateAppearanceCard();
            CreateBehaviorCard();
            CreateHorizontalModeGroup();
            Surface.Content.ResumeLayout();
            _isLoaded = true;
        }

        private void CreateBehaviorCard()
        {
            if (Config == null) return;
            var group = new LiteSettingsGroup(LanguageManager.T("Menu.MainFormSettings")) { Collapsible = true, Collapsed = true };

            // 1. Hide Main Form (with logic)
            var chkHide = group.AddToggle(this, "Menu.HideMainForm",
                () => Config.HideMainForm,
                v => Config.HideMainForm = v);

            chkHide.CheckedChanged += (s, e) => EnsureSafeVisibility(chkHide, null, null);

            // 2. Toggles
            group.AddToggle(this, "Menu.TopMost", () => Config.TopMost, v => Config.TopMost = v);
            group.AddToggle(this, "Menu.ClampToScreen", () => Config.ClampToScreen, v => Config.ClampToScreen = v);
            group.AddToggle(this, "Menu.AutoHide", () => Config.AutoHide, v => Config.AutoHide = v);
            group.AddToggle(this, "Menu.ClickThrough", () => Config.ClickThrough, v => Config.ClickThrough = v);

            // 3. Double Click Action
            string[] actions = {
                LanguageManager.T("Menu.ActionSwitchLayout"),
                LanguageManager.T("Menu.ActionTaskMgr"),
                LanguageManager.T("Menu.ActionSettings"),
                LanguageManager.T("Menu.ActionTrafficHistory")
            };
            group.AddComboIndex(this, "Menu.DoubleClickAction", actions,
                () => Config.MainFormDoubleClickAction,
                idx => Config.MainFormDoubleClickAction = idx
            );

            AddGroupToPage(group);
        }

        private void CreateAppearanceCard()
        {
            if (Config == null) return;
            var group = new LiteSettingsGroup(LanguageManager.T("Menu.Appearance"));

            // 1. Theme
            group.AddCombo(this, "Menu.Theme", ThemeManager.GetAvailableThemes(),
                () => Config.Skin,
                v => Config.Skin = v);

            // 2. Orientation
            group.AddSegmented(this, "Menu.DisplayMode",
                new[] { LanguageManager.T("Menu.Vertical"), LanguageManager.T("Menu.Horizontal") },
                () => Config.HorizontalMode ? 1 : 0,
                idx => Config.HorizontalMode = (idx == 1));

            // 3. Width: the same values the setting has always offered, as slider stops.
            double[] widths = { 180, 200, 220, 240, 260, 280, 300, 360, 420, 480, 540, 600, 660, 720, 780, 840, 900, 960, 1020, 1080, 1140, 1200 };
            group.AddSlider(this, "Menu.Width", widths, v => $"{v:0} px",
                () => Config.PanelWidth,
                v => Config.PanelWidth = (int)v);

            // 4. Opacity
            double[] opacities = { 30, 40, 50, 60, 70, 75, 80, 85, 90, 95, 100 };
            group.AddSlider(this, "Menu.Opacity", opacities, v => $"{v:0}%",
                () => Math.Round(Config.Opacity * 100),
                v => Config.Opacity = v / 100.0);

            // 5. Scale
            double[] scales = { 50, 60, 70, 75, 80, 85, 90, 100, 125, 150, 175, 200 };
            group.AddSlider(this, "Menu.Scale", scales, v => $"{v:0}%",
                () => Math.Round(Config.UIScale * 100, 2),
                v => Config.UIScale = v / 100.0);

            group.AddToggle(this, "Menu.InterfaceAnimations", () => Config.InterfaceAnimationsEnabled, v => Config.InterfaceAnimationsEnabled = v);
            if (InterfaceMotion.SystemAnimationsOff) group.AddHint(LanguageManager.T("Menu.InterfaceAnimationsSystemOff"));
            AddGroupToPage(group);
        }

        private void CreateHorizontalModeGroup()
        {
            if (Config == null) return;
            // --- Group: Horizontal Mode ---
            var groupHorz = new LiteSettingsGroup(LanguageManager.T("Menu.Horizontal")) { Collapsible = true, Collapsed = true };

            // 1. Follow Taskbar
            groupHorz.AddToggle(this, "Menu.HorizontalFollowsTaskbar",
                () => Config.HorizontalFollowsTaskbar,
                v => Config.HorizontalFollowsTaskbar = v);

            // 2. Single Line
            groupHorz.AddToggle(this, "Menu.TaskbarSingleLine",
                () => Config.HorizontalSingleLine,
                v => Config.HorizontalSingleLine = v);

            // 3. Spacing
            groupHorz.AddInt(this, "Menu.TaskbarItemSpacing", "px",
                () => Config.HorizontalItemSpacing,
                v => Config.HorizontalItemSpacing = v, min: 0);

            groupHorz.AddInt(this, "Menu.TaskbarInnerSpacing", "px",
                () => Config.HorizontalInnerSpacing,
                v => Config.HorizontalInnerSpacing = v, min: 0);

            AddGroupToPage(groupHorz);
        }
    }
}
