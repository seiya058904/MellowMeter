using LiteMonitor.src.Core;
using LiteMonitor.src.UI.Controls;
using System.Drawing.Text;

namespace LiteMonitor
{
    /// <summary>
    /// 任务栏渲染器（仅负责绘制，不再负责布局）
    /// </summary>
    public static class TaskbarRenderer
    {
        //private static readonly Settings _settings = Settings.Load();
        
        // 字体缓存 - 直接初始化，避免每次渲染都创建字体
        private static Font? _cachedFont = null;

        // Share the floating theme's hues, adapted once for either Windows taskbar surface.
        private static Color _labelLight, _labelDark, _mutedLight, _mutedDark;
        private static readonly Color[] _statesLight = new Color[3];
        private static readonly Color[] _statesDark = new Color[3];

        // ★★★ [新增] 自定义颜色缓存 ★★★
        private static bool _useCustom = false;
        private static Color _cLabel, _cSafe, _cWarn, _cCrit;

        public static void ClearCache()
        {
            _cachedFont?.Dispose();
            _cachedFont = null;
        }

        // ★★★ [新增] 极简的核心：手动刷新缓存 ★★★
        // 在 UIController 初始化或配置变更时调用它
        public static void ReloadStyle(Settings cfg)
        {     
            var s = cfg.GetStyle();

            // ★★★ 修复：先释放旧字体资源，防止 GDI 句柄泄漏 ★★★
            ClearCache();

            // 无论开关怎么变，这里拿到的永远是正确参数
            // This renderer owns its font; shared UIUtils fonts may be invalidated on theme changes.
            _cachedFont = new Font(s.Font, s.Size, s.Bold ? FontStyle.Bold : FontStyle.Regular);

            var theme = ThemeManager.Current.Color;
            Color lightSurface = Color.FromArgb(210, 210, 210);
            Color darkSurface = Color.FromArgb(40, 40, 40);
            static Color Adapt(string source, Color surface) =>
                UIColors.EnsureContrast(ThemeManager.ParseColor(source), surface);
            _labelLight = Adapt(theme.TextPrimary, lightSurface);
            _labelDark = Adapt(theme.TextPrimary, darkSurface);
            _mutedLight = Adapt(theme.TextGroup, lightSurface);
            _mutedDark = Adapt(theme.TextGroup, darkSurface);
            string[] stateColors = { theme.ValueSafe, theme.ValueWarn, theme.ValueCrit };
            for (int i = 0; i < stateColors.Length; i++)
            {
                _statesLight[i] = Adapt(stateColors[i], lightSurface);
                _statesDark[i] = Adapt(stateColors[i], darkSurface);
            }

            // 颜色依然允许自定义
            _useCustom = cfg.TaskbarCustomStyle;
            if (_useCustom)
            {
                try {
                    _cLabel = ColorTranslator.FromHtml(cfg.TaskbarColorLabel);
                    _cSafe = ColorTranslator.FromHtml(cfg.TaskbarColorSafe);
                    _cWarn = ColorTranslator.FromHtml(cfg.TaskbarColorWarn);
                    _cCrit = ColorTranslator.FromHtml(cfg.TaskbarColorCrit);
                } catch {
                    // 容错：如果解析失败，回退到默认
                    _useCustom = false; 
                }
            }
        }

        public static void Render(Graphics g, List<Column> cols, bool light) // <--- 新的
        {
            // [防空策略] 万一还没人调用 ReloadStyle，就自己兜底初始化一次
            if (_cachedFont == null)
            {
                // 兜底：读磁盘配置（仅第一次）
                ReloadStyle(Settings.Load());
            }
            
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
            g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            // 使用传入的 light 参数，避免每次都查询系统主题瓠
            //bool light = IsSystemLight();

            foreach (var col in cols)
            {
                // ★★★ [新增]：如果只有 Top 没有 Bottom，强制使用全高绘制（居中）
                if (col.Top != null && col.Bottom == null && col.Bounds != Rectangle.Empty)
                {
                    DrawItem(g, col.Top, col.Bounds, light);
                    continue; // 处理完这个特殊情况直接跳过本次循环
                }

                if (col.BoundsTop != Rectangle.Empty && col.Top != null)
                    DrawItem(g, col.Top, col.BoundsTop, light);

                if (col.BoundsBottom != Rectangle.Empty && col.Bottom != null)
                    DrawItem(g, col.Bottom, col.BoundsBottom, light);
            }
        }

        private static void DrawItem(Graphics g, MetricItem item, Rectangle rc, bool light)
        {
            // ★★★ 优化：直接使用缓存的 ShortLabel，避免每帧生成 Key 和查询字典 ★★★
            string label = item.ShortLabel;

            // ★★★ 修复：如果 ShortLabel 被显式设为空格或空，则视为隐藏标签 ★★★
            // 适用于 IP/Dashboard 文本，直接绘制 Value (左对齐)
            bool hideLabel = (string.IsNullOrEmpty(label) || label == " ");

            // 如果不是隐藏，且为空，则回退到 Label 或 Key
            if (!hideLabel)
            {
                if (string.IsNullOrEmpty(label)) label = item.Label;
                if (string.IsNullOrEmpty(label)) label = item.Key;
            }

            string value = item.GetFormattedText(true);

            // 直接使用缓存的字体，不再 new Font
            Font font = _cachedFont!;

            Color labelColor, valueColor;

            // ★★★ [修改] 颜色选择逻辑 ★★★
            if (_useCustom)
            {
                // 自定义模式：忽略系统明暗，强制使用自定义色
                labelColor = _cLabel;
                valueColor = GetCustomStateColor(item.CachedColorState);
            }
            else
            {
                labelColor = light ? _labelLight : _labelDark;
                valueColor = GetStateColor(item.CachedColorState, light);
            }

            if (item.TextValue == null && (!item.Value.HasValue || !float.IsFinite(item.Value.Value)))
                valueColor = _useCustom ? _cLabel : light ? _mutedLight : _mutedDark;

            // ★★★ 修复：如果开启了隐藏标签 (如 IP/Dashboard)，则仅绘制 Value (左对齐) ★★★
            if (hideLabel)
            {
                TextRenderer.DrawText(
                    g, value, font, rc, valueColor,
                    UIRenderer.TextFlags | TextFormatFlags.Left
                );
                return;
            }

            UIRenderer.DrawMetricPair(g, label, value, font, font, rc, labelColor, valueColor);
        }
        // [新增] 辅助：根据状态快速获取颜色 (替代原来的 PickColor)
        private static Color GetStateColor(int state, bool light)
        {
            return (light ? _statesLight : _statesDark)[state is 1 or 2 ? state : 0];
        }

        // [新增] 辅助：自定义模式
        private static Color GetCustomStateColor(int state)
        {
            if (state == 2) return _cCrit;
            if (state == 1) return _cWarn;
            return _cSafe;
        }
    }
}
