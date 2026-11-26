using System.Numerics;
using kajarlabs.Framework.Graphics;
using kajarlabs.Framework.Input;
using kajarlabs.Framework.IO;
using kajarlabs.Framework.IO.Resources;
using kajarlabs.Framework.Numerics;
using kajarlabs.Framework.Scene;
using Microsoft.Extensions.Logging;

namespace kajarlabs.Framework.Samples.TextureLoading;

internal enum TextureShowcaseAction
{
    ToggleDiskOverride,
    ToggleSampler,
    ReloadCache,
    Exit,
}

internal sealed partial class TextureLoadingGame : Game
{
    private readonly TimeSpan? autoExitAfter;
    private readonly StorageResourceProvider diskProvider;
    private readonly List<TextureShowcaseCard> reloadableCards = [];

    private TextureShowcaseCard? overrideCard;
    private TextureShowcaseCard? interactiveSamplerCard;
    private AsyncTextureCard? asyncCard;
    private bool diskOverrideEnabled = true;
    private bool useNearestOnInteractive = true;
    private double elapsedRunMs;
    private bool exitRequested;

    public TextureLoadingGame(TimeSpan? autoExitAfter)
    {
        this.autoExitAfter = autoExitAfter;

        var embedded = new AssemblyResourceProvider(
            typeof(TextureLoadingGame).Assembly,
            "kajarlabs.Framework.Samples.TextureLoading.Assets");

        var diskAssetsPath = Path.Combine(AppContext.BaseDirectory, "DiskAssets");
        diskProvider = new StorageResourceProvider(new FileSystemStorage(diskAssetsPath));

        // Base layer: embedded assembly resources. Override layer: disk storage (last-added wins).
        Resources.AddProvider(embedded, leaveOpen: false);
        Resources.AddProvider(diskProvider, leaveOpen: true);
    }

    protected override void Load()
    {
        var backdrop = new Box
        {
            RelativeSizeAxes = Axes.Both,
            Size = Vector2.One,
            Color = new Color4(0.06f, 0.08f, 0.12f, 1f),
            Depth = 30f,
        };

        var bindingContainer = new ShowcaseKeyBindingContainer(this)
        {
            RelativeSizeAxes = Axes.Both,
            Size = Vector2.One,
        };

        var layoutRoot = new Container
        {
            RelativeSizeAxes = Axes.Both,
            Size = Vector2.One,
            Padding = MarginPadding.All(28f),
        };

        // Top-left: Sampler comparison (Nearest vs Linear on crisp 16x16 pixel-art PNG).
        var nearestCard = new TextureShowcaseCard(
            "pixel-emblem",
            TextureSamplerParameters.NEAREST_CLAMP,
            accentColor: new Color4(0.25f, 0.85f, 1f, 1f))
        {
            Anchor = Anchor.TopLeft,
            Origin = Anchor.TopLeft,
            Size = new Vector2(240f, 240f),
        };

        var linearCard = new TextureShowcaseCard(
            "pixel-emblem",
            TextureSamplerParameters.LINEAR_CLAMP,
            accentColor: new Color4(0.55f, 0.65f, 1f, 1f))
        {
            Anchor = Anchor.TopLeft,
            Origin = Anchor.TopLeft,
            Position = new Vector2(264f, 0f),
            Size = new Vector2(240f, 240f),
        };

        // Top-right: Embedded vs Disk override (press O to toggle live).
        overrideCard = new TextureShowcaseCard(
            "override-banner",
            TextureSamplerParameters.NEAREST_CLAMP,
            accentColor: new Color4(0.98f, 0.72f, 0.22f, 1f))
        {
            Anchor = Anchor.TopRight,
            Origin = Anchor.TopRight,
            Size = new Vector2(260f, 240f),
        };

        // Bottom-left: Disk-only BMP & Embedded JPEG.
        var diskBmpCard = new TextureShowcaseCard(
            "disk-tile",
            TextureSamplerParameters.NEAREST_CLAMP,
            accentColor: new Color4(0.96f, 0.38f, 0.56f, 1f))
        {
            Anchor = Anchor.BottomLeft,
            Origin = Anchor.BottomLeft,
            Size = new Vector2(240f, 240f),
        };

        interactiveSamplerCard = new TextureShowcaseCard(
            "gradient-card",
            TextureSamplerParameters.NEAREST_CLAMP,
            accentColor: new Color4(0.75f, 0.52f, 0.98f, 1f))
        {
            Anchor = Anchor.BottomLeft,
            Origin = Anchor.BottomLeft,
            Position = new Vector2(264f, 0f),
            Size = new Vector2(240f, 240f),
        };

        reloadableCards.AddRange([nearestCard, linearCard, overrideCard, diskBmpCard, interactiveSamplerCard]);

        // Bottom-right: Async loaded TGA sprite via LoadComponentAsync + TextureCache.GetAsync.
        asyncCard = new AsyncTextureCard("async-badge", accentColor: new Color4(0.32f, 0.90f, 0.56f, 1f))
        {
            Anchor = Anchor.BottomRight,
            Origin = Anchor.BottomRight,
            Size = new Vector2(260f, 240f),
        };

        layoutRoot.AddRange([nearestCard, linearCard, overrideCard, diskBmpCard, interactiveSamplerCard]);
        _ = layoutRoot.LoadComponentAsync(asyncCard, layoutRoot.Add);

        bindingContainer.Add(layoutRoot);
        Root.AddRange([backdrop, bindingContainer]);

        logShowcaseReady(Logger);
    }

    protected override void Update()
    {
        base.Update();

        elapsedRunMs += Root.ElapsedFrameTime;

        if (autoExitAfter is { } limit && !exitRequested && elapsedRunMs >= limit.TotalMilliseconds)
        {
            exitRequested = true;
            Host.Exit();
        }
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (disposing)
            diskProvider.Dispose();
    }

    internal void ToggleDiskOverride()
    {
        diskOverrideEnabled = !diskOverrideEnabled;

        if (diskOverrideEnabled)
            Resources.AddProvider(diskProvider, leaveOpen: true);
        else
            Resources.RemoveProvider(diskProvider);

        Textures.Remove("override-banner");
        overrideCard?.ReloadTexture();
        overrideCard?.SetAccentColor(diskOverrideEnabled
            ? new Color4(0.98f, 0.72f, 0.22f, 1f)
            : new Color4(0.22f, 0.78f, 0.98f, 1f));

        logDiskOverrideToggled(Logger, diskOverrideEnabled);
    }

    internal void ToggleInteractiveSampler()
    {
        useNearestOnInteractive = !useNearestOnInteractive;
        var sampler = useNearestOnInteractive
            ? TextureSamplerParameters.NEAREST_CLAMP
            : TextureSamplerParameters.LINEAR_CLAMP;

        interactiveSamplerCard?.SetSampler(sampler);
        logSamplerToggled(Logger, sampler.Filtering);
    }

    internal void ReloadAllTextures()
    {
        Textures.Clear();

        for (var i = 0; i < reloadableCards.Count; i++)
            reloadableCards[i].ReloadTexture();

        asyncCard?.ReloadTextureAsync();
        logCacheReloaded(Logger);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Texture showcase ready. Controls: O=Toggle disk override, Space=Toggle sampler, R=Clear & reload cache, Esc=Exit.")]
    private static partial void logShowcaseReady(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Disk override toggled: Enabled={Enabled}")]
    private static partial void logDiskOverrideToggled(ILogger logger, bool enabled);

    [LoggerMessage(Level = LogLevel.Information, Message = "Interactive card sampler switched to {Filtering}")]
    private static partial void logSamplerToggled(ILogger logger, TextureFilteringMode filtering);

    [LoggerMessage(Level = LogLevel.Information, Message = "TextureCache cleared and reloaded all cards.")]
    private static partial void logCacheReloaded(ILogger logger);
}
