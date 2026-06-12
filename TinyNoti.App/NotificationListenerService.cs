using TinyNoti.Core;
using Windows.UI.Notifications;
using Windows.UI.Notifications.Management;

namespace TinyNoti.App;

public sealed class NotificationListenerService : IDisposable
{
    private readonly UserNotificationListener _listener = UserNotificationListener.Current;
    private bool _started;

    public event Action<NotificationSnapshot>? NotificationReceived;

    public event Action<uint>? NotificationRemoved;

    public event Action<string>? StatusChanged;

    public async Task<UserNotificationListenerAccessStatus> RequestAccessAsync()
    {
        try
        {
            var status = await _listener.RequestAccessAsync();
            StatusChanged?.Invoke($"Notification access: {status}");
            return status;
        }
        catch (Exception ex)
        {
            StatusChanged?.Invoke($"Notification access unavailable in this launch mode: {ex.Message}");
            return UserNotificationListenerAccessStatus.Unspecified;
        }
    }

    public async Task StartAsync()
    {
        if (_started)
        {
            return;
        }

        try
        {
            var status = _listener.GetAccessStatus();
            StatusChanged?.Invoke($"Notification access: {status}");
            if (status != UserNotificationListenerAccessStatus.Allowed)
            {
                return;
            }

            _listener.NotificationChanged += Listener_NotificationChanged;
            _started = true;

            var notifications = await _listener.GetNotificationsAsync(NotificationKinds.Toast);
            foreach (var notification in notifications)
            {
                await PublishAsync(notification);
            }
        }
        catch (Exception ex)
        {
            StatusChanged?.Invoke($"Notification listener unavailable outside packaged install: {ex.Message}");
        }
    }

    public void Dismiss(uint id)
    {
        try
        {
            _listener.RemoveNotification(id);
        }
        catch (Exception ex)
        {
            StatusChanged?.Invoke($"Could not dismiss Windows notification: {ex.Message}");
        }

        NotificationRemoved?.Invoke(id);
    }

    public void ClearAll()
    {
        try
        {
            _listener.ClearNotifications();
        }
        catch (Exception ex)
        {
            StatusChanged?.Invoke($"Could not clear Windows notifications: {ex.Message}");
        }
    }

    public void Dispose()
    {
        if (_started)
        {
            _listener.NotificationChanged -= Listener_NotificationChanged;
        }
    }

    private async void Listener_NotificationChanged(UserNotificationListener sender, UserNotificationChangedEventArgs args)
    {
        if (args.ChangeKind == UserNotificationChangedKind.Added)
        {
            var notification = sender.GetNotification(args.UserNotificationId);
            if (notification is not null)
            {
                await PublishAsync(notification);
            }
        }
        else if (args.ChangeKind == UserNotificationChangedKind.Removed)
        {
            NotificationRemoved?.Invoke(args.UserNotificationId);
        }
    }

    private async Task PublishAsync(UserNotification notification)
    {
        var snapshot = await NotificationSnapshotFactory.FromUserNotificationAsync(notification);
        NotificationReceived?.Invoke(snapshot);
    }
}
