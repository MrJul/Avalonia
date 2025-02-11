using System.Runtime.CompilerServices;

namespace Avalonia.Layout;

/// <summary>
/// Calculates the min and max height for a control. Ported from WPF.
/// </summary>
internal struct MinMax
{
    public double Width;
    public double MinWidth;
    public double MaxWidth;
    public double Height;
    public double MinHeight;
    public double MaxHeight;

    public MinMax(Layoutable e)
    {
        Width = e.Width;
        (MinWidth, MaxWidth) = ClampDimension(Width, e.MinWidth, e.MaxWidth);

        Height = e.Height;
        (MinHeight, MaxHeight) = ClampDimension(e.Height, e.MinHeight, e.MaxHeight);
    }

    private static (double MinResult, double MaxResult) ClampDimension(double value, double min, double max)
    {
        if (double.IsNaN(value))
        {
            return (Clamp(0.0, min, max), Clamp(double.PositiveInfinity, min, max));
        }
        else
        {
            var result = Clamp(value, min, max);
            return (result, result);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static double Clamp(double val, double min, double max)
    {
        if (val < min)
        {
            return min;
        }
        else if (val > max)
        {
            return max;
        }
        else
        {
            return val;
        }
    }
}
