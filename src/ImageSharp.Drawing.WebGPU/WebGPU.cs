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
    public WGPUInstanceImpl* CreateInstance(WGPUInstanceDescriptor* descriptor)
        => WebGPUNative.wgpuCreateInstance(descriptor);

    /// <summary>
    /// Reports whether an adapter supports a feature.
    /// </summary>
    /// <param name="adapter">The adapter to query.</param>
    /// <param name="feature">The feature to query.</param>
    /// <returns><see langword="true"/> when the feature is supported.</returns>
    public bool AdapterHasFeature(WGPUAdapterImpl* adapter, WGPUFeatureName feature)
        => WebGPUNative.wgpuAdapterHasFeature(adapter, feature) != 0;

    /// <summary>
    /// Requests a device from an adapter.
    /// </summary>
    /// <param name="adapter">The adapter that creates the device.</param>
    /// <param name="descriptor">The requested device configuration.</param>
    /// <param name="callback">The rooted completion callback.</param>
    /// <param name="userData">The context pointer passed to the callback.</param>
    public void AdapterRequestDevice(
        WGPUAdapterImpl* adapter,
        in WGPUDeviceDescriptor descriptor,
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

        fixed (WGPUDeviceDescriptor* descriptorPtr = &descriptor)
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
    public WGPUStatus AdapterGetLimits(WGPUAdapterImpl* adapter, WGPULimits* limits)
        => WebGPUNative.wgpuAdapterGetLimits(adapter, limits);

    /// <summary>
    /// Queries an adapter's identity and classification.
    /// </summary>
    /// <param name="adapter">The adapter to query.</param>
    /// <param name="info">Receives the adapter info; release with <see cref="AdapterInfoFreeMembers"/>.</param>
    /// <returns>The query status.</returns>
    public WGPUStatus AdapterGetInfo(WGPUAdapterImpl* adapter, WGPUAdapterInfo* info)
        => WebGPUNative.wgpuAdapterGetInfo(adapter, info);

    /// <summary>
    /// Releases the allocated string members of an adapter info value.
    /// </summary>
    /// <param name="info">The adapter info whose members are released.</param>
    public void AdapterInfoFreeMembers(WGPUAdapterInfo info)
        => WebGPUNative.wgpuAdapterInfoFreeMembers(info);

    /// <summary>
    /// Releases an adapter reference.
    /// </summary>
    /// <param name="adapter">The adapter to release.</param>
    public void AdapterRelease(WGPUAdapterImpl* adapter)
        => WebGPUNative.wgpuAdapterRelease(adapter);

    /// <summary>
    /// Releases an instance reference.
    /// </summary>
    /// <param name="instance">The instance to release.</param>
    public void InstanceRelease(WGPUInstanceImpl* instance)
        => WebGPUNative.wgpuInstanceRelease(instance);

    /// <summary>
    /// Requests an adapter from an instance.
    /// </summary>
    /// <param name="instance">The instance that discovers the adapter.</param>
    /// <param name="options">The adapter selection criteria.</param>
    /// <param name="callback">The rooted completion callback.</param>
    /// <param name="userData">The context pointer passed to the callback.</param>
    public void InstanceRequestAdapter(
        WGPUInstanceImpl* instance,
        in WGPURequestAdapterOptions options,
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

        fixed (WGPURequestAdapterOptions* optionsPtr = &options)
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
    public WGPUSurfaceImpl* InstanceCreateSurface(WGPUInstanceImpl* instance, in WGPUSurfaceDescriptor descriptor)
    {
        fixed (WGPUSurfaceDescriptor* descriptorPtr = &descriptor)
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
    public WGPUBindGroupImpl* DeviceCreateBindGroup(WGPUDeviceImpl* device, in WGPUBindGroupDescriptor descriptor)
    {
        fixed (WGPUBindGroupDescriptor* descriptorPtr = &descriptor)
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
    public WGPUBindGroupLayoutImpl* DeviceCreateBindGroupLayout(WGPUDeviceImpl* device, in WGPUBindGroupLayoutDescriptor descriptor)
    {
        fixed (WGPUBindGroupLayoutDescriptor* descriptorPtr = &descriptor)
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
    public WGPUBufferImpl* DeviceCreateBuffer(WGPUDeviceImpl* device, in WGPUBufferDescriptor descriptor)
    {
        fixed (WGPUBufferDescriptor* descriptorPtr = &descriptor)
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
    public WGPUCommandEncoderImpl* DeviceCreateCommandEncoder(WGPUDeviceImpl* device, in WGPUCommandEncoderDescriptor descriptor)
    {
        fixed (WGPUCommandEncoderDescriptor* descriptorPtr = &descriptor)
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
    public WGPUComputePipelineImpl* DeviceCreateComputePipeline(WGPUDeviceImpl* device, in WGPUComputePipelineDescriptor descriptor)
    {
        fixed (WGPUComputePipelineDescriptor* descriptorPtr = &descriptor)
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
    public WGPUPipelineLayoutImpl* DeviceCreatePipelineLayout(WGPUDeviceImpl* device, in WGPUPipelineLayoutDescriptor descriptor)
    {
        fixed (WGPUPipelineLayoutDescriptor* descriptorPtr = &descriptor)
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
    public WGPURenderPipelineImpl* DeviceCreateRenderPipeline(WGPUDeviceImpl* device, in WGPURenderPipelineDescriptor descriptor)
    {
        fixed (WGPURenderPipelineDescriptor* descriptorPtr = &descriptor)
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
    public WGPUShaderModuleImpl* DeviceCreateShaderModule(WGPUDeviceImpl* device, in WGPUShaderModuleDescriptor descriptor)
    {
        fixed (WGPUShaderModuleDescriptor* descriptorPtr = &descriptor)
        {
            return WebGPUNative.wgpuDeviceCreateShaderModule(device, descriptorPtr);
        }
    }

    /// <summary>
    /// Creates a texture sampler.
    /// </summary>
    /// <param name="device">The device that owns the sampler.</param>
    /// <param name="descriptor">The sampler configuration.</param>
    /// <returns>The created sampler, or <see langword="null"/> on failure.</returns>
    public WGPUSamplerImpl* DeviceCreateSampler(WGPUDeviceImpl* device, in WGPUSamplerDescriptor descriptor)
    {
        fixed (WGPUSamplerDescriptor* descriptorPtr = &descriptor)
        {
            return WebGPUNative.wgpuDeviceCreateSampler(device, descriptorPtr);
        }
    }

    /// <summary>
    /// Begins an error scope for subsequent device operations.
    /// </summary>
    /// <param name="device">The device that owns the scope.</param>
    /// <param name="filter">The class of error captured by the scope.</param>
    public void DevicePushErrorScope(WGPUDeviceImpl* device, WGPUErrorFilter filter)
        => WebGPUNative.wgpuDevicePushErrorScope(device, filter);

    /// <summary>
    /// Ends the current error scope and reports its captured error.
    /// </summary>
    /// <param name="device">The device that owns the scope.</param>
    /// <param name="callback">The rooted callback that receives the scope result.</param>
    /// <param name="userData">The context pointer passed to the callback.</param>
    public void DevicePopErrorScope(WGPUDeviceImpl* device, WebGPUPopErrorScopeCallback callback, void* userData)
    {
        WGPUPopErrorScopeCallbackInfo callbackInfo = new()
        {
            mode = WGPUCallbackMode.AllowSpontaneous,
            callback = callback.Pointer,
            userdata1 = userData
        };

        callback.RegisterInvocation();

        try
        {
            _ = WebGPUNative.wgpuDevicePopErrorScope(device, callbackInfo);
        }
        catch
        {
            callback.CancelInvocation();
            throw;
        }
    }

    /// <summary>
    /// Creates a texture.
    /// </summary>
    /// <param name="device">The device that owns the texture.</param>
    /// <param name="descriptor">The texture configuration.</param>
    /// <returns>The created texture, or <see langword="null"/> on failure.</returns>
    public WGPUTextureImpl* DeviceCreateTexture(WGPUDeviceImpl* device, in WGPUTextureDescriptor descriptor)
    {
        fixed (WGPUTextureDescriptor* descriptorPtr = &descriptor)
        {
            return WebGPUNative.wgpuDeviceCreateTexture(device, descriptorPtr);
        }
    }

    /// <summary>
    /// Reads the features supported by a device.
    /// </summary>
    /// <param name="device">The device to query.</param>
    /// <param name="features">Receives the native feature array.</param>
    public void DeviceGetFeatures(WGPUDeviceImpl* device, WGPUSupportedFeatures* features)
        => WebGPUNative.wgpuDeviceGetFeatures(device, features);

    /// <summary>
    /// Reads the device limits.
    /// </summary>
    /// <param name="device">The device to query.</param>
    /// <param name="limits">Receives the device limits.</param>
    /// <returns>The query status.</returns>
    public WGPUStatus DeviceGetLimits(WGPUDeviceImpl* device, WGPULimits* limits)
        => WebGPUNative.wgpuDeviceGetLimits(device, limits);

    /// <summary>
    /// Frees members allocated by a supported-features query.
    /// </summary>
    /// <param name="features">The supported-features result to release.</param>
    public void SupportedFeaturesFreeMembers(WGPUSupportedFeatures features)
        => WebGPUNative.wgpuSupportedFeaturesFreeMembers(features);

    /// <summary>
    /// Gets the device's default queue.
    /// </summary>
    /// <param name="device">The device that owns the queue.</param>
    /// <returns>The default queue.</returns>
    public WGPUQueueImpl* DeviceGetQueue(WGPUDeviceImpl* device)
        => WebGPUNative.wgpuDeviceGetQueue(device);

    /// <summary>
    /// Polls a device for asynchronous progress.
    /// </summary>
    /// <param name="device">The device to poll.</param>
    /// <param name="wait">Whether to wait for the requested work to complete.</param>
    /// <param name="submissionIndex">The exact submission to wait for, or <see langword="null"/> for all submitted work.</param>
    /// <returns><see langword="true"/> when the queue is empty after polling.</returns>
    public bool DevicePoll(WGPUDeviceImpl* device, bool wait, ulong* submissionIndex)
        => WebGPUNative.wgpuDevicePoll(device, wait ? 1U : 0U, submissionIndex) != 0;

    /// <summary>
    /// Releases a device reference.
    /// </summary>
    /// <param name="device">The device to release.</param>
    public void DeviceRelease(WGPUDeviceImpl* device)
        => WebGPUNative.wgpuDeviceRelease(device);

    /// <summary>
    /// Releases a queue reference.
    /// </summary>
    /// <param name="queue">The queue to release.</param>
    public void QueueRelease(WGPUQueueImpl* queue)
        => WebGPUNative.wgpuQueueRelease(queue);

    /// <summary>
    /// Releases a sampler reference.
    /// </summary>
    /// <param name="sampler">The sampler to release.</param>
    public void SamplerRelease(WGPUSamplerImpl* sampler)
        => WebGPUNative.wgpuSamplerRelease(sampler);

    /// <summary>
    /// Registers a callback for completion of work submitted before this call.
    /// </summary>
    /// <param name="queue">The queue whose submitted work is observed.</param>
    /// <param name="callback">The rooted completion callback.</param>
    /// <param name="userData">The context pointer passed to the callback.</param>
    public void QueueOnSubmittedWorkDone(
        WGPUQueueImpl* queue,
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
    public void QueueWriteBuffer(WGPUQueueImpl* queue, WGPUBufferImpl* buffer, ulong offset, void* data, nuint size)
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
        WGPUQueueImpl* queue,
        in WGPUTexelCopyTextureInfo destination,
        void* data,
        nuint dataSize,
        in WGPUTexelCopyBufferLayout dataLayout,
        in WGPUExtent3D writeSize)
    {
        fixed (WGPUTexelCopyTextureInfo* destinationPtr = &destination)
        {
            fixed (WGPUTexelCopyBufferLayout* dataLayoutPtr = &dataLayout)
            {
                fixed (WGPUExtent3D* writeSizePtr = &writeSize)
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
    public void QueueSubmit(WGPUQueueImpl* queue, nuint commandCount, ref WGPUCommandBufferImpl* commands)
    {
        fixed (WGPUCommandBufferImpl** commandsPtr = &commands)
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
    public ulong QueueSubmitForIndex(WGPUQueueImpl* queue, nuint commandCount, ref WGPUCommandBufferImpl* commands)
    {
        fixed (WGPUCommandBufferImpl** commandsPtr = &commands)
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
    public WGPUComputePassEncoderImpl* CommandEncoderBeginComputePass(WGPUCommandEncoderImpl* encoder, in WGPUComputePassDescriptor descriptor)
    {
        fixed (WGPUComputePassDescriptor* descriptorPtr = &descriptor)
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
    public WGPURenderPassEncoderImpl* CommandEncoderBeginRenderPass(WGPUCommandEncoderImpl* encoder, in WGPURenderPassDescriptor descriptor)
    {
        fixed (WGPURenderPassDescriptor* descriptorPtr = &descriptor)
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
        WGPUCommandEncoderImpl* encoder,
        WGPUBufferImpl* source,
        ulong sourceOffset,
        WGPUBufferImpl* destination,
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
        WGPUCommandEncoderImpl* encoder,
        in WGPUTexelCopyTextureInfo source,
        in WGPUTexelCopyBufferInfo destination,
        in WGPUExtent3D copySize)
    {
        fixed (WGPUTexelCopyTextureInfo* sourcePtr = &source)
        {
            fixed (WGPUTexelCopyBufferInfo* destinationPtr = &destination)
            {
                fixed (WGPUExtent3D* copySizePtr = &copySize)
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
        WGPUCommandEncoderImpl* encoder,
        in WGPUTexelCopyTextureInfo source,
        in WGPUTexelCopyTextureInfo destination,
        in WGPUExtent3D copySize)
    {
        fixed (WGPUTexelCopyTextureInfo* sourcePtr = &source)
        {
            fixed (WGPUTexelCopyTextureInfo* destinationPtr = &destination)
            {
                fixed (WGPUExtent3D* copySizePtr = &copySize)
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
    public WGPUCommandBufferImpl* CommandEncoderFinish(WGPUCommandEncoderImpl* encoder, in WGPUCommandBufferDescriptor descriptor)
    {
        fixed (WGPUCommandBufferDescriptor* descriptorPtr = &descriptor)
        {
            return WebGPUNative.wgpuCommandEncoderFinish(encoder, descriptorPtr);
        }
    }

    /// <summary>
    /// Sets the active compute pipeline.
    /// </summary>
    /// <param name="encoder">The compute-pass encoder.</param>
    /// <param name="pipeline">The compute pipeline.</param>
    public void ComputePassEncoderSetPipeline(WGPUComputePassEncoderImpl* encoder, WGPUComputePipelineImpl* pipeline)
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
        WGPUComputePassEncoderImpl* encoder,
        uint groupIndex,
        WGPUBindGroupImpl* group,
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
    public void ComputePassEncoderDispatchWorkgroups(WGPUComputePassEncoderImpl* encoder, uint x, uint y, uint z)
        => WebGPUNative.wgpuComputePassEncoderDispatchWorkgroups(encoder, x, y, z);

    /// <summary>
    /// Dispatches compute workgroups using indirect counts.
    /// </summary>
    /// <param name="encoder">The compute-pass encoder.</param>
    /// <param name="indirectBuffer">The buffer containing the dispatch counts.</param>
    /// <param name="indirectOffset">The byte offset of the dispatch counts.</param>
    public void ComputePassEncoderDispatchWorkgroupsIndirect(
        WGPUComputePassEncoderImpl* encoder,
        WGPUBufferImpl* indirectBuffer,
        ulong indirectOffset)
        => WebGPUNative.wgpuComputePassEncoderDispatchWorkgroupsIndirect(encoder, indirectBuffer, indirectOffset);

    /// <summary>
    /// Ends a compute pass.
    /// </summary>
    /// <param name="encoder">The compute-pass encoder.</param>
    public void ComputePassEncoderEnd(WGPUComputePassEncoderImpl* encoder)
        => WebGPUNative.wgpuComputePassEncoderEnd(encoder);

    /// <summary>
    /// Ends a render pass.
    /// </summary>
    /// <param name="encoder">The render-pass encoder.</param>
    public void RenderPassEncoderEnd(WGPURenderPassEncoderImpl* encoder)
        => WebGPUNative.wgpuRenderPassEncoderEnd(encoder);

    /// <summary>
    /// Binds a resource group to a render pass.
    /// </summary>
    /// <param name="encoder">The render-pass encoder.</param>
    /// <param name="groupIndex">The pipeline bind-group index.</param>
    /// <param name="bindGroup">The bind group to bind.</param>
    public void RenderPassEncoderSetBindGroup(WGPURenderPassEncoderImpl* encoder, uint groupIndex, WGPUBindGroupImpl* bindGroup)
        => WebGPUNative.wgpuRenderPassEncoderSetBindGroup(encoder, groupIndex, bindGroup, 0, null);

    /// <summary>
    /// Binds a graphics pipeline to a render pass.
    /// </summary>
    /// <param name="encoder">The render-pass encoder.</param>
    /// <param name="pipeline">The graphics pipeline to bind.</param>
    public void RenderPassEncoderSetPipeline(WGPURenderPassEncoderImpl* encoder, WGPURenderPipelineImpl* pipeline)
        => WebGPUNative.wgpuRenderPassEncoderSetPipeline(encoder, pipeline);

    /// <summary>
    /// Sets the rectangular viewport used by subsequent render-pass draws.
    /// </summary>
    /// <param name="encoder">The render pass to update.</param>
    /// <param name="x">The viewport's left coordinate in attachment pixels.</param>
    /// <param name="y">The viewport's top coordinate in attachment pixels.</param>
    /// <param name="width">The viewport width in attachment pixels.</param>
    /// <param name="height">The viewport height in attachment pixels.</param>
    /// <param name="minDepth">The minimum viewport depth.</param>
    /// <param name="maxDepth">The maximum viewport depth.</param>
    public void RenderPassEncoderSetViewport(WGPURenderPassEncoderImpl* encoder, float x, float y, float width, float height, float minDepth, float maxDepth)
        => WebGPUNative.wgpuRenderPassEncoderSetViewport(encoder, x, y, width, height, minDepth, maxDepth);

    /// <summary>
    /// Restricts subsequent render-pass writes to an integer rectangle.
    /// </summary>
    /// <param name="encoder">The render pass to update.</param>
    /// <param name="x">The scissor rectangle's left coordinate in attachment pixels.</param>
    /// <param name="y">The scissor rectangle's top coordinate in attachment pixels.</param>
    /// <param name="width">The scissor rectangle width in attachment pixels.</param>
    /// <param name="height">The scissor rectangle height in attachment pixels.</param>
    public void RenderPassEncoderSetScissorRect(WGPURenderPassEncoderImpl* encoder, uint x, uint y, uint width, uint height)
        => WebGPUNative.wgpuRenderPassEncoderSetScissorRect(encoder, x, y, width, height);

    /// <summary>
    /// Draws non-indexed geometry in a render pass.
    /// </summary>
    /// <param name="encoder">The render-pass encoder.</param>
    /// <param name="vertexCount">The number of vertices per instance.</param>
    /// <param name="instanceCount">The number of instances.</param>
    /// <param name="firstVertex">The first vertex index.</param>
    /// <param name="firstInstance">The first instance index.</param>
    public void RenderPassEncoderDraw(WGPURenderPassEncoderImpl* encoder, uint vertexCount, uint instanceCount, uint firstVertex, uint firstInstance)
        => WebGPUNative.wgpuRenderPassEncoderDraw(encoder, vertexCount, instanceCount, firstVertex, firstInstance);

    /// <summary>
    /// Configures a presentation surface.
    /// </summary>
    /// <param name="surface">The surface to configure.</param>
    /// <param name="configuration">The surface configuration.</param>
    public void SurfaceConfigure(WGPUSurfaceImpl* surface, in WGPUSurfaceConfiguration configuration)
    {
        fixed (WGPUSurfaceConfiguration* configurationPtr = &configuration)
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
    public WGPUStatus SurfaceGetCapabilities(WGPUSurfaceImpl* surface, WGPUAdapterImpl* adapter, WGPUSurfaceCapabilities* capabilities)
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
    public void SurfaceGetCurrentTexture(WGPUSurfaceImpl* surface, WGPUSurfaceTexture* surfaceTexture)
        => WebGPUNative.wgpuSurfaceGetCurrentTexture(surface, surfaceTexture);

    /// <summary>
    /// Presents the surface's current texture.
    /// </summary>
    /// <param name="surface">The surface to present.</param>
    /// <returns>The presentation status.</returns>
    public WGPUStatus SurfacePresent(WGPUSurfaceImpl* surface)
        => WebGPUNative.wgpuSurfacePresent(surface);

    /// <summary>
    /// Creates a texture view.
    /// </summary>
    /// <param name="texture">The source texture.</param>
    /// <param name="descriptor">The view configuration, or <see langword="null"/> for defaults.</param>
    /// <returns>The created view.</returns>
    public WGPUTextureViewImpl* TextureCreateView(WGPUTextureImpl* texture, WGPUTextureViewDescriptor* descriptor)
        => WebGPUNative.wgpuTextureCreateView(texture, descriptor);

    /// <summary>
    /// Releases a bind group.
    /// </summary>
    /// <param name="bindGroup">The bind group to release.</param>
    public void BindGroupRelease(WGPUBindGroupImpl* bindGroup) => WebGPUNative.wgpuBindGroupRelease(bindGroup);

    /// <summary>
    /// Releases a bind-group layout.
    /// </summary>
    /// <param name="layout">The layout to release.</param>
    public void BindGroupLayoutRelease(WGPUBindGroupLayoutImpl* layout) => WebGPUNative.wgpuBindGroupLayoutRelease(layout);

    /// <summary>
    /// Releases a buffer.
    /// </summary>
    /// <param name="buffer">The buffer to release.</param>
    public void BufferRelease(WGPUBufferImpl* buffer) => WebGPUNative.wgpuBufferRelease(buffer);

    /// <summary>
    /// Returns a read-only mapped buffer range.
    /// </summary>
    /// <param name="buffer">The mapped buffer.</param>
    /// <param name="offset">The byte offset.</param>
    /// <param name="size">The byte length.</param>
    /// <returns>The first mapped byte.</returns>
    public void* BufferGetConstMappedRange(WGPUBufferImpl* buffer, nuint offset, nuint size)
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
        WGPUBufferImpl* buffer,
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
    public void BufferUnmap(WGPUBufferImpl* buffer) => WebGPUNative.wgpuBufferUnmap(buffer);

    /// <summary>
    /// Releases a command buffer.
    /// </summary>
    /// <param name="commandBuffer">The command buffer to release.</param>
    public void CommandBufferRelease(WGPUCommandBufferImpl* commandBuffer) => WebGPUNative.wgpuCommandBufferRelease(commandBuffer);

    /// <summary>
    /// Releases a command encoder.
    /// </summary>
    /// <param name="encoder">The command encoder to release.</param>
    public void CommandEncoderRelease(WGPUCommandEncoderImpl* encoder) => WebGPUNative.wgpuCommandEncoderRelease(encoder);

    /// <summary>
    /// Releases a compute-pass encoder.
    /// </summary>
    /// <param name="encoder">The encoder to release.</param>
    public void ComputePassEncoderRelease(WGPUComputePassEncoderImpl* encoder) => WebGPUNative.wgpuComputePassEncoderRelease(encoder);

    /// <summary>
    /// Releases a compute pipeline.
    /// </summary>
    /// <param name="pipeline">The pipeline to release.</param>
    public void ComputePipelineRelease(WGPUComputePipelineImpl* pipeline) => WebGPUNative.wgpuComputePipelineRelease(pipeline);

    /// <summary>
    /// Releases a pipeline layout.
    /// </summary>
    /// <param name="layout">The layout to release.</param>
    public void PipelineLayoutRelease(WGPUPipelineLayoutImpl* layout) => WebGPUNative.wgpuPipelineLayoutRelease(layout);

    /// <summary>
    /// Releases a render-pass encoder.
    /// </summary>
    /// <param name="encoder">The encoder to release.</param>
    public void RenderPassEncoderRelease(WGPURenderPassEncoderImpl* encoder) => WebGPUNative.wgpuRenderPassEncoderRelease(encoder);

    /// <summary>
    /// Releases a render pipeline.
    /// </summary>
    /// <param name="pipeline">The pipeline to release.</param>
    public void RenderPipelineRelease(WGPURenderPipelineImpl* pipeline) => WebGPUNative.wgpuRenderPipelineRelease(pipeline);

    /// <summary>
    /// Releases a shader module.
    /// </summary>
    /// <param name="module">The module to release.</param>
    public void ShaderModuleRelease(WGPUShaderModuleImpl* module) => WebGPUNative.wgpuShaderModuleRelease(module);

    /// <summary>
    /// Releases a surface.
    /// </summary>
    /// <param name="surface">The surface to release.</param>
    public void SurfaceRelease(WGPUSurfaceImpl* surface) => WebGPUNative.wgpuSurfaceRelease(surface);

    /// <summary>
    /// Releases a texture.
    /// </summary>
    /// <param name="texture">The texture to release.</param>
    public void TextureRelease(WGPUTextureImpl* texture) => WebGPUNative.wgpuTextureRelease(texture);

    /// <summary>
    /// Releases a texture view.
    /// </summary>
    /// <param name="view">The view to release.</param>
    public void TextureViewRelease(WGPUTextureViewImpl* view) => WebGPUNative.wgpuTextureViewRelease(view);

    /// <summary>
    /// Registers the process-wide wgpu-native logging callback.
    /// </summary>
    /// <param name="callback">The unmanaged logging callback.</param>
    /// <param name="userData">The context pointer passed to the callback.</param>
    public void SetLogCallback(
        delegate* unmanaged[Cdecl]<WGPULogLevel, WGPUStringView, void*, void> callback,
        void* userData)
        => WebGPUNative.wgpuSetLogCallback(callback, userData);

    /// <summary>
    /// Sets the process-wide wgpu-native log level.
    /// </summary>
    /// <param name="level">The minimum emitted log level.</param>
    public void SetLogLevel(WGPULogLevel level) => WebGPUNative.wgpuSetLogLevel(level);
}

#pragma warning restore CA1822
