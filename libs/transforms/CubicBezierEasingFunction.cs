using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace kajarlabs.Framework.Scene.Transforms;

/// <summary>
/// Evaluates a CSS-style cubic Bézier easing curve defined by control points
/// <c>(0, 0)</c>, <c>(X1, Y1)</c>, <c>(X2, Y2)</c>, and <c>(1, 1)</c> without heap allocations.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public readonly struct CubicBezierEasingFunction : IEasingFunction, IEquatable<CubicBezierEasingFunction>
{
    private const double epsilon = 1e-12;
    private const int max_newton_iterations = 8;
    private const int max_bisection_iterations = 64;

    /// <summary>
    /// Linear cubic Bézier curve <c>cubic-bezier(0, 0, 1, 1)</c>.
    /// </summary>
    public static readonly CubicBezierEasingFunction LINEAR = new(0.0, 0.0, 1.0, 1.0);

    /// <summary>
    /// Standard CSS <c>ease</c> curve <c>cubic-bezier(0.25, 0.1, 0.25, 1)</c>.
    /// </summary>
    public static readonly CubicBezierEasingFunction EASE = new(0.25, 0.1, 0.25, 1.0);

    /// <summary>
    /// Standard CSS <c>ease-in</c> curve <c>cubic-bezier(0.42, 0, 1, 1)</c>.
    /// </summary>
    public static readonly CubicBezierEasingFunction EASE_IN = new(0.42, 0.0, 1.0, 1.0);

    /// <summary>
    /// Standard CSS <c>ease-out</c> curve <c>cubic-bezier(0, 0, 0.58, 1)</c>.
    /// </summary>
    public static readonly CubicBezierEasingFunction EASE_OUT = new(0.0, 0.0, 0.58, 1.0);

    /// <summary>
    /// Standard CSS <c>ease-in-out</c> curve <c>cubic-bezier(0.42, 0, 0.58, 1)</c>.
    /// </summary>
    public static readonly CubicBezierEasingFunction EASE_IN_OUT = new(0.42, 0.0, 0.58, 1.0);

    /// <summary>
    /// Initializes a new <see cref="CubicBezierEasingFunction"/> with the given control point coordinates.
    /// </summary>
    /// <param name="x1">The X coordinate of the first control point, in <c>[0, 1]</c>.</param>
    /// <param name="y1">The Y coordinate of the first control point (finite; values outside <c>[0, 1]</c> create anticipation/overshoot).</param>
    /// <param name="x2">The X coordinate of the second control point, in <c>[0, 1]</c>.</param>
    /// <param name="y2">The Y coordinate of the second control point (finite; values outside <c>[0, 1]</c> create anticipation/overshoot).</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="x1"/> or <paramref name="x2"/> is outside <c>[0, 1]</c> or non-finite,
    /// or when <paramref name="y1"/> or <paramref name="y2"/> is non-finite.
    /// </exception>
    public CubicBezierEasingFunction(double x1, double y1, double x2, double y2)
    {
        if (!double.IsFinite(x1) || x1 < 0.0 || x1 > 1.0)
            throw new ArgumentOutOfRangeException(nameof(x1), x1, "Control point X1 must be finite and in [0, 1].");

        if (!double.IsFinite(y1))
            throw new ArgumentOutOfRangeException(nameof(y1), y1, "Control point Y1 must be finite.");

        if (!double.IsFinite(x2) || x2 < 0.0 || x2 > 1.0)
            throw new ArgumentOutOfRangeException(nameof(x2), x2, "Control point X2 must be finite and in [0, 1].");

        if (!double.IsFinite(y2))
            throw new ArgumentOutOfRangeException(nameof(y2), y2, "Control point Y2 must be finite.");

        X1 = x1;
        Y1 = y1;
        X2 = x2;
        Y2 = y2;
    }

    /// <summary>
    /// Gets the X coordinate of the first control point.
    /// </summary>
    public double X1 { get; }

    /// <summary>
    /// Gets the Y coordinate of the first control point.
    /// </summary>
    public double Y1 { get; }

    /// <summary>
    /// Gets the X coordinate of the second control point.
    /// </summary>
    public double X2 { get; }

    /// <summary>
    /// Gets the Y coordinate of the second control point.
    /// </summary>
    public double Y2 { get; }

    /// <summary>
    /// Determines whether two <see cref="CubicBezierEasingFunction"/> instances have equal control points.
    /// </summary>
    public static bool operator ==(CubicBezierEasingFunction left, CubicBezierEasingFunction right) => left.Equals(right);

    /// <summary>
    /// Determines whether two <see cref="CubicBezierEasingFunction"/> instances have different control points.
    /// </summary>
    public static bool operator !=(CubicBezierEasingFunction left, CubicBezierEasingFunction right) => !left.Equals(right);

    /// <inheritdoc />
    public double ApplyEasing(double time)
    {
        if (double.IsNaN(time))
            return double.NaN;

        if (time <= 0.0)
            return 0.0;

        if (time >= 1.0)
            return 1.0;

        // Covers default(CubicBezierEasingFunction) where all control points are (0, 0, 0, 0)
        // as well as explicit linear curves where X1 == Y1 and X2 == Y2.
        if (X1 == Y1 && X2 == Y2)
            return time;

        var u = solveCurveX(time);
        return sampleCurveY(u);
    }

    /// <inheritdoc />
    public bool Equals(CubicBezierEasingFunction other)
        => normalizeZero(X1).Equals(normalizeZero(other.X1))
            && normalizeZero(Y1).Equals(normalizeZero(other.Y1))
            && normalizeZero(X2).Equals(normalizeZero(other.X2))
            && normalizeZero(Y2).Equals(normalizeZero(other.Y2));

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is CubicBezierEasingFunction other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode()
        => HashCode.Combine(normalizeZero(X1), normalizeZero(Y1), normalizeZero(X2), normalizeZero(Y2));

    /// <inheritdoc />
    public override string ToString()
        => string.Create(CultureInfo.InvariantCulture, $"({X1:G9}, {Y1:G9}, {X2:G9}, {Y2:G9})");

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private double sampleCurveX(double u)
    {
        var cx = 3.0 * X1;
        var bx = (3.0 * (X2 - X1)) - cx;
        var ax = 1.0 - cx - bx;
        return ((((ax * u) + bx) * u) + cx) * u;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private double sampleCurveDerivativeX(double u)
    {
        var cx = 3.0 * X1;
        var bx = (3.0 * (X2 - X1)) - cx;
        var ax = 1.0 - cx - bx;
        return (((3.0 * ax * u) + (2.0 * bx)) * u) + cx;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private double sampleCurveY(double u)
    {
        var cy = 3.0 * Y1;
        var by = (3.0 * (Y2 - Y1)) - cy;
        var ay = 1.0 - cy - by;
        return ((((ay * u) + by) * u) + cy) * u;
    }

    private double solveCurveX(double targetX)
    {
        var lo = 0.0;
        var hi = 1.0;
        var u = targetX;

        for (var i = 0; i < max_newton_iterations; i++)
        {
            var xErr = sampleCurveX(u) - targetX;
            if (Math.Abs(xErr) <= epsilon)
                return u;

            if (xErr < 0.0)
                lo = u;
            else
                hi = u;

            var dx = sampleCurveDerivativeX(u);
            if (Math.Abs(dx) <= 1e-12)
                break;

            var next = u - (xErr / dx);
            if (next <= lo || next >= hi)
                break;

            u = next;
        }

        // Fall back to bracketed bisection when Newton steps stall or leave [lo, hi].
        u = 0.5 * (lo + hi);
        for (var i = 0; i < max_bisection_iterations; i++)
        {
            var xErr = sampleCurveX(u) - targetX;
            if (Math.Abs(xErr) <= epsilon || (hi - lo) <= epsilon)
                return u;

            if (xErr < 0.0)
                lo = u;
            else
                hi = u;

            u = 0.5 * (lo + hi);
        }

        return u;
    }

    private static double normalizeZero(double value) => value == 0.0 ? 0.0 : value;
}
