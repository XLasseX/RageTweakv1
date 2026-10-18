#nullable disable
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace FortniteTweakTool;

public static class StatusHelper
{
    /// <summary>True, wenn der Statustext "aktiv/angewendet" bedeutet.</summary>
    public static bool IsApplied(string status) =>
        !string.IsNullOrEmpty(status) &&
        (status.StartsWith("Aktiv", StringComparison.Ordinal) || status.StartsWith("Angewendet", StringComparison.Ordinal));
}

/// <summary>
/// values[0] = Status (string), values[1] = CanUndo (bool).
/// Parameter "show": sichtbar, wenn angewendet. Ohne Parameter: ausgeblendet, wenn angewendet.
/// Einmalige Aktionen (CanUndo = false) gelten nie als "angewendet", der Button bleibt also sichtbar.
/// </summary>
public sealed class AppliedVisibilityConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        string status = values.Length > 0 ? values[0] as string : null;
        bool canUndo = values.Length > 1 && values[1] is bool b && b;
        bool applied = canUndo && StatusHelper.IsApplied(status);
        bool show = string.Equals(parameter as string, "show", StringComparison.OrdinalIgnoreCase);
        return (show ? applied : !applied) ? Visibility.Visible : Visibility.Collapsed;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>bool -> Text. Parameter "TextWennTrue|TextWennFalse".</summary>
public sealed class BoolTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var parts = ((parameter as string) ?? "|").Split('|');
        string yes = parts.Length > 0 ? parts[0] : "";
        string no = parts.Length > 1 ? parts[1] : "";
        return value is bool b && b ? yes : no;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
