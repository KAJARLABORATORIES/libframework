namespace kajarlabs.Framework.Scene.Transforms;

/// <summary>
/// Provides transform timeline inspection and lifecycle control extension members for <see cref="Drawable"/>.
/// </summary>
public static class DrawableTransformExtensions
{
    extension(Drawable drawable)
    {
        /// <summary>
        /// Gets or sets a value indicating whether completed transforms are automatically removed from this drawable's timeline.
        /// Defaults to <see langword="true"/>. Set to <see langword="false"/> to retain completed transforms and captured
        /// start values for rewindable or seekable clocks.
        /// </summary>
        public bool RemoveCompletedTransforms
        {
            get
            {
                ArgumentNullException.ThrowIfNull(drawable);
                return !DrawableTransformState.TryGet(drawable, out var state) || state.RemoveCompletedTransforms;
            }
            set
            {
                ArgumentNullException.ThrowIfNull(drawable);

                if (DrawableTransformState.TryGet(drawable, out var state))
                {
                    state.RemoveCompletedTransforms = value;
                    return;
                }

                if (!value)
                    DrawableTransformState.GetOrAttach(drawable).RemoveCompletedTransforms = false;
            }
        }

        /// <summary>
        /// Gets a value indicating whether this drawable currently has any transforms in its timeline.
        /// </summary>
        public bool HasTransforms
        {
            get
            {
                ArgumentNullException.ThrowIfNull(drawable);
                return DrawableTransformState.TryGet(drawable, out var state) && state.Count > 0;
            }
        }

        /// <summary>
        /// Gets the latest effective end time across all scheduled transforms on this drawable,
        /// or <see cref="Drawable.Time"/> when no transforms are scheduled.
        /// </summary>
        public double LatestTransformEndTime
        {
            get
            {
                ArgumentNullException.ThrowIfNull(drawable);
                return DrawableTransformState.TryGet(drawable, out var state)
                    ? state.GetLatestTransformEndTime(drawable)
                    : drawable.Time;
            }
        }

        /// <summary>
        /// Removes and aborts all scheduled transforms on this drawable.
        /// </summary>
        public void ClearTransforms()
        {
            ArgumentNullException.ThrowIfNull(drawable);

            if (DrawableTransformState.TryGet(drawable, out var state))
                state.ClearTransforms(drawable);
        }

        /// <summary>
        /// Removes transforms starting at or after <paramref name="time"/> and truncates any in-flight transform
        /// extending past <paramref name="time"/>, signaling abort on affected transforms.
        /// </summary>
        /// <param name="time">The timeline cut-off time in milliseconds.</param>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="time"/> is <see cref="double.NaN"/>.</exception>
        public void ClearTransformsAfter(double time)
        {
            ArgumentNullException.ThrowIfNull(drawable);

            if (double.IsNaN(time))
                throw new ArgumentOutOfRangeException(nameof(time), time, "Time must not be NaN.");

            if (DrawableTransformState.TryGet(drawable, out var state))
                state.ClearTransformsAfter(drawable, time);
        }

        /// <summary>
        /// Immediately completes all currently scheduled transforms on this drawable in chronological order,
        /// applying their final values without hanging on infinite loops.
        /// </summary>
        public void FinishTransforms()
        {
            ArgumentNullException.ThrowIfNull(drawable);

            if (DrawableTransformState.TryGet(drawable, out var state))
                state.FinishTransforms(drawable);
        }
    }
}
