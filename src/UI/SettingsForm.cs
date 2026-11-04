using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using LiteMonitor.src.Core;
using LiteMonitor.src.Core.Actions;
using LiteMonitor.src.UI.Controls;
using LiteMonitor.src.UI.SettingsPage;
using LiteMonitor.src.UI.Helpers;

namespace LiteMonitor.src.UI
{
    public class SettingsForm : Form
    {
        private Settings _cfg; // Live Settings
        private Settings _draftCfg; // Draft Settings
        private UIController _ui;
        private MainForm _mainForm;

        private LiteSelector _nav;
        private BufferedPanel _pnlContent; // host of the retained pages
        private RevealVeil? _veil;
        private RevealVeil? _disclosureVeil;
        internal RevealVeil DisclosureVeil => _disclosureVeil ??= new RevealVeil();

        // 缓存所有页面实例
        private Dictionary<string, SettingsPageBase> _pages = new Dictionary<string, SettingsPageBase>();
        private readonly Dictionary<string, Func<SettingsPageBase>> _pageFactories = new();
        private string _currentKey = "";
        private string _visualSkin = "";
        private string _visualLanguage = "";
        private bool _applyingSettings;
        private bool _shown;
        private bool _visualHighContrast;
        private bool _visualMotionOff;

        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hwnd, int msg, IntPtr w, IntPtr l);
        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool RedrawWindow(IntPtr hwnd, IntPtr rect, IntPtr region, uint flags);

        // Diagnostics only: renders at a synthetic scale on the host monitor for visual checks.
        internal static float? ScaleOverride { get; set; }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            WindowTheme.Apply(this);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            _shown = true;
            FitToWorkingArea(center: true);
        }

        // A short high-DPI desktop should scroll the page, never hide the actions.
        private void FitToWorkingArea(bool center)
        {
            var work = Screen.FromControl(this).WorkingArea;
            int margin = UIUtils.S(12);
            var limit = new Size(work.Width - margin * 2, work.Height - margin * 2);
            MinimumSize = new Size(Math.Min(MinimumSize.Width, limit.Width), Math.Min(MinimumSize.Height, limit.Height));
            Size = new Size(Math.Min(Width, limit.Width), Math.Min(Height, limit.Height));
            if (center) Location = new Point(work.Left + (work.Width - Width) / 2, work.Top + (work.Height - Height) / 2);
        }

        public SettingsForm(Settings cfg, UIController ui, MainForm mainForm)
        {
            _cfg = cfg;
            _ui = ui;
            _mainForm = mainForm;

            // ★★★ Draft 机制核心：创建深拷贝 ★★★
            _draftCfg = _cfg.DeepClone();

            InterfaceMotion.Configure(_cfg);
            InitializeComponent();

            // Build navigation now; construct each page only on its first visit.
            InitPages();
        }

        [System.Diagnostics.CodeAnalysis.MemberNotNull(nameof(_nav), nameof(_pnlContent))]
        private void InitializeComponent()
        {
            UIUtils.ScaleFactor = ScaleOverride ?? this.DeviceDpi / 96f;

            this.AutoScaleMode = AutoScaleMode.None; // Controls use UIUtils DPI scaling.
            this.ClientSize = UIUtils.S(new Size(DS.WindowWidth, DS.WindowHeight));
            this.FormBorderStyle = FormBorderStyle.Sizable;
            this.MaximizeBox = true;
            this.MinimizeBox = true;
            this.MinimumSize = SizeFromClientSize(UIUtils.S(new Size(DS.WindowMinWidth, DS.WindowMinHeight)));
            this.StartPosition = FormStartPosition.CenterScreen;
            this.Text = "MellowMeter · " + LanguageManager.T("Menu.SettingsPanel");
            this.Font = UiFonts.Get(TextRole.Body);
            this.BackColor = UIColors.MainBg;
            this.ForeColor = UIColors.TextMain;
            this.ShowInTaskbar = true;

            // 侧边栏
            _nav = new LiteSelector(vertical: true) { AccessibleName = LanguageManager.T("Menu.SettingsPanel") };
            _nav.SelectionCommitted += index => SwitchPage(_navKeys[index]);
            var sidebar = new SidebarPanel(_nav, LanguageManager.T("Menu.SettingsPanel")) { Dock = DockStyle.Left, Width = UIUtils.S(DS.SidebarWidth), BackColor = UIColors.SidebarBg };

            // 底部按钮
            var btnOk = new LiteButton(LanguageManager.T("Menu.OK"), true);
            var btnCancel = new LiteButton(LanguageManager.T("Menu.Cancel"), false);
            var btnApply = new LiteButton(LanguageManager.T("Menu.Apply"), false);
            this.AcceptButton = btnOk;
            this.CancelButton = btnCancel;

            btnOk.Click += (s, e) => { ApplySettings(); this.DialogResult = DialogResult.OK; this.Close(); };
            btnCancel.Click += (s, e) => { this.DialogResult = DialogResult.Cancel; this.Close(); };
            btnApply.Click += (s, e) => { ApplySettings(); };
            var actions = new ActionBar(new[] { btnOk, btnCancel, btnApply }) { Dock = DockStyle.Bottom, Height = UIUtils.S(DS.BottomBar), BackColor = UIColors.MainBg };

            // 内容区
            _pnlContent = new BufferedPanel { Dock = DockStyle.Fill, Padding = Padding.Empty, BackColor = UIColors.MainBg };

            // Dock order: content fills what the sidebar and action bar leave.
            this.Controls.Add(_pnlContent);
            this.Controls.Add(actions);
            this.Controls.Add(sidebar);
            // Keyboard order follows reading order; the window never opens inside a text field.
            sidebar.TabIndex = 0;
            _pnlContent.TabIndex = 1;
            actions.TabIndex = 2;

            _visualSkin = _cfg.Skin;
            _visualLanguage = LanguageManager.CurrentLang;
            _visualHighContrast = UIColors.HighContrast;
            _visualMotionOff = InterfaceMotion.SystemAnimationsOff;
            WindowTheme.Apply(this);
        }

        private readonly List<string> _navKeys = new();

        private void InitPages(string selectedPage = "MainPanel")
        {
            _pageFactories.Clear();
            _navKeys.Clear();
            AddNav("MainPanel", LanguageManager.T("Menu.MainFormSettings"), () => new MainPanelPage());
            AddNav("Taskbar", LanguageManager.T("Menu.TaskbarSettings"), () => new TaskbarPage());
            AddNav("Monitor", LanguageManager.T("Menu.MonitorItemDisplay"), () => new MonitorPage());
            AddNav("Threshold", LanguageManager.T("Menu.Thresholds"), () => new ThresholdPage());
            AddNav("System", LanguageManager.T("Menu.SystemHardwar"), () => new SystemHardwarPage());
            SwitchPage(selectedPage);
        }

        private void AddNav(string key, string text, Func<SettingsPageBase> factory)
        {
            _pageFactories[key] = factory;
            _navKeys.Add(key);
            _nav.Add(key, text, key);
        }

        public void SwitchPage(string key)
        {
            if (_currentKey == key || !_pageFactories.TryGetValue(key, out var factory)) return;
            _pages.TryGetValue(_currentKey, out var previous);
            previous?.Save();
            // With motion on, a page-coloured veil covers the swap and then dissolves; the new page is
            // complete and interactive beneath it from the first frame. No snapshot of either page.
            bool reveal = _shown && InterfaceMotion.Enabled && previous != null && Visible && WindowState != FormWindowState.Minimized;
            if (reveal)
            {
                _veil ??= new RevealVeil();
                _veil.Cover(_pnlContent, this);
            }
            bool atomic = _pnlContent.IsHandleCreated && _pnlContent.Visible;
            if (atomic) SendMessage(_pnlContent.Handle, 0x000B, IntPtr.Zero, IntPtr.Zero); // WM_SETREDRAW off
            _pnlContent.SuspendLayout();
            try
            {
                if (!_pages.TryGetValue(key, out var target))
                {
                    target = factory();
                    target.Visible = false;
                    target.SetContext(_draftCfg, _mainForm, _ui);
                    target.Size = _pnlContent.ClientSize;
                    target.Dock = DockStyle.Fill;
                    _pnlContent.Controls.Add(target);
                    _pages.Add(key, target);
                }
                target.OnShow();
                target.Visible = true;
                target.BringToFront();
                if (previous != null) previous.Visible = false;
                _currentKey = key;
                _nav.SetSelectedKey(key);
                if (reveal) target.PlayEntrance();
            }
            finally
            {
                _pnlContent.ResumeLayout(true);
                if (atomic)
                {
                    SendMessage(_pnlContent.Handle, 0x000B, new IntPtr(1), IntPtr.Zero); // WM_SETREDRAW on
                    RedrawWindow(_pnlContent.Handle, IntPtr.Zero, IntPtr.Zero, 0x0001 | 0x0080 | 0x0100); // INVALIDATE | ALLCHILDREN | UPDATENOW
                }
            }
            if (reveal) _veil!.Reveal();
        }

        // Tab can enter the page from the sidebar; what it lands on must be on screen.
        protected override bool ProcessTabKey(bool forward)
        {
            bool moved = base.ProcessTabKey(forward);
            if (moved && _pages.TryGetValue(_currentKey, out var page)) page.RevealActiveControl();
            return moved;
        }

        // High contrast and the Windows animation preference change the palette and the motion note,
        // both resolved at construction: rebuild once, keeping every view state and draft.
        internal void OnSystemPreferenceChanged()
        {
            UIColors.InvalidatePalette();
            if (IsDisposed || !_shown || (_visualHighContrast == UIColors.HighContrast && _visualMotionOff == InterfaceMotion.SystemAnimationsOff)) return;
            SaveDraftPages();
            RefreshAppearance(null);
        }

        protected override bool ProcessDialogKey(Keys keyData)
        {
            if (keyData is Keys.PageUp or Keys.PageDown or Keys.Home or Keys.End &&
                _pages.TryGetValue(_currentKey, out var page) && page.ScrollKey(keyData)) return true;
            return base.ProcessDialogKey(keyData);
        }

        // Per-monitor DPI change: the shell is rebuilt at the new scale like a theme change, keeping
        // visited pages, scroll, disclosure and drafts. Fonts are per-scale, so nothing is stale.
        protected override void OnDpiChanged(DpiChangedEventArgs e)
        {
            base.OnDpiChanged(e);
            if (!_shown || IsDisposed) return;
            SaveDraftPages();
            RefreshAppearance(e.SuggestedRectangle);
            FitToWorkingArea(center: false);
        }

        private void ApplySettings()
        {
            bool oldAdvancedSensors = _cfg.AdvancedSensorsEnabled;
            SaveDraftPages();

            // ★★★ Draft 机制核心：提交事务 ★★★
            // 1. 全局校验 (防止全隐藏死锁)
            bool noInteractiveWindow =
                (_draftCfg.HideMainForm || _draftCfg.ClickThrough) &&
                (!_draftCfg.ShowTaskbar || _draftCfg.TaskbarClickThrough) &&
                _draftCfg.HideTrayIcon;

            if (noInteractiveWindow)
            {
                // 自动纠正：如果所有可交互入口都被封死（隐藏或穿透+隐藏托盘），强制显示托盘
                _draftCfg.HideTrayIcon = false;
                MessageBox.Show("为了防止所有可交互入口都被死锁（隐藏或穿透+隐藏托盘），已强制显示托盘图标。", "MellowMeter", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }

            // 2. 合并变更到 Live Settings
            var changes = SettingsChanges.Between(_cfg, _draftCfg);
            if (!changes.Any) return;
            SettingsChanger.Merge(_cfg, _draftCfg);

            // 3. 持久化保存
            _cfg.Save();

            // 4. 应用副作用 (刷新界面)
            _applyingSettings = true;
            try { AppActions.ApplySettingsChanges(_cfg, _mainForm, _ui, changes); }
            finally { _applyingSettings = false; }

            // 5. [Fix] Rebase Draft to match Live
            // 将 Live 环境中由插件生成的最新监控项同步回 Draft，并保留动态显示属性
            SettingsChanger.RebaseDraftMonitorItems(_cfg, _draftCfg);

            RefreshTheme();

            if (oldAdvancedSensors != _cfg.AdvancedSensorsEnabled)
                MessageBox.Show(this, LanguageManager.T("Menu.RestartForSensors"), "MellowMeter", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void SaveDraftPages()
        {
            foreach (var page in _pages)
                if (page.Key != "Monitor") page.Value.Save();
            if (_pages.TryGetValue("Monitor", out var monitor)) monitor.Save();
        }

        public void RefreshTheme()
        {
            if (IsDisposed || _applyingSettings ||
                (_visualSkin == _cfg.Skin && _visualLanguage == LanguageManager.CurrentLang)) return;
            SaveDraftPages();
            // Follow an external appearance change only where the user has no pending override.
            if (_draftCfg.Skin == _visualSkin) _draftCfg.Skin = _cfg.Skin;
            if (_draftCfg.Language == _visualLanguage) _draftCfg.Language = _cfg.Language;
            RefreshAppearance(null);
        }

        private void RefreshAppearance(Rectangle? bounds)
        {
            string selectedPage = _currentKey;
            var views = _pages.ToDictionary(pair => pair.Key, pair => pair.Value.CaptureView());
            Rectangle previousBounds = bounds ?? Bounds;
            var previousFonts = ControlResources.CaptureFonts(this);
            _veil?.HideNow();
            _disclosureVeil?.HideNow();
            bool shown = _shown;
            _shown = false; // reconstruction is not navigation: no transitions
            SuspendLayout();
            try
            {
                AcceptButton = null;
                CancelButton = null;
                // Cached pages include detached controls; dispose those before replacing the shell.
                foreach (var page in _pages.Values) page.Dispose();
                _pages.Clear();
                _currentKey = "";
                while (Controls.Count > 0) Controls[0].Dispose();

                InitializeComponent();
                InitPages(selectedPage);
                foreach (var view in views)
                {
                    SwitchPage(view.Key);
                    _pages[view.Key].RestoreView(view.Value);
                }
                SwitchPage(selectedPage);
                Bounds = previousBounds;
            }
            finally
            {
                _shown = shown;
                foreach (var font in previousFonts) font.Dispose();
                ResumeLayout(true);
                Invalidate(true);
            }
        }

        protected override void Dispose(bool disposing)
        {
            var fonts = disposing ? ControlResources.CaptureFonts(this) : null;
            if (disposing)
            {
                // Detached pages are not in the form's Controls collection.
                foreach (var page in _pages.Values) page.Dispose();
                _pages.Clear();
                _veil?.Dispose();
                _veil = null;
                _disclosureVeil?.Dispose();
                _disclosureVeil = null;
                _cfg.SyncToLanguage();
            }
            base.Dispose(disposing);
            if (fonts != null) foreach (var font in fonts) font.Dispose();
            if (disposing && !Application.OpenForms.OfType<SettingsForm>().Any(f => !ReferenceEquals(f, this))) FrameClock.ReleaseIdle();
        }

        // Sidebar: owner-drawn title above the navigation list and a hairline to the content.
        private sealed class SidebarPanel : Panel
        {
            private readonly LiteSelector _nav;
            private readonly string _title;

            public SidebarPanel(LiteSelector nav, string title)
            {
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.Opaque | ControlStyles.ResizeRedraw, true);
                _nav = nav; _title = title;
                Controls.Add(nav);
            }

            protected override void OnLayout(LayoutEventArgs e)
            {
                int inset = UIUtils.S(10);
                _nav.SetBounds(inset, UIUtils.S(56), Math.Max(0, Width - inset * 2 - 1), _nav.Height);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                g.FillRectangle(Ink.B(UIColors.SidebarBg), e.ClipRectangle);
                Ink.Line(g, _title, UiFonts.Get(TextRole.Title), new Rectangle(UIUtils.S(22), UIUtils.S(14), Math.Max(0, Width - UIUtils.S(30)), UIUtils.S(36)), UIColors.TextMain);
                g.DrawLine(Ink.P(UIColors.Blend(UIColors.MainBg, UIColors.TextMain, 0.09f), 1), Width - 1, 0, Width - 1, Height);
            }
        }

        // Bottom toolbar: right-aligned actions with a hairline, always reachable in a short window.
        private sealed class ActionBar : Panel
        {
            private readonly LiteButton[] _buttons; // right to left

            public ActionBar(LiteButton[] buttons)
            {
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.Opaque | ControlStyles.ResizeRedraw, true);
                _buttons = buttons;
                int order = buttons.Length;
                foreach (var button in buttons)
                {
                    button.TabIndex = --order; // Tab reaches Apply, Cancel, then OK
                    int text = TextRenderer.MeasureText(button.Text, button.Font, Size.Empty, Ink.TextFlags).Width;
                    button.Width = Math.Max(UIUtils.S(DS.ButtonMinWidth), text + UIUtils.S(32));
                    Controls.Add(button);
                }
            }

            protected override void OnLayout(LayoutEventArgs e)
            {
                int x = Width - UIUtils.S(DS.PageGutter);
                foreach (var button in _buttons)
                {
                    x -= button.Width;
                    button.Location = new Point(x, (Height - button.Height) / 2);
                    x -= UIUtils.S(8);
                }
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                e.Graphics.FillRectangle(Ink.B(UIColors.MainBg), e.ClipRectangle);
                e.Graphics.DrawLine(Ink.P(UIColors.Blend(UIColors.MainBg, UIColors.TextMain, 0.09f), 1), 0, 0, Width, 0);
            }
        }
    }
}
