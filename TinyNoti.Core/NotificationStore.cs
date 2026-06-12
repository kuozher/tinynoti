namespace TinyNoti.Core;

public sealed class NotificationStore
{
    private readonly int _historyLimit;
    private readonly Dictionary<long, NotificationSnapshot> _visible = [];
    private readonly List<NotificationSnapshot> _history = [];

    public NotificationStore(int historyLimit)
    {
        _historyLimit = Math.Max(1, historyLimit);
    }

    public IReadOnlyList<NotificationSnapshot> Visible => _visible.Values
        .OrderByDescending(static item => item.ReceivedAt)
        .ToArray();

    public IReadOnlyList<NotificationSnapshot> History => _history
        .OrderByDescending(static item => item.ReceivedAt)
        .ToArray();

    public void Add(NotificationSnapshot snapshot)
    {
        _visible[snapshot.DisplayId] = snapshot;
        _history.Add(snapshot);
        TrimHistory();
    }

    public NotificationSnapshot? FindByDisplayId(long displayId)
    {
        if (_visible.TryGetValue(displayId, out var visible))
        {
            return visible;
        }

        return _history.FirstOrDefault(item => item.DisplayId == displayId);
    }

    public void Remove(long displayId)
    {
        _visible.Remove(displayId);
        _history.RemoveAll(item => item.DisplayId == displayId);
    }

    public void RemoveByWindowsId(uint id)
    {
        foreach (var item in _visible.Values.Where(item => item.Id == id).ToArray())
        {
            _visible.Remove(item.DisplayId);
        }

        _history.RemoveAll(item => item.Id == id);
    }

    public void Hide(long displayId)
    {
        _visible.Remove(displayId);
    }

    public void Clear()
    {
        _visible.Clear();
        _history.Clear();
    }

    private void TrimHistory()
    {
        if (_history.Count <= _historyLimit)
        {
            return;
        }

        var keep = _history
            .OrderByDescending(static item => item.ReceivedAt)
            .Take(_historyLimit)
            .Select(static item => item.DisplayId)
            .ToHashSet();

        _history.RemoveAll(item => !keep.Contains(item.DisplayId));
    }
}
