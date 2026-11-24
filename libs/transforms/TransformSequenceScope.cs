using System.Runtime.InteropServices;

namespace kajarlabs.Framework.Scene.Transforms;

/// <summary>
/// A disposable scope that temporarily overrides the default transform start time for a <see cref="Drawable"/>,
/// restoring the previous start time on disposal without heap allocations.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public readonly struct TransformSequenceScope : IDisposable
{
    private readonly Drawable target;
    private readonly DrawableTransformState state;
    private readonly double previousStartTime;
    private readonly int scopeToken;
    private readonly int previousScopeToken;
    private readonly bool hadPreviousStartTime;

    internal TransformSequenceScope(
        Drawable target,
        DrawableTransformState state,
        bool hadPreviousStartTime,
        double previousStartTime,
        int scopeToken,
        int previousScopeToken)
    {
        this.target = target;
        this.state = state;
        this.hadPreviousStartTime = hadPreviousStartTime;
        this.previousStartTime = previousStartTime;
        this.scopeToken = scopeToken;
        this.previousScopeToken = previousScopeToken;
    }

    /// <summary>
    /// Restores the enclosing sequence start time on the target <see cref="Drawable"/>.
    /// </summary>
    public void Dispose()
    {
        if (state is null || target is null)
            return;

        state.ExitSequenceScope(target, scopeToken, previousScopeToken, hadPreviousStartTime, previousStartTime);
    }
}
