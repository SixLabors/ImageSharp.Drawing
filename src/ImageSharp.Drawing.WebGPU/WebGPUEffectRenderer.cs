// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Processing.Backends.Native;

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// Executes retained shader effects using flush-owned working textures and uniform storage.
/// </summary>
internal sealed unsafe class WebGPUEffectRenderer
{
    private const ulong UserUniformOffset = 256;

    private readonly WebGPUFlushContext flushContext;
    private readonly WGPUTextureViewImpl* scratchAView;
    private readonly WGPUTextureViewImpl* scratchBView;
    private readonly WGPUBufferImpl* uniformBuffer;
    private readonly ulong passUniformStride;

    /// <summary>
    /// Initializes a new instance of the <see cref="WebGPUEffectRenderer"/> class.
    /// </summary>
    /// <param name="flushContext">The flush that owns all invocation resources.</param>
    /// <param name="maximumWidth">The largest effect input width in the retained scene.</param>
    /// <param name="maximumHeight">The largest effect input height in the retained scene.</param>
    /// <param name="maximumUniformByteLength">The largest user uniform binding in the retained scene.</param>
    /// <param name="maximumPassCount">The largest pass count of any single effect invocation in the retained scene.</param>
    public WebGPUEffectRenderer(
        WebGPUFlushContext flushContext,
        int maximumWidth,
        int maximumHeight,
        int maximumUniformByteLength,
        int maximumPassCount)
    {
        this.flushContext = flushContext;

        _ = this.CreateWorkingTexture(maximumWidth, maximumHeight, out this.scratchAView);
        _ = this.CreateWorkingTexture(maximumWidth, maximumHeight, out this.scratchBView);

        // Every pass owns a disjoint 256-byte-aligned region: [framework | user uniforms]. Distinct
        // offsets mean queued uniform writes for a later pass can never replace bytes an earlier
        // recorded pass still references, so one invocation needs no intermediate queue submission.
        // 256 satisfies WebGPU's maximum permitted minUniformBufferOffsetAlignment.
        this.passUniformStride = UserUniformOffset + AlignUp((ulong)Math.Max(16, maximumUniformByteLength), 256);

        // The buffer comes from the device pool: its content is rewritten by every invocation, so
        // reuse across flushes is safe and skips a per-flush native buffer creation.
        nuint uniformByteLength = checked((nuint)(this.passUniformStride * (ulong)Math.Max(1, maximumPassCount)));
        this.uniformBuffer = flushContext.DeviceState.RentEffectUniformBuffer(uniformByteLength);
        if (this.uniformBuffer is null)
        {
            throw new InvalidOperationException("Failed to create the WebGPU layer-effect uniform buffer.");
        }

        flushContext.TrackPooledUniformBuffer(this.uniformBuffer, uniformByteLength);
    }

    /// <summary>
    /// Executes one retained shader effect and returns the working texture containing its result.
    /// </summary>
    /// <param name="effect">The retained effect, source rectangle, and write-back metadata.</param>
    /// <param name="sourceTarget">The latest logical target sampled by the effect.</param>
    /// <returns>The working texture view containing associated <c>Rgba16Float</c> pixels.</returns>
    public WGPUTextureViewImpl* Render(WebGPUShaderEffectSceneItem effect, WebGPUSceneTarget sourceTarget)
    {
        // The ordered executor only ever supplies sampleable sources: the root target is replaced
        // by its sampleable copy before execution begins, and every scratch or layer target is a
        // composition texture created with TextureBinding usage. Copying here again would add one
        // full-target texture and copy per effect invocation for presentation surfaces.
        WebGPUSceneTarget sampleableSource = sourceTarget;

        Rectangle requestedRead = effect.InputRect;
        requestedRead.Offset(-effect.ReadOffset.X, -effect.ReadOffset.Y);
        Rectangle clippedRead = Rectangle.Intersect(sampleableSource.Bounds, requestedRead);
        Point sourceOrigin = new(
            requestedRead.X + sampleableSource.TextureOffset.X,
            requestedRead.Y + sampleableSource.TextureOffset.Y);
        Point validMinimum = new(
            clippedRead.X - requestedRead.X,
            clippedRead.Y - requestedRead.Y);
        Point validMaximum = new(
            validMinimum.X + clippedRead.Width,
            validMinimum.Y + clippedRead.Height);

        WebGPUShaderFrameworkUniforms framework = new(
            sourceOrigin,
            validMinimum,
            validMaximum,
            effect.InputRect.Size);

        WGPUTextureViewImpl* sourceView = sampleableSource.TextureView;
        WebGPUTargetDescriptor sourceDescriptor = this.flushContext.TargetDescriptor;
        ReadOnlySpan<WebGPUShaderPass> shaderPasses = effect.Effect.GetShaderPasses();

        for (int i = 0; i < shaderPasses.Length; i++)
        {
            WGPUTextureViewImpl* outputView = sourceView == this.scratchAView ? this.scratchBView : this.scratchAView;
            this.RenderShaderPass(shaderPasses[i], i, sourceView, sourceDescriptor, in framework, outputView, effect.InputRect.Size);

            sourceView = outputView;
            sourceDescriptor = WebGPUShaderEffectWorkingTexture.Descriptor;

            // Every pass after the first reads a complete working image whose local origin is zero.
            Point inputMaximum = new(effect.InputRect.Width, effect.InputRect.Height);
            framework = new WebGPUShaderFrameworkUniforms(Point.Empty, Point.Empty, inputMaximum, effect.InputRect.Size);
        }

        return sourceView;
    }

    /// <summary>
    /// Encodes one layer-effect fragment-shader pass.
    /// </summary>
    private void RenderShaderPass(
        WebGPUShaderPass shaderPass,
        int passIndex,
        WGPUTextureViewImpl* sourceView,
        WebGPUTargetDescriptor sourceDescriptor,
        in WebGPUShaderFrameworkUniforms framework,
        WGPUTextureViewImpl* outputView,
        Size outputSize)
    {
        WebGPUShaderProgram program = shaderPass.Program;
        WebGPUShaderModuleSource moduleSource = program.GetModuleSource(
            sourceDescriptor,
            shaderPass.XBorderMode,
            shaderPass.YBorderMode);

        // The render-pipeline target must exactly match the working-texture attachment. Fragment
        // arithmetic remains f32; the RGBA16Float attachment performs the documented binary16 store
        // between passes. Keep this native format synchronized with WebGPUShaderEffectWorkingTexture.
        if (!this.flushContext.DeviceState.TryGetOrCreateEffectPipeline(
                moduleSource,
                program.UniformLayout.ByteLength,
                WGPUTextureFormat.RGBA16Float,
                out WGPUBindGroupLayoutImpl* bindGroupLayout,
                out WGPURenderPipelineImpl* pipeline,
                out string? error))
        {
            throw new InvalidOperationException(error);
        }

        // Each pass writes into its own disjoint region, so these queued writes cannot disturb
        // the bytes referenced by an earlier pass recorded in the still-pending flush encoder.
        ulong passUniformOffset = checked(this.passUniformStride * (ulong)passIndex);

        using WebGPUHandle.HandleReference queueReference = this.flushContext.QueueHandle.AcquireReference();
        WGPUQueueImpl* queue = (WGPUQueueImpl*)queueReference.Handle;
        WebGPUShaderFrameworkUniforms frameworkValue = framework;
        this.flushContext.Api.QueueWriteBuffer(
            queue,
            this.uniformBuffer,
            passUniformOffset,
            &frameworkValue,
            (nuint)WebGPUShaderFrameworkUniforms.ByteLength);

        ReadOnlySpan<byte> uniformData = shaderPass.Uniforms.Data;
        fixed (byte* uniformPointer = uniformData)
        {
            this.flushContext.Api.QueueWriteBuffer(
                queue,
                this.uniformBuffer,
                passUniformOffset + UserUniformOffset,
                uniformPointer,
                (nuint)uniformData.Length);
        }

        WGPUBindGroupEntry* entries = stackalloc WGPUBindGroupEntry[4];
        entries[0] = new WGPUBindGroupEntry
        {
            binding = 0,
            textureView = sourceView
        };
        entries[1] = new WGPUBindGroupEntry
        {
            binding = 1,
            buffer = this.uniformBuffer,
            offset = passUniformOffset,
            size = WebGPUShaderFrameworkUniforms.ByteLength
        };
        entries[2] = new WGPUBindGroupEntry
        {
            binding = 2,
            buffer = this.uniformBuffer,
            offset = passUniformOffset + UserUniformOffset,
            size = checked((ulong)uniformData.Length)
        };
        entries[3] = new WGPUBindGroupEntry
        {
            binding = 3,
            sampler = this.flushContext.DeviceState.EffectFilteringSampler
        };

        WGPUBindGroupDescriptor bindGroupDescriptor = new()
        {
            layout = bindGroupLayout,
            entryCount = 4,
            entries = entries
        };

        using WebGPUHandle.HandleReference deviceReference = this.flushContext.DeviceHandle.AcquireReference();
        WGPUBindGroupImpl* bindGroup = this.flushContext.Api.DeviceCreateBindGroup((WGPUDeviceImpl*)deviceReference.Handle, in bindGroupDescriptor);
        if (bindGroup is null)
        {
            throw new InvalidOperationException("Failed to create the WebGPU layer-effect bind group.");
        }

        this.flushContext.TrackBindGroup(bindGroup);

        if (!this.flushContext.EnsureCommandEncoder() ||
            !this.flushContext.BeginRenderPass(outputView, WebGPUShaderEffectWorkingTexture.Descriptor, loadExisting: false))
        {
            throw new InvalidOperationException("Failed to begin the WebGPU layer-effect render pass.");
        }

        WGPURenderPassEncoderImpl* renderPass = this.flushContext.PassEncoder;
        this.flushContext.Api.RenderPassEncoderSetViewport(renderPass, 0, 0, outputSize.Width, outputSize.Height, 0, 1);
        this.flushContext.Api.RenderPassEncoderSetScissorRect(renderPass, 0, 0, (uint)outputSize.Width, (uint)outputSize.Height);
        this.flushContext.Api.RenderPassEncoderSetPipeline(renderPass, pipeline);
        this.flushContext.Api.RenderPassEncoderSetBindGroup(renderPass, 0, bindGroup);
        this.flushContext.Api.RenderPassEncoderDraw(renderPass, 3, 1, 0, 0);
        this.flushContext.EndRenderPassIfOpen();

        // No submission here: every pass owns a disjoint uniform region, so the recorded pass
        // stays in the shared flush encoder and rides the next range submission. Queue ordering
        // still executes the uniform writes above before that submission's commands.
    }

    /// <summary>
    /// Rounds a byte length up to the next multiple of a power-of-two alignment.
    /// </summary>
    /// <param name="value">The byte length to align.</param>
    /// <param name="alignment">The power-of-two alignment.</param>
    /// <returns>The aligned byte length.</returns>
    private static ulong AlignUp(ulong value, ulong alignment)
        => (value + alignment - 1) & ~(alignment - 1);

    /// <summary>
    /// Creates one associated <c>Rgba16Float</c> scratch texture and transfers ownership to the flush context.
    /// </summary>
    /// <remarks>
    /// The renderer creates two textures and alternates between them for consecutive passes. At
    /// 8 bytes per pixel each, they occupy 16 bytes per pixel in total. Using <c>Rgba32Float</c> would
    /// raise that total to 32 bytes per pixel and double the read/write bandwidth of every pass.
    /// See <see cref="WebGPUShaderEffectWorkingTexture.Descriptor"/> for the corresponding precision
    /// tradeoff and logical alpha representation.
    /// </remarks>
    private WGPUTextureImpl* CreateWorkingTexture(int width, int height, out WGPUTextureViewImpl* textureView)
    {
        const TextureUsage usage = TextureUsage.TextureBinding | TextureUsage.RenderAttachment;

        // Prefer a device-pooled working texture from an earlier flush. A larger entry is safe:
        // each pass renders through an explicit viewport/scissor, layer_load addresses texels
        // within the framework valid bounds, and layer_sample normalizes by textureDimensions.
        if (this.flushContext.DeviceState.TryRentPooledTexture(
                WGPUTextureFormat.RGBA16Float,
                (ulong)usage,
                (uint)width,
                (uint)height,
                out WGPUTextureImpl* rentedTexture,
                out textureView,
                out uint rentedWidth,
                out uint rentedHeight))
        {
            this.flushContext.TrackPooledTexture(rentedTexture, textureView, WGPUTextureFormat.RGBA16Float, (ulong)usage, rentedWidth, rentedHeight);
            return rentedTexture;
        }

        // The native texture and view formats must match both the effect pipeline and the logical
        // descriptor used when the resulting texture is sampled by another pass or written back.
        WGPUTextureDescriptor textureDescriptor = new()
        {
            usage = (ulong)usage,
            dimension = WGPUTextureDimension._2D,
            size = new WGPUExtent3D((uint)width, (uint)height, 1),
            format = WGPUTextureFormat.RGBA16Float,
            mipLevelCount = 1,
            sampleCount = 1
        };

        using WebGPUHandle.HandleReference deviceReference = this.flushContext.DeviceHandle.AcquireReference();
        WGPUTextureImpl* texture = this.flushContext.Api.DeviceCreateTexture((WGPUDeviceImpl*)deviceReference.Handle, in textureDescriptor);
        if (texture is null)
        {
            throw new InvalidOperationException("Failed to create a WebGPU layer-effect working texture.");
        }

        WGPUTextureViewDescriptor textureViewDescriptor = new()
        {
            format = WGPUTextureFormat.RGBA16Float,
            dimension = WGPUTextureViewDimension._2D,
            baseMipLevel = 0,
            mipLevelCount = 1,
            baseArrayLayer = 0,
            arrayLayerCount = 1,
            aspect = WGPUTextureAspect.All
        };

        textureView = this.flushContext.Api.TextureCreateView(texture, &textureViewDescriptor);
        if (textureView is null)
        {
            this.flushContext.Api.TextureRelease(texture);
            throw new InvalidOperationException("Failed to create a WebGPU layer-effect working texture view.");
        }

        // Returned to the device pool with the flush so later flushes rent instead of recreating.
        this.flushContext.TrackPooledTexture(texture, textureView, WGPUTextureFormat.RGBA16Float, (ulong)usage, (uint)width, (uint)height);
        return texture;
    }
}
