using Avalonia.Collections;
using static Avalonia.Controls.Presenters.TableViewLayoutHelper;

namespace Avalonia.Controls.Presenters;

/// <summary>
/// Displays the column headers for a <see cref="TableView"/>.
/// Computes column widths (pixel and star) and exposes them via
/// <see cref="TableViewColumn.ActualWidth"/> so that <see cref="TableViewRowPresenter"/>
/// can align cells with the headers.
/// </summary>
public class TableViewColumnHeadersPresenter : Panel
{
    internal AvaloniaList<TableViewColumn>? Columns { get; set; }

    internal void RebuildHeaders()
    {
        Children.Clear();

        if (Columns is not { } columns)
            return;

        foreach (var column in columns)
        {
            var header = new TableViewColumnHeader
            {
                Column = column,
                [!ContentControl.ContentProperty] = column[!TableViewColumn.HeaderProperty],
                [!ContentControl.ContentTemplateProperty] = column[!TableViewColumn.HeaderTemplateProperty],
            };

            Children.Add(header);
        }

        InvalidateMeasure();
    }

    /// <inheritdoc/>
    protected override Size MeasureOverride(Size availableSize)
    {
        if (Columns is not { } columns)
            return default;

        UpdateActualWidths(columns, availableSize.Width);
        return MeasureRow(columns, Children, availableSize);
    }

    /// <inheritdoc/>
    protected override Size ArrangeOverride(Size finalSize)
    {
        if (Columns is not { } columns)
            return finalSize;

        UpdateActualWidths(columns, finalSize.Width);
        return ArrangeRow(columns, Children, finalSize);
    }
}
