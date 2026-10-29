using LiteMonitor.src.Core;
using LiteMonitor.src.UI.Controls;
using System;
using System.Diagnostics;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using System.Net.Http;
using System.Threading.Tasks;

namespace LiteMonitor
{
    public class AboutForm : Form
    {
        public AboutForm()
        {
            // === 1. 语言判断与文案准备 ===
            bool isZh = LanguageManager.CurrentLang == "zh";

            string strTitle = LanguageManager.T("Menu.About");
            string strDesc = LanguageManager.T("Menu.UpstreamProject") + "\n© 2025 Diorser / LiteMonitor Project";
            string strClose = LanguageManager.T("Menu.OK");

            // === 基础外观 ===
            Text = "MellowMeter · " + strTitle;
            AutoScaleMode = AutoScaleMode.None;
            Font = new Font("Segoe UI", 9F);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;   // ✅ 居中在屏幕（不会被主窗体挡住）
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            TopMost = false;                                   // ✅ 确保显示在最前
            
            // ★★★ DPI 修复：计算缩放系数并应用 ★★★
            UIUtils.ScaleFactor = this.DeviceDpi / 96f;
            ClientSize = UIUtils.S(new Size(440, 250));

            var theme = ThemeManager.Current;
            BackColor = ThemeManager.ParseColor(theme.Color.Background);

            // === 标题 ===
            var lblTitle = new Label
            {
                Text = "MellowMeter",
                Font = new Font(theme.Font.Family, 14, FontStyle.Bold),
                ForeColor = ThemeManager.ParseColor(theme.Color.TextTitle),
                AutoSize = true,
                Location = new Point(UIUtils.S(32), UIUtils.S(28))
            };

            // === 简洁版本号 ===
            string version = Assembly.GetExecutingAssembly()
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
                .InformationalVersion ?? "1.0.0";
            // ✅ 自动清理 Git 哈希后缀（如 1.0+abc123 → 1.0）

            int plus = version.IndexOf('+');
            if (plus > 0) version = version[..plus];

            var lblVer = new Label
            {
                Text = $"Version {version}",
                ForeColor = ThemeManager.ParseColor(theme.Color.TextPrimary),
                Location = new Point(UIUtils.S(32), UIUtils.S(68)),
                AutoSize = true
            };

            // === 简介 ===
            var lblDesc = new Label
            {
                Text = strDesc,
                ForeColor = ThemeManager.ParseColor(theme.Color.TextPrimary),
                Location = new Point(UIUtils.S(32), UIUtils.S(98)),
                AutoSize = true
            };

            // === GitHub 链接 ===
            var githubLink = new LinkLabel
            {
                Text = isZh ? "LiteMonitor 上游源码 ↗" : "LiteMonitor upstream source ↗",
                LinkColor = UIColors.Primary,
                ActiveLinkColor = UIColors.Primary,
                VisitedLinkColor = UIColors.Primary,
                Location = new Point(UIUtils.S(32), UIUtils.S(155)),
                AutoSize = true
            };
            githubLink.LinkClicked += (_, __) =>
            {
                try
                {
                    Process.Start(new ProcessStartInfo("https://github.com/Diorser/LiteMonitor")
                    { UseShellExecute = true });
                }
                catch { }
            };

            // === 关闭按钮（扁平风格） ===
            var btnClose = new Button
            {
                Text = strClose,
                DialogResult = DialogResult.OK,
                Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
                Size = UIUtils.S(new Size(70, 30)),
                Location = new Point(UIUtils.S(340), UIUtils.S(200)),
                FlatStyle = FlatStyle.Flat,
                BackColor = ThemeManager.ParseColor(theme.Color.GroupBackground),
                ForeColor = ThemeManager.ParseColor(theme.Color.TextPrimary),
                Font = new Font(theme.Font.Family, 9.5f, FontStyle.Regular)
            };
            btnClose.FlatAppearance.BorderSize = 0;           // ✅ 移除白边框
            btnClose.FlatAppearance.MouseOverBackColor = ThemeManager.ParseColor(theme.Color.BarBackground);

            Controls.AddRange([lblTitle, lblVer, lblDesc, githubLink, btnClose]);
            AcceptButton = btnClose;
            CancelButton = btnClose;
        }
    }
}
