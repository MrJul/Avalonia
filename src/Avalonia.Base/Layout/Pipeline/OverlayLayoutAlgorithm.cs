namespace Avalonia.Layout.Pipeline;

/// <summary>
/// A trivial algorithm with overlay semantics, equivalent to the default
/// <see cref="Layoutable.MeasureOverride"/>/<see cref="Layoutable.ArrangeOverride"/>:
/// every child receives the full available size and is arranged over the full final size,
/// and the desired size is the maximum of the children sizes.
/// </summary>
public sealed class OverlayLayoutAlgorithm(LayoutNodeInputs inputs)
    : LayoutAlgorithm(inputs);
