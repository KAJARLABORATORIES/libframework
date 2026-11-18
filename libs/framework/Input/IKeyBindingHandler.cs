namespace kajarlabs.Framework.Input;

/// <summary>
/// Implemented by <see cref="Scene.Drawable"/> elements that handle bound actions of type <typeparamref name="TAction"/>
/// dispatched by an enclosing <see cref="KeyBindingContainer{TAction}"/> without runtime reflection.
/// </summary>
/// <typeparam name="TAction">The strongly-typed action enum or value type.</typeparam>
public interface IKeyBindingHandler<TAction>
{
    /// <summary>
    /// Invoked when a matching key binding is pressed.
    /// </summary>
    /// <returns><see langword="true"/> to consume the binding and stop propagation to outer handlers/containers; otherwise <see langword="false"/>.</returns>
    bool OnPressed(KeyBindingPressEvent<TAction> e);

    /// <summary>
    /// Invoked when a previously pressed binding is released (via key/button release, removal, disposal, or focus loss).
    /// </summary>
    void OnReleased(KeyBindingReleaseEvent<TAction> e)
    {
    }
}
