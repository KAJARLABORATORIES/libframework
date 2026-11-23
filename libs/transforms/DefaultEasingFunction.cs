using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace kajarlabs.Framework.Scene.Transforms;

/// <summary>
/// Evaluates one of the built-in <see cref="Transforms.Easing"/> curves without heap allocation or boxing.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public readonly struct DefaultEasingFunction : IEasingFunction, IEquatable<DefaultEasingFunction>
{
    private const double half_pi = Math.PI * 0.5;
    private const double two_pi = Math.PI * 2.0;

    // 2^-10 offset and normalization factor so InExpo/OutExpo/InOutExpo are C0-continuous
    // and evaluate to exact 0.0 at t = 0 and exact 1.0 at t = 1.
    private const double expo_offset = 1.0 / 1024.0;
    private const double expo_scale = 1024.0 / 1023.0;

    private const double elastic_period = 0.3;
    private const double elastic_offset = elastic_period / 4.0;
    private const double elastic_in_out_period = elastic_period * 1.5;
    private const double elastic_in_out_offset = elastic_in_out_period / 4.0;

    private const double back_overshoot = 1.70158;
    private const double back_in_out_overshoot = back_overshoot * 1.525;

    /// <summary>
    /// Gets the default linear easing function (<see cref="Easing.None"/>).
    /// </summary>
    public static readonly DefaultEasingFunction DEFAULT = new(Easing.None);

    /// <summary>
    /// Initializes a new <see cref="DefaultEasingFunction"/> wrapping <paramref name="easing"/>.
    /// </summary>
    /// <param name="easing">The standard easing curve to evaluate.</param>
    public DefaultEasingFunction(Easing easing)
    {
        Easing = easing;
    }

    /// <summary>
    /// Gets the underlying <see cref="Transforms.Easing"/> curve.
    /// </summary>
    public Easing Easing { get; }

    /// <summary>
    /// Creates a <see cref="DefaultEasingFunction"/> from an <see cref="Transforms.Easing"/> value.
    /// </summary>
    public static DefaultEasingFunction FromEasing(Easing easing) => new(easing);

    /// <summary>
    /// Implicitly converts an <see cref="Transforms.Easing"/> value to a <see cref="DefaultEasingFunction"/>.
    /// </summary>
    public static implicit operator DefaultEasingFunction(Easing easing) => FromEasing(easing);

    /// <summary>
    /// Determines whether two <see cref="DefaultEasingFunction"/> instances represent the same curve.
    /// </summary>
    public static bool operator ==(DefaultEasingFunction left, DefaultEasingFunction right) => left.Equals(right);

    /// <summary>
    /// Determines whether two <see cref="DefaultEasingFunction"/> instances represent different curves.
    /// </summary>
    public static bool operator !=(DefaultEasingFunction left, DefaultEasingFunction right) => !left.Equals(right);

    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public double ApplyEasing(double time) => ApplyEasing(Easing, time);

    /// <summary>
    /// Evaluates the specified <paramref name="easing"/> curve at normalized progress <paramref name="time"/>.
    /// </summary>
    /// <param name="easing">The standard easing curve to evaluate.</param>
    /// <param name="time">Normalized progress in <c>[0, 1]</c>.</param>
    /// <returns>The eased progress value.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="easing"/> is not a defined curve.</exception>
    public static double ApplyEasing(Easing easing, double time)
    {
        switch (easing)
        {
            case Easing.None:
                return time;

            case Easing.In:
            case Easing.InQuad:
                return time * time;

            case Easing.Out:
            case Easing.OutQuad:
                return time * (2.0 - time);

            case Easing.InOutQuad:
            {
                if (time < 0.5)
                    return 2.0 * time * time;

                var t = (2.0 * time) - 2.0;
                return 1.0 - (0.5 * t * t);
            }

            case Easing.InCubic:
                return time * time * time;

            case Easing.OutCubic:
            {
                var t = time - 1.0;
                return (t * t * t) + 1.0;
            }

            case Easing.InOutCubic:
            {
                if (time < 0.5)
                    return 4.0 * time * time * time;

                var t = (2.0 * time) - 2.0;
                return (0.5 * t * t * t) + 1.0;
            }

            case Easing.InQuart:
            {
                var t2 = time * time;
                return t2 * t2;
            }

            case Easing.OutQuart:
            {
                var t = time - 1.0;
                var t2 = t * t;
                return 1.0 - (t2 * t2);
            }

            case Easing.InOutQuart:
            {
                if (time < 0.5)
                {
                    var t2 = time * time;
                    return 8.0 * t2 * t2;
                }

                var t = (2.0 * time) - 2.0;
                var u2 = t * t;
                return 1.0 - (0.5 * u2 * u2);
            }

            case Easing.InQuint:
            {
                var t2 = time * time;
                return t2 * t2 * time;
            }

            case Easing.OutQuint:
            {
                var t = time - 1.0;
                var t2 = t * t;
                return (t2 * t2 * t) + 1.0;
            }

            case Easing.InOutQuint:
            {
                if (time < 0.5)
                {
                    var t2 = time * time;
                    return 16.0 * t2 * t2 * time;
                }

                var t = (2.0 * time) - 2.0;
                var u2 = t * t;
                return (0.5 * u2 * u2 * t) + 1.0;
            }

            case Easing.InSine:
                if (time == 0.0)
                    return 0.0;
                if (time == 1.0)
                    return 1.0;

                return 1.0 - Math.Cos(time * half_pi);

            case Easing.OutSine:
                if (time == 0.0)
                    return 0.0;
                if (time == 1.0)
                    return 1.0;

                return Math.Sin(time * half_pi);

            case Easing.InOutSine:
                if (time == 0.0)
                    return 0.0;
                if (time == 1.0)
                    return 1.0;

                return 0.5 * (1.0 - Math.Cos(Math.PI * time));

            case Easing.InExpo:
                return inExpo(time);

            case Easing.OutExpo:
                return outExpo(time);

            case Easing.InOutExpo:
                if (time == 0.0)
                    return 0.0;
                if (time == 1.0)
                    return 1.0;

                return time < 0.5
                    ? 0.5 * inExpo(2.0 * time)
                    : 0.5 + (0.5 * outExpo((2.0 * time) - 1.0));

            case Easing.InCirc:
                if (time == 0.0)
                    return 0.0;
                if (time == 1.0)
                    return 1.0;

                return 1.0 - Math.Sqrt(Math.Max(0.0, 1.0 - (time * time)));

            case Easing.OutCirc:
            {
                if (time == 0.0)
                    return 0.0;
                if (time == 1.0)
                    return 1.0;

                var t = time - 1.0;
                return Math.Sqrt(Math.Max(0.0, 1.0 - (t * t)));
            }

            case Easing.InOutCirc:
            {
                if (time == 0.0)
                    return 0.0;
                if (time == 1.0)
                    return 1.0;

                if (time < 0.5)
                {
                    var t = 2.0 * time;
                    return 0.5 * (1.0 - Math.Sqrt(Math.Max(0.0, 1.0 - (t * t))));
                }

                var u = (2.0 * time) - 2.0;
                return 0.5 * (Math.Sqrt(Math.Max(0.0, 1.0 - (u * u))) + 1.0);
            }

            case Easing.InElastic:
            {
                if (time == 0.0)
                    return 0.0;
                if (time == 1.0)
                    return 1.0;

                var t = time - 1.0;
                return -Math.Pow(2.0, 10.0 * t) * Math.Sin((t - elastic_offset) * two_pi / elastic_period);
            }

            case Easing.OutElastic:
                if (time == 0.0)
                    return 0.0;
                if (time == 1.0)
                    return 1.0;

                return (Math.Pow(2.0, -10.0 * time) * Math.Sin((time - elastic_offset) * two_pi / elastic_period)) + 1.0;

            case Easing.InOutElastic:
            {
                if (time == 0.0)
                    return 0.0;
                if (time == 1.0)
                    return 1.0;

                var t = (2.0 * time) - 1.0;
                return time < 0.5
                    ? -0.5 * Math.Pow(2.0, 10.0 * t) * Math.Sin((t - elastic_in_out_offset) * two_pi / elastic_in_out_period)
                    : (0.5 * Math.Pow(2.0, -10.0 * t) * Math.Sin((t - elastic_in_out_offset) * two_pi / elastic_in_out_period)) + 1.0;
            }

            case Easing.InBack:
                if (time == 0.0)
                    return 0.0;
                if (time == 1.0)
                    return 1.0;

                return time * time * (((back_overshoot + 1.0) * time) - back_overshoot);

            case Easing.OutBack:
            {
                if (time == 0.0)
                    return 0.0;
                if (time == 1.0)
                    return 1.0;

                var t = time - 1.0;
                return (t * t * (((back_overshoot + 1.0) * t) + back_overshoot)) + 1.0;
            }

            case Easing.InOutBack:
            {
                if (time == 0.0)
                    return 0.0;
                if (time == 1.0)
                    return 1.0;

                if (time < 0.5)
                {
                    var t = 2.0 * time;
                    return 0.5 * t * t * (((back_in_out_overshoot + 1.0) * t) - back_in_out_overshoot);
                }

                var u = (2.0 * time) - 2.0;
                return 0.5 * ((u * u * (((back_in_out_overshoot + 1.0) * u) + back_in_out_overshoot)) + 2.0);
            }

            case Easing.InBounce:
                return 1.0 - outBounce(1.0 - time);

            case Easing.OutBounce:
                return outBounce(time);

            case Easing.InOutBounce:
                return time < 0.5
                    ? 0.5 * (1.0 - outBounce(1.0 - (2.0 * time)))
                    : (0.5 * outBounce((2.0 * time) - 1.0)) + 0.5;

            case Easing.OutPow10:
                return 1.0 - Math.Pow(1.0 - time, 10.0);

            default:
                throw new ArgumentOutOfRangeException(nameof(easing), easing, "Unsupported easing curve.");
        }
    }

    /// <inheritdoc />
    public bool Equals(DefaultEasingFunction other) => Easing == other.Easing;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is DefaultEasingFunction other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => (int)Easing;

    /// <inheritdoc />
    public override string ToString() => Easing.ToString();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static double inExpo(double time)
    {
        if (time == 0.0)
            return 0.0;
        if (time == 1.0)
            return 1.0;

        return (Math.Pow(2.0, 10.0 * (time - 1.0)) - expo_offset) * expo_scale;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static double outExpo(double time)
    {
        if (time == 0.0)
            return 0.0;
        if (time == 1.0)
            return 1.0;

        return (1.0 - Math.Pow(2.0, -10.0 * time)) * expo_scale;
    }

    private static double outBounce(double time)
    {
        if (time == 0.0)
            return 0.0;
        if (time == 1.0)
            return 1.0;

        const double divisor = 2.75;
        const double factor = 7.5625;

        if (time < 1.0 / divisor)
            return factor * time * time;

        if (time < 2.0 / divisor)
        {
            var t = time - (1.5 / divisor);
            return (factor * t * t) + 0.75;
        }

        if (time < 2.5 / divisor)
        {
            var t = time - (2.25 / divisor);
            return (factor * t * t) + 0.9375;
        }

        {
            var t = time - (2.625 / divisor);
            return (factor * t * t) + 0.984375;
        }
    }
}
