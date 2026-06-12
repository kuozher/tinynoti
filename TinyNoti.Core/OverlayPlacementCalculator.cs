namespace TinyNoti.Core;

public enum OverlayAnchor
{
    TopLeft,
    TopRight,
    BottomLeft,
    BottomRight
}

public sealed record ScreenWorkArea(double X, double Y, double Width, double Height, double Scale);

public sealed record OverlayCardPosition(double X, double Y);

public static class OverlayPlacementCalculator
{
    public static IReadOnlyList<OverlayCardPosition> Calculate(
        ScreenWorkArea area,
        OverlayAnchor anchor,
        double offsetX,
        double offsetY,
        double cardWidth,
        double cardHeight,
        int count,
        double gap)
    {
        if (count <= 0)
        {
            return Array.Empty<OverlayCardPosition>();
        }

        var positions = new List<OverlayCardPosition>(count);
        var left = anchor is OverlayAnchor.TopLeft or OverlayAnchor.BottomLeft
            ? area.X + offsetX
            : area.X + area.Width - offsetX - cardWidth;
        var firstTop = anchor is OverlayAnchor.TopLeft or OverlayAnchor.TopRight
            ? area.Y + offsetY
            : area.Y + area.Height - offsetY - cardHeight;
        var direction = anchor is OverlayAnchor.TopLeft or OverlayAnchor.TopRight ? 1 : -1;

        for (var index = 0; index < count; index++)
        {
            var top = firstTop + direction * index * (cardHeight + gap);
            positions.Add(new OverlayCardPosition(Math.Round(left), Math.Round(top)));
        }

        return positions;
    }
}
