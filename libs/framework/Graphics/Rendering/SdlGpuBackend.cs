using kajarlabs.Framework.Numerics;
using SDL3;

namespace kajarlabs.Framework.Graphics.Rendering;

internal sealed class SdlGpuBackend : IGpuBackend
{
    public static readonly SdlGpuBackend INSTANCE = new();

    private SdlGpuBackend()
    {
    }

    public nint CreateDevice(GpuShaderFormats shaderFormats, bool debugMode, string? preferredDriver)
        => SDL.CreateGPUDevice(toSdlShaderFormats(shaderFormats), debugMode, preferredDriver);

    public void DestroyDevice(nint device)
    {
        if (device != nint.Zero)
            SDL.DestroyGPUDevice(device);
    }

    public string? GetDeviceDriver(nint device) => SDL.GetGPUDeviceDriver(device);

    public GpuShaderFormats GetSupportedShaderFormats(nint device)
        => fromSdlShaderFormats(SDL.GetGPUShaderFormats(device));

    public bool ClaimWindow(nint device, nint windowHandle)
        => SDL.ClaimWindowForGPUDevice(device, windowHandle);

    public void ReleaseWindow(nint device, nint windowHandle)
    {
        if (device != nint.Zero && windowHandle != nint.Zero)
            SDL.ReleaseWindowFromGPUDevice(device, windowHandle);
    }

    public bool WindowSupportsSwapchainComposition(nint device, nint windowHandle, GpuSwapchainComposition composition)
        => SDL.WindowSupportsGPUSwapchainComposition(device, windowHandle, toSdlSwapchainComposition(composition));

    public bool WindowSupportsPresentMode(nint device, nint windowHandle, GpuPresentMode presentMode)
        => SDL.WindowSupportsGPUPresentMode(device, windowHandle, toSdlPresentMode(presentMode));

    public bool SetSwapchainParameters(
        nint device,
        nint windowHandle,
        GpuSwapchainComposition composition,
        GpuPresentMode presentMode)
        => SDL.SetGPUSwapchainParameters(
            device,
            windowHandle,
            toSdlSwapchainComposition(composition),
            toSdlPresentMode(presentMode));

    public GpuTextureFormat GetSwapchainTextureFormat(nint device, nint windowHandle)
        => new((uint)SDL.GetGPUSwapchainTextureFormat(device, windowHandle));

    public GpuTextureFormat ResolveOffscreenColorFormat(nint device, GpuTextureFormat swapchainFormat)
    {
        var sdlFormat = (SDL.GPUTextureFormat)swapchainFormat.RawValue;
        const SDL.GPUTextureUsageFlags required_usage = SDL.GPUTextureUsageFlags.ColorTarget | SDL.GPUTextureUsageFlags.Sampler;

        if (sdlFormat != SDL.GPUTextureFormat.Invalid
            && SDL.GPUTextureSupportsFormat(device, sdlFormat, SDL.GPUTextureType.TextureType2D, required_usage))
        {
            return swapchainFormat;
        }

        return new((uint)SDL.GPUTextureFormat.R8G8B8A8Unorm);
    }

    public bool GetWindowPixelSize(nint windowHandle, out int width, out int height)
        => SDL.GetWindowSizeInPixels(windowHandle, out width, out height);

    public bool IsWindowMinimized(nint windowHandle)
        => (SDL.GetWindowFlags(windowHandle) & SDL.WindowFlags.Minimized) != 0;

    public nint CreateTexture(nint device, in GpuTextureDescription description)
    {
        var createInfo = new SDL.GPUTextureCreateInfo
        {
            Type = SDL.GPUTextureType.TextureType2D,
            Format = toSdlTextureFormat(description.Format),
            Usage = toSdlTextureUsage(description.Usage),
            Width = description.Width,
            Height = description.Height,
            LayerCountOrDepth = 1,
            NumLevels = 1,
            SampleCount = SDL.GPUSampleCount.SampleCount1,
        };

        return SDL.CreateGPUTexture(device, in createInfo);
    }

    public void ReleaseTexture(nint device, nint texture)
    {
        if (device != nint.Zero && texture != nint.Zero)
            SDL.ReleaseGPUTexture(device, texture);
    }

    public nint CreateShader(nint device, in GpuShaderCreateDescription description)
    {
        var createInfo = new SDL.GPUShaderCreateInfo
        {
            Stage = toSdlShaderStage(description.Stage),
            Format = toSdlShaderFormats(description.Format),
            NumSamplers = description.NumSamplers,
            NumStorageTextures = description.NumStorageTextures,
            NumStorageBuffers = description.NumStorageBuffers,
            NumUniformBuffers = description.NumUniformBuffers,
        };

        return SDL.CreateGPUShader(device, in createInfo, description.Bytecode.Span, description.Entrypoint);
    }

    public void ReleaseShader(nint device, nint shader)
    {
        if (device != nint.Zero && shader != nint.Zero)
            SDL.ReleaseGPUShader(device, shader);
    }

    public nint CreateGraphicsPipeline(nint device, in GpuGraphicsPipelineDescription description)
    {
        var bindingsSpan = description.VertexLayout.BufferBindings;
        var attributesSpan = description.VertexLayout.Attributes;

        var sdlBindings = bindingsSpan.Length <= 8
            ? stackalloc SDL.GPUVertexBufferDescription[bindingsSpan.Length]
            : new SDL.GPUVertexBufferDescription[bindingsSpan.Length];

        for (var i = 0; i < bindingsSpan.Length; i++)
        {
            var binding = bindingsSpan[i];
            sdlBindings[i] = new SDL.GPUVertexBufferDescription
            {
                Slot = binding.Slot,
                Pitch = binding.Pitch,
                InputRate = toSdlVertexInputRate(binding.InputRate),
                InstanceStepRate = binding.InstanceStepRate,
            };
        }

        var sdlAttributes = attributesSpan.Length <= 16
            ? stackalloc SDL.GPUVertexAttribute[attributesSpan.Length]
            : new SDL.GPUVertexAttribute[attributesSpan.Length];

        for (var i = 0; i < attributesSpan.Length; i++)
        {
            var attribute = attributesSpan[i];
            sdlAttributes[i] = new SDL.GPUVertexAttribute
            {
                Location = attribute.Location,
                BufferSlot = attribute.BufferSlot,
                Format = toSdlVertexElementFormat(attribute.Format),
                Offset = attribute.Offset,
            };
        }

        ReadOnlySpan<SDL.GPUColorTargetDescription> colorTargets =
        [
            new SDL.GPUColorTargetDescription
            {
                Format = toSdlTextureFormat(description.ColorTargetFormat),
                BlendState = toSdlBlendState(description.Blending),
            },
        ];

        var createInfo = new SDL.GPUGraphicsPipelineCreateInfo
        {
            VertexShader = description.VertexShader,
            FragmentShader = description.FragmentShader,
            PrimitiveType = toSdlPrimitiveType(description.Topology),
            RasterizerState = new SDL.GPURasterizerState
            {
                FillMode = SDL.GPUFillMode.Fill,
                CullMode = SDL.GPUCullMode.None,
                FrontFace = SDL.GPUFrontFace.CounterClockwise,
                EnableDepthBias = false,
                EnableDepthClip = false,
            },
            MultisampleState = new SDL.GPUMultisampleState
            {
                SampleCount = SDL.GPUSampleCount.SampleCount1,
                EnableAlphaToCoverage = false,
            },
            DepthStencilState = new SDL.GPUDepthStencilState
            {
                CompareOp = SDL.GPUCompareOp.Always,
                EnableDepthTest = false,
                EnableDepthWrite = false,
                EnableStencilTest = false,
            },
            TargetInfo = new SDL.GPUGraphicsPipelineTargetInfo
            {
                NumColorTargets = (uint)colorTargets.Length,
                HasDepthStencilTarget = false,
            },
        };

        return SDL.CreateGPUGraphicsPipeline(device, in createInfo, sdlBindings, sdlAttributes, colorTargets);
    }

    public void ReleaseGraphicsPipeline(nint device, nint pipeline)
    {
        if (device != nint.Zero && pipeline != nint.Zero)
            SDL.ReleaseGPUGraphicsPipeline(device, pipeline);
    }

    public nint AcquireCommandBuffer(nint device) => SDL.AcquireGPUCommandBuffer(device);

    public bool SubmitCommandBuffer(nint commandBuffer) => SDL.SubmitGPUCommandBuffer(commandBuffer);

    public nint SubmitCommandBufferAndAcquireFence(nint commandBuffer)
        => SDL.SubmitGPUCommandBufferAndAcquireFence(commandBuffer);

    public bool CancelCommandBuffer(nint commandBuffer) => SDL.CancelGPUCommandBuffer(commandBuffer);

    public void ClearColorTarget(nint commandBuffer, nint colorTargetTexture, Color4 clearColor)
    {
        var colorTargetInfo = new SDL.GPUColorTargetInfo
        {
            Texture = colorTargetTexture,
            ClearColor = new SDL.FColor(clearColor.R, clearColor.G, clearColor.B, clearColor.A),
            LoadOp = SDL.GPULoadOp.Clear,
            StoreOp = SDL.GPUStoreOp.Store,
        };

        ReadOnlySpan<SDL.GPUColorTargetInfo> targets = [colorTargetInfo];
        var renderPass = SDL.BeginGPURenderPass(commandBuffer, targets, 1, nint.Zero);

        if (renderPass != nint.Zero)
            SDL.EndGPURenderPass(renderPass);
    }

    public bool AcquireSwapchainTexture(
        nint commandBuffer,
        nint windowHandle,
        out nint swapchainTexture,
        out uint width,
        out uint height)
        => SDL.AcquireGPUSwapchainTexture(commandBuffer, windowHandle, out swapchainTexture, out width, out height);

    public void BlitTexture(
        nint commandBuffer,
        nint sourceTexture,
        uint sourceWidth,
        uint sourceHeight,
        nint destinationTexture,
        uint destinationWidth,
        uint destinationHeight)
    {
        var blitInfo = new SDL.GPUBlitInfo
        {
            Source = new SDL.GPUBlitRegion
            {
                Texture = sourceTexture,
                W = sourceWidth,
                H = sourceHeight,
            },
            Destination = new SDL.GPUBlitRegion
            {
                Texture = destinationTexture,
                W = destinationWidth,
                H = destinationHeight,
            },
            LoadOp = SDL.GPULoadOp.DontCare,
            FlipMode = SDL.FlipMode.None,
            Filter = SDL.GPUFilter.Linear,
        };

        SDL.BlitGPUTexture(commandBuffer, in blitInfo);
    }

    public bool QueryFence(nint device, nint fence) => SDL.QueryGPUFence(device, fence);

    public bool WaitForFences(nint device, ReadOnlySpan<nint> fences)
        => SDL.WaitForGPUFences(device, true, fences, (uint)fences.Length);

    public void ReleaseFence(nint device, nint fence)
    {
        if (device != nint.Zero && fence != nint.Zero)
            SDL.ReleaseGPUFence(device, fence);
    }

    public bool WaitForIdle(nint device) => SDL.WaitForGPUIdle(device);

    private static SDL.GPUShaderFormat toSdlShaderFormats(GpuShaderFormats formats)
    {
        SDL.GPUShaderFormat result = default;

        if ((formats & GpuShaderFormats.Private) != 0)
            result |= SDL.GPUShaderFormat.Private;
        if ((formats & GpuShaderFormats.SpirV) != 0)
            result |= SDL.GPUShaderFormat.SPIRV;
        if ((formats & GpuShaderFormats.Dxbc) != 0)
            result |= SDL.GPUShaderFormat.DXBC;
        if ((formats & GpuShaderFormats.Dxil) != 0)
            result |= SDL.GPUShaderFormat.DXIL;
        if ((formats & GpuShaderFormats.Msl) != 0)
            result |= SDL.GPUShaderFormat.MSL;
        if ((formats & GpuShaderFormats.MetalLib) != 0)
            result |= SDL.GPUShaderFormat.MetalLib;

        return result;
    }

    private static GpuShaderFormats fromSdlShaderFormats(SDL.GPUShaderFormat formats)
    {
        var result = GpuShaderFormats.None;

        if ((formats & SDL.GPUShaderFormat.Private) != 0)
            result |= GpuShaderFormats.Private;
        if ((formats & SDL.GPUShaderFormat.SPIRV) != 0)
            result |= GpuShaderFormats.SpirV;
        if ((formats & SDL.GPUShaderFormat.DXBC) != 0)
            result |= GpuShaderFormats.Dxbc;
        if ((formats & SDL.GPUShaderFormat.DXIL) != 0)
            result |= GpuShaderFormats.Dxil;
        if ((formats & SDL.GPUShaderFormat.MSL) != 0)
            result |= GpuShaderFormats.Msl;
        if ((formats & SDL.GPUShaderFormat.MetalLib) != 0)
            result |= GpuShaderFormats.MetalLib;

        return result;
    }

    private static SDL.GPUSwapchainComposition toSdlSwapchainComposition(GpuSwapchainComposition composition)
        => composition switch
        {
            GpuSwapchainComposition.Sdr => SDL.GPUSwapchainComposition.SDR,
            GpuSwapchainComposition.SdrLinear => SDL.GPUSwapchainComposition.SDRLinear,
            GpuSwapchainComposition.HdrExtendedLinear => SDL.GPUSwapchainComposition.HDRExtendedLinear,
            GpuSwapchainComposition.Hdr10St2084 => SDL.GPUSwapchainComposition.HDR10ST2084,
            _ => throw new ArgumentOutOfRangeException(nameof(composition), composition, null),
        };

    private static SDL.GPUPresentMode toSdlPresentMode(GpuPresentMode presentMode)
        => presentMode switch
        {
            GpuPresentMode.VSync => SDL.GPUPresentMode.VSync,
            GpuPresentMode.Immediate => SDL.GPUPresentMode.Immediate,
            GpuPresentMode.Mailbox => SDL.GPUPresentMode.Mailbox,
            _ => throw new ArgumentOutOfRangeException(nameof(presentMode), presentMode, null),
        };

    private static SDL.GPUTextureUsageFlags toSdlTextureUsage(GpuTextureUsage usage)
    {
        SDL.GPUTextureUsageFlags result = default;

        if ((usage & GpuTextureUsage.Sampler) != 0)
            result |= SDL.GPUTextureUsageFlags.Sampler;
        if ((usage & GpuTextureUsage.ColorTarget) != 0)
            result |= SDL.GPUTextureUsageFlags.ColorTarget;
        if ((usage & GpuTextureUsage.DepthStencilTarget) != 0)
            result |= SDL.GPUTextureUsageFlags.DepthStencilTarget;

        return result;
    }

    private static SDL.GPUTextureFormat toSdlTextureFormat(GpuTextureFormat format)
        => (SDL.GPUTextureFormat)format.RawValue;

    private static SDL.GPUShaderStage toSdlShaderStage(GpuShaderStage stage)
        => stage switch
        {
            GpuShaderStage.Vertex => SDL.GPUShaderStage.Vertex,
            GpuShaderStage.Fragment => SDL.GPUShaderStage.Fragment,
            _ => throw new ArgumentOutOfRangeException(nameof(stage), stage, null),
        };

    private static SDL.GPUVertexInputRate toSdlVertexInputRate(GpuVertexInputRate inputRate)
        => inputRate switch
        {
            GpuVertexInputRate.Vertex => SDL.GPUVertexInputRate.Vertex,
            GpuVertexInputRate.Instance => SDL.GPUVertexInputRate.Instance,
            _ => throw new ArgumentOutOfRangeException(nameof(inputRate), inputRate, null),
        };

    private static SDL.GPUVertexElementFormat toSdlVertexElementFormat(GpuVertexElementFormat format)
        => format switch
        {
            GpuVertexElementFormat.Float => SDL.GPUVertexElementFormat.Float,
            GpuVertexElementFormat.Float2 => SDL.GPUVertexElementFormat.Float2,
            GpuVertexElementFormat.Float3 => SDL.GPUVertexElementFormat.Float3,
            GpuVertexElementFormat.Float4 => SDL.GPUVertexElementFormat.Float4,
            _ => throw new ArgumentOutOfRangeException(nameof(format), format, null),
        };

    private static SDL.GPUPrimitiveType toSdlPrimitiveType(GpuPrimitiveTopology topology)
        => topology switch
        {
            GpuPrimitiveTopology.TriangleList => SDL.GPUPrimitiveType.TriangleList,
            GpuPrimitiveTopology.TriangleStrip => SDL.GPUPrimitiveType.TriangleStrip,
            GpuPrimitiveTopology.LineList => SDL.GPUPrimitiveType.LineList,
            GpuPrimitiveTopology.LineStrip => SDL.GPUPrimitiveType.LineStrip,
            GpuPrimitiveTopology.PointList => SDL.GPUPrimitiveType.PointList,
            _ => throw new ArgumentOutOfRangeException(nameof(topology), topology, null),
        };

    private static SDL.GPUColorTargetBlendState toSdlBlendState(BlendingParameters blending)
        => new()
        {
            EnableBlend = blending.IsEnabled,
            SrcColorBlendFactor = toSdlBlendFactor(blending.SourceColorFactor),
            DstColorBlendFactor = toSdlBlendFactor(blending.DestinationColorFactor),
            ColorBlendOp = toSdlBlendOp(blending.ColorOperation),
            SrcAlphaBlendFactor = toSdlBlendFactor(blending.SourceAlphaFactor),
            DstAlphaBlendFactor = toSdlBlendFactor(blending.DestinationAlphaFactor),
            AlphaBlendOp = toSdlBlendOp(blending.AlphaOperation),
            EnableColorWriteMask = false,
        };

    private static SDL.GPUBlendFactor toSdlBlendFactor(BlendFactor factor)
        => factor switch
        {
            BlendFactor.Zero => SDL.GPUBlendFactor.Zero,
            BlendFactor.One => SDL.GPUBlendFactor.One,
            BlendFactor.SourceColor => SDL.GPUBlendFactor.SrcColor,
            BlendFactor.OneMinusSourceColor => SDL.GPUBlendFactor.OneMinusSrcColor,
            BlendFactor.DestinationColor => SDL.GPUBlendFactor.DstColor,
            BlendFactor.OneMinusDestinationColor => SDL.GPUBlendFactor.OneMinusDstColor,
            BlendFactor.SourceAlpha => SDL.GPUBlendFactor.SrcAlpha,
            BlendFactor.OneMinusSourceAlpha => SDL.GPUBlendFactor.OneMinusSrcAlpha,
            BlendFactor.DestinationAlpha => SDL.GPUBlendFactor.DstAlpha,
            BlendFactor.OneMinusDestinationAlpha => SDL.GPUBlendFactor.OneMinusDstAlpha,
            BlendFactor.SourceAlphaSaturate => SDL.GPUBlendFactor.SrcAlphaSaturate,
            _ => throw new ArgumentOutOfRangeException(nameof(factor), factor, null),
        };

    private static SDL.GPUBlendOp toSdlBlendOp(BlendOperation operation)
        => operation switch
        {
            BlendOperation.Add => SDL.GPUBlendOp.Add,
            BlendOperation.Subtract => SDL.GPUBlendOp.Subtract,
            BlendOperation.ReverseSubtract => SDL.GPUBlendOp.ReverseSubtract,
            BlendOperation.Min => SDL.GPUBlendOp.Min,
            BlendOperation.Max => SDL.GPUBlendOp.Max,
            _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, null),
        };
}
