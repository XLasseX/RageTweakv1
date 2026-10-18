using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace FortniteTweakTool.Services;

/// <summary>Letzte bekannte GPU-Werte (NVIDIA, über nvidia-smi). NaN = Wert nicht verfügbar.</summary>
public sealed class GpuSample
{
    public string Name { get; init; } = "";
    public double Load { get; init; } = double.NaN;      // %
    public double Temp { get; init; } = double.NaN;      // °C
    public double CoreMhz { get; init; } = double.NaN;
    public double MemMhz { get; init; } = double.NaN;
    public double VramUsedMb { get; init; } = double.NaN;
    public double VramTotalMb { get; init; } = double.NaN;
    public double PowerW { get; init; } = double.NaN;
    public double FanPercent { get; init; } = double.NaN;
}

public sealed class HardwareSnapshot
{
    public int LogicalCores { get; set; }
    public double CpuLoad { get; set; }                  // % gesamt
    public double[] CoreLoads { get; set; } = Array.Empty<double>();
    public double CpuMhz { get; set; }                   // 0 = unbekannt
    public double RamUsedGb { get; set; }
    public double RamTotalGb { get; set; }
    public double RamPercent { get; set; }
    public GpuSample? Gpu { get; set; }
    public bool GpuUnavailable { get; set; }
}

/// <summary>
/// Liest echte Systemwerte ohne Zusatzpakete: CPU/RAM über Windows-APIs, GPU über nvidia-smi (NVIDIA-Treiber).
/// Kein Eingriff in Spiele, keine Hooks.
/// </summary>
public sealed class HardwareMonitor : IDisposable
{
    [StructLayout(LayoutKind.Sequential)]
    private struct CoreTimes { public long Idle, Kernel, User, Dpc, Interrupt; public uint InterruptCount; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemStatus
    {
        public uint Length, MemoryLoad;
        public ulong TotalPhys, AvailPhys, TotalPageFile, AvailPageFile, TotalVirtual, AvailVirtual, AvailExtendedVirtual;
    }

    [DllImport("ntdll.dll")]
    private static extern int NtQuerySystemInformation(int infoClass, IntPtr info, int length, out int returnLength);

    [DllImport("powrprof.dll")]
    private static extern uint CallNtPowerInformation(int level, IntPtr input, uint inputLength, IntPtr output, uint outputLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx(ref MemStatus status);

    private const int SystemProcessorPerformanceInformation = 8;
    private const int ProcessorInformation = 11;
    private const int PowerInfoSize = 24; // PROCESSOR_POWER_INFORMATION: 6 x uint

    private readonly int _coreStructSize = Marshal.SizeOf<CoreTimes>();
    private readonly IntPtr _coreBuffer;
    private readonly IntPtr _powerBuffer;
    private readonly CoreTimes[] _prev;
    private bool _hasPrev;

    private Process? _gpuProcess;
    private volatile GpuSample? _gpu;
    private volatile bool _gpuUnavailable;

    public int LogicalCores { get; }
    public string CpuName { get; }
    public bool GpuUnavailable => _gpuUnavailable && _gpu == null;

    public HardwareMonitor()
    {
        LogicalCores = Math.Max(1, Environment.ProcessorCount);
        _prev = new CoreTimes[LogicalCores];
        _coreBuffer = Marshal.AllocHGlobal(_coreStructSize * LogicalCores);
        _powerBuffer = Marshal.AllocHGlobal(PowerInfoSize * LogicalCores);
        CpuName = ReadCpuName();
    }

    private static string ReadCpuName()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
            var name = key?.GetValue("ProcessorNameString") as string;
            if (!string.IsNullOrWhiteSpace(name)) return string.Join(" ", name.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        }
        catch { }
        return "Unbekannter Prozessor";
    }

    /// <summary>Startet die Messung (GPU-Abfrage läuft in einem kleinen Hintergrundprozess).</summary>
    public void Start(bool withGpu = true)
    {
        _hasPrev = false;
        if (withGpu) StartGpuProcess();
    }

    /// <summary>Startet die GPU-Abfrage nachträglich (tut nichts, wenn sie schon läuft).</summary>
    public void EnsureGpu() => StartGpuProcess();

    /// <summary>Stoppt die GPU-Abfrage, damit nichts im Hintergrund läuft, solange das Dashboard nicht sichtbar ist.</summary>
    public void Stop() => StopGpuProcess();

    public HardwareSnapshot Read()
    {
        var snap = new HardwareSnapshot { LogicalCores = LogicalCores };
        ReadCpu(snap);
        snap.CpuMhz = ReadCpuMhz();
        ReadRam(snap);
        snap.Gpu = _gpu;
        snap.GpuUnavailable = GpuUnavailable;
        return snap;
    }

    private void ReadCpu(HardwareSnapshot snap)
    {
        var loads = new double[LogicalCores];
        try
        {
            int status = NtQuerySystemInformation(SystemProcessorPerformanceInformation, _coreBuffer, _coreStructSize * LogicalCores, out _);
            if (status == 0)
            {
                double sumBusy = 0, sumTotal = 0;
                for (int i = 0; i < LogicalCores; i++)
                {
                    var t = Marshal.PtrToStructure<CoreTimes>(IntPtr.Add(_coreBuffer, i * _coreStructSize));
                    if (_hasPrev)
                    {
                        double idle = t.Idle - _prev[i].Idle;
                        double total = (t.Kernel - _prev[i].Kernel) + (t.User - _prev[i].User); // Kernel enthält Idle
                        if (total > 0)
                        {
                            double busy = Math.Clamp(total - idle, 0, total);
                            loads[i] = busy / total * 100.0;
                            sumBusy += busy;
                            sumTotal += total;
                        }
                    }
                    _prev[i] = t;
                }
                _hasPrev = true;
                snap.CpuLoad = sumTotal > 0 ? sumBusy / sumTotal * 100.0 : 0;
            }
        }
        catch { }
        snap.CoreLoads = loads;
    }

    private double ReadCpuMhz()
    {
        try
        {
            if (CallNtPowerInformation(ProcessorInformation, IntPtr.Zero, 0, _powerBuffer, (uint)(PowerInfoSize * LogicalCores)) != 0)
                return 0;
            double sum = 0;
            for (int i = 0; i < LogicalCores; i++)
                sum += unchecked((uint)Marshal.ReadInt32(_powerBuffer, i * PowerInfoSize + 8)); // CurrentMhz
            return sum / LogicalCores;
        }
        catch { return 0; }
    }

    private static void ReadRam(HardwareSnapshot snap)
    {
        try
        {
            var m = new MemStatus { Length = (uint)Marshal.SizeOf<MemStatus>() };
            if (!GlobalMemoryStatusEx(ref m)) return;
            const double gb = 1024.0 * 1024 * 1024;
            snap.RamTotalGb = m.TotalPhys / gb;
            snap.RamUsedGb = (m.TotalPhys - m.AvailPhys) / gb;
            snap.RamPercent = m.TotalPhys > 0 ? (m.TotalPhys - m.AvailPhys) * 100.0 / m.TotalPhys : 0;
        }
        catch { }
    }

    // ----------------------------- GPU (nvidia-smi) -----------------------------

    private static string FindNvidiaSmi()
    {
        var candidates = new[]
        {
            Path.Combine(Environment.SystemDirectory, "nvidia-smi.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "NVIDIA Corporation", "NVSMI", "nvidia-smi.exe")
        };
        foreach (var c in candidates)
            if (File.Exists(c)) return c;
        return "nvidia-smi"; // PATH-Suche; schlägt Start fehl, gilt die GPU als nicht verfügbar
    }

    private void StartGpuProcess()
    {
        if (_gpuProcess != null || _gpuUnavailable) return;
        try
        {
            var psi = new ProcessStartInfo(FindNvidiaSmi(),
                "--query-gpu=index,name,utilization.gpu,temperature.gpu,clocks.gr,clocks.mem,memory.used,memory.total,power.draw,fan.speed --format=csv,noheader,nounits -l 1")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = System.Text.Encoding.UTF8
            };
            var p = new Process { StartInfo = psi, EnableRaisingEvents = true };
            p.OutputDataReceived += (_, e) => { if (e.Data != null) ParseGpuLine(e.Data); };
            p.ErrorDataReceived += (_, _) => { };
            p.Exited += (_, _) =>
            {
                if (_gpu == null) _gpuUnavailable = true;
                _gpuProcess = null;
            };
            p.Start();
            p.BeginOutputReadLine();
            p.BeginErrorReadLine();
            _gpuProcess = p;
        }
        catch
        {
            _gpuUnavailable = true;
            _gpuProcess = null;
        }
    }

    private void StopGpuProcess()
    {
        var p = _gpuProcess;
        _gpuProcess = null;
        if (p == null) return;
        try { if (!p.HasExited) p.Kill(); } catch { }
        try { p.Dispose(); } catch { }
    }

    private static double Num(string s) =>
        double.TryParse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : double.NaN;

    private void ParseGpuLine(string line)
    {
        try
        {
            var p = line.Split(',');
            if (p.Length < 10) return;
            if (p[0].Trim() != "0") return; // nur die erste GPU

            _gpu = new GpuSample
            {
                Name = p[1].Trim(),
                Load = Num(p[2]),
                Temp = Num(p[3]),
                CoreMhz = Num(p[4]),
                MemMhz = Num(p[5]),
                VramUsedMb = Num(p[6]),
                VramTotalMb = Num(p[7]),
                PowerW = Num(p[8]),
                FanPercent = Num(p[9])
            };
        }
        catch { }
    }

    public void Dispose()
    {
        StopGpuProcess();
        try { Marshal.FreeHGlobal(_coreBuffer); } catch { }
        try { Marshal.FreeHGlobal(_powerBuffer); } catch { }
    }
}
