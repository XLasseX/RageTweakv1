using System.Text.Json;
using System.Text.Json.Serialization;

namespace FortniteTweakTool.Models;

public enum BackgroundMode { Solid, AnimatedGradient, Aurora, Custom }

public enum ParticleStyle { Float, Snow, Drift }

/// <summary>Alle vom Benutzer einstellbaren Design-Optionen. Wird als theme.json gespeichert.</summary>
public sealed class ThemeSettings
{
    // Farben (Hex)
    public string BackgroundColor { get; set; } = "#0D0F1A";
    public string PanelColor { get; set; } = "#171A2B";
    public string AccentColor { get; set; } = "#7B61FF";
    public string Accent2Color { get; set; } = "#00D4FF";
    public string TextColor { get; set; } = "#F2F4FF";
    public string MutedColor { get; set; } = "#9AA3C7";

    // Hintergrund
    public BackgroundMode Mode { get; set; } = BackgroundMode.AnimatedGradient;
    public string? MediaPath { get; set; }
    public double EffectIntensity { get; set; } = 0.6;   // 0..1
    public double AnimationSpeed { get; set; } = 1.0;    // 0.1..3
    public double Dim { get; set; } = 0.30;              // 0..0.9 (nur Bild/Video)
    public double ImageBlur { get; set; } = 0;           // px (nur Bild)

    // Partikel
    public bool Particles { get; set; } = true;
    public int ParticleCount { get; set; } = 60;
    public ParticleStyle ParticleStyle { get; set; } = ParticleStyle.Float;

    // Oberfläche
    public double PanelOpacity { get; set; } = 0.85;     // 0.2..1
    public double CornerRadius { get; set; } = 14;       // px
    public double GlowStrength { get; set; } = 0.45;     // 0..1
    public bool PageAnimation { get; set; } = true;
    public string FontFamily { get; set; } = "Segoe UI";
    public double UiScale { get; set; } = 1.0;           // 0.8..1.4 (Gesamtgröße der Oberfläche)
    public double WindowOpacity { get; set; } = 1.0;     // 0.5..1 (Fenster-Transparenz)
    public double BorderStrength { get; set; } = 0.35;   // 0..1 (Rahmen um Panels)
    public bool AlwaysOnTop { get; set; } = false;

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };
}
