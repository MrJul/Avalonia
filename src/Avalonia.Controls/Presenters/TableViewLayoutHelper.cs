using System;
using Avalonia.Collections;

namespace Avalonia.Controls.Presenters;

internal static class TableViewLayoutHelper
{
    /// <summary>
    /// Distributes <paramref name="availableWidth"/> among the columns.
    /// Pixel columns take their fixed size; remaining space is split proportionally among star columns.
    /// Auto is treated as 1*.
    /// </summary>
    public static void UpdateActualWidths(AvaloniaList<TableViewColumn> columns, double availableWidth)
    {
        if (columns.Count == 0)
            return;

        var fixedTotal = 0.0;
        var starTotal = 0.0;

        for (var i = 0; i < columns.Count; i++)
        {
            var width = columns[i].Width;
            if (width.IsAbsolute)
            {
                columns[i].ActualWidth = width.Value;
                fixedTotal += width.Value;
            }
            else
            {
                // Star or Auto — treat both as star
                starTotal += width.IsStar ? width.Value : 1.0;
            }
        }

        var starBudget = Math.Max(0.0, availableWidth - fixedTotal);

        for (var i = 0; i < columns.Count; i++)
        {
            var width = columns[i].Width;
            if (!width.IsAbsolute)
            {
                var share = width.IsStar ? width.Value : 1.0;
                columns[i].ActualWidth = starTotal > 0 ? share / starTotal * starBudget : 0;
            }
        }
    }

    public static bool NeedsActualWidths(AvaloniaList<TableViewColumn> columns)
        => columns.Count > 1 && double.IsNaN(columns[0].ActualWidth);

    public static Size MeasureRow(AvaloniaList<TableViewColumn> columns, Controls cells, Size availableSize)
    {
        if (cells.Count != columns.Count)
            return default;

        var totalWidth = 0.0;
        var totalHeight = 0.0;

        for (var i = 0; i < cells.Count; i++)
        {
            var child = cells[i];
            child.Measure(new Size(columns[i].ActualWidth, availableSize.Height));
            totalWidth += child.DesiredSize.Width;
            totalHeight = Math.Max(totalHeight, child.DesiredSize.Height);
        }

        return new Size(totalWidth, totalHeight);
    }

    public static Size ArrangeRow(AvaloniaList<TableViewColumn> columns, Controls cells, Size finalSize)
    {
        if (cells.Count != columns.Count)
            return finalSize;

        var x = 0.0;
        for (var i = 0; i < cells.Count; i++)
        {
            var width = columns[i].ActualWidth;
            columns[i].ActualWidth = width;
            cells[i].Arrange(new Rect(x, 0, width, finalSize.Height));
            x += width;
        }

        return finalSize;
    }
}
