using System;
using Avalonia.Metadata;

namespace Avalonia.Layout.Pipeline;

/// <summary>
/// Describes how the size made available to each child of a container relates to its siblings
/// during the measure stage of the <see cref="LayoutPipeline"/>.
/// </summary>
[Unstable]
public enum LayoutChildrenDependency
{
    /// <summary>
    /// The size available to each child derives from the size available to the container alone:
    /// children can be measured in any order, possibly in parallel.
    /// </summary>
    Independent,

    /// <summary>
    /// The size available to a child depends on the sizes measured for the preceding children:
    /// children are measured sequentially, in order. Their subtrees may still be laid out in
    /// parallel internally.
    /// </summary>
    Sequential,
}

/// <summary>
/// The declarative layout protocol consumed by the experimental <see cref="LayoutPipeline"/>,
/// replacing the imperative <see cref="Layoutable.MeasureOverride"/> and
/// <see cref="Layoutable.ArrangeOverride"/> methods. Controls opt in by returning an instance
/// from <see cref="Layoutable.LayoutAlgorithm"/>.
/// </summary>
/// <remarks>
/// Implementations MUST be pure: the pipeline invokes these methods outside the UI thread,
/// potentially concurrently for several children or nodes at once. They must not read the
/// property system, mutate the tree, or raise events — any state they need must be captured
/// before the layout pass starts (i.e. before or during the snapshot stage).
/// The measure/arrange chrome handled by the classic engine (margins, min/max constraints,
/// alignment and layout rounding) is applied by the pipeline itself: implementations only
/// deal with content and children.
/// </remarks>
[Unstable]
public abstract class LayoutAlgorithm(LayoutNodeInputs inputs)
{
    private readonly LayoutNodeInputs _inputs = inputs;

    public ref readonly LayoutNodeInputs Inputs
        => ref _inputs;

    /// <summary>
    /// Gets how the size made available to each child relates to its siblings during measure.
    /// The pipeline may measure the children of
    /// <see cref="LayoutChildrenDependency.Independent"/> containers in parallel.
    /// </summary>
    public virtual LayoutChildrenDependency MeasureDependency => LayoutChildrenDependency.Independent;

    /// <summary>
    /// Measures the control's own content. Only called for nodes without children
    /// (text, images, shapes...).
    /// </summary>
    /// <param name="availableSize">The available size, with margins and min/max applied.</param>
    public virtual Size MeasureContent(Size availableSize) => default;

    /// <summary>
    /// Returns the size available to a child during measure.
    /// </summary>
    /// <param name="childIndex">The index of the child.</param>
    /// <param name="availableSize">The size available to the container.</param>
    /// <param name="measuredSiblings">
    /// The sizes measured for children 0..childIndex-1 when <see cref="MeasureDependency"/> is
    /// <see cref="LayoutChildrenDependency.Sequential"/>; empty when it is
    /// <see cref="LayoutChildrenDependency.Independent"/>.
    /// </param>
    public virtual Size GetChildAvailableSize(int childIndex, Size availableSize, ReadOnlySpan<Size> measuredSiblings)
        => availableSize;

    /// <summary>
    /// Computes the container's desired content size from the measured sizes of its children.
    /// </summary>
    /// <param name="availableSize">The size available to the container.</param>
    /// <param name="childSizes">
    /// The desired sizes measured for the children. Invisible children always measure to an
    /// empty size, like in the classic engine.
    /// </param>
    /// <param name="childrenVisibility">
    /// The visibility of each child. Containers that treat invisible children specially in
    /// their classic implementation (e.g. skipping them for spacing) must do the same here.
    /// </param>
    public virtual Size CombineChildSizes(Size availableSize, ReadOnlySpan<Size> childSizes, ReadOnlySpan<bool> childrenVisibility)
    {
        double width = 0.0, height = 0.0;

        foreach (ref readonly var childSize in childSizes)
        {
            if (childSize.Width > width)
                width = childSize.Width;

            if (childSize.Height > height)
                height = childSize.Height;
        }

        return new Size(width, height);
    }

    /// <summary>
    /// Computes the slot rect of each child, in the container's coordinate space, for the
    /// arrange stage. Each child then aligns itself within its slot exactly like in the
    /// classic engine.
    /// </summary>
    /// <param name="finalSize">The final arranged size of the container.</param>
    /// <param name="desiredSize">
    /// The desired size measured for the container, margins included — matching what reading
    /// DesiredSize inside a classic ArrangeOverride returns.
    /// </param>
    /// <param name="childSizes">The desired sizes measured for the children.</param>
    /// <param name="childrenVisibility">
    /// The visibility of each child. The slot of an invisible child is never used — it is
    /// skipped by the arrange stage, like classic containers skip arranging it.
    /// </param>
    /// <param name="childSlots">The slot rects to fill, one per child.</param>
    public virtual void ArrangeChildren(Size finalSize, Size desiredSize, ReadOnlySpan<Size> childSizes, ReadOnlySpan<bool> childrenVisibility, Span<Rect> childSlots)
        => childSlots.Fill(new Rect(finalSize));

    /// <summary>
    /// Publish-stage hook, called on the UI thread for arranged nodes after their desired size
    /// and bounds have been written back. Lets an algorithm flush results that must live on the
    /// control — e.g. a text layout shaped during the measure stage and used for rendering.
    /// This is the only <see cref="LayoutAlgorithm"/> method allowed to mutate the control.
    /// </summary>
    /// <param name="control">The live control the node was snapshotted from.</param>
    /// <param name="finalSize">The final arranged size of the control.</param>
    public virtual void OnPublish(Layoutable control, Size finalSize)
    {
    }
}
