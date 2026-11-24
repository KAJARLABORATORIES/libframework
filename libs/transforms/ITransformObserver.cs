namespace kajarlabs.Framework.Scene.Transforms;

internal interface ITransformObserver
{
    void OnTransformCompleted(Drawable target);

    void OnTransformAborted(Drawable target);
}
