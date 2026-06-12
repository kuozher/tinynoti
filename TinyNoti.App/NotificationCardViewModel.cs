using TinyNoti.Core;

namespace TinyNoti.App;

public sealed class NotificationCardViewModel
{
    public required long DisplayId { get; init; }

    public required uint WindowsNotificationId { get; init; }

    public required string AppName { get; init; }

    public required string AppUserModelId { get; init; }

    public required string Title { get; init; }

    public required string Body { get; init; }

    public required string TimeText { get; init; }

    public string? ImageUri { get; init; }

    public string? AppIconUri { get; init; }

    public required bool CanDismiss { get; init; }

    public required bool CanLaunchBestEffort { get; init; }

    public required NotificationSnapshot Snapshot { get; init; }

    public static NotificationCardViewModel FromSnapshot(NotificationSnapshot snapshot)
    {
        return new NotificationCardViewModel
        {
            DisplayId = snapshot.DisplayId,
            WindowsNotificationId = snapshot.Id,
            AppName = snapshot.AppName,
            AppUserModelId = snapshot.AppUserModelId,
            Title = string.IsNullOrWhiteSpace(snapshot.Title) ? "(No title)" : snapshot.Title,
            Body = snapshot.Body,
            TimeText = snapshot.CreatedAt.ToLocalTime().ToString("HH:mm"),
            ImageUri = snapshot.ImageCandidates.FirstOrDefault()?.Uri,
            AppIconUri = snapshot.AppIconUri,
            CanDismiss = snapshot.CanDismiss,
            CanLaunchBestEffort = snapshot.CanLaunchBestEffort,
            Snapshot = snapshot
        };
    }
}
