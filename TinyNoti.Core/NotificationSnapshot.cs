namespace TinyNoti.Core;

public sealed record ImageCandidate(
    string Uri,
    string Kind,
    int? Width = null,
    int? Height = null);

public sealed record NotificationSnapshot(
    uint Id,
    long DisplayId,
    string AppName,
    string AppUserModelId,
    string Title,
    string Body,
    DateTimeOffset CreatedAt,
    DateTimeOffset ReceivedAt,
    IReadOnlyList<string> RawTextLines,
    IReadOnlyList<ImageCandidate> ImageCandidates,
    string? AppIconUri,
    LaunchHint? ActivationHint,
    bool CanDismiss,
    bool CanLaunchBestEffort)
{
    public string IdentityKey => $"{AppUserModelId}|{Id}|{DisplayId}|{CreatedAt:O}";

    public string SearchText => string.Join(
        "\n",
        new[] { AppName, AppUserModelId, Title, Body }.Concat(RawTextLines));
}
