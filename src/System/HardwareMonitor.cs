using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Runtime.InteropServices; // ★★★ 新增：引用用于内存修剪的库
using System.Reflection; // ★★★ 新增：用于反射关闭历史记录
using LibreHardwareMonitor.Hardware;
using LiteMonitor.src.Core;
using System.Linq;
using System.Threading;
using System.Diagnostics;

namespace LiteMonitor.src.SystemServices
{
    public sealed class HardwareMonitor : IDisposable
    {
        #region Singleton & Events
        public static HardwareMonitor? Instance { get; private set; }
        public event Action? OnValuesUpdated;
        #endregion

        #region Private Fields
        private readonly Settings _cfg;
        private readonly Computer _computer;
        private readonly object _lock = new object();

        // 拆分出的子服务
        private readonly SensorMap _sensorMap;
        private readonly NetworkManager _networkManager;
        private readonly DiskManager _diskManager;
        private readonly Lazy<FpsCounter> _fpsCounter;
        private readonly BasicSystemSampler _basicSampler;
        private readonly bool _advancedEnabled;
        private volatile bool _disposed;
        private volatile bool _advancedSampleValid;
        private int _samplingGeneration;
        private long _lastSlowScan;
        private long _lastDiskScan;
        private long _lastHistorySave = Stopwatch.GetTimestamp();
        private readonly DriverInstaller _driverInstaller;
        private readonly HardwareValueProvider _valueProvider;

        // 性能计数器管理器
        private readonly PerformanceCounterManager _perfCounterManager;

        private readonly Dictionary<string, float> _lastValidMap = new();
        
        // 状态标记 (防止并发重载)
        private volatile bool _isReloading = false;
        private volatile bool _isOpening = false;
        private bool _gpuMapRepairAttempted = false;

        // 计时器相关
        private long _tickCounter = 0;
        private double _secondAccumulator = 0;
        private long _secondsCounter = 0;
        private DateTime _lastTrafficTime = DateTime.Now;
        #endregion

        #region Public Properties
        // [新增] 允许 UI 层访问原始硬件树（用于硬件信息面板）
        public IComputer ComputerInstance => _computer;
        public object SyncLock => _lock;
        #endregion

        #region Constructor
        public HardwareMonitor(Settings cfg)
        {
            _cfg = cfg;
            Instance = this;

            _advancedEnabled = cfg.AdvancedSensorsEnabled;
            _basicSampler = new BasicSystemSampler(cfg);
            // Constructing a tree is cheap; Open() is strictly opt-in.
            _computer = new Computer();
            ConfigureAdvancedHardware();

            // ★★★ [新增] 1. 初始化计数器管理器 (必须在 ValueProvider 之前) ★★★
            _perfCounterManager = new PerformanceCounterManager();

            // 2. 初始化服务
            _sensorMap = new SensorMap();
            _networkManager = new NetworkManager(_perfCounterManager);
            _diskManager = new DiskManager();
            _driverInstaller = new DriverInstaller(cfg, ReloadComputerSafe, ReleaseComputerForDriverInstall);
            _fpsCounter = new Lazy<FpsCounter>(() => new FpsCounter(_driverInstaller));

            // ★★★ [修改] 2. 将 Manager 注入给 ValueProvider ★★★
            _valueProvider = new HardwareValueProvider(
                _computer, 
                cfg, 
                _sensorMap, 
                _networkManager, 
                _diskManager, 
                _fpsCounter, 
                _perfCounterManager, 
                _lock, 
                _lastValidMap
            );

            // 3. 异步启动 (唯一优化：不卡UI)
            if (_advancedEnabled) InitializeAsync();
        }
        #endregion

        #region Public Methods

        public float? Get(string key)
        {
            if (_disposed) return null;
            if (key is "CPU.Load" or "MEM.Load" or "NET.Up" or "NET.Down")
                return _basicSampler.Get(key);
            if (key == "DATA.DayUp") return TrafficLogger.GetTodayStats().up;
            if (key == "DATA.DayDown") return TrafficLogger.GetTodayStats().down;
            if (!_advancedEnabled || _isOpening || _isReloading || !_advancedSampleValid) return null;
            return _valueProvider.GetValue(key);
        }

        public string GetNetworkIP() => _basicSampler.GetIP();

        public void ResetSamplingBaseline()
        {
            Interlocked.Increment(ref _samplingGeneration);
            _basicSampler.Reset();
            _advancedSampleValid = false;
        }

        // ★★★ 新增：允许主程序手动触发驱动检查 (用于解决启动弹窗冲突) ★★★
        public Task SmartCheckDriver() => Task.CompletedTask; // Never install or prompt from monitoring.

        // ★★★ [新增] 动态刷新硬件配置 (热切换) ★★★
        public void RefreshHardwareConfig()
        {
            if (!_advancedEnabled || _disposed || _isOpening || _isReloading) return;
            _isReloading = true;
            Task.Run(() =>
            {
                try { ReloadComputerSafe(); }
                finally { _isReloading = false; }
            });
        }

        public void UpdateAll()
        {
            if (_disposed) return;
            int generation = Volatile.Read(ref _samplingGeneration);
            _basicSampler.Update();
            long now = Stopwatch.GetTimestamp();
            if (_cfg.HistoryEnabled && Stopwatch.GetElapsedTime(_lastHistorySave, now).TotalSeconds >= 60)
            {
                _lastHistorySave = now;
                TrafficLogger.Save();
            }
            if (!_advancedEnabled || _isOpening || _isReloading)
            {
                OnValuesUpdated?.Invoke();
                return;
            }

            // [Fix #290] 标记是否需要触发重载（因硬件变更或故障）
            bool needsReload = false;

            try
            {
                // 1. 更新计时器
                double timeDelta = UpdateTiming();

                // 2. 计算更新需求
                var requirements = CheckUpdateRequirements();

                _valueProvider.OnUpdateTickStarted();

                lock (_lock)
                {
                    // 每十分钟兜底重建后，同步取值缓存，避免继续使用旧传感器对象。
                    if (_sensorMap.EnsureFresh(_computer, _cfg))
                    {
                        _valueProvider.PreCacheAllSensors(_sensorMap);
                        DisableSensorHistory();
                    }

                    // 3秒一次 (慢速扫描)
                    bool isSlowScanTick = _lastSlowScan == 0 || Stopwatch.GetElapsedTime(_lastSlowScan, now).TotalSeconds >= 3;
                    if (isSlowScanTick) _lastSlowScan = now;
                    // 10秒一次 (磁盘后台扫描)
                    bool needDiskBgScan = _lastDiskScan == 0 || Stopwatch.GetElapsedTime(_lastDiskScan, now).TotalSeconds >= 10;
                    if (needDiskBgScan) _lastDiskScan = now;
                    bool repairGpuMapAfterLoop = false;

                    foreach (var hw in _computer.Hardware)
                    {
                        if (hw.HardwareType == HardwareType.Cpu && requirements.NeedCpu) { hw.Update(); continue; }
                        
                        // [Fix #290] 显卡防闪退保护：双显卡切换时 Update 可能抛出异常
                        if (IsGpu(hw) && requirements.NeedGpu) 
                        { 
                            if (!requirements.ForceAll && !ShouldUpdateGpuHardware(hw)) continue;

                            try
                            {
                                hw.Update();
                                if (ShouldRepairGpuSensorMap()) repairGpuMapAfterLoop = true;
                            }
                            catch (Exception ex)
                            {
                                System.Diagnostics.Debug.WriteLine($"[GPU Error] Update failed: {ex.Message}");
                                needsReload = true;
                            }
                            continue; 
                        }

                        if (hw.HardwareType == HardwareType.Memory && requirements.NeedMem) { hw.Update(); continue; }
                        if (hw.HardwareType == HardwareType.Battery && requirements.NeedBat) { hw.Update(); continue; }

                        if (hw.HardwareType == HardwareType.Network && requirements.NeedNet)
                        {
                            // ★★★ 修复：使用正确的 timeDelta，而不是 requirements.TimeDelta (它是0) ★★★
                            _networkManager.ProcessUpdate(hw, _cfg, timeDelta, isSlowScanTick);
                            continue;
                        }
                        if (hw.HardwareType == HardwareType.Storage && requirements.NeedDisk)
                        {
                            _diskManager.ProcessUpdate(hw, _cfg, isSlowScanTick, needDiskBgScan);
                            continue;
                        }
                        
                        // 递归更新主板 (Motherboard / SuperIO)
                        if (IsMoboOrCooler(hw) && requirements.NeedMobo)
                        {
                             // ★★★ [优化] 降低更新频率：主板传感器每 3 秒更新一次，减少 I/O 阻塞 ★★★
                             // [Fix] 无论是否 ForceAll (WebServer)，只要是 SuperIO 这种慢速设备，
                             // 都必须强制跟随慢速扫描周期 (isSlowScanTick)，禁止高频更新。
                             if (isSlowScanTick)
                             {
                                 UpdateWithSubHardware(hw);
                             }
                             continue;
                        }
                    }

                    if (repairGpuMapAfterLoop)
                    {
                        RepairGpuSensorMap();
                    }
                }
                
                // 任务错峰执行 (调用 SystemOptimizer)
                // Runtime GC owns memory management; no forced collections or working-set trimming.
                
                _advancedSampleValid = !needsReload && generation == Volatile.Read(ref _samplingGeneration);
                OnValuesUpdated?.Invoke();
            }
            catch { _advancedSampleValid = false; }

            // [Fix #290] 硬件故障自动恢复
            // 如果检测到 GPU 丢失或驱动崩溃，触发安全重载以刷新硬件列表
            if (needsReload && !_isReloading)
            {
                _isReloading = true;
                System.Diagnostics.Debug.WriteLine("[HardwareMonitor] Hardware change detected, triggering reload...");
                
                // 异步延迟重载，给硬件切换留出缓冲时间
                Task.Run(async () => 
                {
                    try
                    {
                        await Task.Delay(2000); // 等待 2秒让系统硬件列表稳定
                        if (!_disposed) ReloadComputerSafe();
                    }
                    finally
                    {
                        _isReloading = false;
                    }
                });
            }
        }

        public void CleanMemory(Action<int>? onProgress = null) => SystemOptimizer.CleanMemory(onProgress);

        public void Dispose()
        {
            _disposed = true;
            // ★★★ 核心修复：加锁！防止与正在运行的 UpdateAll() 冲突 ★★★
            lock (_lock)
            {
                try 
                {
                    // 将 Close 操作放入锁内，确保此时 UpdateAll 不在运行
                    _computer.Close(); 
                }
                catch (Exception ex)
                {
                    // 吞掉关闭时的异常，防止弹窗报错困扰用户
                    System.Diagnostics.Debug.WriteLine($"Dispose Error: {ex.Message}");
                }
            }

            _valueProvider.Dispose();
            _perfCounterManager.Dispose(); // ★★★ [新增] 释放计数器资源 ★★★
            if (_fpsCounter.IsValueCreated) _fpsCounter.Value.Dispose();
            _networkManager.ClearCache();
            _diskManager.ClearCache(); // 漏掉的，补上
        }
        #endregion

        #region Private Helpers (Logic)

        private void InitializeAsync()
        {
            _isOpening = true;
            Task.Run(() =>
            {
                try
                {
                    lock (_lock)
                    {
                        if (_disposed) return;
                        _computer.Open();
                        WarmUpMotherboardSensors();
                        WarmUpBatterySensors();
                        _sensorMap.Rebuild(_computer, _cfg);
                        _valueProvider.PreCacheAllSensors(_sensorMap);
                        DisableSensorHistory();
                    }
                }
                catch (Exception ex) { Debug.WriteLine($"Sensor initialization unavailable: {ex.Message}"); }
                finally { _isOpening = false; }
            });
        }

        private void ConfigureAdvancedHardware()
        {
            bool enabled = _advancedEnabled;
            bool all = _cfg.WebServerEnabled;
            bool fans = ShouldEnableController();
            _computer.IsCpuEnabled = enabled && (all || fans || _cfg.IsAnyEnabled("CPU.Temp") ||
                _cfg.IsAnyEnabled("CPU.Clock") || _cfg.IsAnyEnabled("CPU.Power"));
            _computer.IsGpuEnabled = enabled && (all || _cfg.IsAnyEnabled("GPU"));
            _computer.IsMemoryEnabled = false;
            _computer.IsNetworkEnabled = false;
            _computer.IsStorageEnabled = enabled && (all || _cfg.IsAnyEnabled("DISK"));
            _computer.IsMotherboardEnabled = enabled && (all || fans || _cfg.IsAnyEnabled("MOBO"));
            _computer.IsControllerEnabled = enabled && fans;
            _computer.IsBatteryEnabled = enabled && (all || _cfg.IsAnyEnabled("BAT"));
            _computer.IsPsuEnabled = false;
        }

        // ★★★ [新增] 动态判断是否需要开启控制器 ★★★
        private bool ShouldEnableController()
        {
            // 检查是否开启了任何需要读取外部控制器的监控项。
            // 主板温度走 Motherboard/SuperIO 硬件树，不应该因此打开 Controller 扫描。
            if (_cfg.IsAnyEnabled("CPU.Fan")) return true;
            if (_cfg.IsAnyEnabled("CPU.Pump")) return true;
            if (_cfg.IsAnyEnabled("CASE.Fan")) return true;
            return false;
        }

        private void WarmUpMotherboardSensors()
        {
            // 主板硬件树本来就是常开。启动时预热一次，保证主板温度即使暂未显示，
            // 也能在 SensorMap.Rebuild 阶段完成有效映射，后续打开显示项可直接读缓存。
            foreach (var hw in _computer.Hardware)
            {
                if (IsMotherboardSensorHardware(hw))
                {
                    try { UpdateWithSubHardware(hw); }
                    catch { }
                }
            }
        }

        private void WarmUpBatterySensors()
        {
            // 电池传感器在新版硬件库中可能需要先 Update 才会稳定暴露数值。
            // 只预热 Battery，避免恢复启动时全硬件 Update 带来的多显卡/磁盘卡顿。
            foreach (var hw in _computer.Hardware)
            {
                if (hw.HardwareType == HardwareType.Battery)
                {
                    try { hw.Update(); }
                    catch { }
                }
            }
        }

        private double UpdateTiming()
        {
            // 1. 统一心跳计数 (假设 UpdateAll 约 1秒调用一次)
            _tickCounter++;
            
            // 2. 计算精确时间差 (仅用于网速计算)
            DateTime now = DateTime.Now;
            double timeDelta = (now - _lastTrafficTime).TotalSeconds;
            _lastTrafficTime = now;
            if (timeDelta > 5.0) timeDelta = 0; // 防止休眠唤醒后的数据突刺

            // ★★★ [智能处理] 刷新率自适应逻辑 ★★★
            // 无论 UpdateAll 是 300ms 调一次还是 2s 调一次，
            // 我们都将时间累加，直到凑满 1秒，才增加 _secondsCounter。
            // 这样后续的 % 60, % 600 逻辑就是基于"真实时间"，而非"调用次数"。
            _secondAccumulator += timeDelta;
            while (_secondAccumulator >= 1.0)
            {
                _secondAccumulator -= 1.0;
                _secondsCounter++;
            }

            return timeDelta;
        }

        private (bool ForceAll, bool NeedCpu, bool NeedGpu, bool NeedMem, bool NeedNet, bool NeedDisk, bool NeedBat, bool NeedMobo, double TimeDelta) CheckUpdateRequirements()
        {
            // === [优化开始] 精细化判断更新需求 ===
        
            // 1. 获取计数器状态
            bool useCounter = _cfg.UseWinPerCounters && _perfCounterManager.IsInitialized;
            
            // ★★★ [优化] 全量更新判断 ★★★
            // 如果开启了 WebServer，则需要强制更新所有硬件，因为网页端可能会查看主界面未开启的项目
            bool forceAll = _cfg.WebServerEnabled;

            // 2. CPU: 总是需要 (因为 LHM 要读温度)
            bool needCpu = forceAll || _cfg.IsAnyEnabled("CPU");
            
            // 3. 显卡: 总是需要
            bool needGpu = forceAll || _cfg.IsAnyEnabled("GPU");
            
            // 4. ★★★ [优化 1] 内存: 如果走了计数器，就不需要 LHM 轮询了 ★★★
            bool needMem = forceAll || (_cfg.IsAnyEnabled("MEM") && !useCounter);
            
            // 5. 网络: 保持不变
            bool needNet = forceAll || _cfg.IsAnyEnabled("NET") || _cfg.IsAnyEnabled("DATA");
            
            // 7. 电池: 只有在开启时才更新
            bool needBat = forceAll || _cfg.IsAnyEnabled("BAT");

            // 6. ★★★ [优化 2] 磁盘: 智能判断 ★★★
            // 只有当 (没开计数器 OR 指定了特定盘 OR 需要看温度) 时，才需要 LHM 介入
            bool needDiskTemp = _cfg.IsAnyEnabled("DISK.Temp");
            bool hasSpecificDisk = !string.IsNullOrEmpty(_cfg.PreferredDisk);
            bool needDiskSpeed = _cfg.IsAnyEnabled("DISK") && (!useCounter || hasSpecificDisk);
            bool needDisk = forceAll || needDiskSpeed || needDiskTemp;

            // 判断主板更新需求
            bool needMobo = forceAll ||
                _cfg.IsAnyEnabled("MOBO") ||
                _cfg.IsAnyEnabled("CPU.Fan") ||
                _cfg.IsAnyEnabled("CPU.Pump") ||
                _cfg.IsAnyEnabled("CASE.Fan");
            
            return (forceAll, needCpu, needGpu, needMem, needNet, needDisk, needBat, needMobo, 0);
        }

        private void ReloadComputerSafe()
        {
            try
            {
                lock (_lock)
                {
                    if (_disposed || !_advancedEnabled) return;
                    // 1. 清理业务缓存
                    _networkManager.ClearCache();
                    _diskManager.ClearCache();
                    _sensorMap.Clear();
                    _gpuMapRepairAttempted = false;
                    
                    _valueProvider.ClearCache();
                    
                    // 清理扫描器缓存
                    HardwareScanner.ClearCache();

                    // 2. 清理字符串池
                    UIUtils.ClearStringPool();

                    // 3. 关闭旧硬件服务
                    if (_computer != null)
                    {
                        _computer.Accept(new HardwareVisitor(h => { }));
                        _computer.Close();
                        // ★★★ 核心修复：手动清空硬件列表 ★★★
                        // LHM 的 Close() 不会清空列表，必须手动 Clear，否则再次 Open 会追加重复硬件
                        _computer.Hardware.Clear();
                    }
                    
                    ConfigureAdvancedHardware();
                    _computer.Open();
                    WarmUpMotherboardSensors();
                    WarmUpBatterySensors();

                    DisableSensorHistory();

                    // 建图包含显卡更新，必须与正常采集、硬件关闭保持在同一把锁内。
                    _sensorMap.Rebuild(_computer, _cfg);
                    _valueProvider.PreCacheAllSensors(_sensorMap);
                }

                // 4. 优化 T1：重置后再次修剪内存
                // Do not force GC or page out the working set after hardware refresh.
            }
            catch { }
        }

        private void ReleaseComputerForDriverInstall()
        {
            try
            {
                lock (_lock)
                {
                    _networkManager.ClearCache();
                    _diskManager.ClearCache();
                    _sensorMap.Clear();
                    _gpuMapRepairAttempted = false;
                    _valueProvider.ClearCache();
                    HardwareScanner.ClearCache();

                    try
                    {
                        _computer.Accept(new HardwareVisitor(h => { }));
                        _computer.Close();
                        _computer.Hardware.Clear();
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[DriverInstaller] 释放硬件监控失败: {ex.Message}");
                    }
                }
            }
            catch { }
        }

        // =========================================================
        // ★★★ 核心修复：禁用所有传感器的历史记录 ★★★
        // 这将阻止 LibreHardwareMonitor 在内存中保留 24 小时的数据缓存
        // =========================================================
        private void DisableSensorHistory()
        {
            try { _computer.Accept(new DisableHistoryVisitor()); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[MemoryFix] Failed: {ex.Message}"); }
        }

        // 递归更新子硬件，确保 SuperIO 刷新
        private void UpdateWithSubHardware(IHardware hw)
        {
            hw.Update();
            foreach (var sub in hw.SubHardware) UpdateWithSubHardware(sub);
        }

        private static bool IsGpu(IHardware hw)
        {
            return hw.HardwareType == HardwareType.GpuNvidia || 
                   hw.HardwareType == HardwareType.GpuAmd || 
                   hw.HardwareType == HardwareType.GpuIntel;
        }

        private bool ShouldUpdateGpuHardware(IHardware hw)
        {
            var activeGpu = _sensorMap.CachedGpu;
            if (activeGpu == null) return true;

            // 用户手动选择首选显卡时，严格只刷新该显卡，避免改变既有行为。
            if (_sensorMap.HasExplicitGpuSelection)
            {
                return ReferenceEquals(hw, activeGpu);
            }

            // 自动模式下，除默认显卡外，也刷新实际承载 GPU.* 指标的硬件。
            return ReferenceEquals(hw, activeGpu) || _sensorMap.IsMappedGpuSensorHardware(hw);
        }

        private bool ShouldRepairGpuSensorMap()
        {
            if (_gpuMapRepairAttempted || !_cfg.IsAnyEnabled("GPU")) return false;

            if (IsEnabledGpuSensorMissing("GPU.Load")) return true;
            if (IsEnabledGpuSensorMissing("GPU.Temp")) return true;
            if (IsEnabledGpuSensorMissing("GPU.Fan")) return true;

            if (_cfg.IsAnyEnabled("GPU.VRAM") &&
                !HasMappedSensor("GPU.VRAM.Used") &&
                !HasMappedSensor("GPU.VRAM.Total") &&
                !HasMappedSensor("GPU.VRAM.Load"))
            {
                return true;
            }

            return false;
        }

        private bool IsEnabledGpuSensorMissing(string key)
        {
            return _cfg.IsAnyEnabled(key) && !HasMappedSensor(key);
        }

        private bool HasMappedSensor(string key)
        {
            return _sensorMap.TryGetSensor(key, out var sensor) && sensor != null;
        }

        private void RepairGpuSensorMap()
        {
            if (_gpuMapRepairAttempted) return;

            _gpuMapRepairAttempted = true;
            _sensorMap.Rebuild(_computer, _cfg);
            _valueProvider.PreCacheAllSensors(_sensorMap);
            DisableSensorHistory();
        }

        private static bool IsMoboOrCooler(IHardware hw)
        {
            return hw.HardwareType == HardwareType.Motherboard || 
                   hw.HardwareType == HardwareType.SuperIO || 
                   hw.HardwareType == HardwareType.Cooler;
        }

        private static bool IsMotherboardSensorHardware(IHardware hw)
        {
            return hw.HardwareType == HardwareType.Motherboard ||
                   hw.HardwareType == HardwareType.SuperIO;
        }
        #endregion

        #region Static UI Helpers (Delegated to HardwareScanner)
        public static string GenerateSmartName(ISensor sensor, IHardware hardware) => 
            HardwareScanner.GenerateSmartName(sensor, hardware, Instance!._computer);

        public static List<string> ListAllNetworks() => BasicSystemSampler.ListNetworks();

        public static List<string> ListAllDisks() => HardwareScanner.ListAllDisks(Instance!._computer);

        public static List<string> ListAllGpus()
        {
            lock (Instance!._lock)
                return HardwareScanner.ListAllGpus(Instance!._computer);
        }

        public static List<HardwareScanner.GpuOption> ListAllGpuOptions()
        {
            lock (Instance!._lock)
                return HardwareScanner.ListAllGpuOptions(Instance!._computer);
        }

        public static List<string> ListAllFans() => HardwareScanner.ListAllFans(Instance!._computer, Instance!._lock);

        public static List<string> ListAllMoboTemps() => HardwareScanner.ListAllMoboTemps(Instance!._computer, Instance!._lock);
        #endregion

        #region Inner Visitors
        // ★★★ 核心修复：禁用所有传感器的历史记录 ★★★
        /// <summary>
        /// 专用访问器：将所有 Sensor 的 ValuesTimeWindow 设为 0
        /// </summary>
        private class DisableHistoryVisitor : IVisitor
        {
            public void VisitComputer(IComputer computer) => computer.Traverse(this);
            public void VisitHardware(IHardware hardware)
            {
                foreach (var sub in hardware.SubHardware) sub.Accept(this);
                foreach (var sensor in hardware.Sensors) VisitSensor(sensor);
            }
            public void VisitSensor(ISensor sensor)
            {
                // ★ 关键：通过反射设置 ValuesTimeWindow = TimeSpan.Zero
                // 因为 ISensor 接口通常不暴露这个属性，它属于具体的 Sensor 类
                try
                {
                    var prop = sensor.GetType().GetProperty("ValuesTimeWindow");
                    if (prop != null && prop.CanWrite) prop.SetValue(sensor, TimeSpan.Zero);
                }
                catch { }
            }
            public void VisitParameter(IParameter parameter) { }
        }

        // 内部 Visitor 类，用于触发 LHM 清理逻辑
        private class HardwareVisitor : IVisitor
        {
            private readonly Action<IHardware> _action;
            public HardwareVisitor(Action<IHardware> action) { _action = action; }
            public void VisitComputer(IComputer computer) => computer.Traverse(this);
            public void VisitHardware(IHardware hardware)
            {
                _action(hardware);
                foreach (var sub in hardware.SubHardware) sub.Accept(this);
            }
            public void VisitSensor(ISensor sensor) { }
            public void VisitParameter(IParameter parameter) { }
        }
        #endregion
    }
}
