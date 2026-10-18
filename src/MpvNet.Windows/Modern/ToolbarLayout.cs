namespace MpvNet.Windows.Modern;

public readonly record struct ToolbarFit(int VisibleCount, bool Overflow);

public static class ToolbarLayout
{
    // Widths include each button's real, DPI-scaled margins. Keep a prefix in
    // the saved order, reserving space for More only when it is actually needed.
    public static ToolbarFit Fit(int availableWidth, IReadOnlyList<int> itemWidths, int moreWidth, int maximumVisible = int.MaxValue)
    {
        long total = 0;
        foreach (int width in itemWidths) total += Math.Max(0, width);
        int limit = Math.Clamp(maximumVisible, 0, itemWidths.Count);
        if (itemWidths.Count <= limit && total <= Math.Max(0, availableWidth)) return new(itemWidths.Count, false);

        int budget = Math.Max(0, availableWidth - Math.Max(0, moreWidth));
        long used = 0;
        int count = 0;
        foreach (int width in itemWidths.Take(limit)) {
            used += Math.Max(0, width);
            if (used > budget) break;
            count++;
        }
        return new(count, true);
    }
}
