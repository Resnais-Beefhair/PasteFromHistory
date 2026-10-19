namespace PasteFromHistory.UI;

internal static class CompanionPlacement
{
    internal static Point Locate(Point click, Size size, Rectangle work, Rectangle? menu, int gap)
    {
        Point Clamp(Point point) => new(Math.Clamp(point.X, work.Left, Math.Max(work.Left, work.Right - size.Width)),
            Math.Clamp(point.Y, work.Top, Math.Max(work.Top, work.Bottom - size.Height)));
        // Prefer immediately above the click: custom menus normally open below it.
        var candidates = new List<Point>
        {
            new(click.X + gap, click.Y - size.Height - gap),
            new(click.X + gap, click.Y + gap),
            new(click.X - size.Width - gap, click.Y + gap),
            new(click.X - size.Width - gap, click.Y - size.Height - gap)
        };
        if (menu is { } bounds)
        {
            candidates.AddRange([
                new(click.X, bounds.Top - size.Height - gap),
                new(click.X, bounds.Bottom + gap),
                new(bounds.Left - size.Width - gap, click.Y),
                new(bounds.Right + gap, click.Y)]);
        }
        var positions = candidates.Select(Clamp).Distinct().ToArray();
        var free = positions.Where(p => menu == null || !new Rectangle(p, size).IntersectsWith(menu.Value)).ToArray();
        // Distance to the button's nearest edge, not its top-left corner. This
        // keeps it near the pointer at all four screen edges and negative origins.
        return (free.Length > 0 ? free : positions).MinBy(p =>
        {
            var dx = (long)click.X - Math.Clamp(click.X, p.X, p.X + size.Width);
            var dy = (long)click.Y - Math.Clamp(click.Y, p.Y, p.Y + size.Height);
            return dx * dx + dy * dy;
        });
    }
}
