using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using FortniteTweakTool.Services;

namespace FortniteTweakTool;

/// <summary>Kleiner Farbwähler (RGB-Regler, Hex-Feld, Farbpalette) mit Live-Vorschau im ganzen Programm.</summary>
public sealed class ColorPickerWindow : Window
{
    private static readonly string[] Palette =
    {
        "#0D0F1A", "#171A2B", "#000000", "#FFFFFF", "#7B61FF", "#9D4EDD", "#FF2BD6", "#FF2D4A",
        "#FF6B35", "#FFC857", "#00FF66", "#2DE2C4", "#00D4FF", "#1E90FF", "#3B6CFF", "#E9EEF8"
    };

    private readonly Action<Color> _live;
    private readonly Color _original;
    private readonly Slider _r = MakeSlider(), _g = MakeSlider(), _b = MakeSlider();
    private readonly TextBlock _rv = new(), _gv = new(), _bv = new();
    private readonly TextBox _hex = new();
    private readonly Border _preview = new();
    private bool _updating;

    public Color SelectedColor { get; private set; }

    public ColorPickerWindow(string title, Color initial, Action<Color> live)
    {
        _live = live;
        _original = initial;

        Title = "Farbe wählen – " + title;
        Width = 400;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SetResourceReference(BackgroundProperty, "BgBrush");
        SetResourceReference(ForegroundProperty, "TextBrush");

        var root = new StackPanel { Margin = new Thickness(20) };

        _preview.Height = 56;
        _preview.CornerRadius = new CornerRadius(10);
        _preview.Margin = new Thickness(0, 0, 0, 14);
        _preview.BorderThickness = new Thickness(1);
        _preview.SetResourceReference(Border.BorderBrushProperty, "PanelBorderBrush");
        root.Children.Add(_preview);

        root.Children.Add(MakeRow("R", _r, _rv));
        root.Children.Add(MakeRow("G", _g, _gv));
        root.Children.Add(MakeRow("B", _b, _bv));

        var hexRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 12) };
        hexRow.Children.Add(new TextBlock { Text = "Hex", Width = 40, VerticalAlignment = VerticalAlignment.Center });
        _hex.Width = 130;
        _hex.Padding = new Thickness(6, 4, 6, 4);
        _hex.FontFamily = new FontFamily("Consolas");
        _hex.SetResourceReference(BackgroundProperty, "InsetBrush");
        _hex.SetResourceReference(ForegroundProperty, "TextBrush");
        _hex.SetResourceReference(BorderBrushProperty, "PanelBorderBrush");
        _hex.SetResourceReference(TextBox.CaretBrushProperty, "TextBrush");
        hexRow.Children.Add(_hex);
        root.Children.Add(hexRow);

        var palette = new WrapPanel { Margin = new Thickness(0, 0, 0, 14) };
        foreach (var hex in Palette)
        {
            var color = ThemeManager.ParseColor(hex, Colors.Black);
            var swatch = new Border
            {
                Width = 30,
                Height = 26,
                Margin = new Thickness(0, 0, 6, 6),
                CornerRadius = new CornerRadius(6),
                Background = new SolidColorBrush(color),
                BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand,
                ToolTip = hex
            };
            swatch.SetResourceReference(Border.BorderBrushProperty, "MutedBrush");
            swatch.MouseLeftButtonUp += (_, _) => Set(color, false);
            palette.Children.Add(swatch);
        }
        root.Children.Add(palette);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var ok = new Button { Content = "Übernehmen", IsDefault = true, MinWidth = 110 };
        ok.SetResourceReference(BackgroundProperty, "AccentBrush");
        ok.SetResourceReference(ForegroundProperty, "OnAccentBrush");
        ok.Click += (_, _) => DialogResult = true;
        var cancel = new Button { Content = "Abbrechen", IsCancel = true, MinWidth = 90 };
        cancel.Click += (_, _) => DialogResult = false;
        buttons.Children.Add(cancel);
        buttons.Children.Add(ok);
        root.Children.Add(buttons);

        Content = root;

        _r.ValueChanged += OnSlider;
        _g.ValueChanged += OnSlider;
        _b.ValueChanged += OnSlider;
        _hex.TextChanged += (_, _) =>
        {
            if (_updating) return;
            var t = _hex.Text.Trim();
            if (!t.StartsWith('#')) t = "#" + t;
            if ((t.Length == 7 || t.Length == 9) && ThemeManager.TryParseColor(t, out var c)) Set(c, true);
        };

        // Schließen ohne "Übernehmen" stellt die ursprüngliche Farbe wieder her.
        Closing += (_, _) => { if (DialogResult != true) _live(_original); };

        Set(initial, false, notify: false);
    }

    private static Slider MakeSlider() => new()
    {
        Minimum = 0,
        Maximum = 255,
        SmallChange = 1,
        LargeChange = 16,
        VerticalAlignment = VerticalAlignment.Center
    };

    private static Grid MakeRow(string label, Slider slider, TextBlock value)
    {
        var grid = new Grid { Margin = new Thickness(0, 3, 0, 3) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });

        grid.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });
        Grid.SetColumn(slider, 1);
        grid.Children.Add(slider);
        value.HorizontalAlignment = HorizontalAlignment.Right;
        value.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(value, 2);
        grid.Children.Add(value);
        return grid;
    }

    private void OnSlider(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_updating) return;
        Set(Color.FromRgb((byte)_r.Value, (byte)_g.Value, (byte)_b.Value), false);
    }

    private void Set(Color c, bool fromHex, bool notify = true)
    {
        _updating = true;
        _r.Value = c.R;
        _g.Value = c.G;
        _b.Value = c.B;
        _rv.Text = c.R.ToString();
        _gv.Text = c.G.ToString();
        _bv.Text = c.B.ToString();
        if (!fromHex) _hex.Text = ThemeManager.ToHex(c);
        _preview.Background = new SolidColorBrush(c);
        _updating = false;

        SelectedColor = c;
        if (notify) _live(c);
    }
}
