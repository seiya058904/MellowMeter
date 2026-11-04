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
        private StackPanel _container;
        private bool _isLoaded = false;
        private bool _isTaskbarTab = false;
        private string _lastDataSignature = ""; // ★★★ 性能优化：数据指纹

        private Panel _tabPanel;
        private Panel _headerPanel;

        private LiteSelector _tabs;
        private LiteCheck _chkLinkHorizontal;
        private LiteCheck _chkOnlyVisible;
        private LiteCheck _chkEditLabels;

        private List<MonitorItemConfig> _workingList = new();
        private readonly Dictionary<string, MonitorItemRow> _rows = new();
        private readonly Dictionary<string, GroupBlock> _blocks = new();
        private readonly GroupBlock _taskbarCard = new(null, new StackPanel { BackColor = UIColors.CardBg });
        private FooterText? _bottomLabel;
        private ReorderSession? _reorder;

        public MonitorPage()
        {
            _container = CreateSurface(null, UIUtils.S(new Padding(DS.PageGutter, 2, DS.PageGutter, 8)));
            InitHeader();
            Controls.SetChildIndex(Surface!, 0);
        }

        [System.Diagnostics.CodeAnalysis.MemberNotNull(nameof(_tabPanel), nameof(_headerPanel), nameof(_tabs), nameof(_chkLinkHorizontal), nameof(_chkOnlyVisible), nameof(_chkEditLabels))]
        private void InitHeader()
        {
            _tabPanel = new TitlePanel(LanguageManager.T("Menu.MonitorItemDisplay"))
            {
                Dock = DockStyle.Top,
                Height = TabPanelHeight(false),
                BackColor = UIColors.MainBg
            };

            _tabs = new LiteSelector(vertical: false)
            {
                AccessibleName = LanguageManager.T("Menu.MonitorItemDisplay")
            };
            _tabs.Add("panel", LanguageManager.T("Menu.MainForm"));
            _tabs.Add("taskbar", LanguageManager.T("Menu.Taskbar") + " / " + LanguageManager.T("Menu.Horizontal"));
            _tabs.SetSelected(0, animate: false);
            _tabs.SelectionCommitted += index => SwitchTab(index == 1);

            _chkLinkHorizontal = new LiteCheck(false, LanguageManager.T("Menu.HorizontalFollowsTaskbar"))
            {
                Visible = false, ForeColor = UIColors.TextSubOnPage, Font = UiFonts.Get(TextRole.Footnote), BackColor = UIColors.MainBg,
                AccessibleName = LanguageManager.T("Menu.HorizontalFollowsTaskbar")
            };

            RegisterRefresh(() => { if (Config != null) _chkLinkHorizontal.Checked = Config.HorizontalFollowsTaskbar; });

            // Immediate binding to avoid conflict with MainPanelPage
            _chkLinkHorizontal.CheckedChanged += (s, e) => {
                if (Config != null) Config.HorizontalFollowsTaskbar = _chkLinkHorizontal.Checked;
            };

            _chkOnlyVisible = new LiteCheck(false, LanguageManager.T("Menu.OnlyShowEnabled"))
            {
                Visible = false, ForeColor = UIColors.TextSubOnPage, Font = UiFonts.Get(TextRole.Footnote), BackColor = UIColors.MainBg,
                AccessibleName = LanguageManager.T("Menu.OnlyShowEnabled")
            };

            _chkOnlyVisible.CheckedChanged += (s, e) =>
            {
                SaveToWorkingList(isFilteredOverride: !_chkOnlyVisible.Checked);
                ReloadList();
            };

            _chkEditLabels = new LiteCheck(false, LanguageManager.T("Menu.CustomizeLabels"))
            {
                AccessibleName = LanguageManager.T("Menu.CustomizeLabels"), ForeColor = UIColors.TextSubOnPage,
                Font = UiFonts.Get(TextRole.Footnote), BackColor = UIColors.MainBg
            };
            foreach (var check in new[] { _chkLinkHorizontal, _chkOnlyVisible, _chkEditLabels }) check.Size = check.GetPreferredSize(Size.Empty);
            _chkEditLabels.CheckedChanged += (_, _) => {
                if (!_isLoaded) return;
                SaveToWorkingList();
                _container.SuspendLayout();
                foreach (var block in AllLists()) block.SuspendLayout();
                try {
                    foreach (var row in _rows.Values) row.ShowEditors = _chkEditLabels.Checked;
                    foreach (var block in _blocks.Values) block.Header!.ShowEditors = _chkEditLabels.Checked;
                    UpdateHeaderLayout();
                } finally {
                    foreach (var block in AllLists()) block.ResumeLayout(true);
                    _container.ResumeLayout(true);
                }
            };

            _tabPanel.Layout += (s, e) => ArrangeToolbar();
            _tabPanel.Controls.AddRange(new Control[] { _tabs, _chkLinkHorizontal, _chkOnlyVisible, _chkEditLabels });

            _headerPanel = new ColumnHeader(this)
            {
                Dock = DockStyle.Top, Height = UIUtils.S(26), BackColor = UIColors.MainBg
            };

            this.Controls.Add(_headerPanel);
            this.Controls.Add(_tabPanel);
            _tabPanel.TabIndex = 0;
            _headerPanel.TabIndex = 1;
            Surface!.TabIndex = 2;
        }

        private IEnumerable<StackPanel> AllLists()
        {
            yield return _taskbarCard.RowsPanel;
            foreach (var block in _blocks.Values) yield return block.RowsPanel;
        }

        private static int TabPanelHeight(bool taskbar) => UIUtils.S(taskbar ? 124 : 92);

        private void ArrangeToolbar()
        {
            int gutter = UIUtils.S(DS.PageGutter), top = UIUtils.S(50), width = _tabPanel.Width;
            int edit = width - gutter - _chkEditLabels.Width;
            _chkEditLabels.Location = new Point(edit, top + (UIUtils.S(34) - _chkEditLabels.Height) / 2);
            int tabs = Math.Min(NaturalTabsWidth, Math.Max(UIUtils.S(120), edit - gutter - UIUtils.S(12)));
            _tabs.SetBounds(gutter, top, tabs, _tabs.Height);
            int second = top + UIUtils.S(40);
            _chkOnlyVisible.Location = new Point(gutter, second);
            _chkLinkHorizontal.Location = new Point(_chkOnlyVisible.Right + UIUtils.S(20), second);
        }

        private int NaturalTabsWidth
        {
            get
            {
                if (_naturalTabs == 0) _naturalTabs = _tabs.Width;
                return _naturalTabs;
            }
        }
        private int _naturalTabs;

        private void SwitchTab(bool toTaskbarMode)
        {
            if (_isTaskbarTab == toTaskbarMode && _isLoaded) return;

            if (_isLoaded) SaveToWorkingList();

            _isTaskbarTab = toTaskbarMode;

            _tabs.SetSelected(_isTaskbarTab ? 1 : 0);

            _chkLinkHorizontal.Visible = _isTaskbarTab;
            _chkOnlyVisible.Visible = _isTaskbarTab;
            _tabPanel.Height = TabPanelHeight(_isTaskbarTab);

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
            float scroll = Surface!.Offset;
            _container.Visible = false;
            foreach (var list in AllLists()) list.SuspendLayout();
            try
            {
                UpdateLanguageCacheFromWorkingList();

                // Detach reusable editors before arranging the alternate view.
                foreach (var list in AllLists()) list.Controls.Clear();
                _container.Controls.Clear();
                UpdateHeaderLayout();
                var controlsToAdd = new List<Control>();

                // 底部提示文案
                _bottomLabel ??= new FooterText { BackColor = UIColors.MainBg };
                _bottomLabel.Text = LanguageManager.T(Config.AdvancedSensorsEnabled ? "Menu.MonitorItemTip" : "Menu.BasicSensorsTip");
                controlsToAdd.Add(_bottomLabel);

                if (_isTaskbarTab)
                {
                    // [Fix] Taskbar sort fallback to Global SortIndex if TaskbarSortIndex is 0 (or equal)
                    var items = _workingList.Where(x => MenuMonitorHelper.IsAvailable(x.Key, Config))
                        .OrderBy(x => x.TaskbarSortIndex)
                        .ThenBy(x => x.SortIndex)
                        .ToList();

                    if (_chkOnlyVisible.Checked)
                        items = items.Where(x => x.VisibleInTaskbar).ToList();

                    var rows = new List<Control>();
                    for (int i = items.Count - 1; i >= 0; i--) rows.Add(GetRow(items[i], true));
                    // Give the list its window first: each row is then re-parented once, not twice.
                    _taskbarCard.CreateControl();
                    _taskbarCard.RowsPanel.CreateControl();
                    _taskbarCard.RowsPanel.Controls.AddRange(rows.ToArray());
                    controlsToAdd.Add(_taskbarCard);
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
            }
            finally
            {
                foreach (var list in AllLists()) list.ResumeLayout(false);
                _container.Visible = true;
                _container.ResumeLayout(true);
                Surface.ScrollTo(scroll);
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
            foreach (var row in VisibleRows()) row.SyncToConfig();
        }

        private IEnumerable<MonitorItemRow> VisibleRows()
        {
            foreach (Control c in _container.Controls)
                if (c is GroupBlock block)
                    foreach (Control rc in block.RowsPanel.Controls)
                        if (rc is MonitorItemRow r) yield return r;
        }

        private void UpdateHeaderLayout() => _headerPanel.Invalidate();

        private GroupBlock CreateGroupBlock(string groupKey, List<MonitorItemConfig> items)
        {
            if (!_blocks.TryGetValue(groupKey, out var block))
            {
                string alias = Config?.GroupAliases.TryGetValue(groupKey, out var configuredAlias) == true ? configuredAlias : "";
                var header = new MonitorGroupHeader(groupKey, alias) { ShowEditors = _chkEditLabels.Checked };
                var rowsPanel = new StackPanel { BackColor = UIColors.CardBg };
                block = new GroupBlock(header, rowsPanel);
                header.MoveUp += GroupHeader_MoveUp;
                header.MoveDown += GroupHeader_MoveDown;
                header.ToggleGroup += (_, checkState) => {
                    foreach (var row in rowsPanel.Controls.OfType<MonitorItemRow>()) row.SetPanelChecked(checkState);
                };
                header.Tag = block;
                var captured = block;
                header.Grip.DragStarted += e => BeginReorder(captured, header.Grip, e);
                _blocks.Add(groupKey, block);
            }
            block.Header!.ShowEditors = _chkEditLabels.Checked;
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
                var captured = row;
                row.Grip.DragStarted += e => BeginReorder(captured, captured.Grip, e);
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
            if (disposing) { _reorder?.Cancel(); ClearEditors(); _taskbarCard.Dispose(); _bottomLabel?.Dispose(); }
            base.Dispose(disposing);
        }

        private void GroupHeader_MoveUp(object? sender, EventArgs e) { if (sender is MonitorGroupHeader h && h.Tag is GroupBlock b) MoveControl(b, -1); }
        private void GroupHeader_MoveDown(object? sender, EventArgs e) { if (sender is MonitorGroupHeader h && h.Tag is GroupBlock b) MoveControl(b, 1); }
        private void Row_MoveUp(object? sender, EventArgs e) { if (sender is Control r) MoveControl(r, -1); }
        private void Row_MoveDown(object? sender, EventArgs e) { if (sender is Control r) MoveControl(r, 1); }

        // Keyboard and accessibility path of reordering. Neighbours glide to their new slots.
        private void MoveControl(Control c, int dir)
        {
            var p = c.Parent; if (p == null) return;

            int idx = p.Controls.GetChildIndex(c);
            int newIdx = idx - dir;
            if (newIdx < 0 || newIdx >= p.Controls.Count || p.Controls[newIdx].GetType() != c.GetType()) return;
            var other = p.Controls[newIdx];
            int otherTop = other.Top, ownTop = c.Top;
            p.Controls.SetChildIndex(c, newIdx);
            p.PerformLayout();
            UpdateSortButtons(_container);
            foreach (var (control, from) in new[] { (c, ownTop), (other, otherTop) })
            {
                int target = control.Top;
                if (target == from || !InterfaceMotion.Enabled) continue;
                control.Top = from;
                InterfaceMotion.Spring(control, "reorder", SpringSpec.Reorder, from, target, v => control.Top = (int)MathF.Round(v));
            }
        }

        // Gesture path: a session lifts the row and tracks the pointer; only its drop changes the order.
        private void BeginReorder(Control item, LiteDragGrip grip, MouseEventArgs press)
        {
            if (_reorder != null || item.Parent is not StackPanel host || Surface == null) return;
            _reorder = new ReorderSession(host, item, Surface, grip.PointToScreen(press.Location).Y, CommitReorder, () => _reorder = null);
        }

        // The same child-order change as repeated Move commands, so saving is unchanged.
        private void CommitReorder(Control item, int visualIndex)
        {
            if (item.Parent is not { } host) return;
            var siblings = host.Controls.Cast<Control>().Where(c => c.GetType() == item.GetType()).Reverse().ToList();
            var occupant = siblings[Math.Clamp(visualIndex, 0, siblings.Count - 1)];
            host.Controls.SetChildIndex(item, host.Controls.GetChildIndex(occupant));
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
                var visibleOrder = _taskbarCard.RowsPanel.Controls.Cast<Control>().Reverse().OfType<MonitorItemRow>().Select(r => r.Config).ToList();
                SettingsChanger.UpdateTaskbarOrder(_workingList, visibleOrder, isFiltered);
            }
            else
            {
                var orderedVisible = new List<MonitorItemConfig>();
                var blocks = _container.Controls.Cast<Control>().Reverse().OfType<GroupBlock>();
                foreach (var block in blocks)
                {
                    if (block.Header == null) continue;
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

        // Large title of this page: pinned, because the mode control beneath it stays reachable.
        private sealed class TitlePanel : Panel
        {
            private readonly string _title;
            public TitlePanel(string title)
            {
                _title = title;
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.Opaque, true);
            }
            protected override void OnPaint(PaintEventArgs e)
            {
                e.Graphics.FillRectangle(Ink.B(UIColors.MainBg), e.ClipRectangle);
                int left = UIUtils.S(DS.PageGutter + DS.RowInset);
                Ink.Line(e.Graphics, _title, UiFonts.Get(TextRole.LargeTitle), new Rectangle(left, UIUtils.S(DS.PageTop), Math.Max(0, Width - left), UIUtils.S(38)), UIColors.TextMain);
            }
        }

        // Column captions, owner-drawn and aligned with the trailing-anchored rows.
        private sealed class ColumnHeader : Panel
        {
            private readonly MonitorPage _page;
            public ColumnHeader(MonitorPage page)
            {
                _page = page;
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.Opaque | ControlStyles.ResizeRedraw, true);
            }
            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                g.FillRectangle(Ink.B(UIColors.MainBg), e.ClipRectangle);
                var font = UiFonts.Get(TextRole.CaptionStrong);
                Color ink = UIColors.TextSubOnPage;
                int inset = UIUtils.S(DS.PageGutter + DS.RowInset);
                int rowWidth = Math.Max(0, Width - inset * 2);
                void Column(string key, int x, int width, TextFormatFlags flags = TextFormatFlags.Left) =>
                    Ink.Line(g, LanguageManager.T(key), font, new Rectangle(inset + x, 0, width, Height), ink, flags);
                bool editors = _page._chkEditLabels.Checked;
                Column("Menu.MonitorItem", 0, editors ? MonitorLayout.NameX - UIUtils.S(8) : MonitorLayout.SwitchX(rowWidth) - UIUtils.S(12));
                if (editors)
                {
                    Column(_page._isTaskbarTab ? "Menu.short" : "Menu.name", MonitorLayout.NameX, MonitorLayout.NameWidth(rowWidth));
                    Column("Menu.Unit", MonitorLayout.UnitX(rowWidth), UIUtils.S(MonitorLayout.UnitWidth));
                }
                int switchX = MonitorLayout.SwitchX(rowWidth);
                Column("Menu.showHide", switchX - UIUtils.S(24), UIUtils.S(DS.SwitchWidth + 48), TextFormatFlags.HorizontalCenter);
                Column("Menu.sort", MonitorLayout.UpX(rowWidth), rowWidth - MonitorLayout.UpX(rowWidth), TextFormatFlags.Right);
            }
        }

        // One card of the list: an optional group header and its rows. The card is painted here and
        // the content is inset past its corners, so no window needs rounded clipping.
        private sealed class GroupBlock : StackPanel, IReorderVisual
        {
            public MonitorGroupHeader? Header { get; }
            public StackPanel RowsPanel { get; }
            private int Gap => UIUtils.S(DS.GroupGap - 4);
            public int VisualHeight => Height - Gap;
            public GroupBlock(MonitorGroupHeader? header, StackPanel rowsPanel)
            {
                Header = header; RowsPanel = rowsPanel;
                BackColor = UIColors.MainBg;
                int inset = UIUtils.S(DS.RowInset), edge = UIUtils.S(4);
                Padding = new Padding(inset, edge, inset, edge + Gap);
                Controls.Add(rowsPanel);
                if (header != null) Controls.Add(header);
            }

            protected override void OnPaintBackground(PaintEventArgs e)
            {
                e.Graphics.FillRectangle(Ink.B(UIColors.MainBg), e.ClipRectangle);
                Ink.Fill(e.Graphics, new RectangleF(0, 0, Width, Height - Gap), UIUtils.S((float)DS.RadiusCard), UIColors.CardBg);
                if (UIColors.HighContrast) Ink.Outline(e.Graphics, new RectangleF(0, 0, Width, Height - Gap), UIUtils.S((float)DS.RadiusCard), SystemColors.WindowText);
            }
        }
    }
}
