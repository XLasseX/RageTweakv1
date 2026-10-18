using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using FortniteTweakTool.Models;
using FortniteTweakTool.Services;

namespace FortniteTweakTool;

/// <summary>
/// Baut hinter der Oberfläche mehrere Ebenen auf:
/// Farbe → animierter Verlauf → Aurora → Bild/Video → Abdunklung → Partikel.
/// </summary>
public sealed class BackgroundEngine : IDisposable
{
    private sealed class Particle
    {
        public Ellipse Shape = null!;
        public TranslateTransform T = null!;
        public double X, Y, VX, VY, Sway, Phase, Twinkle, BaseAlpha;
    }

    private static readonly string[] VideoExtensions = { ".mp4", ".m4v", ".wmv", ".avi", ".mov", ".mkv" };

    private readonly Grid _host;
    private readonly Rectangle _solid = new();
    private readonly Rectangle _gradient = new();
    private readonly Grid _aurora = new();
    private readonly Border _image = new();
    private readonly MediaElement _video = new();
    private readonly Rectangle _dim = new() { Fill = Brushes.Black };
    private readonly Canvas _particleCanvas = new();

    private readonly List<Particle> _particles = new();
    private readonly Random _rng = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private double _lastTime;
    private bool _rendering;
    private double _speed = 1.0;

    private string? _gradientSig, _auroraSig, _particleSig, _imagePath, _videoPath;

    public event Action<string>? MediaProblem;

    public BackgroundEngine(Grid host)
    {
        _host = host;
        _host.ClipToBounds = true;

        _video.LoadedBehavior = MediaState.Manual;
        _video.UnloadedBehavior = MediaState.Manual;
        _video.IsMuted = true;
        _video.Volume = 0;
        _video.Stretch = Stretch.UniformToFill;
        _video.MediaEnded += (_, _) => { _video.Position = TimeSpan.Zero; _video.Play(); };
        _video.MediaFailed += (_, e) =>
        {
            _video.Visibility = Visibility.Collapsed;
            MediaProblem?.Invoke("Video konnte nicht abgespielt werden: " + e.ErrorException?.Message);
        };

        _particleCanvas.ClipToBounds = true;

        foreach (UIElement el in new UIElement[] { _solid, _gradient, _aurora, _image, _video, _dim, _particleCanvas })
        {
            el.IsHitTestVisible = false;
            el.Visibility = Visibility.Collapsed;
            _host.Children.Add(el);
        }
        _solid.Visibility = Visibility.Visible;
    }

    public static bool IsVideo(string path) =>
        VideoExtensions.Contains(System.IO.Path.GetExtension(path).ToLowerInvariant());

    public void Apply(ThemeSettings s)
    {
        var bg = ThemeManager.ParseColor(s.BackgroundColor, Color.FromRgb(13, 15, 26));
        var a1 = ThemeManager.ParseColor(s.AccentColor, Color.FromRgb(123, 97, 255));
        var a2 = ThemeManager.ParseColor(s.Accent2Color, Color.FromRgb(0, 212, 255));
        _speed = Math.Clamp(s.AnimationSpeed, 0.1, 4.0);
        double k = Math.Clamp(s.EffectIntensity, 0, 1);

        _solid.Fill = new SolidColorBrush(bg);

        bool hasMedia = s.Mode == BackgroundMode.Custom
                        && !string.IsNullOrWhiteSpace(s.MediaPath)
                        && File.Exists(s.MediaPath);
        bool gradient = s.Mode == BackgroundMode.AnimatedGradient || (s.Mode == BackgroundMode.Custom && !hasMedia);
        bool aurora = s.Mode == BackgroundMode.Aurora;

        UpdateGradient(gradient, bg, a1, a2, k);
        UpdateAurora(aurora, a1, a2, k);
        UpdateMedia(s, hasMedia);
        UpdateParticles(s, a1, a2);
    }

    // ---------- Animierter Farbverlauf ----------

    private void UpdateGradient(bool on, Color bg, Color a1, Color a2, double k)
    {
        if (!on)
        {
            if (_gradientSig != null) { _gradient.Fill = null; _gradient.Visibility = Visibility.Collapsed; _gradientSig = null; }
            return;
        }

        string sig = $"{bg}|{a1}|{a2}|{k:F2}|{_speed:F2}";
        if (sig == _gradientSig) return;
        _gradientSig = sig;

        double mix = 0.25 + 0.55 * k;
        var c1 = ThemeManager.Blend(bg, a1, mix);
        var c2 = ThemeManager.Blend(bg, a2, mix);

        var brush = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
        var s0 = new GradientStop(bg, 0.0);
        var s1 = new GradientStop(c1, 0.30);
        var s2 = new GradientStop(c2, 0.70);
        var s3 = new GradientStop(bg, 1.0);
        brush.GradientStops.Add(s0);
        brush.GradientStops.Add(s1);
        brush.GradientStops.Add(s2);
        brush.GradientStops.Add(s3);

        var rotation = new RotateTransform(0, 0.5, 0.5);
        brush.RelativeTransform = rotation;

        var ease = new SineEase { EasingMode = EasingMode.EaseInOut };
        rotation.BeginAnimation(RotateTransform.AngleProperty,
            new DoubleAnimation(0, 360, TimeSpan.FromSeconds(45 / _speed)) { RepeatBehavior = RepeatBehavior.Forever });
        s1.BeginAnimation(GradientStop.OffsetProperty,
            new DoubleAnimation(0.15, 0.42, TimeSpan.FromSeconds(9 / _speed)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = ease });
        s2.BeginAnimation(GradientStop.OffsetProperty,
            new DoubleAnimation(0.85, 0.58, TimeSpan.FromSeconds(12 / _speed)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = ease });

        _gradient.Fill = brush;
        _gradient.Visibility = Visibility.Visible;
    }

    // ---------- Aurora ----------

    private void UpdateAurora(bool on, Color a1, Color a2, double k)
    {
        if (!on)
        {
            if (_auroraSig != null) { _aurora.Children.Clear(); _aurora.Visibility = Visibility.Collapsed; _auroraSig = null; }
            return;
        }

        string sig = $"{a1}|{a2}|{k:F2}|{_speed:F2}";
        if (sig == _auroraSig) return;
        _auroraSig = sig;

        _aurora.Children.Clear();
        double alpha = 0.25 + 0.55 * k;
        var mid = ThemeManager.Blend(a1, a2, 0.5);

        var blobs = new (Color col, HorizontalAlignment ha, VerticalAlignment va, double w, double h, double dx, double dy, double sec)[]
        {
            (a1,  HorizontalAlignment.Left,   VerticalAlignment.Top,     900, 650,  240,  150, 22),
            (a2,  HorizontalAlignment.Right,  VerticalAlignment.Bottom,  950, 700, -260, -130, 28),
            (mid, HorizontalAlignment.Center, VerticalAlignment.Center,  700, 520,  200, -170, 34),
        };

        var ease = new SineEase { EasingMode = EasingMode.EaseInOut };
        foreach (var b in blobs)
        {
            var fill = new RadialGradientBrush();
            fill.GradientStops.Add(new GradientStop(ThemeManager.WithAlpha(b.col, alpha), 0.0));
            fill.GradientStops.Add(new GradientStop(ThemeManager.WithAlpha(b.col, alpha * 0.35), 0.55));
            fill.GradientStops.Add(new GradientStop(ThemeManager.WithAlpha(b.col, 0), 1.0));

            var move = new TranslateTransform();
            var blob = new Ellipse
            {
                Width = b.w,
                Height = b.h,
                Fill = fill,
                HorizontalAlignment = b.ha,
                VerticalAlignment = b.va,
                Margin = new Thickness(-b.w * 0.3, -b.h * 0.3, -b.w * 0.3, -b.h * 0.3),
                RenderTransform = move,
                IsHitTestVisible = false
            };
            move.BeginAnimation(TranslateTransform.XProperty,
                new DoubleAnimation(0, b.dx, TimeSpan.FromSeconds(b.sec / _speed)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = ease });
            move.BeginAnimation(TranslateTransform.YProperty,
                new DoubleAnimation(0, b.dy, TimeSpan.FromSeconds(b.sec * 1.3 / _speed)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = ease });
            _aurora.Children.Add(blob);
        }
        _aurora.Visibility = Visibility.Visible;
    }

    // ---------- Eigenes Bild / Video ----------

    private void UpdateMedia(ThemeSettings s, bool hasMedia)
    {
        _dim.Visibility = hasMedia ? Visibility.Visible : Visibility.Collapsed;
        _dim.Opacity = Math.Clamp(s.Dim, 0, 0.9);

        if (!hasMedia)
        {
            StopVideo();
            _image.Visibility = Visibility.Collapsed;
            _image.Background = null;
            _imagePath = null;
            return;
        }

        string path = s.MediaPath!;
        if (IsVideo(path))
        {
            _image.Visibility = Visibility.Collapsed;
            _image.Background = null;
            _imagePath = null;

            if (_videoPath != path)
            {
                _videoPath = path;
                try
                {
                    _video.Source = new Uri(path);
                }
                catch (Exception ex)
                {
                    MediaProblem?.Invoke("Video konnte nicht geladen werden: " + ex.Message);
                    return;
                }
            }
            _video.Visibility = Visibility.Visible;
            _video.Play();
            return;
        }

        StopVideo();
        if (_imagePath != path)
        {
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;          // Datei wird nicht gesperrt
                bmp.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
                bmp.UriSource = new Uri(path);
                bmp.EndInit();
                bmp.Freeze();
                _image.Background = new ImageBrush(bmp) { Stretch = Stretch.UniformToFill };
                _imagePath = path;
            }
            catch (Exception ex)
            {
                _image.Background = null;
                _imagePath = null;
                MediaProblem?.Invoke("Bild konnte nicht geladen werden: " + ex.Message);
                return;
            }
        }

        double blur = Math.Clamp(s.ImageBlur, 0, 40);
        _image.Effect = blur > 0 ? new BlurEffect { Radius = blur, RenderingBias = RenderingBias.Performance } : null;
        _image.Margin = new Thickness(-blur);   // verhindert helle Ränder durch den Blur
        _image.Visibility = Visibility.Visible;
    }

    private void StopVideo()
    {
        if (_videoPath == null && _video.Source == null) { _video.Visibility = Visibility.Collapsed; return; }
        try { _video.Stop(); } catch { }
        _video.Source = null;
        _videoPath = null;
        _video.Visibility = Visibility.Collapsed;
    }

    // ---------- Partikel ----------

    private void UpdateParticles(ThemeSettings s, Color a1, Color a2)
    {
        int count = Math.Clamp(s.ParticleCount, 0, 400);
        bool on = s.Particles && count > 0;
        string sig = $"{on}|{count}|{s.ParticleStyle}|{a1}|{a2}";
        if (sig == _particleSig) return;
        _particleSig = sig;

        StopRendering();
        _particleCanvas.Children.Clear();
        _particles.Clear();

        if (!on) { _particleCanvas.Visibility = Visibility.Collapsed; return; }

        double w = _host.ActualWidth > 100 ? _host.ActualWidth : 1260;
        double h = _host.ActualHeight > 100 ? _host.ActualHeight : 820;
        var brush1 = new SolidColorBrush(a1); brush1.Freeze();
        var brush2 = new SolidColorBrush(a2); brush2.Freeze();

        for (int i = 0; i < count; i++)
        {
            double size = 2 + _rng.NextDouble() * 4;
            var move = new TranslateTransform();
            var shape = new Ellipse
            {
                Width = size,
                Height = size,
                Fill = i % 2 == 0 ? brush1 : brush2,
                RenderTransform = move,
                IsHitTestVisible = false
            };

            var p = new Particle
            {
                Shape = shape,
                T = move,
                X = _rng.NextDouble() * w,
                Y = _rng.NextDouble() * h,
                Phase = _rng.NextDouble() * Math.PI * 2,
                Twinkle = 0.8 + _rng.NextDouble() * 1.8,
                BaseAlpha = 0.35 + _rng.NextDouble() * 0.55,
                Sway = 4 + _rng.NextDouble() * 10
            };

            switch (s.ParticleStyle)
            {
                case ParticleStyle.Snow:
                    p.VY = 18 + _rng.NextDouble() * 45;
                    p.VX = (_rng.NextDouble() - 0.5) * 14;
                    break;
                case ParticleStyle.Drift:
                    p.VX = (_rng.NextDouble() - 0.5) * 50;
                    p.VY = (_rng.NextDouble() - 0.5) * 50;
                    break;
                default: // Float
                    p.VY = -(12 + _rng.NextDouble() * 38);
                    p.VX = (_rng.NextDouble() - 0.5) * 10;
                    break;
            }

            move.X = p.X;
            move.Y = p.Y;
            _particles.Add(p);
            _particleCanvas.Children.Add(shape);
        }

        _particleCanvas.Visibility = Visibility.Visible;
        StartRendering();
    }

    private void StartRendering()
    {
        if (_rendering) return;
        _lastTime = _clock.Elapsed.TotalSeconds;
        CompositionTarget.Rendering += OnRendering;
        _rendering = true;
    }

    private void StopRendering()
    {
        if (!_rendering) return;
        CompositionTarget.Rendering -= OnRendering;
        _rendering = false;
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        double now = _clock.Elapsed.TotalSeconds;
        double dt = Math.Min(now - _lastTime, 0.1);
        _lastTime = now;

        double w = _particleCanvas.ActualWidth, h = _particleCanvas.ActualHeight;
        if (w < 1 || h < 1) return;

        foreach (var p in _particles)
        {
            p.X += (p.VX + Math.Sin(now * 0.6 + p.Phase) * p.Sway) * dt * _speed;
            p.Y += p.VY * dt * _speed;

            if (p.X < -10) p.X = w + 10; else if (p.X > w + 10) p.X = -10;
            if (p.Y < -10) p.Y = h + 10; else if (p.Y > h + 10) p.Y = -10;

            p.T.X = p.X;
            p.T.Y = p.Y;
            p.Shape.Opacity = p.BaseAlpha * (0.55 + 0.45 * Math.Sin(now * p.Twinkle + p.Phase));
        }
    }

    public void Dispose()
    {
        StopRendering();
        StopVideo();
    }
}
