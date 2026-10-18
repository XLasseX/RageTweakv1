using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;

namespace FortniteTweakTool;

/// <summary>Eine Zeile im Dialog. Kind: "info" (Standard), "ok" oder "fail".</summary>
public sealed record DialogItem(string Title, string Detail = "", string Risk = "", string Kind = "info");

/// <summary>
/// Eigenes Bestätigungs- und Ergebnisfenster im Design der App (ersetzt die grauen Windows-MessageBoxen).
/// Zeigt eine Liste der Aktionen mit Risiko-Anzeige, Hinweisbändern (Wiederherstellungspunkt, UAC, Neustart)
/// und klaren Buttons. Alle Farben kommen aus dem aktuellen Design.
/// </summary>
public sealed class ActionDialog : Window
{
    private static readonly Color Green = Color.FromRgb(0x3D, 0xDC, 0x84);
    private static readonly Color Red = Color.FromRgb(0xFF, 0x5C, 0x5C);
    private static readonly Color Amber = Color.FromRgb(0xFF, 0xC6, 0x6D);

    private readonly Button _ok;

    // ------------------------------------------------------------------ Öffentliche Aufrufe

    /// <summary>Ja/Nein-Bestätigung mit Liste. Gibt true zurück, wenn bestätigt wurde.</summary>
    public static bool Confirm(Window? owner, string title, string subtitle, IEnumerable<DialogItem> items,
        string confirmText, string? note = null, string? warning = null)
    {
        var d = new ActionDialog(owner, title, subtitle, items.ToList(), confirmText, "Abbrechen", note, warning);
        return d.ShowDialog() == true;
    }

    /// <summary>Einfache Frage ohne Liste.</summary>
    public static bool Ask(Window? owner, string title, string message, string confirmText = "Ja")
        => Confirm(owner, title, message, Array.Empty<DialogItem>(), confirmText);

    /// <summary>Informations-/Ergebnisfenster mit nur einem "Schließen"-Button.</summary>
    public static void Info(Window? owner, string title, string subtitle, IEnumerable<DialogItem> items, string? warning = null)
    {
        var d = new ActionDialog(owner, title, subtitle, items.ToList(), "Schließen", null, null, warning);
        d.ShowDialog();
    }

    // ------------------------------------------------------------------ Aufbau

    private ActionDialog(Window? owner, string title, string subtitle, List<DialogItem> items,
        string confirmText, string? cancelText, string? note, string? warning)
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        SizeToContent = SizeToContent.Height;
        Width = 610;
        MaxHeight = Math.Max(420, SystemParameters.WorkArea.Height - 30);
        Title = title;
        Topmost = owner?.Topmost ?? false;

        if (owner is { IsVisible: true })
        {
            Owner = owner;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }
        else WindowStartupLocation = WindowStartupLocation.CenterScreen;

        // Äußerer Rand lässt Platz für den Schatten.
        var root = new Grid
        {
            Margin = new Thickness(20),
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = new ScaleTransform(0.95, 0.95)
        };

        var card = new Border
        {
            BorderThickness = new Thickness(1.5),
            Padding = new Thickness(24, 22, 24, 20),
            Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 30, ShadowDepth = 0, Opacity = 0.75 }
        };
        card.SetResourceReference(Border.BackgroundProperty, "BgBrush");
        card.SetResourceReference(Border.BorderBrushProperty, "AccentBrush");
        card.SetResourceReference(Border.CornerRadiusProperty, "PanelRadius");
        root.Children.Add(card);

        var stack = new StackPanel();
        card.Child = stack;

        // Kopf (zum Verschieben des Fensters)
        var header = new StackPanel { Background = Brushes.Transparent };
        var accentBar = new Border { Height = 4, Width = 46, CornerRadius = new CornerRadius(2), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 12) };
        accentBar.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
        header.Children.Add(accentBar);
        header.Children.Add(Text(title, 21, FontWeights.Bold, "TextBrush"));
        if (!string.IsNullOrWhiteSpace(subtitle))
        {
            var sub = Text(subtitle, 13, null, "MutedBrush");
            sub.Margin = new Thickness(0, 6, 0, 0);
            header.Children.Add(sub);
        }
        header.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ButtonState == MouseButtonState.Pressed) { try { DragMove(); } catch { } }
        };
        stack.Children.Add(header);

        // Liste
        if (items.Count > 0)
        {
            var list = new StackPanel();
            foreach (var it in items) list.Children.Add(BuildItem(it));
            stack.Children.Add(new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                MaxHeight = 330,
                Margin = new Thickness(0, 16, 0, 0),
                Padding = new Thickness(0, 0, 6, 0),
                Content = list
            });
        }

        // Hinweisbänder
        if (!string.IsNullOrWhiteSpace(note))
            stack.Children.Add(Band("ⓘ", note!, null));
        if (!string.IsNullOrWhiteSpace(warning))
            stack.Children.Add(Band("⟳", warning!, Amber));

        // Buttons
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0) };
        if (cancelText != null)
        {
            var cancel = new Button { Content = cancelText, Padding = new Thickness(20, 10, 20, 10), MinWidth = 110 };
            cancel.Click += (_, _) => DialogResult = false;
            buttons.Children.Add(cancel);
        }
        _ok = new Button { Content = confirmText, Padding = new Thickness(22, 10, 22, 10), MinWidth = 130 };
        _ok.SetResourceReference(Control.BackgroundProperty, "AccentBrush");
        _ok.SetResourceReference(Control.ForegroundProperty, "OnAccentBrush");
        _ok.Click += (_, _) => DialogResult = true;
        buttons.Children.Add(_ok);
        stack.Children.Add(buttons);

        Content = root;

        // Esc bricht ab (bzw. schließt das Info-Fenster); Enter löst den fokussierten Button aus.
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { DialogResult = cancelText == null; e.Handled = true; }
        };

        Loaded += (_, _) =>
        {
            _ok.Focus();
            var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180));
            BeginAnimation(OpacityProperty, fade);
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            var st = (ScaleTransform)root.RenderTransform;
            st.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.95, 1, TimeSpan.FromMilliseconds(220)) { EasingFunction = ease });
            st.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.95, 1, TimeSpan.FromMilliseconds(220)) { EasingFunction = ease });
        };
    }

    // ------------------------------------------------------------------ Bausteine

    private static TextBlock Text(string text, double size, FontWeight? weight, string brushKey)
    {
        var t = new TextBlock { Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap };
        if (weight != null) t.FontWeight = weight.Value;
        t.SetResourceReference(TextBlock.ForegroundProperty, brushKey);
        return t;
    }

    private static SolidColorBrush Tint(Color c, byte alpha) => new(Color.FromArgb(alpha, c.R, c.G, c.B));

    private static Color RiskColor(string riskLabel) =>
        riskLabel.Contains("mittel", StringComparison.OrdinalIgnoreCase) || riskLabel.Contains("hoch", StringComparison.OrdinalIgnoreCase)
            ? Amber : Green;

    private static Border BuildItem(DialogItem it)
    {
        var row = new Border { Margin = new Thickness(0, 0, 0, 8), ClipToBounds = true };
        row.SetResourceReference(Border.BackgroundProperty, "Panel2Brush");
        row.SetResourceReference(Border.CornerRadiusProperty, "CardRadius");

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(5) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var strip = new Border();
        if (it.Kind == "ok") strip.Background = new SolidColorBrush(Green);
        else if (it.Kind == "fail") strip.Background = new SolidColorBrush(Red);
        else strip.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
        grid.Children.Add(strip);

        var content = new StackPanel { Margin = new Thickness(14, 10, 12, 10) };
        Grid.SetColumn(content, 1);

        var titleRow = new DockPanel();
        if (it.Kind is "ok" or "fail")
        {
            var icon = new TextBlock
            {
                Text = it.Kind == "ok" ? "✔" : "✖",
                FontWeight = FontWeights.Bold, FontSize = 14, Margin = new Thickness(0, 0, 8, 0),
                Foreground = new SolidColorBrush(it.Kind == "ok" ? Green : Red),
                VerticalAlignment = VerticalAlignment.Top
            };
            DockPanel.SetDock(icon, Dock.Left);
            titleRow.Children.Add(icon);
        }
        titleRow.Children.Add(Text(it.Title, 14, FontWeights.Bold, "TextBrush"));
        content.Children.Add(titleRow);

        if (!string.IsNullOrWhiteSpace(it.Detail))
        {
            var d = Text(it.Detail, 12, null, "MutedBrush");
            d.Margin = new Thickness(0, 4, 0, 0);
            content.Children.Add(d);
        }

        if (!string.IsNullOrWhiteSpace(it.Risk))
        {
            // "Mittel; Strom/Hitze, fragt per UAC"  ->  Chip "Risiko: Mittel" + Rest als graue Zeile
            var parts = it.Risk.Split(';', 2);
            var label = parts[0].Trim();
            var rest = parts.Length > 1 ? parts[1].Trim() : "";
            var col = RiskColor(label);

            var wrap = new WrapPanel { Margin = new Thickness(0, 7, 0, 0) };
            var chip = new Border
            {
                Padding = new Thickness(9, 2, 9, 2), CornerRadius = new CornerRadius(9), BorderThickness = new Thickness(1),
                Background = Tint(col, 0x2A), BorderBrush = Tint(col, 0xAA), Margin = new Thickness(0, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            chip.Child = new TextBlock { Text = "Risiko: " + label, FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(col) };
            wrap.Children.Add(chip);
            if (rest.Length > 0)
            {
                var r = Text(rest, 11, null, "MutedBrush");
                r.VerticalAlignment = VerticalAlignment.Center;
                wrap.Children.Add(r);
            }
            content.Children.Add(wrap);
        }

        grid.Children.Add(content);
        row.Child = grid;
        return row;
    }

    /// <summary>Farbiges Hinweisband. color = null nimmt die Akzentfarbe des Designs.</summary>
    private static Border Band(string icon, string text, Color? color)
    {
        var band = new Border
        {
            CornerRadius = new CornerRadius(10), Padding = new Thickness(12, 9, 12, 9), Margin = new Thickness(0, 12, 0, 0), BorderThickness = new Thickness(1)
        };
        if (color is { } c)
        {
            band.Background = Tint(c, 0x24);
            band.BorderBrush = Tint(c, 0x99);
        }
        else
        {
            band.SetResourceReference(Border.BackgroundProperty, "Panel2Brush");
            band.SetResourceReference(Border.BorderBrushProperty, "PanelBorderBrush");
        }

        var dock = new DockPanel();
        var ic = new TextBlock { Text = icon, FontSize = 15, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Top };
        if (color is { } cc) ic.Foreground = new SolidColorBrush(cc); else ic.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
        DockPanel.SetDock(ic, Dock.Left);
        dock.Children.Add(ic);
        dock.Children.Add(Text(text, 12, null, "TextBrush"));
        band.Child = dock;
        return band;
    }
}
