using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using TinyNoti.Core;

namespace TinyNoti.App;

public sealed class MainWindowViewModel : INotifyPropertyChanged
{
    private string _accessStatusText = "Checking.";
    private string _mirroringStatusText = string.Empty;
    private string _operationStatusText = string.Empty;
    private string _filterText = string.Empty;

    public MainWindowViewModel(AppSettings settings)
    {
        Settings = settings;
        _filterText = string.Join(", ", settings.FilterPatterns);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public AppSettings Settings { get; }

    public ObservableCollection<NotificationCardViewModel> History { get; } = [];

    public IReadOnlyList<AnchorOption> AnchorOptions { get; } =
    [
        new("Top left", OverlayAnchor.TopLeft),
        new("Top right", OverlayAnchor.TopRight),
        new("Bottom left", OverlayAnchor.BottomLeft),
        new("Bottom right", OverlayAnchor.BottomRight)
    ];

    public IReadOnlyList<FilterMode> FilterModeOptions { get; } =
    [
        FilterMode.Blacklist,
        FilterMode.Whitelist
    ];

    public IReadOnlyList<DisplayOption> ScreenOptions { get; } = System.Windows.Forms.Screen.AllScreens
        .Select((screen, index) => new DisplayOption(
            index,
            screen.Primary ? $"Display {index + 1} (Primary)" : $"Display {index + 1}"))
        .ToArray();

    public bool HasMultipleScreens => ScreenOptions.Count > 1;

    public string AccessStatusText => _accessStatusText;

    public System.Windows.Media.Brush AccessStatusBrush => AccessStatusText.Equals("Allowed", StringComparison.OrdinalIgnoreCase)
        ? new SolidColorBrush(System.Windows.Media.Color.FromRgb(34, 197, 94))
        : new SolidColorBrush(System.Windows.Media.Color.FromRgb(239, 68, 68));

    public string MirroringStatusText
    {
        get => _mirroringStatusText;
        set => SetField(ref _mirroringStatusText, value);
    }

    public bool HasMirroringStatus => !string.IsNullOrWhiteSpace(MirroringStatusText);

    public string OperationStatusText
    {
        get => _operationStatusText;
        set => SetField(ref _operationStatusText, value);
    }

    public bool HasOperationStatus => !string.IsNullOrWhiteSpace(OperationStatusText);

    public string StatusText
    {
        get => AccessStatusText;
        set
        {
            SetAccessStatus(value);
        }
    }

    public void SetAccessStatus(string text)
    {
        const string prefix = "Notification access:";
        var next = text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? text[prefix.Length..].Trim()
            : text;

        if (SetField(ref _accessStatusText, next, nameof(AccessStatusText)))
        {
            OnPropertyChanged(nameof(AccessStatusBrush));
        }
    }

    public string FilterText
    {
        get => _filterText;
        set
        {
            if (SetField(ref _filterText, value))
            {
                Settings.FilterPatterns = value
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .ToList();
            }
        }
    }

    public void ReplaceHistory(IEnumerable<NotificationSnapshot> items)
    {
        History.Clear();
        foreach (var item in items.Select(NotificationCardViewModel.FromSnapshot))
        {
            History.Add(item);
        }
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        if (propertyName == nameof(MirroringStatusText))
        {
            OnPropertyChanged(nameof(HasMirroringStatus));
        }
        else if (propertyName == nameof(OperationStatusText))
        {
            OnPropertyChanged(nameof(HasOperationStatus));
        }

        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public sealed record AnchorOption(string Label, OverlayAnchor Value);

public sealed record DisplayOption(int Index, string Label);
