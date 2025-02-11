using System.Runtime.CompilerServices;

namespace Avalonia.Layout;

/// <summary>
/// Calculates the min and max height for a control. Ported from WPF.
/// </summary>
internal struct MinMax
{
    public double MinWidth;
    public double MaxWidth;
    public double MinHeight;
    public double MaxHeight;

    public MinMax(Layoutable e)
    {
        ClampDimension(e.Width, e.MinWidth, e.MaxWidth, out MinWidth, out MaxWidth);
        ClampDimension(e.Height, e.MinHeight, e.MaxHeight, out MinHeight, out MaxHeight);
    }


    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ClampDimension(double l, double min, double max, out double minResult, out double maxResult)
    {
        var (minL, maxL) = double.IsNaN(l) ? (0.0, double.PositiveInfinity) : (l, l);

        if (minL > max)
            minL = max;
        if (minL < min)
            minL = min;

        if (maxL > max)
            maxL = max;
        if (maxL < min)
            maxL = min;

        minResult = minL;
        maxResult = maxL;
    }
}
