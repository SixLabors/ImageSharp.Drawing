// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using SixLabors.ImageSharp.Drawing.Processing.Backends.Native;
using SixLabors.ImageSharp.Memory;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

#pragma warning disable SA1201 // Flush transaction types are kept next to the executor that owns their invariants.

/// <summary>
/// Implements ordered transactional rendering for the WebGPU drawing backend.
/// </summary>
public sealed unsafe partial class WebGPUDrawingBackend
{
    /// <summary>
    /// Executes the immutable ordered plan with allocator validation at Apply barriers, at the
    /// final offscreen boundary, or through deferred presentation readbacks.
    /// </summary>
    /// <typeparam name="TPixel">The pixel format of the render target.</typeparam>
    /// <param name="configuration">The active ImageSharp configuration.</param>
    /// <param name="flushContext">The flush-scoped WebGPU resources and command state.</param>
    /// <param name="rootTarget">The externally owned root render target.</param>
    /// <param name="encodedScene">The retained scene and ordered execution plan.</param>
    /// <param name="scene">The retained scene that receives deferred allocator growth.</param>
    /// <param name="deferSchedulingStatus">Whether presentation allocator statuses can resolve after the frame returns.</param>
    /// <param name="requiredFeature">The texture-format feature required by <typeparamref name="TPixel"/>.</param>
    /// <param name="currentBumpSizes">The scratch capacities carried across flushes.</param>
    /// <param name="resourceArena">The reusable staged-scene resource arena.</param>
    /// <param name="schedulingArena">The reusable scheduling arena.</param>
    private void RenderOrderedScene<TPixel>(
        Configuration configuration,
        WebGPUFlushContext flushContext,
        WebGPUSceneTarget rootTarget,
        WebGPUEncodedScene encodedScene,
        WebGPUDrawingBackendScene scene,
        bool deferSchedulingStatus,
        WGPUFeatureName requiredFeature,
        ref WebGPUSceneBumpSizes currentBumpSizes,
        ref WebGPUSceneResourceArena? resourceArena,
        ref WebGPUSceneSchedulingArena? schedulingArena)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        using WebGPUOrderedExecutor<TPixel> executor = new(
            this,
            configuration,
            flushContext,
            rootTarget,
            encodedScene,
            scene,
            deferSchedulingStatus,
            requiredFeature,
            currentBumpSizes,
            resourceArena,
            schedulingArena);

        try
        {
            executor.Execute();
            currentBumpSizes = executor.BumpSizes;
        }
        finally
        {
            // The executor replaces its arenas in place when a range outgrows them, disposing
            // the instances these ref slots still name. The copy-back must run on the failure
            // path too: otherwise the caller's finally returns the disposed originals to the
            // reuse caches (a later release then frees their buffers twice) and the live
            // replacements held only by the executor leak.
            resourceArena = executor.ResourceArena;
            schedulingArena = executor.SchedulingArena;
        }
    }

    /// <summary>
    /// Owns one ordered flush's target checkpoints and either its synchronous transaction journal
    /// or its deferred presentation status readbacks.
    /// </summary>
    /// <typeparam name="TPixel">The pixel format used to lay out and materialize Apply readbacks.</typeparam>
    private sealed unsafe class WebGPUOrderedExecutor<TPixel> : IDisposable
        where TPixel : unmanaged, IPixel<TPixel>
    {
        private readonly WebGPUDrawingBackend backend;
        private readonly Configuration configuration;
        private readonly WebGPUFlushContext flushContext;
        private readonly WebGPUEncodedScene encodedScene;
        private readonly WebGPUDrawingBackendScene scene;
        private readonly bool deferSchedulingStatus;
        private readonly WGPUFeatureName requiredFeature;
        private readonly WebGPUOrderedTargetState[] targets;
        private readonly WebGPUOrderedJournalEntry[] journal;
        private readonly WebGPUSceneBumpSizes[] submittedBumpSizes;
        private readonly uint[] submittedChunkTileHeights;
        private readonly WebGPUApplyReadbackRegion[] applyRegions;
        private readonly Image<TPixel>?[] applyImages;
        private readonly WGPUBufferImpl* readbackBuffer;
        private readonly nuint readbackByteLength;
        private readonly ManualResetEventSlim? mapReady;
        private readonly WebGPUBufferMapCallback? mapCallback;
        private readonly WebGPUEffectRenderer? effectRenderer;
        private WebGPUSceneResourceArena? resourceArena;
        private WebGPUSceneSchedulingArena? schedulingArena;
        private WGPUMapAsyncStatus mapStatus;
        private ulong lastSubmissionIndex;
        private int currentTargetId;
        private int journalCount;
        private int statusCount;
        private bool disposed;

        /// <summary>
        /// Initializes a new instance of the <see cref="WebGPUOrderedExecutor{TPixel}"/> class.
        /// </summary>
        /// <param name="backend">The backend that owns shared capacity and chunk-hint state.</param>
        /// <param name="configuration">The active ImageSharp configuration.</param>
        /// <param name="flushContext">The flush-scoped WebGPU command and resource context.</param>
        /// <param name="rootTarget">The externally owned root target.</param>
        /// <param name="encodedScene">The retained ordered scene.</param>
        /// <param name="scene">The retained scene that receives deferred allocator growth.</param>
        /// <param name="deferSchedulingStatus">Whether presentation allocator statuses can resolve after the frame returns.</param>
        /// <param name="requiredFeature">The target-format feature required by <typeparamref name="TPixel"/>.</param>
        /// <param name="bumpSizes">The initial scheduling scratch capacities.</param>
        /// <param name="resourceArena">The rented resource arena, or <see langword="null"/>.</param>
        /// <param name="schedulingArena">The rented scheduling arena, or <see langword="null"/>.</param>
        public WebGPUOrderedExecutor(
            WebGPUDrawingBackend backend,
            Configuration configuration,
            WebGPUFlushContext flushContext,
            WebGPUSceneTarget rootTarget,
            WebGPUEncodedScene encodedScene,
            WebGPUDrawingBackendScene scene,
            bool deferSchedulingStatus,
            WGPUFeatureName requiredFeature,
            WebGPUSceneBumpSizes bumpSizes,
            WebGPUSceneResourceArena? resourceArena,
            WebGPUSceneSchedulingArena? schedulingArena)
        {
            this.backend = backend;
            this.configuration = configuration;
            this.flushContext = flushContext;
            this.encodedScene = encodedScene;
            this.scene = scene;
            this.deferSchedulingStatus = deferSchedulingStatus;
            this.requiredFeature = requiredFeature;
            this.BumpSizes = bumpSizes;
            this.resourceArena = resourceArena;
            this.schedulingArena = schedulingArena;
            this.targets = new WebGPUOrderedTargetState[encodedScene.OrderedTargetCount];
            this.journal = deferSchedulingStatus
                ? []
                : new WebGPUOrderedJournalEntry[encodedScene.MaxPendingRangeCount];

            this.submittedBumpSizes = new WebGPUSceneBumpSizes[encodedScene.MaxStatusCapacity];
            this.submittedChunkTileHeights = new uint[encodedScene.MaxStatusCapacity];
            this.applyRegions = new WebGPUApplyReadbackRegion[encodedScene.OrderedApplyCount];
            this.applyImages = new Image<TPixel>?[encodedScene.MaxApplyGroupCount];
            this.targets[0].Initialize(rootTarget);

            if (deferSchedulingStatus)
            {
                this.readbackBuffer = null;
                this.readbackByteLength = 0;
                this.mapReady = null;
                this.mapCallback = null;
            }
            else
            {
                this.readbackByteLength = this.BuildReadbackLayout();
                WGPUBufferDescriptor descriptor = new()
                {
                    usage = (ulong)(BufferUsage.CopyDst | BufferUsage.MapRead),
                    size = checked(this.readbackByteLength),
                    mappedAtCreation = 0U,
                };

                using WebGPUHandle.HandleReference deviceReference = flushContext.DeviceHandle.AcquireReference();
                this.readbackBuffer = flushContext.Api.DeviceCreateBuffer((WGPUDeviceImpl*)deviceReference.Handle, in descriptor);
                if (this.readbackBuffer is null)
                {
                    throw new InvalidOperationException("Failed to create the ordered WebGPU barrier readback buffer.");
                }

                // The flush context releases the native buffer after every submitted command that
                // references it has left managed ownership.
                flushContext.TrackBuffer(this.readbackBuffer);
                this.mapReady = new ManualResetEventSlim(false);
                this.mapCallback = WebGPUBufferMapCallback.From(this.OnMapCompleted);
            }

            if (encodedScene.OrderedShaderEffectCount != 0)
            {
                // The uniform buffer holds one disjoint region per pass of a single invocation, so
                // its size follows the largest pass count among the retained effect operations.
                int maximumPassCount = 0;
                ReadOnlySpan<WebGPUSceneOperation> operations = encodedScene.OrderedOperations;
                for (int i = 0; i < operations.Length; i++)
                {
                    if (operations[i].Kind == WebGPUSceneOperationKind.ShaderEffect)
                    {
                        maximumPassCount = Math.Max(maximumPassCount, operations[i].ShaderEffect!.Effect.GetShaderPasses().Length);
                    }
                }

                try
                {
                    this.effectRenderer = new WebGPUEffectRenderer(
                        flushContext,
                        encodedScene.MaximumShaderEffectWidth,
                        encodedScene.MaximumShaderEffectHeight,
                        encodedScene.MaximumShaderUniformByteLength,
                        maximumPassCount);
                }
                catch
                {
                    // A ctor throw means the caller's using never binds, so Dispose cannot retire
                    // the callback owner and event created above.
                    this.mapCallback?.Dispose();
                    this.mapReady?.Dispose();
                    throw;
                }
            }
        }

        /// <summary>
        /// Gets the scratch capacities after all successful validation and replay attempts.
        /// </summary>
        public WebGPUSceneBumpSizes BumpSizes { get; private set; }

        /// <summary>
        /// Gets the resource arena used by the completed ordered flush.
        /// </summary>
        public WebGPUSceneResourceArena? ResourceArena => this.resourceArena;

        /// <summary>
        /// Gets the scheduling arena used by the completed ordered flush.
        /// </summary>
        public WebGPUSceneSchedulingArena? SchedulingArena => this.schedulingArena;

        /// <summary>
        /// Executes every retained operation and commits the root target. Presentation-only
        /// allocator validation completes through the backend's deferred status queue.
        /// </summary>
        public void Execute()
        {
            ReadOnlySpan<WebGPUSceneOperation> operations = this.encodedScene.OrderedOperations;

            for (int i = 0; i < operations.Length; i++)
            {
                WebGPUSceneOperation operation = operations[i];
                switch (operation.Kind)
                {
                    case WebGPUSceneOperationKind.RenderRange:
                        this.SubmitRange(
                            operation.Range,
                            this.currentTargetId,
                            WebGPUOrderedExternalSourceKind.None,
                            null,
                            0);
                        break;

                    case WebGPUSceneOperationKind.BeginLayer:
                        this.BeginLayer(operation);
                        break;

                    case WebGPUSceneOperationKind.EndLayer:
                        this.currentTargetId = operation.ParentTargetId;
                        this.SubmitRange(
                            operation.Range,
                            operation.ParentTargetId,
                            WebGPUOrderedExternalSourceKind.Target,
                            null,
                            operation.LayerTargetId);
                        this.targets[operation.LayerTargetId].MarkInactive();
                        break;

                    case WebGPUSceneOperationKind.Apply:
                        this.ResolveApplyBarrier(i, operation.ApplyGroupCount);
                        this.CommitTargets();
                        this.ProcessApplyGroup(i, operation.ApplyGroupCount);
                        i += operation.ApplyGroupCount - 1;
                        break;

                    case WebGPUSceneOperationKind.ShaderEffect:
                        if (!this.deferSchedulingStatus)
                        {
                            this.ResolveFinalBarrier();
                            this.CommitTargets();
                        }

                        this.ProcessShaderEffect(operation.ShaderEffect!);
                        break;
                }
            }

            if (!this.deferSchedulingStatus)
            {
                this.ResolveFinalBarrier();
            }

            this.CommitTargets();
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (this.disposed)
            {
                return;
            }

            this.disposed = true;
            this.DisposeApplyImages(this.applyImages.Length);
            this.mapCallback?.Dispose();
            this.mapReady?.Dispose();
        }

        /// <summary>
        /// Computes every generic pixel row once and returns the largest aligned barrier slice.
        /// </summary>
        /// <returns>The required readback buffer size in bytes.</returns>
        private nuint BuildReadbackLayout()
        {
            int pixelSize = Unsafe.SizeOf<TPixel>();
            ulong maximumByteLength = checked((ulong)this.encodedScene.MaxStatusCapacity * (ulong)sizeof(GpuSceneBumpAllocators));
            ReadOnlySpan<WebGPUSceneOperation> operations = this.encodedScene.OrderedOperations;

            for (int i = 0; i < operations.Length; i++)
            {
                WebGPUSceneOperation operation = operations[i];
                if (operation.Kind != WebGPUSceneOperationKind.Apply || operation.ApplyGroupCount == 0)
                {
                    continue;
                }

                // WebGPU requires both texture-copy row pitches and texture-to-buffer offsets to
                // be 256-byte aligned. Status records occupy the prefix before this aligned point.
                ulong nextPixelOffset = AlignUp(
                    checked((ulong)operation.PendingStatusCapacity * (ulong)sizeof(GpuSceneBumpAllocators)),
                    256);
                ulong groupByteLength = nextPixelOffset;

                for (int groupIndex = 0; groupIndex < operation.ApplyGroupCount; groupIndex++)
                {
                    WebGPUSceneOperation applyOperation = operations[i + groupIndex];
                    WebGPUApplySceneItem apply = applyOperation.Apply!;
                    Rectangle requestedRead = apply.InputRect;
                    requestedRead.Offset(-apply.ReadOffset.X, -apply.ReadOffset.Y);
                    Rectangle clippedRead = Rectangle.Intersect(apply.DrawRange.TargetBounds, requestedRead);
                    int packedRowBytes = checked(clippedRead.Width * pixelSize);
                    int readbackRowBytes = clippedRead.Width == 0 ? 0 : checked((int)AlignUp((ulong)packedRowBytes, 256));
                    ulong regionByteLength = checked((ulong)readbackRowBytes * (ulong)clippedRead.Height);

                    this.applyRegions[applyOperation.ApplyIndex] = new WebGPUApplyReadbackRegion(
                        clippedRead,
                        clippedRead.X - requestedRead.X,
                        clippedRead.Y - requestedRead.Y,
                        nextPixelOffset,
                        packedRowBytes,
                        readbackRowBytes);

                    nextPixelOffset = checked(nextPixelOffset + regionByteLength);
                    groupByteLength = Math.Max(groupByteLength, nextPixelOffset);
                }

                maximumByteLength = Math.Max(maximumByteLength, groupByteLength);
                i += operation.ApplyGroupCount - 1;
            }

            // WebGPU buffers cannot be zero-sized. Ordered scenes with only empty ranges still
            // retain one allocator-record-sized buffer so the ownership path remains uniform.
            return checked((nuint)Math.Max(maximumByteLength, (ulong)sizeof(GpuSceneBumpAllocators)));
        }

        /// <summary>
        /// Creates and clears one stable scoped-layer target.
        /// </summary>
        /// <param name="operation">The retained layer-entry operation.</param>
        private void BeginLayer(WebGPUSceneOperation operation)
        {
            if (!TryCreateCompositionTexture(
                    this.flushContext,
                    operation.LayerBounds.Width,
                    operation.LayerBounds.Height,
                    renderAttachment: true,
                    out WGPUTextureImpl* layerTexture,
                    out WGPUTextureViewImpl* layerTextureView,
                    out string? error))
            {
                throw new InvalidOperationException(error);
            }

            ClearTarget(this.flushContext, layerTextureView);
            WebGPUSceneTarget layerTarget = new(
                layerTexture,
                layerTextureView,
                operation.LayerBounds,
                new Point(-operation.LayerBounds.X, -operation.LayerBounds.Y));

            this.targets[operation.LayerTargetId].Initialize(layerTarget);
            this.currentTargetId = operation.LayerTargetId;
        }

        /// <summary>
        /// Submits one range with synchronous transaction validation or deferred presentation validation.
        /// </summary>
        /// <param name="range">The retained range to submit.</param>
        /// <param name="targetId">The logical target receiving the range.</param>
        /// <param name="externalSourceKind">The source bound to the range's external texture slot.</param>
        /// <param name="externalTextureView">The fixed Apply upload view, when applicable.</param>
        /// <param name="externalTargetId">The layer target supplying a composite view, when applicable.</param>
        private void SubmitRange(
            WebGPUSceneRange range,
            int targetId,
            WebGPUOrderedExternalSourceKind externalSourceKind,
            WGPUTextureViewImpl* externalTextureView,
            int externalTargetId)
        {
            if (range.FillCount == 0)
            {
                return;
            }

            WebGPUOrderedJournalEntry newEntry = new(
                range,
                targetId,
                externalSourceKind,
                externalTextureView,
                externalTargetId);

            if (this.deferSchedulingStatus)
            {
                this.SubmitRangeAttempt(ref newEntry);
                return;
            }

            ref WebGPUOrderedJournalEntry entry = ref this.journal[this.journalCount];
            entry = newEntry;
            this.SubmitRangeAttempt(ref entry);
            this.journalCount++;
        }

        /// <summary>
        /// Submits one initial or replayed range attempt and records or defers its status metadata.
        /// </summary>
        /// <param name="entry">The stable journal entry describing the GPU draw.</param>
        private void SubmitRangeAttempt(ref WebGPUOrderedJournalEntry entry)
        {
            ref WebGPUOrderedTargetState targetState = ref this.targets[entry.TargetId];
            WebGPUSceneTarget backdropTarget = targetState.GetLatestTarget();
            targetState.GetNextOutput(
                this.flushContext,
                out WGPUTextureImpl* outputTexture,
                out WGPUTextureViewImpl* outputTextureView,
                out WebGPUOrderedTargetImage outputImage);

            WGPUTextureViewImpl* externalTextureView = entry.ExternalSourceKind switch
            {
                WebGPUOrderedExternalSourceKind.TextureView => entry.ExternalTextureView,
                WebGPUOrderedExternalSourceKind.Target => this.targets[entry.ExternalTargetId].GetLatestTarget().TextureView,
                _ => null
            };

            WebGPUStagedScene stagedScene = WebGPUSceneDispatch.CreateStagedScene<TPixel>(
                this.flushContext,
                this.encodedScene,
                entry.Range,
                this.requiredFeature,
                this.BumpSizes,
                externalTextureView,
                ref this.resourceArena);

            nuint statusBufferCapacity = 0;
            WGPUBufferImpl* statusReadbackBuffer = this.readbackBuffer;
            bool statusBufferPassedToDispatch = false;
            bool statusBufferOwnershipTransferred = false;

            if (this.deferSchedulingStatus)
            {
                statusBufferCapacity = checked(
                    (nuint)entry.Range.MaximumStatusRecordCount * (nuint)sizeof(GpuSceneBumpAllocators));
                statusReadbackBuffer = this.flushContext.DeviceState.RentStatusReadbackBuffer(statusBufferCapacity);
                if (statusReadbackBuffer is null)
                {
                    stagedScene.Dispose();
                    throw new InvalidOperationException("Failed to create the deferred ordered scheduling-status readback buffer.");
                }
            }

            try
            {
                if (stagedScene.BindingLimitFailure.Buffer != WebGPUSceneDispatch.BindingLimitBuffer.None)
                {
                    this.backend.DiagnosticLastFlushUsedChunking = true;
                    this.backend.lastChunkingBindingFailure = stagedScene.BindingLimitFailure.Buffer;
                }

                int statusStart = this.deferSchedulingStatus ? 0 : this.statusCount;
                statusBufferPassedToDispatch = this.deferSchedulingStatus;
                bool submitted = WebGPUSceneDispatch.TrySubmitTransactionalStagedScene(
                    ref stagedScene,
                    backdropTarget,
                    outputTexture,
                    outputTextureView,
                    ref this.schedulingArena,
                    this.backend.GetChunkTileHeightHint(stagedScene.BindingLimitFailure.Buffer, entry.Range.TargetBounds.Size),
                    statusReadbackBuffer,
                    checked((nuint)statusStart * (nuint)sizeof(GpuSceneBumpAllocators)),
                    this.submittedBumpSizes.AsSpan(statusStart),
                    this.submittedChunkTileHeights.AsSpan(statusStart),
                    out int emittedStatusCount,
                    out uint fullTileHeight,
                    out uint successfulChunkTileHeight,
                    out ulong submissionIndex,
                    out string? error);

                if (!submitted)
                {
                    throw new InvalidOperationException(error ?? "The transactional WebGPU range submission failed.");
                }

                entry.SetStatus(statusStart, emittedStatusCount, fullTileHeight, stagedScene.BindingLimitFailure.IsExceeded);
                this.lastSubmissionIndex = submissionIndex;
                targetState.SetLatestImage(outputImage);

                if (this.deferSchedulingStatus)
                {
                    WebGPUPendingSchedulingStatus pendingStatus = stagedScene.BindingLimitFailure.IsExceeded
                        ? new WebGPUPendingSchedulingStatus(
                            this.flushContext.Api,
                            this.flushContext.DeviceHandle,
                            this.flushContext.DeviceState,
                            statusReadbackBuffer,
                            statusBufferCapacity,
                            stagedScene.Config.BumpSizes,
                            this.submittedBumpSizes.AsSpan(0, emittedStatusCount),
                            this.submittedChunkTileHeights.AsSpan(0, emittedStatusCount),
                            fullTileHeight,
                            submissionIndex)
                        : new WebGPUPendingSchedulingStatus(
                            this.flushContext.Api,
                            this.flushContext.DeviceHandle,
                            this.flushContext.DeviceState,
                            statusReadbackBuffer,
                            statusBufferCapacity,
                            this.submittedBumpSizes[0],
                            submissionIndex);

                    statusBufferOwnershipTransferred = true;
                    this.backend.EnqueuePendingSchedulingStatus(pendingStatus, this.scene, correctiveRender: null);
                }
                else
                {
                    this.statusCount += emittedStatusCount;
                }

                if (successfulChunkTileHeight != 0)
                {
                    this.backend.UpdateChunkTileHeightHint(
                        stagedScene.BindingLimitFailure.Buffer,
                        entry.Range.TargetBounds.Size,
                        successfulChunkTileHeight);
                }
            }
            finally
            {
                stagedScene.Dispose();

                if (this.deferSchedulingStatus && !statusBufferOwnershipTransferred)
                {
                    if (statusBufferPassedToDispatch)
                    {
                        // Dispatch may already have submitted commands that reference the buffer.
                        // Native command ownership permits releasing it with the flush resources,
                        // but it must not return to the reusable pool before those commands retire.
                        this.flushContext.TrackBuffer(statusReadbackBuffer);
                    }
                    else
                    {
                        this.flushContext.DeviceState.ReturnStatusReadbackBuffer(statusReadbackBuffer, statusBufferCapacity);
                    }
                }
            }
        }

        /// <summary>
        /// Validates pending statuses and captures every Apply source in one mapped buffer.
        /// </summary>
        /// <param name="firstOperationIndex">The operation index of the Apply group head.</param>
        /// <param name="applyCount">The number of independent Apply snapshots in the group.</param>
        private void ResolveApplyBarrier(int firstOperationIndex, int applyCount)
        {
            ReadOnlySpan<WebGPUSceneOperation> operations = this.encodedScene.OrderedOperations;
            WebGPUSceneOperation groupHead = operations[firstOperationIndex];

            for (int attempt = 0; attempt < MaxDynamicGrowthAttempts; attempt++)
            {
                ulong mappedByteLength = checked((ulong)groupHead.PendingStatusCapacity * (ulong)sizeof(GpuSceneBumpAllocators));
                bool recordedPixelCopy = false;
                WebGPUSceneTarget sourceTarget = this.targets[this.currentTargetId].GetLatestTarget();

                for (int groupIndex = 0; groupIndex < applyCount; groupIndex++)
                {
                    WebGPUSceneOperation applyOperation = operations[firstOperationIndex + groupIndex];
                    WebGPUApplyReadbackRegion region = this.applyRegions[applyOperation.ApplyIndex];
                    mappedByteLength = Math.Max(mappedByteLength, region.EndOffset);

                    if (region.ClippedSource.Width == 0 || region.ClippedSource.Height == 0)
                    {
                        continue;
                    }

                    if (!this.flushContext.EnsureCommandEncoder())
                    {
                        throw new InvalidOperationException("Failed to create the ordered Apply readback command encoder.");
                    }

                    WGPUTexelCopyTextureInfo sourceCopy = new()
                    {
                        texture = sourceTarget.Texture,
                        mipLevel = 0,
                        origin = new WGPUOrigin3D(
                            (uint)(region.ClippedSource.X + sourceTarget.TextureOffset.X),
                            (uint)(region.ClippedSource.Y + sourceTarget.TextureOffset.Y),
                            0),
                        aspect = WGPUTextureAspect.All
                    };

                    WGPUTexelCopyBufferInfo destinationCopy = new()
                    {
                        buffer = this.readbackBuffer,
                        layout = new WGPUTexelCopyBufferLayout
                        {
                            offset = region.BufferOffset,
                            bytesPerRow = (uint)region.ReadbackRowBytes,
                            rowsPerImage = (uint)region.ClippedSource.Height
                        }
                    };

                    WGPUExtent3D copySize = new((uint)region.ClippedSource.Width, (uint)region.ClippedSource.Height, 1);
                    this.flushContext.Api.CommandEncoderCopyTextureToBuffer(
                        this.flushContext.CommandEncoder,
                        in sourceCopy,
                        in destinationCopy,
                        in copySize);

                    recordedPixelCopy = true;
                }

                if (!recordedPixelCopy && this.statusCount == 0)
                {
                    // A fully clipped Apply reads transparent pixels and has no GPU dependency.
                    // Materialize the correctly sized blank images without creating a fake submit.
                    this.CreateBlankApplyImages(firstOperationIndex, applyCount);
                    this.journalCount = 0;
                    return;
                }

                ulong barrierSubmissionIndex = recordedPixelCopy
                    ? this.SubmitBarrierCommands()
                    : this.lastSubmissionIndex;

                void* mapped = this.MapReadback(checked((nuint)mappedByteLength), barrierSubmissionIndex);

                bool requiresReplay;
                try
                {
                    requiresReplay = this.ValidateMappedStatuses(mapped);

                    if (!requiresReplay)
                    {
                        this.CopyApplyImagesFromMapped(firstOperationIndex, applyCount, mapped, checked((nuint)mappedByteLength));
                    }
                }
                finally
                {
                    this.flushContext.Api.BufferUnmap(this.readbackBuffer);
                }

                if (requiresReplay)
                {
                    // CopyDst commands cannot target a mapped buffer. Replay starts only after
                    // the failed barrier has been unmapped.
                    this.ReplayJournal();
                    continue;
                }

                this.journalCount = 0;
                this.statusCount = 0;
                return;
            }

            throw new InvalidOperationException("The ordered WebGPU transaction exceeded the dynamic growth retry budget.");
        }

        /// <summary>
        /// Validates and commits the final status-only transaction.
        /// </summary>
        private void ResolveFinalBarrier()
        {
            if (this.journalCount == 0)
            {
                return;
            }

            for (int attempt = 0; attempt < MaxDynamicGrowthAttempts; attempt++)
            {
                nuint mappedByteLength = checked((nuint)this.statusCount * (nuint)sizeof(GpuSceneBumpAllocators));
                void* mapped = this.MapReadback(mappedByteLength, this.lastSubmissionIndex);

                bool requiresReplay;
                try
                {
                    requiresReplay = this.ValidateMappedStatuses(mapped);
                }
                finally
                {
                    this.flushContext.Api.BufferUnmap(this.readbackBuffer);
                }

                if (requiresReplay)
                {
                    this.ReplayJournal();
                    continue;
                }

                this.journalCount = 0;
                this.statusCount = 0;
                return;
            }

            throw new InvalidOperationException("The ordered WebGPU transaction exceeded the dynamic growth retry budget.");
        }

        /// <summary>
        /// Maps the barrier buffer and waits only for the submission that owns its final bytes.
        /// </summary>
        /// <param name="mappedByteLength">The byte length required by the current barrier.</param>
        /// <param name="submissionIndex">The exact submission that completes the barrier data.</param>
        /// <returns>The mapped read-only range.</returns>
        private void* MapReadback(nuint mappedByteLength, ulong submissionIndex)
        {
            this.mapStatus = default;
            this.mapReady!.Reset();
            this.flushContext.Api.BufferMapAsync(
                this.readbackBuffer,
                MapMode.Read,
                0,
                mappedByteLength,
                this.mapCallback!,
                null);

            using WebGPUHandle.HandleReference deviceReference = this.flushContext.DeviceHandle.AcquireReference();
            if (!WaitForMapSignal(this.flushContext.Api, (WGPUDeviceImpl*)deviceReference.Handle, this.mapReady!, submissionIndex) ||
                this.mapStatus != WGPUMapAsyncStatus.Success)
            {
                // Unmapping cancels a still-pending map before executor disposal retires its
                // managed owner. The wrapper remains self-rooted until native delivers the
                // cancellation callback, so native code cannot retain a dead delegate target.
                this.flushContext.Api.BufferUnmap(this.readbackBuffer);
                throw new InvalidOperationException($"Failed to map the ordered WebGPU barrier with status '{this.mapStatus}'.");
            }

            void* mapped = this.flushContext.Api.BufferGetConstMappedRange(this.readbackBuffer, 0, mappedByteLength);
            if (mapped is null)
            {
                this.flushContext.Api.BufferUnmap(this.readbackBuffer);
                throw new InvalidOperationException("The ordered WebGPU barrier mapped successfully but returned no readable range.");
            }

            return mapped;
        }

        /// <summary>
        /// Validates every journal status, updates all eight allocator high-water marks, and
        /// reports whether the transaction must be replayed when any record overflowed.
        /// </summary>
        /// <param name="mapped">The mapped barrier prefix containing allocator records.</param>
        /// <returns><see langword="true"/> when the transaction was replayed; otherwise, <see langword="false"/>.</returns>
        private bool ValidateMappedStatuses(void* mapped)
        {
            ReadOnlySpan<GpuSceneBumpAllocators> statuses = new(mapped, this.statusCount);
            WebGPUSceneBumpSizes mergedSizes = this.BumpSizes;
            bool requiresReplay = false;

            for (int i = 0; i < this.journalCount; i++)
            {
                WebGPUOrderedJournalEntry entry = this.journal[i];
                ReadOnlySpan<GpuSceneBumpAllocators> entryStatuses = statuses.Slice(entry.StatusStart, entry.StatusCount);
                ReadOnlySpan<WebGPUSceneBumpSizes> entrySubmittedSizes = this.submittedBumpSizes.AsSpan(entry.StatusStart, entry.StatusCount);

                if (entry.IsChunked)
                {
                    bool entryRequiresGrowth = WebGPUSceneDispatch.ResolveChunkSchedulingStatuses(
                        entryStatuses,
                        entrySubmittedSizes,
                        this.submittedChunkTileHeights.AsSpan(entry.StatusStart, entry.StatusCount),
                        entry.FullTileHeight,
                        out WebGPUSceneBumpSizes entrySizes);

                    requiresReplay |= entryRequiresGrowth;
                    mergedSizes = MaxBumpSizes(mergedSizes, entrySizes);
                    continue;
                }

                GpuSceneBumpAllocators actual = entryStatuses[0];
                WebGPUSceneBumpSizes submitted = entrySubmittedSizes[0];
                WebGPUSceneBumpSizes actualSizes = new(
                    Math.Max(actual.Lines, submitted.Lines),
                    Math.Max(actual.Binning, submitted.Binning),
                    Math.Max(actual.PathRows, 1U),
                    Math.Max(actual.Tile, 1U),
                    Math.Max(actual.SegCounts, submitted.SegCounts),
                    Math.Max(actual.Segments, submitted.Segments),
                    Math.Max(actual.BlendSpill, submitted.BlendSpill),
                    Math.Max(actual.Ptcl, submitted.Ptcl));

                if (WebGPUSceneDispatch.RequiresScratchReallocation(in actual, submitted))
                {
                    requiresReplay = true;
                    actualSizes = MaxBumpSizes(actualSizes, WebGPUSceneDispatch.GrowBumpSizes(submitted, in actual));
                }

                mergedSizes = MaxBumpSizes(mergedSizes, actualSizes);
            }

            this.BumpSizes = mergedSizes;
            return requiresReplay;
        }

        /// <summary>
        /// Replays only GPU ranges from committed target checkpoints after allocator growth.
        /// Apply callbacks and their retained uploads are not repeated.
        /// </summary>
        private void ReplayJournal()
        {
            for (int i = 0; i < this.targets.Length; i++)
            {
                if (this.targets[i].IsInitialized)
                {
                    this.targets[i].ResetToCommitted();
                }
            }

            this.statusCount = 0;
            for (int i = 0; i < this.journalCount; i++)
            {
                this.SubmitRangeAttempt(ref this.journal[i]);
            }
        }

        /// <summary>
        /// Copies active target images into their durable root/layer checkpoints and submits all
        /// commits together. Synchronous paths call this after validation; presentation paths
        /// validate the submitted allocator records through the deferred status queue.
        /// </summary>
        private void CommitTargets()
        {
            bool recordedCommit = false;

            for (int i = 0; i < this.targets.Length; i++)
            {
                ref WebGPUOrderedTargetState target = ref this.targets[i];
                if (!target.IsInitialized || !target.IsActive || !target.RequiresCommit)
                {
                    continue;
                }

                if (!this.flushContext.EnsureCommandEncoder())
                {
                    throw new InvalidOperationException("Failed to create the ordered target commit encoder.");
                }

                WebGPUSceneTarget latest = target.GetLatestTarget();
                WebGPUSceneTarget actual = target.ActualTarget;
                CopyTextureRegion(
                    this.flushContext,
                    latest.Texture,
                    0,
                    0,
                    actual.Texture,
                    actual.Bounds.X + actual.TextureOffset.X,
                    actual.Bounds.Y + actual.TextureOffset.Y,
                    actual.Bounds.Width,
                    actual.Bounds.Height);

                recordedCommit = true;
            }

            if (!recordedCommit)
            {
                return;
            }

            if (!TrySubmitPendingCommands(this.flushContext))
            {
                throw new InvalidOperationException("Failed to submit ordered target commits.");
            }

            for (int i = 0; i < this.targets.Length; i++)
            {
                if (this.targets[i].IsInitialized && this.targets[i].IsActive)
                {
                    this.targets[i].MarkCommitted();
                }
            }
        }

        /// <summary>
        /// Runs one Apply group in source order and retains each upload until the following
        /// transaction validates or replays its draw range.
        /// </summary>
        /// <param name="firstOperationIndex">The Apply group head operation index.</param>
        /// <param name="applyCount">The number of processors in the group.</param>
        private void ProcessApplyGroup(int firstOperationIndex, int applyCount)
        {
            ReadOnlySpan<WebGPUSceneOperation> operations = this.encodedScene.OrderedOperations;
            int processedCount = 0;

            try
            {
                for (; processedCount < applyCount; processedCount++)
                {
                    WebGPUApplySceneItem apply = operations[firstOperationIndex + processedCount].Apply!;
                    Image<TPixel> image = this.applyImages[processedCount]!;
                    image.Mutate(apply.Operation);

                    if (!TryCreateCompositionTexture(
                            this.flushContext,
                            apply.InputRect.Width,
                            apply.InputRect.Height,
                            out WGPUTextureImpl* texture,
                            out WGPUTextureViewImpl* textureView,
                            out string? error))
                    {
                        throw new InvalidOperationException(error);
                    }

                    using (WebGPUHandle.HandleReference queueReference = this.flushContext.QueueHandle.AcquireReference())
                    {
                        WebGPUFlushContext.UploadTextureFromRegion(
                            this.flushContext.Api,
                            (WGPUQueueImpl*)queueReference.Handle,
                            texture,
                            image.Frames.RootFrame.PixelBuffer.GetRegion(),
                            this.configuration.MemoryAllocator);
                    }

                    image.Dispose();
                    this.applyImages[processedCount] = null;
                    this.SubmitRange(
                        apply.DrawRange,
                        this.currentTargetId,
                        WebGPUOrderedExternalSourceKind.TextureView,
                        textureView,
                        0);
                }
            }
            catch (Exception exception)
            {
                this.DisposeApplyImages(applyCount);

                try
                {
                    // Current behavior makes every earlier successful Apply visible before a
                    // later processor exception escapes. Validate and commit those draws without
                    // invoking any processor a second time.
                    this.ResolveFinalBarrier();
                    this.CommitTargets();
                }
                catch
                {
                    // The processor exception is the observable failure that initiated cleanup;
                    // a secondary device failure must not replace its stack or exception identity.
                }

                ExceptionDispatchInfo.Capture(exception).Throw();
                throw;
            }
        }

        /// <summary>
        /// Executes one native effect against the latest ordered source and journals its retained write-back range.
        /// </summary>
        /// <param name="effect">The native effect operation.</param>
        private void ProcessShaderEffect(WebGPUShaderEffectSceneItem effect)
        {
            // A fully clipped write-back has no fill to draw. Skip the passes entirely: nothing
            // samples the result, and SubmitRange would return without submitting, leaving the
            // recorded passes in the shared encoder while the next invocation rewrites the
            // uniform regions they reference.
            if (effect.DrawRange.FillCount == 0)
            {
                return;
            }

            WebGPUSceneTarget sourceTarget = this.targets[this.currentTargetId].GetLatestTarget();
            WGPUTextureViewImpl* effectTextureView = this.effectRenderer!.Render(effect, sourceTarget);
            this.SubmitRange(
                effect.DrawRange,
                this.currentTargetId,
                WebGPUOrderedExternalSourceKind.TextureView,
                effectTextureView,
                0);
        }

        /// <summary>
        /// Creates transparent source images for a group whose requested reads are fully clipped.
        /// </summary>
        /// <param name="firstOperationIndex">The Apply group head operation index.</param>
        /// <param name="applyCount">The number of source images to create.</param>
        private void CreateBlankApplyImages(int firstOperationIndex, int applyCount)
        {
            ReadOnlySpan<WebGPUSceneOperation> operations = this.encodedScene.OrderedOperations;
            for (int i = 0; i < applyCount; i++)
            {
                Rectangle inputRect = operations[firstOperationIndex + i].Apply!.InputRect;
                this.applyImages[i] = new Image<TPixel>(this.configuration, inputRect.Width, inputRect.Height);
            }
        }

        /// <summary>
        /// Materializes full Apply images from aligned, clipped rows while the barrier is mapped.
        /// </summary>
        /// <param name="firstOperationIndex">The Apply group head operation index.</param>
        /// <param name="applyCount">The number of source images to materialize.</param>
        /// <param name="mapped">The mapped barrier buffer.</param>
        /// <param name="mappedByteLength">The readable mapped byte length.</param>
        private void CopyApplyImagesFromMapped(
            int firstOperationIndex,
            int applyCount,
            void* mapped,
            nuint mappedByteLength)
        {
            ReadOnlySpan<byte> readback = new(mapped, checked((int)mappedByteLength));
            ReadOnlySpan<WebGPUSceneOperation> operations = this.encodedScene.OrderedOperations;

            try
            {
                for (int i = 0; i < applyCount; i++)
                {
                    WebGPUSceneOperation operation = operations[firstOperationIndex + i];
                    WebGPUApplySceneItem apply = operation.Apply!;
                    WebGPUApplyReadbackRegion region = this.applyRegions[operation.ApplyIndex];
                    Image<TPixel> image = new(this.configuration, apply.InputRect.Width, apply.InputRect.Height);
                    this.applyImages[i] = image;

                    Buffer2DRegion<TPixel> destination = image.Frames.RootFrame.PixelBuffer.GetRegion();
                    for (int y = 0; y < region.ClippedSource.Height; y++)
                    {
                        int sourceOffset = checked((int)region.BufferOffset + (y * region.ReadbackRowBytes));
                        readback
                            .Slice(sourceOffset, region.PackedRowBytes)
                            .CopyTo(MemoryMarshal.AsBytes(
                                destination.DangerousGetRowSpan(region.DestinationY + y)
                                           .Slice(region.DestinationX, region.ClippedSource.Width)));
                    }
                }
            }
            catch
            {
                this.DisposeApplyImages(applyCount);
                throw;
            }
        }

        /// <summary>
        /// Disposes materialized Apply images and clears their reusable slots.
        /// </summary>
        /// <param name="count">The number of leading slots belonging to the current group.</param>
        private void DisposeApplyImages(int count)
        {
            for (int i = 0; i < count; i++)
            {
                this.applyImages[i]?.Dispose();
                this.applyImages[i] = null;
            }
        }

        /// <summary>
        /// Submits the texture-copy commands that complete one mixed Apply barrier.
        /// </summary>
        /// <returns>The exact submission index owning the copied pixels.</returns>
        private ulong SubmitBarrierCommands()
        {
            if (!TrySubmitWithIndex(this.flushContext, out ulong submissionIndex))
            {
                throw new InvalidOperationException("Failed to submit the ordered Apply barrier.");
            }

            return submissionIndex;
        }

        /// <summary>
        /// Receives the native map completion status for the currently active barrier.
        /// </summary>
        /// <param name="status">The native buffer-map completion status.</param>
        /// <param name="userData">Unused native callback state.</param>
        private void OnMapCompleted(WGPUMapAsyncStatus status, void* userData)
        {
            _ = userData;
            this.mapStatus = status;
            this.mapReady!.Set();
        }

        /// <summary>
        /// Aligns an unsigned byte offset upward to a required power-of-two boundary.
        /// </summary>
        /// <param name="value">The byte offset to align.</param>
        /// <param name="alignment">The required power-of-two alignment in bytes.</param>
        /// <returns>The aligned byte offset.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ulong AlignUp(ulong value, ulong alignment)
            => checked((value + alignment - 1) & ~(alignment - 1));
    }

    /// <summary>
    /// Identifies the image currently representing one logical ordered target.
    /// </summary>
    private enum WebGPUOrderedTargetImage
    {
        /// <summary>
        /// The durable actual root or layer texture.
        /// </summary>
        Actual,

        /// <summary>
        /// The target's first transaction scratch texture.
        /// </summary>
        ScratchA,

        /// <summary>
        /// The target's second transaction scratch texture.
        /// </summary>
        ScratchB
    }

    /// <summary>
    /// Identifies the external texture binding retained by one replayable range.
    /// </summary>
    private enum WebGPUOrderedExternalSourceKind
    {
        /// <summary>
        /// The range has no external texture binding.
        /// </summary>
        None,

        /// <summary>
        /// The range samples a fixed Apply upload texture view.
        /// </summary>
        TextureView,

        /// <summary>
        /// The range samples the latest logical image of another target.
        /// </summary>
        Target
    }

    /// <summary>
    /// Tracks durable and ping-pong images for one stable root or layer target identity.
    /// </summary>
    private struct WebGPUOrderedTargetState
    {
        private WGPUTextureImpl* scratchATexture;
        private WGPUTextureViewImpl* scratchAView;
        private WGPUTextureImpl* scratchBTexture;
        private WGPUTextureViewImpl* scratchBView;
        private WebGPUOrderedTargetImage latestImage;

        /// <summary>
        /// Gets a value indicating whether this target identity has been materialized.
        /// </summary>
        public bool IsInitialized { get; private set; }

        /// <summary>
        /// Gets a value indicating whether later operations can still require this target as a checkpoint.
        /// </summary>
        public bool IsActive { get; private set; }

        /// <summary>
        /// Gets the durable actual target used as the replay checkpoint.
        /// </summary>
        public WebGPUSceneTarget ActualTarget { get; private set; }

        /// <summary>
        /// Gets a value indicating whether the latest validated logical image still needs copying
        /// into <see cref="ActualTarget"/>.
        /// </summary>
        public readonly bool RequiresCommit => this.latestImage != WebGPUOrderedTargetImage.Actual;

        /// <summary>
        /// Initializes this identity from an externally owned root or flush-owned layer target.
        /// </summary>
        /// <param name="actualTarget">The durable target used for future replay.</param>
        public void Initialize(WebGPUSceneTarget actualTarget)
        {
            this.ActualTarget = actualTarget;
            this.latestImage = WebGPUOrderedTargetImage.Actual;
            this.IsInitialized = true;
            this.IsActive = true;
        }

        /// <summary>
        /// Gets the target image that subsequent fine shading must sample.
        /// </summary>
        /// <returns>The latest logical target with the correct logical-to-texture offset.</returns>
        public readonly WebGPUSceneTarget GetLatestTarget()
            => this.latestImage switch
            {
                WebGPUOrderedTargetImage.ScratchA => new WebGPUSceneTarget(
                    this.scratchATexture,
                    this.scratchAView,
                    this.ActualTarget.Bounds,
                    new Point(-this.ActualTarget.Bounds.X, -this.ActualTarget.Bounds.Y)),
                WebGPUOrderedTargetImage.ScratchB => new WebGPUSceneTarget(
                    this.scratchBTexture,
                    this.scratchBView,
                    this.ActualTarget.Bounds,
                    new Point(-this.ActualTarget.Bounds.X, -this.ActualTarget.Bounds.Y)),
                _ => this.ActualTarget
            };

        /// <summary>
        /// Selects and lazily creates the scratch texture that does not contain the current input image.
        /// </summary>
        /// <param name="flushContext">The flush context that owns created scratch handles.</param>
        /// <param name="texture">Receives the selected output texture.</param>
        /// <param name="textureView">Receives the selected output storage view.</param>
        /// <param name="image">Receives the selected scratch identity.</param>
        public void GetNextOutput(
            WebGPUFlushContext flushContext,
            out WGPUTextureImpl* texture,
            out WGPUTextureViewImpl* textureView,
            out WebGPUOrderedTargetImage image)
        {
            image = this.latestImage == WebGPUOrderedTargetImage.ScratchA
                ? WebGPUOrderedTargetImage.ScratchB
                : WebGPUOrderedTargetImage.ScratchA;

            if (image == WebGPUOrderedTargetImage.ScratchA)
            {
                if (this.scratchATexture is null &&
                    !TryCreateCompositionTexture(
                        flushContext,
                        this.ActualTarget.Bounds.Width,
                        this.ActualTarget.Bounds.Height,
                        out this.scratchATexture,
                        out this.scratchAView,
                        out string? error))
                {
                    throw new InvalidOperationException(error);
                }

                texture = this.scratchATexture;
                textureView = this.scratchAView;
                return;
            }

            if (this.scratchBTexture is null &&
                !TryCreateCompositionTexture(
                    flushContext,
                    this.ActualTarget.Bounds.Width,
                    this.ActualTarget.Bounds.Height,
                    out this.scratchBTexture,
                    out this.scratchBView,
                    out string? scratchError))
            {
                throw new InvalidOperationException(scratchError);
            }

            texture = this.scratchBTexture;
            textureView = this.scratchBView;
        }

        /// <summary>
        /// Publishes the scratch image written by a successfully submitted range.
        /// </summary>
        /// <param name="image">The scratch identity containing the new logical output.</param>
        public void SetLatestImage(WebGPUOrderedTargetImage image) => this.latestImage = image;

        /// <summary>
        /// Restores the logical image to the durable checkpoint before transaction replay.
        /// </summary>
        public void ResetToCommitted() => this.latestImage = WebGPUOrderedTargetImage.Actual;

        /// <summary>
        /// Marks the latest validated image as durably copied into the actual target.
        /// </summary>
        public void MarkCommitted() => this.latestImage = WebGPUOrderedTargetImage.Actual;

        /// <summary>
        /// Marks a completed layer target as no longer requiring commits after its composite range.
        /// </summary>
        public void MarkInactive() => this.IsActive = false;
    }

    /// <summary>
    /// Retains one GPU-only range so allocator growth can replay it without invoking Apply callbacks.
    /// </summary>
    private struct WebGPUOrderedJournalEntry
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="WebGPUOrderedJournalEntry"/> struct.
        /// </summary>
        /// <param name="range">The retained range to replay.</param>
        /// <param name="targetId">The target identity receiving the range.</param>
        /// <param name="externalSourceKind">The range's external texture source.</param>
        /// <param name="externalTextureView">The fixed Apply upload view.</param>
        /// <param name="externalTargetId">The layer target supplying a composite view.</param>
        public WebGPUOrderedJournalEntry(
            WebGPUSceneRange range,
            int targetId,
            WebGPUOrderedExternalSourceKind externalSourceKind,
            WGPUTextureViewImpl* externalTextureView,
            int externalTargetId)
        {
            this.Range = range;
            this.TargetId = targetId;
            this.ExternalSourceKind = externalSourceKind;
            this.ExternalTextureView = externalTextureView;
            this.ExternalTargetId = externalTargetId;
            this.StatusStart = 0;
            this.StatusCount = 0;
            this.FullTileHeight = 0;
            this.IsChunked = false;
        }

        /// <summary>
        /// Gets the retained encoded range.
        /// </summary>
        public WebGPUSceneRange Range { get; }

        /// <summary>
        /// Gets the stable target identity receiving the range.
        /// </summary>
        public int TargetId { get; }

        /// <summary>
        /// Gets the kind of external texture source retained by the range.
        /// </summary>
        public WebGPUOrderedExternalSourceKind ExternalSourceKind { get; }

        /// <summary>
        /// Gets the fixed Apply upload texture view.
        /// </summary>
        public WGPUTextureViewImpl* ExternalTextureView { get; }

        /// <summary>
        /// Gets the stable layer target identity supplying a composite texture.
        /// </summary>
        public int ExternalTargetId { get; }

        /// <summary>
        /// Gets the first allocator record written by the latest range attempt.
        /// </summary>
        public int StatusStart { get; private set; }

        /// <summary>
        /// Gets the number of allocator records written by the latest range attempt.
        /// </summary>
        public int StatusCount { get; private set; }

        /// <summary>
        /// Gets the full range height in tile rows for chunk-status expansion.
        /// </summary>
        public uint FullTileHeight { get; private set; }

        /// <summary>
        /// Gets a value indicating whether the latest range attempt used chunk-local capacities.
        /// </summary>
        public bool IsChunked { get; private set; }

        /// <summary>
        /// Replaces status metadata after an initial or replayed submission.
        /// </summary>
        /// <param name="statusStart">The first status record for this attempt.</param>
        /// <param name="statusCount">The number of records emitted by this attempt.</param>
        /// <param name="fullTileHeight">The full range height in tile rows.</param>
        /// <param name="isChunked">Whether the attempt used chunk-local capacities.</param>
        public void SetStatus(int statusStart, int statusCount, uint fullTileHeight, bool isChunked)
        {
            this.StatusStart = statusStart;
            this.StatusCount = statusCount;
            this.FullTileHeight = fullTileHeight;
            this.IsChunked = isChunked;
        }
    }

    /// <summary>
    /// Describes one aligned and clipped Apply pixel region inside the mixed barrier buffer.
    /// </summary>
    private readonly struct WebGPUApplyReadbackRegion
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="WebGPUApplyReadbackRegion"/> struct.
        /// </summary>
        /// <param name="clippedSource">The source rectangle clipped to its logical target.</param>
        /// <param name="destinationX">The destination X offset inside the full Apply image.</param>
        /// <param name="destinationY">The destination Y offset inside the full Apply image.</param>
        /// <param name="bufferOffset">The 256-byte-aligned buffer offset.</param>
        /// <param name="packedRowBytes">The meaningful pixel byte count in each row.</param>
        /// <param name="readbackRowBytes">The 256-byte-aligned GPU row pitch.</param>
        public WebGPUApplyReadbackRegion(
            Rectangle clippedSource,
            int destinationX,
            int destinationY,
            ulong bufferOffset,
            int packedRowBytes,
            int readbackRowBytes)
        {
            this.ClippedSource = clippedSource;
            this.DestinationX = destinationX;
            this.DestinationY = destinationY;
            this.BufferOffset = bufferOffset;
            this.PackedRowBytes = packedRowBytes;
            this.ReadbackRowBytes = readbackRowBytes;
        }

        /// <summary>
        /// Gets the clipped logical source rectangle.
        /// </summary>
        public Rectangle ClippedSource { get; }

        /// <summary>
        /// Gets the destination X offset inside the full Apply image.
        /// </summary>
        public int DestinationX { get; }

        /// <summary>
        /// Gets the destination Y offset inside the full Apply image.
        /// </summary>
        public int DestinationY { get; }

        /// <summary>
        /// Gets the aligned byte offset inside the barrier buffer.
        /// </summary>
        public ulong BufferOffset { get; }

        /// <summary>
        /// Gets the meaningful byte count in one pixel row.
        /// </summary>
        public int PackedRowBytes { get; }

        /// <summary>
        /// Gets the GPU-aligned byte pitch between consecutive rows.
        /// </summary>
        public int ReadbackRowBytes { get; }

        /// <summary>
        /// Gets the exclusive buffer end offset of this clipped pixel region.
        /// </summary>
        public ulong EndOffset
            => checked(this.BufferOffset + ((ulong)this.ReadbackRowBytes * (ulong)this.ClippedSource.Height));
    }
}

#pragma warning restore SA1201
