// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// Presents an ImageSharp-owned texture through a WebGPU surface texture.
/// </summary>
internal sealed unsafe class WebGPUPresentationRenderer : IDisposable
{
    private const string PipelineKey = "surface/presentation";

    private readonly WebGPU api;
    private readonly WebGPUDeviceHandle deviceHandle;
    private readonly WebGPUQueueHandle queueHandle;
    private readonly WebGPUTargetDescriptor targetDescriptor;
    private readonly WebGPUTextureHandle sourceTextureHandle;
    private readonly uint width;
    private readonly uint height;
    private readonly bool copyToSurface;
    private readonly RenderPipeline* pipeline;
    private BindGroup* bindGroup;
    private bool isDisposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="WebGPUPresentationRenderer"/> class for a persistent source target.
    /// </summary>
    /// <param name="api">The WebGPU API facade.</param>
    /// <param name="deviceContext">The device context that owns the source and destination textures.</param>
    /// <param name="source">The persistent ImageSharp-owned target presented by this renderer.</param>
    /// <param name="copyToSurface">Whether the surface supports receiving a native texture copy.</param>
    public WebGPUPresentationRenderer(WebGPU api, WebGPUDeviceContext deviceContext, WebGPURenderTarget source, bool copyToSurface)
    {
        this.api = api;
        this.deviceHandle = deviceContext.DeviceHandle;
        this.queueHandle = deviceContext.QueueHandle;
        this.targetDescriptor = source.Surface.TargetDescriptor;
        this.sourceTextureHandle = source.TextureHandle;
        this.width = (uint)source.Width;
        this.height = (uint)source.Height;
        this.copyToSurface = copyToSurface;

        // Copy-capable surfaces need no shader resources. The source and destination have the
        // same negotiated format and extent, so WebGPU's native texture copy is the shortest path.
        if (copyToSurface)
        {
            return;
        }

        WebGPURuntime.DeviceSharedState deviceState = WebGPURuntime.GetOrCreateDeviceState(api, this.deviceHandle);
        TextureFormat textureFormat = WebGPUTextureFormatMapper.ToNative(this.targetDescriptor.Format);

        if (!deviceState.TryGetOrCreateCompositePipeline(
                PipelineKey,
                PresentationShader.ShaderCode,
                PresentationShader.TryCreateBindGroupLayout,
                textureFormat,
                CompositePipelineBlendMode.None,
                out BindGroupLayout* bindGroupLayout,
                out RenderPipeline* createdPipeline,
                out string? error))
        {
            throw new InvalidOperationException(error);
        }

        this.pipeline = createdPipeline;

        using WebGPUHandle.HandleReference deviceReference = this.deviceHandle.AcquireReference();
        using WebGPUHandle.HandleReference sourceTextureViewReference = source.TextureViewHandle.AcquireReference();

        BindGroupEntry entry = new()
        {
            binding = 0,
            textureView = (TextureView*)sourceTextureViewReference.Handle
        };

        BindGroupDescriptor bindGroupDescriptor = new()
        {
            layout = bindGroupLayout,
            entryCount = 1,
            entries = &entry
        };

        this.bindGroup = api.DeviceCreateBindGroup((Device*)deviceReference.Handle, in bindGroupDescriptor);
        if (this.bindGroup is null)
        {
            throw new InvalidOperationException("Failed to create the WebGPU presentation bind group.");
        }
    }

    /// <summary>
    /// Transfers the bound source texture into a presentable destination texture.
    /// </summary>
    /// <param name="destinationTextureHandle">The acquired surface texture that receives a native copy when supported.</param>
    /// <param name="destinationTextureViewHandle">The acquired surface texture view that receives the frame.</param>
    public void Present(WebGPUTextureHandle destinationTextureHandle, WebGPUTextureViewHandle destinationTextureViewHandle)
    {
        ObjectDisposedException.ThrowIf(this.isDisposed, this);

        if (this.copyToSurface)
        {
            this.CopyToSurface(destinationTextureHandle);
            return;
        }

        CommandEncoder* commandEncoder = null;
        RenderPassEncoder* renderPass = null;
        CommandBuffer* commandBuffer = null;

        try
        {
            using WebGPUHandle.HandleReference deviceReference = this.deviceHandle.AcquireReference();
            using WebGPUHandle.HandleReference queueReference = this.queueHandle.AcquireReference();
            using WebGPUHandle.HandleReference destinationTextureViewReference = destinationTextureViewHandle.AcquireReference();

            CommandEncoderDescriptor commandEncoderDescriptor = default;
            commandEncoder = this.api.DeviceCreateCommandEncoder((Device*)deviceReference.Handle, in commandEncoderDescriptor);
            if (commandEncoder is null)
            {
                throw new InvalidOperationException("Failed to create the WebGPU presentation command encoder.");
            }

            RenderPassColorAttachment colorAttachment = new()
            {
                view = (TextureView*)destinationTextureViewReference.Handle,
                depthSlice = uint.MaxValue,
                loadOp = LoadOp.Clear,
                storeOp = StoreOp.Store,

                // The fullscreen triangle overwrites every texel. This format-correct clear still
                // preserves logical transparency if clipping rules ever leave an edge uncovered.
                clearValue = this.targetDescriptor.NumericEncoding == WebGPUTargetNumericEncoding.SignedUnit
                    ? new Native.WGPUColor { r = -1D, g = -1D, b = -1D, a = -1D }
                    : default
            };

            RenderPassDescriptor renderPassDescriptor = new()
            {
                colorAttachmentCount = 1,
                colorAttachments = &colorAttachment
            };

            renderPass = this.api.CommandEncoderBeginRenderPass(commandEncoder, in renderPassDescriptor);
            if (renderPass is null)
            {
                throw new InvalidOperationException("Failed to begin the WebGPU presentation render pass.");
            }

            this.api.RenderPassEncoderSetPipeline(renderPass, this.pipeline);
            this.api.RenderPassEncoderSetBindGroup(renderPass, 0, this.bindGroup);
            this.api.RenderPassEncoderDraw(renderPass, 3, 1, 0, 0);
            this.api.RenderPassEncoderEnd(renderPass);
            this.api.RenderPassEncoderRelease(renderPass);
            renderPass = null;

            CommandBufferDescriptor commandBufferDescriptor = default;
            commandBuffer = this.api.CommandEncoderFinish(commandEncoder, in commandBufferDescriptor);
            if (commandBuffer is null)
            {
                throw new InvalidOperationException("Failed to finish the WebGPU presentation command buffer.");
            }

            this.api.QueueSubmit((Queue*)queueReference.Handle, 1, ref commandBuffer);
        }
        finally
        {
            if (renderPass is not null)
            {
                this.api.RenderPassEncoderEnd(renderPass);
                this.api.RenderPassEncoderRelease(renderPass);
            }

            if (commandBuffer is not null)
            {
                this.api.CommandBufferRelease(commandBuffer);
            }

            if (commandEncoder is not null)
            {
                this.api.CommandEncoderRelease(commandEncoder);
            }
        }
    }

    /// <summary>
    /// Copies the persistent source texture into a copy-capable surface texture.
    /// </summary>
    /// <param name="destinationTextureHandle">The acquired surface texture.</param>
    private void CopyToSurface(WebGPUTextureHandle destinationTextureHandle)
    {
        CommandEncoder* commandEncoder = null;
        CommandBuffer* commandBuffer = null;

        try
        {
            using WebGPUHandle.HandleReference deviceReference = this.deviceHandle.AcquireReference();
            using WebGPUHandle.HandleReference queueReference = this.queueHandle.AcquireReference();
            using WebGPUHandle.HandleReference sourceTextureReference = this.sourceTextureHandle.AcquireReference();
            using WebGPUHandle.HandleReference destinationTextureReference = destinationTextureHandle.AcquireReference();

            CommandEncoderDescriptor commandEncoderDescriptor = default;
            commandEncoder = this.api.DeviceCreateCommandEncoder((Device*)deviceReference.Handle, in commandEncoderDescriptor);
            if (commandEncoder is null)
            {
                throw new InvalidOperationException("Failed to create the WebGPU presentation copy command encoder.");
            }

            ImageCopyTexture source = new()
            {
                texture = (Texture*)sourceTextureReference.Handle,
                mipLevel = 0,
                origin = default,
                aspect = TextureAspect.All
            };

            ImageCopyTexture destination = new()
            {
                texture = (Texture*)destinationTextureReference.Handle,
                mipLevel = 0,
                origin = default,
                aspect = TextureAspect.All
            };

            Extent3D copySize = new(this.width, this.height, 1);
            this.api.CommandEncoderCopyTextureToTexture(commandEncoder, in source, in destination, in copySize);

            CommandBufferDescriptor commandBufferDescriptor = default;
            commandBuffer = this.api.CommandEncoderFinish(commandEncoder, in commandBufferDescriptor);
            if (commandBuffer is null)
            {
                throw new InvalidOperationException("Failed to finish the WebGPU presentation copy command buffer.");
            }

            // Canvas disposal submits first. Queue ordering therefore completes the render before
            // this copy without a CPU wait or an additional synchronization primitive.
            this.api.QueueSubmit((Queue*)queueReference.Handle, 1, ref commandBuffer);
        }
        finally
        {
            if (commandBuffer is not null)
            {
                this.api.CommandBufferRelease(commandBuffer);
            }

            if (commandEncoder is not null)
            {
                this.api.CommandEncoderRelease(commandEncoder);
            }
        }
    }

    /// <summary>
    /// Releases the source-texture bind group owned by this renderer.
    /// </summary>
    public void Dispose()
    {
        if (this.isDisposed)
        {
            return;
        }

        if (this.bindGroup is not null)
        {
            this.api.BindGroupRelease(this.bindGroup);
            this.bindGroup = null;
        }

        this.isDisposed = true;
    }
}
