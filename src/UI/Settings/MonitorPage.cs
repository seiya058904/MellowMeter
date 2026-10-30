using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text.Json;
using System.Windows.Forms;
using LiteMonitor.src.Core;
using LiteMonitor.src.UI.Controls;
using LiteMonitor.src.Core.Actions;
using LiteMonitor.src.UI.Helpers;

namespace LiteMonitor.src.UI.SettingsPage
{
    public class MonitorPage : SettingsPageBase
    {
        private Panel _container;
        private bool _isLoaded = false;
        private bool _isTaskbarTab = false;
        private string _lastDataSignature = ""; // ★★★ 性能优化：数据指纹

        private Panel _tabPanel;    
        private Panel _headerPanel; 

        private Button _btnTabMain;
        private Button _btnTabBar;
        private LiteCheck _chkLinkHorizontal;
        private LiteCheck _chkOnlyVisible;
        private LiteCheck _chkEditLabels;
        
        private Label _lblCol1; 
        private Label _lblCol2; 
        private Label _lblColUnit;
        private Label _lblCol3; 
        private Label _lblCol4; 

        private List<MonitorItemConfig> _workingList = new();
        private readonly Dictionary<string, MonitorItemRow> _rows = new();
        private readonly Dictionary<string, GroupBlock> _blocks = new();
        private readonly Panel _spacer = new() { Dock = DockStyle.Top, Height = UIUtils.S(12) };
        private Label? _bottomLabel;

        public MonitorPage()
        {
            this.BackColor = UIColors.MainBg;
            this.Dock = DockStyle.Fill;
            this.Padding = new Padding(0);

            _container = new BufferedPanel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                Padding = UIUtils.S(new Padding(18, 5, 18, 0))
            };
            this.Controls.Add(_container);

            InitHeader();
            this.Controls.SetChildIndex(_container, 0); 
        }

        [System.Diagnostics.CodeAnalysis.MemberNotNull(nameof(_tabPanel), nameof(_headerPanel), nameof(_btnTabMain), nameof(_btnTabBar), nameof(_chkLinkHorizontal), nameof(_chkOnlyVisible), nameof(_chkEditLabels), nameof(_lblCol1), nameof(_lblCol2), nameof(_lblColUnit), nameof(_lblCol3), nameof(_lblCol4))]
        private void InitHeader()
        {
            _tabPanel = new Panel 
            { 
                Dock = DockStyle.Top, Height = UIUtils.S(42), BackColor = UIColors.MainBg,
                Padding = UIUtils.S(new Padding(20, 0, 20, 0))
            };

            _btnTabMain = CreateTabButton(LanguageManager.T("Menu.MainForm"), true);
            _btnTabBar = CreateTabButton(LanguageManager.T("Menu.Taskbar") + " / " + LanguageManager.T("Menu.Horizontal"), false);

            _btnTabMain.Click += (s, e) => SwitchTab(false);
            _btnTabBar.Click += (s, e) => SwitchTab(true);

            _btnTabMain.Location = new Point(UIUtils.S(20), UIUtils.S(8));
            // [Fix] 动态跟随：防止 HighDPI 下 AutoSize 尚未计算完成导致重叠
            _btnTabMain.SizeChanged += (s, e) => {
                 _btnTabBar.Location = new Point(_btnTabMain.Right, UIUtils.S(8));
            };
            // 初始也设置一次 (虽然此时 Width 可能不准，但有了 SizeChanged 兜底)
            _btnTabBar.Location = new Point(_btnTabMain.Right, UIUtils.S(8));

            _chkLinkHorizontal = new LiteCheck(false, LanguageManager.T("Menu.HorizontalFollowsTaskbar")) 
            {
                AutoSize = true, Visible = false, ForeColor = UIColors.TextSub, Font = UIFonts.Bold(9F)
            };
            
            RegisterRefresh(() => { if (Config != null) _chkLinkHorizontal.Checked = Config.HorizontalFollowsTaskbar; });

            // Immediate binding to avoid conflict with MainPanelPage
            _chkLinkHorizontal.CheckedChanged += (s, e) => {
                if (Config != null) Config.HorizontalFollowsTaskbar = _chkLinkHorizontal.Checked;
            };
            
            _chkOnlyVisible = new LiteCheck(false, LanguageManager.T("Menu.OnlyShowEnabled")) 
            {
                AutoSize = true, Visible = false, ForeColor = UIColors.TextSub, Font = UIFonts.Bold(9F)
            };
            
            _chkOnlyVisible.CheckedChanged += (s, e) => 
            {
                SaveToWorkingList(isFilteredOverride: !_chkOnlyVisible.Checked);
                ReloadList();
            };

            _chkEditLabels = new LiteCheck(false, LanguageManager.T("Menu.CustomizeLabels")) { AccessibleName = LanguageManager.T("Menu.CustomizeLabels") };
            _chkEditLabels.CheckedChanged += (_, _) => {
                if (!_isLoaded) return;
                SaveToWorkingList();
                _container.SuspendLayout();
                foreach (var block in _blocks.Values) block.RowsPanel.SuspendLayout();
                try {
                    foreach (var row in _rows.Values) row.ShowEditors = _chkEditLabels.Checked;
                    foreach (var block in _blocks.Values) block.Header.ShowEditors = _chkEditLabels.Checked;
                    UpdateHeaderLayout();
                } finally {
                    foreach (var block in _blocks.Values) block.RowsPanel.ResumeLayout(true);
                    _container.ResumeLayout(true);
                }
            };
            
            _tabPanel.Resize += (s, e) => {
                _chkOnlyVisible.Location = new Point(UIUtils.S(20), UIUtils.S(48));
                _chkLinkHorizontal.Location = new Point(_chkOnlyVisible.Right + UIUtils.S(20), UIUtils.S(48));
                _chkEditLabels.Location = new Point(_tabPanel.Width - _chkEditLabels.Width - UIUtils.S(20), UIUtils.S(10));
            };

            _tabPanel.Controls.AddRange(new Control[] { _btnTabMain, _btnTabBar, _chkLinkHorizontal, _chkOnlyVisible, _chkEditLabels });

            _headerPanel = new Panel
            {
                Dock = DockStyle.Top, Height = UIUtils.S(34), BackColor = UIColors.MainBg, 
                Padding = UIUtils.S(new Padding(20, 0, 20, 0))
            };

            _lblCol1 = CreateHeaderLabel(); _lblCol2 = CreateHeaderLabel();
            _lblColUnit = CreateHeaderLabel(); 
            _lblCol3 = CreateHeaderLabel(); _lblCol4 = CreateHeaderLabel();
            _headerPanel.Controls.AddRange(new Control[] { _lblCol1, _lblCol2, _lblColUnit, _lblCol3, _lblCol4 });

            this.Controls.Add(_headerPanel);
            this.Controls.Add(_tabPanel);
        }

        private Label CreateHeaderLabel() => new Label { AutoSize = true, ForeColor = UIColors.TextSub, Font = UIFonts.Bold(8F), Visible = true };
        
        private Button CreateTabButton(string text, bool active)
        {
            // [Fix] Scale Padding for High DPI
            var btn = new LiteNavBtn(text) { Segmented = true };
            btn.Size = new Size(TextRenderer.MeasureText(text, btn.Font).Width + UIUtils.S(32), UIUtils.S(30));
            UpdateBtnStyle(btn, active);
            return btn;
        }

        private void UpdateBtnStyle(Button btn, bool active)
        {
            if (btn is LiteNavBtn tab) tab.IsActive = active;
        }

        private void SwitchTab(bool toTaskbarMode)
        {
            if (_isTaskbarTab == toTaskbarMode && _isLoaded) return;
            
            if (_isLoaded) SaveToWorkingList(); 

            _isTaskbarTab = toTaskbarMode;

            UpdateBtnStyle(_btnTabMain, !_isTaskbarTab);
            UpdateBtnStyle(_btnTabBar, _isTaskbarTab);
            
            _chkLinkHorizontal.Visible = _isTaskbarTab;
            _chkOnlyVisible.Visible = _isTaskbarTab;
            _tabPanel.Height = UIUtils.S(_isTaskbarTab ? 82 : 42);
            
            if (_isTaskbarTab && Config != null)
                _chkLinkHorizontal.Checked = Config.HorizontalFollowsTaskbar;

            _tabPanel.PerformLayout();
            
            // 切换 Tab 时必须刷新，因为显示模式变了
            ReloadList();
        }

        // ★★★ 性能优化核心：按需加载 ★★★
        public override void OnShow()
        {
            base.OnShow();
            if (Config == null) return;

            // Sync Checkbox if in taskbar tab (in case it was changed in MainPanelPage)
            _chkLinkHorizontal.Checked = Config.HorizontalFollowsTaskbar;

            // 1. 计算数据指纹 (Data Signature)
            // 只要 Config.MonitorItems 的内容（Keys的组合）没变，我们就不需要重新加载
            string currentSig = GenerateSignature();

            // 2. 脏检查：如果已加载且指纹未变，直接返回！(0ms 耗时)
            if (_isLoaded && currentSig == _lastDataSignature) 
            {
                return; 
            }

            ClearEditors();

            // 3. 数据变了（例如在插件页添加了新项），执行全量加载
            try
            {
                var json = JsonSerializer.Serialize(Config.MonitorItems);
                _workingList = JsonSerializer.Deserialize<List<MonitorItemConfig>>(json) ?? new List<MonitorItemConfig>();

                var liveMap = Config.MonitorItems.ToDictionary(x => x.Key, x => x);
                foreach (var item in _workingList)
                {
                    if (liveMap.TryGetValue(item.Key, out var liveItem))
                    {
                        item.DynamicLabel = liveItem.DynamicLabel;
                        item.DynamicTaskbarLabel = liveItem.DynamicTaskbarLabel;
                    }
                }
            }
            catch
            {
                _workingList = new List<MonitorItemConfig>();
            }

            _lastDataSignature = currentSig;
            ReloadList();
            
            _isLoaded = true;
            UpdateSortButtons(_container);
        }

        private string GenerateSignature() => Config == null ? "null" : SettingsChanger.CaptureMonitorState(Config);

        private void ReloadList()
        {
            if (_container == null || _container.IsDisposed || Config == null) return;
            
            _container.SuspendLayout();
            var scroll = new Point(-_container.AutoScrollPosition.X, -_container.AutoScrollPosition.Y);
            _container.Visible = false;
            foreach (var block in _blocks.Values) block.RowsPanel.SuspendLayout();
            try 
            {
                UpdateLanguageCacheFromWorkingList();

                // Detach reusable editors before arranging the alternate view.
                foreach (var block in _blocks.Values) block.RowsPanel.Controls.Clear();
                _container.Controls.Clear();
                UpdateHeaderLayout();
                var controlsToAdd = new List<Control> { _spacer };

                if (_isTaskbarTab)
                {
                    // [Fix] Taskbar sort fallback to Global SortIndex if TaskbarSortIndex is 0 (or equal)
                    var items = _workingList.Where(x => MenuMonitorHelper.IsAvailable(x.Key, Config))
                        .OrderBy(x => x.TaskbarSortIndex)
                        .ThenBy(x => x.SortIndex)
                        .ToList();

                    if (_chkOnlyVisible.Checked)
                        items = items.Where(x => x.VisibleInTaskbar).ToList();

                    for (int i = items.Count - 1; i >= 0; i--)
                    {
                        var row = GetRow(items[i], true);
                        controlsToAdd.Add(row);
                    }
                }
                else
                {
                    var items = _workingList.Where(x => MenuMonitorHelper.IsAvailable(x.Key, Config)).OrderBy(x => x.SortIndex).ToList();
                    var groups = items.GroupBy(x => x.UIGroup);
                    
                    foreach (var g in groups.Reverse())
                    {
                        var block = CreateGroupBlock(g.Key, g.ToList());
                        controlsToAdd.Add(block);
                    }
                }
                
                _container.Controls.AddRange(controlsToAdd.ToArray());

            // 底部提示文案
            _bottomLabel ??= new Label {
                AutoSize = true, ForeColor = UIColors.TextSub, Font = UIFonts.Regular(8.5F),
                Dock = DockStyle.Bottom, Padding = UIUtils.S(new Padding(0, 12, 12, 10))
            };
            _bottomLabel.Text = LanguageManager.T(Config.AdvancedSensorsEnabled ? "Menu.MonitorItemTip" : "Menu.BasicSensorsTip");
            _bottomLabel.MaximumSize = new Size(Math.Max(1, _container.ClientSize.Width - UIUtils.S(36)), 0);
            _container.Controls.Add(_bottomLabel);
            }
            finally
            {
                foreach (var block in _blocks.Values) block.RowsPanel.ResumeLayout(true);
                _container.Visible = true;
                _container.ResumeLayout(true);
                _container.AutoScrollPosition = scroll;
            }

            _isLoaded = true;
            UpdateSortButtons(_container);
        }

        private void UpdateLanguageCacheFromWorkingList()
        {
            if (_workingList == null || Config == null) return;
            
            LanguageManager.ClearOverrides();
            if (Config.GroupAliases != null)
            {
                foreach (var kv in Config.GroupAliases)
                    LanguageManager.SetOverride(UIUtils.Intern("Groups." + kv.Key), kv.Value);
            }
            
            foreach (var item in _workingList)
            {
                // [Fix] Use MetricLabelResolver to get the best available label (User > InfoService > Dynamic)
                // This ensures that plugin items show their dynamic labels even in the settings list
                string resolvedLabel = MetricLabelResolver.ResolveLabel(item);
                if (!string.IsNullOrEmpty(resolvedLabel))
                    LanguageManager.SetOverride(UIUtils.Intern("Items." + item.Key), resolvedLabel);

                string resolvedShort = MetricLabelResolver.ResolveShortLabel(item);
                if (!string.IsNullOrEmpty(resolvedShort))
                    LanguageManager.SetOverride(UIUtils.Intern("Short." + item.Key), resolvedShort);
            }
        }

        private void SyncUIToWorkingList()
        {
            foreach (Control c in _container.Controls)
            {
                if (c is MonitorItemRow row) row.SyncToConfig();
                else if (c is GroupBlock block)
                {
                    foreach (Control rc in block.RowsPanel.Controls)
                        if (rc is MonitorItemRow r) r.SyncToConfig();
                }
            }
        }

        private void UpdateHeaderLayout()
        {
            int y = UIUtils.S(10); 
            int offset = UIUtils.S(18);

            _lblCol1.Text = LanguageManager.T("Menu.MonitorItem");
            _lblCol1.Location = new Point(MonitorLayout.X_COL1 + offset, y);

            if (_isTaskbarTab) _lblCol2.Text = LanguageManager.T("Menu.short"); 
            else _lblCol2.Text = LanguageManager.T("Menu.name");  
            _lblCol2.Location = new Point(MonitorLayout.X_COL2 + offset, y);

            _lblColUnit.Text = LanguageManager.T("Menu.Unit");
            _lblColUnit.Location = new Point(MonitorLayout.X_COL_UNIT + offset, y);
            _lblCol2.Visible = _lblColUnit.Visible = _chkEditLabels.Checked;

            _lblCol3.Text = LanguageManager.T("Menu.showHide"); 
            _lblCol3.Location = new Point(MonitorLayout.X_COL3 + offset, y);

            _lblCol4.Text = LanguageManager.T("Menu.sort");
            _lblCol4.Location = new Point(MonitorLayout.X_COL4 + offset, y);
        }

        private GroupBlock CreateGroupBlock(string groupKey, List<MonitorItemConfig> items)
        {
            if (!_blocks.TryGetValue(groupKey, out var block))
            {
                string alias = Config?.GroupAliases.TryGetValue(groupKey, out var configuredAlias) == true ? configuredAlias : "";
                var header = new MonitorGroupHeader(groupKey, alias) { ShowEditors = _chkEditLabels.Checked };
                var rowsPanel = new BufferedPanel { Dock = DockStyle.Top, AutoSize = true, BackColor = UIColors.CardBg };
                block = new GroupBlock(header, rowsPanel);
                header.MoveUp += GroupHeader_MoveUp;
                header.MoveDown += GroupHeader_MoveDown;
                header.ToggleGroup += (_, checkState) => {
                    foreach (var row in rowsPanel.Controls.OfType<MonitorItemRow>()) row.SetPanelChecked(checkState);
                };
                header.Tag = block;
                _blocks.Add(groupKey, block);
            }
            block.Header.ShowEditors = _chkEditLabels.Checked;
            var rows = new List<Control>();
            for (int i = items.Count - 1; i >= 0; i--)
            {
                var row = GetRow(items[i], false);
                rows.Add(row);
            }
            block.RowsPanel.Controls.AddRange(rows.ToArray());
            
            return block;
        }

        private MonitorItemRow GetRow(MonitorItemConfig item, bool taskbar)
        {
            if (!_rows.TryGetValue(item.Key, out var row)) {
                row = new MonitorItemRow(item, taskbar);
                row.MoveUp += Row_MoveUp; row.MoveDown += Row_MoveDown;
                _rows.Add(item.Key, row);
            }
            row.SetMode(taskbar);
            row.ShowEditors = _chkEditLabels.Checked;
            return row;
        }

        private void ClearEditors()
        {
            foreach (var row in _rows.Values) row.Dispose();
            _rows.Clear();
            foreach (var block in _blocks.Values) block.Dispose();
            _blocks.Clear();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { ClearEditors(); _spacer.Dispose(); _bottomLabel?.Dispose(); }
            base.Dispose(disposing);
        }

        private void GroupHeader_MoveUp(object? sender, EventArgs e) { if (sender is MonitorGroupHeader h && h.Tag is GroupBlock b) MoveControl(b, -1); }
        private void GroupHeader_MoveDown(object? sender, EventArgs e) { if (sender is MonitorGroupHeader h && h.Tag is GroupBlock b) MoveControl(b, 1); }
        private void Row_MoveUp(object? sender, EventArgs e) { if (sender is Control r) MoveControl(r, -1); }
        private void Row_MoveDown(object? sender, EventArgs e) { if (sender is Control r) MoveControl(r, 1); }
        
        private void MoveControl(Control c, int dir)
        {
            var p = c.Parent as ScrollableControl; if (p == null) return;
            
            // [Fix] 防止 WinForms 自动滚动跳动
            p.SuspendLayout();
            var scroll = p.AutoScrollPosition;
            
            int idx = p.Controls.GetChildIndex(c); 
            int newIdx = idx - dir; 
            if (newIdx >= 0 && newIdx < p.Controls.Count) p.Controls.SetChildIndex(c, newIdx);

            p.AutoScrollPosition = new Point(Math.Abs(scroll.X), Math.Abs(scroll.Y));
            p.ResumeLayout(false);
            p.PerformLayout();
            UpdateSortButtons(_container);
        }

        private void UpdateSortButtons(Control parent)
        {
            foreach (Control child in parent.Controls) {
                Control? sortable = child is MonitorItemRow ? child : child is MonitorGroupHeader header ? header.Tag as GroupBlock : null;
                if (sortable?.Parent != null) {
                    var siblings = sortable.Parent.Controls.Cast<Control>().Where(c => c.GetType() == sortable.GetType()).ToList();
                    int index = siblings.IndexOf(sortable);
                    foreach (var button in child.Controls.OfType<LiteSortBtn>())
                        button.Enabled = button.Text == "▲" ? index < siblings.Count - 1 : index > 0;
                }
                UpdateSortButtons(child);
            }
        }

        private void SaveToWorkingList(bool? isFilteredOverride = null)
        {
            if (_workingList == null || Config == null) return;

            SyncUIToWorkingList();

            if (_isTaskbarTab)
            {
                bool isFiltered = (isFilteredOverride ?? _chkOnlyVisible.Checked)
                    || _workingList.Any(x => !MenuMonitorHelper.IsAvailable(x.Key, Config));
                var visibleOrder = _container.Controls.Cast<Control>().Reverse().OfType<MonitorItemRow>().Select(r => r.Config).ToList();
                SettingsChanger.UpdateTaskbarOrder(_workingList, visibleOrder, isFiltered);
            }
            else
            {
                var orderedVisible = new List<MonitorItemConfig>();
                var blocks = _container.Controls.Cast<Control>().Reverse().OfType<GroupBlock>();
                foreach (var block in blocks)
                {
                    string alias = block.Header.InputAlias.Inner.Text.Trim();
                    string defName = LanguageManager.GetOriginal("Groups." + block.Header.GroupKey);
                    if (!string.IsNullOrEmpty(alias) && alias != defName) 
                        Config.GroupAliases[block.Header.GroupKey] = alias;
                    else 
                        Config.GroupAliases.Remove(block.Header.GroupKey);

                    foreach (MonitorItemRow row in block.RowsPanel.Controls.Cast<Control>().Reverse())
                        orderedVisible.Add(row.Config);
                }

                // Keep unavailable advanced metrics in their existing slots while reordering the visible rows.
                var visibleSet = new HashSet<MonitorItemConfig>(orderedVisible);
                var queue = new Queue<MonitorItemConfig>(orderedVisible);
                var ordered = _workingList.OrderBy(x => x.SortIndex)
                    .Select(x => visibleSet.Contains(x) ? queue.Dequeue() : x).ToList();
                for (int i = 0; i < ordered.Count; i++) ordered[i].SortIndex = i;
            }
        }

        public override void Save()
        {
            if (!_isLoaded || Config == null || _workingList == null) return;
            
            SaveToWorkingList();
            
            // Delegate logic to SettingsOperations
            _lastDataSignature = SettingsChanger.SaveMonitorDraft(Config, _workingList, _lastDataSignature);
        }

        public override ViewState CaptureView() => base.CaptureView() with {
            TaskbarTab = _isTaskbarTab, OnlyVisible = _chkOnlyVisible.Checked, EditLabels = _chkEditLabels.Checked
        };

        public override void RestoreView(ViewState state)
        {
            _chkEditLabels.Checked = state.EditLabels;
            SwitchTab(state.TaskbarTab);
            _chkOnlyVisible.Checked = state.OnlyVisible;
            base.RestoreView(state);
        }

        private class GroupBlock : Panel
        {
            public MonitorGroupHeader Header { get; }
            public Panel RowsPanel { get; }
            public GroupBlock(MonitorGroupHeader header, Panel rowsPanel)
            {
                Header = header; RowsPanel = rowsPanel;
                Dock = DockStyle.Top; AutoSize = true;
                Padding = UIUtils.S(new Padding(0, 0, 0, 12));
                var card = new LiteCard { Dock = DockStyle.Top };
                card.Controls.Add(rowsPanel); card.Controls.Add(header);
                Controls.Add(card);
            }
        }
    }
}
