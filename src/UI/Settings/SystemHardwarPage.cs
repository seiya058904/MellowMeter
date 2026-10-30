using System;
using System.Drawing;
using System.Windows.Forms;
using System.Linq;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using LiteMonitor.src.Core;
using LiteMonitor.src.SystemServices;
using LiteMonitor.src.UI.Controls;

namespace LiteMonitor.src.UI.SettingsPage
{
    public class SystemHardwarPage : SettingsPageBase
    {
        private Panel _container;
        private readonly List<Control> _advancedGroups = new();
        private bool _loading;
        
        // ★★★ 修复：类型更正为 LiteComboBox ★★★
        private LiteComboBox _cbDisk, _cbNet, _cbGpu, _cbMobo;
        private Label _sourceStatus = null!;

        public SystemHardwarPage()
        {
            this.BackColor = UIColors.MainBg;
            this.Dock = DockStyle.Fill;
            this.Padding = new Padding(0);
            _container = new BufferedPanel { Dock = DockStyle.Fill, AutoScroll = true, Padding = UIUtils.S(new Padding(18)) };
            this.Controls.Add(_container);

            InitializeUI();
        }

        private void InitializeUI()
        {
            CreateSourceCard();

            CreateSystemCard();
        }

        public override void OnShow()
        {
            base.OnShow();
            if (Config == null) return;
            foreach (var group in _advancedGroups) group.Parent.Visible = Config.AdvancedSensorsEnabled;
            PopulateAsyncData();
        }

        // 将 PopulateAsyncData 改为“批量处理”模式
        private async void PopulateAsyncData()
        {
            if (_loading || IsDisposed) return;
            _loading = true;
            _sourceStatus.Text = LanguageManager.T("Menu.SourcesLoading");
            try 
            {
                string strAuto = LanguageManager.T("Menu.Auto");

                // 1. 并行等待所有数据返回 (使用 HardwareScanner)
                var taskDisks = Config.AdvancedSensorsEnabled ? Task.Run(() => HardwareMonitor.ListAllDisks()) : Task.FromResult(new List<string>());
                var taskNets  = Task.Run(() => HardwareMonitor.ListAllNetworks());
                var taskGpus  = Config.AdvancedSensorsEnabled ? Task.Run(() => HardwareMonitor.ListAllGpuOptions()) : Task.FromResult(new List<HardwareScanner.GpuOption>());
                var taskMobo  = Config.AdvancedSensorsEnabled ? Task.Run(() => HardwareScanner.ListAllMoboTemps(HardwareMonitor.Instance.ComputerInstance, HardwareMonitor.Instance.SyncLock)) : Task.FromResult(new List<string>());

                await Task.WhenAll(taskDisks, taskNets, taskGpus, taskMobo);
                if (IsDisposed || Disposing) return;

                // 2. ★★★ 锁定全局布局 (防止每填一个框就重绘一次) ★★★
                this.SuspendLayout();
                
            
                // 定义一个同步填充的 Action，避免重复代码
                void FillSync(LiteComboBox combo, List<string> data, string currentVal)
                {
                    if (combo == null || combo.Inner.Items.Count > 2) return;

                    var fullList = new List<string>(data);
                    fullList.Insert(0, strAuto);
                    if (!string.IsNullOrEmpty(currentVal) && !fullList.Contains(currentVal)) fullList.Add(currentVal);

                    combo.Inner.BeginUpdate(); // 锁定 ComboBox 自身
                    combo.Inner.Items.Clear();
                    foreach (var item in fullList) combo.Inner.Items.Add(item);

                    if (!string.IsNullOrEmpty(currentVal) && fullList.Contains(currentVal))
                        combo.Inner.SelectedItem = currentVal;
                    else
                        combo.Inner.SelectedIndex = 0;
                    
                    combo.Inner.EndUpdate(); // 解锁 ComboBox
                }

                void FillGpuSync(LiteComboBox combo, List<HardwareScanner.GpuOption> data, string currentVal)
                {
                    if (combo == null || combo.Inner.Items.Count > 2) return;

                    HardwareScanner.GpuOption? ResolveCurrent()
                    {
                        if (string.IsNullOrWhiteSpace(currentVal)) return null;

                        var byValue = data.FirstOrDefault(x =>
                            string.Equals(x.Value, currentVal, StringComparison.OrdinalIgnoreCase));
                        if (byValue != null) return byValue;

                        var byName = data.Where(x =>
                            string.Equals(x.Name, currentVal, StringComparison.OrdinalIgnoreCase)).ToList();
                        return byName.Count == 1 ? byName[0] : null;
                    }

                    combo.Inner.BeginUpdate();
                    combo.Inner.Items.Clear();
                    combo.AddItem(strAuto, "");
                    foreach (var item in data) combo.AddItem(item.Label, item.Value);

                    var selected = ResolveCurrent();
                    if (selected == null && !string.IsNullOrEmpty(currentVal)) combo.AddItem(currentVal, currentVal);
                    combo.SelectValue(selected?.Value ?? currentVal ?? "");
                    combo.Inner.EndUpdate();
                }

                // 3. 瞬间填入所有数据 (因为布局被挂起，用户看不见中间过程)
                FillSync(_cbDisk, taskDisks.Result, Config.PreferredDisk);
                FillSync(_cbNet, taskNets.Result, Config.PreferredNetwork);
                FillGpuSync(_cbGpu, taskGpus.Result, Config.PreferredGpu);
                FillSync(_cbMobo, taskMobo.Result, Config.PreferredMoboTemp);
                _sourceStatus.Text = LanguageManager.T(taskNets.Result.Count + taskDisks.Result.Count + taskGpus.Result.Count == 0 ? "Menu.SourcesEmpty" : "Menu.SourcesReady");
                
                // Fan 的数据是复用的
            }
            catch (Exception ex)
            {
                // 记录日志，或者只是简单地忽略（硬件读取失败不应该影响用户进入设置）
                System.Diagnostics.Debug.WriteLine("Device enumeration failed: " + ex.Message);
                if (!IsDisposed) _sourceStatus.Text = LanguageManager.T("Menu.SourcesFailed");
            }
            finally
            {
                // 4. 恢复布局 (此时所有控件已就绪，一次性渲染)
                _loading = false;
                if (!IsDisposed) this.ResumeLayout(true);
            }
        }

        private void CreateSourceCard()
        {
            var group = new LiteSettingsGroup(LanguageManager.T("Menu.HardwareSettings"));
            string strAuto = LanguageManager.T("Menu.Auto");
            
            // 内存/显存显示模式 (从主界面设置移来)
            string[] memOptions = { LanguageManager.T("Menu.Percent"), LanguageManager.T("Menu.UsedSize") };
            group.AddComboIndex(this, "Menu.MemoryDisplayMode", memOptions,
                () => Config?.MemoryDisplayMode ?? 0,
                idx => { if (Config != null) Config.MemoryDisplayMode = idx; }
            );

            int[] rates = { 500, 1000, 1500, 2000, 3000 };
            group.AddCombo(this, "Menu.Refresh", rates.Select(r => r + " ms"),
                () => (Config?.RefreshMs ?? 1000) + " ms",
                v => { if (Config != null) Config.RefreshMs = MetricUtils.ParseInt(v); }
            );

            _cbNet = group.AddCombo(this, "Menu.NetworkSource", new List<string> { strAuto },
                () => Config?.PreferredNetwork ?? strAuto,
                v => { if (Config != null) Config.PreferredNetwork = v == strAuto ? "" : v; });
            _sourceStatus = new Label { AutoSize = true, MaximumSize = UIUtils.S(new Size(390, 0)),
                ForeColor = UIColors.TextSub, Padding = UIUtils.S(new Padding(0, 6, 0, 6)), AccessibleRole = AccessibleRole.StaticText };
            group.AddFullItem(_sourceStatus);
            group.AddToggle(this, "Menu.HistoryEnabled", () => Config?.HistoryEnabled ?? true,
                v => { if (Config != null) Config.HistoryEnabled = v; });
            AddGroupToPage(group);

            var advanced = new LiteSettingsGroup(LanguageManager.T("Menu.AdvancedSensors")) { Collapsible = true, Collapsed = true };
            advanced.AddToggle(this, "Menu.AdvancedSensors", () => Config?.AdvancedSensorsEnabled ?? false,
                v => {
                    if (Config == null) return;
                    Config.AdvancedSensorsEnabled = v;
                    foreach (var control in _advancedGroups) control.Parent.Visible = v;
                });
            advanced.AddHint(LanguageManager.T("Menu.AdvancedSensorsTip"));
            AddGroupToPage(advanced);

            group = new LiteSettingsGroup(LanguageManager.T("Menu.HardwareSettings"));
            _advancedGroups.Add(group);
            _cbDisk = group.AddCombo(this, "Menu.DiskSource", new List<string> { strAuto },
                () => Config?.PreferredDisk ?? strAuto,
                v => { if (Config != null) Config.PreferredDisk = v == strAuto ? "" : v; });
            _cbGpu = (LiteComboBox)group.AddComboPair(this, LanguageManager.T("Menu.GpuSource"),
                new[] { new { Label = strAuto, Value = "" } },
                () => Config?.PreferredGpu ?? "",
                v => { if (Config != null) Config.PreferredGpu = v ?? ""; });

            _cbMobo = (LiteComboBox)group.AddCombo(this, "Items.MOBO.Temp", new List<string> { strAuto },
                () => Config?.PreferredMoboTemp ?? strAuto, v => { if (Config != null) Config.PreferredMoboTemp = (v == strAuto ? "" : v); });

            AddGroupToPage(group);
        }



        private void CreateSystemCard()
        {
            var group = new LiteSettingsGroup(LanguageManager.T("Menu.SystemSettings"));
            
            var langs = new List<string>();
            string langDir = Path.Combine(AppContext.BaseDirectory, "resources/lang");
            if (Directory.Exists(langDir))
                langs.AddRange(Directory.EnumerateFiles(langDir, "*.json").Select(f => Path.GetFileNameWithoutExtension(f).ToUpper()));
            
            group.AddCombo(this, "Menu.Language", langs,
                () => string.IsNullOrEmpty(Config?.Language) ? LanguageManager.CurrentLang.ToUpper() : Config.Language.ToUpper(),
                v => { if(Config!=null) Config.Language = v.ToLower(); }
            );

            group.AddToggle(this, "Menu.AutoStart", () => Config?.AutoStart ?? false, v => { if(Config!=null) Config.AutoStart = v; });

            var chkTray = group.AddToggle(this, "Menu.HideTrayIcon", 
                () => Config?.HideTrayIcon ?? false, 
                v => { if(Config!=null) Config.HideTrayIcon = v; });
            chkTray.CheckedChanged += (s, e) => { if(Config!=null) EnsureSafeVisibility(null, chkTray, null); };

            AddGroupToPage(group);
        }

        private void AddGroupToPage(LiteSettingsGroup group)
        {
            var wrapper = new Panel { Dock = DockStyle.Top, AutoSize = true, Padding = UIUtils.S(new Padding(0, 0, 0, 12)) };
            wrapper.Controls.Add(group);
            _container.Controls.Add(wrapper);
            _container.Controls.SetChildIndex(wrapper, 0);
        }
    }
}
