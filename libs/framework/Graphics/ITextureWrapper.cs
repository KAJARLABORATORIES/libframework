namespace kajarlabs.Framework.Graphics;

/// <summary>
/// Internal seam for wrapper <see cref="ITexture"/> implementations so renderers can unwrap the underlying
/// renderer-owned texture without widening public contracts.
/// </summary>
internal interface ITextureWrapper
{
    ITexture Underlying { get; }
}
