using System.Drawing;
using System.IO;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Threading;
using TinyNoti.Core;
using Windows.ApplicationModel;
using Application = System.Windows.Application;

namespace TinyNoti.App;

public sealed class AppController : IDisposable
{
    private readonly AppSettings _settings;
    private readonly MainWindowViewModel _mainViewModel;
    private readonly NotificationListenerService _listener;
    private readonly NotificationStore _store;
    private readonly OverlayWindow _overlay;
    private readonly MainWindow _mainWindow;
    private readonly NotifyIcon _trayIcon;
    private readonly LaunchTargetResolver _launchResolver;
    private readonly Dictionary<long, DispatcherTimer> _hideTimers = [];
    private readonly DispatcherTimer _operationStatusTimer;
    private bool _updatingStartupTask;
    private bool _showingHistoryOverlay;
    private DateTimeOffset _ignoreTrayToggleUntil;

    public AppController()
    {
        _settings = AppSettings.Load();
        _mainViewModel = new MainWindowViewModel(_settings);
        _listener = new NotificationListenerService();
        _store = new NotificationStore(_settings.HistoryLimit);
        _overlay = new OverlayWindow();
        _mainWindow = new MainWindow(this, _mainViewModel);
        _launchResolver = new LaunchTargetResolver(DefaultLaunchRules());
        _trayIcon = CreateTrayIcon();
        _operationStatusTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(4)
        };
        _operationStatusTimer.Tick += (_, _) =>
        {
            _operationStatusTimer.Stop();
            _mainViewModel.OperationStatusText = string.Empty;
        };

        _listener.NotificationReceived += OnNotificationReceived;
        _listener.NotificationRemoved += OnNotificationRemoved;
        _listener.StatusChanged += text => Application.Current.Dispatcher.Invoke(() => _mainViewModel.SetAccessStatus(text));
        _overlay.DismissRequested += DismissNotification;
        _overlay.LaunchRequested += LaunchNotification;
        _overlay.ClearAllRequested += ClearAllNotifications;
        _overlay.HideOverlayRequested += HideOverlayForNow;
        _settings.PropertyChanged += Settings_PropertyChanged;
        UpdateMirroringStatus();
    }

    public async Task StartAsync(bool showMainWindow)
    {
        _trayIcon.Visible = true;
        if (showMainWindow)
        {
            ShowMainWindow();
        }

        await _listener.StartAsync();
        await SyncStartupTaskSettingAsync();
    }

    public async Task RequestAccessAsync()
    {
        var status = await _listener.RequestAccessAsync();
        if (status == Windows.UI.Notifications.Management.UserNotificationListenerAccessStatus.Allowed)
        {
            await _listener.StartAsync();
        }
    }

    public void DismissNotification(long displayId)
    {
        StopHideTimer(displayId);
        var snapshot = _store.FindByDisplayId(displayId);
        if (snapshot is not null)
        {
            _listener.Dismiss(snapshot.Id);
        }

        _store.Remove(displayId);
        RefreshViews();
    }

    public void ClearAllNotifications()
    {
        _showingHistoryOverlay = false;
        foreach (var timer in _hideTimers.Values)
        {
            timer.Stop();
        }

        _hideTimers.Clear();
        _listener.ClearAll();
        _store.Clear();
        RefreshViews();
    }

    public void ShowRecentNotifications()
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            if (_store.History.Count == 0)
            {
                _showingHistoryOverlay = true;
                RenderEmptyRecentOverlay();
                return;
            }

            _showingHistoryOverlay = true;
            RenderOverlay(_store.History);
        });
    }

    public void ToggleRecentNotifications()
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            if (_showingHistoryOverlay && _overlay.IsOverlayPresented)
            {
                HideOverlayForNow();
                return;
            }

            ShowRecentNotifications();
        });
    }

    public void Dispose()
    {
        _settings.Save();
        _trayIcon.Visible = false;
        _trayIcon.Dispose();
        _operationStatusTimer.Stop();
        _listener.Dispose();
        _overlay.Close();
        _mainWindow.AllowClose = true;
        _mainWindow.Close();
    }

    public void ShowMainWindow()
    {
        _mainWindow.Show();
        if (_mainWindow.WindowState == WindowState.Minimized)
        {
            _mainWindow.WindowState = WindowState.Normal;
        }

        _mainWindow.Activate();
    }

    private void OnNotificationReceived(NotificationSnapshot snapshot)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            if (_settings.IsPaused)
            {
                return;
            }

            var filter = new NotificationFilter(_settings.FilterMode, _settings.FilterPatterns);
            if (!filter.Allows(snapshot))
            {
                return;
            }

            var resolved = snapshot with { ActivationHint = _launchResolver.ResolveBestEffort(snapshot) };
            _store.Add(resolved);
            RefreshViews();
            if (!_showingHistoryOverlay || !_overlay.IsOverlayPresented)
            {
                StartHideTimer(resolved.DisplayId);
            }
        });
    }

    private async void Settings_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AppSettings.StartWithWindows))
        {
            await ConfigureStartupTaskAsync();
            return;
        }

        if (e.PropertyName == nameof(AppSettings.IsPaused))
        {
            UpdateMirroringStatus();
        }

        PersistSettings();
    }

    private async Task ConfigureStartupTaskAsync()
    {
        if (_updatingStartupTask)
        {
            return;
        }

        _updatingStartupTask = true;
        _ignoreTrayToggleUntil = DateTimeOffset.Now.AddMilliseconds(700);
        try
        {
            var startupTask = await StartupTask.GetAsync("TinyNotiStartup");
            var state = startupTask.State;
            if (_settings.StartWithWindows)
            {
                if (state is not (StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy))
                {
                    state = await startupTask.RequestEnableAsync();
                }
            }
            else if (state == StartupTaskState.Enabled)
            {
                startupTask.Disable();
                state = StartupTaskState.Disabled;
            }

            SetStartWithWindowsFromSystemState(state is StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy);
            SetOperationStatus(state switch
            {
                StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy => "Startup task enabled.",
                StartupTaskState.DisabledByUser => "Startup task disabled by Windows settings.",
                _ => "Startup task disabled."
            });
        }
        catch (Exception ex)
        {
            SetStartWithWindowsFromSystemState(false);
            SetOperationStatus($"Startup task unavailable outside MSIX: {ex.Message}");
        }
        finally
        {
            _updatingStartupTask = false;
            _ignoreTrayToggleUntil = DateTimeOffset.Now.AddMilliseconds(700);
        }
    }

    private async Task SyncStartupTaskSettingAsync()
    {
        if (_updatingStartupTask)
        {
            return;
        }

        _updatingStartupTask = true;
        try
        {
            var startupTask = await StartupTask.GetAsync("TinyNotiStartup");
            SetStartWithWindowsFromSystemState(startupTask.State is StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy);
        }
        catch (Exception ex)
        {
            SetOperationStatus($"Startup task unavailable outside MSIX: {ex.Message}");
        }
        finally
        {
            _updatingStartupTask = false;
        }
    }

    private void SetStartWithWindowsFromSystemState(bool isEnabled)
    {
        if (_settings.StartWithWindows != isEnabled)
        {
            _settings.PropertyChanged -= Settings_PropertyChanged;
            _settings.StartWithWindows = isEnabled;
            _settings.PropertyChanged += Settings_PropertyChanged;
        }

        _settings.Save();
    }

    private void OnNotificationRemoved(uint id)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            foreach (var item in _store.Visible.Where(item => item.Id == id).ToArray())
            {
                StopHideTimer(item.DisplayId);
            }

            _store.RemoveByWindowsId(id);
            RefreshViews();
        });
    }

    private void LaunchNotification(long displayId)
    {
        var snapshot = _store.FindByDisplayId(displayId);

        if (snapshot is null)
        {
            return;
        }

        var hint = snapshot.ActivationHint ?? _launchResolver.ResolveBestEffort(snapshot);
        LaunchTargetResolver.TryOpen(hint);
    }

    private void StartHideTimer(long displayId)
    {
        StopHideTimer(displayId);
        var timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(_settings.AutoHideSeconds)
        };

        timer.Tick += (_, _) =>
        {
            StopHideTimer(displayId);
            _store.Hide(displayId);
            RefreshViews();
        };

        _hideTimers[displayId] = timer;
        timer.Start();
    }

    private void StopHideTimer(long displayId)
    {
        if (_hideTimers.Remove(displayId, out var timer))
        {
            timer.Stop();
        }
    }

    private void RefreshViews()
    {
        _mainViewModel.ReplaceHistory(_store.History);

        if (_showingHistoryOverlay && _overlay.IsOverlayPresented)
        {
            if (_store.History.Count > 0)
            {
                RenderOverlay(_store.History);
            }
            else
            {
                RenderEmptyRecentOverlay();
            }

            return;
        }

        if (_store.Visible.Count > 0)
        {
            RenderOverlay(_store.Visible);
        }
        else
        {
            _overlay.HideWithAnimation();
        }
    }

    private void HideOverlayForNow()
    {
        _showingHistoryOverlay = false;
        _overlay.HideWithAnimation();
    }

    private void PersistSettings()
    {
        _settings.Save();
        RefreshOverlayPosition();
    }

    private void UpdateMirroringStatus()
    {
        _mainViewModel.MirroringStatusText = _settings.IsPaused ? "Mirroring paused" : string.Empty;
    }

    private void SetOperationStatus(string text)
    {
        _operationStatusTimer.Stop();
        _mainViewModel.OperationStatusText = text;
        if (!string.IsNullOrWhiteSpace(text))
        {
            _operationStatusTimer.Start();
        }
    }

    private void RenderOverlay(IEnumerable<NotificationSnapshot> snapshots)
    {
        var bottomAnchored = IsBottomAnchored();
        var ordered = bottomAnchored
            ? snapshots.OrderBy(static item => item.ReceivedAt)
            : snapshots.OrderByDescending(static item => item.ReceivedAt);
        var cards = ordered.Select(NotificationCardViewModel.FromSnapshot).ToArray();

        if (cards.Length == 0)
        {
            _overlay.HideWithAnimation();
            return;
        }

        var availableHeight = PrepareOverlaySizing();
        _overlay.SetCards(cards, IsLeftAnchored());
        var shouldAnimateShow = _overlay.PrepareShowForLayout();
        _overlay.UpdateLayout();
        _overlay.FitToContentHeight(availableHeight);
        _overlay.UpdateLayout();
        RefreshOverlayPosition();
        _overlay.FinishShowAnimation(shouldAnimateShow);
    }

    private void RenderEmptyRecentOverlay()
    {
        var availableHeight = PrepareOverlaySizing();
        _overlay.SetEmptyRecentState(IsLeftAnchored());
        var shouldAnimateShow = _overlay.PrepareShowForLayout();
        _overlay.UpdateLayout();
        _overlay.FitToContentHeight(availableHeight);
        _overlay.UpdateLayout();
        RefreshOverlayPosition();
        _overlay.FinishShowAnimation(shouldAnimateShow);
    }

    private void RefreshOverlayPosition()
    {
        var screen = GetTargetScreen();
        var width = _overlay.ActualWidth > 0 ? _overlay.ActualWidth : 390;
        var height = _overlay.OverlayHeight;

        _overlay.Left = _settings.Anchor is OverlayAnchor.TopLeft or OverlayAnchor.BottomLeft
            ? screen.WorkingArea.Left + _settings.OffsetX
            : screen.WorkingArea.Right - _settings.OffsetX - width;
        _overlay.Top = IsBottomAnchored()
            ? screen.WorkingArea.Bottom - _settings.OffsetY - height
            : screen.WorkingArea.Top + _settings.OffsetY;
    }

    private double PrepareOverlaySizing()
    {
        var screen = GetTargetScreen();
        var availableHeight = Math.Max(260, screen.WorkingArea.Height - _settings.OffsetY);
        _overlay.PrepareLayoutCapacity(availableHeight);
        return availableHeight;
    }

    private bool IsBottomAnchored() => _settings.Anchor is OverlayAnchor.BottomLeft or OverlayAnchor.BottomRight;

    private bool IsLeftAnchored() => _settings.Anchor is OverlayAnchor.TopLeft or OverlayAnchor.BottomLeft;

    private Screen GetTargetScreen()
    {
        var screens = Screen.AllScreens;
        if (_settings.ScreenIndex < 0 || _settings.ScreenIndex >= screens.Length)
        {
            _settings.ScreenIndex = 0;
        }

        return screens[_settings.ScreenIndex];
    }

    private NotifyIcon CreateTrayIcon()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Open TinyNoti", null, (_, _) => ShowMainWindow());
        menu.Items.Add("Show recent notifications", null, (_, _) => ShowRecentNotifications());
        menu.Items.Add("Pause mirroring", null, (_, _) => _settings.IsPaused = !_settings.IsPaused);
        menu.Items.Add("Clear all notifications", null, (_, _) => ClearAllNotifications());
        menu.Items.Add("Exit", null, (_, _) => Application.Current.Shutdown());

        var tray = new NotifyIcon
        {
            Icon = LoadTrayIcon(),
            Text = "TinyNoti",
            ContextMenuStrip = menu
        };
        tray.MouseUp += (_, e) =>
        {
            if (e.Button == MouseButtons.Left && DateTimeOffset.Now >= _ignoreTrayToggleUntil)
            {
                ToggleRecentNotifications();
            }
        };
        return tray;
    }

    private static Icon LoadTrayIcon()
    {
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");
        return File.Exists(iconPath) ? new Icon(iconPath) : SystemIcons.Application;
    }

    private static IReadOnlyList<AppLaunchRule> DefaultLaunchRules()
    {
        return
        [
            new AppLaunchRule("Asana task", "asana", @"(?:task|tasks|/0/0/)(?<id>\d+)", "https://app.asana.com/0/0/${id}"),
            new AppLaunchRule("Slack channel", "slack", @"(?<url>slack://[^\s]+)", "${url}")
        ];
    }
}
