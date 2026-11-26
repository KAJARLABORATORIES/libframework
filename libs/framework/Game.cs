using System.Numerics;
using kajarlabs.Framework.DependencyInjection;
using kajarlabs.Framework.Graphics;
using kajarlabs.Framework.Graphics.Rendering;
using kajarlabs.Framework.Input;
using kajarlabs.Framework.IO;
using kajarlabs.Framework.IO.Resources;
using kajarlabs.Framework.Platform;
using kajarlabs.Framework.Scene;
using kajarlabs.Framework.Threading;
using Microsoft.Extensions.Logging;

namespace kajarlabs.Framework;

/// <summary>
/// Base class for a game hosted by a <see cref="GameHost"/>.
/// </summary>
public class Game : IDisposable
{
    private readonly TripleBuffer<DrawNode?> drawNodeBuffer = new();
    private readonly List<PlatformInputEvent> frameInputEvents = [];
    private GameHost? host;
    private CompositeResourceProvider? textureResourceScope;
    private TextureCache? textures;
    private bool disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="Game"/> class.
    /// </summary>
    public Game()
    {
        InputManager.Add(Root);
    }

    /// <summary>
    /// Gets the root <see cref="Input.InputManager"/> hosting <see cref="Root"/>.
    /// </summary>
    public InputManager InputManager { get; } = new();

    /// <summary>
    /// Gets the root scene <see cref="Container"/> updated on the update thread and rendered on the draw thread.
    /// </summary>
    public Container Root { get; } = new();

    /// <summary>
    /// Gets the <see cref="GameHost"/> running this game.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when the game is not attached to a host.</exception>
    public GameHost Host => host ?? throw new InvalidOperationException("Game is not attached to a GameHost.");

    /// <summary>
    /// Gets a value indicating whether this game is attached to a <see cref="GameHost"/>.
    /// </summary>
    public bool IsAttached => host is not null;

    /// <summary>
    /// Gets the user data storage owned by the host.
    /// </summary>
    public Storage Storage => Host.Storage;

    /// <summary>
    /// Gets the logger factory owned by the host.
    /// </summary>
    public ILoggerFactory LoggerFactory => Host.LoggerFactory;

    /// <summary>
    /// Gets a logger bound to this game type.
    /// </summary>
    public ILogger Logger => field ??= LoggerFactory.CreateLogger(GetType().FullName ?? GetType().Name);

    /// <summary>
    /// Gets the host window, or <see langword="null"/> when running without a window.
    /// </summary>
    public GameWindow? Window => Host.Window;

    /// <summary>
    /// Gets the renderer owned by the host.
    /// </summary>
    public IRenderer Renderer => Host.Renderer;

    /// <summary>
    /// Gets the root <see cref="CompositeResourceProvider"/> for this game. Subclasses may register embedded or
    /// disk resource providers before or during <see cref="Load"/>.
    /// </summary>
    public CompositeResourceProvider Resources { get; } = new();

    /// <summary>
    /// Gets the root <see cref="TextureCache"/> initialized for this game before <see cref="Load"/>, probing the
    /// <c>Textures/</c> namespace first and falling back to the root of <see cref="Resources"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when accessed before the game is loaded by a host.</exception>
    public TextureCache Textures
        => textures ?? throw new InvalidOperationException("Game textures are not initialized until the game is loaded by a GameHost.");

    /// <summary>
    /// Gets the root read-only dependency collection initialized for this game before <see cref="Load"/>,
    /// or <see cref="DependencyCollection.EMPTY"/> before initialization.
    /// </summary>
    public IReadOnlyDependencyCollection Dependencies { get; private set; } = DependencyCollection.EMPTY;

    /// <summary>
    /// Requests a clean shutdown of the host.
    /// </summary>
    public void Exit() => Host.Exit();

    /// <inheritdoc />
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    internal void Attach(GameHost targetHost)
    {
        ArgumentNullException.ThrowIfNull(targetHost);
        ObjectDisposedException.ThrowIf(disposed, this);

        if (host is not null && !ReferenceEquals(host, targetHost))
            throw new InvalidOperationException("Game is already attached to another GameHost.");

        host = targetHost;
        drawNodeBuffer.Clear();
    }

    internal void InvokeLoad()
    {
        InputManager.Clock = Host.Threads.Update.Clock;
        InputManager.ProcessCustomClock = false;
        syncRootViewportSize();

        if (InputManager.LoadState == LoadState.NotLoaded)
        {
            if (textures is null)
            {
                var scopedTextureResources = new CompositeResourceProvider();
                scopedTextureResources.AddProvider(Resources, leaveOpen: true);
                scopedTextureResources.AddProvider(new NamespacedResourceProvider(Resources, "Textures", leaveOpen: true), leaveOpen: false);
                textureResourceScope = scopedTextureResources;
                textures = new TextureCache(Renderer, scopedTextureResources, LoggerFactory.CreateLogger<TextureCache>());
            }

            var rootDependencies = new DependencyCollection();
            rootDependencies.Cache(this);
            rootDependencies.Cache(Host);
            rootDependencies.Cache(Renderer);
            rootDependencies.Cache(Storage);
            rootDependencies.Cache(LoggerFactory);
            rootDependencies.Cache(InputManager);
            rootDependencies.Cache<IResourceProvider>(Resources);
            rootDependencies.Cache(Resources);
            rootDependencies.Cache(Textures);

            if (Window is { } window)
                rootDependencies.Cache(window);

            InvokeCacheDependencies(rootDependencies);
            Dependencies = rootDependencies;
            InvokeResolveDependencies(rootDependencies);

            InputManager.Load(rootDependencies);
        }

        Load();
    }

    internal IReadOnlyList<PlatformInputEvent> CurrentFrameInputEvents => frameInputEvents;

    internal void InvokeUpdate()
    {
        frameInputEvents.Clear();

        if (host is { } attachedHost && attachedHost.DrainInputEvents(frameInputEvents) > 0)
            ProcessInputEvents(frameInputEvents);

        syncRootViewportSize();
        InputManager.HandlePlatformInputEvents(frameInputEvents);
        Update();
        InputManager.UpdateSubTree();

        var rootNode = InputManager.GenerateDrawNodeSubtree(drawNodeBuffer.WriteIndex);
        drawNodeBuffer.PublishWrite(rootNode);
    }

    internal virtual void ProcessInputEvents(IReadOnlyList<PlatformInputEvent> events)
    {
    }

    internal void InvokeDraw()
    {
        Draw();

        if (drawNodeBuffer.TryAcquireRead(out var rootNode) && rootNode is not null)
            rootNode.Draw(Renderer);
    }

    internal void InvokeExiting() => Exiting();

    internal void InvokeCacheDependencies(DependencyCollection dependencies)
        => CacheDependencies(dependencies);

    internal void InvokeResolveDependencies(IReadOnlyDependencyCollection dependencies)
        => ResolveDependencies(dependencies);

    /// <summary>
    /// Registers compile-time <c>[Cached]</c> dependencies into <paramref name="dependencies"/>.
    /// Overridden by source-generated dependency injection hooks.
    /// </summary>
    protected virtual void CacheDependencies(DependencyCollection dependencies)
    {
    }

    /// <summary>
    /// Populates compile-time <c>[Resolved]</c> members from <paramref name="dependencies"/>.
    /// Overridden by source-generated dependency injection hooks.
    /// </summary>
    protected virtual void ResolveDependencies(IReadOnlyDependencyCollection dependencies)
    {
    }

    /// <summary>
    /// Called once on the update thread after host storage, logging, SDL, and the window are initialized.
    /// </summary>
    protected virtual void Load()
    {
    }

    /// <summary>
    /// Called once per frame on the update thread.
    /// </summary>
    protected virtual void Update()
    {
    }

    /// <summary>
    /// Called once per frame on the draw thread.
    /// </summary>
    protected virtual void Draw()
    {
    }

    /// <summary>
    /// Called when the host begins shutting down.
    /// </summary>
    protected virtual void Exiting()
    {
    }

    /// <summary>
    /// Releases resources used by this <see cref="Game"/>.
    /// </summary>
    protected virtual void Dispose(bool disposing)
    {
        if (!disposing || disposed)
            return;

        disposed = true;
        Dependencies = DependencyCollection.EMPTY;
        InputManager.Dispose();
        textures?.Dispose();
        textureResourceScope?.Dispose();
        Resources.Dispose();
        drawNodeBuffer.Clear();
    }

    private void syncRootViewportSize()
    {
        var viewport = Renderer.Viewport;
        var targetSize = new Vector2(viewport.Width, viewport.Height);

        if (InputManager.Size != targetSize)
            InputManager.Size = targetSize;

        if (Root.Size != targetSize)
            Root.Size = targetSize;
    }
}
