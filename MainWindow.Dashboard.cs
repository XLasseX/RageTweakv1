using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using FortniteTweakTool.Services;
using Ellipse = System.Windows.Shapes.Ellipse;
using Line = System.Windows.Shapes.Line;
using Polygon = System.Windows.Shapes.Polygon;
using Polyline = System.Windows.Shapes.Polyline;
using Shape = System.Windows.Shapes.Shape;
using ShapePath = System.Windows.Shapes.Path;

namespace FortniteTweakTool;

public partial class MainWindow
{
    // ------------------------------------------------------------------ Zustand
    private HardwareMonitor? _hw;
    private DispatcherTimer? _dashTimer;
    private int _dashTick;
    private bool _dashReady, _dashRunning, _pingBusy, _fnRunning, _gpuNameShown;
    private double _lastPing = double.NaN;
    private readonly List<FrameworkElement> _reveal = new();

    private Ring _ringCpu = null!, _ringGpu = null!, _ringRam = null!, _ringVram = null!;
    private Metric _mGpuLoad = null!, _mGpuTemp = null!, _mGpuClock = null!, _mVram = null!;
    private Metric _mCpuLoad = null!, _mCpuClock = null!, _mRam = null!, _mPing = null!;
    private Border[] _coreBars = Array.Empty<Border>();
    private bool[] _coreHot = Array.Empty<bool>();
    private TextBlock _coreAvg = null!;
    private const double CoreBarHeight = 76;

    private static readonly Color DashGreen = Color.FromRgb(0x3D, 0xDC, 0x84);
    private static readonly Color DashAmber = Color.FromRgb(0xFF, 0xC6, 0x6D);
    private static readonly Color DashRed = Color.FromRgb(0xFF, 0x5C, 0x5C);
    private static readonly Color DashPurple = Color.FromRgb(0xB3, 0x88, 0xFF);
    private static readonly Color DashOrange = Color.FromRgb(0xFF, 0x7A, 0x59);
    private static readonly Color DashSky = Color.FromRgb(0x4F, 0xC3, 0xF7);

    // ------------------------------------------------------------------ Farben (Theme-Resource oder fest)
    private sealed class Paint
    {
        public string? Key { get; }
        public Color Color { get; }
        public Paint(string? key, Color color) { Key = key; Color = color; }

        public void Apply(DependencyObject target, DependencyProperty dp)
        {
            if (Key != null && target is FrameworkElement fe) fe.SetResourceReference(dp, Key);
            else target.SetValue(dp, new SolidColorBrush(Color));
        }
    }

    private Paint ThemePaint(string key, Color fallback) =>
        new(key, TryFindResource(key) is SolidColorBrush b ? b.Color : fallback);

    private static Paint FixedPaint(Color c) => new(null, c);

    // ------------------------------------------------------------------ Init
    private void InitDashboard()
    {
        _hw = new HardwareMonitor();
        DashHardware.Text = $"{_hw.CpuName} · {_hw.LogicalCores} Threads";

        var accent = ThemePaint("AccentBrush", Color.FromRgb(0x7C, 0x5C, 0xFF));
        var blue = ThemePaint("BlueBrush", Color.FromRgb(0x4F, 0x8C, 0xFF));

        _ringCpu = BuildRing("CPU", accent);
        _ringGpu = BuildRing("GPU", FixedPaint(DashGreen));
        _ringRam = BuildRing("RAM", blue);
        _ringVram = BuildRing("VRAM", FixedPaint(DashPurple));
        foreach (var r in new[] { _ringCpu, _ringGpu, _ringRam, _ringVram })
        {
            RingHost.Children.Add(r.Card);
            PrepareCard(r.Card);
        }

        _mGpuLoad = BuildMetric("GPU-AUSLASTUNG", "0", " %", FixedPaint(DashGreen), 100, 100);
        _mCpuLoad = BuildMetric("CPU-AUSLASTUNG", "0", " %", accent, 100, 100);
        _mGpuTemp = BuildMetric("GPU-TEMPERATUR", "0", " °C", FixedPaint(DashOrange), 100, 100);
        _mCpuClock = BuildMetric("CPU-TAKT", "0", " MHz", FixedPaint(DashAmber), null, 1000);
        _mGpuClock = BuildMetric("GPU-TAKT", "0", " MHz", FixedPaint(DashAmber), null, 1000);
        _mRam = BuildMetric("RAM-BELEGUNG", "0.0", " GB", blue, null, 4);
        _mVram = BuildMetric("VRAM-BELEGUNG", "0.0", " GB", FixedPaint(DashPurple), null, 2);
        _mPing = BuildMetric("PING (ICMP)", "0", " ms", FixedPaint(DashSky), null, 50);
        foreach (var m in new[] { _mGpuLoad, _mCpuLoad, _mGpuTemp, _mCpuClock, _mGpuClock, _mRam, _mVram, _mPing })
        {
            GraphHost.Children.Add(m.Card);
            PrepareCard(m.Card);
        }

        BuildCores();
        PrepareCard(CoreHost);

        PrepareCard(DashHero);
        PrepareCard(OneClickCard);
        PrepareCard(AnalysisCard);

        // Karten-Reihenfolge für die Einblend-Animation: Hero, Ringe, Grafiken, Kerne, unten
        _reveal.Remove(DashHero);
        _reveal.Insert(0, DashHero);
        _reveal.Remove(CoreHost);
        _reveal.Add(CoreHost);
        _reveal.Remove(OneClickCard);
        _reveal.Add(OneClickCard);
        _reveal.Remove(AnalysisCard);
        _reveal.Add(AnalysisCard);

        _dashTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _dashTimer.Tick += (_, _) => DashTick();

        InitNav();

        Loaded += (_, _) =>
        {
            _dashReady = true;
            SetDashboardActive(DashboardPanel.Visibility == Visibility.Visible);
            PlayStartupIntro();
            PlayDashboardIntro();
        };
        StateChanged += (_, _) => SetDashboardActive(DashboardPanel.Visibility == Visibility.Visible);
        Closed += (_, _) =>
        {
            _dashTimer?.Stop();
            _hw?.Dispose();
        };
    }

    private void SetDashboardActive(bool wantActive)
    {
        if (_hw == null || _dashTimer == null || !_dashReady) return;
        bool run = wantActive && WindowState != WindowState.Minimized;
        if (run && !_dashRunning)
        {
            _hw.Start();
            _dashTimer.Start();
            _dashRunning = true;
            DashTick();
        }
        else if (!run && _dashRunning)
        {
            _dashTimer.Stop();
            _hw.Stop();
            _dashRunning = false;
        }
    }

    // Wird von ShowPage aufgerufen.
    private void OnPageShown(bool dashboard)
    {
        SetDashboardActive(dashboard);
        if (dashboard) PlayDashboardIntro();
    }

    // ------------------------------------------------------------------ Live-Update
    private void DashTick()
    {
        if (_hw == null) return;
        _dashTick++;
        var s = _hw.Read();

        // CPU
        string cpuSub = $"{s.LogicalCores} Threads" + (s.CpuMhz > 0 ? $" · {s.CpuMhz / 1000.0:0.0} GHz" : "");
        _ringCpu.Set(s.CpuLoad, cpuSub);
        _mCpuLoad.Push(s.CpuLoad);
        _mCpuClock.Push(s.CpuMhz > 0 ? s.CpuMhz : double.NaN);
        UpdateCores(s.CoreLoads, s.CpuLoad);

        // RAM
        _ringRam.Set(s.RamPercent, $"{s.RamUsedGb:0.0} / {s.RamTotalGb:0.#} GB");
        _mRam.Push(s.RamUsedGb);

        // GPU
        var g = s.Gpu;
        if (g != null)
        {
            if (!_gpuNameShown && !string.IsNullOrWhiteSpace(g.Name))
            {
                DashHardware.Text = $"{_hw.CpuName} · {_hw.LogicalCores} Threads{Environment.NewLine}{g.Name}";
                _gpuNameShown = true;
            }

            var parts = new List<string>();
            if (!double.IsNaN(g.Temp)) parts.Add($"{g.Temp:0} °C");
            if (!double.IsNaN(g.PowerW)) parts.Add($"{g.PowerW:0} W");
            if (!double.IsNaN(g.FanPercent)) parts.Add($"Lüfter {g.FanPercent:0} %");

            if (double.IsNaN(g.Load)) _ringGpu.SetNA("keine Daten");
            else _ringGpu.Set(g.Load, parts.Count > 0 ? string.Join(" · ", parts) : "aktiv");

            _mGpuLoad.Push(g.Load);
            _mGpuTemp.Push(g.Temp);
            _mGpuClock.Push(g.CoreMhz);

            double usedGb = g.VramUsedMb / 1024.0;
            double totalGb = g.VramTotalMb / 1024.0;
            if (!double.IsNaN(usedGb) && !double.IsNaN(totalGb) && totalGb > 0)
            {
                string vsub = $"{usedGb:0.0} / {totalGb:0.0} GB" + (double.IsNaN(g.MemMhz) ? "" : $" · {g.MemMhz:0} MHz");
                _ringVram.Set(usedGb / totalGb * 100.0, vsub);
                _mVram.Push(usedGb);
            }
            else
            {
                _ringVram.SetNA("keine Daten");
                _mVram.Push(double.NaN);
            }
        }
        else
        {
            string why = s.GpuUnavailable ? "NVIDIA-Daten nicht verfügbar" : "lädt …";
            _ringGpu.SetNA(why);
            _ringVram.SetNA(why);
            _mGpuLoad.Push(double.NaN);
            _mGpuTemp.Push(double.NaN);
            _mGpuClock.Push(double.NaN);
            _mVram.Push(double.NaN);
        }

        // Ping: alle paar Sekunden neu messen, der Graph läuft jede Sekunde mit dem letzten Wert
        if (_dashTick % 4 == 2 && !_pingBusy) _ = PingTickAsync();
        _mPing.Push(_lastPing);

        if (_dashTick % 3 == 1) UpdateFortniteChip();
    }

    private async Task PingTickAsync()
    {
        _pingBusy = true;
        try
        {
            var r = await SystemService.PingAsync();
            _lastPing = r.ms.HasValue ? r.ms.Value : double.NaN;
            DashPing.Text = r.ms.HasValue ? $"{r.ms.Value} ms" : "Fehler";
        }
        catch
        {
            _lastPing = double.NaN;
        }
        finally
        {
            _pingBusy = false;
        }
    }

    private void UpdateFortniteChip()
    {
        bool running = Process.GetProcessesByName("FortniteClient-Win64-Shipping").Length > 0;
        DashFortnite.Text = running ? "Läuft" : "Nicht erkannt";
        if (running == _fnRunning && FnDot.Fill != null && _dashTick > 1) return;
        _fnRunning = running;
        FnDot.Fill = new SolidColorBrush(running ? DashGreen : Color.FromRgb(0x7A, 0x7A, 0x7A));
        FnDot.BeginAnimation(OpacityProperty, running
            ? new DoubleAnimation(1.0, 0.25, TimeSpan.FromMilliseconds(800)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever }
            : null);
        if (!running) FnDot.Opacity = 1.0;
    }

    // ------------------------------------------------------------------ Ring-Anzeige
    private sealed class Ring
    {
        public Border Card = null!;
        public ShapePath Arc = null!;
        public TextBlock Value = null!;
        public TextBlock Sub = null!;

        private static DoubleAnimation Tween(double to, int ms) =>
            new(to, TimeSpan.FromMilliseconds(ms)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };

        public void Set(double percent, string sub)
        {
            percent = Math.Clamp(percent, 0, 100);
            Arc.BeginAnimation(UiAnim.PercentProperty, Tween(percent, 700));
            Value.BeginAnimation(UiAnim.NumberProperty, Tween(percent, 700));
            Sub.Text = sub;
        }

        public void SetNA(string sub)
        {
            Arc.BeginAnimation(UiAnim.PercentProperty, Tween(0, 400));
            Value.BeginAnimation(UiAnim.NumberProperty, null);
            Value.Text = "–";
            Sub.Text = sub;
        }
    }

    private Ring BuildRing(string title, Paint paint)
    {
        var r = new Ring();
        double size = UiAnim.RingSize, thick = UiAnim.RingThickness;

        var host = new Grid { Width = size, Height = size, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 12, 0, 10) };

        var track = new Ellipse { Width = size - thick, Height = size - thick, StrokeThickness = thick, Opacity = 0.18 };
        track.SetResourceReference(Shape.StrokeProperty, "MutedBrush");

        var arc = new ShapePath
        {
            Width = size,
            Height = size,
            StrokeThickness = thick,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            Effect = new DropShadowEffect { Color = paint.Color, BlurRadius = 14, ShadowDepth = 0, Opacity = 0.7 }
        };
        paint.Apply(arc, Shape.StrokeProperty);

        var value = new TextBlock
        {
            Text = "–",
            FontSize = 26,
            FontWeight = FontWeights.Bold,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        UiAnim.SetFormat(value, "0");
        UiAnim.SetSuffix(value, "%");

        host.Children.Add(track);
        host.Children.Add(arc);
        host.Children.Add(value);

        var label = new TextBlock { Text = title, FontSize = 12, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center };
        var sub = new TextBlock
        {
            Text = "wird gelesen …",
            FontSize = 11,
            HorizontalAlignment = HorizontalAlignment.Center,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 30
        };
        sub.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");

        var stack = new StackPanel();
        stack.Children.Add(label);
        stack.Children.Add(host);
        stack.Children.Add(sub);

        var card = new Border { Child = stack };
        card.Style = (Style)FindResource("DashCard");

        r.Card = card;
        r.Arc = arc;
        r.Value = value;
        r.Sub = sub;
        return r;
    }

    // ------------------------------------------------------------------ Verlaufsgrafik (wie MSI Afterburner)
    private sealed class Spark
    {
        private const int N = 60;
        private readonly List<double> _d = new();
        private readonly double? _fixedMax;
        private readonly double _minMax;
        private readonly Canvas _gridLines = new();
        private readonly Canvas _plot = new();
        private readonly Polyline _line = new();
        private readonly Polygon _area = new();
        private readonly TranslateTransform _shift = new();
        private readonly Line[] _gl = new Line[3];

        public readonly Grid Host = new() { Height = 70, ClipToBounds = true };

        public Spark(Paint paint, double? fixedMax, double minMax)
        {
            _fixedMax = fixedMax;
            _minMax = minMax;

            for (int i = 0; i < _gl.Length; i++)
            {
                var l = new Line { StrokeThickness = 1, Opacity = 0.2, StrokeDashArray = new DoubleCollection { 2, 3 } };
                l.SetResourceReference(Shape.StrokeProperty, "MutedBrush");
                _gl[i] = l;
                _gridLines.Children.Add(l);
            }

            _line.StrokeThickness = 2;
            _line.StrokeLineJoin = PenLineJoin.Round;
            paint.Apply(_line, Shape.StrokeProperty);

            _area.Opacity = 0.30;
            paint.Apply(_area, Shape.FillProperty);
            _area.OpacityMask = new LinearGradientBrush(Color.FromArgb(255, 0, 0, 0), Color.FromArgb(0, 0, 0, 0), 90);

            _plot.RenderTransform = _shift;
            _plot.Children.Add(_area);
            _plot.Children.Add(_line);

            Host.Children.Add(_gridLines);
            Host.Children.Add(_plot);
            Host.SizeChanged += (_, _) => Redraw();
        }

        public void Push(double v)
        {
            _d.Add(v);
            if (_d.Count > N) _d.RemoveAt(0);
            Redraw();

            double w = Host.ActualWidth;
            if (w > 4)
                _shift.BeginAnimation(TranslateTransform.XProperty,
                    new DoubleAnimation(w / (N - 1), 0, TimeSpan.FromMilliseconds(950)));
        }

        public (double min, double max, double avg) Summary()
        {
            if (_d.Count == 0) return (0, 0, 0);
            return (_d.Min(), _d.Max(), _d.Average());
        }

        private void Redraw()
        {
            double w = Host.ActualWidth, h = Host.ActualHeight;
            if (w < 4 || h < 4) return;

            for (int i = 0; i < _gl.Length; i++)
            {
                double y = h * (i + 1) / 4.0;
                _gl[i].X1 = 0;
                _gl[i].X2 = w;
                _gl[i].Y1 = y;
                _gl[i].Y2 = y;
            }

            if (_d.Count < 2) return;

            double max = _fixedMax ?? Math.Max(_minMax, _d.Max() * 1.15);
            if (max <= 0) max = 1;
            double step = w / (N - 1);
            int offset = N - _d.Count;

            var pts = new PointCollection();
            for (int i = 0; i < _d.Count; i++)
            {
                double x = (offset + i) * step;
                double y = h - 3 - Math.Clamp(_d[i] / max, 0, 1) * (h - 6);
                pts.Add(new Point(x, y));
            }
            _line.Points = pts;

            var area = new PointCollection(pts)
            {
                new Point((offset + _d.Count - 1) * step, h),
                new Point(offset * step, h)
            };
            _area.Points = area;
        }
    }

    private sealed class Metric
    {
        public Border Card = null!;
        public TextBlock Value = null!;
        public TextBlock Stats = null!;
        public Spark Spark = null!;
        public string Format = "0";

        public void Push(double v)
        {
            if (double.IsNaN(v))
            {
                Value.BeginAnimation(UiAnim.NumberProperty, null);
                Value.Text = "n/a";
                return;
            }

            Value.BeginAnimation(UiAnim.NumberProperty,
                new DoubleAnimation(v, TimeSpan.FromMilliseconds(700)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            Spark.Push(v);

            var s = Spark.Summary();
            Stats.Text = $"MIN {s.min.ToString(Format, CultureInfo.CurrentCulture)}   MAX {s.max.ToString(Format, CultureInfo.CurrentCulture)}   Ø {s.avg.ToString(Format, CultureInfo.CurrentCulture)}";
        }
    }

    private Metric BuildMetric(string title, string format, string suffix, Paint paint, double? fixedMax, double minMax)
    {
        var m = new Metric { Format = format };

        var head = new Grid();
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var t = new TextBlock { Text = title, VerticalAlignment = VerticalAlignment.Center, FontSize = 11, FontWeight = FontWeights.SemiBold };
        t.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");

        var v = new TextBlock { Text = "–", FontSize = 24, FontWeight = FontWeights.Bold };
        UiAnim.SetFormat(v, format);
        UiAnim.SetSuffix(v, suffix);
        Grid.SetColumn(v, 1);

        head.Children.Add(t);
        head.Children.Add(v);

        m.Spark = new Spark(paint, fixedMax, minMax);
        var frame = new Border { CornerRadius = new CornerRadius(8), Margin = new Thickness(0, 10, 0, 8), ClipToBounds = true, Child = m.Spark.Host };
        frame.SetResourceReference(Border.BackgroundProperty, "InsetBrush");

        m.Stats = new TextBlock { FontFamily = new FontFamily("Consolas"), FontSize = 11, Text = "MIN –   MAX –   Ø –" };
        m.Stats.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");

        var stack = new StackPanel();
        stack.Children.Add(head);
        stack.Children.Add(frame);
        stack.Children.Add(m.Stats);

        m.Value = v;
        m.Card = new Border { Child = stack };
        m.Card.Style = (Style)FindResource("DashCard");
        return m;
    }

    // ------------------------------------------------------------------ CPU-Kerne
    private void BuildCores()
    {
        int n = _hw!.LogicalCores;
        _coreBars = new Border[n];
        _coreHot = new bool[n];

        var head = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var title = new TextBlock { Text = $"CPU-KERNE · {n} THREADS", FontSize = 11, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        title.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");

        _coreAvg = new TextBlock { Text = "Ø –", FontFamily = new FontFamily("Consolas"), FontSize = 12 };
        Grid.SetColumn(_coreAvg, 1);
        head.Children.Add(title);
        head.Children.Add(_coreAvg);

        var bars = new UniformGrid { Rows = 1, Columns = n, Height = CoreBarHeight };
        for (int i = 0; i < n; i++)
        {
            var slot = new Grid { Margin = new Thickness(2, 0, 2, 0), ToolTip = $"Thread {i}" };

            var back = new Border { CornerRadius = new CornerRadius(4), Opacity = 0.14 };
            back.SetResourceReference(Border.BackgroundProperty, "MutedBrush");

            var bar = new Border { VerticalAlignment = VerticalAlignment.Bottom, Height = 2, CornerRadius = new CornerRadius(4) };
            bar.SetResourceReference(Border.BackgroundProperty, "AccentBrush");

            slot.Children.Add(back);
            slot.Children.Add(bar);
            bars.Children.Add(slot);
            _coreBars[i] = bar;
        }

        var stack = new StackPanel();
        stack.Children.Add(head);
        stack.Children.Add(bars);
        CoreHost.Child = stack;
    }

    private void UpdateCores(double[] loads, double avg)
    {
        _coreAvg.Text = $"Ø {avg:0} %";
        for (int i = 0; i < _coreBars.Length && i < loads.Length; i++)
        {
            double pct = Math.Clamp(loads[i], 0, 100);
            var bar = _coreBars[i];
            bar.BeginAnimation(HeightProperty,
                new DoubleAnimation(Math.Max(2, pct / 100.0 * CoreBarHeight), TimeSpan.FromMilliseconds(500))
                { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });

            bool hot = pct >= 90;
            if (hot != _coreHot[i])
            {
                _coreHot[i] = hot;
                if (hot) bar.Background = new SolidColorBrush(DashRed);
                else bar.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
            }
            if (bar.Parent is FrameworkElement slot) slot.ToolTip = $"Thread {i}: {pct:0} %";
        }
    }

    // ------------------------------------------------------------------ Karten: Hover + Einblenden
    private void PrepareCard(FrameworkElement el)
    {
        el.RenderTransformOrigin = new Point(0.5, 0.5);
        el.RenderTransform = new TransformGroup { Children = { new ScaleTransform(1, 1), new TranslateTransform(0, 0) } };

        if (el is Border b)
        {
            b.MouseEnter += (_, _) => CardHover(b, true);
            b.MouseLeave += (_, _) => CardHover(b, false);
        }
        if (!_reveal.Contains(el)) _reveal.Add(el);
    }

    private static void CardHover(Border b, bool on)
    {
        if (b.RenderTransform is not TransformGroup g || g.Children[0] is not ScaleTransform sc) return;

        DoubleAnimation Make() => new(on ? 1.012 : 1.0, TimeSpan.FromMilliseconds(140))
        { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };

        sc.BeginAnimation(ScaleTransform.ScaleXProperty, Make());
        sc.BeginAnimation(ScaleTransform.ScaleYProperty, Make());
        Panel.SetZIndex(b, on ? 5 : 0);
        b.SetResourceReference(Border.BorderBrushProperty, on ? "AccentBrush" : "PanelBorderBrush");
    }

    private void PlayDashboardIntro()
    {
        int i = 0;
        foreach (var el in _reveal)
        {
            if (el.RenderTransform is not TransformGroup g || g.Children[1] is not TranslateTransform tt) continue;

            var delay = TimeSpan.FromMilliseconds(55 * i++);
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

            el.BeginAnimation(OpacityProperty, null);
            tt.BeginAnimation(TranslateTransform.YProperty, null);
            el.Opacity = 0;
            tt.Y = 24;

            el.BeginAnimation(OpacityProperty,
                new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(450)) { BeginTime = delay, EasingFunction = ease });
            tt.BeginAnimation(TranslateTransform.YProperty,
                new DoubleAnimation(24, 0, TimeSpan.FromMilliseconds(520)) { BeginTime = delay, EasingFunction = ease });
        }
    }

    // Header, Navigation und Inhalt gleiten beim Start nacheinander ein.
    private void PlayStartupIntro()
    {
        void Slide(FrameworkElement el, double fromX, double fromY, int delayMs)
        {
            var tt = new TranslateTransform(fromX, fromY);
            el.RenderTransform = tt;
            el.Opacity = 0;
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            var delay = TimeSpan.FromMilliseconds(delayMs);
            el.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(500)) { BeginTime = delay, EasingFunction = ease });
            tt.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(fromX, 0, TimeSpan.FromMilliseconds(600)) { BeginTime = delay, EasingFunction = ease });
            tt.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(fromY, 0, TimeSpan.FromMilliseconds(600)) { BeginTime = delay, EasingFunction = ease });
        }

        Slide(HeaderGrid, 0, -24, 0);
        Slide(NavGrid, -36, 0, 120);
        Slide(ContentGrid, 0, 24, 220);
    }

    // ------------------------------------------------------------------ Navigation: gleitender Indikator
    private void InitNav()
    {
        NavPanel.AddHandler(ButtonBase.ClickEvent, new RoutedEventHandler((_, e) =>
        {
            if (e.OriginalSource is Button b && ReferenceEquals(b.Parent, NavPanel)) MoveNav(b, true);
        }));

        Loaded += (_, _) => Dispatcher.BeginInvoke(new Action(() => MoveNav(NavDashboard, false)), DispatcherPriority.Loaded);
    }

    private void MoveNav(Button target, bool animate)
    {
        foreach (var c in NavPanel.Children.OfType<Button>())
        {
            if (ReferenceEquals(c, target)) c.FontWeight = FontWeights.Bold;
            else c.ClearValue(Control.FontWeightProperty);
        }

        if (target.ActualHeight <= 0) return;

        double h = Math.Max(16, target.ActualHeight * 0.62);
        double y = target.TranslatePoint(new Point(0, 0), NavHost).Y + (target.ActualHeight - h) / 2.0;

        if (!animate)
        {
            NavMove.BeginAnimation(TranslateTransform.YProperty, null);
            NavMove.Y = y;
            NavIndicator.BeginAnimation(HeightProperty, null);
            NavIndicator.Height = h;
            return;
        }

        var ease = new CubicEase { EasingMode = EasingMode.EaseInOut };
        NavMove.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(y, TimeSpan.FromMilliseconds(320)) { EasingFunction = ease });
        NavIndicator.BeginAnimation(HeightProperty, new DoubleAnimation(h, TimeSpan.FromMilliseconds(320)) { EasingFunction = ease });
    }

    // ------------------------------------------------------------------ Ein-Klick-Karte: Button ausblenden, wenn schon aktiv
    private void UpdateOneClickState()
    {
        if (OneClickButton == null || OneClickDone == null || OneClickGameMode == null || OneClickPower == null) return;

        var wanted = new List<string>();
        if (OneClickGameMode.IsChecked == true) wanted.Add("gamemode");
        if (OneClickPower.IsChecked == true) wanted.Add("power");

        bool allActive = wanted.Count > 0 &&
            wanted.All(k => _tweaks.FirstOrDefault(t => t.Key == k) is { } item && StatusHelper.IsApplied(item.Status));

        OneClickButton.Visibility = allActive ? Visibility.Collapsed : Visibility.Visible;
        OneClickDone.Visibility = allActive ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OneClickOption_Changed(object sender, RoutedEventArgs e) => UpdateOneClickState();
}

// ====================================================================== Animierbare Zahlen / Ringe
internal static class UiAnim
{
    public const double RingSize = 112;
    public const double RingThickness = 10;

    public static readonly DependencyProperty NumberProperty =
        DependencyProperty.RegisterAttached("Number", typeof(double), typeof(UiAnim), new PropertyMetadata(0.0, OnNumberChanged));
    public static readonly DependencyProperty FormatProperty =
        DependencyProperty.RegisterAttached("Format", typeof(string), typeof(UiAnim), new PropertyMetadata("0"));
    public static readonly DependencyProperty SuffixProperty =
        DependencyProperty.RegisterAttached("Suffix", typeof(string), typeof(UiAnim), new PropertyMetadata(""));
    public static readonly DependencyProperty PercentProperty =
        DependencyProperty.RegisterAttached("Percent", typeof(double), typeof(UiAnim), new PropertyMetadata(0.0, OnPercentChanged));

    public static double GetNumber(DependencyObject o) => (double)o.GetValue(NumberProperty);
    public static void SetNumber(DependencyObject o, double v) => o.SetValue(NumberProperty, v);
    public static string GetFormat(DependencyObject o) => (string)o.GetValue(FormatProperty);
    public static void SetFormat(DependencyObject o, string v) => o.SetValue(FormatProperty, v);
    public static string GetSuffix(DependencyObject o) => (string)o.GetValue(SuffixProperty);
    public static void SetSuffix(DependencyObject o, string v) => o.SetValue(SuffixProperty, v);
    public static double GetPercent(DependencyObject o) => (double)o.GetValue(PercentProperty);
    public static void SetPercent(DependencyObject o, double v) => o.SetValue(PercentProperty, v);

    private static void OnNumberChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TextBlock tb)
            tb.Text = ((double)e.NewValue).ToString(GetFormat(tb), CultureInfo.CurrentCulture) + GetSuffix(tb);
    }

    private static void OnPercentChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ShapePath p) p.Data = BuildArc((double)e.NewValue);
    }

    private static Geometry BuildArc(double percent)
    {
        percent = Math.Clamp(percent, 0, 100);
        if (percent < 0.4) return Geometry.Empty;

        double c = RingSize / 2.0;
        double r = (RingSize - RingThickness) / 2.0;
        double angle = Math.Min(percent, 99.95) * 3.6;
        double rad = (angle - 90) * Math.PI / 180.0;

        var start = new Point(c, c - r);
        var end = new Point(c + r * Math.Cos(rad), c + r * Math.Sin(rad));

        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            ctx.BeginFigure(start, false, false);
            ctx.ArcTo(end, new Size(r, r), 0, angle > 180, SweepDirection.Clockwise, true, false);
        }
        g.Freeze();
        return g;
    }
}
