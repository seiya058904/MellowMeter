using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading;
using LiteMonitor.src.Core;

namespace LiteMonitor.src.SystemServices
{
    /// <summary>Driver-free Windows counters. Read once per tick; UI getters only read snapshots.</summary>
    internal sealed class BasicSystemSampler
    {
        private readonly object _sync = new();
        private readonly Settings _settings;
        private readonly Dictionary<string, (long Up, long Down)> _previousNetwork = new();
        private NetworkInterface[] _adapters = Array.Empty<NetworkInterface>();
        private long _lastDiscovery;
        private long _lastSample;
        private ulong _lastIdle, _lastKernel, _lastUser;
        private bool _hasCpuBaseline;
        private bool _historyEnabled;
        private int _resetRequested;
        private const double MaximumSampleGapSeconds = 120;
        private string _preference = "";
        private float? _cpu, _memory, _up, _down;
        private string _ip = "--";
        private static readonly string[] VirtualMarkers =
            { "virtual", "vmware", "hyper-v", "vbox", "loopback", "tunnel", "tap-", "tailscale", "zerotier" };

        public BasicSystemSampler(Settings settings)
        {
            _settings = settings;
            _historyEnabled = settings.HistoryEnabled;
        }

        public float? Get(string key)
        {
            lock (_sync)
            {
                if (Volatile.Read(ref _resetRequested) != 0) return null;
                return key switch { "CPU.Load" => _cpu, "MEM.Load" => _memory,
                    "NET.Up" => _up, "NET.Down" => _down, _ => null };
            }
        }

        public string GetIP() { lock (_sync) return Volatile.Read(ref _resetRequested) == 0 ? _ip : "--"; }

        public static List<string> ListNetworks()
        {
            try
            {
                return NetworkInterface.GetAllNetworkInterfaces()
                    .Where(n => n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                    .Select(n => n.Name).Distinct().OrderBy(n => n).ToList();
            }
            catch { return new List<string>(); }
        }

        // Power/session events run on the UI thread. Never wait for NIC discovery here.
        public void Reset() => Volatile.Write(ref _resetRequested, 1);

        private void ResetCore()
        {
            _lastSample = 0;
            _lastDiscovery = 0;
            _hasCpuBaseline = false;
            _previousNetwork.Clear();
            _cpu = _memory = _up = _down = null;
            _ip = "--";
        }

        public void Update()
        {
            lock (_sync)
            {
                if (Interlocked.Exchange(ref _resetRequested, 0) != 0) ResetCore();
                _up = _down = null;
                if (_historyEnabled != _settings.HistoryEnabled)
                {
                    _historyEnabled = _settings.HistoryEnabled;
                    // Do not assign traffic from an interval crossing a disabled-history boundary.
                    _previousNetwork.Clear();
                }
                long now = Stopwatch.GetTimestamp();
                double elapsed = _lastSample == 0 ? 0 : Stopwatch.GetElapsedTime(_lastSample, now).TotalSeconds;
                _lastSample = now;
                // A long suspension is a discontinuity, not an enormous rate sample.
                if (elapsed <= 0 || elapsed > MaximumSampleGapSeconds)
                {
                    _hasCpuBaseline = false;
                    _previousNetwork.Clear();
                    elapsed = 0;
                }
                ReadCpu();
                ReadMemory();
                if (_settings.HistoryEnabled || _settings.IsAnyEnabled("NET") || _settings.IsAnyEnabled("DATA"))
                    ReadNetwork(now, elapsed);
                else
                {
                    _previousNetwork.Clear();
                    _up = _down = null;
                    _ip = "--";
                }
            }
        }

        private void ReadCpu()
        {
            _cpu = null;
            if (!GetSystemTimes(out ulong idle, out ulong kernel, out ulong user))
            {
                _hasCpuBaseline = false;
                return;
            }
            if (_hasCpuBaseline && idle >= _lastIdle && kernel >= _lastKernel && user >= _lastUser)
                _cpu = CalculateCpuLoad(idle - _lastIdle, kernel - _lastKernel, user - _lastUser);
            _lastIdle = idle; _lastKernel = kernel; _lastUser = user;
            _hasCpuBaseline = true;
        }

        internal static float? CalculateCpuLoad(ulong idleDelta, ulong kernelDelta, ulong userDelta)
        {
            double total = (double)kernelDelta + userDelta;
            return total > 0 && idleDelta <= total ? (float)(100d * (total - idleDelta) / total) : null;
        }

        internal static bool TryNetworkDelta(long current, long previous, double elapsed, out long delta)
        {
            delta = 0;
            if (previous < 0 || current < previous || !double.IsFinite(elapsed) || elapsed <= 0 || elapsed > MaximumSampleGapSeconds)
                return false;
            delta = current - previous;
            return true;
        }

        private void ReadMemory()
        {
            var memory = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
            _memory = null;
            if (!GlobalMemoryStatusEx(ref memory) || memory.TotalPhysical == 0 || memory.AvailablePhysical > memory.TotalPhysical)
                return;
            _memory = (float)(100d * (memory.TotalPhysical - memory.AvailablePhysical) / memory.TotalPhysical);
            Settings.DetectedRamTotalGB = (float)(memory.TotalPhysical / 1073741824d);
        }

        private void ReadNetwork(long now, double elapsed)
        {
            _up = _down = null;
            string preference = _settings.PreferredNetwork ?? "";
            if (preference != _preference)
            {
                _preference = preference;
                _previousNetwork.Clear();
                _lastDiscovery = 0;
            }
            if (_lastDiscovery == 0 || Stopwatch.GetElapsedTime(_lastDiscovery, now).TotalSeconds >= 10)
            {
                try { _adapters = NetworkInterface.GetAllNetworkInterfaces(); }
                catch { _adapters = Array.Empty<NetworkInterface>(); }
                var ids = new HashSet<string>(_adapters.Select(n => n.Id));
                foreach (string id in _previousNetwork.Keys.Where(id => !ids.Contains(id)).ToArray())
                    _previousNetwork.Remove(id);
                _lastDiscovery = now;
            }

            long selectedUp = 0, selectedDown = 0, bestActivity = -1;
            NetworkInterface? selected = null;
            foreach (var adapter in _adapters)
            {
                bool explicitSelection = !string.IsNullOrWhiteSpace(preference);
                if (adapter.OperationalStatus != OperationalStatus.Up ||
                    adapter.NetworkInterfaceType == NetworkInterfaceType.Loopback ||
                    (explicitSelection ? !Matches(adapter, preference) : !IsAutomaticCandidate(adapter)))
                {
                    _previousNetwork.Remove(adapter.Id);
                    continue;
                }
                try
                {
                    var stats = adapter.GetIPStatistics();
                    bool previous = _previousNetwork.TryGetValue(adapter.Id, out var last);
                    _previousNetwork[adapter.Id] = (stats.BytesSent, stats.BytesReceived);
                    if (!previous || !TryNetworkDelta(stats.BytesSent, last.Up, elapsed, out long up) ||
                        !TryNetworkDelta(stats.BytesReceived, last.Down, elapsed, out long down))
                        continue;
                    // Auto means the busiest eligible adapter, matching the original monitor's display.
                    // Counting just that adapter avoids counting VPN + underlying NIC bytes twice.
                    long activity = up + down;
                    if (activity > bestActivity)
                    {
                        selected = adapter;
                        selectedUp = up; selectedDown = down; bestActivity = activity;
                    }
                }
                catch { _previousNetwork.Remove(adapter.Id); }
            }
            _ip = "--";
            if (selected == null) return;
            _up = (float)(selectedUp / elapsed);
            _down = (float)(selectedDown / elapsed);
            try
            {
                _ip = selected.GetIPProperties().UnicastAddresses
                    .FirstOrDefault(a => a.Address.AddressFamily == AddressFamily.InterNetwork)?.Address.ToString() ?? "--";
            }
            catch { }
            RecordTraffic(selectedUp, selectedDown);
        }

        internal void RecordTraffic(long up, long down)
        {
            if (up < 0 || down < 0 || (up == 0 && down == 0)) return;
            _settings.SessionUploadBytes += up;
            _settings.SessionDownloadBytes += down;
            if (_settings.HistoryEnabled && _historyEnabled && Volatile.Read(ref _resetRequested) == 0)
                TrafficLogger.AddTraffic(up, down);
        }

        private static bool Matches(NetworkInterface adapter, string preference) =>
            string.Equals(adapter.Name, preference, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(adapter.Description, preference, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(adapter.Id, preference, StringComparison.OrdinalIgnoreCase);

        private static bool IsAutomaticCandidate(NetworkInterface adapter)
        {
            if (adapter.NetworkInterfaceType == NetworkInterfaceType.Tunnel) return false;
            string name = adapter.Name + " " + adapter.Description;
            foreach (string marker in VirtualMarkers)
                if (name.Contains(marker, StringComparison.OrdinalIgnoreCase)) return false;
            return true;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MemoryStatus
        {
            public uint Length, MemoryLoad;
            public ulong TotalPhysical, AvailablePhysical, TotalPageFile, AvailablePageFile,
                TotalVirtual, AvailableVirtual, AvailableExtendedVirtual;
        }

        [DllImport("kernel32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetSystemTimes(out ulong idle, out ulong kernel, out ulong user);

        [DllImport("kernel32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);
    }
}
