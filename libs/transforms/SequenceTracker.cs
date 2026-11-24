namespace kajarlabs.Framework.Scene.Transforms;

internal interface ISequenceTracker : ITransformObserver
{
    void AddOnComplete(Delegate callback);

    void AddOnAbort(Delegate callback);

    void AddFinally(Delegate callback);
}

internal sealed class SequenceTracker<TDrawable> : ISequenceTracker
    where TDrawable : Drawable
{
    private readonly int sequenceId;
    private readonly DrawableTransformState state;
    private Action<TDrawable>? onComplete;
    private Action<TDrawable>? onAbort;
    private Action<TDrawable>? onFinally;
    private bool isFinalized;

    public SequenceTracker(int sequenceId, DrawableTransformState state)
    {
        this.sequenceId = sequenceId;
        this.state = state;
    }

    public void AddOnComplete(Delegate callback)
        => onComplete += (Action<TDrawable>)callback;

    public void AddOnAbort(Delegate callback)
        => onAbort += (Action<TDrawable>)callback;

    public void AddFinally(Delegate callback)
        => onFinally += (Action<TDrawable>)callback;

    public void OnTransformCompleted(Drawable target)
    {
        if (isFinalized || state.HasActiveEntriesForSequence(sequenceId))
            return;

        isFinalized = true;
        state.RemoveSequenceTracker(sequenceId);

        var typedTarget = (TDrawable)target;
        onComplete?.Invoke(typedTarget);

        if (!target.IsDisposed)
            onFinally?.Invoke(typedTarget);
    }

    public void OnTransformAborted(Drawable target)
    {
        if (isFinalized)
            return;

        isFinalized = true;
        state.CancelSequenceEntriesOnAbort(sequenceId);
        state.RemoveSequenceTracker(sequenceId);

        var typedTarget = (TDrawable)target;
        onAbort?.Invoke(typedTarget);

        if (!target.IsDisposed)
            onFinally?.Invoke(typedTarget);
    }
}
