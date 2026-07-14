using System;
using Avalonia.Media;
using Avalonia.Metadata;

namespace Avalonia.Layout.Pipeline;

/// <summary>
/// An <see cref="ILayoutManager"/> driving layout through the experimental
/// <see cref="LayoutPipeline"/> instead of the classic engine.
/// </summary>
/// <remarks>
/// The whole opted-in tree is laid out again on every pass: per-control invalidation
/// granularity, effective viewport notifications and the layout/styling feedback loop are not
/// implemented yet. If the root doesn't provide a <see cref="LayoutAlgorithm"/>, passes are
/// no-ops and nothing renders.
/// </remarks>
[Unstable]
public sealed class PipelineLayoutManager : ILayoutManager
{
    private readonly ILayoutRoot _owner;
    private readonly Func<Size>? _availableSize;
    private readonly LayoutPipeline _pipeline = new();
    private readonly Action _invokeOnRender;
    private bool _dirty = true;
    private bool _queued;
    private bool _running;
    private bool _disposed;

    /// <param name="owner">The layout root owning this manager.</param>
    /// <param name="availableSize">
    /// Returns the size available to the root on each pass (e.g. the client size of a top
    /// level), or null to measure the root unconstrained and arrange it to its desired size.
    /// </param>
    internal PipelineLayoutManager(ILayoutRoot owner, Func<Size>? availableSize = null)
    {
        _owner = owner;
        _availableSize = availableSize;
        _invokeOnRender = () =>
        {
            _queued = false;
            ExecuteLayoutPass();
        };
    }

    public event EventHandler? LayoutUpdated;

    public void InvalidateMeasure(Layoutable control) => Invalidate();

    public void InvalidateArrange(Layoutable control) => Invalidate();

    public void ExecuteLayoutPass()
    {
        if (_disposed || !_dirty || _running)
            return;

        if (_owner.RootVisual is not { } root || root.GetLayoutAlgorithm() is null)
            return;

        _dirty = false;

        try
        {
            _running = true;

            var availableSize = _availableSize?.Invoke() ?? Size.Infinity;
            var arrangeRect = double.IsFinite(availableSize.Width) && double.IsFinite(availableSize.Height) ?
                new Rect(availableSize) :
                (Rect?)null;

            _pipeline.ExecuteFrame(root, availableSize, arrangeRect);
        }
        finally
        {
            _running = false;
        }

        // Publishing results may have invalidated controls again: schedule a new pass.
        if (_dirty)
            Queue();

        LayoutUpdated?.Invoke(this, EventArgs.Empty);
    }

    public void ExecuteInitialLayoutPass()
    {
        _dirty = true;
        ExecuteLayoutPass();
    }

    public void RegisterEffectiveViewportListener(Layoutable control)
    {
        // Effective viewport notifications are not implemented by the pipeline yet.
    }

    public void UnregisterEffectiveViewportListener(Layoutable control)
    {
    }

    public void Dispose() => _disposed = true;

    private void Invalidate()
    {
        if (_disposed)
            return;

        _dirty = true;

        if (!_running)
            Queue();
    }

    private void Queue()
    {
        if (_queued)
            return;

        _queued = true;
        MediaContext.Instance.BeginInvokeOnRender(_invokeOnRender);
    }
}
