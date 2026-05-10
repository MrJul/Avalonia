using System.Collections.Specialized;
using Avalonia.Collections;
using Avalonia.Controls.Metadata;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;

namespace Avalonia.Controls;

/// <summary>
/// A read-only tabular control that presents items in configurable columns.
/// </summary>
[TemplatePart(PartColumnHeadersPresenter, typeof(TableViewColumnHeadersPresenter))]
public class TableView : ListBox
{
    private const string PartColumnHeadersPresenter = "PART_ColumnHeadersPresenter";

    private TableViewColumnHeadersPresenter? _columnHeadersPresenter;

    /// <summary>
    /// Defines the <see cref="Columns"/> property.
    /// </summary>
    public static readonly DirectProperty<TableView, AvaloniaList<TableViewColumn>> ColumnsProperty =
        AvaloniaProperty.RegisterDirect<TableView, AvaloniaList<TableViewColumn>>(nameof(Columns), o => o.Columns);

    /// <summary>
    /// Gets the collection of columns displayed by this <see cref="TableView"/>.
    /// </summary>
    public AvaloniaList<TableViewColumn> Columns { get; } = [];

    /// <summary>
    /// Initializes a new instance of <see cref="TableView"/>.
    /// </summary>
    public TableView()
        => Columns.CollectionChanged += OnColumnsChanged;

    /// <inheritdoc/>
    protected internal override Control CreateContainerForItemOverride(object? item, int index, object? recycleKey)
        => new TableViewRow();

    /// <inheritdoc/>
    protected internal override void PrepareContainerForItemOverride(Control container, object? item, int index)
    {
        base.PrepareContainerForItemOverride(container, item, index);

        if (container is TableViewRow row)
        {
            row.Columns = Columns;
            row.RebuildCells();
        }
    }

    /// <inheritdoc/>
    protected internal override bool NeedsContainerOverride(object? item, int index, out object? recycleKey)
        => NeedsContainer<TableViewRow>(item, out recycleKey);

    protected internal override void ClearContainerForItemOverride(Control element)
    {
        base.ClearContainerForItemOverride(element);

        if (element is TableViewRow row)
        {
            row.Columns = null;
            row.RebuildCells();
        }
    }

    /// <inheritdoc/>
    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);

        SetPresenterColumns(null);
        _columnHeadersPresenter = e.NameScope.Find<TableViewColumnHeadersPresenter>(PartColumnHeadersPresenter);
        SetPresenterColumns(Columns);
    }

    private void SetPresenterColumns(AvaloniaList<TableViewColumn>? columns)
    {
        if (_columnHeadersPresenter is null)
            return;

        _columnHeadersPresenter.Columns = columns;
        _columnHeadersPresenter.RebuildHeaders();
    }

    private void OnColumnsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (!IsInitialized)
            return;

        foreach (var row in GetRealizedContainers())
        {
            if (row is TableViewRow tableViewRow)
                tableViewRow.RebuildCells();
            else
                row.InvalidateMeasure();
        }
    }
}
