using System.IO;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using FortniteTweakTool.Models;
using FortniteTweakTool.Services;

namespace FortniteTweakTool;

/// <summary>
/// Verwaltet das Overlay: Einstellungen laden/speichern, Fenster ein-/ausblenden.
/// Das Overlay ist ein normales, transparentes Fenster ganz oben. Es greift NICHT in das Spiel ein
/// (kein Hook, keine Injection) und zeigt nur echte Systemwerte.
/// </summary>
public static class OverlayManager
{
    public static OverlaySettings Current { get; private set; } = new();
    private static OverlayWindow? _window;
    private static readonly DispatcherTimer SaveTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private static bool _timerHooked;

    public static string SettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FortniteTweakTool", "overlay.json");

    public static bool IsVisible => _window != null;

    public static void Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
                Current = JsonSerializer.Deserialize<OverlaySettings>(File.ReadAllText(SettingsPath), OverlaySettings.JsonOptions) ?? new OverlaySettings();
        }
        catch (Exception ex)
        {
            Current = new OverlaySettings();
            try { TweakService.Log("Overlay-Einstellungen konnten nicht geladen werden: " + ex.Message); } catch { }
        }
        if (!_timerHooked)
        {
            _timerHooked = true;
            SaveTimer.Tick += (_, _) => { SaveTimer.Stop(); Save(); };
        }
    }

    public static void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(Current, OverlaySettings.JsonOptions));
        }
        catch (Exception ex)
        {
            try { TweakService.Log("Overlay-Einstellungen konnten nicht gespeichert werden: " + ex.Message); } catch { }
        }
    }

    public static void SaveSoon() { SaveTimer.Stop(); SaveTimer.Start(); }

    public static void Show()
    {
        if (_window != null) return;
        _window = new OverlayWindow();
        _window.Closed += (_, _) => _window = null;
        _window.Show();
        Current.Enabled = true;
        SaveSoon();
    }

    public static void Hide()
    {
        var w = _window;
        _window = null;
        w?.Close();
        Current.Enabled = false;
        SaveSoon();
    }

    public static void Toggle() { if (IsVisible) Hide(); else Show(); }

    /// <summary>Wird bei jeder Änderung der Einstellungen oder des Designs aufgerufen.</summary>
    public static void Refresh(bool save = false)
    {
        _window?.Rebuild();
        if (save) SaveSoon();
    }

    /// <summary>Beim Beenden: Fenster schließen, ohne den gespeicherten Ein/Aus-Zustand zu ändern.</summary>
    public static void Shutdown()
    {
        var w = _window;
        _window = null;
        w?.Close();
        SaveTimer.Stop();
        Save();
    }
}

public sealed class OverlayWindow : Window
{
    // ---- Win32 ----
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x20;
    private const int WS_EX_TOOLWINDOW = 0x80;
    private const int WS_EX_NOACTIVATE = 0x08000000;

    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    // ---- Anzeige ----
    private readonly Border _card = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly Dictionary<string, TextBlock> _values = new();
    private readonly HardwareMonitor _hw = new();
    private readonly FpsMonitor _fps = new();
    private double _lastFps = double.NaN;
    private int _gamePid;
    private Brush _textBrush = Brushes.White;
    private int _tick;
    private long _pingMs = -1;
    private bool _pinging;
    private bool _closed;
    private static OverlaySettings S => OverlayManager.Current;

    private static readonly Brush Green = Frozen(Color.FromRgb(0x3D, 0xDC, 0x84));
    private static readonly Brush Amber = Frozen(Color.FromRgb(0xFF, 0xC6, 0x6D));
    private static readonly Brush Red = Frozen(Color.FromRgb(0xFF, 0x5C, 0x5C));
    private static readonly Brush Gray = Frozen(Color.FromRgb(0x8A, 0x8A, 0x8A));

    private static Brush Frozen(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }

    public OverlayWindow()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        Title = "Rage Tweaks Overlay";
        Content = _card;

        SourceInitialized += (_, _) => ApplyWindowStyles();
        SizeChanged += (_, _) => PositionWindow();
        LocationChanged += (_, _) => RememberCustomPosition();
        MouseLeftButtonDown += (_, _) =>
        {
            if (S.ClickThrough) return;
            try { DragMove(); } catch { }
        };
        Closed += (_, _) =>
        {
            _closed = true;
            _timer.Stop();
            try { _hw.Dispose(); } catch { }
            try { _fps.Dispose(); } catch { }
        };

        _hw.Start(withGpu: false);
        _timer.Tick += (_, _) => Tick();
        Rebuild();
        _timer.Start();
        Tick();
    }

    private void ApplyWindowStyles()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;
        int ex = GetWindowLong(hwnd, GWL_EXSTYLE) | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
        ex = S.ClickThrough ? ex | WS_EX_TRANSPARENT : ex & ~WS_EX_TRANSPARENT;
        SetWindowLong(hwnd, GWL_EXSTYLE, ex);
    }

    // ---- Aufbau ----
    public void Rebuild()
    {
        var th = ThemeManager.Current;
        var panel = ThemeManager.ParseColor(th.PanelColor, Color.FromRgb(23, 26, 43));
        var accent = ThemeManager.ParseColor(th.AccentColor, Color.FromRgb(123, 97, 255));
        var text = ThemeManager.ParseColor(th.TextColor, Colors.White);
        var muted = ThemeManager.ParseColor(th.MutedColor, Color.FromRgb(154, 163, 199));
        _textBrush = new SolidColorBrush(text);

        Opacity = Math.Clamp(S.Opacity, 0.3, 1.0);

        _card.Background = new SolidColorBrush(ThemeManager.WithAlpha(panel, Math.Clamp(S.BackgroundOpacity, 0, 1)));
        _card.BorderBrush = new SolidColorBrush(ThemeManager.WithAlpha(accent, 0.8));
        _card.BorderThickness = new Thickness(1.2);
        _card.CornerRadius = new CornerRadius(Math.Max(4, th.CornerRadius * 0.6));
        _card.Padding = new Thickness(12, 8, 12, 8);
        var sc = Math.Clamp(S.Scale, 0.7, 2.0);
        _card.LayoutTransform = new ScaleTransform(sc, sc);

        try { FontFamily = new FontFamily(th.FontFamily); } catch { }
        _values.Clear();

        var host = new StackPanel { Orientation = S.Horizontal ? Orientation.Horizontal : Orientation.Vertical };

        void Add(string key, string label)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = S.Horizontal ? new Thickness(0, 0, 14, 0) : new Thickness(0, 1, 0, 1) };
            row.Children.Add(new TextBlock { Text = label, Foreground = new SolidColorBrush(muted), FontSize = 11, Width = S.Horizontal ? double.NaN : 56, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
            var v = new TextBlock { Text = "…", Foreground = _textBrush, FontSize = 13, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
            row.Children.Add(v);
            _values[key] = v;
            host.Children.Add(row);
        }

        if (S.ShowTitle)
        {
            host.Children.Add(new TextBlock
            {
                Text = "RAGE TWEAKS",
                FontSize = 11, FontWeight = FontWeights.Bold,
                Foreground = new LinearGradientBrush(accent, ThemeManager.ParseColor(th.Accent2Color, accent), 0),
                Margin = S.Horizontal ? new Thickness(0, 0, 14, 0) : new Thickness(0, 0, 0, 3),
                VerticalAlignment = VerticalAlignment.Center
            });
        }
        if (S.ShowFps) Add("fps", "FPS");
        if (S.ShowClock) Add("clock", "ZEIT");
        if (S.ShowPing) Add("ping", "PING");
        if (S.ShowCpu) Add("cpu", "CPU");
        if (S.ShowCpuClock) Add("cpumhz", "CPU-TAKT");
        if (S.ShowGpu) Add("gpu", "GPU");
        if (S.ShowGpuClock) Add("gpumhz", "GPU-TAKT");
        if (S.ShowGpuTemp) Add("gputemp", "GPU-TEMP");
        if (S.ShowGpuPower) Add("gpupower", "GPU-WATT");
        if (S.ShowGpuFan) Add("gpufan", "GPU-FAN");
        if (S.ShowVram) Add("vram", "VRAM");
        if (S.ShowRam) Add("ram", "RAM");
        if (S.ShowGame) Add("game", "SPIEL");

        // Wenn alles ausgeschaltet ist, bleibt wenigstens ein Hinweis sichtbar.
        if (host.Children.Count == 0)
            host.Children.Add(new TextBlock { Text = "Overlay (nichts ausgewählt)", Foreground = new SolidColorBrush(muted), FontSize = 11 });

        _card.Child = host;

        // GPU-Abfrage (nvidia-smi) nur laufen lassen, wenn auch GPU-Werte angezeigt werden.
        if (S.ShowGpu || S.ShowGpuClock || S.ShowGpuTemp || S.ShowGpuPower || S.ShowGpuFan || S.ShowVram) _hw.EnsureGpu(); else _hw.Stop();

        // FPS-Messung (ETW) nur mit Administratorrechten und nur, wenn die Zeile angezeigt wird.
        if (S.ShowFps && SystemService.IsAdmin()) _fps.Start(); else _fps.Stop();

        if (IsLoaded || new WindowInteropHelper(this).Handle != IntPtr.Zero) ApplyWindowStyles();
        UpdateValues();
        PositionWindow();
    }

    // ---- Position ----
    private bool _positioning;

    private void PositionWindow()
    {
        if (_closed || ActualWidth <= 0 || ActualHeight <= 0) return;
        _positioning = true;
        try
        {
            var wa = SystemParameters.WorkArea;
            const double m = 16;
            switch (S.Corner)
            {
                case OverlayCorner.TopLeft: Left = wa.Left + m; Top = wa.Top + m; break;
                case OverlayCorner.TopRight: Left = wa.Right - ActualWidth - m; Top = wa.Top + m; break;
                case OverlayCorner.BottomLeft: Left = wa.Left + m; Top = wa.Bottom - ActualHeight - m; break;
                case OverlayCorner.BottomRight: Left = wa.Right - ActualWidth - m; Top = wa.Bottom - ActualHeight - m; break;
                default:
                    Left = Math.Clamp(S.Left, wa.Left - 20, Math.Max(wa.Left, wa.Right - 40));
                    Top = Math.Clamp(S.Top, wa.Top - 20, Math.Max(wa.Top, wa.Bottom - 40));
                    break;
            }
        }
        finally { _positioning = false; }
    }

    private void RememberCustomPosition()
    {
        if (_positioning || _closed || S.Corner != OverlayCorner.Custom) return;
        S.Left = Left;
        S.Top = Top;
        OverlayManager.SaveSoon();
    }

    // ---- Werte ----
    private void Tick()
    {
        _tick++;
        if (S.ShowFps || S.ShowGame) _gamePid = FindGamePid();
        if (_fps.Running) _lastFps = _fps.SampleFps(_gamePid);
        UpdateValues();
        if (S.ShowPing && _tick % 2 == 1) _ = PingAsync();
    }

    private async Task PingAsync()
    {
        if (_pinging) return;
        _pinging = true;
        try
        {
            using var p = new Ping();
            var reply = await p.SendPingAsync(string.IsNullOrWhiteSpace(S.PingHost) ? "1.1.1.1" : S.PingHost.Trim(), 1500);
            _pingMs = reply.Status == IPStatus.Success ? reply.RoundtripTime : -1;
        }
        catch { _pingMs = -1; }
        finally { _pinging = false; }
        if (!_closed) UpdateValues();
    }

    private static int FindGamePid()
    {
        var list = System.Diagnostics.Process.GetProcessesByName("FortniteClient-Win64-Shipping");
        try { return list.Length > 0 ? list[0].Id : 0; }
        finally { foreach (var p in list) p.Dispose(); }
    }

    private Brush LoadBrush(double pct) => pct >= 90 ? Red : pct >= 70 ? Amber : _textBrush;

    private void UpdateValues()
    {
        if (_closed) return;
        var s = _hw.Read();
        var g = s.Gpu;
        string gpuMissing = s.GpuUnavailable ? "n/a" : "…";

        void GpuValue(string key, Func<GpuSample, double> pick, Func<double, string> fmt, Func<double, Brush>? color = null)
        {
            if (!_values.TryGetValue(key, out var tb)) return;
            double v = g == null ? double.NaN : pick(g);
            if (double.IsNaN(v)) { tb.Text = g == null ? gpuMissing : "—"; tb.Foreground = Gray; }
            else { tb.Text = fmt(v); tb.Foreground = color?.Invoke(v) ?? _textBrush; }
        }

        if (_values.TryGetValue("fps", out var fps))
        {
            if (!SystemService.IsAdmin()) { fps.Text = "Admin nötig"; fps.Foreground = Gray; }
            else if (_fps.Error != null) { fps.Text = "n/a"; fps.Foreground = Gray; }
            else if (_gamePid == 0) { fps.Text = "—"; fps.Foreground = Gray; }
            else if (double.IsNaN(_lastFps) || _lastFps < 0.5) { fps.Text = "…"; fps.Foreground = Gray; }
            else { fps.Text = $"{_lastFps:0}"; fps.Foreground = _lastFps < 30 ? Red : _lastFps < 60 ? Amber : Green; }
        }

        if (_values.TryGetValue("clock", out var clock)) clock.Text = DateTime.Now.ToString("HH:mm:ss");

        if (_values.TryGetValue("ping", out var ping))
        {
            if (_pingMs < 0) { ping.Text = _tick < 2 ? "…" : "—"; ping.Foreground = Gray; }
            else
            {
                ping.Text = _pingMs + " ms";
                ping.Foreground = _pingMs < 50 ? Green : _pingMs < 100 ? Amber : Red;
            }
        }

        if (_values.TryGetValue("cpu", out var cpu))
        {
            cpu.Text = $"{s.CpuLoad:0} %";
            cpu.Foreground = LoadBrush(s.CpuLoad);
        }
        if (_values.TryGetValue("cpumhz", out var mhz))
            mhz.Text = s.CpuMhz > 0 ? $"{s.CpuMhz:0} MHz" : "—";

        GpuValue("gpu", x => x.Load, v => $"{v:0} %", LoadBrush);
        GpuValue("gpumhz", x => x.CoreMhz, v => $"{v:0} MHz");
        GpuValue("gputemp", x => x.Temp, v => $"{v:0} °C", v => v >= 85 ? Red : v >= 75 ? Amber : _textBrush);
        GpuValue("gpupower", x => x.PowerW, v => $"{v:0} W");
        GpuValue("gpufan", x => x.FanPercent, v => $"{v:0} %");

        if (_values.TryGetValue("vram", out var vram))
        {
            if (g == null || double.IsNaN(g.VramUsedMb) || double.IsNaN(g.VramTotalMb)) { vram.Text = g == null ? gpuMissing : "—"; vram.Foreground = Gray; }
            else { vram.Text = $"{g.VramUsedMb / 1024.0:0.0} / {g.VramTotalMb / 1024.0:0.#} GB"; vram.Foreground = _textBrush; }
        }

        if (_values.TryGetValue("ram", out var ram))
            ram.Text = s.RamTotalGb > 0 ? $"{s.RamUsedGb:0.0} / {s.RamTotalGb:0.#} GB" : "…";

        if (_values.TryGetValue("game", out var game) && (_tick % 2 == 1 || game.Text == "…"))
        {
            bool running = FindGamePid() != 0;
            game.Text = running ? "Fortnite läuft" : "nicht erkannt";
            game.Foreground = running ? Green : Gray;
        }
    }
}
