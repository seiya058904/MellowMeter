using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using LiteMonitor.src.Core;
using LiteMonitor.src.Core.Actions;
using LiteMonitor.src.UI.Controls;
using LiteMonitor.src.UI.SettingsPage;

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
            
            InitializeComponent(); 
            
            // ★★★ 关键点 1：构造时就初始化所有页面 ★★★
            InitPages(); 
        }

        private void InitializeComponent()
        {
            UIUtils.ScaleFactor = this.DeviceDpi / 96f;

            this.AutoScaleMode = AutoScaleMode.None; // Controls use UIUtils DPI scaling.
            this.ClientSize = UIUtils.S(new Size(880, 640));
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
            var pnlSidebar = new Panel { Dock = DockStyle.Left, Width = UIUtils.S(160), BackColor = UIColors.SidebarBg };
            
            _pnlNavContainer = new FlowLayoutPanel 
            { 
                Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, 
                Padding = UIUtils.S(new Padding(0, 20, 0, 0)), BackColor = UIColors.SidebarBg
            };
            
            pnlSidebar.Controls.Add(_pnlNavContainer);
            this.Controls.Add(pnlSidebar);

            // 底部按钮
            var pnlBottom = new Panel { Dock = DockStyle.Bottom, Height = UIUtils.S(60), BackColor = UIColors.MainBg };

            var flowBtns = new FlowLayoutPanel 
            { 
                Dock = DockStyle.Right, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, 
                Padding = UIUtils.S(new Padding(0, 14, 20, 0)), WrapContents = false, BackColor = Color.Transparent 
            };
            
            var btnOk = new LiteButton(LanguageManager.T("Menu.OK"), true);
            var btnCancel = new LiteButton(LanguageManager.T("Menu.Cancel"), false);
            var btnApply = new LiteButton(LanguageManager.T("Menu.Apply"), false);
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
        }

        private void InitPages()
        {
            while (_pnlNavContainer.Controls.Count > 0)
                _pnlNavContainer.Controls[0].Dispose();
            foreach (var page in _pages.Values) page.Dispose();
            _pages.Clear();
            _currentKey = "";
            
            // 注册所有页面
            AddNav("MainPanel", "🖥️ " + LanguageManager.T("Menu.MainFormSettings"), new MainPanelPage());
            AddNav("Taskbar", "➖ " + LanguageManager.T("Menu.TaskbarSettings"), new TaskbarPage());
            AddNav("Monitor", "📊 " + LanguageManager.T("Menu.MonitorItemDisplay"), new MonitorPage());
            AddNav("Threshold", "🔔 " + LanguageManager.T("Menu.Thresholds"), new ThresholdPage());
            AddNav("System", "⚙️ " + LanguageManager.T("Menu.SystemHardwar"), new SystemHardwarPage());

            _pnlNavContainer.PerformLayout();
            SwitchPage("MainPanel");
        }

        private void AddNav(string key, string text, SettingsPageBase page)
        {
            // ★★★ 关键点 2：将 Draft 传递给页面，而不是 Live Settings ★★★
            page.SetContext(_draftCfg, _mainForm, _ui);
            _pages[key] = page;
            var btn = new LiteNavBtn(text) { Tag = key };
            btn.Click += (s, e) => SwitchPage(key);
            _pnlNavContainer.Controls.Add(btn);
        }

        public void SwitchPage(string key)
        {
            if (_currentKey == key || !_pages.ContainsKey(key)) return;
            if (_pages.TryGetValue(_currentKey, out var previousPage)) previousPage.Save();
            _currentKey = key;

            // 更新导航按钮状态
            _pnlNavContainer.SuspendLayout();
            foreach (Control c in _pnlNavContainer.Controls)
                if (c is LiteNavBtn b) b.IsActive = ((string)b.Tag == key);
            _pnlNavContainer.ResumeLayout();
            _pnlNavContainer.Refresh(); 

            if (_pages.ContainsKey(key))
            {
                // ★★★ 核心修复开始 ★★★
                
                // 1. 挂起父容器布局：告诉系统“在我操作完之前，千万不要重绘”
                _pnlContent.SuspendLayout(); 
                
                try 
                {
                    _pnlContent.Controls.Clear();
                    var targetPage = _pages[key];
                    
                    // 2. 关键技：手动预设尺寸
                    // 在 Dock 生效前，先强制把它设为和父容器一样大。
                    // 这样即使 Layout 有微小延迟，肉眼看到的也是填满的状态。
                    targetPage.Size = _pnlContent.ClientSize; 
                    targetPage.Dock = DockStyle.Fill; // 双保险

                    _pnlContent.Controls.Add(targetPage);
                    targetPage.OnShow();
                }
                finally
                {
                    // 3. 恢复布局：此时控件大小已正确，系统一次性绘制最终画面
                    _pnlContent.ResumeLayout(); 
                }
                // ★★★ 核心修复结束 ★★★
            }
        }

        private void ApplySettings()
        {
            bool oldAutoStart = _cfg.AutoStart;
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
            SettingsChanger.Merge(_cfg, _draftCfg);
            bool autoStartChanged = oldAutoStart != _cfg.AutoStart;

            // 3. 持久化保存
            _cfg.Save();
            
            // 4. 应用副作用 (刷新界面)
            _applyingSettings = true;
            try { AppActions.ApplyAllSettings(_cfg, _mainForm, _ui, autoStartChanged); }
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
            Rectangle previousBounds = Bounds;
            Font previousFont = Font;
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
                InitPages();
                SwitchPage(selectedPage);
                Bounds = previousBounds;
            }
            finally
            {
                if (!ReferenceEquals(previousFont, Font) && !previousFont.IsSystemFont)
                    previousFont.Dispose();
                ResumeLayout(true);
                Invalidate(true);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                // Detached pages are not in the form's Controls collection.
                foreach (var page in _pages.Values) page.Dispose();
                _pages.Clear();
                _cfg.SyncToLanguage();
            }
            base.Dispose(disposing);
        }
    }
}
