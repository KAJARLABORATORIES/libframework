using kajarlabs.Framework.Input;

namespace kajarlabs.Framework.Samples.TextureLoading;

internal sealed class ShowcaseKeyBindingContainer : KeyBindingContainer<TextureShowcaseAction>, IKeyBindingHandler<TextureShowcaseAction>
{
    private readonly TextureLoadingGame game;

    public ShowcaseKeyBindingContainer(TextureLoadingGame game)
        : base(SimultaneousBindingMode.Unique)
    {
        this.game = game;
        KeyBindings =
        [
            new(new KeyCombination(Key.O), TextureShowcaseAction.ToggleDiskOverride),
            new(new KeyCombination(Key.Space), TextureShowcaseAction.ToggleSampler),
            new(new KeyCombination(Key.R), TextureShowcaseAction.ReloadCache),
            new(new KeyCombination(Key.Escape), TextureShowcaseAction.Exit),
        ];
    }

    public bool OnPressed(KeyBindingPressEvent<TextureShowcaseAction> e)
    {
        switch (e.Action)
        {
            case TextureShowcaseAction.ToggleDiskOverride:
                game.ToggleDiskOverride();
                return true;

            case TextureShowcaseAction.ToggleSampler:
                game.ToggleInteractiveSampler();
                return true;

            case TextureShowcaseAction.ReloadCache:
                game.ReloadAllTextures();
                return true;

            case TextureShowcaseAction.Exit:
                game.Exit();
                return true;

            default:
                return false;
        }
    }

    public void OnReleased(KeyBindingReleaseEvent<TextureShowcaseAction> e)
    {
    }
}
