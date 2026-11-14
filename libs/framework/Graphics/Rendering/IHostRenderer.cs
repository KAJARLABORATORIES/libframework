namespace kajarlabs.Framework.Graphics.Rendering;

internal interface IHostRenderer : IRenderer, IDisposable
{
    void Initialize(nint windowHandle);

    void BeginFrame(Viewport? viewport = null);

    void EndFrame(bool faulted = false);

    bool Present();

    void Resize(int width, int height);

    void RefreshSurface();

    void Shutdown();
}
