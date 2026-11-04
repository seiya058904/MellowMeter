using System;
using System.Drawing;
using System.Windows.Forms;
using LiteMonitor.src.Core;
using LiteMonitor.src.SystemServices.InfoService; // [New]

namespace LiteMonitor.src.UI.Controls
{
    // Column geometry of the monitor list, anchored to the trailing edge so rows follow the window.
    public static class MonitorLayout
    {
        private const int Grip = 26, Sort = 26, GapGrip = 4, GapSwitch = 12;
        public static int H_ROW => UIUtils.S(DS.RowHeight);
        public static int GripX(int width) => width - UIUtils.S(Grip);
        public static int DownX(int width) => GripX(width) - UIUtils.S(GapGrip + Sort);
        public static int UpX(int width) => DownX(width) - UIUtils.S(Sort);
        public static int SwitchX(int width) => UpX(width) - UIUtils.S(GapSwitch + DS.SwitchWidth);
        public static int UnitX(int width) => SwitchX(width) - UIUtils.S(14 + UnitWidth);
        public const int UnitWidth = 64;
        public static int NameX => UIUtils.S(112);
        public static int NameWidth(int width) => Math.Max(UIUtils.S(48), UnitX(width) - UIUtils.S(8) - NameX);
    }

    public class MonitorItemRow : Panel, IHeightForWidth
    {
        public MonitorItemConfig Config { get; private set; }

        private string _name;                   // owner-drawn title
        private LiteUnderlineInput _inputName;
        private LiteUnderlineInput _inputShort;
        private LiteUnderlineInput _inputUnit;

        private LiteCheck _chkPanel;
        private LiteCheck _chkTaskbar;

        private LiteSortBtn _btnUp;
        private LiteSortBtn _btnDown;
        internal LiteDragGrip Grip { get; }

        private float _reveal;
        private bool _isTaskbarMode = false; // 记录当前模式
        private bool _showEditors;
        public bool ShowEditors
        {
            get => _showEditors;
            set {
                _name = _inputName.Inner.Text;
                if (_showEditors == value) { Invalidate(); return; }
                _showEditors = value;
                ApplyModeVisibility();
            }
        }

        public event EventHandler? MoveUp;
        public event EventHandler? MoveDown;

        public int HeightForWidth(int width) => MonitorLayout.H_ROW;

        // ★★★ 修改构造函数：增加 isTaskbarMode 参数 ★★★
        public MonitorItemRow(MonitorItemConfig item, bool isTaskbarMode)
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.Opaque, true);
            this.Config = item;
            this._isTaskbarMode = isTaskbarMode; // 记录模式
            this.BackColor = UIColors.CardBg;

            // [Refactor] 使用统一解析器
            string labelResolved = MetricLabelResolver.ResolveLabel(item);

            string defName = LanguageManager.T(UIUtils.Intern("Items." + item.Key));
            string valName = !string.IsNullOrEmpty(labelResolved) ? labelResolved : defName;
            _name = valName;

            // Name Input
            _inputName = new LiteUnderlineInput(valName, "", "", 100, UIColors.TextMain) { ClearButtonEnabled = true, Visible = false, AccessibleName = LanguageManager.T("Menu.name") + " · " + valName };

            // Short Input
            // [Refactor] 使用统一解析器
            string shortResolved = MetricLabelResolver.ResolveShortLabel(item);

            string defShortKey = UIUtils.Intern("Short." + item.Key);
            string defShort = LanguageManager.T(defShortKey);
            if (defShort.StartsWith("Short.")) defShort = item.Key.Split('.')[1];

            string valShort = !string.IsNullOrEmpty(shortResolved) ? shortResolved : defShort;

            _inputShort = new LiteUnderlineInput(valShort, "", "", 100, UIColors.TextMain) { ClearButtonEnabled = true, Visible = false, AccessibleName = LanguageManager.T("Menu.short") + " · " + valName };

            // ★★★ Unit Input 初始化逻辑 ★★★
            // 1. 根据传入的模式，获取正确的默认单位 (如 Panel="{u}/s", Taskbar="{u}")
            // 使用 Settings 上下文，确保用户能看到 {u} 占位符
            var unitCtx = isTaskbarMode ? MetricUtils.UnitContext.SettingsTaskbar : MetricUtils.UnitContext.SettingsPanel;
            string defUnit = MetricUtils.GetUnitStr(item.Key, 0, unitCtx);

            // 2. 获取当前配置值
            string? userConfig = isTaskbarMode ? item.UnitTaskbar : item.UnitPanel;

            // 3. 决定显示文本 (null显示默认值, ""显示空格)
            // 注意：如果配置是""(隐藏)，我们给输入框显示一个空格" "，让用户知道这里不是空的(默认)，而是特意留空的。
            string displayUnit;
            if (userConfig == null) displayUnit = defUnit;   // Auto -> 显示默认
            else if (userConfig == "") displayUnit = " ";    // Hide -> 显示空格
            else displayUnit = userConfig;                   // Custom -> 显示自定义

            // 4. 初始化输入框
            _inputUnit = new LiteUnderlineInput(displayUnit, "", "", MonitorLayout.UnitWidth, UIColors.TextSub) { Visible = false, AccessibleName = LanguageManager.T("Menu.Unit") + " · " + valName };
            _inputUnit.Inner.Font = UiFonts.Get(TextRole.Footnote);

            // Switches
            _chkPanel = new LiteCheck(item.VisibleInPanel) { AccessibleName = valName + " · " + LanguageManager.T("Menu.MainForm") };
            _chkTaskbar = new LiteCheck(item.VisibleInTaskbar) { Visible = false, AccessibleName = valName + " · " + LanguageManager.T("Menu.Taskbar") };

            // Move commands stay as real buttons: they are the keyboard path to reordering.
            _btnUp = new LiteSortBtn("▲") { Reveal = 0 };
            _btnDown = new LiteSortBtn("▼") { Reveal = 0 };
            _btnUp.Click += (s, e) => MoveUp?.Invoke(this, EventArgs.Empty);
            _btnDown.Click += (s, e) => MoveDown?.Invoke(this, EventArgs.Empty);
            Grip = new LiteDragGrip();

            this.Controls.AddRange(new Control[] {
                _inputName, _inputShort, _inputUnit,
                _chkPanel, _chkTaskbar,
                _btnUp, _btnDown, Grip
            });
            foreach (Control child in Controls)
            {
                child.MouseEnter += (_, _) => SetHover(true);
                child.MouseLeave += (_, _) => CheckHover();
            }

            this.Height = MonitorLayout.H_ROW;
            ApplyModeVisibility();
        }

        private void SetHover(bool hovered) =>
            InterfaceMotion.Spring(this, "reveal", SpringSpec.Hover, _reveal, hovered ? 1 : 0, v =>
            {
                _reveal = v;
                _btnUp.Reveal = _btnDown.Reveal = v;
                _btnUp.Invalidate(); _btnDown.Invalidate();
            });

        private void CheckHover()
        {
            if (!IsHandleCreated || !ClientRectangle.Contains(PointToClient(Cursor.Position))) SetHover(false);
        }

        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); SetHover(true); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); CheckHover(); }

        // 提取出来的可见性设置方法
        private void ApplyModeVisibility()
        {
            _inputUnit.Visible = _showEditors;
            _inputName.Visible = _showEditors && !_isTaskbarMode;
            _inputShort.Visible = _showEditors && _isTaskbarMode;
            _chkPanel.Visible = !_isTaskbarMode;
            _chkTaskbar.Visible = _isTaskbarMode;
            Arrange();
            Invalidate();
        }

        private static void Place(Control control, int x, int y, int? width = null)
        {
            if (control.Left != x || control.Top != y || (width is int w && control.Width != w))
                control.SetBounds(x, y, width ?? control.Width, control.Height);
        }

        private void Arrange()
        {
            int width = Width;
            if (width <= 0) return;
            int Middle(Control c) => (Height - c.Height) / 2;
            Place(Grip, MonitorLayout.GripX(width), Middle(Grip));
            Place(_btnDown, MonitorLayout.DownX(width), Middle(_btnDown));
            Place(_btnUp, MonitorLayout.UpX(width), Middle(_btnUp));
            Place(_chkPanel, MonitorLayout.SwitchX(width), Middle(_chkPanel));
            Place(_chkTaskbar, MonitorLayout.SwitchX(width), Middle(_chkTaskbar));
            if (!_showEditors) return;
            Place(_inputName, MonitorLayout.NameX, Middle(_inputName), MonitorLayout.NameWidth(width));
            Place(_inputShort, MonitorLayout.NameX, Middle(_inputShort), MonitorLayout.NameWidth(width));
            Place(_inputUnit, MonitorLayout.UnitX(width), Middle(_inputUnit));
        }

        protected override void OnLayout(LayoutEventArgs e) => Arrange();

        // [需求2] 暴露方法供组开关调用
        public void SetPanelChecked(bool check)
        {
            _chkPanel.Checked = check;
        }

        public void SetMode(bool isTaskbarMode)
        {
            // 如果模式没变，直接返回
            if (_isTaskbarMode == isTaskbarMode) return;

            _isTaskbarMode = isTaskbarMode;

            // 重新获取默认值和配置
            // 使用 Settings 上下文，确保切换模式时，Placeholder 依然正确显示带 {u} 的格式
            // [Fix] 根据模式动态选择上下文
            var unitCtx = isTaskbarMode ? MetricUtils.UnitContext.SettingsTaskbar : MetricUtils.UnitContext.SettingsPanel;
            string defUnit = MetricUtils.GetUnitStr(Config.Key, 0, unitCtx);
            string? userConfig = isTaskbarMode ? Config.UnitTaskbar : Config.UnitPanel;

            // 更新 Input 状态
            // null -> 默认, "" -> 空格
            string display = (userConfig == null) ? defUnit : (userConfig == "" ? " " : userConfig);
            _inputUnit.Inner.Text = display;
            ApplyModeVisibility();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.FillRectangle(Ink.B(UIColors.CardBg), e.ClipRectangle);
            bool keyColumn = _showEditors && !_isTaskbarMode;
            string title = keyColumn ? Config.Key : _name;
            int limit = _showEditors ? MonitorLayout.NameX - UIUtils.S(8) : MonitorLayout.SwitchX(Width) - UIUtils.S(12);
            Ink.Line(g, title, UiFonts.Get(keyColumn ? TextRole.Footnote : TextRole.Body), new Rectangle(0, 0, Math.Max(0, limit), Height), keyColumn ? UIColors.TextSub : UIColors.TextMain);
            if (Parent is { } parent && !ReferenceEquals(parent.Controls[0], this))
                g.DrawLine(Ink.P(UIColors.Separator, 1), 0, Height - 1, Width, Height - 1);
        }

        public void SyncToConfig()
        {
            // Name
            string valName = _inputName.Inner.Text; // 不使用 Trim，允许用户输入空格隐藏
            string originalName = LanguageManager.GetOriginal(UIUtils.Intern("Items." + Config.Key));
            
            // [Fix] Handle untranslated plugin keys (e.g. DASH.UniversalAPI.0.val)
            // If GetOriginal returns the full key (fallback), try to extract the last part as the field name
            if (Config.Key.StartsWith("DASH.") && originalName.Contains("DASH."))
            {
                int lastDot = originalName.LastIndexOf('.');
                if (lastDot >= 0 && lastDot < originalName.Length - 1) 
                    originalName = originalName.Substring(lastDot + 1);
            }

            // 规范化：如果是纯空格，则转为一个空格
            if (valName.Length > 0 && string.IsNullOrWhiteSpace(valName))
            {
                 valName = " ";
                 // 即时反馈
                 if (_inputName.Inner.Text != valName) _inputName.Inner.Text = valName;
            }
            else
            {
                // 否则去掉首尾空格
                string trimmed = valName.Trim();
                if (trimmed != valName) 
                {
                    valName = trimmed;
                    _inputName.Inner.Text = valName;
                }
            }

            // [Improvement] Check against current runtime dynamic label as well
            string currentDynamicLabel = InfoService.Instance.GetValue("PROP.Label." + Config.Key);
            if (string.IsNullOrEmpty(currentDynamicLabel)) currentDynamicLabel = Config.DynamicLabel;

            // 如果等于默认值，或者等于当前的动态值，则存为空字符串 (恢复自动模式)
            // 否则存为用户自定义值 (锁定模式)
            if (valName != " " && (string.Equals(valName, originalName, StringComparison.OrdinalIgnoreCase) || valName == currentDynamicLabel))
                Config.UserLabel = "";
            else
                Config.UserLabel = valName;

            // Short (TaskbarLabel)
            // ★★★ 修复：允许用户输入空格 (" ") 来隐藏标签 ★★★
            string rawShort = _inputShort.Inner.Text;
            string valShort;
            
            // Handle space logic for short label
            if (rawShort.Length > 0 && string.IsNullOrWhiteSpace(rawShort))
            {
                valShort = " ";
            }
            else
            {
                valShort = rawShort.Trim();
            }
            
            // 同样应用动态值检查逻辑
            string defShortKey = UIUtils.Intern("Short." + Config.Key);
            // [Fix] Must use GetOriginal to ignore current user overrides, otherwise it will toggle back to default
            string defShort = LanguageManager.GetOriginal(defShortKey); 
            
            // 获取当前理论上的动态 Short (InfoService > DynamicShort > null)
            string currentDynamicShort = InfoService.Instance.GetValue("PROP.ShortLabel." + Config.Key);
            if (string.IsNullOrEmpty(currentDynamicShort)) currentDynamicShort = Config.DynamicTaskbarLabel;

            if (valShort == "")
            {
                Config.TaskbarLabel = "";
            }
            else if (valShort == " ")
            {
                Config.TaskbarLabel = " ";
            }
            else
            {
                 // 如果等于默认值，或者等于当前的动态简称，则存为空字符串 (恢复自动模式)
                if (valShort == defShort || valShort == currentDynamicShort)
                    Config.TaskbarLabel = "";
                else
                    Config.TaskbarLabel = valShort;
            }
            
            // 最终赋值 (逻辑已在上方处理完毕)
            // 无需再次检查，直接使用计算好的 valShort 即可
            // (注意：上方的 if/else 块已经给 Config.TaskbarLabel 赋值了，这里需要移除多余的旧代码)

            // ★★★ 核心修改：保存单位逻辑 ★★★
            string rawUnit = _inputUnit.Inner.Text; // 【关键】不使用 Trim()，保留用户输入的空格
            
            // 获取当前模式下的默认单位，用于对比
            // 保存时同样需要对比 Settings 上下文下的默认值 (因为输入框显示的是 Settings 格式)
            var unitCtx = _isTaskbarMode ? MetricUtils.UnitContext.SettingsTaskbar : MetricUtils.UnitContext.SettingsPanel;
            string defUnit = MetricUtils.GetUnitStr(Config.Key, 0, unitCtx);
            string? finalVal;

            if (string.IsNullOrEmpty(rawUnit)) 
            {
                // 情况1：用户清空输入框 -> 恢复默认 (Auto/null)
                finalVal = null;
            }
            else if (string.IsNullOrWhiteSpace(rawUnit))
            {
                // 情况2：用户只输入了空格 (" ") -> 强制不显示 ("")
                // 这里的逻辑必须保留：纯空格意味着用户想隐藏单位
                finalVal = "";
            }
            else
            {
                string val = rawUnit; // 已经是原始值了

                // 情况3：用户输入了内容
                // 【关键】使用 Ordinal 区分大小写。
                // 如果用户输入 "{u}/S"，不等于默认的 "{u}/s"，会作为自定义值保存。
                if (string.Equals(val, defUnit, StringComparison.Ordinal))
                    finalVal = null;
                else
                    finalVal = val;
            }

            if (_isTaskbarMode) Config.UnitTaskbar = finalVal;
            else Config.UnitPanel = finalVal;

            Config.VisibleInPanel = _chkPanel.Checked;
            Config.VisibleInTaskbar = _chkTaskbar.Checked;
        }
    }

    public class MonitorGroupHeader : Panel, IHeightForWidth
    {
        public string GroupKey { get; private set; }
        public LiteUnderlineInput InputAlias { get; private set; }
        private readonly LiteCheck _chkAll; // [需求2] 增加组开关
        private readonly LiteSortBtn _btnUp, _btnDown;
        internal LiteDragGrip Grip { get; }
        private string _title;
        private bool _editing;
        private float _reveal;

        public bool ShowEditors
        {
            set
            {
                _editing = value;
                InputAlias.Visible = value;
                _title = value ? GroupKey : UiText.StripLeadingSymbol(InputAlias.Inner.Text);
                Arrange();
                Invalidate();
            }
        }
        public event EventHandler? MoveUp;
        public event EventHandler? MoveDown;
        public event EventHandler<bool>? ToggleGroup; // [需求2] 组开关事件

        public int HeightForWidth(int width) => UIUtils.S(DS.RowHeight);

        public MonitorGroupHeader(string groupKey, string alias)
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.Opaque, true);
            this.GroupKey = groupKey;
            this.BackColor = UIColors.GroupHeader;
            _title = groupKey;

            string defGName = LanguageManager.GetOriginal("Groups." + groupKey);
            if (defGName.StartsWith("Groups.")) defGName = groupKey;

            InputAlias = new LiteUnderlineInput(string.IsNullOrEmpty(alias) ? defGName : alias, "", "", 100) { ClearButtonEnabled = true, Visible = false, AccessibleName = LanguageManager.T("Menu.name") + " · " + groupKey };
            InputAlias.SetBg(UIColors.GroupHeader);
            InputAlias.Inner.Font = UiFonts.Get(TextRole.BodyStrong);
            _title = UiText.StripLeadingSymbol(InputAlias.Inner.Text);

            // [需求2] 组开关
            _chkAll = new LiteCheck(true, "")
            {
                AccessibleName = defGName + " · " + LanguageManager.T("Menu.showHide"),
            };
            // 点击事件：触发 ToggleGroup
            _chkAll.Click += (s, e) => ToggleGroup?.Invoke(this, _chkAll.Checked);

            _btnUp = new LiteSortBtn("▲") { Reveal = 0 };
            _btnDown = new LiteSortBtn("▼") { Reveal = 0 };
            _btnUp.Click += (s, e) => MoveUp?.Invoke(this, EventArgs.Empty);
            _btnDown.Click += (s, e) => MoveDown?.Invoke(this, EventArgs.Empty);
            Grip = new LiteDragGrip();

            this.Controls.AddRange(new Control[] { InputAlias, _chkAll, _btnUp, _btnDown, Grip });
            foreach (Control child in Controls)
            {
                child.MouseEnter += (_, _) => SetHover(true);
                child.MouseLeave += (_, _) => CheckHover();
            }
            this.Height = UIUtils.S(DS.RowHeight);
        }

        private void SetHover(bool hovered) =>
            InterfaceMotion.Spring(this, "reveal", SpringSpec.Hover, _reveal, hovered ? 1 : 0, v =>
            {
                _reveal = v;
                _btnUp.Reveal = _btnDown.Reveal = v;
                _btnUp.Invalidate(); _btnDown.Invalidate();
            });

        private void CheckHover()
        {
            if (!IsHandleCreated || !ClientRectangle.Contains(PointToClient(Cursor.Position))) SetHover(false);
        }

        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); SetHover(true); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); CheckHover(); }

        private void Arrange()
        {
            int width = Width;
            if (width <= 0) return;
            void Place(Control c, int x) { int y = (Height - c.Height) / 2; if (c.Left != x || c.Top != y) c.Location = new Point(x, y); }
            Place(Grip, MonitorLayout.GripX(width));
            Place(_btnDown, MonitorLayout.DownX(width));
            Place(_btnUp, MonitorLayout.UpX(width));
            Place(_chkAll, MonitorLayout.SwitchX(width));
            if (_editing)
            {
                InputAlias.Width = MonitorLayout.NameWidth(width);
                Place(InputAlias, MonitorLayout.NameX);
            }
        }

        protected override void OnLayout(LayoutEventArgs e) => Arrange();

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.FillRectangle(Ink.B(UIColors.GroupHeader), e.ClipRectangle);
            int limit = _editing ? MonitorLayout.NameX - UIUtils.S(8) : MonitorLayout.SwitchX(Width) - UIUtils.S(12);
            Ink.Line(g, _title, UiFonts.Get(TextRole.Headline), new Rectangle(0, 0, Math.Max(0, limit), Height), _editing ? UIColors.TextSub : UIColors.TextMain);
            g.DrawLine(Ink.P(UIColors.Separator, 1), 0, Height - 1, Width, Height - 1);
        }
    }
}
