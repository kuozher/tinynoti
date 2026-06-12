using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using TinyNoti.Core;

namespace TinyNoti.App;

public sealed class AppSettings : INotifyPropertyChanged
{
    private OverlayAnchor _anchor = OverlayAnchor.TopRight;
    private int _screenIndex;
    private double _offsetX = 24;
    private double _offsetY = 32;
    private int _autoHideSeconds = 7;
    private int _historyLimit = 100;
    private bool _isPaused;
    private bool _startWithWindows;
    private FilterMode _filterMode = FilterMode.Blacklist;
    private List<string> _filterPatterns = [];

    public event PropertyChangedEventHandler? PropertyChanged;

    public OverlayAnchor Anchor
    {
        get => _anchor;
        set => SetField(ref _anchor, value);
    }

    public int ScreenIndex
    {
        get => _screenIndex;
        set => SetField(ref _screenIndex, value);
    }

    public double OffsetX
    {
        get => _offsetX;
        set => SetField(ref _offsetX, Math.Max(0, value));
    }

    public double OffsetY
    {
        get => _offsetY;
        set => SetField(ref _offsetY, Math.Max(0, value));
    }

    public int AutoHideSeconds
    {
        get => _autoHideSeconds;
        set => SetField(ref _autoHideSeconds, Math.Clamp(value, 2, 60));
    }

    public int HistoryLimit
    {
        get => _historyLimit;
        set => SetField(ref _historyLimit, Math.Clamp(value, 10, 500));
    }

    public bool IsPaused
    {
        get => _isPaused;
        set => SetField(ref _isPaused, value);
    }

    public bool StartWithWindows
    {
        get => _startWithWindows;
        set => SetField(ref _startWithWindows, value);
    }

    public FilterMode FilterMode
    {
        get => _filterMode;
        set => SetField(ref _filterMode, value);
    }

    public List<string> FilterPatterns
    {
        get => _filterPatterns;
        set => SetField(ref _filterPatterns, value);
    }

    public static string SettingsPath
    {
        get
        {
            var folder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "TinyNoti");
            Directory.CreateDirectory(folder);
            return Path.Combine(folder, "settings.json");
        }
    }

    public static AppSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath))
            {
                return new AppSettings();
            }

            var json = File.ReadAllText(SettingsPath);
            return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    public void Save()
    {
        var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(SettingsPath, json);
    }

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
