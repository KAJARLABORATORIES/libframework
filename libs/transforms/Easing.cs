namespace kajarlabs.Framework.Scene.Transforms;

/// <summary>
/// Identifies the standard easing curves supported by <see cref="DefaultEasingFunction"/>
/// and <see cref="Interpolation"/>.
/// </summary>
public enum Easing
{
    /// <summary>
    /// Linear interpolation with no easing.
    /// </summary>
    None,

    /// <summary>
    /// Default ease-out curve (equivalent to <see cref="OutQuad"/>).
    /// </summary>
    Out,

    /// <summary>
    /// Default ease-in curve (equivalent to <see cref="InQuad"/>).
    /// </summary>
    In,

    /// <summary>
    /// Quadratic ease-in (<c>t^2</c>).
    /// </summary>
    InQuad,

    /// <summary>
    /// Quadratic ease-out.
    /// </summary>
    OutQuad,

    /// <summary>
    /// Quadratic ease-in-out.
    /// </summary>
    InOutQuad,

    /// <summary>
    /// Cubic ease-in (<c>t^3</c>).
    /// </summary>
    InCubic,

    /// <summary>
    /// Cubic ease-out.
    /// </summary>
    OutCubic,

    /// <summary>
    /// Cubic ease-in-out.
    /// </summary>
    InOutCubic,

    /// <summary>
    /// Quartic ease-in (<c>t^4</c>).
    /// </summary>
    InQuart,

    /// <summary>
    /// Quartic ease-out.
    /// </summary>
    OutQuart,

    /// <summary>
    /// Quartic ease-in-out.
    /// </summary>
    InOutQuart,

    /// <summary>
    /// Quintic ease-in (<c>t^5</c>).
    /// </summary>
    InQuint,

    /// <summary>
    /// Quintic ease-out.
    /// </summary>
    OutQuint,

    /// <summary>
    /// Quintic ease-in-out.
    /// </summary>
    InOutQuint,

    /// <summary>
    /// Sinusoidal ease-in.
    /// </summary>
    InSine,

    /// <summary>
    /// Sinusoidal ease-out.
    /// </summary>
    OutSine,

    /// <summary>
    /// Sinusoidal ease-in-out.
    /// </summary>
    InOutSine,

    /// <summary>
    /// Continuous exponential ease-in.
    /// </summary>
    InExpo,

    /// <summary>
    /// Continuous exponential ease-out.
    /// </summary>
    OutExpo,

    /// <summary>
    /// Continuous exponential ease-in-out.
    /// </summary>
    InOutExpo,

    /// <summary>
    /// Circular ease-in.
    /// </summary>
    InCirc,

    /// <summary>
    /// Circular ease-out.
    /// </summary>
    OutCirc,

    /// <summary>
    /// Circular ease-in-out.
    /// </summary>
    InOutCirc,

    /// <summary>
    /// Elastic ease-in with damped oscillation.
    /// </summary>
    InElastic,

    /// <summary>
    /// Elastic ease-out with damped oscillation.
    /// </summary>
    OutElastic,

    /// <summary>
    /// Elastic ease-in-out with damped oscillation.
    /// </summary>
    InOutElastic,

    /// <summary>
    /// Back ease-in with initial negative anticipation.
    /// </summary>
    InBack,

    /// <summary>
    /// Back ease-out with final overshoot.
    /// </summary>
    OutBack,

    /// <summary>
    /// Back ease-in-out with anticipation and overshoot.
    /// </summary>
    InOutBack,

    /// <summary>
    /// Bounce ease-in.
    /// </summary>
    InBounce,

    /// <summary>
    /// Bounce ease-out.
    /// </summary>
    OutBounce,

    /// <summary>
    /// Bounce ease-in-out.
    /// </summary>
    InOutBounce,

    /// <summary>
    /// Power-of-10 ease-out (<c>1 - (1 - t)^10</c>).
    /// </summary>
    OutPow10,
}
