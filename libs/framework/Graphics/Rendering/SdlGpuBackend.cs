using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
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

    public GpuTextureFormat Rgba8TextureFormat => new((uint)SDL.GPUTextureFormat.R8G8B8A8Unorm);

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

    public nint CreateSampler(nint device, TextureSamplerParameters parameters)
    {
        var filter = toSdlFilter(parameters.Filtering);
        var mipmapMode = toSdlMipmapMode(parameters.Filtering);
        var addressMode = toSdlAddressMode(parameters.WrapMode);

        var createInfo = new SDL.GPUSamplerCreateInfo
        {
            MinFilter = filter,
            MagFilter = filter,
            MipmapMode = mipmapMode,
            AddressModeU = addressMode,
            AddressModeV = addressMode,
            AddressModeW = addressMode,
            MinLod = 0f,
            MaxLod = 1000f,
            EnableAnisotropy = false,
            EnableCompare = false,
        };

        return SDL.CreateGPUSampler(device, in createInfo);
    }

    public void ReleaseSampler(nint device, nint sampler)
    {
        if (device != nint.Zero && sampler != nint.Zero)
            SDL.ReleaseGPUSampler(device, sampler);
    }

    public nint CreateTransferBuffer(nint device, GpuTransferBufferUsage usage, uint sizeInBytes)
    {
        var createInfo = new SDL.GPUTransferBufferCreateInfo
        {
            Usage = toSdlTransferBufferUsage(usage),
            Size = sizeInBytes,
        };

        return SDL.CreateGPUTransferBuffer(device, in createInfo);
    }

    public void ReleaseTransferBuffer(nint device, nint transferBuffer)
    {
        if (device != nint.Zero && transferBuffer != nint.Zero)
            SDL.ReleaseGPUTransferBuffer(device, transferBuffer);
    }

    public nint MapTransferBuffer(nint device, nint transferBuffer, bool cycle)
        => SDL.MapGPUTransferBuffer(device, transferBuffer, cycle);

    public void CopyToMappedBuffer(
        nint mappedDestination,
        int byteOffset,
        byte[] source,
        int sourceOffset,
        int byteCount)
        => Marshal.Copy(source, sourceOffset, mappedDestination + byteOffset, byteCount);

    public void UnmapTransferBuffer(nint device, nint transferBuffer)
    {
        if (device != nint.Zero && transferBuffer != nint.Zero)
            SDL.UnmapGPUTransferBuffer(device, transferBuffer);
    }

    public nint BeginCopyPass(nint commandBuffer) => SDL.BeginGPUCopyPass(commandBuffer);

    public void UploadToTexture(
        nint copyPass,
        in GpuTextureUploadSource source,
        in GpuTextureRegion destination,
        bool cycle)
    {
        var transferInfo = new SDL.GPUTextureTransferInfo
        {
            TransferBuffer = source.TransferBuffer,
            Offset = source.Offset,
            PixelsPerRow = source.PixelsPerRow,
            RowsPerLayer = source.RowsPerLayer,
        };

        var region = new SDL.GPUTextureRegion
        {
            Texture = destination.Texture,
            MipLevel = 0,
            Layer = 0,
            X = destination.X,
            Y = destination.Y,
            Z = 0,
            W = destination.Width,
            H = destination.Height,
            D = 1,
        };

        SDL.UploadToGPUTexture(copyPass, in transferInfo, in region, cycle);
    }

    public void EndCopyPass(nint copyPass)
    {
        if (copyPass != nint.Zero)
            SDL.EndGPUCopyPass(copyPass);
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

    public nint CreateBuffer(nint device, GpuBufferUsage usage, uint sizeInBytes)
    {
        var createInfo = new SDL.GPUBufferCreateInfo
        {
            Usage = toSdlBufferUsage(usage),
            Size = sizeInBytes,
        };

        return SDL.CreateGPUBuffer(device, in createInfo);
    }

    public void ReleaseBuffer(nint device, nint buffer)
    {
        if (device != nint.Zero && buffer != nint.Zero)
            SDL.ReleaseGPUBuffer(device, buffer);
    }

    public void UploadToBuffer(
        nint copyPass,
        nint transferBuffer,
        uint transferOffset,
        nint destinationBuffer,
        uint destinationOffset,
        uint sizeInBytes,
        bool cycle)
    {
        var location = new SDL.GPUTransferBufferLocation
        {
            TransferBuffer = transferBuffer,
            Offset = transferOffset,
        };

        var region = new SDL.GPUBufferRegion
        {
            Buffer = destinationBuffer,
            Offset = destinationOffset,
            Size = sizeInBytes,
        };

        SDL.UploadToGPUBuffer(copyPass, in location, in region, cycle);
    }

    public nint BeginRenderPass(nint commandBuffer, nint colorTargetTexture, Color4? clearColor)
    {
        var colorTargetInfo = new SDL.GPUColorTargetInfo
        {
            Texture = colorTargetTexture,
            ClearColor = clearColor is { } clear
                ? new SDL.FColor(clear.R, clear.G, clear.B, clear.A)
                : default,
            LoadOp = clearColor.HasValue ? SDL.GPULoadOp.Clear : SDL.GPULoadOp.Load,
            StoreOp = SDL.GPUStoreOp.Store,
        };

        ReadOnlySpan<SDL.GPUColorTargetInfo> targets = [colorTargetInfo];
        return SDL.BeginGPURenderPass(commandBuffer, targets, 1, nint.Zero);
    }

    public void EndRenderPass(nint renderPass)
    {
        if (renderPass != nint.Zero)
            SDL.EndGPURenderPass(renderPass);
    }

    public void BindGraphicsPipeline(nint renderPass, nint pipeline)
        => SDL.BindGPUGraphicsPipeline(renderPass, pipeline);

    public void SetViewport(nint renderPass, Viewport viewport)
    {
        var sdlViewport = new SDL.GPUViewport
        {
            X = viewport.X,
            Y = viewport.Y,
            W = viewport.Width,
            H = viewport.Height,
            MinDepth = viewport.MinDepth,
            MaxDepth = viewport.MaxDepth,
        };

        SDL.SetGPUViewport(renderPass, in sdlViewport);
    }

    public void SetScissor(nint renderPass, RectangleI scissor)
    {
        var rect = new SDL.Rect
        {
            X = scissor.X,
            Y = scissor.Y,
            W = scissor.Width,
            H = scissor.Height,
        };

        SDL.SetGPUScissor(renderPass, in rect);
    }

    public void BindVertexBuffer(nint renderPass, uint firstSlot, nint vertexBuffer, uint offset)
    {
        ReadOnlySpan<SDL.GPUBufferBinding> bindings =
        [
            new SDL.GPUBufferBinding
            {
                Buffer = vertexBuffer,
                Offset = offset,
            },
        ];

        SDL.BindGPUVertexBuffers(renderPass, firstSlot, bindings, 1);
    }

    public void BindIndexBuffer(nint renderPass, nint indexBuffer, uint offset, GpuIndexElementSize indexElementSize)
    {
        var binding = new SDL.GPUBufferBinding
        {
            Buffer = indexBuffer,
            Offset = offset,
        };

        SDL.BindGPUIndexBuffer(renderPass, in binding, toSdlIndexElementSize(indexElementSize));
    }

    public void BindFragmentSampler(nint renderPass, uint firstSlot, nint texture, nint sampler)
    {
        ReadOnlySpan<SDL.GPUTextureSamplerBinding> bindings =
        [
            new SDL.GPUTextureSamplerBinding
            {
                Texture = texture,
                Sampler = sampler,
            },
        ];

        SDL.BindGPUFragmentSamplers(renderPass, firstSlot, bindings, 1);
    }

    public void PushVertexUniformData(nint commandBuffer, uint slotIndex, in Matrix4x4 matrix)
    {
        var bytes = MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(ref Unsafe.AsRef(in matrix), 1));
        SDL.PushGPUVertexUniformData(commandBuffer, slotIndex, bytes, (uint)bytes.Length);
    }

    public void PushFragmentUniformData(nint commandBuffer, uint slotIndex, in GpuMsdfFragmentUniform uniform)
    {
        var bytes = MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(ref Unsafe.AsRef(in uniform), 1));
        SDL.PushGPUFragmentUniformData(commandBuffer, slotIndex, bytes, (uint)bytes.Length);
    }

    public void DrawIndexedPrimitives(
        nint renderPass,
        uint numIndices,
        uint numInstances,
        uint firstIndex,
        int vertexOffset,
        uint firstInstance)
        => SDL.DrawGPUIndexedPrimitives(renderPass, numIndices, numInstances, firstIndex, vertexOffset, firstInstance);

    public nint AcquireCommandBuffer(nint device) => SDL.AcquireGPUCommandBuffer(device);

    public bool SubmitCommandBuffer(nint commandBuffer) => SDL.SubmitGPUCommandBuffer(commandBuffer);

    public nint SubmitCommandBufferAndAcquireFence(nint commandBuffer)
        => SDL.SubmitGPUCommandBufferAndAcquireFence(commandBuffer);

    public bool CancelCommandBuffer(nint commandBuffer) => SDL.CancelGPUCommandBuffer(commandBuffer);

    public void ClearColorTarget(nint commandBuffer, nint colorTargetTexture, Color4 clearColor)
        => EndRenderPass(BeginRenderPass(commandBuffer, colorTargetTexture, clearColor));

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

    private static SDL.GPUFilter toSdlFilter(TextureFilteringMode filtering)
        => filtering switch
        {
            TextureFilteringMode.Linear => SDL.GPUFilter.Linear,
            TextureFilteringMode.Nearest => SDL.GPUFilter.Nearest,
            _ => throw new ArgumentOutOfRangeException(nameof(filtering), filtering, null),
        };

    private static SDL.GPUSamplerMipmapMode toSdlMipmapMode(TextureFilteringMode filtering)
        => filtering switch
        {
            TextureFilteringMode.Linear => SDL.GPUSamplerMipmapMode.Linear,
            TextureFilteringMode.Nearest => SDL.GPUSamplerMipmapMode.Nearest,
            _ => throw new ArgumentOutOfRangeException(nameof(filtering), filtering, null),
        };

    private static SDL.GPUSamplerAddressMode toSdlAddressMode(TextureWrapMode wrapMode)
        => wrapMode switch
        {
            TextureWrapMode.ClampToEdge => SDL.GPUSamplerAddressMode.ClampToEdge,
            TextureWrapMode.Repeat => SDL.GPUSamplerAddressMode.Repeat,
            _ => throw new ArgumentOutOfRangeException(nameof(wrapMode), wrapMode, null),
        };

    private static SDL.GPUTransferBufferUsage toSdlTransferBufferUsage(GpuTransferBufferUsage usage)
        => usage switch
        {
            GpuTransferBufferUsage.Upload => SDL.GPUTransferBufferUsage.Upload,
            GpuTransferBufferUsage.Download => SDL.GPUTransferBufferUsage.Download,
            _ => throw new ArgumentOutOfRangeException(nameof(usage), usage, null),
        };

    private static SDL.GPUBufferUsageFlags toSdlBufferUsage(GpuBufferUsage usage)
    {
        SDL.GPUBufferUsageFlags result = default;

        if ((usage & GpuBufferUsage.Vertex) != 0)
            result |= SDL.GPUBufferUsageFlags.Vertex;
        if ((usage & GpuBufferUsage.Index) != 0)
            result |= SDL.GPUBufferUsageFlags.Index;

        return result;
    }

    private static SDL.GPUIndexElementSize toSdlIndexElementSize(GpuIndexElementSize size)
        => size switch
        {
            GpuIndexElementSize.SixteenBit => SDL.GPUIndexElementSize.IndexElementSize16Bit,
            GpuIndexElementSize.ThirtyTwoBit => SDL.GPUIndexElementSize.IndexElementSize32Bit,
            _ => throw new ArgumentOutOfRangeException(nameof(size), size, null),
        };
}
