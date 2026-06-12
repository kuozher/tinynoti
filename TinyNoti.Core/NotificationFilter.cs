namespace TinyNoti.Core;

public enum FilterMode
{
    Blacklist,
    Whitelist
}

public sealed class NotificationFilter
{
    private readonly HashSet<string> _patterns;

    public NotificationFilter(FilterMode mode, IEnumerable<string> patterns)
    {
        Mode = mode;
        _patterns = patterns
            .Where(static pattern => !string.IsNullOrWhiteSpace(pattern))
            .Select(static pattern => pattern.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public FilterMode Mode { get; }

    public bool Allows(NotificationSnapshot snapshot)
    {
        if (_patterns.Count == 0)
        {
            return true;
        }

        var matched = _patterns.Any(pattern =>
            snapshot.AppName.Contains(pattern, StringComparison.OrdinalIgnoreCase)
            || snapshot.AppUserModelId.Contains(pattern, StringComparison.OrdinalIgnoreCase));

        return Mode switch
        {
            FilterMode.Blacklist => !matched,
            FilterMode.Whitelist => matched,
            _ => true
        };
    }
}
