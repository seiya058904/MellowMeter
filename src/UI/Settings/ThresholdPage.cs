using System;
using System.Drawing;
using System.Windows.Forms;
using LiteMonitor.src.Core;
using LiteMonitor.src.UI.Controls;

namespace LiteMonitor.src.UI.SettingsPage
{
    public class ThresholdPage : SettingsPageBase
    {
        private readonly StackPanel _container;
        private bool _isLoaded = false;
        private bool _advanced;

        public ThresholdPage()
        {
            _container = CreateSurface(LanguageManager.T("Menu.Thresholds"));
        }

        public override void OnShow()
        {
            base.OnShow();
            if (Config == null || (_isLoaded && _advanced == Config.AdvancedSensorsEnabled)) return;
            _advanced = Config.AdvancedSensorsEnabled;

            _container.SuspendLayout();
            RemoveGroups();
            _refreshActions.Clear();

            // === 1. Alert Temp ===
            if (_advanced)
            {
                var grpAlert = new LiteSettingsGroup(LanguageManager.T("Menu.AlertTemp"));

                grpAlert.AddToggle(this, "Menu.AlertTemp",
                    () => Config.AlertTempEnabled,
                    v => Config.AlertTempEnabled = v);

                grpAlert.AddInt(this, "Menu.AlertThreshold", "°C",
                    () => Config.AlertTempThreshold,
                    v => Config.AlertTempThreshold = v,
                    width: 80, color: UIColors.TextCritOnField, min: 0, max: 150);

                AddGroupToPage(grpAlert);
            }

            // === 2. Hardware ===
            var grpHardware = new LiteSettingsGroup(LanguageManager.T("Menu.GeneralHardware"));
            grpHardware.AddHint(LanguageManager.T("Menu.ThresholdsTips"));

            grpHardware.AddThreshold(this, LanguageManager.T("Menu.HardwareLoad"), "%", Config.Thresholds.Load);
            if (_advanced) grpHardware.AddThreshold(this, LanguageManager.T("Menu.HardwareTemp"), "°C", Config.Thresholds.Temp);

            AddGroupToPage(grpHardware);

            // === 3. Network & Disk ===
            var grpNet = new LiteSettingsGroup(LanguageManager.T("Menu.NetworkDiskSpeed"));

            if (_advanced) grpNet.AddThreshold(this, LanguageManager.T("Menu.DiskIOSpeed"), "MB/s", Config.Thresholds.DiskIOMB);
            grpNet.AddThreshold(this, LanguageManager.T("Menu.UploadSpeed"), "MB/s", Config.Thresholds.NetUpMB);
            grpNet.AddThreshold(this, LanguageManager.T("Menu.DownloadSpeed"), "MB/s", Config.Thresholds.NetDownMB);

            AddGroupToPage(grpNet);

            // === 4. Data Usage ===
            var grpData = new LiteSettingsGroup(LanguageManager.T("Menu.DailyTraffic"));

            grpData.AddThreshold(this, LanguageManager.T("Items.DATA.DayUp"), "MB", Config.Thresholds.DataUpMB);
            grpData.AddThreshold(this, LanguageManager.T("Items.DATA.DayDown"), "MB", Config.Thresholds.DataDownMB);

            AddGroupToPage(grpData);

            _container.ResumeLayout();
            _isLoaded = true;
        }

        // Keep the large title; rebuild only the groups when sensor availability changes.
        private void RemoveGroups()
        {
            foreach (var group in _container.Controls.OfType<LiteSettingsGroup>().ToArray())
            {
                _container.Controls.Remove(group);
                group.Dispose();
            }
        }
    }
}
