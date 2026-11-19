using kajarlabs.Framework.Graphics;
using kajarlabs.Framework.Graphics.Rendering;
using kajarlabs.Framework.Numerics;
using kajarlabs.Framework.Tests.TestUtilities;
using Microsoft.Extensions.Logging;

namespace kajarlabs.Framework.Tests.Graphics.Rendering;

public sealed class GpuDeviceContextTests
{
    private static readonly nint fake_window = new(0x1000);

    [Fact]
    public void InitializeSelectsShaderFormatsClaimsWindowConfiguresSwapchainAndLogsDriver()
    {
        var backend = new FakeGpuBackend
        {
            DriverName = "vulkan",
            SupportedShaderFormats = GpuShaderFormats.SpirV | GpuShaderFormats.Dxil,
            MailboxSupported = true,
            SwapchainFormat = new GpuTextureFormat(42),
            OffscreenFormat = new GpuTextureFormat(42),
        };
        using var loggerProvider = new CapturingLoggerProvider();
        var logger = loggerProvider.CreateLogger("GpuTest");
        using var context = new GpuDeviceContext(backend, logger);

        context.Initialize(fake_window, debugMode: true, preferredDriver: "vulkan");

        Assert.Equal(GpuDeviceLifecycleState.Initialized, context.LifecycleState);
        Assert.Equal("vulkan", context.DeviceDriver);
        Assert.Equal(
            GpuShaderFormats.SpirV | GpuShaderFormats.Dxil | GpuShaderFormats.Msl,
            backend.RequestedShaderFormats);
        Assert.Equal(GpuShaderFormats.SpirV | GpuShaderFormats.Dxil, context.SupportedShaderFormats);
        Assert.Equal(GpuPresentMode.Mailbox, context.ActivePresentMode);
        Assert.Equal(new GpuTextureFormat(42), context.SwapchainFormat);
        Assert.Equal(new GpuTextureFormat(42), context.OffscreenColorFormat);

        Assert.Equal(
            [
                "CreateDevice",
                "GetDeviceDriver",
                "GetSupportedShaderFormats",
                "ClaimWindow",
                "WindowSupportsSwapchainComposition",
                "WindowSupportsPresentMode",
                "SetSwapchainParameters",
                "GetSwapchainTextureFormat",
                "ResolveOffscreenColorFormat",
            ],
            backend.Calls.Take(9));

        Assert.Contains(
            loggerProvider.Entries,
            e => e.Level == LogLevel.Information && e.Message.Contains("vulkan", StringComparison.Ordinal));
    }

    [Fact]
    public void InitializeFallsBackToVSyncWhenMailboxIsUnsupported()
    {
        var backend = new FakeGpuBackend { MailboxSupported = false };
        using var loggerProvider = new CapturingLoggerProvider();
        using var context = new GpuDeviceContext(backend, loggerProvider.CreateLogger("GpuTest"));

        context.Initialize(fake_window);

        Assert.Equal(GpuPresentMode.VSync, context.ActivePresentMode);
        Assert.Equal(GpuPresentMode.VSync, backend.ConfiguredPresentMode);
    }

    [Fact]
    public void InitializeRollsBackDeviceWhenClaimWindowFails()
    {
        var backend = new FakeGpuBackend { ClaimWindowResult = false };
        using var loggerProvider = new CapturingLoggerProvider();
        using var context = new GpuDeviceContext(backend, loggerProvider.CreateLogger("GpuTest"));

        Assert.Throws<InvalidOperationException>(() => context.Initialize(fake_window));
        Assert.Equal(GpuDeviceLifecycleState.Uninitialized, context.LifecycleState);
        Assert.Equal(["CreateDevice", "GetDeviceDriver", "GetSupportedShaderFormats", "ClaimWindow", "DestroyDevice"], backend.Calls);
    }

    [Fact]
    public void DrawPublishesOffscreenTargetAndPresentBlitsLatestFrame()
    {
        var backend = new FakeGpuBackend
        {
            PixelWidth = 1280,
            PixelHeight = 720,
            AutoSignalFences = false,
        };
        using var loggerProvider = new CapturingLoggerProvider();
        using var context = new GpuDeviceContext(backend, loggerProvider.CreateLogger("GpuTest"));

        context.Initialize(fake_window);

        Assert.False(context.PresentLatestFrame());
        Assert.Equal(0, backend.BlitCount);

        Assert.True(context.BeginOffscreenFrame(out var frame));
        Assert.Equal(1280, frame.Width);
        Assert.Equal(720, frame.Height);
        Assert.NotEqual(nint.Zero, frame.ColorTargetTexture);
        Assert.NotEqual(nint.Zero, frame.CommandBuffer);

        context.EndOffscreenFrame(in frame);

        Assert.True(context.PresentLatestFrame());
        Assert.Equal(1, backend.BlitCount);
        Assert.Equal(frame.ColorTargetTexture, backend.LastBlitSourceTexture);
        Assert.Equal(1280u, backend.LastBlitSourceWidth);
        Assert.Equal(720u, backend.LastBlitSourceHeight);

        Assert.False(context.PresentLatestFrame());
        Assert.Equal(1, backend.BlitCount);
    }

    [Fact]
    public void NonBlockingPresentSkipsBlitWhenSwapchainTextureIsNullAndRetriesLater()
    {
        var backend = new FakeGpuBackend
        {
            PixelWidth = 800,
            PixelHeight = 600,
            ReturnNullSwapchainTexture = true,
        };
        using var loggerProvider = new CapturingLoggerProvider();
        using var context = new GpuDeviceContext(backend, loggerProvider.CreateLogger("GpuTest"));

        context.Initialize(fake_window);

        Assert.True(context.BeginOffscreenFrame(out var frame));
        context.EndOffscreenFrame(in frame);

        Assert.False(context.PresentLatestFrame());
        Assert.Equal(0, backend.BlitCount);
        Assert.Equal(0, backend.CancelCommandBufferCount);
        Assert.Equal(1, backend.PlainSubmitCountForPresent);
        Assert.DoesNotContain(loggerProvider.Entries, e => e.Level >= LogLevel.Error);

        backend.ReturnNullSwapchainTexture = false;
        Assert.True(context.PresentLatestFrame());
        Assert.Equal(1, backend.BlitCount);
        Assert.Equal(frame.ColorTargetTexture, backend.LastBlitSourceTexture);
    }

    [Fact]
    public void ResizeRecreatesOffscreenTexturesAndDefersOldTextureReleaseUntilFenceCompletes()
    {
        var backend = new FakeGpuBackend
        {
            PixelWidth = 640,
            PixelHeight = 480,
            AutoSignalFences = false,
        };
        using var loggerProvider = new CapturingLoggerProvider();
        using var context = new GpuDeviceContext(backend, loggerProvider.CreateLogger("GpuTest"));

        context.Initialize(fake_window);

        Assert.True(context.BeginOffscreenFrame(out var frame1));
        var tex1 = frame1.ColorTargetTexture;
        context.EndOffscreenFrame(in frame1);
        Assert.True(context.PresentLatestFrame());

        // Present fence for slot 0 is still unsignaled; resize and draw on remaining slots.
        context.HandleResize(1024, 768);

        Assert.True(context.BeginOffscreenFrame(out var frame2));
        Assert.NotEqual(tex1, frame2.ColorTargetTexture);
        Assert.Equal(1024, frame2.Width);
        Assert.Equal(768, frame2.Height);
        Assert.DoesNotContain(tex1, backend.ReleasedTextures);
        context.EndOffscreenFrame(in frame2);

        // Once fence is signaled, reusing slot 0 releases old 640x480 texture and allocates 1024x768.
        backend.SignalAllFences();
        Assert.True(context.BeginOffscreenFrame(out var frame3));
        Assert.Equal(1024, frame3.Width);
        Assert.Equal(768, frame3.Height);
        context.EndOffscreenFrame(in frame3);

        Assert.True(context.PresentLatestFrame());

        // Draw one more frame so slot 0 with tex1 is picked up or verify on shutdown.
        backend.SignalAllFences();
        Assert.True(context.BeginOffscreenFrame(out var frame4));
        context.EndOffscreenFrame(in frame4);

        Assert.Contains(tex1, backend.ReleasedTextures);
    }

    [Fact]
    public void MinimizedAndZeroSizeWindowsSkipGracefullyWithoutThrowingOrErrorSpam()
    {
        var backend = new FakeGpuBackend
        {
            PixelWidth = 800,
            PixelHeight = 600,
        };
        using var loggerProvider = new CapturingLoggerProvider();
        using var context = new GpuDeviceContext(backend, loggerProvider.CreateLogger("GpuTest"));

        context.Initialize(fake_window);

        context.HandleResize(0, 0);

        for (var i = 0; i < 25; i++)
        {
            Assert.False(context.BeginOffscreenFrame(out _));
            Assert.False(context.PresentLatestFrame());
        }

        backend.Minimized = true;
        context.HandleResize(800, 600);

        for (var i = 0; i < 25; i++)
        {
            Assert.False(context.BeginOffscreenFrame(out _));
            Assert.False(context.PresentLatestFrame());
        }

        Assert.Empty(backend.CreatedTextures);
        Assert.Equal(0, backend.BlitCount);
        Assert.DoesNotContain(loggerProvider.Entries, e => e.Level >= LogLevel.Warning);
        Assert.True(loggerProvider.Entries.Count(e => e.Level == LogLevel.Debug) <= 1);

        backend.Minimized = false;
        context.RefreshSurfaceMetrics();

        Assert.True(context.BeginOffscreenFrame(out var restoredFrame));
        Assert.Equal(800, restoredFrame.Width);
        Assert.Equal(600, restoredFrame.Height);
        context.EndOffscreenFrame(in restoredFrame);
        Assert.True(context.PresentLatestFrame());
        Assert.Equal(1, backend.BlitCount);
    }

    [Fact]
    public void ShutdownExecutesDeterministicOrderBeforeWindowDestruction()
    {
        var backend = new FakeGpuBackend
        {
            PixelWidth = 1280,
            PixelHeight = 720,
            AutoSignalFences = false,
        };
        using var loggerProvider = new CapturingLoggerProvider();
        var context = new GpuDeviceContext(backend, loggerProvider.CreateLogger("GpuTest"));

        context.Initialize(fake_window);

        Assert.True(context.BeginOffscreenFrame(out var frame1));
        context.EndOffscreenFrame(in frame1);
        Assert.True(context.PresentLatestFrame());

        Assert.True(context.BeginOffscreenFrame(out var frame2));
        context.EndOffscreenFrame(in frame2);

        backend.Calls.Clear();
        context.Shutdown();
        backend.Calls.Add("DestroyWindow");

        // Idempotent second call must not emit duplicate shutdown operations.
        context.Dispose();

        Assert.Equal(GpuDeviceLifecycleState.Disposed, context.LifecycleState);

        var waitIdleIndex = backend.Calls.IndexOf("WaitForIdle");
        var releaseWindowIndex = backend.Calls.IndexOf("ReleaseWindow");
        var destroyDeviceIndex = backend.Calls.IndexOf("DestroyDevice");
        var destroyWindowIndex = backend.Calls.IndexOf("DestroyWindow");

        Assert.True(waitIdleIndex >= 0);
        Assert.True(releaseWindowIndex > waitIdleIndex);
        Assert.True(destroyDeviceIndex > releaseWindowIndex);
        Assert.True(destroyWindowIndex > destroyDeviceIndex);

        var releaseTextureIndices = backend.Calls
            .Select((name, idx) => (name, idx))
            .Where(x => x.name == "ReleaseTexture")
            .Select(x => x.idx)
            .ToList();

        Assert.Equal(2, releaseTextureIndices.Count);
        Assert.All(releaseTextureIndices, idx => Assert.InRange(idx, waitIdleIndex + 1, releaseWindowIndex - 1));

        var releaseFenceIndices = backend.Calls
            .Select((name, idx) => (name, idx))
            .Where(x => x.name == "ReleaseFence")
            .Select(x => x.idx)
            .ToList();

        Assert.Single(releaseFenceIndices);
        Assert.InRange(releaseFenceIndices[0], waitIdleIndex + 1, releaseWindowIndex - 1);
    }

    [Fact]
    public void ConcurrentDrawAndInputThreadsRunWithoutDeadlockOrTextureUseAfterFree()
    {
        var backend = new FakeGpuBackend
        {
            PixelWidth = 640,
            PixelHeight = 360,
        };
        using var loggerProvider = new CapturingLoggerProvider();
        using var context = new GpuDeviceContext(backend, loggerProvider.CreateLogger("GpuTest"));

        context.Initialize(fake_window);

        using var stopSignal = new ManualResetEventSlim(false);
        Exception? workerException = null;

        var drawThread = new Thread(() =>
        {
            try
            {
                for (var i = 0; i < 60 && !stopSignal.IsSet; i++)
                {
                    if (context.BeginOffscreenFrame(out var frame))
                    {
                        if (i % 15 == 0)
                        {
                            // Exercise cancel path cleanly as well.
                            context.CancelOffscreenFrame(in frame);
                        }
                        else
                        {
                            context.EndOffscreenFrame(in frame);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                workerException = ex;
            }
        });

        drawThread.Start();

        for (var i = 0; i < 60; i++)
        {
            if (i == 20)
                context.HandleResize(800, 450);

            context.PresentLatestFrame();
            Thread.Yield();
        }

        stopSignal.Set();
        Assert.True(drawThread.Join(TimeSpan.FromSeconds(5)));
        Assert.Null(workerException);

        context.Shutdown();
        Assert.False(backend.UseAfterFreeDetected);
    }

    [Fact]
    public void ReentrantPresentLatestFrameReturnsFalseWithoutCorruptingSlots()
    {
        var backend = new FakeGpuBackend
        {
            PixelWidth = 800,
            PixelHeight = 600,
        };
        using var loggerProvider = new CapturingLoggerProvider();
        using var context = new GpuDeviceContext(backend, loggerProvider.CreateLogger("GpuTest"));

        context.Initialize(fake_window);

        Assert.True(context.BeginOffscreenFrame(out var frame));
        context.EndOffscreenFrame(in frame);

        var reentrantResult = true;
        backend.OnBlitTexture = () => reentrantResult = context.PresentLatestFrame();

        Assert.True(context.PresentLatestFrame());
        Assert.False(reentrantResult);
        Assert.Equal(1, backend.BlitCount);

        backend.OnBlitTexture = null;
        Assert.True(context.BeginOffscreenFrame(out var nextFrame));
        context.EndOffscreenFrame(in nextFrame);
        Assert.True(context.PresentLatestFrame());
        Assert.Equal(2, backend.BlitCount);
    }

    private sealed class FakeGpuBackend : StubGpuBackend
    {
        private readonly Lock sync = new();
        private readonly HashSet<nint> activeTextures = [];
        private readonly Dictionary<nint, bool> fences = [];
        private readonly HashSet<nint> drawCommandBuffers = [];

        public List<string> Calls { get; } = [];
        public List<nint> CreatedTextures { get; } = [];
        public List<nint> ReleasedTextures { get; } = [];

        public string DriverName { get; init; } = "vulkan";
        public GpuShaderFormats RequestedShaderFormats { get; private set; }
        public GpuShaderFormats SupportedShaderFormats { get; init; } = GpuShaderFormats.SpirV;
        public bool ClaimWindowResult { get; init; } = true;
        public bool MailboxSupported { get; init; } = true;
        public GpuPresentMode ConfiguredPresentMode { get; private set; }
        public GpuTextureFormat SwapchainFormat { get; init; } = new(12);
        public GpuTextureFormat OffscreenFormat { get; init; } = new(12);
        public bool Minimized { get; set; }
        public bool ReturnNullSwapchainTexture { get; set; }
        public bool AutoSignalFences { get; init; } = true;
        public Action? OnBlitTexture { get; set; }

        public int BlitCount { get; private set; }
        public int CancelCommandBufferCount { get; private set; }
        public int PlainSubmitCountForPresent { get; private set; }
        public nint LastBlitSourceTexture { get; private set; }
        public uint LastBlitSourceWidth { get; private set; }
        public uint LastBlitSourceHeight { get; private set; }
        public bool UseAfterFreeDetected { get; private set; }

        public override nint CreateDevice(GpuShaderFormats shaderFormats, bool debugMode, string? preferredDriver)
        {
            lock (sync)
            {
                Calls.Add(nameof(CreateDevice));
                RequestedShaderFormats = shaderFormats;
                return NextHandle();
            }
        }

        public override void DestroyDevice(nint device)
        {
            lock (sync)
                Calls.Add(nameof(DestroyDevice));
        }

        public override string? GetDeviceDriver(nint device)
        {
            lock (sync)
            {
                Calls.Add(nameof(GetDeviceDriver));
                return DriverName;
            }
        }

        public override GpuShaderFormats GetSupportedShaderFormats(nint device)
        {
            lock (sync)
            {
                Calls.Add(nameof(GetSupportedShaderFormats));
                return SupportedShaderFormats;
            }
        }

        public override bool ClaimWindow(nint device, nint windowHandle)
        {
            lock (sync)
            {
                Calls.Add(nameof(ClaimWindow));
                return ClaimWindowResult;
            }
        }

        public override void ReleaseWindow(nint device, nint windowHandle)
        {
            lock (sync)
                Calls.Add(nameof(ReleaseWindow));
        }

        public override bool WindowSupportsSwapchainComposition(nint device, nint windowHandle, GpuSwapchainComposition composition)
        {
            lock (sync)
            {
                Calls.Add(nameof(WindowSupportsSwapchainComposition));
                return composition == GpuSwapchainComposition.Sdr;
            }
        }

        public override bool WindowSupportsPresentMode(nint device, nint windowHandle, GpuPresentMode presentMode)
        {
            lock (sync)
            {
                Calls.Add(nameof(WindowSupportsPresentMode));
                return presentMode == GpuPresentMode.VSync || (presentMode == GpuPresentMode.Mailbox && MailboxSupported);
            }
        }

        public override bool SetSwapchainParameters(
            nint device,
            nint windowHandle,
            GpuSwapchainComposition composition,
            GpuPresentMode presentMode)
        {
            lock (sync)
            {
                Calls.Add(nameof(SetSwapchainParameters));
                ConfiguredPresentMode = presentMode;
                return true;
            }
        }

        public override GpuTextureFormat GetSwapchainTextureFormat(nint device, nint windowHandle)
        {
            lock (sync)
            {
                Calls.Add(nameof(GetSwapchainTextureFormat));
                return SwapchainFormat;
            }
        }

        public override GpuTextureFormat ResolveOffscreenColorFormat(nint device, GpuTextureFormat swapchainFormat)
        {
            lock (sync)
            {
                Calls.Add(nameof(ResolveOffscreenColorFormat));
                return OffscreenFormat;
            }
        }

        public override bool GetWindowPixelSize(nint windowHandle, out int width, out int height)
        {
            lock (sync)
            {
                width = PixelWidth;
                height = PixelHeight;
                return true;
            }
        }

        public override bool IsWindowMinimized(nint windowHandle)
        {
            lock (sync)
                return Minimized;
        }

        public override nint CreateTexture(nint device, in GpuTextureDescription description)
        {
            lock (sync)
            {
                Calls.Add(nameof(CreateTexture));
                var handle = NextHandle();
                activeTextures.Add(handle);
                CreatedTextures.Add(handle);
                return handle;
            }
        }

        public override void ReleaseTexture(nint device, nint texture)
        {
            lock (sync)
            {
                Calls.Add(nameof(ReleaseTexture));
                activeTextures.Remove(texture);
                ReleasedTextures.Add(texture);
            }
        }

        public override nint AcquireCommandBuffer(nint device)
        {
            lock (sync)
            {
                Calls.Add(nameof(AcquireCommandBuffer));
                var cmd = NextHandle();
                drawCommandBuffers.Add(cmd);
                return cmd;
            }
        }

        public override bool SubmitCommandBuffer(nint commandBuffer)
        {
            lock (sync)
            {
                Calls.Add(nameof(SubmitCommandBuffer));
                if (!drawCommandBuffers.Remove(commandBuffer))
                    PlainSubmitCountForPresent++;
                return true;
            }
        }

        public override nint SubmitCommandBufferAndAcquireFence(nint commandBuffer)
        {
            lock (sync)
            {
                Calls.Add(nameof(SubmitCommandBufferAndAcquireFence));
                drawCommandBuffers.Remove(commandBuffer);
                var fence = NextHandle();
                fences[fence] = AutoSignalFences;
                return fence;
            }
        }

        public override bool CancelCommandBuffer(nint commandBuffer)
        {
            lock (sync)
            {
                Calls.Add(nameof(CancelCommandBuffer));
                CancelCommandBufferCount++;
                drawCommandBuffers.Remove(commandBuffer);
                return true;
            }
        }

        public override void ClearColorTarget(nint commandBuffer, nint colorTargetTexture, Color4 clearColor)
        {
            lock (sync)
                Calls.Add(nameof(ClearColorTarget));
        }

        public override bool AcquireSwapchainTexture(
            nint commandBuffer,
            nint windowHandle,
            out nint swapchainTexture,
            out uint width,
            out uint height)
        {
            lock (sync)
            {
                Calls.Add(nameof(AcquireSwapchainTexture));
                drawCommandBuffers.Remove(commandBuffer);

                if (ReturnNullSwapchainTexture)
                {
                    swapchainTexture = nint.Zero;
                    width = 0;
                    height = 0;
                    return true;
                }

                swapchainTexture = NextHandle();
                width = (uint)Math.Max(0, PixelWidth);
                height = (uint)Math.Max(0, PixelHeight);
                return true;
            }
        }

        public override void BlitTexture(
            nint commandBuffer,
            nint sourceTexture,
            uint sourceWidth,
            uint sourceHeight,
            nint destinationTexture,
            uint destinationWidth,
            uint destinationHeight)
        {
            lock (sync)
            {
                Calls.Add(nameof(BlitTexture));
                if (!activeTextures.Contains(sourceTexture))
                    UseAfterFreeDetected = true;

                BlitCount++;
                LastBlitSourceTexture = sourceTexture;
                LastBlitSourceWidth = sourceWidth;
                LastBlitSourceHeight = sourceHeight;
            }

            OnBlitTexture?.Invoke();
        }

        public override bool QueryFence(nint device, nint fence)
        {
            lock (sync)
            {
                Calls.Add(nameof(QueryFence));
                return fences.TryGetValue(fence, out var signaled) && signaled;
            }
        }

        public override bool WaitForFences(nint device, ReadOnlySpan<nint> fencesToWait)
        {
            lock (sync)
            {
                Calls.Add(nameof(WaitForFences));
                foreach (var fence in fencesToWait)
                {
                    if (fences.ContainsKey(fence))
                        fences[fence] = true;
                }
                return true;
            }
        }

        public override void ReleaseFence(nint device, nint fence)
        {
            lock (sync)
            {
                Calls.Add(nameof(ReleaseFence));
                fences.Remove(fence);
            }
        }

        public override bool WaitForIdle(nint device)
        {
            lock (sync)
            {
                Calls.Add(nameof(WaitForIdle));
                signalAllFencesLocked();
                return true;
            }
        }

        public void SignalAllFences()
        {
            lock (sync)
                signalAllFencesLocked();
        }

        private void signalAllFencesLocked()
        {
            foreach (var key in fences.Keys.ToList())
                fences[key] = true;
        }
    }
}
