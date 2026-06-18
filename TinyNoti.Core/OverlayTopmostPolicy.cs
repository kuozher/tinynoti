namespace TinyNoti.Core;

public readonly record struct WindowBounds(int Left, int Top, int Right, int Bottom);

public static class OverlayTopmostPolicy
{
    private const int EdgeTolerance = 2;

    public static bool ShouldUseTopmost(
        WindowBounds foreground,
        WindowBounds monitor,
        bool hasStandardFrame = false)
    {
        var coversMonitor = foreground.Left <= monitor.Left + EdgeTolerance
            && foreground.Top <= monitor.Top + EdgeTolerance
            && foreground.Right >= monitor.Right - EdgeTolerance
            && foreground.Bottom >= monitor.Bottom - EdgeTolerance;

        return hasStandardFrame || !coversMonitor;
    }
}
