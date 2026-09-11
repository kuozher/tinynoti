using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace TinyNoti.Core;

public static class ToastPayloadParser
{
    private static bool IsSupportedUri(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length < 3)
        {
            return false;
        }

        if (value.Length > 2 && value[1] == ':' && (value[2] == '\\' || value[2] == '/'))
        {
            // Windows path like C:\ or C:/
            return false;
        }

        if (Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            return !string.IsNullOrWhiteSpace(uri.Scheme) &&
                   uri.Scheme.Length > 1 &&
                   !uri.IsFile &&
                   char.IsLetter(value[0]);
        }

        return false;
    }

    public static LaunchHint? TryResolve(string rawPayload, string appName, string appUserModelId)
    {
        if (string.IsNullOrWhiteSpace(rawPayload))
        {
            return null;
        }

        string? launch = null;
        try
        {
            var doc = XDocument.Parse(rawPayload);
            var root = doc.Root;
            if (root is not null && string.Equals(root.Name.LocalName, "toast", StringComparison.OrdinalIgnoreCase))
            {
                launch = root.Attribute("launch")?.Value?.Trim();

                if (string.IsNullOrWhiteSpace(launch))
                {
                    launch = root.Descendants()
                        .Where(e => string.Equals(e.Name.LocalName, "action", StringComparison.OrdinalIgnoreCase))
                        .Select(e => e.Attribute("arguments")?.Value?.Trim())
                        .FirstOrDefault(arg => !string.IsNullOrWhiteSpace(arg));
                }
            }
        }
        catch
        {
            // If rawPayload isn't XML (e.g. plain text or already raw launch args), continue with rawPayload as fallback
            launch = rawPayload.Trim();
        }

        if (string.IsNullOrWhiteSpace(launch))
        {
            return null;
        }

        // 1. Direct Web URLs or Protocol URIs (e.g. https://, slack://, asana://, msteams:/)
        if (IsSupportedUri(launch))
        {
            return new LaunchHint(LaunchTargetKind.Url, launch, "Direct URL or protocol URI from notification payload.");
        }

        // 3. Slack Payload Handling
        if (IsSlack(appName, appUserModelId) || launch.Contains("team_id") || launch.Contains("channel_id"))
        {
            var slackHint = TryResolveSlackLaunch(launch);
            if (slackHint is not null)
            {
                return slackHint;
            }
        }

        // 4. Asana Payload Handling
        if (IsAsana(appName, appUserModelId))
        {
            var asanaHint = TryResolveAsanaLaunch(launch);
            if (asanaHint is not null)
            {
                return asanaHint;
            }
        }

        // 5. Generic JSON payload checking for url/link/target property
        if (launch.StartsWith('{') && launch.EndsWith('}'))
        {
            var genericJsonHint = TryResolveGenericJson(launch);
            if (genericJsonHint is not null)
            {
                return genericJsonHint;
            }
        }

        return null;
    }

    private static bool IsSlack(string appName, string appUserModelId)
    {
        return appName.Contains("slack", StringComparison.OrdinalIgnoreCase) ||
               appUserModelId.Contains("slack", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsAsana(string appName, string appUserModelId)
    {
        return appName.Contains("asana", StringComparison.OrdinalIgnoreCase) ||
               appUserModelId.Contains("asana", StringComparison.OrdinalIgnoreCase);
    }

    private static LaunchHint? TryResolveSlackLaunch(string launch)
    {
        string? team = null;
        string? channel = null;
        string? ts = null;

        // Try JSON
        if (launch.StartsWith('{') && launch.EndsWith('}'))
        {
            try
            {
                using var jsonDoc = JsonDocument.Parse(launch);
                var root = jsonDoc.RootElement;

                // Check for direct url/uri in json
                if (TryGetStringProperty(root, out var directUrl, "url", "uri", "deep_link", "link") &&
                    !string.IsNullOrWhiteSpace(directUrl))
                {
                    if (IsSupportedUri(directUrl))
                    {
                        return new LaunchHint(LaunchTargetKind.Url, directUrl, "URL in Slack JSON payload.");
                    }
                }

                TryGetStringProperty(root, out team, "team_id", "team", "teamId");
                TryGetStringProperty(root, out channel, "channel_id", "channel", "channelId", "id");
                TryGetStringProperty(root, out ts, "message_ts", "ts", "thread_ts", "messageTs");
            }
            catch
            {
                // Not valid JSON
            }
        }

        // Try query string format (e.g. team_id=...&channel_id=...)
        if (string.IsNullOrWhiteSpace(channel))
        {
            var queryParams = ParseQueryString(launch);
            if (queryParams.Count > 0)
            {
                queryParams.TryGetValue("team_id", out team);
                if (team is null) queryParams.TryGetValue("team", out team);

                queryParams.TryGetValue("channel_id", out channel);
                if (channel is null) queryParams.TryGetValue("channel", out channel);
                if (channel is null) queryParams.TryGetValue("id", out channel);

                queryParams.TryGetValue("message_ts", out ts);
                if (ts is null) queryParams.TryGetValue("ts", out ts);
                if (ts is null) queryParams.TryGetValue("thread_ts", out ts);
            }
        }

        if (!string.IsNullOrWhiteSpace(channel))
        {
            var uriBuilder = new StringBuilder("slack://channel?");
            if (!string.IsNullOrWhiteSpace(team))
            {
                uriBuilder.Append($"team={Uri.EscapeDataString(team)}&");
            }
            uriBuilder.Append($"id={Uri.EscapeDataString(channel)}");
            if (!string.IsNullOrWhiteSpace(ts))
            {
                uriBuilder.Append($"&message={Uri.EscapeDataString(ts)}");
            }

            return new LaunchHint(LaunchTargetKind.Url, uriBuilder.ToString(), "Resolved Slack deep link from payload.");
        }

        return null;
    }

    private static LaunchHint? TryResolveAsanaLaunch(string launch)
    {
        if (launch.StartsWith('{') && launch.EndsWith('}'))
        {
            try
            {
                using var jsonDoc = JsonDocument.Parse(launch);
                var root = jsonDoc.RootElement;
                if (TryGetStringProperty(root, out var directUrl, "url", "uri", "link") && !string.IsNullOrWhiteSpace(directUrl))
                {
                    return new LaunchHint(LaunchTargetKind.Url, directUrl, "URL in Asana JSON payload.");
                }

                if (TryGetStringProperty(root, out var taskId, "task_id", "taskId", "id") && !string.IsNullOrWhiteSpace(taskId))
                {
                    return new LaunchHint(LaunchTargetKind.Url, $"https://app.asana.com/0/0/{taskId}", "Resolved Asana task from JSON payload.");
                }
            }
            catch
            {
            }
        }

        var match = Regex.Match(launch, @"(?:tasks?(?:[/\s:]+)|/0/0/|/0/\d+/)?(?<id>\d{6,})", RegexOptions.IgnoreCase);
        if (match.Success)
        {
            var id = match.Groups["id"].Value;
            return new LaunchHint(LaunchTargetKind.Url, $"https://app.asana.com/0/0/{id}", "Resolved Asana task link from payload.");
        }

        return null;
    }

    private static LaunchHint? TryResolveGenericJson(string launch)
    {
        try
        {
            using var jsonDoc = JsonDocument.Parse(launch);
            var root = jsonDoc.RootElement;
            if (TryGetStringProperty(root, out var url, "url", "uri", "target", "link", "deep_link", "deeplink") &&
                !string.IsNullOrWhiteSpace(url))
            {
                if (IsSupportedUri(url))
                {
                    return new LaunchHint(LaunchTargetKind.Url, url, "Resolved URL from generic JSON payload.");
                }
            }
        }
        catch
        {
        }

        return null;
    }

    private static bool TryGetStringProperty(JsonElement element, out string? value, params string[] propertyNames)
    {
        foreach (var prop in propertyNames)
        {
            if (element.TryGetProperty(prop, out var propVal))
            {
                if (propVal.ValueKind == JsonValueKind.String)
                {
                    value = propVal.GetString();
                    return true;
                }
                if (propVal.ValueKind == JsonValueKind.Number)
                {
                    value = propVal.GetRawText();
                    return true;
                }
            }
        }

        value = null;
        return false;
    }

    private static Dictionary<string, string> ParseQueryString(string query)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var tokens = query.Split('&', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var token in tokens)
        {
            var parts = token.Split('=', 2);
            if (parts.Length == 2)
            {
                dict[Uri.UnescapeDataString(parts[0])] = Uri.UnescapeDataString(parts[1]);
            }
            else if (parts.Length == 1)
            {
                dict[Uri.UnescapeDataString(parts[0])] = string.Empty;
            }
        }
        return dict;
    }
}
