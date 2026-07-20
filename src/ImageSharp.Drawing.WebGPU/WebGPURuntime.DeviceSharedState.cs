// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Collections.Concurrent;
using SixLabors.ImageSharp.Drawing.Processing.Backends.Native;

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

internal static unsafe partial class WebGPURuntime
{
    /// <summary>
    /// Maps raw native device pointers to their shared state. Keyed by the raw pointer so
    /// distinct wrapper handles over the same native device resolve to one shared state.
    /// </summary>
    private static readonly ConcurrentDictionary<nint, DeviceSharedState> DeviceStateCache = new();

    /// <summary>
    /// Serializes creation and teardown of cache entries so a state cannot be created
    /// concurrently with <see cref="ClearDeviceStateCache"/> disposing the cache.
    /// </summary>
    private static readonly object DeviceStateCacheSync = new();

    /// <summary>
    /// Gets or creates process-scoped shared resources for the specified device.
    /// </summary>
    /// <param name="api">The WebGPU API facade used to manage native resources.</param>
    /// <param name="deviceHandle">The device key and owner for the shared state.</param>
    /// <returns>The shared device state instance for <paramref name="deviceHandle"/>.</returns>
    public static DeviceSharedState GetOrCreateDeviceState(WebGPU api, WebGPUDeviceHandle deviceHandle)
    {
        nint cacheKey = deviceHandle.DangerousGetHandle();

        lock (DeviceStateCacheSync)
        {
            if (DeviceStateCache.TryGetValue(cacheKey, out DeviceSharedState? existing))
            {
                return existing;
            }

            DeviceSharedState created = new(api, deviceHandle);
            DeviceStateCache[cacheKey] = created;

            // First-ever compilation of the staged pipeline set costs multi-second driver work
            // on a cold driver shader cache. Warming in the background at device creation moves
            // that cost off the first flush; the pipeline caches are thread-safe, so an early
            // flush simply blocks on the specific pipelines it needs.
            WebGPUSceneDispatch.BeginPipelineWarmup(created);
            return created;
        }
    }

    /// <summary>
    /// Disposes all cached device-scoped shared state. Called from process-exit teardown before
    /// the runtime-owned device handles are disposed, because each state holds a reference on
    /// its device handle.
    /// </summary>
    private static void ClearDeviceStateCache()
    {
        lock (DeviceStateCacheSync)
        {
            foreach (DeviceSharedState state in DeviceStateCache.Values)
            {
                state.Dispose();
            }

            DeviceStateCache.Clear();
        }
    }

    /// <summary>
    /// Disposes and removes the cached shared state for a single device. Called when a device is
    /// being torn down, such as after device-loss recovery replaces it, so the device's cached
    /// pipelines are released and the reference the state holds on the device handle is dropped
    /// now, instead of lingering in the pointer-keyed cache until process exit.
    /// </summary>
    /// <remarks>
    /// The state must be disposed while its device is still live, because disposal releases
    /// pipelines and unregisters the error callback through the device. The caller therefore
    /// invokes this before disposing the device handle. No-op when the device was never flushed
    /// and so never created a cache entry.
    /// </remarks>
    /// <param name="deviceKey">
    /// The raw device pointer that keyed the entry, as returned by the device handle's
    /// <see cref="System.Runtime.InteropServices.SafeHandle.DangerousGetHandle"/>.
    /// </param>
    public static void RemoveDeviceState(nint deviceKey)
    {
        lock (DeviceStateCacheSync)
        {
            if (DeviceStateCache.TryRemove(deviceKey, out DeviceSharedState? state))
            {
                state.Dispose();
            }
        }
    }

    /// <summary>
    /// Marks the cached state for a native device as lost.
    /// </summary>
    /// <param name="deviceKey">The raw device pointer reported by the native callback.</param>
    public static void MarkDeviceLost(nint deviceKey)
    {
        if (DeviceStateCache.TryGetValue(deviceKey, out DeviceSharedState? state))
        {
            state.MarkLost();
        }
    }

    /// <summary>
    /// Shared device-scoped caches for pipelines and pipeline layouts.
    /// </summary>
    internal sealed partial class DeviceSharedState : IDisposable
    {
        /// <summary>
        /// Fallback storage-buffer binding ceiling (WebGPU's guaranteed minimum of 128 MiB)
        /// used when the device limit cannot be queried.
        /// </summary>
        private const nuint DefaultMaxStorageBufferBindingSize = 128U * 1024U * 1024U;

        /// <summary>
        /// Cached graphics-pipeline families keyed by pipeline key. Creation within one family
        /// is serialized by locking the family instance itself.
        /// </summary>
        private readonly ConcurrentDictionary<string, CompositePipelineInfrastructure> compositePipelines = new(StringComparer.Ordinal);

        /// <summary>
        /// Cached compute-pipeline families keyed by pipeline key. Creation within one family
        /// is serialized by locking the family instance itself.
        /// </summary>
        private readonly ConcurrentDictionary<string, CompositeComputePipelineInfrastructure> compositeComputePipelines = new(StringComparer.Ordinal);

        /// <summary>
        /// Cached layer-effect pipelines keyed by their exact generated source and binding contract.
        /// </summary>
        private readonly ConcurrentDictionary<WebGPUEffectPipelineKey, WebGPUEffectPipelineInfrastructure> effectPipelines = new();

        /// <summary>
        /// Pool of map-readable status buffers reused by deferred overflow readbacks. One buffer
        /// is needed per deferred flush; creating it fresh each time is a measurable per-frame
        /// driver cost, so retired buffers are recycled here.
        /// </summary>
        private readonly Stack<StatusReadbackBufferEntry> statusReadbackBuffers = new();

        /// <summary>
        /// Guards <see cref="statusReadbackBuffers"/>; flushes on different threads can rent
        /// and return concurrently.
        /// </summary>
        private readonly object statusReadbackSync = new();

        /// <summary>
        /// Pool of full-size transient textures (ordered ping-pong targets, presentation backdrop
        /// copies, and layer-effect working textures) reused across flushes. Creating and lazily
        /// zero-initializing these multi-megabyte textures every frame is a measurable per-frame
        /// driver and GPU cost, so retired textures are recycled here with their views.
        /// </summary>
        private readonly List<PooledTextureEntry> pooledTextures = new();

        /// <summary>
        /// Guards <see cref="pooledTextures"/>; flushes on different threads can rent and return
        /// concurrently.
        /// </summary>
        private readonly object pooledTextureSync = new();

        /// <summary>
        /// Pool of layer-effect uniform buffers reused across flushes.
        /// </summary>
        private readonly Stack<StatusReadbackBufferEntry> effectUniformBuffers = new();

        /// <summary>
        /// Guards <see cref="effectUniformBuffers"/>.
        /// </summary>
        private readonly object effectUniformSync = new();

        /// <summary>
        /// Immutable single-pixel fallback textures keyed by format and pixel bits. Scenes bind
        /// these when they contain no gradients or images; their content never changes, so they
        /// live for the device lifetime instead of being recreated per staged range.
        /// </summary>
        private readonly Dictionary<SinglePixelTextureKey, SinglePixelTextureEntry> singlePixelTextures = new();

        /// <summary>
        /// Guards <see cref="singlePixelTextures"/>.
        /// </summary>
        private readonly object singlePixelSync = new();

        /// <summary>
        /// Signals that the one-time background pipeline warm-up no longer uses this state.
        /// </summary>
        private readonly ManualResetEventSlim pipelineWarmupCompleted = new(false);

        /// <summary>
        /// Upper bound on pooled status readback buffers; returns beyond it release instead.
        /// </summary>
        private const int MaxPooledStatusReadbackBuffers = 16;

        /// <summary>
        /// Upper bound on pooled transient textures; returns beyond it release instead. The bound
        /// caps retained GPU memory when a scene stops using ordered targets or effects.
        /// </summary>
        private const int MaxPooledTextures = 12;

        /// <summary>
        /// Upper bound on pooled layer-effect uniform buffers.
        /// </summary>
        private const int MaxPooledEffectUniformBuffers = 4;

        /// <summary>
        /// Snapshot of the device features taken at construction time.
        /// </summary>
        private readonly HashSet<WGPUFeatureName> deviceFeatures;

        /// <summary>
        /// Holds one reference on the device handle for the lifetime of this state so the
        /// cached pipelines can never outlive the device pointer they were created from.
        /// </summary>
        private WebGPUHandle.HandleReference deviceReference;

        /// <summary>
        /// Tracks whether <see cref="Dispose"/> has run.
        /// </summary>
        private bool disposed;

        /// <summary>
        /// Set atomically by the native device-lost callback, which may run on any runtime thread.
        /// </summary>
        private int isLost;

        /// <summary>
        /// Initializes a new instance of the <see cref="DeviceSharedState"/> class, acquiring a
        /// device reference and snapshotting the device features and limits.
        /// </summary>
        /// <param name="api">The WebGPU API facade used to manage native resources.</param>
        /// <param name="deviceHandle">The device this state is scoped to.</param>
        public DeviceSharedState(WebGPU api, WebGPUDeviceHandle deviceHandle)
        {
            this.Api = api;
            this.deviceReference = deviceHandle.AcquireReference();

            try
            {
                this.Device = (WGPUDeviceImpl*)this.deviceReference.Handle;
                this.deviceFeatures = EnumerateDeviceFeatures(api, this.Device);
                this.MaxStorageBufferBindingSize = QueryMaxStorageBufferBindingSize(api, this.Device);

                // Every effect module exposes the same filtered layer-sampling binding. Keep one
                // immutable sampler per device so effects do not allocate and release identical
                // native sampler objects for each pass or flush.
                WGPUSamplerDescriptor effectFilteringSamplerDescriptor = new()
                {
                    addressModeU = WGPUAddressMode.ClampToEdge,
                    addressModeV = WGPUAddressMode.ClampToEdge,
                    addressModeW = WGPUAddressMode.ClampToEdge,
                    magFilter = WGPUFilterMode.Linear,
                    minFilter = WGPUFilterMode.Linear,
                    mipmapFilter = WGPUMipmapFilterMode.Nearest,
                    lodMinClamp = 0,
                    lodMaxClamp = 0,
                    maxAnisotropy = 1
                };

                this.EffectFilteringSampler = api.DeviceCreateSampler(this.Device, in effectFilteringSamplerDescriptor);
                if (this.EffectFilteringSampler is null)
                {
                    throw new InvalidOperationException("Failed to create the WebGPU layer-effect filtering sampler.");
                }
            }
            catch
            {
                // Construction failed part-way; release the device reference so the handle
                // refcount is not left permanently elevated.
                if (this.EffectFilteringSampler is not null)
                {
                    api.SamplerRelease(this.EffectFilteringSampler);
                }

                this.deviceReference.Dispose();
                throw;
            }
        }

        /// <summary>
        /// Gets the fixed entry point used by all cached composite vertex shaders.
        /// </summary>
        private static ReadOnlySpan<byte> CompositeVertexEntryPoint => "vs_main\0"u8;

        /// <summary>
        /// Gets the fixed entry point used by all cached composite fragment shaders.
        /// </summary>
        private static ReadOnlySpan<byte> CompositeFragmentEntryPoint => "fs_main\0"u8;

        /// <summary>
        /// Gets the WebGPU API instance used by this shared state.
        /// </summary>
        public WebGPU Api { get; }

        /// <summary>
        /// Gets the device associated with this shared state. The pointer stays valid for the
        /// lifetime of this instance because <see cref="deviceReference"/> pins the handle open.
        /// </summary>
        public WGPUDeviceImpl* Device { get; }

        /// <summary>
        /// Gets the maximum size, in bytes, of one usable storage scratch buffer on this device: the
        /// smaller of the queried maxStorageBufferBindingSize and maxBufferSize.
        /// </summary>
        /// <remarks>
        /// A scratch buffer must be both creatable (within maxBufferSize) and bindable in full (within
        /// maxStorageBufferBindingSize), so the effective ceiling that drives the chunking decision is the
        /// minimum of both. Using the (often larger) binding size alone lets a scene skip chunking and then
        /// fail buffer creation, surfacing as an invalid-buffer validation error and a blank frame.
        /// </remarks>
        public nuint MaxStorageBufferBindingSize { get; }

        /// <summary>
        /// Gets the linear filtering sampler shared by layer-effect shader passes on this device.
        /// </summary>
        public WGPUSamplerImpl* EffectFilteringSampler { get; }

        /// <summary>
        /// Gets a value indicating whether the native runtime has reported this device as lost.
        /// </summary>
        public bool IsLost => Volatile.Read(ref this.isLost) != 0;

        /// <summary>
        /// Returns whether the device has the specified feature.
        /// </summary>
        /// <param name="feature">The feature to check.</param>
        /// <returns><see langword="true"/> when the device has the feature; otherwise <see langword="false"/>.</returns>
        public bool HasFeature(WGPUFeatureName feature)
            => this.deviceFeatures.Contains(feature);

        /// <summary>
        /// Marks the native device as lost.
        /// </summary>
        public void MarkLost()
            => Volatile.Write(ref this.isLost, 1);

        /// <summary>
        /// Signals that background pipeline warm-up has stopped using this state.
        /// </summary>
        public void CompletePipelineWarmup()
            => this.pipelineWarmupCompleted.Set();

        /// <summary>
        /// Rents a pooled map-readable status buffer, or creates one when the pool has no buffer
        /// large enough for the requested status data.
        /// </summary>
        /// <param name="byteLength">The required scheduling-status byte length.</param>
        /// <returns>The rented buffer, or <see langword="null"/> when creation failed.</returns>
        public WGPUBufferImpl* RentStatusReadbackBuffer(nuint byteLength)
        {
            lock (this.statusReadbackSync)
            {
                Span<StatusReadbackBufferEntry> retained = stackalloc StatusReadbackBufferEntry[MaxPooledStatusReadbackBuffers];
                int retainedIndex = 0;

                while (this.statusReadbackBuffers.Count > 0)
                {
                    StatusReadbackBufferEntry entry = this.statusReadbackBuffers.Pop();
                    if (entry.ByteLength >= byteLength)
                    {
                        while (retainedIndex > 0)
                        {
                            this.statusReadbackBuffers.Push(retained[--retainedIndex]);
                        }

                        return (WGPUBufferImpl*)entry.Buffer;
                    }

                    retained[retainedIndex++] = entry;
                }

                while (retainedIndex > 0)
                {
                    this.statusReadbackBuffers.Push(retained[--retainedIndex]);
                }
            }

            WGPUBufferDescriptor descriptor = new()
            {
                usage = (ulong)(BufferUsage.CopyDst | BufferUsage.MapRead),
                size = byteLength,
                mappedAtCreation = 0U,
            };

            return this.Api.DeviceCreateBuffer(this.Device, in descriptor);
        }

        /// <summary>
        /// Returns a status readback buffer to the pool. The buffer must be unmapped with no
        /// map pending; a buffer whose map state is unknown must be released, not returned.
        /// </summary>
        /// <param name="buffer">The buffer to recycle.</param>
        /// <param name="byteLength">The byte capacity of <paramref name="buffer"/>.</param>
        public void ReturnStatusReadbackBuffer(WGPUBufferImpl* buffer, nuint byteLength)
        {
            if (buffer is null)
            {
                return;
            }

            lock (this.statusReadbackSync)
            {
                if (!this.disposed && this.statusReadbackBuffers.Count < MaxPooledStatusReadbackBuffers)
                {
                    this.statusReadbackBuffers.Push(new StatusReadbackBufferEntry((nint)buffer, byteLength));
                    return;
                }
            }

            this.Api.BufferRelease(buffer);
        }

        /// <summary>
        /// Rents a pooled transient texture whose format and usage match exactly and whose
        /// dimensions are at least the requested size.
        /// </summary>
        /// <remarks>
        /// Oversized reuse is safe for every transient-texture consumer: fine shading, layer
        /// effects, and presentation blits address these textures in texel space or normalize by
        /// the queried <c>textureDimensions</c>, never by an assumed logical size.
        /// </remarks>
        /// <param name="format">The required texture format.</param>
        /// <param name="usage">The exact usage bits the texture must have been created with.</param>
        /// <param name="width">The minimum width in texels.</param>
        /// <param name="height">The minimum height in texels.</param>
        /// <param name="texture">Receives the rented texture on success.</param>
        /// <param name="textureView">Receives the rented texture's full view on success.</param>
        /// <param name="entryWidth">Receives the rented texture's created width.</param>
        /// <param name="entryHeight">Receives the rented texture's created height.</param>
        /// <returns><see langword="true"/> when a pooled texture satisfied the request.</returns>
        public bool TryRentPooledTexture(
            WGPUTextureFormat format,
            ulong usage,
            uint width,
            uint height,
            out WGPUTextureImpl* texture,
            out WGPUTextureViewImpl* textureView,
            out uint entryWidth,
            out uint entryHeight)
        {
            lock (this.pooledTextureSync)
            {
                for (int i = 0; i < this.pooledTextures.Count; i++)
                {
                    PooledTextureEntry entry = this.pooledTextures[i];
                    if (entry.Format == format && entry.Usage == usage && entry.Width >= width && entry.Height >= height)
                    {
                        this.pooledTextures.RemoveAt(i);
                        texture = (WGPUTextureImpl*)entry.Texture;
                        textureView = (WGPUTextureViewImpl*)entry.View;
                        entryWidth = entry.Width;
                        entryHeight = entry.Height;
                        return true;
                    }
                }
            }

            texture = null;
            textureView = null;
            entryWidth = 0;
            entryHeight = 0;
            return false;
        }

        /// <summary>
        /// Returns a transient texture and its view to the pool, or releases both when the pool
        /// is full or this state has been disposed.
        /// </summary>
        /// <remarks>
        /// Returning while previously submitted commands still reference the texture is safe:
        /// reuse happens through later submissions on the same queue, which execute after the
        /// earlier commands retire, and native command buffers hold their own references.
        /// </remarks>
        /// <param name="texture">The texture to recycle.</param>
        /// <param name="textureView">The full view created with <paramref name="texture"/>.</param>
        /// <param name="format">The texture format it was created with.</param>
        /// <param name="usage">The exact usage bits it was created with.</param>
        /// <param name="width">The created width in texels.</param>
        /// <param name="height">The created height in texels.</param>
        public void ReturnPooledTexture(
            WGPUTextureImpl* texture,
            WGPUTextureViewImpl* textureView,
            WGPUTextureFormat format,
            ulong usage,
            uint width,
            uint height)
        {
            if (texture is null)
            {
                return;
            }

            // Entries strictly dominated by the returning texture can never be preferred over it,
            // so releasing them here keeps superseded sizes from accumulating after a resize.
            // Equal-size entries are kept: ping-pong targets rent two identical textures at once.
            Span<nint> released = stackalloc nint[2 * MaxPooledTextures];
            int releasedCount = 0;

            lock (this.pooledTextureSync)
            {
                if (!this.disposed)
                {
                    for (int i = this.pooledTextures.Count - 1; i >= 0; i--)
                    {
                        PooledTextureEntry entry = this.pooledTextures[i];
                        if (entry.Format == format &&
                            entry.Usage == usage &&
                            entry.Width <= width &&
                            entry.Height <= height &&
                            (entry.Width < width || entry.Height < height))
                        {
                            released[releasedCount++] = entry.View;
                            released[releasedCount++] = entry.Texture;
                            this.pooledTextures.RemoveAt(i);
                        }
                    }
                }

                if (!this.disposed && this.pooledTextures.Count < MaxPooledTextures)
                {
                    this.pooledTextures.Add(new PooledTextureEntry((nint)texture, (nint)textureView, format, usage, width, height));
                    texture = null;
                }
            }

            // Native releases happen outside the lock; the entries are already unreachable.
            for (int i = 0; i < releasedCount; i += 2)
            {
                if (released[i] != 0)
                {
                    this.Api.TextureViewRelease((WGPUTextureViewImpl*)released[i]);
                }

                this.Api.TextureRelease((WGPUTextureImpl*)released[i + 1]);
            }

            if (texture is not null)
            {
                if (textureView is not null)
                {
                    this.Api.TextureViewRelease(textureView);
                }

                this.Api.TextureRelease(texture);
            }
        }

        /// <summary>
        /// Rents a pooled layer-effect uniform buffer, or creates one when the pool has no
        /// buffer of at least the requested capacity.
        /// </summary>
        /// <param name="byteLength">The required uniform capacity in bytes.</param>
        /// <returns>The rented buffer, or <see langword="null"/> when creation failed.</returns>
        public WGPUBufferImpl* RentEffectUniformBuffer(nuint byteLength)
        {
            lock (this.effectUniformSync)
            {
                Span<StatusReadbackBufferEntry> retained = stackalloc StatusReadbackBufferEntry[MaxPooledEffectUniformBuffers];
                int retainedIndex = 0;

                while (this.effectUniformBuffers.Count > 0)
                {
                    StatusReadbackBufferEntry entry = this.effectUniformBuffers.Pop();
                    if (entry.ByteLength >= byteLength)
                    {
                        while (retainedIndex > 0)
                        {
                            this.effectUniformBuffers.Push(retained[--retainedIndex]);
                        }

                        return (WGPUBufferImpl*)entry.Buffer;
                    }

                    retained[retainedIndex++] = entry;
                }

                while (retainedIndex > 0)
                {
                    this.effectUniformBuffers.Push(retained[--retainedIndex]);
                }
            }

            WGPUBufferDescriptor descriptor = new()
            {
                usage = (ulong)(BufferUsage.Uniform | BufferUsage.CopyDst),
                size = byteLength
            };

            return this.Api.DeviceCreateBuffer(this.Device, in descriptor);
        }

        /// <summary>
        /// Returns a layer-effect uniform buffer to the pool, or releases it when the pool is
        /// full or this state has been disposed.
        /// </summary>
        /// <param name="buffer">The buffer to recycle.</param>
        /// <param name="byteLength">The byte capacity of <paramref name="buffer"/>.</param>
        public void ReturnEffectUniformBuffer(WGPUBufferImpl* buffer, nuint byteLength)
        {
            if (buffer is null)
            {
                return;
            }

            lock (this.effectUniformSync)
            {
                if (!this.disposed && this.effectUniformBuffers.Count < MaxPooledEffectUniformBuffers)
                {
                    this.effectUniformBuffers.Push(new StatusReadbackBufferEntry((nint)buffer, byteLength));
                    return;
                }
            }

            this.Api.BufferRelease(buffer);
        }

        /// <summary>
        /// Gets or creates the immutable single-pixel fallback texture for the supplied format
        /// and raw pixel bits. The returned view is owned by this state for the device lifetime
        /// and must not be released or flush-tracked by callers.
        /// </summary>
        /// <param name="queue">The queue used to upload the pixel on first creation.</param>
        /// <param name="format">The texture format.</param>
        /// <param name="pixelBits">The raw little-endian pixel bytes, at most 16.</param>
        /// <param name="textureView">Receives the cached texture view on success.</param>
        /// <returns><see langword="true"/> when the fallback texture is available.</returns>
        public bool TryGetOrCreateSinglePixelTexture(
            WGPUQueueImpl* queue,
            WGPUTextureFormat format,
            ReadOnlySpan<byte> pixelBits,
            out WGPUTextureViewImpl* textureView)
        {
            Span<byte> keyBits = stackalloc byte[16];
            keyBits.Clear();
            pixelBits.CopyTo(keyBits);
            SinglePixelTextureKey key = new(
                format,
                BitConverter.ToUInt64(keyBits),
                BitConverter.ToUInt64(keyBits[8..]),
                pixelBits.Length);

            lock (this.singlePixelSync)
            {
                ObjectDisposedException.ThrowIf(this.disposed, this);

                if (this.singlePixelTextures.TryGetValue(key, out SinglePixelTextureEntry entry))
                {
                    textureView = (WGPUTextureViewImpl*)entry.View;
                    return true;
                }

                WGPUTextureDescriptor textureDescriptor = new()
                {
                    usage = (ulong)(TextureUsage.TextureBinding | TextureUsage.CopyDst),
                    dimension = WGPUTextureDimension._2D,
                    size = new WGPUExtent3D(1, 1, 1),
                    format = format,
                    mipLevelCount = 1,
                    sampleCount = 1
                };

                WGPUTextureImpl* texture = this.Api.DeviceCreateTexture(this.Device, in textureDescriptor);
                if (texture is null)
                {
                    textureView = null;
                    return false;
                }

                WGPUTextureViewDescriptor viewDescriptor = new()
                {
                    format = format,
                    dimension = WGPUTextureViewDimension._2D,
                    baseMipLevel = 0,
                    mipLevelCount = 1,
                    baseArrayLayer = 0,
                    arrayLayerCount = 1,
                    aspect = WGPUTextureAspect.All
                };

                WGPUTextureViewImpl* view = this.Api.TextureCreateView(texture, &viewDescriptor);
                if (view is null)
                {
                    this.Api.TextureRelease(texture);
                    textureView = null;
                    return false;
                }

                WGPUTexelCopyTextureInfo destination = new()
                {
                    texture = texture,
                    mipLevel = 0,
                    origin = new WGPUOrigin3D(0, 0, 0),
                    aspect = WGPUTextureAspect.All
                };

                WGPUTexelCopyBufferLayout layout = new()
                {
                    offset = 0,
                    bytesPerRow = (uint)pixelBits.Length,
                    rowsPerImage = 1
                };

                WGPUExtent3D extent = new(1, 1, 1);
                fixed (byte* pixelPtr = pixelBits)
                {
                    this.Api.QueueWriteTexture(queue, in destination, pixelPtr, (nuint)pixelBits.Length, in layout, in extent);
                }

                this.singlePixelTextures[key] = new SinglePixelTextureEntry((nint)texture, (nint)view);
                textureView = view;
                return true;
            }
        }

        /// <summary>
        /// Snapshots the feature set currently reported by the native device.
        /// </summary>
        /// <param name="api">The WebGPU API facade.</param>
        /// <param name="device">The device to query.</param>
        /// <returns>The set of features the device reports; empty when the device is <see langword="null"/> or reports none.</returns>
        private static HashSet<WGPUFeatureName> EnumerateDeviceFeatures(WebGPU api, WGPUDeviceImpl* device)
        {
            if (device is null)
            {
                return [];
            }

            WGPUSupportedFeatures supportedFeatures = default;
            api.DeviceGetFeatures(device, &supportedFeatures);
            if (supportedFeatures.featureCount == 0)
            {
                return [];
            }

            HashSet<WGPUFeatureName> result = new((int)supportedFeatures.featureCount);
            try
            {
                for (nuint i = 0; i < supportedFeatures.featureCount; i++)
                {
                    result.Add(supportedFeatures.features[i]);
                }
            }
            finally
            {
                // The feature array belongs to the native query result and must be returned through
                // the matching free-members function after the managed snapshot has been copied.
                api.SupportedFeaturesFreeMembers(supportedFeatures);
            }

            return result;
        }

        /// <summary>
        /// Queries the device's storage-buffer binding ceiling, falling back to WebGPU's guaranteed minimum when unavailable.
        /// </summary>
        /// <param name="api">The WebGPU API facade.</param>
        /// <param name="device">The device to query.</param>
        /// <returns>The maximum storage-buffer binding size in bytes.</returns>
        private static nuint QueryMaxStorageBufferBindingSize(WebGPU api, WGPUDeviceImpl* device)
        {
            if (device is null)
            {
                return DefaultMaxStorageBufferBindingSize;
            }

            WGPULimits supportedLimits = default;
            if (api.DeviceGetLimits(device, &supportedLimits) != WGPUStatus.Success)
            {
                return DefaultMaxStorageBufferBindingSize;
            }

            // A scratch buffer must be both creatable (<= maxBufferSize) and bindable in full
            // (<= maxStorageBufferBindingSize). maxBufferSize is frequently the smaller of the two
            // (256 MiB by default versus a multi-gigabyte binding size), so the effective per-buffer
            // ceiling that the chunking decision must respect is the minimum of both. Using the binding
            // size alone lets a large scene skip chunking and then fail buffer creation, which surfaces
            // as an invalid-buffer validation error and a blank frame.
            ulong binding = supportedLimits.maxStorageBufferBindingSize;
            ulong bufferSize = supportedLimits.maxBufferSize;
            ulong reported = Math.Min(binding == 0 ? ulong.MaxValue : binding, bufferSize == 0 ? ulong.MaxValue : bufferSize);
            if (reported == 0 || reported == ulong.MaxValue || reported > nuint.MaxValue)
            {
                return DefaultMaxStorageBufferBindingSize;
            }

            return (nuint)reported;
        }

        /// <summary>
        /// Gets or creates a graphics pipeline used for composite rendering. Pipeline variants
        /// are cached per (texture format, blend mode) within the family identified by
        /// <paramref name="pipelineKey"/>.
        /// </summary>
        /// <param name="pipelineKey">The stable key identifying the pipeline family.</param>
        /// <param name="shaderCode">Null-terminated WGSL source, used only when the family's shader module has not been created yet.</param>
        /// <param name="bindGroupLayoutFactory">Creates the family's bind-group layout on first use.</param>
        /// <param name="textureFormat">The color-target format for the requested variant.</param>
        /// <param name="blendMode">The blend mode for the requested variant.</param>
        /// <param name="bindGroupLayout">Receives the family's shared bind-group layout on success.</param>
        /// <param name="pipeline">Receives the cached or newly created render pipeline on success.</param>
        /// <param name="error">Receives the failure description when creation fails.</param>
        /// <returns><see langword="true"/> when the pipeline is available; otherwise <see langword="false"/>.</returns>
        public bool TryGetOrCreateCompositePipeline(
            string pipelineKey,
            ReadOnlySpan<byte> shaderCode,
            WebGPUCompositeBindGroupLayoutFactory bindGroupLayoutFactory,
            WGPUTextureFormat textureFormat,
            CompositePipelineBlendMode blendMode,
            out WGPUBindGroupLayoutImpl* bindGroupLayout,
            out WGPURenderPipelineImpl* pipeline,
            out string? error)
        {
            bindGroupLayout = null;
            pipeline = null;

            ObjectDisposedException.ThrowIf(this.disposed, this);

            CompositePipelineInfrastructure infrastructure = this.compositePipelines.GetOrAdd(
                pipelineKey,
                static _ => new CompositePipelineInfrastructure());

            lock (infrastructure)
            {
                if (infrastructure.BindGroupLayout is null ||
                    infrastructure.PipelineLayout is null ||
                    infrastructure.ShaderModule is null)
                {
                    if (!this.TryCreateCompositeInfrastructure(
                            shaderCode,
                            bindGroupLayoutFactory,
                            out WGPUBindGroupLayoutImpl* createdBindGroupLayout,
                            out WGPUPipelineLayoutImpl* createdPipelineLayout,
                            out WGPUShaderModuleImpl* createdShaderModule,
                            out error))
                    {
                        return false;
                    }

                    infrastructure.BindGroupLayout = createdBindGroupLayout;
                    infrastructure.PipelineLayout = createdPipelineLayout;
                    infrastructure.ShaderModule = createdShaderModule;
                }

                bindGroupLayout = infrastructure.BindGroupLayout;
                (WGPUTextureFormat TextureFormat, CompositePipelineBlendMode BlendMode) variantKey = (textureFormat, blendMode);
                if (infrastructure.Pipelines.TryGetValue(variantKey, out nint cachedPipelineHandle) && cachedPipelineHandle != 0)
                {
                    pipeline = (WGPURenderPipelineImpl*)cachedPipelineHandle;
                    error = null;
                    return true;
                }

                WGPURenderPipelineImpl* createdPipeline = this.CreateCompositePipeline(
                    infrastructure.PipelineLayout,
                    infrastructure.ShaderModule,
                    textureFormat,
                    blendMode);
                if (createdPipeline is null)
                {
                    error = $"Failed to create composite pipeline '{pipelineKey}' for format '{textureFormat}'.";
                    return false;
                }

                infrastructure.Pipelines[variantKey] = (nint)createdPipeline;
                pipeline = createdPipeline;
                error = null;
                return true;
            }
        }

        /// <summary>
        /// Gets or creates a compute pipeline used for composite execution. One pipeline is
        /// cached per family, so <paramref name="shaderCode"/> and <paramref name="entryPoint"/>
        /// are only consumed on first creation; later calls must pass the same values for the
        /// same <paramref name="pipelineKey"/>.
        /// </summary>
        /// <param name="pipelineKey">The stable key identifying the pipeline family.</param>
        /// <param name="shaderCode">Null-terminated WGSL source, used only when the family's shader module has not been created yet.</param>
        /// <param name="entryPoint">Null-terminated compute entry-point name, used only on first creation.</param>
        /// <param name="bindGroupLayoutFactory">Creates the family's bind-group layout on first use.</param>
        /// <param name="bindGroupLayout">Receives the family's shared bind-group layout on success.</param>
        /// <param name="pipeline">Receives the cached or newly created compute pipeline on success.</param>
        /// <param name="error">Receives the failure description when creation fails.</param>
        /// <returns><see langword="true"/> when the pipeline is available; otherwise <see langword="false"/>.</returns>
        public bool TryGetOrCreateCompositeComputePipeline(
            string pipelineKey,
            ReadOnlySpan<byte> shaderCode,
            ReadOnlySpan<byte> entryPoint,
            WebGPUCompositeBindGroupLayoutFactory bindGroupLayoutFactory,
            out WGPUBindGroupLayoutImpl* bindGroupLayout,
            out WGPUComputePipelineImpl* pipeline,
            out string? error)
        {
            bindGroupLayout = null;
            pipeline = null;

            ObjectDisposedException.ThrowIf(this.disposed, this);

            CompositeComputePipelineInfrastructure infrastructure = this.compositeComputePipelines.GetOrAdd(
                pipelineKey,
                static _ => new CompositeComputePipelineInfrastructure());

            lock (infrastructure)
            {
                if (infrastructure.BindGroupLayout is null ||
                    infrastructure.PipelineLayout is null ||
                    infrastructure.ShaderModule is null)
                {
                    if (!this.TryCreateCompositeInfrastructure(
                            shaderCode,
                            bindGroupLayoutFactory,
                            out WGPUBindGroupLayoutImpl* createdBindGroupLayout,
                            out WGPUPipelineLayoutImpl* createdPipelineLayout,
                            out WGPUShaderModuleImpl* createdShaderModule,
                            out error))
                    {
                        return false;
                    }

                    infrastructure.BindGroupLayout = createdBindGroupLayout;
                    infrastructure.PipelineLayout = createdPipelineLayout;
                    infrastructure.ShaderModule = createdShaderModule;
                }

                bindGroupLayout = infrastructure.BindGroupLayout;
                if (infrastructure.Pipeline is not null)
                {
                    pipeline = infrastructure.Pipeline;
                    error = null;
                    return true;
                }

                WGPUComputePipelineImpl* createdPipeline = this.CreateCompositeComputePipeline(
                    infrastructure.PipelineLayout,
                    infrastructure.ShaderModule,
                    entryPoint);
                if (createdPipeline is null)
                {
                    error = $"Failed to create composite compute pipeline '{pipelineKey}'.";
                    return false;
                }

                infrastructure.Pipeline = createdPipeline;
                pipeline = createdPipeline;
                error = null;
                return true;
            }
        }

        /// <summary>
        /// Releases all cached pipelines owned by this state, unregisters the uncaptured-error
        /// callback, and drops the device reference. Invoked under the runtime's cache lock via
        /// <see cref="ClearDeviceStateCache"/>; not safe to run concurrently with pipeline creation.
        /// </summary>
        public void Dispose()
        {
            if (this.disposed)
            {
                return;
            }

            // Warm-up creates entries through the same pipeline caches as a foreground flush.
            // Wait before enumerating and releasing them so teardown never races their creation.
            this.pipelineWarmupCompleted.Wait();

            // Mark disposed before draining the pools: every Return* method checks the flag
            // under its pool lock and releases instead of pooling, so a return racing this
            // teardown cannot re-add an entry to an already-drained pool and leak it.
            this.disposed = true;

            foreach (CompositePipelineInfrastructure infrastructure in this.compositePipelines.Values)
            {
                this.ReleaseCompositeInfrastructure(infrastructure);
            }

            this.compositePipelines.Clear();

            foreach (CompositeComputePipelineInfrastructure infrastructure in this.compositeComputePipelines.Values)
            {
                this.ReleaseCompositeComputeInfrastructure(infrastructure);
            }

            this.compositeComputePipelines.Clear();

            foreach (WebGPUEffectPipelineInfrastructure infrastructure in this.effectPipelines.Values)
            {
                this.ReleaseEffectPipelineInfrastructure(infrastructure);
            }

            this.effectPipelines.Clear();

            this.Api.SamplerRelease(this.EffectFilteringSampler);

            lock (this.statusReadbackSync)
            {
                while (this.statusReadbackBuffers.Count > 0)
                {
                    this.Api.BufferRelease((WGPUBufferImpl*)this.statusReadbackBuffers.Pop().Buffer);
                }
            }

            lock (this.pooledTextureSync)
            {
                for (int i = 0; i < this.pooledTextures.Count; i++)
                {
                    PooledTextureEntry entry = this.pooledTextures[i];
                    if (entry.View != 0)
                    {
                        this.Api.TextureViewRelease((WGPUTextureViewImpl*)entry.View);
                    }

                    this.Api.TextureRelease((WGPUTextureImpl*)entry.Texture);
                }

                this.pooledTextures.Clear();
            }

            lock (this.effectUniformSync)
            {
                while (this.effectUniformBuffers.Count > 0)
                {
                    this.Api.BufferRelease((WGPUBufferImpl*)this.effectUniformBuffers.Pop().Buffer);
                }
            }

            lock (this.singlePixelSync)
            {
                foreach (SinglePixelTextureEntry entry in this.singlePixelTextures.Values)
                {
                    if (entry.View != 0)
                    {
                        this.Api.TextureViewRelease((WGPUTextureViewImpl*)entry.View);
                    }

                    this.Api.TextureRelease((WGPUTextureImpl*)entry.Texture);
                }

                this.singlePixelTextures.Clear();
            }

            // Drop the device reference last: every release above calls into the device.
            this.deviceReference.Dispose();
            this.pipelineWarmupCompleted.Dispose();
        }

        /// <summary>
        /// Creates the shared bind-group layout, pipeline layout, and shader module for one cached pipeline family.
        /// On failure every partially created object is released before returning.
        /// </summary>
        /// <param name="shaderCode">Null-terminated WGSL source for the family's shader module.</param>
        /// <param name="bindGroupLayoutFactory">Creates the family's bind-group layout.</param>
        /// <param name="bindGroupLayout">Receives the created bind-group layout on success.</param>
        /// <param name="pipelineLayout">Receives the created pipeline layout on success.</param>
        /// <param name="shaderModule">Receives the created shader module on success.</param>
        /// <param name="error">Receives the failure description when creation fails.</param>
        /// <returns><see langword="true"/> when all three objects were created; otherwise <see langword="false"/>.</returns>
        private bool TryCreateCompositeInfrastructure(
            ReadOnlySpan<byte> shaderCode,
            WebGPUCompositeBindGroupLayoutFactory bindGroupLayoutFactory,
            out WGPUBindGroupLayoutImpl* bindGroupLayout,
            out WGPUPipelineLayoutImpl* pipelineLayout,
            out WGPUShaderModuleImpl* shaderModule,
            out string? error)
        {
            bindGroupLayout = null;
            pipelineLayout = null;
            shaderModule = null;

            if (!bindGroupLayoutFactory(this.Api, this.Device, out bindGroupLayout, out error))
            {
                return false;
            }

            WGPUBindGroupLayoutImpl** bindGroupLayouts = stackalloc WGPUBindGroupLayoutImpl*[1];
            bindGroupLayouts[0] = bindGroupLayout;
            WGPUPipelineLayoutDescriptor pipelineLayoutDescriptor = new()
            {
                bindGroupLayoutCount = 1,
                bindGroupLayouts = bindGroupLayouts
            };

            pipelineLayout = this.Api.DeviceCreatePipelineLayout(this.Device, in pipelineLayoutDescriptor);
            if (pipelineLayout is null)
            {
                this.Api.BindGroupLayoutRelease(bindGroupLayout);
                error = "Failed to create composite pipeline layout.";
                return false;
            }

            shaderModule = this.CreateShaderModule(shaderCode);

            if (shaderModule is null)
            {
                this.Api.PipelineLayoutRelease(pipelineLayout);
                this.Api.BindGroupLayoutRelease(bindGroupLayout);
                error = "Failed to create composite shader module.";
                return false;
            }

            error = null;
            return true;
        }

        /// <summary>
        /// Creates one graphics pipeline variant for the specified output format and blend mode.
        /// </summary>
        /// <param name="pipelineLayout">The family's shared pipeline layout.</param>
        /// <param name="shaderModule">The family's shared shader module.</param>
        /// <param name="textureFormat">The color-target format for the variant.</param>
        /// <param name="blendMode">The blend mode for the variant.</param>
        /// <returns>The created render pipeline, or <see langword="null"/> on failure.</returns>
        private WGPURenderPipelineImpl* CreateCompositePipeline(
            WGPUPipelineLayoutImpl* pipelineLayout,
            WGPUShaderModuleImpl* shaderModule,
            WGPUTextureFormat textureFormat,
            CompositePipelineBlendMode blendMode)
        {
            ReadOnlySpan<byte> vertexEntryPoint = CompositeVertexEntryPoint;
            ReadOnlySpan<byte> fragmentEntryPoint = CompositeFragmentEntryPoint;
            fixed (byte* vertexEntryPointPtr = vertexEntryPoint)
            {
                fixed (byte* fragmentEntryPointPtr = fragmentEntryPoint)
                {
                    return this.CreateCompositePipelineCore(
                        pipelineLayout,
                        shaderModule,
                        vertexEntryPointPtr,
                        fragmentEntryPointPtr,
                        textureFormat,
                        blendMode);
                }
            }
        }

        /// <summary>
        /// Creates the underlying render pipeline once the shared shader module and entry points are fixed.
        /// </summary>
        /// <param name="pipelineLayout">The family's shared pipeline layout.</param>
        /// <param name="shaderModule">The family's shared shader module.</param>
        /// <param name="vertexEntryPointPtr">Pinned pointer to the null-terminated vertex entry-point name.</param>
        /// <param name="fragmentEntryPointPtr">Pinned pointer to the null-terminated fragment entry-point name.</param>
        /// <param name="textureFormat">The color-target format for the variant.</param>
        /// <param name="blendMode">The blend mode for the variant.</param>
        /// <returns>The created render pipeline, or <see langword="null"/> on failure.</returns>
        private WGPURenderPipelineImpl* CreateCompositePipelineCore(
            WGPUPipelineLayoutImpl* pipelineLayout,
            WGPUShaderModuleImpl* shaderModule,
            byte* vertexEntryPointPtr,
            byte* fragmentEntryPointPtr,
            WGPUTextureFormat textureFormat,
            CompositePipelineBlendMode blendMode)
        {
            _ = blendMode;
            WGPUVertexState vertexState = new()
            {
                module = shaderModule,
                entryPoint = vertexEntryPointPtr,
                bufferCount = 0,
                buffers = null
            };

            WGPUColorTargetState* colorTargets = stackalloc WGPUColorTargetState[1];
            colorTargets[0] = new WGPUColorTargetState
            {
                format = textureFormat,
                blend = null,
                writeMask = (ulong)ColorWriteMask.All
            };

            WGPUFragmentState fragmentState = new()
            {
                module = shaderModule,
                entryPoint = fragmentEntryPointPtr,
                targetCount = 1,
                targets = colorTargets
            };

            WGPURenderPipelineDescriptor descriptor = new()
            {
                layout = pipelineLayout,
                vertex = vertexState,
                primitive = new WGPUPrimitiveState
                {
                    topology = WGPUPrimitiveTopology.TriangleList,
                    stripIndexFormat = WGPUIndexFormat.Undefined,
                    frontFace = WGPUFrontFace.CCW,
                    cullMode = WGPUCullMode.None
                },
                depthStencil = null,
                multisample = new WGPUMultisampleState
                {
                    count = 1,
                    mask = uint.MaxValue,
                    alphaToCoverageEnabled = 0U,
                },
                fragment = &fragmentState
            };

            return this.Api.DeviceCreateRenderPipeline(this.Device, in descriptor);
        }

        /// <summary>
        /// Creates the compute pipeline used by one cached composite compute shader.
        /// </summary>
        /// <param name="pipelineLayout">The family's shared pipeline layout.</param>
        /// <param name="shaderModule">The family's shared shader module.</param>
        /// <param name="entryPoint">Null-terminated compute entry-point name.</param>
        /// <returns>The created compute pipeline, or <see langword="null"/> on failure.</returns>
        private WGPUComputePipelineImpl* CreateCompositeComputePipeline(
            WGPUPipelineLayoutImpl* pipelineLayout,
            WGPUShaderModuleImpl* shaderModule,
            ReadOnlySpan<byte> entryPoint)
        {
            fixed (byte* entryPointPtr = entryPoint)
            {
                WGPUComputeState computeState = new()
                {
                    module = shaderModule,
                    entryPoint = entryPointPtr
                };

                WGPUComputePipelineDescriptor descriptor = new()
                {
                    layout = pipelineLayout,
                    compute = computeState
                };

                return this.Api.DeviceCreateComputePipeline(this.Device, in descriptor);
            }
        }

        /// <summary>
        /// Creates a shader module from null-terminated WGSL source bytes.
        /// </summary>
        /// <param name="shaderCode">Null-terminated WGSL source bytes.</param>
        /// <returns>The created shader module, or <see langword="null"/> on failure.</returns>
        private WGPUShaderModuleImpl* CreateShaderModule(ReadOnlySpan<byte> shaderCode)
        {
            fixed (byte* shaderCodePtr = shaderCode)
            {
                WGPUShaderSourceWGSL wgslDescriptor = new()
                {
                    chain = new WGPUChainedStruct { sType = WGPUSType.ShaderSourceWGSL },
                    code = shaderCodePtr
                };

                WGPUShaderModuleDescriptor shaderDescriptor = new()
                {
                    nextInChain = (WGPUChainedStruct*)&wgslDescriptor
                };

                return this.Api.DeviceCreateShaderModule(this.Device, in shaderDescriptor);
            }
        }

        /// <summary>
        /// Releases one cached graphics-pipeline family and every render pipeline variant it owns.
        /// </summary>
        /// <param name="infrastructure">The pipeline family to release.</param>
        private void ReleaseCompositeInfrastructure(CompositePipelineInfrastructure infrastructure)
        {
            foreach (nint pipelineHandle in infrastructure.Pipelines.Values)
            {
                if (pipelineHandle != 0)
                {
                    this.Api.RenderPipelineRelease((WGPURenderPipelineImpl*)pipelineHandle);
                }
            }

            infrastructure.Pipelines.Clear();

            if (infrastructure.PipelineLayout is not null)
            {
                this.Api.PipelineLayoutRelease(infrastructure.PipelineLayout);
                infrastructure.PipelineLayout = null;
            }

            if (infrastructure.ShaderModule is not null)
            {
                this.Api.ShaderModuleRelease(infrastructure.ShaderModule);
                infrastructure.ShaderModule = null;
            }

            if (infrastructure.BindGroupLayout is not null)
            {
                this.Api.BindGroupLayoutRelease(infrastructure.BindGroupLayout);
                infrastructure.BindGroupLayout = null;
            }
        }

        /// <summary>
        /// Releases one cached compute-pipeline family and the shared resources behind it.
        /// </summary>
        /// <param name="infrastructure">The pipeline family to release.</param>
        private void ReleaseCompositeComputeInfrastructure(CompositeComputePipelineInfrastructure infrastructure)
        {
            if (infrastructure.Pipeline is not null)
            {
                this.Api.ComputePipelineRelease(infrastructure.Pipeline);
                infrastructure.Pipeline = null;
            }

            if (infrastructure.PipelineLayout is not null)
            {
                this.Api.PipelineLayoutRelease(infrastructure.PipelineLayout);
                infrastructure.PipelineLayout = null;
            }

            if (infrastructure.ShaderModule is not null)
            {
                this.Api.ShaderModuleRelease(infrastructure.ShaderModule);
                infrastructure.ShaderModule = null;
            }

            if (infrastructure.BindGroupLayout is not null)
            {
                this.Api.BindGroupLayoutRelease(infrastructure.BindGroupLayout);
                infrastructure.BindGroupLayout = null;
            }
        }

        /// <summary>
        /// Stores one pooled transient texture with the creation parameters that gate its reuse.
        /// </summary>
        private readonly struct PooledTextureEntry
        {
            /// <summary>
            /// Initializes a new instance of the <see cref="PooledTextureEntry"/> struct.
            /// </summary>
            /// <param name="texture">The native texture pointer stored as an integer.</param>
            /// <param name="view">The native full-texture view pointer stored as an integer.</param>
            /// <param name="format">The texture format it was created with.</param>
            /// <param name="usage">The exact usage bits it was created with.</param>
            /// <param name="width">The created width in texels.</param>
            /// <param name="height">The created height in texels.</param>
            public PooledTextureEntry(nint texture, nint view, WGPUTextureFormat format, ulong usage, uint width, uint height)
            {
                this.Texture = texture;
                this.View = view;
                this.Format = format;
                this.Usage = usage;
                this.Width = width;
                this.Height = height;
            }

            /// <summary>
            /// Gets the native texture pointer stored as an integer.
            /// </summary>
            public nint Texture { get; }

            /// <summary>
            /// Gets the native full-texture view pointer stored as an integer.
            /// </summary>
            public nint View { get; }

            /// <summary>
            /// Gets the texture format the texture was created with.
            /// </summary>
            public WGPUTextureFormat Format { get; }

            /// <summary>
            /// Gets the exact usage bits the texture was created with.
            /// </summary>
            public ulong Usage { get; }

            /// <summary>
            /// Gets the created width in texels.
            /// </summary>
            public uint Width { get; }

            /// <summary>
            /// Gets the created height in texels.
            /// </summary>
            public uint Height { get; }
        }

        /// <summary>
        /// Identifies one immutable single-pixel fallback texture by format and raw pixel bits.
        /// </summary>
        private readonly struct SinglePixelTextureKey : IEquatable<SinglePixelTextureKey>
        {
            /// <summary>
            /// Initializes a new instance of the <see cref="SinglePixelTextureKey"/> struct.
            /// </summary>
            /// <param name="format">The texture format.</param>
            /// <param name="pixelLow">The first eight raw pixel bytes.</param>
            /// <param name="pixelHigh">The second eight raw pixel bytes, zero-padded.</param>
            /// <param name="pixelByteLength">The raw pixel byte length.</param>
            public SinglePixelTextureKey(WGPUTextureFormat format, ulong pixelLow, ulong pixelHigh, int pixelByteLength)
            {
                this.Format = format;
                this.PixelLow = pixelLow;
                this.PixelHigh = pixelHigh;
                this.PixelByteLength = pixelByteLength;
            }

            /// <summary>
            /// Gets the texture format.
            /// </summary>
            public WGPUTextureFormat Format { get; }

            /// <summary>
            /// Gets the first eight raw pixel bytes.
            /// </summary>
            public ulong PixelLow { get; }

            /// <summary>
            /// Gets the second eight raw pixel bytes, zero-padded.
            /// </summary>
            public ulong PixelHigh { get; }

            /// <summary>
            /// Gets the raw pixel byte length.
            /// </summary>
            public int PixelByteLength { get; }

            /// <inheritdoc />
            public bool Equals(SinglePixelTextureKey other)
                => this.Format == other.Format &&
                   this.PixelLow == other.PixelLow &&
                   this.PixelHigh == other.PixelHigh &&
                   this.PixelByteLength == other.PixelByteLength;

            /// <inheritdoc />
            public override bool Equals(object? obj)
                => obj is SinglePixelTextureKey other && this.Equals(other);

            /// <inheritdoc />
            public override int GetHashCode()
                => HashCode.Combine(this.Format, this.PixelLow, this.PixelHigh, this.PixelByteLength);
        }

        /// <summary>
        /// Stores one immutable single-pixel fallback texture and its view.
        /// </summary>
        private readonly struct SinglePixelTextureEntry
        {
            /// <summary>
            /// Initializes a new instance of the <see cref="SinglePixelTextureEntry"/> struct.
            /// </summary>
            /// <param name="texture">The native texture pointer stored as an integer.</param>
            /// <param name="view">The native view pointer stored as an integer.</param>
            public SinglePixelTextureEntry(nint texture, nint view)
            {
                this.Texture = texture;
                this.View = view;
            }

            /// <summary>
            /// Gets the native texture pointer stored as an integer.
            /// </summary>
            public nint Texture { get; }

            /// <summary>
            /// Gets the native view pointer stored as an integer.
            /// </summary>
            public nint View { get; }
        }

        /// <summary>
        /// Stores a pooled status-readback buffer with its byte capacity so larger chunked
        /// readbacks never receive a smaller single-status buffer.
        /// </summary>
        private readonly struct StatusReadbackBufferEntry
        {
            /// <summary>
            /// Initializes a new instance of the <see cref="StatusReadbackBufferEntry"/> struct.
            /// </summary>
            /// <param name="buffer">The native buffer pointer stored as an integer.</param>
            /// <param name="byteLength">The byte capacity of <paramref name="buffer"/>.</param>
            public StatusReadbackBufferEntry(nint buffer, nuint byteLength)
            {
                this.Buffer = buffer;
                this.ByteLength = byteLength;
            }

            /// <summary>
            /// Gets the native buffer pointer stored as an integer.
            /// </summary>
            public nint Buffer { get; }

            /// <summary>
            /// Gets the byte capacity of <see cref="Buffer"/>.
            /// </summary>
            public nuint ByteLength { get; }
        }

        /// <summary>
        /// Shared render-pipeline infrastructure for compositing variants. Instances double as
        /// the per-family lock object that serializes lazy creation of their contents.
        /// </summary>
        private sealed class CompositePipelineInfrastructure
        {
            /// <summary>
            /// Gets the cached pipeline variants keyed by (texture format, blend mode).
            /// Values are stored as <see cref="nint"/> because pointer types cannot be
            /// dictionary type arguments.
            /// </summary>
            public Dictionary<(WGPUTextureFormat TextureFormat, CompositePipelineBlendMode BlendMode), nint> Pipelines { get; } = [];

            /// <summary>
            /// Gets or sets the bind-group layout shared by all variants in this family.
            /// </summary>
            public WGPUBindGroupLayoutImpl* BindGroupLayout { get; set; }

            /// <summary>
            /// Gets or sets the pipeline layout shared by all variants in this family.
            /// </summary>
            public WGPUPipelineLayoutImpl* PipelineLayout { get; set; }

            /// <summary>
            /// Gets or sets the shader module shared by all variants in this family.
            /// </summary>
            public WGPUShaderModuleImpl* ShaderModule { get; set; }
        }

        /// <summary>
        /// Shared compute-pipeline infrastructure for one cached compute shader. Instances
        /// double as the per-family lock object that serializes lazy creation of their contents.
        /// </summary>
        private sealed class CompositeComputePipelineInfrastructure
        {
            /// <summary>
            /// Gets or sets the bind-group layout for this family.
            /// </summary>
            public WGPUBindGroupLayoutImpl* BindGroupLayout { get; set; }

            /// <summary>
            /// Gets or sets the pipeline layout for this family.
            /// </summary>
            public WGPUPipelineLayoutImpl* PipelineLayout { get; set; }

            /// <summary>
            /// Gets or sets the shader module for this family.
            /// </summary>
            public WGPUShaderModuleImpl* ShaderModule { get; set; }

            /// <summary>
            /// Gets or sets the single cached compute pipeline for this family.
            /// </summary>
            public WGPUComputePipelineImpl* Pipeline { get; set; }
        }
    }
}
