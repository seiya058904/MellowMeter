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

        private FlowLayoutPanel _pnlNavContainer;
        private BufferedPanel _pnlContent; // 使用现有的 BufferedPanel

        // 缓存所有页面实例
        private Dictionary<string, SettingsPageBase> _pages = new Dictionary<string, SettingsPageBase>();
        private readonly Dictionary<string, Func<SettingsPageBase>> _pageFactories = new();
        private string _currentKey = "";
        private string _visualSkin = "";
        private string _visualLanguage = "";
        private bool _applyingSettings;

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= 0x02000000; // Preserve upstream composited page rendering.
                return cp;
            }
        }

        protected override void OnResizeBegin(EventArgs e)
        {
            this.SuspendLayout();
            base.OnResizeBegin(e);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            WindowTheme.Apply(this);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            // A short high-DPI desktop should scroll the page, never hide the actions.
            var work = Screen.FromControl(this).WorkingArea;
            int margin = UIUtils.S(12);
            Size = new Size(Math.Min(Width, work.Width - margin * 2), Math.Min(Height, work.Height - margin * 2));
            Location = new Point(work.Left + (work.Width - Width) / 2, work.Top + (work.Height - Height) / 2);
        }

        protected override void OnResizeEnd(EventArgs e)
        {
            base.OnResizeEnd(e);
            this.ResumeLayout(true);
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

        [System.Diagnostics.CodeAnalysis.MemberNotNull(nameof(_pnlNavContainer), nameof(_pnlContent))]
        private void InitializeComponent()
        {
            UIUtils.ScaleFactor = this.DeviceDpi / 96f;

            this.AutoScaleMode = AutoScaleMode.None; // Controls use UIUtils DPI scaling.
            this.ClientSize = UIUtils.S(new Size(800, 540));
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = true;
            this.StartPosition = FormStartPosition.CenterScreen;
            this.Text = "MellowMeter · " + LanguageManager.T("Menu.SettingsPanel");
            this.Font = new Font("Segoe UI", 9F);
            this.BackColor = UIColors.MainBg;
            this.ForeColor = UIColors.TextMain;
            this.ShowInTaskbar = true;

            // 侧边栏
            var pnlSidebar = new Panel { Dock = DockStyle.Left, Width = UIUtils.S(156), BackColor = UIColors.SidebarBg };

            _pnlNavContainer = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false,
                Padding = UIUtils.S(new Padding(6, 8, 6, 0)), BackColor = UIColors.SidebarBg
            };
            var navigationTitle = new Label {
                Text = LanguageManager.T("Menu.SettingsPanel"), Dock = DockStyle.Top,
                Height = UIUtils.S(52), Padding = UIUtils.S(new Padding(18, 18, 0, 0)),
                Font = new Font("Segoe UI", 14F, FontStyle.Bold), ForeColor = UIColors.TextMain
            };
            pnlSidebar.Controls.Add(_pnlNavContainer);
            pnlSidebar.Controls.Add(navigationTitle);
            this.Controls.Add(pnlSidebar);

            // 底部按钮
            var pnlBottom = new Panel { Dock = DockStyle.Bottom, Height = UIUtils.S(60), BackColor = UIColors.MainBg };

            var flowBtns = new FlowLayoutPanel
            {
                Dock = DockStyle.Right, AutoSize = true, FlowDirection = FlowDirection.RightToLeft,
                Padding = UIUtils.S(new Padding(0, 12, 18, 0)), WrapContents = false, BackColor = Color.Transparent
            };

            var btnOk = new LiteButton(LanguageManager.T("Menu.OK"), true);
            var btnCancel = new LiteButton(LanguageManager.T("Menu.Cancel"), false);
            var btnApply = new LiteButton(LanguageManager.T("Menu.Apply"), false);
            foreach (var button in new[] { btnOk, btnCancel, btnApply })
                button.Margin = UIUtils.S(new Padding(4, 0, 4, 0));
            this.AcceptButton = btnOk;
            this.CancelButton = btnCancel;

            btnOk.Click += (s, e) => { ApplySettings(); this.DialogResult = DialogResult.OK; this.Close(); };
            btnCancel.Click += (s, e) => { this.DialogResult = DialogResult.Cancel; this.Close(); };
            btnApply.Click += (s, e) => { ApplySettings(); };

            flowBtns.Controls.Add(btnOk); flowBtns.Controls.Add(btnCancel); flowBtns.Controls.Add(btnApply);
            pnlBottom.Controls.Add(flowBtns);
            this.Controls.Add(pnlBottom);

            // 内容区 - 使用 LiteUI.cs 中定义的 BufferedPanel
            _pnlContent = new BufferedPanel { Dock = DockStyle.Fill, Padding = new Padding(0) };
            this.Controls.Add(_pnlContent);

            pnlSidebar.BringToFront();
            pnlBottom.SendToBack();
            _pnlContent.BringToFront();
            _visualSkin = _cfg.Skin;
            _visualLanguage = LanguageManager.CurrentLang;
            WindowTheme.Apply(this);
        }

        private void InitPages(string selectedPage = "MainPanel")
        {
            _pageFactories.Clear();
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
            var btn = new LiteNavBtn(text) { IconKey = key, Tag = key, Width = UIUtils.S(144), Margin = UIUtils.S(new Padding(0, 3, 0, 3)) };
            btn.Click += (_, _) => SwitchPage(key);
            _pnlNavContainer.Controls.Add(btn);
        }

        public void SwitchPage(string key)
        {
            if (_currentKey == key || !_pageFactories.TryGetValue(key, out var factory)) return;
            _pages.TryGetValue(_currentKey, out var previous);
            previous?.Save();
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
                foreach (var button in _pnlNavContainer.Controls.OfType<LiteNavBtn>())
                    button.IsActive = Equals(button.Tag, key);
            }
            finally { _pnlContent.ResumeLayout(true); }
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
            RefreshAppearance();
        }

        private void RefreshAppearance()
        {
            string selectedPage = _currentKey;
            var views = _pages.ToDictionary(pair => pair.Key, pair => pair.Value.CaptureView());
            Rectangle previousBounds = Bounds;
            var previousFonts = ControlResources.CaptureFonts(this);
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
                _cfg.SyncToLanguage();
            }
            base.Dispose(disposing);
            if (fonts != null) foreach (var font in fonts) font.Dispose();
        }
    }
}
