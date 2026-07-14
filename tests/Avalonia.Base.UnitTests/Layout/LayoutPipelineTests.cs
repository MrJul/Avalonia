using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Layout.Pipeline;
using Avalonia.Media;
using Avalonia.UnitTests;
using Xunit;

namespace Avalonia.Base.UnitTests.Layout
{
    public class LayoutPipelineTests : ScopedTestBase
    {
        [Theory]
        [InlineData(0, 0, 0, 0)]
        [InlineData(10, 5, 3, 2)]
        public void Leaf_Chrome_Matches_Classic_Engine(double l, double t, double r, double b)
        {
            foreach (var horizontalAlignment in Enum.GetValues<HorizontalAlignment>())
            {
                foreach (var verticalAlignment in Enum.GetValues<VerticalAlignment>())
                {
                    var classic = new ClassicFixedControl
                    {
                        Size = new Size(25, 15),
                        Margin = new Thickness(l, t, r, b),
                        HorizontalAlignment = horizontalAlignment,
                        VerticalAlignment = verticalAlignment,
                        MinWidth = 10,
                        MaxHeight = 40,
                    };

                    var piped = new PipelineControl(new FixedContentAlgorithm(new Size(25, 15)))
                    {
                        Margin = new Thickness(l, t, r, b),
                        HorizontalAlignment = horizontalAlignment,
                        VerticalAlignment = verticalAlignment,
                        MinWidth = 10,
                        MaxHeight = 40,
                    };

                    classic.Measure(new Size(100, 100));
                    classic.Arrange(new Rect(0, 0, 100, 100));

                    new LayoutPipeline().ExecuteFrame(piped, new Size(100, 100), new Rect(0, 0, 100, 100));

                    Assert.Equal(classic.DesiredSize, piped.DesiredSize);
                    Assert.Equal(classic.Bounds, piped.Bounds);
                }
            }
        }

        [Fact]
        public void Overlay_Container_Matches_Classic_Engine()
        {
            var classicChild1 = new ClassicFixedControl { Size = new Size(30, 10), Margin = new Thickness(2) };
            var classicChild2 = new ClassicFixedControl { Size = new Size(20, 40), HorizontalAlignment = HorizontalAlignment.Right };
            var classicParent = new ClassicPanel();
            classicParent.AddChild(classicChild1);
            classicParent.AddChild(classicChild2);

            var pipedChild1 = new PipelineControl(new FixedContentAlgorithm(new Size(30, 10))) { Margin = new Thickness(2) };
            var pipedChild2 = new PipelineControl(new FixedContentAlgorithm(new Size(20, 40))) { HorizontalAlignment = HorizontalAlignment.Right };
            var pipedParent = new PipelineControl();
            pipedParent.AddChild(pipedChild1);
            pipedParent.AddChild(pipedChild2);

            classicParent.Measure(new Size(100, 100));
            classicParent.Arrange(new Rect(0, 0, 100, 100));

            new LayoutPipeline().ExecuteFrame(pipedParent, new Size(100, 100), new Rect(0, 0, 100, 100));

            Assert.Equal(classicParent.DesiredSize, pipedParent.DesiredSize);
            Assert.Equal(classicParent.Bounds, pipedParent.Bounds);
            Assert.Equal(classicChild1.DesiredSize, pipedChild1.DesiredSize);
            Assert.Equal(classicChild1.Bounds, pipedChild1.Bounds);
            Assert.Equal(classicChild2.DesiredSize, pipedChild2.DesiredSize);
            Assert.Equal(classicChild2.Bounds, pipedChild2.Bounds);
        }

        [Fact]
        public void Sequential_Algorithm_Stacks_Children()
        {
            var child1 = new PipelineControl(new FixedContentAlgorithm(new Size(30, 10)));
            var child2 = new PipelineControl(new FixedContentAlgorithm(new Size(20, 20)));
            var child3 = new PipelineControl(new FixedContentAlgorithm(new Size(10, 30)));
            var parent = new PipelineControl(new VerticalStackAlgorithm());
            parent.AddChild(child1);
            parent.AddChild(child2);
            parent.AddChild(child3);

            new LayoutPipeline().ExecuteFrame(parent, new Size(100, 100));

            Assert.Equal(new Size(30, 60), parent.DesiredSize);
            Assert.Equal(new Rect(0, 0, 30, 60), parent.Bounds);
            Assert.Equal(new Rect(0, 0, 30, 10), child1.Bounds);
            Assert.Equal(new Rect(0, 10, 30, 20), child2.Bounds);
            Assert.Equal(new Rect(0, 30, 30, 30), child3.Bounds);
        }

        [Fact]
        public void Non_Opted_In_Children_Are_Skipped()
        {
            var optedIn = new PipelineControl(new FixedContentAlgorithm(new Size(30, 10)));
            var classic = new ClassicFixedControl { Size = new Size(500, 500) };
            var parent = new PipelineControl();
            parent.AddChild(optedIn);
            parent.AddChild(classic);

            new LayoutPipeline().ExecuteFrame(parent, new Size(100, 100));

            // The classic child was excluded from the pipeline: it contributed nothing to its
            // parent and was neither measured nor arranged.
            Assert.Equal(new Size(30, 10), parent.DesiredSize);
            Assert.Equal(default, classic.DesiredSize);
            Assert.False(classic.IsMeasureValid);
            Assert.Equal(default, classic.Bounds);
        }

        [Fact]
        public void Invisible_Nodes_Measure_As_Empty()
        {
            var visible = new PipelineControl(new FixedContentAlgorithm(new Size(30, 10)));
            var invisible = new PipelineControl(new FixedContentAlgorithm(new Size(500, 500))) { IsVisible = false };
            var parent = new PipelineControl();
            parent.AddChild(visible);
            parent.AddChild(invisible);

            new LayoutPipeline().ExecuteFrame(parent, new Size(100, 100));

            Assert.Equal(new Size(30, 10), parent.DesiredSize);
            Assert.Equal(default, invisible.DesiredSize);
            Assert.Equal(default, invisible.Bounds);
        }

        [Fact]
        public void Sequential_Containers_Lay_Out_In_Parallel_Deterministically()
        {
            var (parallelRoot, parallelNodes) = CreateSequentialTree();
            var (sequentialRoot, sequentialNodes) = CreateSequentialTree();

            new LayoutPipeline { ParallelismThreshold = 1 }
                .ExecuteFrame(parallelRoot, new Size(500, 500), new Rect(0, 0, 500, 500));
            new LayoutPipeline { ParallelismThreshold = int.MaxValue }
                .ExecuteFrame(sequentialRoot, new Size(500, 500), new Rect(0, 0, 500, 500));

            for (var i = 0; i < parallelNodes.Count; i++)
            {
                Assert.Equal(sequentialNodes[i].DesiredSize, parallelNodes[i].DesiredSize);
                Assert.Equal(sequentialNodes[i].Bounds, parallelNodes[i].Bounds);
            }

            static (PipelineControl Root, List<PipelineControl> Nodes) CreateSequentialTree()
            {
                var nodes = new List<PipelineControl>();
                var root = new PipelineControl(new VerticalStackAlgorithm());
                nodes.Add(root);

                for (var i = 0; i < 15; i++)
                {
                    var group = new PipelineControl(new VerticalStackAlgorithm());
                    nodes.Add(group);
                    root.AddChild(group);

                    for (var j = 0; j < 8; j++)
                    {
                        var leaf = new PipelineControl(new FixedContentAlgorithm(new Size(5 + j, 3 + i)));
                        nodes.Add(leaf);
                        group.AddChild(leaf);
                    }
                }

                return (root, nodes);
            }
        }

        [Fact]
        public void Exceptions_From_Worker_Items_Propagate_To_The_Caller()
        {
            var root = new PipelineControl();

            for (var i = 0; i < 8; i++)
            {
                var group = new PipelineControl();
                group.AddChild(new PipelineControl(new ThrowingAlgorithm()));
                root.AddChild(group);
            }

            var pipeline = new LayoutPipeline { ParallelismThreshold = 1 };

            var exception = Assert.Throws<InvalidOperationException>(
                () => pipeline.ExecuteFrame(root, new Size(100, 100)));

            Assert.Equal("boom", exception.Message);
        }

        private sealed class ThrowingAlgorithm : LayoutAlgorithm
        {
            public override Size MeasureContent(Size availableSize)
                => throw new InvalidOperationException("boom");
        }

        [Fact]
        public void Parallel_Execution_Matches_Sequential_Execution()
        {
            var (parallelRoot, parallelNodes) = CreateWideTree();
            var (sequentialRoot, sequentialNodes) = CreateWideTree();

            // Threshold 1 forces forking everywhere; int.MaxValue disables it entirely.
            new LayoutPipeline { ParallelismThreshold = 1 }
                .ExecuteFrame(parallelRoot, new Size(1000, 1000), new Rect(0, 0, 1000, 1000));
            new LayoutPipeline { ParallelismThreshold = int.MaxValue }
                .ExecuteFrame(sequentialRoot, new Size(1000, 1000), new Rect(0, 0, 1000, 1000));

            Assert.Equal(sequentialNodes.Count, parallelNodes.Count);

            for (var i = 0; i < parallelNodes.Count; i++)
            {
                Assert.Equal(sequentialNodes[i].DesiredSize, parallelNodes[i].DesiredSize);
                Assert.Equal(sequentialNodes[i].Bounds, parallelNodes[i].Bounds);
            }

            static (PipelineControl Root, List<Control> Nodes) CreateWideTree()
            {
                var nodes = new List<Control>();
                var root = new PipelineControl();
                nodes.Add(root);

                for (var i = 0; i < 20; i++)
                {
                    var group = new StackPanel
                    {
                        Margin = new Thickness(i % 3),
                        Spacing = i % 4,
                        Orientation = i % 2 == 0 ? Orientation.Vertical : Orientation.Horizontal,
                    };
                    nodes.Add(group);
                    root.AddChild(group);

                    for (var j = 0; j < 10; j++)
                    {
                        var leaf = new PipelineControl(new FixedContentAlgorithm(new Size(10 + i, 5 + j)))
                        {
                            HorizontalAlignment = j % 2 == 0 ? HorizontalAlignment.Left : HorizontalAlignment.Stretch,
                        };
                        nodes.Add(leaf);
                        group.Children.Add(leaf);
                    }
                }

                return (root, nodes);
            }
        }

        [Fact]
        public void Border_And_Decorator_Match_Classic_Engine()
        {
            var classicRoot = CreateTree(new ClassicFixedControl { Size = new Size(30, 10), HorizontalAlignment = HorizontalAlignment.Center });
            var pipedRoot = CreateTree(new PipelineControl(new FixedContentAlgorithm(new Size(30, 10))) { HorizontalAlignment = HorizontalAlignment.Center });

            classicRoot.Measure(new Size(200, 200));
            classicRoot.Arrange(new Rect(0, 0, 200, 200));

            new LayoutPipeline().ExecuteFrame(pipedRoot, new Size(200, 200), new Rect(0, 0, 200, 200));

            AssertTreeMatches(classicRoot, pipedRoot);

            static Border CreateTree(Control leaf) => new()
            {
                Padding = new Thickness(5),
                BorderThickness = new Thickness(2),
                Margin = new Thickness(3),
                Child = new Decorator
                {
                    Padding = new Thickness(1, 2, 3, 4),
                    Child = leaf,
                },
            };

            static void AssertTreeMatches(Border classic, Border piped)
            {
                var classicDecorator = (Decorator)classic.Child!;
                var pipedDecorator = (Decorator)piped.Child!;

                Assert.Equal(classic.DesiredSize, piped.DesiredSize);
                Assert.Equal(classic.Bounds, piped.Bounds);
                Assert.Equal(classicDecorator.DesiredSize, pipedDecorator.DesiredSize);
                Assert.Equal(classicDecorator.Bounds, pipedDecorator.Bounds);
                Assert.Equal(classicDecorator.Child!.DesiredSize, pipedDecorator.Child!.DesiredSize);
                Assert.Equal(classicDecorator.Child!.Bounds, pipedDecorator.Child!.Bounds);
            }
        }

        [Fact]
        public void ContentPresenter_Matches_Classic_Engine()
        {
            foreach (var horizontalContentAlignment in Enum.GetValues<HorizontalAlignment>())
            {
                foreach (var verticalContentAlignment in Enum.GetValues<VerticalAlignment>())
                {
                    var classicPresenter = CreatePresenter(
                        new ClassicFixedControl { Size = new Size(30, 10) },
                        horizontalContentAlignment,
                        verticalContentAlignment);

                    var pipedPresenter = CreatePresenter(
                        new PipelineControl(new FixedContentAlgorithm(new Size(30, 10))),
                        horizontalContentAlignment,
                        verticalContentAlignment);

                    classicPresenter.Measure(new Size(200, 200));
                    classicPresenter.Arrange(new Rect(0, 0, 200, 200));

                    new LayoutPipeline().ExecuteFrame(pipedPresenter, new Size(200, 200), new Rect(0, 0, 200, 200));

                    Assert.Equal(classicPresenter.DesiredSize, pipedPresenter.DesiredSize);
                    Assert.Equal(classicPresenter.Bounds, pipedPresenter.Bounds);
                    Assert.Equal(classicPresenter.Child!.DesiredSize, pipedPresenter.Child!.DesiredSize);
                    Assert.Equal(classicPresenter.Child!.Bounds, pipedPresenter.Child!.Bounds);
                }
            }

            static ContentPresenter CreatePresenter(
                Control content,
                HorizontalAlignment horizontalContentAlignment,
                VerticalAlignment verticalContentAlignment)
            {
                var presenter = new ContentPresenter
                {
                    Content = content,
                    Padding = new Thickness(5),
                    BorderThickness = new Thickness(2),
                    Margin = new Thickness(3),
                    HorizontalContentAlignment = horizontalContentAlignment,
                    VerticalContentAlignment = verticalContentAlignment,
                };

                // Materialize the child outside a layout pass, like the prepare stage does for
                // an attached presenter. ApplyTemplate is not enough here because the presenter
                // is not attached to a logical tree.
                presenter.UpdateChild();
                return presenter;
            }
        }

        [Theory]
        [InlineData(Orientation.Vertical, 0.0)]
        [InlineData(Orientation.Vertical, 6.0)]
        [InlineData(Orientation.Horizontal, 0.0)]
        [InlineData(Orientation.Horizontal, 6.0)]
        public void StackPanel_Matches_Classic_Engine(Orientation orientation, double spacing)
        {
            var classicPanel = CreatePanel(orientation, spacing, size => new ClassicFixedControl { Size = size });
            var pipedPanel = CreatePanel(orientation, spacing, size => new PipelineControl(new FixedContentAlgorithm(size)));

            classicPanel.Measure(new Size(200, 200));
            classicPanel.Arrange(new Rect(0, 0, 200, 200));

            new LayoutPipeline().ExecuteFrame(pipedPanel, new Size(200, 200), new Rect(0, 0, 200, 200));

            Assert.Equal(classicPanel.DesiredSize, pipedPanel.DesiredSize);
            Assert.Equal(classicPanel.Bounds, pipedPanel.Bounds);

            for (var i = 0; i < classicPanel.Children.Count; i++)
            {
                Assert.Equal(classicPanel.Children[i].DesiredSize, pipedPanel.Children[i].DesiredSize);
                Assert.Equal(classicPanel.Children[i].Bounds, pipedPanel.Children[i].Bounds);
            }

            static StackPanel CreatePanel(Orientation orientation, double spacing, Func<Size, Control> createLeaf)
            {
                var panel = new StackPanel
                {
                    Orientation = orientation,
                    Spacing = spacing,
                    Margin = new Thickness(2),
                };

                panel.Children.Add(createLeaf(new Size(30, 10)));
                panel.Children.Add(createLeaf(new Size(20, 20)));

                // An invisible child contributes neither size nor spacing.
                var invisible = createLeaf(new Size(500, 500));
                invisible.IsVisible = false;
                panel.Children.Add(invisible);

                // A visible zero-sized child still gets spacing.
                panel.Children.Add(createLeaf(default));

                var aligned = createLeaf(new Size(10, 30));
                aligned.HorizontalAlignment = HorizontalAlignment.Center;
                aligned.VerticalAlignment = VerticalAlignment.Bottom;
                panel.Children.Add(aligned);

                return panel;
            }
        }

        [Fact]
        public void Panel_Matches_Classic_Engine()
        {
            var classicPanel = CreatePanel(size => new ClassicFixedControl { Size = size });
            var pipedPanel = CreatePanel(size => new PipelineControl(new FixedContentAlgorithm(size)));

            classicPanel.Measure(new Size(200, 200));
            classicPanel.Arrange(new Rect(0, 0, 200, 200));

            new LayoutPipeline().ExecuteFrame(pipedPanel, new Size(200, 200), new Rect(0, 0, 200, 200));

            Assert.Equal(classicPanel.DesiredSize, pipedPanel.DesiredSize);
            Assert.Equal(classicPanel.Bounds, pipedPanel.Bounds);

            for (var i = 0; i < classicPanel.Children.Count; i++)
            {
                Assert.Equal(classicPanel.Children[i].DesiredSize, pipedPanel.Children[i].DesiredSize);
                Assert.Equal(classicPanel.Children[i].Bounds, pipedPanel.Children[i].Bounds);
            }

            static Panel CreatePanel(Func<Size, Control> createLeaf)
            {
                var panel = new Panel { Margin = new Thickness(2) };

                panel.Children.Add(createLeaf(new Size(30, 10)));

                var aligned = createLeaf(new Size(20, 20));
                aligned.HorizontalAlignment = HorizontalAlignment.Right;
                aligned.VerticalAlignment = VerticalAlignment.Bottom;
                panel.Children.Add(aligned);

                return panel;
            }
        }

        [Fact]
        public void Derived_Panel_Does_Not_Opt_In()
        {
            Assert.NotNull(new Panel().GetLayoutAlgorithm());
            Assert.Null(new DerivedPanel().GetLayoutAlgorithm());
        }

        [Fact]
        public void VisualLayerManager_Matches_Classic_Engine()
        {
            var classicManager = CreateManager(new ClassicFixedControl { Size = new Size(30, 10) });
            var pipedManager = CreateManager(new PipelineControl(new FixedContentAlgorithm(new Size(30, 10))));

            classicManager.Measure(new Size(200, 200));
            classicManager.Arrange(new Rect(0, 0, 200, 200));

            new LayoutPipeline().ExecuteFrame(pipedManager, new Size(200, 200), new Rect(0, 0, 200, 200));

            Assert.Equal(classicManager.DesiredSize, pipedManager.DesiredSize);
            Assert.Equal(classicManager.Bounds, pipedManager.Bounds);
            Assert.Equal(classicManager.Child!.DesiredSize, pipedManager.Child!.DesiredSize);
            Assert.Equal(classicManager.Child!.Bounds, pipedManager.Child!.Bounds);

            static VisualLayerManager CreateManager(Control child)
            {
                var manager = new VisualLayerManager { Padding = new Thickness(5) };

                // Create the adorner layer before assigning the child, so that the child comes
                // after the (non-opted-in, snapshot-excluded) layer in VisualChildren and the
                // child index mapping is exercised.
                _ = manager.AdornerLayer;
                manager.Child = child;
                return manager;
            }
        }

        private class DerivedPanel : Panel
        {
        }

        [Theory]
        [InlineData(TextWrapping.NoWrap, HorizontalAlignment.Stretch)]
        [InlineData(TextWrapping.NoWrap, HorizontalAlignment.Left)]
        [InlineData(TextWrapping.Wrap, HorizontalAlignment.Stretch)]
        [InlineData(TextWrapping.Wrap, HorizontalAlignment.Left)]
        public void TextBlock_Matches_Classic_Engine(TextWrapping wrapping, HorizontalAlignment alignment)
        {
            using var app = UnitTestApplication.Start(TestServices.MockPlatformRenderInterface);

            // Stretch keeps the final size equal to the measure constraint (the shaped layout is
            // adopted at publish); Left makes them differ (the render layout is recreated).
            var classicText = CreateTextBlock(wrapping, alignment);
            var pipedText = CreateTextBlock(wrapping, alignment);

            classicText.Measure(new Size(100, 200));
            classicText.Arrange(new Rect(0, 0, 100, 200));

            new LayoutPipeline().ExecuteFrame(pipedText, new Size(100, 200), new Rect(0, 0, 100, 200));

            Assert.Equal(classicText.DesiredSize, pipedText.DesiredSize);
            Assert.Equal(classicText.Bounds, pipedText.Bounds);
            Assert.Equal(classicText.TextLayout.MaxWidth, pipedText.TextLayout.MaxWidth);
            Assert.Equal(classicText.TextLayout.MaxHeight, pipedText.TextLayout.MaxHeight);
            Assert.Equal(classicText.TextLayout.Height, pipedText.TextLayout.Height);

            static TextBlock CreateTextBlock(TextWrapping wrapping, HorizontalAlignment alignment) => new()
            {
                Text = "The quick brown fox jumps over the lazy dog",
                TextWrapping = wrapping,
                HorizontalAlignment = alignment,
                Padding = new Thickness(2),
            };
        }

        [Fact]
        public void TextBlocks_Shape_In_Parallel_Deterministically()
        {
            using var app = UnitTestApplication.Start(TestServices.MockPlatformRenderInterface);

            var classicRoot = CreateTree();
            var parallelRoot = CreateTree();
            var sequentialRoot = CreateTree();

            classicRoot.Measure(new Size(300, 10000));
            classicRoot.Arrange(new Rect(0, 0, 300, 10000));

            new LayoutPipeline { ParallelismThreshold = 1 }
                .ExecuteFrame(parallelRoot, new Size(300, 10000), new Rect(0, 0, 300, 10000));
            new LayoutPipeline { ParallelismThreshold = int.MaxValue }
                .ExecuteFrame(sequentialRoot, new Size(300, 10000), new Rect(0, 0, 300, 10000));

            for (var i = 0; i < classicRoot.Children.Count; i++)
            {
                Assert.Equal(classicRoot.Children[i].DesiredSize, parallelRoot.Children[i].DesiredSize);
                Assert.Equal(classicRoot.Children[i].Bounds, parallelRoot.Children[i].Bounds);
                Assert.Equal(sequentialRoot.Children[i].Bounds, parallelRoot.Children[i].Bounds);
            }

            static StackPanel CreateTree()
            {
                var panel = new StackPanel();

                for (var i = 0; i < 50; i++)
                {
                    panel.Children.Add(new TextBlock
                    {
                        Text = string.Join(" ", Enumerable.Repeat("word", i + 1)),
                        TextWrapping = TextWrapping.Wrap,
                    });
                }

                return panel;
            }
        }

        [Fact]
        public void Button_Matches_Classic_Engine()
        {
            using var app = UnitTestApplication.Start(TestServices.MockPlatformRenderInterface);

            var classicButton = CreateButton(new ClassicFixedControl { Size = new Size(30, 10) });
            var pipedButton = CreateButton(new PipelineControl(new FixedContentAlgorithm(new Size(30, 10))));

            classicButton.Measure(new Size(200, 200));
            classicButton.Arrange(new Rect(0, 0, 200, 200));

            new LayoutPipeline().ExecuteFrame(pipedButton, new Size(200, 200), new Rect(0, 0, 200, 200));

            Assert.Equal(classicButton.DesiredSize, pipedButton.DesiredSize);
            Assert.Equal(classicButton.Bounds, pipedButton.Bounds);
            Assert.Equal(classicButton.Presenter!.Bounds, pipedButton.Presenter!.Bounds);
            Assert.Equal(classicButton.Presenter!.Child!.Bounds, pipedButton.Presenter!.Child!.Bounds);

            static Button CreateButton(Control content)
            {
                var button = new Button
                {
                    Content = content,
                    Padding = new Thickness(4, 2),
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Top,
                    Template = new FuncControlTemplate<Button>((parent, scope) =>
                        new ContentPresenter
                        {
                            Name = "PART_ContentPresenter",
                            [!ContentPresenter.ContentProperty] = parent[!ContentControl.ContentProperty],
                            [!ContentPresenter.PaddingProperty] = parent[!TemplatedControl.PaddingProperty],
                        }.RegisterInNameScope(scope)),
                };

                button.ApplyTemplate();

                // Materialize the presenter's child: the button isn't attached to a logical tree.
                button.Presenter!.UpdateChild();
                return button;
            }
        }

        [Fact]
        public void Templated_Controls_Opt_In_Unless_They_Customize_Layout()
        {
            Assert.NotNull(new Button().GetLayoutAlgorithm());
            Assert.NotNull(new PlainDerivedButton().GetLayoutAlgorithm());
            Assert.Null(new CustomMeasureButton().GetLayoutAlgorithm());
        }

        private class PlainDerivedButton : Button
        {
        }

        private class CustomMeasureButton : Button
        {
            protected override Size MeasureOverride(Size availableSize) => base.MeasureOverride(availableSize);
        }

        [Fact]
        public void PipelineLayoutManager_Lays_Out_On_Demand()
        {
            var child = new PipelineControl(new FixedContentAlgorithm(new Size(30, 10)));
            var root = new PipedRoot { Padding = new Thickness(10), Child = child };
            var manager = new PipelineLayoutManager(root, () => new Size(100, 100));
            root.SetLayoutManager(manager);

            manager.ExecuteInitialLayoutPass();

            Assert.Equal(new Rect(0, 0, 100, 100), root.Bounds);
            Assert.Equal(new Rect(10, 10, 80, 80), child.Bounds);

            child.HorizontalAlignment = HorizontalAlignment.Left;
            manager.InvalidateMeasure(child);
            manager.ExecuteLayoutPass();

            Assert.Equal(new Rect(10, 10, 30, 80), child.Bounds);

            // Drain the layout pass queued on the dispatcher by InvalidateMeasure.
            Threading.Dispatcher.UIThread.RunJobs();
        }

        private class PipedRoot : Border, ILayoutRoot
        {
            private ILayoutManager? _layoutManager;

            public void SetLayoutManager(ILayoutManager layoutManager) => _layoutManager = layoutManager;

            public double LayoutScaling => 1.0;
            public ILayoutManager LayoutManager => _layoutManager!;
            public Layoutable RootVisual => this;
        }

        private class PipelineControl : Control
        {
            private readonly LayoutAlgorithm _algorithm;

            public PipelineControl(LayoutAlgorithm? algorithm = null)
                => _algorithm = algorithm ?? LayoutAlgorithm.Overlay;

            protected internal override LayoutAlgorithm? GetLayoutAlgorithm() => _algorithm;

            public void AddChild(Layoutable child) => VisualChildren.Add(child);
        }

        private class ClassicPanel : Control
        {
            public void AddChild(Layoutable child) => VisualChildren.Add(child);
        }

        private class ClassicFixedControl : Control
        {
            public Size Size { get; set; }

            protected override Size MeasureOverride(Size availableSize) => Size;
        }

        private sealed class FixedContentAlgorithm : LayoutAlgorithm
        {
            private readonly Size _size;

            public FixedContentAlgorithm(Size size) => _size = size;

            public override Size MeasureContent(Size availableSize) => _size;
        }

        private sealed class VerticalStackAlgorithm : LayoutAlgorithm
        {
            public override LayoutChildrenDependency MeasureDependency => LayoutChildrenDependency.Sequential;

            public override Size GetChildAvailableSize(int childIndex, Size availableSize, ReadOnlySpan<Size> measuredSiblings)
            {
                var usedHeight = 0.0;

                foreach (ref readonly var sibling in measuredSiblings)
                    usedHeight += sibling.Height;

                return new Size(availableSize.Width, Math.Max(0, availableSize.Height - usedHeight));
            }

            public override Size CombineChildSizes(Size availableSize, ReadOnlySpan<Size> childSizes)
            {
                double width = 0.0, height = 0.0;

                foreach (ref readonly var childSize in childSizes)
                {
                    width = Math.Max(width, childSize.Width);
                    height += childSize.Height;
                }

                return new Size(width, height);
            }

            public override void ArrangeChildren(Size finalSize, Size desiredSize, ReadOnlySpan<Size> childSizes, Span<Rect> childSlots)
            {
                var y = 0.0;

                for (var i = 0; i < childSlots.Length; i++)
                {
                    childSlots[i] = new Rect(0, y, finalSize.Width, childSizes[i].Height);
                    y += childSizes[i].Height;
                }
            }
        }
    }
}
