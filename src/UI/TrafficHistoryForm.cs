using System.Globalization;
using System.Reflection; // 用于给 DataGridView 开缓冲
using LiteMonitor.src.Core;
using LiteMonitor.src.UI.Controls;

namespace LiteMonitor
{
    public enum HistoryViewMode { Daily, Weekly, Monthly, Quarterly, Yearly }

    public class TrafficHistoryForm : Form
    {
        private readonly Settings _cfg;
        private readonly System.Windows.Forms.Timer _timer;

        private Label _lblSessionUp, _lblSessionDown;
        private Label _lblTodayUp, _lblTodayDown;
        private Label _lblListSummary;
        private Panel _dashboardPanel, _toolbarPanel, _footerPanel;
        private Label _dashboardSeparator, _viewLabel;
        private DataGridView _grid;
        private Dictionary<HistoryViewMode, Button> _viewButtons = new Dictionary<HistoryViewMode, Button>();

        private HistoryViewMode _currentMode = HistoryViewMode.Daily;

        private float _scale = 1.0f;
        private string _visualSkin;
        private string _visualLanguage;

        // 参考 SettingsForm，使用 WS_EX_COMPOSITED 消除首次打开闪烁
        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= 0x02000000; // WS_EX_COMPOSITED
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

        // === 配色 ===
        private Color C_Back = UIColors.MainBg;
        private Color C_Panel = UIColors.CardBg;
        private Color C_GridBack = UIColors.CardBg;
        private Color C_GridLine = UIColors.Border;
        private Color C_TextMain = UIColors.TextMain;
        private Color C_TextDim = UIColors.TextSub;
        private Color C_Header = UIColors.GroupHeader;
        private Color C_Accent = UIColors.Primary;

        private Color C_Up = UIColors.IsDark ? Color.FromArgb(110, 173, 244) : Color.FromArgb(34, 104, 180);
        private Color C_Down = UIColors.Primary;
        private Color C_BarBg = UIColors.Border;

        private Color C_Highlight = UIColors.TextWarn;

        public TrafficHistoryForm(Settings cfg)
        {
            _cfg = cfg;
            _visualSkin = cfg.Skin;
            _visualLanguage = LanguageManager.CurrentLang;
            AutoScaleMode = AutoScaleMode.None;
            // 【修改点 1】开启窗体自身的双缓冲（解决背景白屏）
            this.DoubleBuffered = true;

            using (Graphics g = this.CreateGraphics())
            {
                _scale = g.DpiX / 96.0f;
            }

            this.Text = "MellowMeter · " + LanguageManager.T("Menu.Traffic");
            this.Size = new Size(S(840), S(650));
            this.MinimumSize = new Size(S(840), S(650));
            this.StartPosition = FormStartPosition.CenterScreen;
            this.BackColor = C_Back;
            this.ForeColor = C_TextMain;
            this.Font = new Font("Segoe UI", 9F);

            // 【修改点 2】在 InitializeUI 前后加上挂起和恢复布局
            // 这会让系统知道：“我还没摆好控件，先别画，等我摆完了再一次性画出来”
            this.SuspendLayout(); 
            InitializeUI();
            this.ResumeLayout(true); // 参数 true 也就是立即执行布局逻辑

            SwitchView(HistoryViewMode.Daily);
            UpdateRealtimeStats();

            _timer = new System.Windows.Forms.Timer { Interval = 1000 };
            _timer.Tick += (_, __) => UpdateRealtimeStats();
            _timer.Start();
        }

        private string T(string zh, string en) => (_cfg.Language ?? "").StartsWith("zh", StringComparison.OrdinalIgnoreCase) ? zh : en;

        public void RefreshTheme()
        {
            if (IsDisposed || (_visualSkin == _cfg.Skin && _visualLanguage == LanguageManager.CurrentLang)) return;
            C_Back = UIColors.MainBg;
            C_Panel = C_GridBack = UIColors.CardBg;
            C_GridLine = C_BarBg = UIColors.Border;
            C_TextMain = UIColors.TextMain;
            C_TextDim = UIColors.TextSub;
            C_Header = UIColors.GroupHeader;
            C_Accent = C_Down = UIColors.Primary;
            C_Up = UIColors.IsDark ? Color.FromArgb(110, 173, 244) : Color.FromArgb(34, 104, 180);
            C_Highlight = UIColors.TextWarn;
            SuspendLayout();
            try
            {
                Text = "MellowMeter · " + LanguageManager.T("Menu.Traffic");
                BackColor = C_Back;
                ForeColor = C_TextMain;
                _dashboardPanel.BackColor = _footerPanel.BackColor = C_Panel;
                _toolbarPanel.BackColor = C_Back;
                _dashboardSeparator.BackColor = C_GridLine;
                _lblSessionUp.ForeColor = _lblTodayUp.ForeColor = C_Up;
                _lblSessionDown.ForeColor = _lblTodayDown.ForeColor = C_Down;
                _lblListSummary.ForeColor = C_TextMain;
                _viewLabel.Text = T("统计维度", "View");
                _viewLabel.ForeColor = C_TextDim;
                foreach (Control control in _dashboardPanel.Controls)
                    if (control.Tag is ValueTuple<string, string, string> caption)
                    {
                        control.Text = T(caption.Item2, caption.Item3);
                        control.ForeColor = caption.Item1 switch { "up" => C_Up, "down" => C_Down, _ => C_TextDim };
                    }
                _grid.BackgroundColor = C_GridBack;
                _grid.GridColor = C_GridLine;
                _grid.ColumnHeadersDefaultCellStyle.BackColor = C_Header;
                _grid.ColumnHeadersDefaultCellStyle.ForeColor = C_TextMain;
                _grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = C_Header;
                _grid.ColumnHeadersDefaultCellStyle.SelectionForeColor = C_TextMain;
                _grid.DefaultCellStyle.BackColor = C_GridBack;
                _grid.DefaultCellStyle.ForeColor = C_TextMain;
                _grid.DefaultCellStyle.SelectionBackColor = UIColors.NavSelected;
                _grid.DefaultCellStyle.SelectionForeColor = C_TextMain;
                _grid.Columns["Up"].DefaultCellStyle.ForeColor = C_Up;
                _grid.Columns["Down"].DefaultCellStyle.ForeColor = C_Down;
                _grid.Columns["Date"].HeaderText = T("时间段", "Period");
                _grid.Columns["Up"].HeaderText = T("上传", "Upload");
                _grid.Columns["Down"].HeaderText = T("下载", "Download");
                _grid.Columns["Total"].HeaderText = T("总计", "Total");
                _grid.Columns["Chart"].HeaderText = T("流量占比", "Traffic share");
                long sumUp = 0, sumDown = 0;
                foreach (DataGridViewRow row in _grid.Rows)
                {
                    if (!row.DefaultCellStyle.ForeColor.IsEmpty) row.DefaultCellStyle.ForeColor = C_Highlight;
                    if (!row.Cells["Up"].Style.ForeColor.IsEmpty) row.Cells["Up"].Style.ForeColor = C_Up;
                    if (!row.Cells["Down"].Style.ForeColor.IsEmpty) row.Cells["Down"].Style.ForeColor = C_Down;
                    if (row.Tag is long[] values) { sumUp += values[2]; sumDown += values[3]; }
                }
                _lblListSummary.Text = _grid.Rows.Count == 0 ? T("暂无历史数据", "No history yet") :
                    $"{T("当前视图总计", "View total")}:  ↑ {MetricUtils.FormatDataSize(sumUp)}    ↓ {MetricUtils.FormatDataSize(sumDown)}    (Σ {MetricUtils.FormatDataSize(sumUp + sumDown)})";
                foreach (var pair in _viewButtons)
                {
                    pair.Value.Text = pair.Key switch {
                        HistoryViewMode.Daily => T("日", "Day"), HistoryViewMode.Weekly => T("周", "Week"),
                        HistoryViewMode.Monthly => T("月", "Month"), HistoryViewMode.Quarterly => T("季", "Quarter"),
                        _ => T("年", "Year") };
                    pair.Value.BackColor = pair.Key == _currentMode ? C_Accent : C_Back;
                    pair.Value.ForeColor = pair.Key == _currentMode ? UIColors.OnPrimary : C_TextDim;
                    pair.Value.FlatAppearance.MouseOverBackColor = UIColors.NavHover;
                    pair.Value.FlatAppearance.MouseDownBackColor = UIColors.NavSelected;
                }
                _visualSkin = _cfg.Skin;
                _visualLanguage = LanguageManager.CurrentLang;
            }
            finally { ResumeLayout(true); Invalidate(true); }
        }

        private int S(int pixel) => (int)(pixel * _scale);

        private void InitializeUI()
        {
            // === 1. 顶部仪表盘 ===
            var pnlDash = new Panel
            {
                Dock = DockStyle.Top,
                Height = S(120),
                BackColor = C_Panel,
            };
            _dashboardPanel = pnlDash;

            CreateDirectDash(pnlDash, S(50), "本次运行", "Current session", out _lblSessionUp, out _lblSessionDown);

            var split = new Label
            {
                AutoSize = false,
                Size = new Size(1, S(80)),
                Location = new Point(S(400), S(25)),
                BackColor = UIColors.Border,
                Anchor = AnchorStyles.Top | AnchorStyles.Left
            };
            _dashboardSeparator = split;
            pnlDash.Controls.Add(split);

            CreateDirectDash(pnlDash, S(450), "今日汇总", "Today", out _lblTodayUp, out _lblTodayDown);

            this.Controls.Add(pnlDash);

            // === 2. 视图切换栏 ===
            var pnlTool = new Panel { Dock = DockStyle.Top, Height = S(55), BackColor = C_Back };
            _toolbarPanel = pnlTool;
            _viewLabel = new Label
            {
                Text = T("统计维度", "View"),
                Tag = ("统计维度", "View"),
                ForeColor = C_TextDim,
                Location = new Point(S(20), S(18)),
                AutoSize = true,
                Font = new Font("Segoe UI", 9)
            };
            pnlTool.Controls.Add(_viewLabel);

            int btnX = S(140);
            _viewButtons.Add(HistoryViewMode.Daily, CreateBtn(pnlTool, T("日", "Day"), ref btnX));
            _viewButtons.Add(HistoryViewMode.Weekly, CreateBtn(pnlTool, T("周", "Week"), ref btnX));
            _viewButtons.Add(HistoryViewMode.Monthly, CreateBtn(pnlTool, T("月", "Month"), ref btnX));
            _viewButtons.Add(HistoryViewMode.Quarterly, CreateBtn(pnlTool, T("季", "Quarter"), ref btnX));
            _viewButtons.Add(HistoryViewMode.Yearly, CreateBtn(pnlTool, T("年", "Year"), ref btnX));
            this.Controls.Add(pnlTool);

            // === 3. 底部汇总栏 ===
            var pnlBottom = new Panel { Dock = DockStyle.Bottom, Height = S(45), BackColor = C_Panel };
            _footerPanel = pnlBottom;
            _lblListSummary = new Label
            {
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleRight,
                ForeColor = C_TextMain,
                Font = new Font("Consolas", 11, FontStyle.Bold),
                Padding = new Padding(0, 0, S(25), 0),
                Text = T("正在加载…", "Loading…")
            };
            pnlBottom.Controls.Add(_lblListSummary);
            this.Controls.Add(pnlBottom);

            // === 4. 列表 ===
            _grid = new DataGridView
            {
                Dock = DockStyle.Fill,
                BackgroundColor = C_GridBack,
                BorderStyle = BorderStyle.None,
                EnableHeadersVisualStyles = false,
                GridColor = C_GridLine,
                RowHeadersVisible = false,
                AllowUserToAddRows = false,
                AllowUserToResizeRows = false,
                ReadOnly = true,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                ColumnHeadersHeight = S(45),
                ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
                AutoGenerateColumns = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill
            };

            _grid.ColumnHeadersDefaultCellStyle.BackColor = C_Header;
            _grid.ColumnHeadersDefaultCellStyle.ForeColor = C_TextMain;
            _grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = C_Header;
            _grid.ColumnHeadersDefaultCellStyle.SelectionForeColor = C_TextMain;
            _grid.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI", 10, FontStyle.Regular);
            _grid.ColumnHeadersDefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            _grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single;

            _grid.DefaultCellStyle.BackColor = C_GridBack;
            _grid.DefaultCellStyle.ForeColor = C_TextMain;
            _grid.DefaultCellStyle.SelectionBackColor = UIColors.NavSelected;
            _grid.DefaultCellStyle.SelectionForeColor = C_TextMain;
            _grid.DefaultCellStyle.Font = new Font("Consolas", 11);
            _grid.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
            _grid.RowTemplate.Height = S(36);

            // 添加列，并显式指定 SortMode 为 Automatic
            // Tag结构: [0]=Total, [1]=Max, [2]=Up, [3]=Down
            var colDate = new DataGridViewTextBoxColumn { Name = "Date", HeaderText = T("时间段", "Period"), FillWeight = 90, SortMode = DataGridViewColumnSortMode.Automatic };
            colDate.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleLeft;
            _grid.Columns.Add(colDate);

            _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Up", HeaderText = T("上传", "Upload"), FillWeight = 80, SortMode = DataGridViewColumnSortMode.Automatic });
            _grid.Columns["Up"].DefaultCellStyle.ForeColor = C_Up;

            _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Down", HeaderText = T("下载", "Download"), FillWeight = 80, SortMode = DataGridViewColumnSortMode.Automatic });
            _grid.Columns["Down"].DefaultCellStyle.ForeColor = C_Down;

            _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Total", HeaderText = T("总计", "Total"), FillWeight = 80, SortMode = DataGridViewColumnSortMode.Automatic });

            _grid.Columns.Add(new DataGridViewImageColumn { Name = "Chart", HeaderText = T("流量占比", "Traffic share"), FillWeight = 180, SortMode = DataGridViewColumnSortMode.Automatic });

            // 绘图事件
            _grid.CellPainting += Grid_CellPainting;

            // ★★★ 核心修复：自定义排序逻辑 ★★★
            _grid.SortCompare += Grid_SortCompare;

            this.Controls.Add(_grid);
            _grid.BringToFront();
        }

        // ★★★ 新增：自定义排序方法 ★★★
        private void Grid_SortCompare(object sender, DataGridViewSortCompareEventArgs e)
        {
            // 1. 获取两行的 Tag 数据 (long数组)
            // Tag: [0]=Total, [1]=Max, [2]=Up, [3]=Down
            var tag1 = _grid.Rows[e.RowIndex1].Tag as long[];
            var tag2 = _grid.Rows[e.RowIndex2].Tag as long[];

            if (tag1 == null || tag2 == null)
            {
                e.Handled = false;
                return;
            }

            long val1 = 0, val2 = 0;

            // 2. 根据点击的列名，决定比较哪个数值
            switch (e.Column.Name)
            {
                case "Up":
                    val1 = tag1[2]; val2 = tag2[2];
                    break;
                case "Down":
                    val1 = tag1[3]; val2 = tag2[3];
                    break;
                case "Total":
                case "Chart": // 点击图表列也按总流量排序
                    val1 = tag1[0]; val2 = tag2[0];
                    break;
                default:
                    // 对于 "Date" 列，直接使用默认的字符串排序即可 (yyyy-MM-dd 本身就是可排序的)
                    // 如果您希望周报/季报也完美排序，建议保持默认字符序，通常也是对的
                    e.Handled = false;
                    return;
            }

            // 3. 执行数字比较
            e.SortResult = val1.CompareTo(val2);
            e.Handled = true; // 告诉系统：我排完了，你别管了
        }

        private void CreateDirectDash(Panel p, int startX, string titleZh, string titleEn, out Label lblUp, out Label lblDown)
        {
            p.Controls.Add(new Label { Text = T(titleZh, titleEn), Tag = ("title", titleZh, titleEn), ForeColor = C_TextDim, Location = new Point(startX, S(15)), AutoSize = true, Font = new Font("Segoe UI", 9) });
            lblUp = new Label { Text = "0 KB", ForeColor = C_Up, Location = new Point(startX, S(45)), AutoSize = true, Font = new Font("Consolas", 20, FontStyle.Bold) };
            p.Controls.Add(lblUp);
            p.Controls.Add(new Label { Text = T("↑ 上传", "↑ Upload"), Tag = ("up", "↑ 上传", "↑ Upload"), ForeColor = C_Up, Location = new Point(startX + S(2), S(85)), AutoSize = true, Font = new Font("Segoe UI", 8) });

            int downX = startX + S(165);
            lblDown = new Label { Text = "0 KB", ForeColor = C_Down, Location = new Point(downX, S(45)), AutoSize = true, Font = new Font("Consolas", 20, FontStyle.Bold) };
            p.Controls.Add(lblDown);
            p.Controls.Add(new Label { Text = T("↓ 下载", "↓ Download"), Tag = ("down", "↓ 下载", "↓ Download"), ForeColor = C_Down, Location = new Point(downX + S(2), S(85)), AutoSize = true, Font = new Font("Segoe UI", 8) });
        }

        private Button CreateBtn(Panel p, string txt, ref int x)
        {
            var btn = new LiteButton(txt)
            {
                Text = txt,
                Location = new Point(x, S(12)),
                Size = new Size(S(100), S(30)),
                FlatStyle = FlatStyle.Flat,
                BackColor = C_Back,
                ForeColor = C_TextDim,
                Cursor = Cursors.Hand,
                Font = new Font("Segoe UI", 9)
            };
            btn.FlatAppearance.BorderSize = 0;
            btn.Click += (s, e) => SwitchView(_viewButtons.FirstOrDefault(kv => kv.Value == s).Key);
            p.Controls.Add(btn);
            x += S(105);
            return btn;
        }

        private void UpdateRealtimeStats()
        {
            _lblSessionUp.Text = MetricUtils.FormatDataSize(_cfg.SessionUploadBytes);
            _lblSessionDown.Text = MetricUtils.FormatDataSize(_cfg.SessionDownloadBytes);
            var today = TrafficLogger.GetTodayStats();
            _lblTodayUp.Text = MetricUtils.FormatDataSize(today.up);
            _lblTodayDown.Text = MetricUtils.FormatDataSize(today.down);
        }

        private void SwitchView(HistoryViewMode mode)
        {
            _currentMode = mode;
            foreach (var kv in _viewButtons)
            {
                bool active = kv.Key == mode;
                kv.Value.BackColor = active ? C_Accent : C_Back;
                kv.Value.ForeColor = active ? UIColors.OnPrimary : C_TextDim;
            }
            LoadGridData(mode);
        }

        private void LoadGridData(HistoryViewMode mode)
        {
            _grid.Rows.Clear();
            var raw = TrafficLogger.Data.History;
            if (raw == null || raw.Count == 0)
            {
                _lblListSummary.Text = T("暂无历史数据", "No history yet");
                return;
            }

            var list = AggregateData(raw, mode);
            long maxTotal = list.Any() ? list.Max(x => x.Total) : 1;
            if (maxTotal == 0) maxTotal = 1;

            double avg = list.Count > 0 ? list.Average(x => x.Total) : 0;
            double threshold = Math.Max(avg * 2.0, 1024.0 * 1024 * 1024);

            long sumUp = 0, sumDown = 0;
            foreach (var item in list)
            {
                int idx = _grid.Rows.Add();
                var row = _grid.Rows[idx];

                bool isOutlier = item.Total > threshold;
                string dateText = item.DateLabel;

                // 保留🔥
                // if (isOutlier) dateText = "🔥 " + dateText; // 根据您之前的截图，这里似乎去掉了火，只留颜色？
                // 如果您想保留火，请取消上面的注释。
                // 现在的代码只做高亮颜色处理。

                row.Cells["Date"].Value = dateText;
                row.Cells["Up"].Value = MetricUtils.FormatDataSize(item.Upload);
                row.Cells["Down"].Value = MetricUtils.FormatDataSize(item.Download);



                // ★★★ 修复：将🔥加回 Total 列 ★★★
                string totalText = MetricUtils.FormatDataSize(item.Total);  
                row.Cells["Total"].Value = totalText;

                if (isOutlier)
                {
                    row.DefaultCellStyle.ForeColor = C_Highlight;
                    row.Cells["Up"].Style.ForeColor = C_Up;
                    row.Cells["Down"].Style.ForeColor = C_Down;
                }

                // 存入数值供排序和绘图使用
                row.Tag = new long[] { item.Total, maxTotal, item.Upload, item.Download };

                sumUp += item.Upload;
                sumDown += item.Download;
            }

            _grid.ClearSelection();
            _lblListSummary.Text = $"{T("当前视图总计", "View total")}:  ↑ {MetricUtils.FormatDataSize(sumUp)}    ↓ {MetricUtils.FormatDataSize(sumDown)}    (Σ {MetricUtils.FormatDataSize(sumUp + sumDown)})";
        }

        private void Grid_CellPainting(object sender, DataGridViewCellPaintingEventArgs e)
        {
            if (e.ColumnIndex == 4 && e.RowIndex >= 0)
            {
                e.Paint(e.CellBounds, DataGridViewPaintParts.All & ~DataGridViewPaintParts.ContentForeground);
                var row = _grid.Rows[e.RowIndex];
                if (row.Tag is long[] stats)
                {
                    long total = stats[0], max = stats[1], up = stats[2];
                    if (max > 0)
                    {
                        Rectangle r = e.CellBounds;
                        r.Inflate(-10, -10);
                        using (var brushBg = new SolidBrush(C_BarBg)) e.Graphics.FillRectangle(brushBg, r);

                        double sqrtTotal = Math.Sqrt(total);
                        double sqrtMax = Math.Sqrt(max);
                        float pctTotal = (float)(sqrtTotal / sqrtMax);

                        int wTotal = (int)(r.Width * pctTotal);
                        if (wTotal < 4 && pctTotal > 0) wTotal = 4;

                        if (wTotal > 0)
                        {
                            float pctUp = total > 0 ? (float)up / total : 0;
                            int wUp = (int)(wTotal * pctUp);
                            int wDown = wTotal - wUp;
                            if (wUp > 0) { using var upBrush = new SolidBrush(C_Up); e.Graphics.FillRectangle(upBrush, new Rectangle(r.X, r.Y, wUp, r.Height)); }
                            if (wDown > 0) { using var downBrush = new SolidBrush(C_Down); e.Graphics.FillRectangle(downBrush, new Rectangle(r.X + wUp, r.Y, wDown, r.Height)); }
                        }
                    }
                }
                e.Handled = true;
            }
        }

        private List<DisplayItem> AggregateData(Dictionary<string, DailyRecord> raw, HistoryViewMode mode)
        {
            var source = raw.Select(x => { DateTime.TryParse(x.Key, out var d); return new { D = d, V = x.Value }; }).Where(x => x.D != DateTime.MinValue).ToList();
            return source.GroupBy(x => GetKey(x.D, mode))
                .Select(g => new DisplayItem
                {
                    DateLabel = g.Key,
                    SortDate = g.Max(x => x.D),
                    Upload = g.Sum(x => x.V.Upload),
                    Download = g.Sum(x => x.V.Download)
                })
                .OrderByDescending(x => x.SortDate).ToList();
        }

        private string GetKey(DateTime d, HistoryViewMode m) => m switch
        {
            HistoryViewMode.Daily => d.ToString("yyyy-MM-dd"),
            HistoryViewMode.Weekly => $"{d.Year}-W{CultureInfo.CurrentCulture.Calendar.GetWeekOfYear(d, CalendarWeekRule.FirstDay, DayOfWeek.Monday):00}",
            HistoryViewMode.Monthly => d.ToString("yyyy-MM"),
            HistoryViewMode.Quarterly => $"{d.Year}-Q{(d.Month - 1) / 3 + 1}",
            HistoryViewMode.Yearly => d.ToString("yyyy"),
            _ => d.ToString("yyyy-MM-dd")
        };

        private class DisplayItem { public string DateLabel; public DateTime SortDate; public long Upload; public long Download; public long Total => Upload + Download; }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _timer?.Dispose();
            base.Dispose(disposing);
        }

        // 确保窗口在显示时居中
        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            // 强制窗口居中
            this.StartPosition = FormStartPosition.CenterScreen;
            this.CenterToScreen();
        }
    }
}
