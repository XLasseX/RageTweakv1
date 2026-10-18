using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Microsoft.Win32;
using FortniteTweakTool.Models;
using FortniteTweakTool.Services;

namespace FortniteTweakTool;

/// <summary>Eine Zeile in der Farbliste (Name, Hex-Wert, Vorschau-Pinsel).</summary>
public sealed class ColorEntry : INotifyPropertyChanged
{
    private string _hex = "";
    private Brush _brush = Brushes.Black;

    public string Key { get; init; } = "";
    public string Label { get; init; } = "";

    public string Hex
    {
        get => _hex;
        private set { _hex = value; OnChanged(); }
    }

    public Brush Brush
    {
        get => _brush;
        private set { _brush = value; OnChanged(); }
    }

    public void Update(string hex)
    {
        Hex = hex;
        Brush = new SolidColorBrush(ThemeManager.ParseColor(hex, Colors.Black));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public partial class MainWindow
{
    private static readonly (string Key, string Label)[] ColorKeys =
    {
        ("bg", "Hintergrund"),
        ("panel", "Panels / Karten"),
        ("accent", "Akzentfarbe (Buttons, Glow)"),
        ("accent2", "Zweitfarbe (Verlauf, Highlights)"),
        ("text", "Textfarbe"),
        ("muted", "Gedämpfter Text"),
    };

    private static readonly string[] FontChoices =
        { "Segoe UI", "Bahnschrift", "Trebuchet MS", "Verdana", "Georgia", "Consolas", "Comic Sans MS" };

    private const string MediaHelp =
        "Keine Datei gewählt. Unterstützt: PNG, JPG, BMP, TIFF (Bilder) sowie MP4, WMV, AVI, MOV, MKV (Videos – laufen stumm in Dauerschleife).";

    private BackgroundEngine? _engine;
    private bool _uiReady;
    private readonly ObservableCollection<ColorEntry> _colorEntries = new();
    private readonly DispatcherTimer _saveTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };

    private static ThemeSettings T => ThemeManager.Current;

    // ====================== Initialisierung ======================

    private void InitDesign()
    {
        _engine = new BackgroundEngine(BackgroundHost);
        _engine.MediaProblem += msg => MediaInfo.Text = msg;

        _saveTimer.Tick += (_, _) => { _saveTimer.Stop(); ThemeManager.Save(); };
        Closed += (_, _) =>
        {
            _saveTimer.Stop();
            ThemeManager.Save();
            _engine?.Dispose();
        };
        SourceInitialized += (_, _) => UpdateTitleBar();

        ColorList.ItemsSource = _colorEntries;
        BuildPresetButtons();
        BuildFontChips();
        ThemePathInfo.Text = "Gespeichert in: " + ThemeManager.SettingsPath;

        SyncDesignUi();
        ApplyDesign(save: false);
        _uiReady = true;
    }

    private void BuildPresetButtons()
    {
        PresetPanel.Children.Clear();
        foreach (var p in ThemeManager.Presets)
        {
            var swatch = new Border
            {
                Width = 40,
                Height = 18,
                CornerRadius = new CornerRadius(5),
                Margin = new Thickness(0, 0, 10, 0),
                Background = new LinearGradientBrush(
                    ThemeManager.ParseColor(p.Accent, Colors.Purple),
                    ThemeManager.ParseColor(p.Accent2, Colors.Cyan), 0)
            };
            var content = new StackPanel { Orientation = Orientation.Horizontal };
            content.Children.Add(swatch);
            content.Children.Add(new TextBlock { Text = p.Name, VerticalAlignment = VerticalAlignment.Center });

            var preset = p;
            var button = new Button { Content = content, Margin = new Thickness(0, 0, 8, 8) };
            button.Click += (_, _) => ApplyPreset(preset);
            PresetPanel.Children.Add(button);
        }
    }

    private void BuildFontChips()
    {
        FontPanel.Children.Clear();
        var chipStyle = (Style)FindResource("ChipStyle");
        foreach (var name in FontChoices)
        {
            var chip = new RadioButton
            {
                Content = name,
                Tag = name,
                GroupName = "FontChoice",
                Style = chipStyle,
                FontFamily = new FontFamily(name)
            };
            chip.Checked += Font_Checked;
            FontPanel.Children.Add(chip);
        }
    }

    // ====================== Einstellungen ⇄ Oberfläche ======================

    private void SyncDesignUi()
    {
        bool wasReady = _uiReady;
        _uiReady = false;
        var s = T;

        ModeSolid.IsChecked = s.Mode == BackgroundMode.Solid;
        ModeGradient.IsChecked = s.Mode == BackgroundMode.AnimatedGradient;
        ModeAurora.IsChecked = s.Mode == BackgroundMode.Aurora;
        ModeCustom.IsChecked = s.Mode == BackgroundMode.Custom;

        SldIntensity.Value = s.EffectIntensity * 100;
        SldSpeed.Value = s.AnimationSpeed * 100;
        SldDim.Value = s.Dim * 100;
        SldBlur.Value = s.ImageBlur;

        ChkParticles.IsChecked = s.Particles;
        SldParticles.Value = s.ParticleCount;
        PartFloat.IsChecked = s.ParticleStyle == ParticleStyle.Float;
        PartSnow.IsChecked = s.ParticleStyle == ParticleStyle.Snow;
        PartDrift.IsChecked = s.ParticleStyle == ParticleStyle.Drift;

        SldPanelOpacity.Value = s.PanelOpacity * 100;
        SldRadius.Value = s.CornerRadius;
        SldGlow.Value = s.GlowStrength * 100;
        ChkPageAnim.IsChecked = s.PageAnimation;
        SldScale.Value = s.UiScale * 100;
        SldWinOpacity.Value = s.WindowOpacity * 100;
        SldBorder.Value = s.BorderStrength * 100;
        ChkTopMost.IsChecked = s.AlwaysOnTop;

        foreach (var child in FontPanel.Children.OfType<RadioButton>())
            child.IsChecked = (string?)child.Tag == s.FontFamily;

        RefreshColorEntries();
        MediaInfo.Text = string.IsNullOrWhiteSpace(s.MediaPath) ? MediaHelp : "Aktiv: " + s.MediaPath;

        _uiReady = wasReady;
    }

    private void ReadControls()
    {
        var s = T;
        s.EffectIntensity = SldIntensity.Value / 100;
        s.AnimationSpeed = SldSpeed.Value / 100;
        s.Dim = SldDim.Value / 100;
        s.ImageBlur = SldBlur.Value;
        s.Particles = ChkParticles.IsChecked == true;
        s.ParticleCount = (int)SldParticles.Value;
        s.PanelOpacity = SldPanelOpacity.Value / 100;
        s.CornerRadius = SldRadius.Value;
        s.GlowStrength = SldGlow.Value / 100;
        s.PageAnimation = ChkPageAnim.IsChecked == true;
        s.UiScale = SldScale.Value / 100;
        s.WindowOpacity = SldWinOpacity.Value / 100;
        s.BorderStrength = SldBorder.Value / 100;
        s.AlwaysOnTop = ChkTopMost.IsChecked == true;
    }

    private void ApplyDesign(bool save = true)
    {
        ThemeManager.ApplyResources();
        _engine?.Apply(T);
        try { FontFamily = new FontFamily(T.FontFamily); } catch { }
        UpdateTitleBar();

        var scale = Math.Clamp(T.UiScale, 0.8, 1.4);
        RootGrid.LayoutTransform = new ScaleTransform(scale, scale);
        Opacity = Math.Clamp(T.WindowOpacity, 0.5, 1.0);
        Topmost = T.AlwaysOnTop;
        OverlayManager.Refresh();

        if (save)
        {
            _saveTimer.Stop();
            _saveTimer.Start();
        }
    }

    // ====================== Farben ======================

    private static string GetColor(string key) => key switch
    {
        "bg" => T.BackgroundColor,
        "panel" => T.PanelColor,
        "accent" => T.AccentColor,
        "accent2" => T.Accent2Color,
        "text" => T.TextColor,
        _ => T.MutedColor
    };

    private static void SetColor(string key, string hex)
    {
        switch (key)
        {
            case "bg": T.BackgroundColor = hex; break;
            case "panel": T.PanelColor = hex; break;
            case "accent": T.AccentColor = hex; break;
            case "accent2": T.Accent2Color = hex; break;
            case "text": T.TextColor = hex; break;
            default: T.MutedColor = hex; break;
        }
    }

    private void RefreshColorEntries()
    {
        if (_colorEntries.Count == 0)
        {
            foreach (var (key, label) in ColorKeys)
                _colorEntries.Add(new ColorEntry { Key = key, Label = label });
        }
        foreach (var entry in _colorEntries)
            entry.Update(GetColor(entry.Key));
    }

    private void OpenPicker(string key)
    {
        var current = ThemeManager.ParseColor(GetColor(key), Colors.Black);
        var label = ColorKeys.First(k => k.Key == key).Label;
        var dialog = new ColorPickerWindow(label, current, c =>
        {
            SetColor(key, ThemeManager.ToHex(c));
            ApplyDesign();
        })
        { Owner = this };
        dialog.ShowDialog();
        RefreshColorEntries();
    }

    private void PickColor_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string key }) OpenPicker(key);
    }

    private void Swatch_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string key }) OpenPicker(key);
    }

    private void CommitHex(TextBox box)
    {
        if (box.Tag is not string key) return;
        var text = box.Text.Trim();
        if (!text.StartsWith('#')) text = "#" + text;

        if (ThemeManager.TryParseColor(text, out var color))
        {
            SetColor(key, ThemeManager.ToHex(color));
            ApplyDesign();
        }
        // Gültigen Wert (oder bei Fehleingabe den alten) wieder anzeigen
        var entry = _colorEntries.FirstOrDefault(c => c.Key == key);
        entry?.Update(GetColor(key));
        box.Text = GetColor(key);
    }

    private void HexBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox box) CommitHex(box);
    }

    private void HexBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && sender is TextBox box)
            box.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
    }

    // ====================== Hintergrund / Regler ======================

    private void Mode_Checked(object sender, RoutedEventArgs e)
    {
        if (!_uiReady) return;
        if (sender is not RadioButton { Tag: string tag } || !Enum.TryParse<BackgroundMode>(tag, out var mode)) return;

        T.Mode = mode;
        if (mode == BackgroundMode.Custom && string.IsNullOrWhiteSpace(T.MediaPath))
        {
            PickMedia();
            return;
        }
        ApplyDesign();
    }

    private void ParticleStyle_Checked(object sender, RoutedEventArgs e)
    {
        if (!_uiReady) return;
        if (sender is RadioButton { Tag: string tag } && Enum.TryParse<ParticleStyle>(tag, out var style))
        {
            T.ParticleStyle = style;
            ApplyDesign();
        }
    }

    private void Font_Checked(object sender, RoutedEventArgs e)
    {
        if (!_uiReady) return;
        if (sender is RadioButton { Tag: string name })
        {
            T.FontFamily = name;
            ApplyDesign();
        }
    }

    private void Slider_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_uiReady) return;
        ReadControls();
        ApplyDesign();
    }

    private void Toggle_Changed(object sender, RoutedEventArgs e)
    {
        if (!_uiReady) return;
        ReadControls();
        ApplyDesign();
    }

    // ====================== Eigenes Bild / Video ======================

    private void PickMedia_Click(object sender, RoutedEventArgs e) => PickMedia();

    private void PickMedia()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Hintergrund-Bild oder -Video wählen",
            Filter = "Bilder und Videos|*.png;*.jpg;*.jpeg;*.bmp;*.tif;*.tiff;*.mp4;*.m4v;*.wmv;*.avi;*.mov;*.mkv" +
                     "|Bilder|*.png;*.jpg;*.jpeg;*.bmp;*.tif;*.tiff" +
                     "|Videos|*.mp4;*.m4v;*.wmv;*.avi;*.mov;*.mkv"
        };

        if (dialog.ShowDialog(this) == true)
        {
            T.MediaPath = dialog.FileName;
            T.Mode = BackgroundMode.Custom;
            SyncDesignUi();
            ApplyDesign();
        }
        else
        {
            // Abgebrochen und noch nichts gewählt → zurück auf den Farbverlauf
            if (string.IsNullOrWhiteSpace(T.MediaPath) && T.Mode == BackgroundMode.Custom)
            {
                T.Mode = BackgroundMode.AnimatedGradient;
                SyncDesignUi();
                ApplyDesign();
            }
        }
    }

    private void ClearMedia_Click(object sender, RoutedEventArgs e)
    {
        T.MediaPath = null;
        if (T.Mode == BackgroundMode.Custom) T.Mode = BackgroundMode.AnimatedGradient;
        SyncDesignUi();
        ApplyDesign();
    }

    // ====================== Vorlagen / Import / Export ======================

    private void ApplyPreset(ThemePreset p)
    {
        var s = T;
        s.BackgroundColor = p.Bg;
        s.PanelColor = p.Panel;
        s.AccentColor = p.Accent;
        s.Accent2Color = p.Accent2;
        s.TextColor = p.Text;
        s.MutedColor = p.Muted;
        s.Mode = p.Mode;
        s.EffectIntensity = p.Intensity;
        s.Particles = p.Particles;
        s.ParticleStyle = p.PStyle;
        s.GlowStrength = p.Glow;

        SyncDesignUi();
        ApplyDesign();
    }

    private void ExportTheme_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Design exportieren",
            FileName = "FortniteTweakTool-Design.json",
            Filter = "Design-Datei (*.json)|*.json"
        };
        if (dialog.ShowDialog(this) != true) return;

        try
        {
            File.WriteAllText(dialog.FileName, JsonSerializer.Serialize(T, ThemeSettings.JsonOptions));
            MessageBox.Show("Design exportiert:\n" + dialog.FileName, "Design", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show("Export fehlgeschlagen:\n" + ex.Message, "Design", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ImportTheme_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "Design importieren", Filter = "Design-Datei (*.json)|*.json" };
        if (dialog.ShowDialog(this) != true) return;

        try
        {
            var imported = JsonSerializer.Deserialize<ThemeSettings>(File.ReadAllText(dialog.FileName), ThemeSettings.JsonOptions);
            if (imported == null) throw new InvalidDataException("Datei enthält keine Design-Daten.");
            ThemeManager.Replace(imported);
            SyncDesignUi();
            ApplyDesign();
        }
        catch (Exception ex)
        {
            MessageBox.Show("Import fehlgeschlagen:\n" + ex.Message, "Design", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ResetTheme_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show("Alle Design-Einstellungen auf den Standard zurücksetzen?", "Design",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

        ThemeManager.Replace(new ThemeSettings());
        SyncDesignUi();
        ApplyDesign();
    }

    private void Design_Click(object sender, RoutedEventArgs e) =>
        ShowPage("Design & Effekte", "Farben, animierte Hintergründe, eigene Bilder/Videos und Effekte – alles live.", "", design: true);

    // ====================== Seitenwechsel-Animation ======================

    private void PlayPageAnimation()
    {
        if (!T.PageAnimation) return;

        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        PageHost.BeginAnimation(UIElement.OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(280)) { EasingFunction = ease });

        if (PageHost.RenderTransform is TranslateTransform move)
            move.BeginAnimation(TranslateTransform.YProperty,
                new DoubleAnimation(18, 0, TimeSpan.FromMilliseconds(320)) { EasingFunction = ease });
    }

    // ====================== Fensterleiste an Design anpassen ======================

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    private void UpdateTitleBar()
    {
        try
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero) return;

            var bg = ThemeManager.ParseColor(T.BackgroundColor, Colors.Black);
            int dark = ThemeManager.Luminance(bg) < 0.5 ? 1 : 0;
            DwmSetWindowAttribute(hwnd, 20, ref dark, sizeof(int));             // dunkle Titelleiste

            int caption = bg.R | (bg.G << 8) | (bg.B << 16);                    // Windows 11: Farbe der Titelleiste
            DwmSetWindowAttribute(hwnd, 35, ref caption, sizeof(int));
            var fg = ThemeManager.Contrast(bg);
            int text = fg.R | (fg.G << 8) | (fg.B << 16);
            DwmSetWindowAttribute(hwnd, 36, ref text, sizeof(int));
        }
        catch { /* ältere Windows-Versionen unterstützen das nicht – dann bleibt die Standard-Leiste */ }
    }
}
