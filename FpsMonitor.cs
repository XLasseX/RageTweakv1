using System.Diagnostics;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Session;

namespace FortniteTweakTool.Services;

/// <summary>
/// Misst die FPS eines Prozesses über die Windows-Ereignisse (ETW) des DXGI-Present-Aufrufs, so wie es auch PresentMon tut.
/// Kein Hook, keine Injection, kein Zugriff auf den Spielprozess. Braucht Administratorrechte, weil Windows
/// Ereignis-Sitzungen nur für Administratoren erlaubt.
/// </summary>
public sealed class FpsMonitor : IDisposable
{
    private static readonly Guid DxgiProvider = new("CA11C036-0102-4A2D-A6AD-F03CFED5D3C9"); // Microsoft-Windows-DXGI
    private const int PresentStart = 42;        // DXGI Present_Start
    private const int PresentMpoStart = 45;     // DXGI PresentMultiplaneOverlay_Start

    private readonly object _lock = new();
    private readonly Dictionary<int, int> _counts = new();
    private TraceEventSession? _session;
    private long _lastStamp;

    /// <summary>Fehlertext, falls die Messung nicht gestartet werden konnte (sonst null).</summary>
    public string? Error { get; private set; }
    public bool Running => _session != null && Error == null;

    public void Start()
    {
        if (_session != null) return;
        try
        {
            Error = null;
            var session = new TraceEventSession("RageTweaksFpsSession");   // eine eventuell hängende alte Sitzung wird ersetzt
            session.Source.AllEvents += OnEvent;
            session.EnableProvider(DxgiProvider, TraceEventLevel.Verbose, ulong.MaxValue);
            _session = session;
            _lastStamp = Stopwatch.GetTimestamp();

            var t = new Thread(() =>
            {
                try { session.Source.Process(); }
                catch (Exception ex) { Error = ex.Message; }
            })
            { IsBackground = true, Name = "RageTweaks.FPS" };
            t.Start();
        }
        catch (Exception ex)
        {
            Error = ex.Message;
            TweakService.Log("FPS-Messung konnte nicht starten: " + ex.Message);
            Stop();
        }
    }

    private void OnEvent(TraceEvent data)
    {
        if (data.ProviderGuid != DxgiProvider) return;
        int id = (int)data.ID;
        if (id != PresentStart && id != PresentMpoStart) return;
        lock (_lock)
        {
            _counts.TryGetValue(data.ProcessID, out int c);
            _counts[data.ProcessID] = c + 1;
        }
    }

    /// <summary>
    /// Liefert die FPS des Prozesses seit dem letzten Aufruf (NaN = noch keine Messung / Prozess zeigt nichts an).
    /// Nur einmal pro Sekunde aufrufen, jeder Aufruf setzt die Zähler zurück.
    /// </summary>
    public double SampleFps(int pid)
    {
        long now = Stopwatch.GetTimestamp();
        double seconds = (now - _lastStamp) / (double)Stopwatch.Frequency;
        _lastStamp = now;

        int count;
        lock (_lock)
        {
            _counts.TryGetValue(pid, out count);
            _counts.Clear();
        }
        if (pid <= 0 || seconds < 0.2) return double.NaN;
        return count / seconds;
    }

    public void Stop()
    {
        var s = _session;
        _session = null;
        if (s == null) return;
        try { s.Dispose(); } catch { }
        lock (_lock) _counts.Clear();
    }

    public void Dispose() => Stop();
}
