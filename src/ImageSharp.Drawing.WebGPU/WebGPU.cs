// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Processing.Backends.Native;

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

// The instance-shaped facade is intentional: resource owners retain the exact API dependency used
// to create their handles, keeping native lifetime calls on the same boundary as construction.
#pragma warning disable CA1822

/// <summary>
/// Provides the WebGPU operations used by the drawing backend.
/// </summary>
internal sealed unsafe class WebGPU
{
    private static readonly WebGPU Shared = new();

    private WebGPU()
    {
    }

    /// <summary>
    /// Gets the process-wide WebGPU API facade.
    /// </summary>
    /// <returns>The shared API facade.</returns>
    public static WebGPU GetApi() => Shared;

    /// <summary>
    /// Creates a WebGPU instance.
    /// </summary>
    /// <param name="descriptor">The instance configuration, or <see langword="null"/> for defaults.</param>
    /// <returns>The created instance, or <see langword="null"/> on failure.</returns>
    public Instance* CreateInstance(InstanceDescriptor* descriptor)
        => WebGPUNative.wgpuCreateInstance(descriptor);

    /// <summary>
    /// Reports whether an adapter supports a feature.
    /// </summary>
    /// <param name="adapter">The adapter to query.</param>
    /// <param name="feature">The feature to query.</param>
    /// <returns><see langword="true"/> when the feature is supported.</returns>
    public bool AdapterHasFeature(Adapter* adapter, FeatureName feature)
        => WebGPUNative.wgpuAdapterHasFeature(adapter, feature) != 0;

    /// <summary>
    /// Requests a device from an adapter.
    /// </summary>
    /// <param name="adapter">The adapter that creates the device.</param>
    /// <param name="descriptor">The requested device configuration.</param>
    /// <param name="callback">The rooted completion callback.</param>
    /// <param name="userData">The context pointer passed to the callback.</param>
    public void AdapterRequestDevice(
        Adapter* adapter,
        in DeviceDescriptor descriptor,
        WebGPURequestDeviceCallback callback,
        void* userData)
    {
        WGPURequestDeviceCallbackInfo callbackInfo = new()
        {
            mode = WGPUCallbackMode.AllowSpontaneous,
            callback = callback.Pointer,
            userdata1 = userData
        };

        // Register before entering native code because AllowSpontaneous permits the callback to
        // run before the request function returns. If P/Invoke cannot enter native code, cancel
        // that registration so the managed thunk does not retain a root for an invocation that
        // native WebGPU never accepted.
        callback.RegisterInvocation();

        fixed (DeviceDescriptor* descriptorPtr = &descriptor)
        {
            try
            {
                _ = WebGPUNative.wgpuAdapterRequestDevice(adapter, descriptorPtr, callbackInfo);
            }
            catch
            {
                callback.CancelInvocation();
                throw;
            }
        }
    }

    /// <summary>
    /// Reads the adapter limits.
    /// </summary>
    /// <param name="adapter">The adapter to query.</param>
    /// <param name="limits">Receives the adapter limits.</param>
    /// <returns>The query status.</returns>
    public Status AdapterGetLimits(Adapter* adapter, Limits* limits)
        => WebGPUNative.wgpuAdapterGetLimits(adapter, limits);

    /// <summary>
    /// Releases an adapter reference.
    /// </summary>
    /// <param name="adapter">The adapter to release.</param>
    public void AdapterRelease(Adapter* adapter)
        => WebGPUNative.wgpuAdapterRelease(adapter);

    /// <summary>
    /// Releases an instance reference.
    /// </summary>
    /// <param name="instance">The instance to release.</param>
    public void InstanceRelease(Instance* instance)
        => WebGPUNative.wgpuInstanceRelease(instance);

    /// <summary>
    /// Requests an adapter from an instance.
    /// </summary>
    /// <param name="instance">The instance that discovers the adapter.</param>
    /// <param name="options">The adapter selection criteria.</param>
    /// <param name="callback">The rooted completion callback.</param>
    /// <param name="userData">The context pointer passed to the callback.</param>
    public void InstanceRequestAdapter(
        Instance* instance,
        in RequestAdapterOptions options,
        WebGPURequestAdapterCallback callback,
        void* userData)
    {
        WGPURequestAdapterCallbackInfo callbackInfo = new()
        {
            mode = WGPUCallbackMode.AllowSpontaneous,
            callback = callback.Pointer,
            userdata1 = userData
        };

        // The callback may run from inside this request, so establish its managed lifetime before
        // native code sees the function pointer. A failed P/Invoke leaves no native invocation to
        // retire and must undo the registration immediately.
        callback.RegisterInvocation();

        fixed (RequestAdapterOptions* optionsPtr = &options)
        {
            try
            {
                _ = WebGPUNative.wgpuInstanceRequestAdapter(instance, optionsPtr, callbackInfo);
            }
            catch
            {
                callback.CancelInvocation();
                throw;
            }
        }
    }

    /// <summary>
    /// Creates a presentation surface for a native platform source.
    /// </summary>
    /// <param name="instance">The instance that owns the surface.</param>
    /// <param name="descriptor">The platform surface descriptor.</param>
    /// <returns>The created surface, or <see langword="null"/> on failure.</returns>
    public Surface* InstanceCreateSurface(Instance* instance, in SurfaceDescriptor descriptor)
    {
        fixed (SurfaceDescriptor* descriptorPtr = &descriptor)
        {
            return WebGPUNative.wgpuInstanceCreateSurface(instance, descriptorPtr);
        }
    }

    /// <summary>
    /// Creates a bind group.
    /// </summary>
    /// <param name="device">The device that owns the bind group.</param>
    /// <param name="descriptor">The bind-group configuration.</param>
    /// <returns>The created bind group, or <see langword="null"/> on failure.</returns>
    public BindGroup* DeviceCreateBindGroup(Device* device, in BindGroupDescriptor descriptor)
    {
        fixed (BindGroupDescriptor* descriptorPtr = &descriptor)
        {
            return WebGPUNative.wgpuDeviceCreateBindGroup(device, descriptorPtr);
        }
    }

    /// <summary>
    /// Creates a bind-group layout.
    /// </summary>
    /// <param name="device">The device that owns the layout.</param>
    /// <param name="descriptor">The layout configuration.</param>
    /// <returns>The created layout, or <see langword="null"/> on failure.</returns>
    public BindGroupLayout* DeviceCreateBindGroupLayout(Device* device, in BindGroupLayoutDescriptor descriptor)
    {
        fixed (BindGroupLayoutDescriptor* descriptorPtr = &descriptor)
        {
            return WebGPUNative.wgpuDeviceCreateBindGroupLayout(device, descriptorPtr);
        }
    }

    /// <summary>
    /// Creates a buffer.
    /// </summary>
    /// <param name="device">The device that owns the buffer.</param>
    /// <param name="descriptor">The buffer configuration.</param>
    /// <returns>The created buffer, or <see langword="null"/> on failure.</returns>
    public WgpuBuffer* DeviceCreateBuffer(Device* device, in BufferDescriptor descriptor)
    {
        fixed (BufferDescriptor* descriptorPtr = &descriptor)
        {
            return WebGPUNative.wgpuDeviceCreateBuffer(device, descriptorPtr);
        }
    }

    /// <summary>
    /// Creates a command encoder.
    /// </summary>
    /// <param name="device">The device that owns the encoder.</param>
    /// <param name="descriptor">The encoder configuration.</param>
    /// <returns>The created encoder, or <see langword="null"/> on failure.</returns>
    public CommandEncoder* DeviceCreateCommandEncoder(Device* device, in CommandEncoderDescriptor descriptor)
    {
        fixed (CommandEncoderDescriptor* descriptorPtr = &descriptor)
        {
            return WebGPUNative.wgpuDeviceCreateCommandEncoder(device, descriptorPtr);
        }
    }

    /// <summary>
    /// Creates a compute pipeline.
    /// </summary>
    /// <param name="device">The device that owns the pipeline.</param>
    /// <param name="descriptor">The compute-pipeline configuration.</param>
    /// <returns>The created pipeline, or <see langword="null"/> on failure.</returns>
    public ComputePipeline* DeviceCreateComputePipeline(Device* device, in ComputePipelineDescriptor descriptor)
    {
        fixed (ComputePipelineDescriptor* descriptorPtr = &descriptor)
        {
            return WebGPUNative.wgpuDeviceCreateComputePipeline(device, descriptorPtr);
        }
    }

    /// <summary>
    /// Creates a pipeline layout.
    /// </summary>
    /// <param name="device">The device that owns the layout.</param>
    /// <param name="descriptor">The pipeline-layout configuration.</param>
    /// <returns>The created layout, or <see langword="null"/> on failure.</returns>
    public PipelineLayout* DeviceCreatePipelineLayout(Device* device, in PipelineLayoutDescriptor descriptor)
    {
        fixed (PipelineLayoutDescriptor* descriptorPtr = &descriptor)
        {
            return WebGPUNative.wgpuDeviceCreatePipelineLayout(device, descriptorPtr);
        }
    }

    /// <summary>
    /// Creates a render pipeline.
    /// </summary>
    /// <param name="device">The device that owns the pipeline.</param>
    /// <param name="descriptor">The render-pipeline configuration.</param>
    /// <returns>The created pipeline, or <see langword="null"/> on failure.</returns>
    public RenderPipeline* DeviceCreateRenderPipeline(Device* device, in RenderPipelineDescriptor descriptor)
    {
        fixed (RenderPipelineDescriptor* descriptorPtr = &descriptor)
        {
            return WebGPUNative.wgpuDeviceCreateRenderPipeline(device, descriptorPtr);
        }
    }

    /// <summary>
    /// Creates a shader module.
    /// </summary>
    /// <param name="device">The device that owns the module.</param>
    /// <param name="descriptor">The shader-module configuration.</param>
    /// <returns>The created module, or <see langword="null"/> on failure.</returns>
    public ShaderModule* DeviceCreateShaderModule(Device* device, in ShaderModuleDescriptor descriptor)
    {
        fixed (ShaderModuleDescriptor* descriptorPtr = &descriptor)
        {
            return WebGPUNative.wgpuDeviceCreateShaderModule(device, descriptorPtr);
        }
    }

    /// <summary>
    /// Creates a texture.
    /// </summary>
    /// <param name="device">The device that owns the texture.</param>
    /// <param name="descriptor">The texture configuration.</param>
    /// <returns>The created texture, or <see langword="null"/> on failure.</returns>
    public Texture* DeviceCreateTexture(Device* device, in TextureDescriptor descriptor)
    {
        fixed (TextureDescriptor* descriptorPtr = &descriptor)
        {
            return WebGPUNative.wgpuDeviceCreateTexture(device, descriptorPtr);
        }
    }

    /// <summary>
    /// Reads the features supported by a device.
    /// </summary>
    /// <param name="device">The device to query.</param>
    /// <param name="features">Receives the native feature array.</param>
    public void DeviceGetFeatures(Device* device, SupportedFeatures* features)
        => WebGPUNative.wgpuDeviceGetFeatures(device, features);

    /// <summary>
    /// Reads the device limits.
    /// </summary>
    /// <param name="device">The device to query.</param>
    /// <param name="limits">Receives the device limits.</param>
    /// <returns>The query status.</returns>
    public Status DeviceGetLimits(Device* device, Limits* limits)
        => WebGPUNative.wgpuDeviceGetLimits(device, limits);

    /// <summary>
    /// Frees members allocated by a supported-features query.
    /// </summary>
    /// <param name="features">The supported-features result to release.</param>
    public void SupportedFeaturesFreeMembers(SupportedFeatures features)
        => WebGPUNative.wgpuSupportedFeaturesFreeMembers(features);

    /// <summary>
    /// Gets the device's default queue.
    /// </summary>
    /// <param name="device">The device that owns the queue.</param>
    /// <returns>The default queue.</returns>
    public Queue* DeviceGetQueue(Device* device)
        => WebGPUNative.wgpuDeviceGetQueue(device);

    /// <summary>
    /// Polls a device for asynchronous progress.
    /// </summary>
    /// <param name="device">The device to poll.</param>
    /// <param name="wait">Whether to wait for the requested work to complete.</param>
    /// <param name="submissionIndex">The exact submission to wait for, or <see langword="null"/> for all submitted work.</param>
    /// <returns><see langword="true"/> when the queue is empty after polling.</returns>
    public bool DevicePoll(Device* device, bool wait, ulong* submissionIndex)
        => WebGPUNative.wgpuDevicePoll(device, wait ? 1U : 0U, submissionIndex) != 0;

    /// <summary>
    /// Releases a device reference.
    /// </summary>
    /// <param name="device">The device to release.</param>
    public void DeviceRelease(Device* device)
        => WebGPUNative.wgpuDeviceRelease(device);

    /// <summary>
    /// Releases a queue reference.
    /// </summary>
    /// <param name="queue">The queue to release.</param>
    public void QueueRelease(Queue* queue)
        => WebGPUNative.wgpuQueueRelease(queue);

    /// <summary>
    /// Registers a callback for completion of work submitted before this call.
    /// </summary>
    /// <param name="queue">The queue whose submitted work is observed.</param>
    /// <param name="callback">The rooted completion callback.</param>
    /// <param name="userData">The context pointer passed to the callback.</param>
    public void QueueOnSubmittedWorkDone(
        Queue* queue,
        WebGPUQueueWorkDoneCallback callback,
        void* userData)
    {
        WGPUQueueWorkDoneCallbackInfo callbackInfo = new()
        {
            mode = WGPUCallbackMode.AllowSpontaneous,
            callback = callback.Pointer,
            userdata1 = userData
        };

        callback.RegisterInvocation();

        try
        {
            _ = WebGPUNative.wgpuQueueOnSubmittedWorkDone(queue, callbackInfo);
        }
        catch
        {
            callback.CancelInvocation();
            throw;
        }
    }

    /// <summary>
    /// Writes bytes into a buffer.
    /// </summary>
    /// <param name="queue">The queue performing the write.</param>
    /// <param name="buffer">The destination buffer.</param>
    /// <param name="offset">The destination byte offset.</param>
    /// <param name="data">The source bytes.</param>
    /// <param name="size">The number of source bytes.</param>
    public void QueueWriteBuffer(Queue* queue, WgpuBuffer* buffer, ulong offset, void* data, nuint size)
        => WebGPUNative.wgpuQueueWriteBuffer(queue, buffer, offset, data, size);

    /// <summary>
    /// Writes texels into a texture.
    /// </summary>
    /// <param name="queue">The queue performing the write.</param>
    /// <param name="destination">The destination texture region.</param>
    /// <param name="data">The source bytes.</param>
    /// <param name="dataSize">The number of source bytes.</param>
    /// <param name="dataLayout">The source layout.</param>
    /// <param name="writeSize">The destination extent.</param>
    public void QueueWriteTexture(
        Queue* queue,
        in ImageCopyTexture destination,
        void* data,
        nuint dataSize,
        in TextureDataLayout dataLayout,
        in Extent3D writeSize)
    {
        fixed (ImageCopyTexture* destinationPtr = &destination)
        {
            fixed (TextureDataLayout* dataLayoutPtr = &dataLayout)
            {
                fixed (Extent3D* writeSizePtr = &writeSize)
                {
                    WebGPUNative.wgpuQueueWriteTexture(queue, destinationPtr, data, dataSize, dataLayoutPtr, writeSizePtr);
                }
            }
        }
    }

    /// <summary>
    /// Submits command buffers to a queue.
    /// </summary>
    /// <param name="queue">The destination queue.</param>
    /// <param name="commandCount">The number of command-buffer pointers.</param>
    /// <param name="commands">The first command-buffer pointer.</param>
    public void QueueSubmit(Queue* queue, nuint commandCount, ref CommandBuffer* commands)
    {
        fixed (CommandBuffer** commandsPtr = &commands)
        {
            WebGPUNative.wgpuQueueSubmit(queue, commandCount, commandsPtr);
        }
    }

    /// <summary>
    /// Submits command buffers and returns the native submission index.
    /// </summary>
    /// <param name="queue">The destination queue.</param>
    /// <param name="commandCount">The number of command-buffer pointers.</param>
    /// <param name="commands">The first command-buffer pointer.</param>
    /// <returns>The submission index assigned by the queue.</returns>
    public ulong QueueSubmitForIndex(Queue* queue, nuint commandCount, ref CommandBuffer* commands)
    {
        fixed (CommandBuffer** commandsPtr = &commands)
        {
            return WebGPUNative.wgpuQueueSubmitForIndex(queue, commandCount, commandsPtr);
        }
    }

    /// <summary>
    /// Begins a compute pass.
    /// </summary>
    /// <param name="encoder">The command encoder.</param>
    /// <param name="descriptor">The compute-pass configuration.</param>
    /// <returns>The compute-pass encoder.</returns>
    public ComputePassEncoder* CommandEncoderBeginComputePass(CommandEncoder* encoder, in ComputePassDescriptor descriptor)
    {
        fixed (ComputePassDescriptor* descriptorPtr = &descriptor)
        {
            return WebGPUNative.wgpuCommandEncoderBeginComputePass(encoder, descriptorPtr);
        }
    }

    /// <summary>
    /// Begins a render pass.
    /// </summary>
    /// <param name="encoder">The command encoder.</param>
    /// <param name="descriptor">The render-pass configuration.</param>
    /// <returns>The render-pass encoder.</returns>
    public RenderPassEncoder* CommandEncoderBeginRenderPass(CommandEncoder* encoder, in RenderPassDescriptor descriptor)
    {
        fixed (RenderPassDescriptor* descriptorPtr = &descriptor)
        {
            return WebGPUNative.wgpuCommandEncoderBeginRenderPass(encoder, descriptorPtr);
        }
    }

    /// <summary>
    /// Copies bytes between buffers.
    /// </summary>
    /// <param name="encoder">The command encoder.</param>
    /// <param name="source">The source buffer.</param>
    /// <param name="sourceOffset">The source byte offset.</param>
    /// <param name="destination">The destination buffer.</param>
    /// <param name="destinationOffset">The destination byte offset.</param>
    /// <param name="size">The number of bytes to copy.</param>
    public void CommandEncoderCopyBufferToBuffer(
        CommandEncoder* encoder,
        WgpuBuffer* source,
        ulong sourceOffset,
        WgpuBuffer* destination,
        ulong destinationOffset,
        ulong size)
        => WebGPUNative.wgpuCommandEncoderCopyBufferToBuffer(
            encoder,
            source,
            sourceOffset,
            destination,
            destinationOffset,
            size);

    /// <summary>
    /// Copies texture data into a buffer.
    /// </summary>
    /// <param name="encoder">The command encoder.</param>
    /// <param name="source">The source texture region.</param>
    /// <param name="destination">The destination buffer layout.</param>
    /// <param name="copySize">The copied extent.</param>
    public void CommandEncoderCopyTextureToBuffer(
        CommandEncoder* encoder,
        in ImageCopyTexture source,
        in ImageCopyBuffer destination,
        in Extent3D copySize)
    {
        fixed (ImageCopyTexture* sourcePtr = &source)
        {
            fixed (ImageCopyBuffer* destinationPtr = &destination)
            {
                fixed (Extent3D* copySizePtr = &copySize)
                {
                    WebGPUNative.wgpuCommandEncoderCopyTextureToBuffer(encoder, sourcePtr, destinationPtr, copySizePtr);
                }
            }
        }
    }

    /// <summary>
    /// Copies data between textures.
    /// </summary>
    /// <param name="encoder">The command encoder.</param>
    /// <param name="source">The source texture region.</param>
    /// <param name="destination">The destination texture region.</param>
    /// <param name="copySize">The copied extent.</param>
    public void CommandEncoderCopyTextureToTexture(
        CommandEncoder* encoder,
        in ImageCopyTexture source,
        in ImageCopyTexture destination,
        in Extent3D copySize)
    {
        fixed (ImageCopyTexture* sourcePtr = &source)
        {
            fixed (ImageCopyTexture* destinationPtr = &destination)
            {
                fixed (Extent3D* copySizePtr = &copySize)
                {
                    WebGPUNative.wgpuCommandEncoderCopyTextureToTexture(encoder, sourcePtr, destinationPtr, copySizePtr);
                }
            }
        }
    }

    /// <summary>
    /// Finishes command recording.
    /// </summary>
    /// <param name="encoder">The command encoder to finish.</param>
    /// <param name="descriptor">The command-buffer configuration.</param>
    /// <returns>The recorded command buffer.</returns>
    public CommandBuffer* CommandEncoderFinish(CommandEncoder* encoder, in CommandBufferDescriptor descriptor)
    {
        fixed (CommandBufferDescriptor* descriptorPtr = &descriptor)
        {
            return WebGPUNative.wgpuCommandEncoderFinish(encoder, descriptorPtr);
        }
    }

    /// <summary>
    /// Sets the active compute pipeline.
    /// </summary>
    /// <param name="encoder">The compute-pass encoder.</param>
    /// <param name="pipeline">The compute pipeline.</param>
    public void ComputePassEncoderSetPipeline(ComputePassEncoder* encoder, ComputePipeline* pipeline)
        => WebGPUNative.wgpuComputePassEncoderSetPipeline(encoder, pipeline);

    /// <summary>
    /// Sets one compute bind group.
    /// </summary>
    /// <param name="encoder">The compute-pass encoder.</param>
    /// <param name="groupIndex">The bind-group slot.</param>
    /// <param name="group">The bind group.</param>
    /// <param name="dynamicOffsetCount">The number of dynamic offsets.</param>
    /// <param name="dynamicOffsets">The dynamic offsets, or <see langword="null"/> when the count is zero.</param>
    public void ComputePassEncoderSetBindGroup(
        ComputePassEncoder* encoder,
        uint groupIndex,
        BindGroup* group,
        nuint dynamicOffsetCount,
        uint* dynamicOffsets)
        => WebGPUNative.wgpuComputePassEncoderSetBindGroup(
            encoder,
            groupIndex,
            group,
            dynamicOffsetCount,
            dynamicOffsets);

    /// <summary>
    /// Dispatches compute workgroups.
    /// </summary>
    /// <param name="encoder">The compute-pass encoder.</param>
    /// <param name="x">The workgroup count in X.</param>
    /// <param name="y">The workgroup count in Y.</param>
    /// <param name="z">The workgroup count in Z.</param>
    public void ComputePassEncoderDispatchWorkgroups(ComputePassEncoder* encoder, uint x, uint y, uint z)
        => WebGPUNative.wgpuComputePassEncoderDispatchWorkgroups(encoder, x, y, z);

    /// <summary>
    /// Dispatches compute workgroups using indirect counts.
    /// </summary>
    /// <param name="encoder">The compute-pass encoder.</param>
    /// <param name="indirectBuffer">The buffer containing the dispatch counts.</param>
    /// <param name="indirectOffset">The byte offset of the dispatch counts.</param>
    public void ComputePassEncoderDispatchWorkgroupsIndirect(
        ComputePassEncoder* encoder,
        WgpuBuffer* indirectBuffer,
        ulong indirectOffset)
        => WebGPUNative.wgpuComputePassEncoderDispatchWorkgroupsIndirect(encoder, indirectBuffer, indirectOffset);

    /// <summary>
    /// Ends a compute pass.
    /// </summary>
    /// <param name="encoder">The compute-pass encoder.</param>
    public void ComputePassEncoderEnd(ComputePassEncoder* encoder)
        => WebGPUNative.wgpuComputePassEncoderEnd(encoder);

    /// <summary>
    /// Ends a render pass.
    /// </summary>
    /// <param name="encoder">The render-pass encoder.</param>
    public void RenderPassEncoderEnd(RenderPassEncoder* encoder)
        => WebGPUNative.wgpuRenderPassEncoderEnd(encoder);

    /// <summary>
    /// Binds a resource group to a render pass.
    /// </summary>
    /// <param name="encoder">The render-pass encoder.</param>
    /// <param name="groupIndex">The pipeline bind-group index.</param>
    /// <param name="bindGroup">The bind group to bind.</param>
    public void RenderPassEncoderSetBindGroup(RenderPassEncoder* encoder, uint groupIndex, BindGroup* bindGroup)
        => WebGPUNative.wgpuRenderPassEncoderSetBindGroup(encoder, groupIndex, bindGroup, 0, null);

    /// <summary>
    /// Binds a graphics pipeline to a render pass.
    /// </summary>
    /// <param name="encoder">The render-pass encoder.</param>
    /// <param name="pipeline">The graphics pipeline to bind.</param>
    public void RenderPassEncoderSetPipeline(RenderPassEncoder* encoder, RenderPipeline* pipeline)
        => WebGPUNative.wgpuRenderPassEncoderSetPipeline(encoder, pipeline);

    /// <summary>
    /// Draws non-indexed geometry in a render pass.
    /// </summary>
    /// <param name="encoder">The render-pass encoder.</param>
    /// <param name="vertexCount">The number of vertices per instance.</param>
    /// <param name="instanceCount">The number of instances.</param>
    /// <param name="firstVertex">The first vertex index.</param>
    /// <param name="firstInstance">The first instance index.</param>
    public void RenderPassEncoderDraw(RenderPassEncoder* encoder, uint vertexCount, uint instanceCount, uint firstVertex, uint firstInstance)
        => WebGPUNative.wgpuRenderPassEncoderDraw(encoder, vertexCount, instanceCount, firstVertex, firstInstance);

    /// <summary>
    /// Configures a presentation surface.
    /// </summary>
    /// <param name="surface">The surface to configure.</param>
    /// <param name="configuration">The surface configuration.</param>
    public void SurfaceConfigure(Surface* surface, in SurfaceConfiguration configuration)
    {
        fixed (SurfaceConfiguration* configurationPtr = &configuration)
        {
            WebGPUNative.wgpuSurfaceConfigure(surface, configurationPtr);
        }
    }

    /// <summary>
    /// Reads the capabilities supported by a surface and adapter pair.
    /// </summary>
    /// <param name="surface">The surface to query.</param>
    /// <param name="adapter">The adapter used to present to the surface.</param>
    /// <param name="capabilities">Receives the supported surface capabilities.</param>
    /// <returns>The query status.</returns>
    public Status SurfaceGetCapabilities(Surface* surface, Adapter* adapter, WGPUSurfaceCapabilities* capabilities)
        => WebGPUNative.wgpuSurfaceGetCapabilities(surface, adapter, capabilities);

    /// <summary>
    /// Frees members allocated by a surface-capabilities query.
    /// </summary>
    /// <param name="capabilities">The surface-capabilities result to release.</param>
    public void SurfaceCapabilitiesFreeMembers(WGPUSurfaceCapabilities capabilities)
        => WebGPUNative.wgpuSurfaceCapabilitiesFreeMembers(capabilities);

    /// <summary>
    /// Acquires the surface's current texture.
    /// </summary>
    /// <param name="surface">The surface to acquire from.</param>
    /// <param name="surfaceTexture">Receives the acquired texture and status.</param>
    public void SurfaceGetCurrentTexture(Surface* surface, SurfaceTexture* surfaceTexture)
        => WebGPUNative.wgpuSurfaceGetCurrentTexture(surface, surfaceTexture);

    /// <summary>
    /// Presents the surface's current texture.
    /// </summary>
    /// <param name="surface">The surface to present.</param>
    /// <returns>The presentation status.</returns>
    public Status SurfacePresent(Surface* surface)
        => WebGPUNative.wgpuSurfacePresent(surface);

    /// <summary>
    /// Creates a texture view.
    /// </summary>
    /// <param name="texture">The source texture.</param>
    /// <param name="descriptor">The view configuration, or <see langword="null"/> for defaults.</param>
    /// <returns>The created view.</returns>
    public TextureView* TextureCreateView(Texture* texture, TextureViewDescriptor* descriptor)
        => WebGPUNative.wgpuTextureCreateView(texture, descriptor);

    /// <summary>
    /// Releases a bind group.
    /// </summary>
    /// <param name="bindGroup">The bind group to release.</param>
    public void BindGroupRelease(BindGroup* bindGroup) => WebGPUNative.wgpuBindGroupRelease(bindGroup);

    /// <summary>
    /// Releases a bind-group layout.
    /// </summary>
    /// <param name="layout">The layout to release.</param>
    public void BindGroupLayoutRelease(BindGroupLayout* layout) => WebGPUNative.wgpuBindGroupLayoutRelease(layout);

    /// <summary>
    /// Releases a buffer.
    /// </summary>
    /// <param name="buffer">The buffer to release.</param>
    public void BufferRelease(WgpuBuffer* buffer) => WebGPUNative.wgpuBufferRelease(buffer);

    /// <summary>
    /// Returns a read-only mapped buffer range.
    /// </summary>
    /// <param name="buffer">The mapped buffer.</param>
    /// <param name="offset">The byte offset.</param>
    /// <param name="size">The byte length.</param>
    /// <returns>The first mapped byte.</returns>
    public void* BufferGetConstMappedRange(WgpuBuffer* buffer, nuint offset, nuint size)
        => WebGPUNative.wgpuBufferGetConstMappedRange(buffer, offset, size);

    /// <summary>
    /// Maps a buffer range asynchronously.
    /// </summary>
    /// <param name="buffer">The buffer to map.</param>
    /// <param name="mode">The requested CPU access.</param>
    /// <param name="offset">The first mapped byte.</param>
    /// <param name="size">The mapped byte length.</param>
    /// <param name="callback">The rooted completion callback.</param>
    /// <param name="userData">The context pointer passed to the callback.</param>
    public void BufferMapAsync(
        WgpuBuffer* buffer,
        MapMode mode,
        nuint offset,
        nuint size,
        WebGPUBufferMapCallback callback,
        void* userData)
    {
        WGPUBufferMapCallbackInfo callbackInfo = new()
        {
            mode = WGPUCallbackMode.AllowSpontaneous,
            callback = callback.Pointer,
            userdata1 = userData
        };

        callback.RegisterInvocation();

        try
        {
            _ = WebGPUNative.wgpuBufferMapAsync(buffer, (ulong)mode, offset, size, callbackInfo);
        }
        catch
        {
            callback.CancelInvocation();
            throw;
        }
    }

    /// <summary>
    /// Unmaps a buffer.
    /// </summary>
    /// <param name="buffer">The buffer to unmap.</param>
    public void BufferUnmap(WgpuBuffer* buffer) => WebGPUNative.wgpuBufferUnmap(buffer);

    /// <summary>
    /// Releases a command buffer.
    /// </summary>
    /// <param name="commandBuffer">The command buffer to release.</param>
    public void CommandBufferRelease(CommandBuffer* commandBuffer) => WebGPUNative.wgpuCommandBufferRelease(commandBuffer);

    /// <summary>
    /// Releases a command encoder.
    /// </summary>
    /// <param name="encoder">The command encoder to release.</param>
    public void CommandEncoderRelease(CommandEncoder* encoder) => WebGPUNative.wgpuCommandEncoderRelease(encoder);

    /// <summary>
    /// Releases a compute-pass encoder.
    /// </summary>
    /// <param name="encoder">The encoder to release.</param>
    public void ComputePassEncoderRelease(ComputePassEncoder* encoder) => WebGPUNative.wgpuComputePassEncoderRelease(encoder);

    /// <summary>
    /// Releases a compute pipeline.
    /// </summary>
    /// <param name="pipeline">The pipeline to release.</param>
    public void ComputePipelineRelease(ComputePipeline* pipeline) => WebGPUNative.wgpuComputePipelineRelease(pipeline);

    /// <summary>
    /// Releases a pipeline layout.
    /// </summary>
    /// <param name="layout">The layout to release.</param>
    public void PipelineLayoutRelease(PipelineLayout* layout) => WebGPUNative.wgpuPipelineLayoutRelease(layout);

    /// <summary>
    /// Releases a render-pass encoder.
    /// </summary>
    /// <param name="encoder">The encoder to release.</param>
    public void RenderPassEncoderRelease(RenderPassEncoder* encoder) => WebGPUNative.wgpuRenderPassEncoderRelease(encoder);

    /// <summary>
    /// Releases a render pipeline.
    /// </summary>
    /// <param name="pipeline">The pipeline to release.</param>
    public void RenderPipelineRelease(RenderPipeline* pipeline) => WebGPUNative.wgpuRenderPipelineRelease(pipeline);

    /// <summary>
    /// Releases a shader module.
    /// </summary>
    /// <param name="module">The module to release.</param>
    public void ShaderModuleRelease(ShaderModule* module) => WebGPUNative.wgpuShaderModuleRelease(module);

    /// <summary>
    /// Releases a surface.
    /// </summary>
    /// <param name="surface">The surface to release.</param>
    public void SurfaceRelease(Surface* surface) => WebGPUNative.wgpuSurfaceRelease(surface);

    /// <summary>
    /// Releases a texture.
    /// </summary>
    /// <param name="texture">The texture to release.</param>
    public void TextureRelease(Texture* texture) => WebGPUNative.wgpuTextureRelease(texture);

    /// <summary>
    /// Releases a texture view.
    /// </summary>
    /// <param name="view">The view to release.</param>
    public void TextureViewRelease(TextureView* view) => WebGPUNative.wgpuTextureViewRelease(view);

    /// <summary>
    /// Registers the process-wide wgpu-native logging callback.
    /// </summary>
    /// <param name="callback">The unmanaged logging callback.</param>
    /// <param name="userData">The context pointer passed to the callback.</param>
    public void SetLogCallback(
        delegate* unmanaged[Cdecl]<LogLevel, WGPUStringView, void*, void> callback,
        void* userData)
        => WebGPUNative.wgpuSetLogCallback(callback, userData);

    /// <summary>
    /// Sets the process-wide wgpu-native log level.
    /// </summary>
    /// <param name="level">The minimum emitted log level.</param>
    public void SetLogLevel(LogLevel level) => WebGPUNative.wgpuSetLogLevel(level);
}

#pragma warning restore CA1822
