using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;
using FortniteTweakTool.Models;

namespace FortniteTweakTool.Services;

public sealed record ThemePreset(
    string Name, string Bg, string Panel, string Accent, string Accent2, string Text, string Muted,
    BackgroundMode Mode, double Intensity, bool Particles, ParticleStyle PStyle, double Glow);

public static class ThemeManager
{
    public static ThemeSettings Current { get; private set; } = new();

    public static string SettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FortniteTweakTool", "theme.json");

    public static readonly ThemePreset[] Presets =
    {
        new("Standard Violett", "#0D0F1A", "#171A2B", "#7B61FF", "#00D4FF", "#F2F4FF", "#9AA3C7", BackgroundMode.AnimatedGradient, 0.60, true,  ParticleStyle.Float, 0.45),
        new("Neon Cyber",       "#0A0612", "#160B26", "#FF2BD6", "#00F0FF", "#FDF2FF", "#B49AD0", BackgroundMode.Aurora,          0.75, true,  ParticleStyle.Drift, 0.70),
        new("Ozean",            "#04101C", "#0A1D30", "#1E90FF", "#2DE2C4", "#EAF6FF", "#8FB0C8", BackgroundMode.Aurora,          0.60, true,  ParticleStyle.Float, 0.45),
        new("Sonnenuntergang",  "#160A12", "#26111C", "#FF6B35", "#FFC857", "#FFF3EA", "#D1A596", BackgroundMode.AnimatedGradient, 0.70, true,  ParticleStyle.Float, 0.50),
        new("Matrix",           "#020A04", "#07140A", "#00FF66", "#7CFFB2", "#D8FFE6", "#6FB58A", BackgroundMode.Solid,            0.40, true,  ParticleStyle.Snow,  0.55),
        new("Blutrot",          "#0F0507", "#1C0A0E", "#FF2D4A", "#FF8A00", "#FFEFF1", "#C99AA2", BackgroundMode.AnimatedGradient, 0.55, true,  ParticleStyle.Drift, 0.50),
        new("Eis (hell)",       "#E9EEF8", "#FFFFFF", "#3B6CFF", "#00B7D4", "#1A2033", "#5C6685", BackgroundMode.AnimatedGradient, 0.35, false, ParticleStyle.Float, 0.20),
        new("Graphit (ruhig)",  "#121212", "#1C1C1C", "#4C9AFF", "#8AB4F8", "#EDEDED", "#9A9A9A", BackgroundMode.Solid,            0.00, false, ParticleStyle.Float, 0.00),
        new("Synthwave",        "#0E0220", "#1B0A38", "#FF4FD8", "#4FD8FF", "#FFEFFF", "#B79AD8", BackgroundMode.Aurora,          0.80, true,  ParticleStyle.Drift, 0.75),
        new("Wald",             "#07120B", "#0F2016", "#3DDC84", "#C6F432", "#EAFBEF", "#8DB89B", BackgroundMode.Aurora,          0.50, true,  ParticleStyle.Float, 0.35),
        new("Gold Luxus",       "#0E0C07", "#1B170C", "#E6B422", "#FFE08A", "#FFF8E6", "#BFA66B", BackgroundMode.AnimatedGradient, 0.45, true,  ParticleStyle.Float, 0.50),
        new("Rage Schwarz-Rot", "#050505", "#121212", "#FF1E1E", "#FF6A00", "#FFFFFF", "#A0A0A0", BackgroundMode.AnimatedGradient, 0.50, true,  ParticleStyle.Drift, 0.65),
        new("Tiefsee",          "#02080F", "#071723", "#00B4D8", "#90E0EF", "#E6F7FF", "#7FA6BA", BackgroundMode.Aurora,          0.65, true,  ParticleStyle.Float, 0.50),
        new("Lavendel (hell)",  "#F3EEFF", "#FFFFFF", "#7B4DFF", "#FF7AC8", "#241B3A", "#6D6488", BackgroundMode.AnimatedGradient, 0.35, false, ParticleStyle.Float, 0.20),
        new("Mono Weiß",        "#F6F6F6", "#FFFFFF", "#111111", "#555555", "#111111", "#6B6B6B", BackgroundMode.Solid,            0.00, false, ParticleStyle.Float, 0.10),
    };

    // ---------- Laden / Speichern ----------

    public static void Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
                Current = JsonSerializer.Deserialize<ThemeSettings>(File.ReadAllText(SettingsPath), ThemeSettings.JsonOptions) ?? new ThemeSettings();
        }
        catch (Exception ex)
        {
            Current = new ThemeSettings();
            try { TweakService.Log("Design-Einstellungen konnten nicht geladen werden: " + ex.Message); } catch { }
        }
    }

    public static void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(Current, ThemeSettings.JsonOptions));
        }
        catch (Exception ex)
        {
            try { TweakService.Log("Design-Einstellungen konnten nicht gespeichert werden: " + ex.Message); } catch { }
        }
    }

    public static void Replace(ThemeSettings settings) => Current = settings;

    // ---------- Farb-Helfer ----------

    public static bool TryParseColor(string? text, out Color color)
    {
        color = Colors.Black;
        if (string.IsNullOrWhiteSpace(text)) return false;
        try
        {
            if (ColorConverter.ConvertFromString(text.Trim()) is Color c)
            {
                color = Color.FromRgb(c.R, c.G, c.B);
                return true;
            }
        }
        catch { }
        return false;
    }

    public static Color ParseColor(string? text, Color fallback) => TryParseColor(text, out var c) ? c : fallback;

    public static string ToHex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    public static Color Blend(Color a, Color b, double t)
    {
        t = Math.Clamp(t, 0, 1);
        return Color.FromRgb(
            (byte)Math.Round(a.R + (b.R - a.R) * t),
            (byte)Math.Round(a.G + (b.G - a.G) * t),
            (byte)Math.Round(a.B + (b.B - a.B) * t));
    }

    public static Color WithAlpha(Color c, double alpha01) =>
        Color.FromArgb((byte)Math.Round(Math.Clamp(alpha01, 0, 1) * 255), c.R, c.G, c.B);

    public static double Luminance(Color c) => (0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B) / 255.0;

    /// <summary>Gut lesbare Textfarbe (dunkel/hell) für einen Hintergrund.</summary>
    public static Color Contrast(Color background) =>
        Luminance(background) > 0.6 ? Color.FromRgb(0x12, 0x14, 0x1C) : Colors.White;

    private static SolidColorBrush Solid(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    // ---------- Resources der App aktualisieren ----------

    public static void ApplyResources()
    {
        var s = Current;
        var res = Application.Current.Resources;

        var bg = ParseColor(s.BackgroundColor, Color.FromRgb(13, 15, 26));
        var panel = ParseColor(s.PanelColor, Color.FromRgb(23, 26, 43));
        var accent = ParseColor(s.AccentColor, Color.FromRgb(123, 97, 255));
        var accent2 = ParseColor(s.Accent2Color, Color.FromRgb(0, 212, 255));
        var text = ParseColor(s.TextColor, Colors.White);
        var muted = ParseColor(s.MutedColor, Color.FromRgb(154, 163, 199));

        double a = Math.Clamp(s.PanelOpacity, 0.2, 1.0);
        var panel2 = Blend(panel, text, 0.07);
        var inset = Luminance(panel) < 0.5 ? Blend(panel, Colors.Black, 0.45) : Blend(panel, Colors.Black, 0.06);
        var button = Blend(panel, accent, 0.28);

        res["BgBrush"] = Solid(bg);
        res["PanelBrush"] = Solid(WithAlpha(panel, a));
        res["Panel2Brush"] = Solid(WithAlpha(panel2, a));
        res["InsetBrush"] = Solid(WithAlpha(inset, Math.Max(a, 0.6)));
        res["PanelBorderBrush"] = Solid(WithAlpha(accent, Math.Clamp(s.BorderStrength, 0, 1)));
        res["TextBrush"] = Solid(text);
        res["MutedBrush"] = Solid(muted);
        res["AccentBrush"] = Solid(accent);
        res["OnAccentBrush"] = Solid(Contrast(accent));
        res["Accent2Brush"] = Solid(accent2);
        res["BlueBrush"] = Solid(accent2);          // alter Name, wird weiterhin im XAML verwendet
        res["ButtonBrush"] = Solid(button);
        res["ButtonTextBrush"] = Solid(Contrast(button));

        var title = new LinearGradientBrush(accent, accent2, 0);
        title.Freeze();
        res["TitleBrush"] = title;

        double r = Math.Clamp(s.CornerRadius, 0, 40);
        res["PanelRadius"] = new CornerRadius(r);
        res["CardRadius"] = new CornerRadius(Math.Max(0, r * 0.7));
        res["ButtonRadius"] = new CornerRadius(Math.Max(2, r * 0.55));

        var glow = new DropShadowEffect { Color = accent, BlurRadius = 28, ShadowDepth = 0, Opacity = 1 };
        glow.Freeze();
        res["GlowEffect"] = glow;
        res["GlowOpacity"] = Math.Clamp(s.GlowStrength, 0, 1);
    }
}
