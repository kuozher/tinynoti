using TinyNoti.Core;

var tests = new (string Name, Action Body)[]
{
    ("extracts first https url from notification text", UrlExtraction),
    ("matches app specific launch rule", AppSpecificLaunchRule),
    ("resolves built-in Asana task links", BuiltInAsanaLaunchRule),
    ("resolves built-in Slack deep links", BuiltInSlackLaunchRule),
    ("filters blacklist and whitelist modes", Filtering),
    ("stacks repeated notification events and removes by display id", NotificationStoreState),
    ("trims history by display id", NotificationStoreTrimHistory),
    ("calculates bottom right overlay positions", OverlayPlacement),
    ("keeps overlay below fullscreen foreground windows", FullscreenTopmostPolicy)
};

var failed = 0;
foreach (var test in tests)
{
    try
    {
        test.Body();
        Console.WriteLine($"PASS {test.Name}");
    }
    catch (Exception ex)
    {
        failed++;
        Console.Error.WriteLine($"FAIL {test.Name}: {ex.Message}");
    }
}

if (failed > 0)
{
    Environment.Exit(1);
}

static void UrlExtraction()
{
    var hint = LaunchTargetResolver.Resolve(new NotificationSnapshot(
        12,
        12_001,
        "Slack",
        "com.slack.desktop",
        "Thread",
        "Open https://example.com/thread/123 when ready.",
        DateTimeOffset.Parse("2026-06-10T08:00:00Z"),
        DateTimeOffset.Parse("2026-06-10T08:00:01Z"),
        ["Thread", "Open https://example.com/thread/123 when ready."],
        [],
        null,
        null,
        true,
        true));

    AssertEqual(LaunchTargetKind.Url, hint.Kind);
    AssertEqual("https://example.com/thread/123", hint.Target);
}

static void AppSpecificLaunchRule()
{
    var resolver = new LaunchTargetResolver([
        new AppLaunchRule("Asana", "app.asana", @"task/(?<id>\d+)", "https://app.asana.com/0/0/${id}")
    ]);

    var hint = resolver.ResolveBestEffort(new NotificationSnapshot(
        8,
        8_001,
        "Asana",
        "app.asana.desktop",
        "Task updated",
        "task/445566 changed",
        DateTimeOffset.Parse("2026-06-10T08:00:00Z"),
        DateTimeOffset.Parse("2026-06-10T08:00:01Z"),
        ["Task updated", "task/445566 changed"],
        [],
        null,
        null,
        true,
        true));

    AssertEqual(LaunchTargetKind.Url, hint.Kind);
    AssertEqual("https://app.asana.com/0/0/445566", hint.Target);
}

static void BuiltInAsanaLaunchRule()
{
    var hint = LaunchTargetResolver.Resolve(Snapshot(
        "Asana",
        "app.asana.desktop") with
    {
        Body = "Task 445566 was updated",
        RawTextLines = ["Task updated", "Task 445566 was updated"]
    });

    AssertEqual(LaunchTargetKind.Url, hint.Kind);
    AssertEqual("https://app.asana.com/0/0/445566", hint.Target);
}

static void BuiltInSlackLaunchRule()
{
    var hint = LaunchTargetResolver.Resolve(Snapshot(
        "Slack",
        "com.slack.desktop") with
    {
        Body = "Open slack://channel?team=T123&id=C456",
        RawTextLines = ["New message", "Open slack://channel?team=T123&id=C456"]
    });

    AssertEqual(LaunchTargetKind.Url, hint.Kind);
    AssertEqual("slack://channel?team=T123&id=C456", hint.Target);
}

static void Filtering()
{
    var blacklist = new NotificationFilter(FilterMode.Blacklist, ["Slack"]);
    var whitelist = new NotificationFilter(FilterMode.Whitelist, ["Teams"]);
    var slack = Snapshot("Slack", "com.slack.desktop");
    var teams = Snapshot("Teams", "msteams");

    AssertFalse(blacklist.Allows(slack), "Slack should be blocked by blacklist.");
    AssertTrue(blacklist.Allows(teams), "Teams should pass blacklist.");
    AssertFalse(whitelist.Allows(slack), "Slack should not pass Teams whitelist.");
    AssertTrue(whitelist.Allows(teams), "Teams should pass whitelist.");
}

static void NotificationStoreState()
{
    var store = new NotificationStore(10);
    var first = Snapshot("Slack", "com.slack.desktop", id: 1, displayId: 101);
    var duplicate = first with { DisplayId = 102, Body = "Repeated event should stack." };
    var second = Snapshot("Teams", "msteams", id: 2);

    store.Add(first);
    store.Add(duplicate);
    store.Add(second);

    AssertEqual(3, store.Visible.Count);
    store.Remove(101);
    AssertEqual(2, store.Visible.Count);
    AssertEqual(2, store.History.Count);
    store.RemoveByWindowsId(1);
    AssertEqual(1, store.Visible.Count);
    AssertEqual(1, store.History.Count);
    store.Clear();
    AssertEqual(0, store.Visible.Count);
    AssertEqual(0, store.History.Count);
}

static void OverlayPlacement()
{
    var area = new ScreenWorkArea(0, 0, 1920, 1040, 1.0);
    var cards = OverlayPlacementCalculator.Calculate(area, OverlayAnchor.BottomRight, 24, 32, 360, 96, 3, 8);

    AssertEqual(3, cards.Count);
    AssertEqual(1536, cards[0].X);
    AssertEqual(912, cards[0].Y);
    AssertEqual(1536, cards[1].X);
    AssertEqual(808, cards[1].Y);
}

static void FullscreenTopmostPolicy()
{
    var monitor = new WindowBounds(0, 0, 1920, 1080);

    AssertFalse(
        OverlayTopmostPolicy.ShouldUseTopmost(new WindowBounds(0, 0, 1920, 1080), monitor),
        "A fullscreen foreground window should stay above the overlay.");
    AssertTrue(
        OverlayTopmostPolicy.ShouldUseTopmost(new WindowBounds(100, 100, 1820, 980), monitor),
        "A regular foreground window should stay below the overlay.");
    AssertTrue(
        OverlayTopmostPolicy.ShouldUseTopmost(
            new WindowBounds(-8, -8, 1928, 1088),
            monitor,
            hasStandardFrame: true),
        "A maximized standard window should stay below the overlay even when the taskbar auto-hides.");
}

static void NotificationStoreTrimHistory()
{
    var store = new NotificationStore(2);
    store.Add(Snapshot("One", "one", id: 1, displayId: 101) with { ReceivedAt = DateTimeOffset.Parse("2026-06-10T08:00:01Z") });
    store.Add(Snapshot("Two", "two", id: 2, displayId: 102) with { ReceivedAt = DateTimeOffset.Parse("2026-06-10T08:00:02Z") });
    store.Add(Snapshot("Three", "three", id: 3, displayId: 103) with { ReceivedAt = DateTimeOffset.Parse("2026-06-10T08:00:03Z") });

    AssertEqual(2, store.History.Count);
    AssertTrue(store.History.Any(item => item.DisplayId == 103), "Newest notification should stay in history.");
    AssertTrue(store.History.Any(item => item.DisplayId == 102), "Second newest notification should stay in history.");
    AssertFalse(store.History.Any(item => item.DisplayId == 101), "Oldest notification should be trimmed.");
}

static NotificationSnapshot Snapshot(string appName, string appUserModelId, uint id = 1, long displayId = 201)
{
    return new NotificationSnapshot(
        id,
        displayId,
        appName,
        appUserModelId,
        "Title",
        "Body",
        DateTimeOffset.Parse("2026-06-10T08:00:00Z"),
        DateTimeOffset.Parse("2026-06-10T08:00:01Z"),
        ["Title", "Body"],
        [],
        null,
        null,
        true,
        true);
}

static void AssertEqual<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
    {
        throw new InvalidOperationException($"Expected {expected}, got {actual}.");
    }
}

static void AssertTrue(bool value, string message)
{
    if (!value)
    {
        throw new InvalidOperationException(message);
    }
}

static void AssertFalse(bool value, string message)
{
    if (value)
    {
        throw new InvalidOperationException(message);
    }
}
