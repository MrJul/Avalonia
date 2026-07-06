using System;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Markup.Xaml.Templates;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.UnitTests;
using Avalonia.VisualTree;
using Xunit;

namespace Avalonia.Controls.UnitTests;

public partial class TreeViewTests
{
    [Fact]
    public void Pressing_Over_Own_Header_Should_Select_Parent()
    {
        using var services = new CompositorTestServices();
        var target = CreateNestedInputTarget(services);

        var parent = GetItem(target, 0, 1); // Child2, which is expanded and has a child of its own.

        _mouse.Down(parent, position: HeaderPoint(parent));
        _mouse.Up(parent, position: HeaderPoint(parent));

        Assert.Same(parent.DataContext, target.SelectedItem);
        Assert.True(parent.IsSelected);
    }

    [Fact]
    public void Pressing_Over_Own_Items_Panel_Should_Not_Select_Parent()
    {
        using var services = new CompositorTestServices();
        var target = CreateNestedInputTarget(services);

        var parent = GetItem(target, 0, 1); // Child2
        var child = GetItem(target, 0, 1, 0); // Grandchild2a

        _mouse.Down(parent, position: ItemsPanelPoint(child));
        _mouse.Up(parent, position: ItemsPanelPoint(child));

        Assert.Null(target.SelectedItem);
        Assert.False(parent.IsSelected);
        Assert.False(child.IsSelected);
    }

    [Fact]
    public void Releasing_Over_Own_Items_Panel_Should_Not_Select_Parent()
    {
        using var services = new CompositorTestServices();
        var target = CreateNestedInputTarget(services);

        var parent = GetItem(target, 0, 1); // Child2
        var child = GetItem(target, 0, 1, 0); // Grandchild2a

        var touch = new MouseTestHelper(PointerType.Touch);

        touch.Down(parent, position: HeaderPoint(parent));
        touch.Up(parent, position: ItemsPanelPoint(child));

        Assert.Null(target.SelectedItem);
        Assert.False(parent.IsSelected);
        Assert.False(child.IsSelected);
    }

    /// <summary>
    /// Builds an expanded tree hosted in a real <see cref="CompositorTestServices.TopLevel"/> so that the
    /// compositor's real hit tester is used. Unlike <see cref="CreateTreeViewItemControlTheme"/>, the item template
    /// here lays the header out above a separate items panel (they must not overlap) and gives the header a
    /// hit-test-visible background, both of which are needed to exercise <c>TreeViewItem.IsItemsPanelEvent</c>.
    /// </summary>
    private static TreeView CreateNestedInputTarget(CompositorTestServices services)
    {
        var target = new TreeView
        {
            ItemsSource = CreateTestTreeData(),
            SelectionMode = SelectionMode.Single,
        };

        var top = services.TopLevel;
        top.Resources.Add(typeof(TreeView), CreateTreeViewControlTheme());
        top.Resources.Add(typeof(TreeViewItem), CreateHitTestableTreeViewItemControlTheme());
        top.DataTemplates.Add(new TreeDataTemplate
        {
            DataType = typeof(Node),
            ItemsSource = new Binding(nameof(Node.Children)),
            Content = (IServiceProvider? _) => new TemplateResult<Control>(
                new TextBlock
                {
                    [!TextBlock.TextProperty] = new Binding(nameof(Node.Value)),
                },
                new NameScope())
        });
        top.Content = target;

        services.RunJobs();
        ExpandAll(target);
        services.RunJobs();

        return target;
    }

    private static ControlTheme CreateHitTestableTreeViewItemControlTheme()
        => new(typeof(TreeViewItem))
        {
            Setters =
            {
                new Setter(TemplatedControl.TemplateProperty,
                    new FuncControlTemplate<TreeViewItem>((parent, scope) => new StackPanel
                    {
                        Children =
                        {
                            new Border
                            {
                                Name = "PART_Header",
                                Background = Brushes.Transparent,
                                Height = 20,
                                Child = new ContentPresenter
                                {
                                    Name = "PART_HeaderPresenter",
                                    [~ContentPresenter.ContentProperty] = parent[~TreeViewItem.HeaderProperty],
                                    [~ContentPresenter.ContentTemplateProperty] = parent[~TreeViewItem.HeaderTemplateProperty],
                                }.RegisterInNameScope(scope)
                            }.RegisterInNameScope(scope),
                            new ItemsPresenter
                            {
                                Name = "PART_ItemsPresenter",
                                [~Visual.IsVisibleProperty] = parent[~TreeViewItem.IsExpandedProperty],
                            }.RegisterInNameScope(scope)
                        }
                    })),
            },
        };

    // Midpoint of an item's header in root coordinates - over the header, never the items panel.
    private static Point HeaderPoint(TreeViewItem item)
        => MidpointInRoot(item.HeaderPresenter!);

    // Midpoint of a child container in root coordinates - guaranteed to fall within its parent's items panel.
    private static Point ItemsPanelPoint(TreeViewItem child)
        => MidpointInRoot(child);

    private static Point MidpointInRoot(Visual element)
    {
        var root = element.GetVisualRoot();
        Assert.NotNull(root);
        var point = element.TranslatePoint(new Point(element.Bounds.Width / 2, element.Bounds.Height / 2), root);
        return Assert.NotNull(point);
    }
}
