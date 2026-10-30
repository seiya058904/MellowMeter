using System;
using System.Collections.Generic;
using System.Linq;
using LibreHardwareMonitor.Hardware;
using LiteMonitor.src.Core;

namespace LiteMonitor.src.SystemServices
{
    /// <summary>
    /// 专用服务：负责自动识别和映射 CPU 风扇
    /// </summary>
    public class FanMapper
    {
        // 将原 SensorMap 中的 ScanAndMapFans 及相关辅助方法移到这里
        
        public void ScanAndMapFans(Computer computer, Settings cfg, Dictionary<string, ISensor> targetMap)
        {
            ISensor? cpuFan = null;
            // 1. 搜集全系统所有“活着”的风扇 (采用黑名单机制)
            var activeFans = new List<(IHardware Hw, ISensor S, float Rpm)>();
            
            void CollectFans(IHardware hw)
            {
                // ★★★ 黑名单：坚决排除以下类型的风扇 ★★★
                bool isExcluded = hw.HardwareType == HardwareType.GpuNvidia ||
                                  hw.HardwareType == HardwareType.GpuAmd ||
                                  hw.HardwareType == HardwareType.GpuIntel ||
                                  hw.HardwareType == HardwareType.Cpu ||
                                  hw.HardwareType == HardwareType.Storage ||
                                  hw.HardwareType == HardwareType.Memory ||
                                  hw.HardwareType == HardwareType.Network;

                if (!isExcluded)
                {
                    hw.Update();
                    foreach (var s in hw.Sensors)
                    {
                        if (s.SensorType == SensorType.Fan && s.Value.HasValue)
                        {
                            // [底噪过滤] 门槛 200 RPM
                            if (s.Value.Value > 200)
                            {
                                activeFans.Add((hw, s, s.Value.Value));
                            }
                        }
                    }
                }
                foreach (var sub in hw.SubHardware) CollectFans(sub);
            }
            foreach (var hw in computer.Hardware) CollectFans(hw);

            // -----------------------------------------------------------
            if (cpuFan == null && activeFans.Count > 0)
            {
                var coolerFan = activeFans.FirstOrDefault(x =>
                    IsCoolerHardware(x.Hw) && !SensorMap.Has(x.S.Name, "Pump"));
                
                if (coolerFan.S != null) cpuFan = coolerFan.S;
                else
                {
                    var namedCpu = activeFans.FirstOrDefault(x => SensorMap.Has(x.S.Name, "CPU") && !SensorMap.Has(x.S.Name, "Pump"));
                    if (namedCpu.S != null) cpuFan = namedCpu.S;
                    else
                    {
                        var first = activeFans.FirstOrDefault(x => !SensorMap.Has(x.S.Name, "Pump") && !SensorMap.Has(x.S.Name, "Water"));
                        if (first.S != null) cpuFan = first.S;
                    }
                }
            }

            if (cpuFan != null) targetMap["CPU.Fan"] = cpuFan;
        }

        private bool IsCoolerHardware(IHardware h) 
        {
            if (h.HardwareType == HardwareType.Cooler) return true;
            string n = h.Name;
            return SensorMap.Has(n, "Kraken") || SensorMap.Has(n, "Corsair") || SensorMap.Has(n, "Liquid") || SensorMap.Has(n, "AIO") || SensorMap.Has(n, "Cooler");
        }
    }
}
