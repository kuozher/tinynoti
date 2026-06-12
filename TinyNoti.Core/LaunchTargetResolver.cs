using System.Diagnostics;
using System.Text.RegularExpressions;

namespace TinyNoti.Core;

public enum LaunchTargetKind
{
    None,
    Url,
    App
}

public sealed record LaunchHint(LaunchTargetKind Kind, string? Target, string Reason)
{
    public static LaunchHint None(string reason) => new(LaunchTargetKind.None, null, reason);
}

public sealed record AppLaunchRule(
    string Name,
    string AppPattern,
    string TextPattern,
    string TargetTemplate);

public sealed class LaunchTargetResolver
{
    private static readonly Regex UrlPattern = new(
        @"https?://[^\s<>'"")\]]+",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly IReadOnlyList<AppLaunchRule> _rules;

    public LaunchTargetResolver(IReadOnlyList<AppLaunchRule>? rules = null)
    {
        _rules = rules ?? Array.Empty<AppLaunchRule>();
    }

    public static LaunchHint Resolve(NotificationSnapshot snapshot)
    {
        return new LaunchTargetResolver().ResolveBestEffort(snapshot);
    }

    public LaunchHint ResolveBestEffort(NotificationSnapshot snapshot)
    {
        var urlMatch = UrlPattern.Match(snapshot.SearchText);
        if (urlMatch.Success)
        {
            return new LaunchHint(LaunchTargetKind.Url, TrimTrailingPunctuation(urlMatch.Value), "URL found in notification text.");
        }

        foreach (var rule in _rules)
        {
            if (!MatchesApp(rule, snapshot))
            {
                continue;
            }

            var textMatch = Regex.Match(snapshot.SearchText, rule.TextPattern, RegexOptions.IgnoreCase);
            if (textMatch.Success)
            {
                return new LaunchHint(LaunchTargetKind.Url, ApplyTemplate(rule.TargetTemplate, textMatch), $"Matched {rule.Name} launch rule.");
            }
        }

        if (!string.IsNullOrWhiteSpace(snapshot.AppUserModelId))
        {
            return new LaunchHint(LaunchTargetKind.App, snapshot.AppUserModelId, "Fallback to source app launch.");
        }

        return LaunchHint.None("No launch target available.");
    }

    public static bool TryOpen(LaunchHint hint)
    {
        if (hint.Kind == LaunchTargetKind.None || string.IsNullOrWhiteSpace(hint.Target))
        {
            return false;
        }

        try
        {
            var startInfo = hint.Kind == LaunchTargetKind.Url
                ? new ProcessStartInfo(hint.Target) { UseShellExecute = true }
                : new ProcessStartInfo("explorer.exe", $"shell:AppsFolder\\{hint.Target}") { UseShellExecute = true };

            Process.Start(startInfo);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool MatchesApp(AppLaunchRule rule, NotificationSnapshot snapshot)
    {
        return snapshot.AppName.Contains(rule.AppPattern, StringComparison.OrdinalIgnoreCase)
            || snapshot.AppUserModelId.Contains(rule.AppPattern, StringComparison.OrdinalIgnoreCase);
    }

    private static string ApplyTemplate(string template, Match match)
    {
        var result = template;
        foreach (var name in match.Groups.Keys)
        {
            if (int.TryParse(name, out _))
            {
                continue;
            }

            result = result.Replace("${" + name + "}", match.Groups[name].Value, StringComparison.Ordinal);
        }

        return result;
    }

    private static string TrimTrailingPunctuation(string value)
    {
        return value.TrimEnd('.', ',', ';', ':', '!', '?');
    }
}
