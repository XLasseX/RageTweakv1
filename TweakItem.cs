using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;

namespace FortniteTweakTool.Models;

public sealed class TweakItem : INotifyPropertyChanged
{
    private string _status = "Nicht angewendet";

    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Risk { get; set; } = "Niedrig";
    public string Key { get; set; } = "";
    public string Category { get; set; } = "fps";
    public bool CanUndo { get; set; }

    private bool _isSelected;
    /// <summary>Vom Benutzer für "Ausgewählte anwenden" markiert (Checkbox oder Rechtsklick).</summary>
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value) return;
            _isSelected = value;
            OnPropertyChanged();
        }
    }

    public Visibility UndoVisibility => CanUndo ? Visibility.Visible : Visibility.Collapsed;

    public string Status
    {
        get => _status;
        set
        {
            if (_status == value) return;
            _status = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(StatusKind));
        }
    }

    // ok = aktiv (grün), fail = Fehler (rot), busy = läuft, none = neutral
    public string StatusKind =>
        _status.StartsWith("Angewendet") || _status.StartsWith("Aktiv") ? "ok" :
        _status.Contains("fehlgeschlagen", StringComparison.OrdinalIgnoreCase) ? "fail" :
        _status.StartsWith("Wird") ? "busy" : "none";

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
