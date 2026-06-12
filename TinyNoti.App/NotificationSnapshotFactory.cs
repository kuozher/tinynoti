using TinyNoti.Core;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Text;
using Windows.UI.Notifications;
using Windows.UI.Notifications.Management;
using Windows.Storage.Streams;

namespace TinyNoti.App;

public static class NotificationSnapshotFactory
{
    private static long _nextDisplayId;

    public static async Task<NotificationSnapshot> FromUserNotificationAsync(UserNotification userNotification)
    {
        var appName = ReadAppName(userNotification);
        var appUserModelId = ReadAppUserModelId(userNotification);
        var lines = ReadTextLines(userNotification.Notification).ToArray();
        var images = ReadImages(lines).ToArray();
        var appIconUri = await ReadAppIconAsync(userNotification, appUserModelId, appName);
        var title = lines.FirstOrDefault() ?? string.Empty;
        var body = string.Join(Environment.NewLine, lines.Skip(1));
        var createdAt = userNotification.CreationTime;

        var snapshot = new NotificationSnapshot(
            userNotification.Id,
            Interlocked.Increment(ref _nextDisplayId),
            appName,
            appUserModelId,
            title,
            body,
            createdAt,
            DateTimeOffset.Now,
            lines,
            images,
            appIconUri,
            null,
            true,
            true);

        var hint = LaunchTargetResolver.Resolve(snapshot);
        return snapshot with { ActivationHint = hint };
    }

    private static async Task<string> ReadAppIconAsync(UserNotification userNotification, string appUserModelId, string appName)
    {
        var folder = GetIconCacheFolder();
        Directory.CreateDirectory(folder);

        var safeName = SafeFileStem(appUserModelId, "unknown");
        var cachedPath = Path.Combine(folder, $"{safeName}.png");
        if (File.Exists(cachedPath))
        {
            return cachedPath;
        }

        try
        {
            var bytes = await ReadBestLogoBytesAsync(userNotification);
            await File.WriteAllBytesAsync(cachedPath, bytes);
            return cachedPath;
        }
        catch (Exception ex)
        {
            LogIconFailure(appName, appUserModelId, ex);
            return EnsurePlaceholderIcon(folder, appName, appUserModelId);
        }
    }

    private static async Task<byte[]> ReadBestLogoBytesAsync(UserNotification userNotification)
    {
        var failures = new List<string>();
        foreach (var size in new[] { 64, 48, 32, 256 })
        {
            try
            {
                var logo = userNotification.AppInfo.DisplayInfo.GetLogo(new Windows.Foundation.Size(size, size));
                using var source = await logo.OpenReadAsync();
                if (source.Size is 0 or > 1_048_576)
                {
                    failures.Add($"{size}px returned {source.Size} bytes");
                    continue;
                }

                var buffer = new Windows.Storage.Streams.Buffer((uint)source.Size);
                await source.ReadAsync(buffer, (uint)source.Size, InputStreamOptions.None);
                using var reader = DataReader.FromBuffer(buffer);
                var bytes = new byte[buffer.Length];
                reader.ReadBytes(bytes);
                if (bytes.Length > 0)
                {
                    return bytes;
                }

                failures.Add($"{size}px returned an empty buffer");
            }
            catch (Exception ex)
            {
                failures.Add($"{size}px {ex.GetType().Name}: {ex.Message}");
            }
        }

        throw new InvalidOperationException($"Unable to read app logo. {string.Join("; ", failures)}");
    }

    private static string EnsurePlaceholderIcon(string folder, string appName, string appUserModelId)
    {
        var name = string.IsNullOrWhiteSpace(appName) ? appUserModelId : appName;
        var safeName = SafeFileStem(appUserModelId, "unknown");
        var path = Path.Combine(folder, $"{safeName}.placeholder.png");
        if (File.Exists(path))
        {
            return path;
        }

        using var bitmap = new System.Drawing.Bitmap(96, 96, PixelFormat.Format32bppArgb);
        using var graphics = System.Drawing.Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.HighQuality;
        graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        graphics.Clear(System.Drawing.Color.Transparent);

        using var background = new LinearGradientBrush(
            new System.Drawing.Rectangle(0, 0, 96, 96),
            ColorForKey(appUserModelId),
            System.Drawing.Color.FromArgb(255, 241, 245, 249),
            LinearGradientMode.ForwardDiagonal);
        using var textBrush = new System.Drawing.SolidBrush(System.Drawing.Color.White);
        using var font = new System.Drawing.Font("Segoe UI Variable Text", 32, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Pixel);
        using var format = new System.Drawing.StringFormat
        {
            Alignment = System.Drawing.StringAlignment.Center,
            LineAlignment = System.Drawing.StringAlignment.Center
        };

        using var iconShape = CreateRoundedRectanglePath(new System.Drawing.Rectangle(0, 0, 96, 96), 18);
        graphics.FillPath(background, iconShape);
        graphics.DrawString(GetInitials(name), font, textBrush, new System.Drawing.RectangleF(0, 1, 96, 96), format);
        bitmap.Save(path, ImageFormat.Png);
        return path;
    }

    private static string GetIconCacheFolder()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "TinyNoti",
            "IconCache");
    }

    private static string SafeFileStem(string value, string fallback)
    {
        var result = string.Concat(value.Select(ch => char.IsLetterOrDigit(ch) ? ch : '_')).Trim('_');
        return string.IsNullOrWhiteSpace(result) ? fallback : result;
    }

    private static string GetInitials(string value)
    {
        var tokens = value
            .Split([' ', '.', '-', '_'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(static token => token.FirstOrDefault(char.IsLetterOrDigit))
            .Where(static ch => ch != default)
            .Take(2)
            .ToArray();

        if (tokens.Length > 0)
        {
            return new string(tokens).ToUpperInvariant();
        }

        var first = value.FirstOrDefault(char.IsLetterOrDigit);
        return first == default ? "?" : char.ToUpperInvariant(first).ToString();
    }

    private static System.Drawing.Color ColorForKey(string key)
    {
        var hash = 2166136261u;
        foreach (var ch in key)
        {
            hash ^= ch;
            hash *= 16777619;
        }

        var hue = hash % 360;
        return HslToRgb(hue, 0.58, 0.46);
    }

    private static System.Drawing.Color HslToRgb(double hue, double saturation, double lightness)
    {
        var chroma = (1 - Math.Abs(2 * lightness - 1)) * saturation;
        var x = chroma * (1 - Math.Abs(hue / 60 % 2 - 1));
        var match = lightness - chroma / 2;
        var (r, g, b) = hue switch
        {
            < 60 => (chroma, x, 0d),
            < 120 => (x, chroma, 0d),
            < 180 => (0d, chroma, x),
            < 240 => (0d, x, chroma),
            < 300 => (x, 0d, chroma),
            _ => (chroma, 0d, x)
        };

        return System.Drawing.Color.FromArgb(
            255,
            (int)Math.Round((r + match) * 255),
            (int)Math.Round((g + match) * 255),
            (int)Math.Round((b + match) * 255));
    }

    private static GraphicsPath CreateRoundedRectanglePath(System.Drawing.Rectangle bounds, int radius)
    {
        var diameter = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    private static void LogIconFailure(string appName, string appUserModelId, Exception exception)
    {
        try
        {
            var folder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "TinyNoti",
                "Logs");
            Directory.CreateDirectory(folder);

            var path = Path.Combine(folder, "icon.log");
            if (File.Exists(path) && new FileInfo(path).Length > 262_144)
            {
                File.Move(path, Path.Combine(folder, "icon.previous.log"), true);
            }

            var line = new StringBuilder()
                .Append(DateTimeOffset.Now.ToString("O"))
                .Append(" | ")
                .Append(appName)
                .Append(" | ")
                .Append(appUserModelId)
                .Append(" | ")
                .Append(exception.GetType().Name)
                .Append(": ")
                .Append(exception.Message.ReplaceLineEndings(" "))
                .AppendLine()
                .ToString();

            File.AppendAllText(path, line);
        }
        catch
        {
            // Icon diagnostics should never block notification rendering.
        }
    }

    private static string ReadAppName(UserNotification userNotification)
    {
        try
        {
            return userNotification.AppInfo.DisplayInfo.DisplayName;
        }
        catch
        {
            return ReadAppUserModelId(userNotification);
        }
    }

    private static string ReadAppUserModelId(UserNotification userNotification)
    {
        try
        {
            return userNotification.AppInfo.AppUserModelId;
        }
        catch
        {
            return "unknown";
        }
    }

    private static IEnumerable<string> ReadTextLines(Notification notification)
    {
        foreach (var binding in notification.Visual.Bindings)
        {
            foreach (var text in binding.GetTextElements())
            {
                if (!string.IsNullOrWhiteSpace(text.Text))
                {
                    yield return text.Text.Trim();
                }
            }
        }
    }

    private static IEnumerable<ImageCandidate> ReadImages(IEnumerable<string> lines)
    {
        foreach (var line in lines)
        {
            var tokens = line.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var token in tokens)
            {
                var uri = token.TrimEnd('.', ',', ';', ')', ']');
                if (IsImageUri(uri))
                {
                    yield return new ImageCandidate(uri, "text-uri");
                }
            }
        }
    }

    private static bool IsImageUri(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            return false;
        }

        if (uri.Scheme is not ("http" or "https" or "file"))
        {
            return false;
        }

        var path = uri.AbsolutePath;
        return path.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".webp", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".gif", StringComparison.OrdinalIgnoreCase);
    }
}
