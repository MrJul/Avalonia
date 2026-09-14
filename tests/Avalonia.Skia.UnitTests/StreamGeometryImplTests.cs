using Avalonia.Platform;
using Xunit;

namespace Avalonia.Skia.UnitTests;

public sealed class StreamGeometryImplTests
{
    [Fact]
    public void Reopened_Stream_Should_Update_Transformed_Geometry()
    {
        var stream = new StreamGeometryImpl();
        AddLine(stream, new Point(0, 0), new Point(10, 10));
        var transformed = stream.WithTransform(Matrix.CreateTranslation(50, 50));
        Assert.Equal(new Rect(50, 50, 10, 10), transformed.Bounds);

        AddLine(stream, new Point(20, 20), new Point(30, 30));
        Assert.Equal(new Rect(50, 50, 30, 30), transformed.Bounds);
    }

    [Fact]
    public void Reopened_Stream_Should_Update_ContourLength()
    {
        var stream = new StreamGeometryImpl();
        Assert.Equal(0, stream.ContourLength);

        AddLine(stream, new Point(0, 0), new Point(100, 0));
        Assert.Equal(100, stream.ContourLength, 3);
    }

    [Fact]
    public void Reopened_Stream_Should_Update_Transformed_ContourLength()
    {
        var stream = new StreamGeometryImpl();
        var transformed = stream.WithTransform(Matrix.CreateScale(2, 2));
        Assert.Equal(0, transformed.ContourLength);

        AddLine(stream, new Point(0, 0), new Point(100, 0));
        Assert.Equal(200, transformed.ContourLength, 3);
    }

    private static void AddLine(IStreamGeometryImpl stream, Point start, Point end)
    {
        using var context = stream.Open();
        context.BeginFigure(start, false);
        context.LineTo(end);
        context.EndFigure(false);
    }
}
