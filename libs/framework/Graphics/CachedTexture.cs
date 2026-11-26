using kajarlabs.Framework.Numerics;

namespace kajarlabs.Framework.Graphics;

/// <summary>
/// Immutable, cache-owned wrapper around a renderer <see cref="ITexture"/> whose public <see cref="Dispose"/>
/// is a safe no-op so callers cannot accidentally release shared cached GPU textures.
/// </summary>
internal sealed class CachedTexture : ITexture, ITextureWrapper
{
    public CachedTexture(ITexture underlying)
    {
        ArgumentNullException.ThrowIfNull(underlying);
        Underlying = underlying;
    }

    public ITexture Underlying { get; }

    public int Width => Underlying.Width;

    public int Height => Underlying.Height;

    public void SetData(ReadOnlySpan<byte> rgba8Pixels)
        => throw new NotSupportedException("Cached textures owned by TextureCache cannot be mutated.");

    public void SetData(RectangleI region, ReadOnlySpan<byte> rgba8Pixels)
        => throw new NotSupportedException("Cached textures owned by TextureCache cannot be mutated.");

    public void Dispose()
    {
        // Shared cached textures are owned by TextureCache; caller disposal is an intentional no-op.
    }

    internal void ReleaseUnderlying() => Underlying.Dispose();
}
