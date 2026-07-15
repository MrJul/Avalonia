using Avalonia.Layout;
using Avalonia.Layout.Pipeline;
using Avalonia.Metadata;
using Avalonia.Platform;
using Avalonia.Styling;

namespace Avalonia.Controls
{
    /// <summary>
    /// Base class for controls which decorate a single child control.
    /// </summary>
    public class Decorator : Control
    {
        /// <summary>
        /// Defines the <see cref="Child"/> property.
        /// </summary>
        public static readonly StyledProperty<Control?> ChildProperty =
            AvaloniaProperty.Register<Decorator, Control?>(nameof(Child));

        /// <summary>
        /// Defines the <see cref="Padding"/> property.
        /// </summary>
        public static readonly StyledProperty<Thickness> PaddingProperty =
            AvaloniaProperty.Register<Decorator, Thickness>(nameof(Padding), validate: MarginProperty.ValidateValue);

        /// <summary>
        /// Initializes static members of the <see cref="Decorator"/> class.
        /// </summary>
        static Decorator()
        {
            AffectsMeasure<Decorator>(ChildProperty, PaddingProperty);
            ChildProperty.Changed.AddClassHandler<Decorator>((x, e) => x.ChildChanged(e));
        }

        /// <summary>
        /// Gets or sets the decorated control.
        /// </summary>
        [Content]
        public Control? Child
        {
            get => GetValue(ChildProperty);
            set => SetValue(ChildProperty, value);
        }

        /// <summary>
        /// Gets or sets the padding to place around the <see cref="Child"/> control.
        /// </summary>
        public Thickness Padding
        {
            get => GetValue(PaddingProperty);
            set => SetValue(PaddingProperty, value);
        }

        protected override LayoutAlgorithm ComputeLayoutAlgorithm()
        {
            var inputs = LayoutNodeInputs.FromLayoutable(this);
            return new DecoratorLayoutAlgorithm(inputs, GetLayoutPadding(inputs.UseLayoutRounding, inputs.LayoutScale));
        }

        /// <summary>
        /// Gets the total thickness placed around the <see cref="Child"/> during layout,
        /// captured by the layout pipeline algorithm. The classic engine rounds each thickness
        /// on every pass (see <see cref="LayoutHelper.MeasureChild(Layoutable, Size, Thickness)"/>);
        /// the pipeline captures the result once since the scale is constant during a frame.
        /// </summary>
        private protected virtual Thickness GetLayoutPadding(bool useLayoutRounding, double scale)
            => useLayoutRounding ? LayoutHelper.RoundLayoutThickness(Padding, scale) : Padding;

        /// <summary>
        /// A decorator lays out its single <see cref="Child"/>, like its classic
        /// measure/arrange implementations do — never other visual children.
        /// </summary>
        protected internal override int GetLayoutChildrenCount() => Child is null ? 0 : 1;

        /// <inheritdoc cref="GetLayoutChildrenCount"/>
        protected internal override Layoutable? GetLayoutChild(int index) => Child;

        /// <inheritdoc/>
        protected override Size MeasureOverride(Size availableSize)
        {
            return LayoutHelper.MeasureChild(Child, availableSize, Padding);
        }

        /// <inheritdoc/>
        protected override Size ArrangeOverride(Size finalSize)
        {
            return LayoutHelper.ArrangeChild(Child, finalSize, Padding);
        }

        /// <summary>
        /// Called when the <see cref="Child"/> property changes.
        /// </summary>
        /// <param name="e">The event args.</param>
        private void ChildChanged(AvaloniaPropertyChangedEventArgs e)
        {
            var oldChild = (Control?)e.OldValue;
            var newChild = (Control?)e.NewValue;

            if (oldChild != null)
            {
                ((ISetLogicalParent)oldChild).SetParent(null);
                LogicalChildren.Clear();
                VisualChildren.Remove(oldChild);
            }

            if (newChild != null)
            {
                ((ISetLogicalParent)newChild).SetParent(this);
                VisualChildren.Add(newChild);
                LogicalChildren.Add(newChild);
            }
        }
    }
}
