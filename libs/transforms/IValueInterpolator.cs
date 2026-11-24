using System.Numerics;
using System.Runtime.CompilerServices;
using kajarlabs.Framework.Numerics;

namespace kajarlabs.Framework.Scene.Transforms;

internal interface IValueInterpolator<TValue>
{
    TValue Lerp(TValue start, TValue end, double amount);
}

internal readonly struct DoubleInterpolator : IValueInterpolator<double>
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public double Lerp(double start, double end, double amount) => Interpolation.Lerp(start, end, amount);
}

internal readonly struct SingleInterpolator : IValueInterpolator<float>
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public float Lerp(float start, float end, double amount) => Interpolation.Lerp(start, end, amount);
}

internal readonly struct Int32Interpolator : IValueInterpolator<int>
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int Lerp(int start, int end, double amount) => Interpolation.Lerp(start, end, amount);
}

internal readonly struct Vector2Interpolator : IValueInterpolator<Vector2>
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Vector2 Lerp(Vector2 start, Vector2 end, double amount) => Interpolation.Lerp(start, end, amount);
}

internal readonly struct Color4Interpolator : IValueInterpolator<Color4>
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Color4 Lerp(Color4 start, Color4 end, double amount) => Interpolation.Lerp(start, end, amount);
}

internal readonly struct MarginPaddingInterpolator : IValueInterpolator<MarginPadding>
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public MarginPadding Lerp(MarginPadding start, MarginPadding end, double amount) => Interpolation.Lerp(start, end, amount);
}

internal readonly struct RectangleFInterpolator : IValueInterpolator<RectangleF>
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public RectangleF Lerp(RectangleF start, RectangleF end, double amount) => Interpolation.Lerp(start, end, amount);
}

internal readonly struct DelegateInterpolator<TValue> : IValueInterpolator<TValue>
{
    private readonly Func<TValue, TValue, double, TValue> lerp;

    public DelegateInterpolator(Func<TValue, TValue, double, TValue> lerp)
    {
        ArgumentNullException.ThrowIfNull(lerp);
        this.lerp = lerp;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public TValue Lerp(TValue start, TValue end, double amount) => lerp(start, end, amount);
}
