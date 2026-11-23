using System.Numerics;
using System.Runtime.CompilerServices;
using kajarlabs.Framework.Numerics;

namespace kajarlabs.Framework.Scene.Transforms;

/// <summary>
/// Provides allocation-free, NativeAOT-safe linear and eased value interpolation primitives.
/// </summary>
public static class Interpolation
{
    /// <summary>
    /// Linearly interpolates between <paramref name="start"/> and <paramref name="final"/> by <paramref name="amount"/>
    /// without clamping <paramref name="amount"/>.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double Lerp(double start, double final, double amount)
        => start + ((final - start) * amount);

    /// <summary>
    /// Linearly interpolates between <paramref name="start"/> and <paramref name="final"/> by <paramref name="amount"/>
    /// without clamping <paramref name="amount"/>.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Lerp(float start, float final, double amount)
        => (float)(start + (((double)final - start) * amount));

    /// <summary>
    /// Linearly interpolates between <paramref name="start"/> and <paramref name="final"/> by <paramref name="amount"/>
    /// without clamping <paramref name="amount"/>, rounding to the nearest integer.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int Lerp(int start, int final, double amount)
        => (int)Math.Round(start + (((double)final - start) * amount));

    /// <summary>
    /// Linearly interpolates component-wise between <paramref name="start"/> and <paramref name="final"/>
    /// by <paramref name="amount"/> without clamping <paramref name="amount"/>.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector2 Lerp(Vector2 start, Vector2 final, double amount)
        => new(Lerp(start.X, final.X, amount), Lerp(start.Y, final.Y, amount));

    /// <summary>
    /// Linearly interpolates component-wise between <paramref name="start"/> and <paramref name="final"/>
    /// by <paramref name="amount"/> without clamping <paramref name="amount"/>.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Color4 Lerp(Color4 start, Color4 final, double amount)
        => new(
            Lerp(start.R, final.R, amount),
            Lerp(start.G, final.G, amount),
            Lerp(start.B, final.B, amount),
            Lerp(start.A, final.A, amount));

    /// <summary>
    /// Linearly interpolates component-wise between <paramref name="start"/> and <paramref name="final"/>
    /// by <paramref name="amount"/> without clamping <paramref name="amount"/>.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static MarginPadding Lerp(MarginPadding start, MarginPadding final, double amount)
        => new(
            Lerp(start.Left, final.Left, amount),
            Lerp(start.Top, final.Top, amount),
            Lerp(start.Right, final.Right, amount),
            Lerp(start.Bottom, final.Bottom, amount));

    /// <summary>
    /// Linearly interpolates component-wise between <paramref name="start"/> and <paramref name="final"/>
    /// by <paramref name="amount"/> without clamping <paramref name="amount"/>.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static RectangleF Lerp(RectangleF start, RectangleF final, double amount)
        => new(
            Lerp(start.X, final.X, amount),
            Lerp(start.Y, final.Y, amount),
            Lerp(start.Width, final.Width, amount),
            Lerp(start.Height, final.Height, amount));

    /// <summary>
    /// Evaluates the interpolated <see cref="double"/> value at <paramref name="time"/> between
    /// <paramref name="startTime"/> and <paramref name="endTime"/> using <paramref name="easing"/>.
    /// </summary>
    public static double ValueAt(
        double time,
        double startValue,
        double endValue,
        double startTime,
        double endTime,
        Easing easing = Easing.None)
        => ValueAt(time, startValue, endValue, startTime, endTime, new DefaultEasingFunction(easing));

    /// <summary>
    /// Evaluates the interpolated <see cref="double"/> value at <paramref name="time"/> between
    /// <paramref name="startTime"/> and <paramref name="endTime"/> using the value-type easing function <paramref name="easing"/>.
    /// </summary>
    public static double ValueAt<TEasing>(
        double time,
        double startValue,
        double endValue,
        double startTime,
        double endTime,
        TEasing easing)
        where TEasing : struct, IEasingFunction
    {
        validateTimeBounds(time, startTime, endTime);

        if (time >= endTime)
            return endValue;

        if (time <= startTime)
            return startValue;

        return Lerp(startValue, endValue, easing.ApplyEasing((time - startTime) / (endTime - startTime)));
    }

    /// <summary>
    /// Evaluates the interpolated <see cref="float"/> value at <paramref name="time"/> between
    /// <paramref name="startTime"/> and <paramref name="endTime"/> using <paramref name="easing"/>.
    /// </summary>
    public static float ValueAt(
        double time,
        float startValue,
        float endValue,
        double startTime,
        double endTime,
        Easing easing = Easing.None)
        => ValueAt(time, startValue, endValue, startTime, endTime, new DefaultEasingFunction(easing));

    /// <summary>
    /// Evaluates the interpolated <see cref="float"/> value at <paramref name="time"/> between
    /// <paramref name="startTime"/> and <paramref name="endTime"/> using the value-type easing function <paramref name="easing"/>.
    /// </summary>
    public static float ValueAt<TEasing>(
        double time,
        float startValue,
        float endValue,
        double startTime,
        double endTime,
        TEasing easing)
        where TEasing : struct, IEasingFunction
    {
        validateTimeBounds(time, startTime, endTime);

        if (time >= endTime)
            return endValue;

        if (time <= startTime)
            return startValue;

        return Lerp(startValue, endValue, easing.ApplyEasing((time - startTime) / (endTime - startTime)));
    }

    /// <summary>
    /// Evaluates the interpolated <see cref="int"/> value at <paramref name="time"/> between
    /// <paramref name="startTime"/> and <paramref name="endTime"/> using <paramref name="easing"/>.
    /// </summary>
    public static int ValueAt(
        double time,
        int startValue,
        int endValue,
        double startTime,
        double endTime,
        Easing easing = Easing.None)
        => ValueAt(time, startValue, endValue, startTime, endTime, new DefaultEasingFunction(easing));

    /// <summary>
    /// Evaluates the interpolated <see cref="int"/> value at <paramref name="time"/> between
    /// <paramref name="startTime"/> and <paramref name="endTime"/> using the value-type easing function <paramref name="easing"/>.
    /// </summary>
    public static int ValueAt<TEasing>(
        double time,
        int startValue,
        int endValue,
        double startTime,
        double endTime,
        TEasing easing)
        where TEasing : struct, IEasingFunction
    {
        validateTimeBounds(time, startTime, endTime);

        if (time >= endTime)
            return endValue;

        if (time <= startTime)
            return startValue;

        return Lerp(startValue, endValue, easing.ApplyEasing((time - startTime) / (endTime - startTime)));
    }

    /// <summary>
    /// Evaluates the interpolated <see cref="Vector2"/> value at <paramref name="time"/> between
    /// <paramref name="startTime"/> and <paramref name="endTime"/> using <paramref name="easing"/>.
    /// </summary>
    public static Vector2 ValueAt(
        double time,
        Vector2 startValue,
        Vector2 endValue,
        double startTime,
        double endTime,
        Easing easing = Easing.None)
        => ValueAt(time, startValue, endValue, startTime, endTime, new DefaultEasingFunction(easing));

    /// <summary>
    /// Evaluates the interpolated <see cref="Vector2"/> value at <paramref name="time"/> between
    /// <paramref name="startTime"/> and <paramref name="endTime"/> using the value-type easing function <paramref name="easing"/>.
    /// </summary>
    public static Vector2 ValueAt<TEasing>(
        double time,
        Vector2 startValue,
        Vector2 endValue,
        double startTime,
        double endTime,
        TEasing easing)
        where TEasing : struct, IEasingFunction
    {
        validateTimeBounds(time, startTime, endTime);

        if (time >= endTime)
            return endValue;

        if (time <= startTime)
            return startValue;

        return Lerp(startValue, endValue, easing.ApplyEasing((time - startTime) / (endTime - startTime)));
    }

    /// <summary>
    /// Evaluates the interpolated <see cref="Color4"/> value at <paramref name="time"/> between
    /// <paramref name="startTime"/> and <paramref name="endTime"/> using <paramref name="easing"/>.
    /// </summary>
    public static Color4 ValueAt(
        double time,
        Color4 startValue,
        Color4 endValue,
        double startTime,
        double endTime,
        Easing easing = Easing.None)
        => ValueAt(time, startValue, endValue, startTime, endTime, new DefaultEasingFunction(easing));

    /// <summary>
    /// Evaluates the interpolated <see cref="Color4"/> value at <paramref name="time"/> between
    /// <paramref name="startTime"/> and <paramref name="endTime"/> using the value-type easing function <paramref name="easing"/>.
    /// </summary>
    public static Color4 ValueAt<TEasing>(
        double time,
        Color4 startValue,
        Color4 endValue,
        double startTime,
        double endTime,
        TEasing easing)
        where TEasing : struct, IEasingFunction
    {
        validateTimeBounds(time, startTime, endTime);

        if (time >= endTime)
            return endValue;

        if (time <= startTime)
            return startValue;

        return Lerp(startValue, endValue, easing.ApplyEasing((time - startTime) / (endTime - startTime)));
    }

    /// <summary>
    /// Evaluates the interpolated <see cref="MarginPadding"/> value at <paramref name="time"/> between
    /// <paramref name="startTime"/> and <paramref name="endTime"/> using <paramref name="easing"/>.
    /// </summary>
    public static MarginPadding ValueAt(
        double time,
        MarginPadding startValue,
        MarginPadding endValue,
        double startTime,
        double endTime,
        Easing easing = Easing.None)
        => ValueAt(time, startValue, endValue, startTime, endTime, new DefaultEasingFunction(easing));

    /// <summary>
    /// Evaluates the interpolated <see cref="MarginPadding"/> value at <paramref name="time"/> between
    /// <paramref name="startTime"/> and <paramref name="endTime"/> using the value-type easing function <paramref name="easing"/>.
    /// </summary>
    public static MarginPadding ValueAt<TEasing>(
        double time,
        MarginPadding startValue,
        MarginPadding endValue,
        double startTime,
        double endTime,
        TEasing easing)
        where TEasing : struct, IEasingFunction
    {
        validateTimeBounds(time, startTime, endTime);

        if (time >= endTime)
            return endValue;

        if (time <= startTime)
            return startValue;

        return Lerp(startValue, endValue, easing.ApplyEasing((time - startTime) / (endTime - startTime)));
    }

    /// <summary>
    /// Evaluates the interpolated <see cref="RectangleF"/> value at <paramref name="time"/> between
    /// <paramref name="startTime"/> and <paramref name="endTime"/> using <paramref name="easing"/>.
    /// </summary>
    public static RectangleF ValueAt(
        double time,
        RectangleF startValue,
        RectangleF endValue,
        double startTime,
        double endTime,
        Easing easing = Easing.None)
        => ValueAt(time, startValue, endValue, startTime, endTime, new DefaultEasingFunction(easing));

    /// <summary>
    /// Evaluates the interpolated <see cref="RectangleF"/> value at <paramref name="time"/> between
    /// <paramref name="startTime"/> and <paramref name="endTime"/> using the value-type easing function <paramref name="easing"/>.
    /// </summary>
    public static RectangleF ValueAt<TEasing>(
        double time,
        RectangleF startValue,
        RectangleF endValue,
        double startTime,
        double endTime,
        TEasing easing)
        where TEasing : struct, IEasingFunction
    {
        validateTimeBounds(time, startTime, endTime);

        if (time >= endTime)
            return endValue;

        if (time <= startTime)
            return startValue;

        return Lerp(startValue, endValue, easing.ApplyEasing((time - startTime) / (endTime - startTime)));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void validateTimeBounds(double time, double startTime, double endTime)
    {
        if (double.IsNaN(time))
            throw new ArgumentOutOfRangeException(nameof(time), time, "Time must not be NaN.");

        if (double.IsNaN(startTime))
            throw new ArgumentOutOfRangeException(nameof(startTime), startTime, "Start time must not be NaN.");

        if (double.IsNaN(endTime))
            throw new ArgumentOutOfRangeException(nameof(endTime), endTime, "End time must not be NaN.");

        if (endTime < startTime)
            throw new ArgumentOutOfRangeException(nameof(endTime), endTime, "End time must be greater than or equal to start time.");
    }
}
