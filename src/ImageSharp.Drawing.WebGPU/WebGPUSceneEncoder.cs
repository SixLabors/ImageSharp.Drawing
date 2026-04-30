// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

#pragma warning disable SA1201 // Scene-model types are grouped together in one file.

using System.Buffers;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using SixLabors.ImageSharp.Memory;
using SixLabors.ImageSharp.PixelFormats;

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// Builds the flush-scoped scene payload consumed by the staged WebGPU rasterizer.
/// </summary>
internal static class WebGPUSceneEncoder
{
    private const int GradientWidth = 512;
    private const int PathGradientHeaderWordCount = 4;
    private const int PathGradientEdgeWordCount = 6;
    private const int StyleWordCount = 5;
    private const int TransformWordCount = 9;
    private const int TileWidth = 16;
    private const int TileHeight = 16;
    private const float PointStrokeSegmentHalfLength = 1F / 128F;
    private const float StrokeMicroSegmentEpsilon = 1F / 64F;
    private static readonly GraphicsOptions DefaultClipGraphicsOptions = new();

    /// <summary>
    /// Tags packed into the byte path-tag stream consumed by the WebGPU shaders.
    /// </summary>
    /// <remarks>
    /// The low two bits store the segment type; the remaining bits are flags/count markers.
    /// Values must match the PATH_TAG_* constants in pathtag.wgsl.
    /// </remarks>
    [Flags]
    private enum PathTag : byte
    {
        None = 0,
        LineTo = 1 << 0,
        QuadTo = 1 << 1,
        CubicTo = LineTo | QuadTo,
        SubpathEnd = 1 << 2,
        F32 = 1 << 3,
        LineToF32 = F32 | LineTo,
        QuadToF32 = F32 | QuadTo,
        Path = 1 << 4,
        Transform = 1 << 5,
        Style = 1 << 6
    }

    /// <summary>
    /// Flags packed into the first style word consumed by the WebGPU shaders.
    /// </summary>
    /// <remarks>
    /// Cap styles are stored as two two-bit fields: end caps in bits 24-25 and start caps
    /// in bits 26-27. The flatten shader shifts the start-cap field down before decoding it.
    /// </remarks>
    [Flags]
    private enum StyleFlags : uint
    {
        None = 0U,
        JoinMiterRevert = 1U << 22,
        JoinMiterRound = 1U << 23,
        EndCapSquare = 1U << 24,
        EndCapRound = 1U << 25,
        StartCapSquare = EndCapSquare << 2,
        StartCapRound = EndCapRound << 2,
        JoinMiter = 1U << 28,
        JoinRound = 1U << 29,
        Fill = 1U << 30,
        Stroke = 1U << 31
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static byte PackPathTag(PathTag tag) => (byte)tag;

    /// <summary>
    /// Encodes composition commands into flush-scoped scene buffers.
    /// </summary>
    /// <param name="scene">The command batch to encode.</param>
    /// <param name="targetBounds">The root target bounds used for target-local coordinate conversion.</param>
    /// <param name="allocator">The allocator used for temporary and packed scene storage.</param>
    /// <param name="maxDegreeOfParallelism">The maximum degree of parallelism used for encoder planning and partitioned encoding.</param>
    /// <param name="encodedScene">Receives the encoded scene on success.</param>
    /// <param name="error">Receives the staged-scene support failure reason when encoding fails.</param>
    /// <returns><see langword="true"/> when the scene encoded successfully; otherwise, <see langword="false"/>.</returns>
    public static bool TryEncode(
        DrawingCommandBatch scene,
        in Rectangle targetBounds,
        MemoryAllocator allocator,
        int maxDegreeOfParallelism,
        out WebGPUEncodedScene encodedScene,
        out string? error)
    {
        if (scene.CommandCount == 0)
        {
            encodedScene = WebGPUEncodedScene.Empty;
            error = null;
            return true;
        }

        int firstTargetRowBandIndex = targetBounds.Top / TileHeight;
        int lastTargetRowBandIndex = (targetBounds.Bottom - 1) / TileHeight;
        int targetRowCount = (lastTargetRowBandIndex - firstTargetRowBandIndex) + 1;
        int commandCount = scene.CommandCount;
        int partitionCount = GetPartitionCount(maxDegreeOfParallelism, commandCount, targetRowCount);

        SceneEncodingPlan plan = SceneEncodingPlan.Create(scene, maxDegreeOfParallelism, partitionCount);
        if (partitionCount > 1)
        {
            Rectangle targetRectangle = targetBounds;
            SceneEncodingPartition?[] partitions = new SceneEncodingPartition?[partitionCount];
            bool[] failed = new bool[partitionCount];
            string?[] errors = new string?[partitionCount];

            try
            {
                _ = Parallel.For(
                    0,
                    partitionCount,
                    CreateParallelOptions(maxDegreeOfParallelism, partitionCount),
                    (partitionIndex, loopState) =>
                    {
                        // Match the CPU retained scene builder: partitions are contiguous command ranges,
                        // and the final merge appends them by partition index to preserve timeline order.
                        int commandStart = (partitionIndex * commandCount) / partitionCount;
                        int commandEnd = ((partitionIndex + 1) * commandCount) / partitionCount;
                        SceneEncodingPlan partitionPlan = plan.GetPartitionPlan(partitionIndex);
                        SupportedSubsetSceneEncoding partitionEncoding = new(allocator, targetRectangle, partitionPlan);

                        try
                        {
                            if (!partitionEncoding.TryBuild(scene, partitionPlan, commandStart, commandEnd, out string? partitionError))
                            {
                                failed[partitionIndex] = true;
                                errors[partitionIndex] = partitionError;
                                loopState.Stop();
                                return;
                            }

                            partitions[partitionIndex] = SceneEncodingPartition.Detach(ref partitionEncoding);
                        }
                        finally
                        {
                            partitionEncoding.Dispose();
                        }
                    });
            }
            catch
            {
                DisposePartitions(partitions);
                throw;
            }

            for (int i = 0; i < failed.Length; i++)
            {
                if (failed[i])
                {
                    DisposePartitions(partitions);
                    encodedScene = WebGPUEncodedScene.Empty;
                    error = errors[i];
                    return false;
                }
            }

            int fillCount = 0;
            bool hasFineRasterizationMode = false;
            RasterizationMode fineRasterizationMode = RasterizationMode.Antialiased;
            float fineCoverageThreshold = 0F;

            for (int i = 0; i < partitions.Length; i++)
            {
                SceneEncodingPartition partition = partitions[i]!;
                fillCount += partition.FillCount;
                if (partition.VisibleFillCount == 0)
                {
                    continue;
                }

                if (!hasFineRasterizationMode)
                {
                    fineRasterizationMode = partition.FineRasterizationMode;
                    fineCoverageThreshold = partition.FineCoverageThreshold;
                    hasFineRasterizationMode = true;
                    continue;
                }

                if (partition.FineRasterizationMode != fineRasterizationMode ||
                    partition.FineCoverageThreshold != fineCoverageThreshold)
                {
                    DisposePartitions(partitions);
                    encodedScene = WebGPUEncodedScene.Empty;
                    error = "The staged WebGPU scene pipeline does not support mixed fine rasterization modes or thresholds within one flush.";
                    return false;
                }
            }

            if (fillCount == 0)
            {
                DisposePartitions(partitions);
                encodedScene = WebGPUEncodedScene.Empty;
                error = null;
                return true;
            }

            try
            {
                encodedScene = SupportedSubsetSceneResolver.Resolve(partitions, targetBounds, allocator, fineRasterizationMode, fineCoverageThreshold);
                error = null;
                return true;
            }
            finally
            {
                DisposePartitions(partitions);
            }
        }

        SupportedSubsetSceneEncoding encoding = new(allocator, targetBounds, plan);
        try
        {
            if (!encoding.TryBuild(scene, plan, out error))
            {
                encodedScene = WebGPUEncodedScene.Empty;
                return false;
            }

            if (encoding.IsEmpty)
            {
                encodedScene = WebGPUEncodedScene.Empty;
                error = null;
                return true;
            }

            encodedScene = SupportedSubsetSceneResolver.Resolve(ref encoding, targetBounds, allocator);
            error = null;
            return true;
        }
        finally
        {
            encoding.Dispose();
        }
    }

    /// <summary>
    /// Disposes any partition encodings that were produced before the parallel operation completed.
    /// </summary>
    private static void DisposePartitions(SceneEncodingPartition?[] partitions)
    {
        for (int i = 0; i < partitions.Length; i++)
        {
            partitions[i]?.Dispose();
        }
    }

    /// <summary>
    /// Encoding plan produced before the mutable scene streams are allocated.
    /// </summary>
    private readonly struct SceneEncodingPlan
    {
        private readonly LinearGeometry?[]? geometries;

        public SceneEncodingPlan(
            int pathTagCapacity,
            int pathDataWordCapacity,
            int drawTagCapacity,
            int drawDataWordCapacity,
            int transformWordCapacity,
            int styleWordCapacity,
            int gradientPixelCapacity,
            int pathGradientDataWordCapacity,
            LinearGeometry?[]? geometries,
            SceneEncodingPlan[]? partitionPlans)
        {
            this.PathTagCapacity = pathTagCapacity;
            this.PathDataWordCapacity = pathDataWordCapacity;
            this.DrawTagCapacity = drawTagCapacity;
            this.DrawDataWordCapacity = drawDataWordCapacity;
            this.TransformWordCapacity = transformWordCapacity;
            this.StyleWordCapacity = styleWordCapacity;
            this.GradientPixelCapacity = gradientPixelCapacity;
            this.PathGradientDataWordCapacity = pathGradientDataWordCapacity;
            this.geometries = geometries;
            this.PartitionPlans = partitionPlans;
        }

        /// <summary>
        /// Gets the initial byte capacity for path tags.
        /// </summary>
        public int PathTagCapacity { get; }

        /// <summary>
        /// Gets the initial word capacity for path data.
        /// </summary>
        public int PathDataWordCapacity { get; }

        /// <summary>
        /// Gets the initial draw-tag capacity.
        /// </summary>
        public int DrawTagCapacity { get; }

        /// <summary>
        /// Gets the initial word capacity for draw data.
        /// </summary>
        public int DrawDataWordCapacity { get; }

        /// <summary>
        /// Gets the initial word capacity for transforms.
        /// </summary>
        public int TransformWordCapacity { get; }

        /// <summary>
        /// Gets the initial word capacity for styles.
        /// </summary>
        public int StyleWordCapacity { get; }

        /// <summary>
        /// Gets the initial gradient-pixel capacity.
        /// </summary>
        public int GradientPixelCapacity { get; }

        /// <summary>
        /// Gets the initial path-gradient data capacity.
        /// </summary>
        public int PathGradientDataWordCapacity { get; }

        /// <summary>
        /// Gets per-partition capacity plans produced by the parallel planning pass.
        /// </summary>
        public SceneEncodingPlan[]? PartitionPlans { get; }

        /// <summary>
        /// Creates an encoding plan for the supplied command batch.
        /// </summary>
        public static SceneEncodingPlan Create(
            DrawingCommandBatch scene,
            int maxDegreeOfParallelism,
            int partitionCount)
        {
            int commandCount = scene.CommandCount;
            if (partitionCount <= 1)
            {
                return CreateDefault(commandCount);
            }

            LinearGeometry?[] geometries = new LinearGeometry?[commandCount];
            SceneCapacityEstimate[] estimates = new SceneCapacityEstimate[partitionCount];

            _ = Parallel.For(
                0,
                partitionCount,
                CreateParallelOptions(maxDegreeOfParallelism, partitionCount),
                partitionIndex =>
                {
                    int commandStart = (partitionIndex * commandCount) / partitionCount;
                    int commandEnd = ((partitionIndex + 1) * commandCount) / partitionCount;
                    SceneCapacityEstimate estimate = default;

                    for (int i = commandStart; i < commandEnd; i++)
                    {
                        estimate.Add(EstimateCommand(scene.Commands[i], i, geometries));
                    }

                    estimates[partitionIndex] = estimate;
                });

            SceneCapacityEstimate total = SceneCapacityEstimate.CreateInitial();
            SceneEncodingPlan[] partitionPlans = new SceneEncodingPlan[partitionCount];
            for (int i = 0; i < estimates.Length; i++)
            {
                total.Add(estimates[i]);

                int commandStart = (i * commandCount) / partitionCount;
                int commandEnd = ((i + 1) * commandCount) / partitionCount;
                partitionPlans[i] = estimates[i].ToPlan(commandEnd - commandStart, geometries, null);
            }

            return total.ToPlan(commandCount, geometries, partitionPlans);
        }

        /// <summary>
        /// Gets prepared geometry for a command when the parallel plan computed it.
        /// </summary>
        public LinearGeometry? GetGeometry(int commandIndex)
            => this.geometries?[commandIndex];

        /// <summary>
        /// Gets the stream capacity plan for one contiguous command-range partition.
        /// </summary>
        public readonly SceneEncodingPlan GetPartitionPlan(int partitionIndex)
            => this.PartitionPlans is null ? this : this.PartitionPlans[partitionIndex];

        public static SceneEncodingPlan CreateDefault(int commandCount)
            => new(
                Math.Max(commandCount * 8, 256),
                Math.Max(commandCount * 16, 256),
                Math.Max(commandCount, 16),
                Math.Max(commandCount, 16),
                16,
                Math.Max(commandCount * StyleWordCount, 16),
                16,
                16,
                null,
                null);
    }

    /// <summary>
    /// Accumulates initial stream capacities for one large-batch encoder plan.
    /// </summary>
    private struct SceneCapacityEstimate
    {
        public long PathTagCount;
        public long PathDataWordCount;
        public long DrawTagCount;
        public long DrawDataWordCount;
        public long TransformWordCount;
        public long StyleWordCount;
        public long GradientPixelCount;
        public long PathGradientDataWordCount;

        /// <summary>
        /// Creates the initial estimate for the transform emitted by every scene.
        /// </summary>
        public static SceneCapacityEstimate CreateInitial()
            => new()
            {
                PathTagCount = 1,
                TransformWordCount = WebGPUSceneEncoder.TransformWordCount
            };

        /// <summary>
        /// Adds another estimate into this accumulator.
        /// </summary>
        public void Add(in SceneCapacityEstimate other)
        {
            this.PathTagCount += other.PathTagCount;
            this.PathDataWordCount += other.PathDataWordCount;
            this.DrawTagCount += other.DrawTagCount;
            this.DrawDataWordCount += other.DrawDataWordCount;
            this.TransformWordCount += other.TransformWordCount;
            this.StyleWordCount += other.StyleWordCount;
            this.GradientPixelCount += other.GradientPixelCount;
            this.PathGradientDataWordCount += other.PathGradientDataWordCount;
        }

        /// <summary>
        /// Adds capacity for one encoded fill path.
        /// </summary>
        public void AddFill(LinearGeometryInfo geometryInfo, Brush brush)
        {
            uint drawTag = GetDrawTag(brush);
            this.PathTagCount += geometryInfo.SegmentCount + 3L;
            this.PathDataWordCount += (geometryInfo.ContourCount * 2L) + (geometryInfo.SegmentCount * 2L);
            this.AddDrawPayload(drawTag, brush);
        }

        /// <summary>
        /// Adds capacity for one encoded stroked path.
        /// </summary>
        public void AddStroke(LinearGeometry geometry, Brush brush)
        {
            uint drawTag = GetDrawTag(brush);
            int markerWordCount = 0;

            for (int i = 0; i < geometry.Contours.Count; i++)
            {
                markerWordCount += geometry.Contours[i].IsClosed ? 2 : 4;
            }

            this.PathTagCount += geometry.Info.SegmentCount + geometry.Info.ContourCount + 3L;
            this.PathDataWordCount += (geometry.Info.ContourCount * 2L) + (geometry.Info.SegmentCount * 2L) + markerWordCount;
            this.AddDrawPayload(drawTag, brush);
        }

        /// <summary>
        /// Adds capacity for one encoded explicit two-point stroke.
        /// </summary>
        public void AddOpenSegmentStroke(Brush brush)
        {
            uint drawTag = GetDrawTag(brush);
            this.PathTagCount += 4;
            this.PathDataWordCount += 8;
            this.AddDrawPayload(drawTag, brush);
        }

        /// <summary>
        /// Adds capacity for one begin-layer marker.
        /// </summary>
        public void AddBeginLayer()
        {
            this.PathTagCount += 6;
            this.PathDataWordCount += 10;
            this.DrawTagCount++;
            this.DrawDataWordCount += 2;
            this.StyleWordCount += WebGPUSceneEncoder.StyleWordCount;
        }

        /// <summary>
        /// Adds capacity for one end-layer marker.
        /// </summary>
        public void AddEndLayer()
        {
            this.PathTagCount++;
            this.DrawTagCount++;
        }

        /// <summary>
        /// Converts the accumulated counts into an encoder plan.
        /// </summary>
        public readonly SceneEncodingPlan ToPlan(
            int commandCount,
            LinearGeometry?[] geometries,
            SceneEncodingPlan[]? partitionPlans)
        {
            SceneEncodingPlan defaults = SceneEncodingPlan.CreateDefault(commandCount);

            return new SceneEncodingPlan(
                Math.Max(defaults.PathTagCapacity, checked((int)this.PathTagCount)),
                Math.Max(defaults.PathDataWordCapacity, checked((int)this.PathDataWordCount)),
                Math.Max(defaults.DrawTagCapacity, checked((int)this.DrawTagCount)),
                Math.Max(defaults.DrawDataWordCapacity, checked((int)this.DrawDataWordCount)),
                Math.Max(defaults.TransformWordCapacity, checked((int)this.TransformWordCount)),
                Math.Max(defaults.StyleWordCapacity, checked((int)this.StyleWordCount)),
                Math.Max(defaults.GradientPixelCapacity, checked((int)this.GradientPixelCount)),
                Math.Max(defaults.PathGradientDataWordCapacity, checked((int)this.PathGradientDataWordCount)),
                geometries,
                partitionPlans);
        }

        private void AddDrawPayload(uint drawTag, Brush brush)
        {
            this.DrawTagCount++;
            this.DrawDataWordCount += GetDrawDataWordCount(drawTag);
            this.TransformWordCount += WebGPUSceneEncoder.TransformWordCount;
            this.StyleWordCount += WebGPUSceneEncoder.StyleWordCount;

            if (DrawTagUsesGradientRamp(drawTag))
            {
                this.GradientPixelCount += GradientWidth;
            }

            this.PathGradientDataWordCount += GetPathGradientDataWordCount(brush);
        }
    }

    /// <summary>
    /// Estimates one command and stores prepared geometry for the sequential append phase.
    /// </summary>
    private static SceneCapacityEstimate EstimateCommand(
        CompositionSceneCommand command,
        int commandIndex,
        LinearGeometry?[] geometries)
    {
        SceneCapacityEstimate estimate = default;

        if (command is PathCompositionSceneCommand pathCommand)
        {
            CompositionCommand composition = pathCommand.Command;
            switch (composition.Kind)
            {
                case CompositionCommandKind.FillLayer:
                    if (!IsSupportedBrush(composition.Brush))
                    {
                        return estimate;
                    }

                    LinearGeometry fillGeometry = composition.SourcePath.ToLinearGeometry(ExtractScale(composition.Transform));
                    geometries[commandIndex] = fillGeometry;
                    estimate.AddFill(fillGeometry.Info, composition.Brush);
                    return estimate;

                case CompositionCommandKind.BeginLayer:
                    estimate.AddBeginLayer();
                    return estimate;

                case CompositionCommandKind.EndLayer:
                    estimate.AddEndLayer();
                    return estimate;
            }
        }

        if (command is StrokePathCompositionSceneCommand strokePathCommand)
        {
            StrokePathCommand composition = strokePathCommand.Command;
            if (!IsSupportedBrush(composition.Brush))
            {
                return estimate;
            }

            LinearGeometry strokeGeometry = composition.SourcePath.ToLinearGeometry(ExtractScale(composition.Transform));
            geometries[commandIndex] = strokeGeometry;
            estimate.AddStroke(strokeGeometry, composition.Brush);
            return estimate;
        }

        if (command is LineSegmentCompositionSceneCommand lineSegmentCommand)
        {
            StrokeLineSegmentCommand composition = lineSegmentCommand.Command;
            if (IsSupportedBrush(composition.Brush))
            {
                estimate.AddOpenSegmentStroke(composition.Brush);
            }

            return estimate;
        }

        StrokePolylineCommand polyline = ((PolylineCompositionSceneCommand)command).Command;
        if (!IsSupportedBrush(polyline.Brush))
        {
            return estimate;
        }

        LinearGeometry polylineGeometry = LinearGeometry.CreateOpenPolyline(polyline.SourcePoints, ExtractScale(polyline.Transform));
        geometries[commandIndex] = polylineGeometry;
        estimate.AddStroke(polylineGeometry, polyline.Brush);
        return estimate;
    }

    /// <summary>
    /// Computes the number of useful partitions using the same limits as the retained CPU scene builder.
    /// </summary>
    private static int GetPartitionCount(
        int maxDegreeOfParallelism,
        int workItemCount,
        int secondaryLimit)
        => Math.Min(
            maxDegreeOfParallelism == -1 ? Environment.ProcessorCount : maxDegreeOfParallelism,
            Math.Min(workItemCount, secondaryLimit));

    /// <summary>
    /// Creates the parallel options for an already-sized partitioned encoder operation.
    /// </summary>
    private static ParallelOptions CreateParallelOptions(int maxDegreeOfParallelism, int partitionCount)
        => new() { MaxDegreeOfParallelism = Math.Min(maxDegreeOfParallelism, partitionCount) };

    /// <summary>
    /// Mutable flush-scoped encoder state used while appending supported commands into contiguous scene streams.
    /// </summary>
    private ref struct SupportedSubsetSceneEncoding
    {
        private bool hasLastStyle;
        private bool streamsDetached;
        private bool gradientPixelsDetached;
        private bool pathGradientDataDetached;
        private long estimatedPathRowCount;
        private uint lastStyle0;
        private uint lastStyle1;
        private uint lastStyle2;
        private uint lastStyle3;
        private uint lastStyle4;
        private GpuSceneTransform lastTransform;
        private readonly Rectangle rootTargetBounds;
        private List<Rectangle>? openLayerBounds;

        /// <summary>
        /// Initializes a new instance of the <see cref="SupportedSubsetSceneEncoding"/> struct.
        /// </summary>
        /// <param name="allocator">The allocator used for all temporary scene streams.</param>
        /// <param name="rootTargetBounds">The root target bounds used for target-local coordinate conversion.</param>
        /// <param name="plan">The large-batch encoding plan used for initial stream sizing and prepared geometry.</param>
        internal SupportedSubsetSceneEncoding(
            MemoryAllocator allocator,
            in Rectangle rootTargetBounds,
            in SceneEncodingPlan plan)
        {
            this.PathTags = new OwnedStream<byte>(allocator, plan.PathTagCapacity);
            this.PathData = new OwnedStream<uint>(allocator, plan.PathDataWordCapacity);
            this.DrawTags = new OwnedStream<uint>(allocator, plan.DrawTagCapacity);
            this.DrawData = new OwnedStream<uint>(allocator, plan.DrawDataWordCapacity);
            this.Transforms = new OwnedStream<uint>(allocator, plan.TransformWordCapacity);
            this.Styles = new OwnedStream<uint>(allocator, plan.StyleWordCapacity);

            // Gradient payloads are sparse in common scenes, so grow these only when a gradient brush is encoded.
            this.GradientPixels = new OwnedStream<uint>(allocator, plan.GradientPixelCapacity);
            this.PathGradientData = new OwnedStream<uint>(allocator, plan.PathGradientDataWordCapacity);
            this.Images = [];
            this.FillCount = 0;
            this.PathCount = 0;
            this.LineCount = 0;
            this.InfoWordCount = 0;
            this.ClipCount = 0;
            this.GradientRowCount = 0;
            this.hasLastStyle = false;
            this.streamsDetached = false;
            this.gradientPixelsDetached = false;
            this.pathGradientDataDetached = false;
            this.lastStyle0 = 0;
            this.lastStyle1 = 0;
            this.lastStyle2 = 0;
            this.lastStyle3 = 0;
            this.lastStyle4 = 0;
            this.rootTargetBounds = rootTargetBounds;
            this.lastTransform = GpuSceneTransform.Identity;
            this.openLayerBounds = null;
            this.estimatedPathRowCount = 0;
            this.VisibleFillCount = 0;
            this.FineRasterizationMode = RasterizationMode.Antialiased;
            this.FineCoverageThreshold = 0F;

            this.PathTags.Add(PackPathTag(PathTag.Transform));
            AppendIdentityTransform(ref this.Transforms);
        }

        /// <summary>
        /// Gets or sets the encoded path-tag byte stream.
        /// </summary>
        public OwnedStream<byte> PathTags;

        /// <summary>
        /// Gets or sets the encoded path-coordinate stream.
        /// </summary>
        public OwnedStream<uint> PathData;

        /// <summary>
        /// Gets or sets the encoded draw-tag stream.
        /// </summary>
        public OwnedStream<uint> DrawTags;

        /// <summary>
        /// Gets or sets the encoded draw-data stream.
        /// </summary>
        public OwnedStream<uint> DrawData;

        /// <summary>
        /// Gets or sets the encoded transform stream.
        /// </summary>
        public OwnedStream<uint> Transforms;

        /// <summary>
        /// Gets or sets the encoded style stream.
        /// </summary>
        public OwnedStream<uint> Styles;

        /// <summary>
        /// Gets or sets the packed gradient-ramp pixel stream.
        /// </summary>
        public OwnedStream<uint> GradientPixels;

        /// <summary>
        /// Gets or sets the encoded path-gradient payload stream.
        /// </summary>
        public OwnedStream<uint> PathGradientData;

        /// <summary>
        /// Gets or sets the deferred image payload descriptors that are patched after atlas creation.
        /// </summary>
        public List<GpuImageDescriptor> Images;

        /// <summary>
        /// Gets the number of emitted fill records.
        /// </summary>
        public int FillCount { get; private set; }

        /// <summary>
        /// Gets the number of visible fills accepted by staged-scene validation.
        /// </summary>
        public int VisibleFillCount { get; private set; }

        /// <summary>
        /// Gets the number of emitted paths.
        /// </summary>
        public int PathCount { get; private set; }

        /// <summary>
        /// Gets the number of emitted non-horizontal line segments.
        /// </summary>
        public int LineCount { get; private set; }

        /// <summary>
        /// Gets the total info-word count implied by the emitted draw tags.
        /// </summary>
        public int InfoWordCount { get; private set; }

        /// <summary>
        /// Gets the number of emitted clip records.
        /// </summary>
        public int ClipCount { get; private set; }

        /// <summary>
        /// Gets the number of emitted gradient-ramp rows.
        /// </summary>
        public int GradientRowCount { get; private set; }

        /// <summary>
        /// Gets the CPU-side estimate of the number of active tile rows referenced by sparse path metadata.
        /// </summary>
        public readonly int EstimatedPathRowCount => (int)Math.Min(this.estimatedPathRowCount, int.MaxValue);

        /// <summary>
        /// Gets the flush-wide fine rasterization mode selected while encoding visible fills.
        /// </summary>
        public RasterizationMode FineRasterizationMode { get; private set; }

        /// <summary>
        /// Gets the aliased coverage threshold consumed by the fine pass when aliased mode is selected.
        /// </summary>
        public float FineCoverageThreshold { get; private set; }

        /// <summary>
        /// Gets a value indicating whether the encoding produced no fill work.
        /// </summary>
        public readonly bool IsEmpty => this.FillCount == 0;

        /// <summary>
        /// Disposes all owned stream storage that has not already been detached.
        /// </summary>
        public void Dispose()
        {
            if (!this.gradientPixelsDetached)
            {
                this.GradientPixels.Dispose();
            }

            if (!this.pathGradientDataDetached)
            {
                this.PathGradientData.Dispose();
            }

            if (!this.streamsDetached)
            {
                this.Styles.Dispose();
                this.Transforms.Dispose();
                this.DrawData.Dispose();
                this.DrawTags.Dispose();
                this.PathData.Dispose();
                this.PathTags.Dispose();
            }
        }

        /// <summary>
        /// Marks the main scene streams as detached so disposal does not free retained partition storage.
        /// </summary>
        public void MarkStreamsDetached() => this.streamsDetached = true;

        /// <summary>
        /// Marks the gradient pixel stream as detached so disposal does not free it twice.
        /// </summary>
        public void MarkGradientPixelsDetached() => this.gradientPixelsDetached = true;

        /// <summary>
        /// Marks the path-gradient payload stream as detached so disposal does not free it twice.
        /// </summary>
        public void MarkPathGradientDataDetached() => this.pathGradientDataDetached = true;

        /// <summary>
        /// Appends all supported scene operations into the mutable scene streams.
        /// </summary>
        internal bool TryBuild(
            DrawingCommandBatch scene,
            in SceneEncodingPlan plan,
            out string? error)
            => this.TryBuild(scene, plan, 0, scene.CommandCount, out error);

        /// <summary>
        /// Appends a contiguous command range into this partition's mutable scene streams.
        /// </summary>
        internal bool TryBuild(
            DrawingCommandBatch scene,
            in SceneEncodingPlan plan,
            int commandStart,
            int commandEnd,
            out string? error)
        {
            for (int i = commandStart; i < commandEnd; i++)
            {
                CompositionSceneCommand command = scene.Commands[i];
                LinearGeometry? geometry = plan.GetGeometry(i);
                if (command is PathCompositionSceneCommand pathCommand)
                {
                    if (!this.TryAppend(pathCommand.Command, geometry, out error))
                    {
                        return false;
                    }
                }
                else if (command is StrokePathCompositionSceneCommand strokePathCommand)
                {
                    if (!this.TryAppend(strokePathCommand.Command, geometry, out error))
                    {
                        return false;
                    }
                }
                else if (command is LineSegmentCompositionSceneCommand lineSegmentCommand)
                {
                    if (!this.TryAppend(lineSegmentCommand.Command, out error))
                    {
                        return false;
                    }
                }
                else
                {
                    if (!this.TryAppend(((PolylineCompositionSceneCommand)command).Command, geometry, out error))
                    {
                        return false;
                    }
                }
            }

            error = null;
            return true;
        }

        /// <summary>
        /// Appends one prepared fill-path or layer-based command to the scene streams when the command kind is supported.
        /// </summary>
        private bool TryAppend(
            in CompositionCommand command,
            LinearGeometry? geometry,
            out string? error)
        {
            switch (command.Kind)
            {
                case CompositionCommandKind.FillLayer:
                    if (!TryResolveCommand(command, out ResolvedPathCommand resolved))
                    {
                        error = null;
                        return true;
                    }

                    if (!this.TryRegisterVisibleFill(resolved.Brush, resolved.RasterizerOptions, out error))
                    {
                        return false;
                    }

                    Matrix4x4 fillResidual = ComputeResidual(ExtractScale(command.Transform), command.Transform);
                    this.AppendTransformIfChanged(GetSceneTransform(fillResidual, resolved.RasterizerOptions.SamplingOrigin));

                    this.AppendPlainFill(resolved, geometry);
                    error = null;
                    return true;

                case CompositionCommandKind.BeginLayer:
                    this.AppendBeginLayer(command);
                    error = null;
                    return true;

                case CompositionCommandKind.EndLayer:
                    this.AppendEndLayer();
                    error = null;
                    return true;
            }

            error = null;
            return true;
        }

        /// <summary>
        /// Appends one prepared stroked path command to the scene streams.
        /// </summary>
        private bool TryAppend(
            in StrokePathCommand command,
            LinearGeometry? geometry,
            out string? error)
        {
            if (!TryResolveCommand(command, out ResolvedPathCommand resolved))
            {
                error = null;
                return true;
            }

            if (!this.TryRegisterVisibleFill(resolved.Brush, resolved.RasterizerOptions, out error))
            {
                return false;
            }

            Matrix4x4 strokeResidual = ComputeResidual(ExtractScale(command.Transform), command.Transform);
            this.AppendTransformIfChanged(GetSceneTransform(strokeResidual, resolved.RasterizerOptions.SamplingOrigin));
            this.AppendPlainStroke(resolved, command.Pen, geometry);
            error = null;
            return true;
        }

        /// <summary>
        /// Appends one prepared explicit line-segment stroke to the scene streams.
        /// </summary>
        private bool TryAppend(in StrokeLineSegmentCommand command, out string? error)
        {
            if (!TryResolveCommand(command, out ResolvedLineSegmentCommand resolved))
            {
                error = null;
                return true;
            }

            if (!this.TryRegisterVisibleFill(resolved.Brush, resolved.RasterizerOptions, out error))
            {
                return false;
            }

            Vector2 segmentScale = ExtractScale(command.Transform);
            Matrix4x4 segmentResidual = ComputeResidual(segmentScale, command.Transform);
            this.AppendTransformIfChanged(GetSceneTransform(segmentResidual, resolved.RasterizerOptions.SamplingOrigin));
            PointF start = new(resolved.Start.X * segmentScale.X, resolved.Start.Y * segmentScale.Y);
            PointF end = new(resolved.End.X * segmentScale.X, resolved.End.Y * segmentScale.Y);
            float widthScale = GetTransformWidthScale(command.Transform);
            this.AppendExplicitStroke(
                resolved.Brush,
                resolved.GraphicsOptions,
                resolved.RasterizerOptions,
                resolved.DestinationOffset,
                resolved.BrushBounds,
                resolved.Pen,
                widthScale,
                command.Transform,
                segmentScale,
                start,
                end);
            error = null;
            return true;
        }

        /// <summary>
        /// Appends one prepared explicit polyline stroke to the scene streams.
        /// </summary>
        private bool TryAppend(
            in StrokePolylineCommand command,
            LinearGeometry? geometry,
            out string? error)
        {
            if (!TryResolveCommand(command, out ResolvedPolylineCommand resolved))
            {
                error = null;
                return true;
            }

            if (!this.TryRegisterVisibleFill(resolved.Brush, resolved.RasterizerOptions, out error))
            {
                return false;
            }

            Vector2 polylineScale = ExtractScale(command.Transform);
            Matrix4x4 polylineResidual = ComputeResidual(polylineScale, command.Transform);
            this.AppendTransformIfChanged(GetSceneTransform(polylineResidual, resolved.RasterizerOptions.SamplingOrigin));
            geometry ??= LinearGeometry.CreateOpenPolyline(resolved.Points, polylineScale);
            float widthScale = GetTransformWidthScale(command.Transform);
            this.AppendExplicitStroke(
                resolved.Brush,
                resolved.GraphicsOptions,
                resolved.RasterizerOptions,
                resolved.DestinationOffset,
                resolved.BrushBounds,
                resolved.Pen,
                widthScale,
                command.Transform,
                polylineScale,
                geometry);
            error = null;
            return true;
        }

        private bool TryRegisterVisibleFill(Brush brush, in RasterizerOptions options, out string? error)
        {
            if (!IsSupportedBrush(brush))
            {
                error = $"The staged WebGPU scene pipeline does not support brush type '{brush.GetType().Name}'.";
                return false;
            }

            this.VisibleFillCount++;
            if (this.VisibleFillCount == 1)
            {
                this.FineRasterizationMode = options.RasterizationMode;
                this.FineCoverageThreshold = options.AntialiasThreshold;
                error = null;
                return true;
            }

            if (options.RasterizationMode != this.FineRasterizationMode ||
                options.AntialiasThreshold != this.FineCoverageThreshold)
            {
                error = "The staged WebGPU scene pipeline does not support mixed fine rasterization modes or thresholds within one flush.";
                return false;
            }

            error = null;
            return true;
        }

        /// <summary>
        /// Emits a transform tag + data if the transform differs from the last one emitted.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void AppendTransformIfChanged(Matrix4x4 matrix)
        {
            GpuSceneTransform transform = GpuSceneTransform.FromMatrix4x4(matrix);
            if (transform.Equals(this.lastTransform))
            {
                return;
            }

            this.PathTags.Add(PackPathTag(PathTag.Transform));
            AppendTransform(transform, ref this.Transforms);
            this.lastTransform = transform;
        }

        // Path geometry is baked at device-space scale via ToLinearGeometry(scale); the scene-side
        // transform carries the rotation/shear/translation/perspective residual composed with the
        // sampling-origin half-pixel shift, so the flatten shader lands each point in device space
        // at the correct sampling origin in one transform_apply per vertex.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Matrix4x4 GetSceneTransform(Matrix4x4 residual, RasterizerSamplingOrigin samplingOrigin)
            => samplingOrigin == RasterizerSamplingOrigin.PixelCenter
                ? residual * Matrix4x4.CreateTranslation(0.5f, 0.5f, 0f)
                : residual;

        /// <summary>
        /// Encodes one visible fill command into the path, draw, style, and auxiliary payload streams.
        /// </summary>
        private void AppendPlainFill(
            in ResolvedPathCommand command,
            LinearGeometry? geometry)
        {
            uint drawTag = GetDrawTag(command.Brush);
            GpuSceneDrawMonoid drawTagMonoid = GpuSceneDrawTag.Map(drawTag);
            (uint style0, uint style1, uint style2, uint style3, uint style4) = GetFillStyle(command.GraphicsOptions, command.RasterizerOptions.IntersectionRule);
            int pathTagCheckpoint = this.PathTags.Count;
            int pathDataCheckpoint = this.PathData.Count;
            int styleCheckpoint = this.Styles.Count;
            bool appendStyle = !this.hasLastStyle ||
                style0 != this.lastStyle0 ||
                style1 != this.lastStyle1 ||
                style2 != this.lastStyle2 ||
                style3 != this.lastStyle3 ||
                style4 != this.lastStyle4;
            Vector2 scale = ExtractScale(command.Transform);
            geometry ??= command.Path.ToLinearGeometry(scale);

            // Reserve the exact words/tags this item can append before encoding so the
            // subsequent Add calls stay on the already-allocated contiguous spans.
            ReservePlainFillCapacity(
                geometry.Info,
                drawTag,
                appendStyle,
                GetPathGradientDataWordCount(command.Brush),
                ref this.PathTags,
                ref this.PathData,
                ref this.DrawTags,
                ref this.DrawData,
                ref this.Styles,
                ref this.GradientPixels,
                ref this.PathGradientData);

            if (appendStyle)
            {
                this.PathTags.Add(PackPathTag(PathTag.Style));
                this.Styles.Add(style0);
                this.Styles.Add(style1);
                this.Styles.Add(style2);
                this.Styles.Add(style3);
                this.Styles.Add(style4);
            }

            int encodedPathCount = EncodePath(
                command,
                geometry,
                this.rootTargetBounds,
                ref this.PathTags,
                ref this.PathData,
                out int geometryLineCount);

            if (encodedPathCount == 0)
            {
                this.PathTags.SetCount(pathTagCheckpoint);
                this.PathData.SetCount(pathDataCheckpoint);
                this.Styles.SetCount(styleCheckpoint);
                return;
            }

            this.hasLastStyle = true;
            this.lastStyle0 = style0;
            this.lastStyle1 = style1;
            this.lastStyle2 = style2;
            this.lastStyle3 = style3;
            this.lastStyle4 = style4;
            this.AccumulateDrawRowEstimate(command.RasterizerOptions.Interest);
            this.FillCount++;
            this.PathCount += encodedPathCount;
            this.LineCount += geometryLineCount;
            this.InfoWordCount += (int)drawTagMonoid.InfoOffset;
            this.DrawTags.Add(drawTag);
            int gradientRowCount = this.GradientRowCount;

            AppendDrawData(
                command.Brush,
                command.BrushBounds,
                command.GraphicsOptions,
                drawTag,
                scale,
                command.Transform,
                this.rootTargetBounds,
                ref this.DrawData,
                ref this.GradientPixels,
                ref this.PathGradientData,
                this.Images,
                ref gradientRowCount);
            this.GradientRowCount = gradientRowCount;
        }

        /// <summary>
        /// Encodes one visible stroke command into the path, draw, style, and auxiliary payload streams.
        /// </summary>
        private void AppendPlainStroke(
            in ResolvedPathCommand command,
            Pen pen,
            LinearGeometry? geometry)
        {
            Vector2 scale = ExtractScale(command.Transform);
            geometry ??= command.Path.ToLinearGeometry(scale);
            float widthScale = GetTransformWidthScale(command.Transform);
            uint drawTag = GetDrawTag(command.Brush);
            GpuSceneDrawMonoid drawTagMonoid = GpuSceneDrawTag.Map(drawTag);
            (uint style0, uint style1, uint style2, uint style3, uint style4) = GetStrokeStyle(command.GraphicsOptions, pen, widthScale);
            int pathTagCheckpoint = this.PathTags.Count;
            int styleCheckpoint = this.Styles.Count;
            bool appendStyle = !this.hasLastStyle ||
                style0 != this.lastStyle0 ||
                style1 != this.lastStyle1 ||
                style2 != this.lastStyle2 ||
                style3 != this.lastStyle3 ||
                style4 != this.lastStyle4;

            ReservePlainStrokeCapacity(
                geometry,
                drawTag,
                appendStyle,
                GetPathGradientDataWordCount(command.Brush),
                ref this.PathTags,
                ref this.PathData,
                ref this.DrawTags,
                ref this.DrawData,
                ref this.Styles,
                ref this.GradientPixels,
                ref this.PathGradientData);

            if (appendStyle)
            {
                this.PathTags.Add(PackPathTag(PathTag.Style));
                this.Styles.Add(style0);
                this.Styles.Add(style1);
                this.Styles.Add(style2);
                this.Styles.Add(style3);
                this.Styles.Add(style4);
            }

            int encodedPathCount = EncodeStrokePath(
                geometry,
                command.DestinationOffset,
                pen,
                widthScale,
                this.rootTargetBounds,
                ref this.PathTags,
                ref this.PathData,
                out int geometryLineCount);

            if (encodedPathCount == 0)
            {
                this.PathTags.SetCount(pathTagCheckpoint);
                this.Styles.SetCount(styleCheckpoint);
                return;
            }

            this.hasLastStyle = true;
            this.lastStyle0 = style0;
            this.lastStyle1 = style1;
            this.lastStyle2 = style2;
            this.lastStyle3 = style3;
            this.lastStyle4 = style4;
            this.AccumulateDrawRowEstimate(command.RasterizerOptions.Interest);
            this.FillCount++;
            this.PathCount += encodedPathCount;
            this.LineCount += geometryLineCount;
            this.InfoWordCount += (int)drawTagMonoid.InfoOffset;
            this.DrawTags.Add(drawTag);
            int gradientRowCount = this.GradientRowCount;

            AppendDrawData(
                command.Brush,
                command.BrushBounds,
                command.GraphicsOptions,
                drawTag,
                scale,
                command.Transform,
                this.rootTargetBounds,
                ref this.DrawData,
                ref this.GradientPixels,
                ref this.PathGradientData,
                this.Images,
                ref gradientRowCount);
            this.GradientRowCount = gradientRowCount;
        }

        /// <summary>
        /// Encodes one visible explicit stroke primitive into the path, draw, style, and auxiliary payload streams.
        /// </summary>
        private void AppendExplicitStroke(
            Brush brush,
            GraphicsOptions graphicsOptions,
            RasterizerOptions rasterizerOptions,
            Point destinationOffset,
            Rectangle brushBounds,
            Pen pen,
            float widthScale,
            Matrix4x4 transform,
            Vector2 scale,
            LinearGeometry geometry)
        {
            uint drawTag = GetDrawTag(brush);
            GpuSceneDrawMonoid drawTagMonoid = GpuSceneDrawTag.Map(drawTag);
            (uint style0, uint style1, uint style2, uint style3, uint style4) = GetStrokeStyle(graphicsOptions, pen, widthScale);
            int pathTagCheckpoint = this.PathTags.Count;
            int styleCheckpoint = this.Styles.Count;
            bool appendStyle = !this.hasLastStyle ||
                style0 != this.lastStyle0 ||
                style1 != this.lastStyle1 ||
                style2 != this.lastStyle2 ||
                style3 != this.lastStyle3 ||
                style4 != this.lastStyle4;

            ReservePlainStrokeCapacity(
                geometry,
                drawTag,
                appendStyle,
                GetPathGradientDataWordCount(brush),
                ref this.PathTags,
                ref this.PathData,
                ref this.DrawTags,
                ref this.DrawData,
                ref this.Styles,
                ref this.GradientPixels,
                ref this.PathGradientData);

            if (appendStyle)
            {
                this.PathTags.Add(PackPathTag(PathTag.Style));
                this.Styles.Add(style0);
                this.Styles.Add(style1);
                this.Styles.Add(style2);
                this.Styles.Add(style3);
                this.Styles.Add(style4);
            }

            int encodedPathCount = EncodeStrokePath(
                geometry,
                destinationOffset,
                pen,
                widthScale,
                this.rootTargetBounds,
                ref this.PathTags,
                ref this.PathData,
                out int geometryLineCount);

            if (encodedPathCount == 0)
            {
                this.PathTags.SetCount(pathTagCheckpoint);
                this.Styles.SetCount(styleCheckpoint);
                return;
            }

            this.hasLastStyle = true;
            this.lastStyle0 = style0;
            this.lastStyle1 = style1;
            this.lastStyle2 = style2;
            this.lastStyle3 = style3;
            this.lastStyle4 = style4;
            this.AccumulateDrawRowEstimate(rasterizerOptions.Interest);
            this.FillCount++;
            this.PathCount += encodedPathCount;
            this.LineCount += geometryLineCount;
            this.InfoWordCount += (int)drawTagMonoid.InfoOffset;
            this.DrawTags.Add(drawTag);
            int gradientRowCount = this.GradientRowCount;

            AppendDrawData(
                brush,
                brushBounds,
                graphicsOptions,
                drawTag,
                scale,
                transform,
                this.rootTargetBounds,
                ref this.DrawData,
                ref this.GradientPixels,
                ref this.PathGradientData,
                this.Images,
                ref gradientRowCount);
            this.GradientRowCount = gradientRowCount;
        }

        /// <summary>
        /// Encodes one visible explicit line-segment stroke primitive into the path, draw, style, and auxiliary payload streams.
        /// </summary>
        private void AppendExplicitStroke(
            Brush brush,
            GraphicsOptions graphicsOptions,
            RasterizerOptions rasterizerOptions,
            Point destinationOffset,
            Rectangle brushBounds,
            Pen pen,
            float widthScale,
            Matrix4x4 transform,
            Vector2 scale,
            PointF start,
            PointF end)
        {
            uint drawTag = GetDrawTag(brush);
            GpuSceneDrawMonoid drawTagMonoid = GpuSceneDrawTag.Map(drawTag);
            (uint style0, uint style1, uint style2, uint style3, uint style4) = GetStrokeStyle(graphicsOptions, pen, widthScale);
            int pathTagCheckpoint = this.PathTags.Count;
            int styleCheckpoint = this.Styles.Count;
            bool appendStyle = !this.hasLastStyle ||
                style0 != this.lastStyle0 ||
                style1 != this.lastStyle1 ||
                style2 != this.lastStyle2 ||
                style3 != this.lastStyle3 ||
                style4 != this.lastStyle4;

            ReservePlainStrokeCapacityForOpenSegment(
                drawTag,
                appendStyle,
                GetPathGradientDataWordCount(brush),
                ref this.PathTags,
                ref this.PathData,
                ref this.DrawTags,
                ref this.DrawData,
                ref this.Styles,
                ref this.GradientPixels,
                ref this.PathGradientData);

            if (appendStyle)
            {
                this.PathTags.Add(PackPathTag(PathTag.Style));
                this.Styles.Add(style0);
                this.Styles.Add(style1);
                this.Styles.Add(style2);
                this.Styles.Add(style3);
                this.Styles.Add(style4);
            }

            int encodedPathCount = EncodeOpenSegmentStrokePath(
                start,
                end,
                destinationOffset,
                pen,
                widthScale,
                this.rootTargetBounds,
                ref this.PathTags,
                ref this.PathData,
                out int geometryLineCount);

            if (encodedPathCount == 0)
            {
                this.PathTags.SetCount(pathTagCheckpoint);
                this.Styles.SetCount(styleCheckpoint);
                return;
            }

            this.hasLastStyle = true;
            this.lastStyle0 = style0;
            this.lastStyle1 = style1;
            this.lastStyle2 = style2;
            this.lastStyle3 = style3;
            this.lastStyle4 = style4;
            this.AccumulateDrawRowEstimate(rasterizerOptions.Interest);
            this.FillCount++;
            this.PathCount += encodedPathCount;
            this.LineCount += geometryLineCount;
            this.InfoWordCount += (int)drawTagMonoid.InfoOffset;
            this.DrawTags.Add(drawTag);
            int gradientRowCount = this.GradientRowCount;

            AppendDrawData(
                brush,
                brushBounds,
                graphicsOptions,
                drawTag,
                scale,
                transform,
                this.rootTargetBounds,
                ref this.DrawData,
                ref this.GradientPixels,
                ref this.PathGradientData,
                this.Images,
                ref gradientRowCount);
            this.GradientRowCount = gradientRowCount;
        }

        /// <summary>
        /// Encodes one begin-layer clip command as a rectangular clip path and draw record.
        /// </summary>
        private void AppendBeginLayer(in CompositionCommand command)
        {
            Rectangle layerBounds = ToTargetLocal(command.LayerBounds, this.rootTargetBounds);
            (uint style0, uint style1, uint style2, uint style3, uint style4) = GetFillStyle(DefaultClipGraphicsOptions, IntersectionRule.NonZero);
            int pathTagCheckpoint = this.PathTags.Count;
            int styleCheckpoint = this.Styles.Count;
            bool appendStyle = !this.hasLastStyle ||
                style0 != this.lastStyle0 ||
                style1 != this.lastStyle1 ||
                style2 != this.lastStyle2 ||
                style3 != this.lastStyle3 ||
                style4 != this.lastStyle4;

            // Begin-layer clip emission is fixed-size: one optional style record,
            // one rectangular clip path, and one BeginClip draw record.
            ReserveBeginLayerCapacity(
                appendStyle,
                ref this.PathTags,
                ref this.PathData,
                ref this.DrawTags,
                ref this.DrawData,
                ref this.Styles);

            if (appendStyle)
            {
                this.PathTags.Add(PackPathTag(PathTag.Style));
                this.Styles.Add(style0);
                this.Styles.Add(style1);
                this.Styles.Add(style2);
                this.Styles.Add(style3);
                this.Styles.Add(style4);
            }

            if (EncodeRectanglePath(layerBounds, ref this.PathTags, ref this.PathData, out int clipLineCount) == 0)
            {
                this.PathTags.SetCount(pathTagCheckpoint);
                this.Styles.SetCount(styleCheckpoint);
                return;
            }

            this.hasLastStyle = true;
            this.lastStyle0 = style0;
            this.lastStyle1 = style1;
            this.lastStyle2 = style2;
            this.lastStyle3 = style3;
            this.lastStyle4 = style4;
            this.AccumulateDrawRowEstimateLocal(layerBounds);
            this.PathCount++;
            this.LineCount += clipLineCount;
            this.InfoWordCount += (int)GpuSceneDrawTag.Map(GpuSceneDrawTag.BeginClip).InfoOffset;
            this.DrawTags.Add(GpuSceneDrawTag.BeginClip);
            AppendBeginClipData(command.GraphicsOptions, ref this.DrawData);
            this.ClipCount++;
            this.openLayerBounds ??= new List<Rectangle>(4);
            this.openLayerBounds.Add(layerBounds);
        }

        /// <summary>
        /// Adds one draw object's target-space interest rectangle to the sparse path-row estimate after converting it to root-target-local coordinates.
        /// </summary>
        /// <param name="absoluteInterest">The draw object's absolute raster interest bounds.</param>
        private void AccumulateDrawRowEstimate(Rectangle absoluteInterest)
            => this.AccumulateDrawRowEstimateLocal(ToTargetLocal(absoluteInterest, this.rootTargetBounds));

        /// <summary>
        /// Adds one draw object's clipped root-target-local tile-row span to the sparse path-row estimate.
        /// </summary>
        /// <param name="localBounds">The draw object's root-target-local raster interest bounds.</param>
        private void AccumulateDrawRowEstimateLocal(Rectangle localBounds)
        {
            Rectangle clippedBounds = Rectangle.Intersect(localBounds, new Rectangle(0, 0, this.rootTargetBounds.Width, this.rootTargetBounds.Height));

            if (this.openLayerBounds is { Count: > 0 })
            {
                for (int i = 0; i < this.openLayerBounds.Count && clippedBounds.Width > 0 && clippedBounds.Height > 0; i++)
                {
                    clippedBounds = Rectangle.Intersect(clippedBounds, this.openLayerBounds[i]);
                }
            }

            if (clippedBounds.Width <= 0 || clippedBounds.Height <= 0)
            {
                return;
            }

            int tileY0 = clippedBounds.Top / TileHeight;
            int tileY1 = DivideRoundUp(clippedBounds.Bottom, TileHeight);
            long rowCount = Math.Max(tileY1 - tileY0, 0);
            this.estimatedPathRowCount = Math.Min(this.estimatedPathRowCount + rowCount, int.MaxValue);
        }

        /// <summary>
        /// Encodes the closing record for the next end-layer command in the retained timeline.
        /// </summary>
        private void AppendEndLayer()
        {
            if (this.openLayerBounds is { Count: > 0 })
            {
                this.openLayerBounds.RemoveAt(this.openLayerBounds.Count - 1);
            }

            // End-layer emission is fixed-size: one EndClip draw tag and one PathTagPath
            // terminator for the zero-data end marker. In parallel encoding, a partition can
            // see the closing layer command without the matching opener, so the command itself
            // is the ordering contract rather than the local estimate stack.
            ReserveEndLayerCapacity(ref this.PathTags, ref this.DrawTags);
            this.DrawTags.Add(GpuSceneDrawTag.EndClip);
            this.PathTags.Add(PackPathTag(PathTag.Path));
            this.PathCount++;
            this.ClipCount++;
        }
    }

    /// <summary>
    /// Owns one command-range encoding produced by the parallel scene encoder.
    /// </summary>
    private sealed class SceneEncodingPartition : IDisposable
    {
        private readonly IMemoryOwner<byte> pathTagsOwner;
        private readonly IMemoryOwner<uint> pathDataOwner;
        private readonly IMemoryOwner<uint> drawTagsOwner;
        private readonly IMemoryOwner<uint> drawDataOwner;
        private readonly IMemoryOwner<uint> transformsOwner;
        private readonly IMemoryOwner<uint> stylesOwner;
        private readonly IMemoryOwner<uint>? gradientPixelsOwner;
        private readonly IMemoryOwner<uint>? pathGradientDataOwner;

        /// <summary>
        /// Initializes a new instance of the <see cref="SceneEncodingPartition"/> class.
        /// </summary>
        private SceneEncodingPartition(
            IMemoryOwner<byte> pathTagsOwner,
            IMemoryOwner<uint> pathDataOwner,
            IMemoryOwner<uint> drawTagsOwner,
            IMemoryOwner<uint> drawDataOwner,
            IMemoryOwner<uint> transformsOwner,
            IMemoryOwner<uint> stylesOwner,
            IMemoryOwner<uint>? gradientPixelsOwner,
            IMemoryOwner<uint>? pathGradientDataOwner,
            List<GpuImageDescriptor> images,
            int fillCount,
            int visibleFillCount,
            int pathCount,
            int lineCount,
            int infoWordCount,
            int clipCount,
            int gradientRowCount,
            int estimatedPathRowCount,
            int pathTagByteCount,
            int pathDataWordCount,
            int drawTagCount,
            int drawDataWordCount,
            int transformWordCount,
            int styleWordCount,
            int pathGradientDataWordCount,
            RasterizationMode fineRasterizationMode,
            float fineCoverageThreshold)
        {
            this.pathTagsOwner = pathTagsOwner;
            this.pathDataOwner = pathDataOwner;
            this.drawTagsOwner = drawTagsOwner;
            this.drawDataOwner = drawDataOwner;
            this.transformsOwner = transformsOwner;
            this.stylesOwner = stylesOwner;
            this.gradientPixelsOwner = gradientPixelsOwner;
            this.pathGradientDataOwner = pathGradientDataOwner;
            this.Images = images;
            this.FillCount = fillCount;
            this.VisibleFillCount = visibleFillCount;
            this.PathCount = pathCount;
            this.LineCount = lineCount;
            this.InfoWordCount = infoWordCount;
            this.ClipCount = clipCount;
            this.GradientRowCount = gradientRowCount;
            this.EstimatedPathRowCount = estimatedPathRowCount;
            this.PathTagByteCount = pathTagByteCount;
            this.PathDataWordCount = pathDataWordCount;
            this.DrawTagCount = drawTagCount;
            this.DrawDataWordCount = drawDataWordCount;
            this.TransformWordCount = transformWordCount;
            this.StyleWordCount = styleWordCount;
            this.PathGradientDataWordCount = pathGradientDataWordCount;
            this.FineRasterizationMode = fineRasterizationMode;
            this.FineCoverageThreshold = fineCoverageThreshold;
        }

        /// <summary>
        /// Gets the encoded path-tag bytes.
        /// </summary>
        public ReadOnlySpan<byte> PathTags => this.pathTagsOwner.Memory.Span[..this.PathTagByteCount];

        /// <summary>
        /// Gets the encoded path-data words.
        /// </summary>
        public ReadOnlySpan<uint> PathData => this.pathDataOwner.Memory.Span[..this.PathDataWordCount];

        /// <summary>
        /// Gets the encoded draw tags.
        /// </summary>
        public ReadOnlySpan<uint> DrawTags => this.drawTagsOwner.Memory.Span[..this.DrawTagCount];

        /// <summary>
        /// Gets the encoded draw-data words.
        /// </summary>
        public ReadOnlySpan<uint> DrawData => this.drawDataOwner.Memory.Span[..this.DrawDataWordCount];

        /// <summary>
        /// Gets the encoded transform words.
        /// </summary>
        public ReadOnlySpan<uint> Transforms => this.transformsOwner.Memory.Span[..this.TransformWordCount];

        /// <summary>
        /// Gets the encoded style words.
        /// </summary>
        public ReadOnlySpan<uint> Styles => this.stylesOwner.Memory.Span[..this.StyleWordCount];

        /// <summary>
        /// Gets the packed gradient-ramp pixels.
        /// </summary>
        public ReadOnlySpan<uint> GradientPixels
            => this.gradientPixelsOwner is null
                ? ReadOnlySpan<uint>.Empty
                : this.gradientPixelsOwner.Memory.Span[..(this.GradientRowCount * GradientWidth)];

        /// <summary>
        /// Gets the encoded path-gradient payload words.
        /// </summary>
        public ReadOnlySpan<uint> PathGradientData
            => this.pathGradientDataOwner is null
                ? ReadOnlySpan<uint>.Empty
                : this.pathGradientDataOwner.Memory.Span[..this.PathGradientDataWordCount];

        /// <summary>
        /// Gets the deferred image descriptors recorded by this partition.
        /// </summary>
        public List<GpuImageDescriptor> Images { get; }

        /// <summary>
        /// Gets the number of emitted fill records.
        /// </summary>
        public int FillCount { get; }

        /// <summary>
        /// Gets the number of visible fills accepted by staged-scene validation.
        /// </summary>
        public int VisibleFillCount { get; }

        /// <summary>
        /// Gets the number of emitted paths.
        /// </summary>
        public int PathCount { get; }

        /// <summary>
        /// Gets the number of emitted non-horizontal lines.
        /// </summary>
        public int LineCount { get; }

        /// <summary>
        /// Gets the total info-word count implied by the emitted draw tags.
        /// </summary>
        public int InfoWordCount { get; }

        /// <summary>
        /// Gets the number of emitted clip records.
        /// </summary>
        public int ClipCount { get; }

        /// <summary>
        /// Gets the number of emitted gradient-ramp rows.
        /// </summary>
        public int GradientRowCount { get; }

        /// <summary>
        /// Gets the CPU-side estimate of active tile rows referenced by sparse path metadata.
        /// </summary>
        public int EstimatedPathRowCount { get; }

        /// <summary>
        /// Gets the unpadded path-tag byte count.
        /// </summary>
        public int PathTagByteCount { get; }

        /// <summary>
        /// Gets the path-data word count.
        /// </summary>
        public int PathDataWordCount { get; }

        /// <summary>
        /// Gets the draw-tag count.
        /// </summary>
        public int DrawTagCount { get; }

        /// <summary>
        /// Gets the draw-data word count.
        /// </summary>
        public int DrawDataWordCount { get; }

        /// <summary>
        /// Gets the transform word count.
        /// </summary>
        public int TransformWordCount { get; }

        /// <summary>
        /// Gets the style word count.
        /// </summary>
        public int StyleWordCount { get; }

        /// <summary>
        /// Gets the encoded path-gradient payload word count.
        /// </summary>
        public int PathGradientDataWordCount { get; }

        /// <summary>
        /// Gets the fine-pass rasterization mode selected by this partition.
        /// </summary>
        public RasterizationMode FineRasterizationMode { get; }

        /// <summary>
        /// Gets the aliased coverage threshold selected by this partition.
        /// </summary>
        public float FineCoverageThreshold { get; }

        /// <summary>
        /// Detaches all retained streams from a mutable partition encoder.
        /// </summary>
        public static SceneEncodingPartition Detach(ref SupportedSubsetSceneEncoding encoding)
        {
            int pathTagByteCount = encoding.PathTags.Count;
            int pathDataWordCount = encoding.PathData.Count;
            int drawTagCount = encoding.DrawTags.Count;
            int drawDataWordCount = encoding.DrawData.Count;
            int transformWordCount = encoding.Transforms.Count;
            int styleWordCount = encoding.Styles.Count;
            int pathGradientDataWordCount = encoding.PathGradientData.Count;
            int gradientRowCount = encoding.GradientRowCount;

            IMemoryOwner<byte> pathTagsOwner = encoding.PathTags.DetachOwner();
            IMemoryOwner<uint> pathDataOwner = encoding.PathData.DetachOwner();
            IMemoryOwner<uint> drawTagsOwner = encoding.DrawTags.DetachOwner();
            IMemoryOwner<uint> drawDataOwner = encoding.DrawData.DetachOwner();
            IMemoryOwner<uint> transformsOwner = encoding.Transforms.DetachOwner();
            IMemoryOwner<uint> stylesOwner = encoding.Styles.DetachOwner();
            encoding.MarkStreamsDetached();

            IMemoryOwner<uint>? gradientPixelsOwner = null;
            if (gradientRowCount > 0)
            {
                gradientPixelsOwner = encoding.GradientPixels.DetachOwner();
                encoding.MarkGradientPixelsDetached();
            }

            IMemoryOwner<uint>? pathGradientDataOwner = null;
            if (pathGradientDataWordCount > 0)
            {
                pathGradientDataOwner = encoding.PathGradientData.DetachOwner();
                encoding.MarkPathGradientDataDetached();
            }

            return new SceneEncodingPartition(
                pathTagsOwner,
                pathDataOwner,
                drawTagsOwner,
                drawDataOwner,
                transformsOwner,
                stylesOwner,
                gradientPixelsOwner,
                pathGradientDataOwner,
                encoding.Images,
                encoding.FillCount,
                encoding.VisibleFillCount,
                encoding.PathCount,
                encoding.LineCount,
                encoding.InfoWordCount,
                encoding.ClipCount,
                gradientRowCount,
                encoding.EstimatedPathRowCount,
                pathTagByteCount,
                pathDataWordCount,
                drawTagCount,
                drawDataWordCount,
                transformWordCount,
                styleWordCount,
                pathGradientDataWordCount,
                encoding.FineRasterizationMode,
                encoding.FineCoverageThreshold);
        }

        /// <summary>
        /// Releases all retained partition stream buffers.
        /// </summary>
        public void Dispose()
        {
            this.pathTagsOwner.Dispose();
            this.pathDataOwner.Dispose();
            this.drawTagsOwner.Dispose();
            this.drawDataOwner.Dispose();
            this.transformsOwner.Dispose();
            this.stylesOwner.Dispose();
            this.gradientPixelsOwner?.Dispose();
            this.pathGradientDataOwner?.Dispose();
        }
    }

    /// <summary>
    /// Finalizes mutable scene streams into the immutable encoded payload handed to the GPU backend.
    /// </summary>
    private static class SupportedSubsetSceneResolver
    {
        /// <summary>
        /// Resolves the mutable encoding into the final packed scene buffers.
        /// </summary>
        public static WebGPUEncodedScene Resolve(
            ref SupportedSubsetSceneEncoding encoding,
            in Rectangle targetBounds,
            MemoryAllocator allocator)
        {
            int pathTagByteCount = encoding.PathTags.Count;
            int pathTagWordCount = AlignUp(DivideRoundUp(pathTagByteCount, 4), 256);
            int pathDataWordCount = encoding.PathData.Count;
            int drawTagCount = encoding.DrawTags.Count;
            int drawDataWordCount = encoding.DrawData.Count;
            int transformWordCount = encoding.Transforms.Count;
            int styleWordCount = encoding.Styles.Count;
            int pathGradientDataWordCount = encoding.PathGradientData.Count;
            int drawTagBase = pathTagWordCount + pathDataWordCount;
            int drawDataBase = drawTagBase + drawTagCount;
            int transformBase = drawDataBase + drawDataWordCount;
            int styleBase = transformBase + transformWordCount;
            int sceneWordCount = styleBase + styleWordCount;
            GpuSceneLayout layout = new(
                (uint)drawTagCount,
                (uint)encoding.PathCount,
                (uint)encoding.ClipCount,
                (uint)(encoding.InfoWordCount + pathGradientDataWordCount),
                (uint)encoding.InfoWordCount,
                0U,
                0U,
                (uint)pathTagWordCount,
                (uint)drawTagBase,
                (uint)drawDataBase,
                (uint)transformBase,
                (uint)styleBase);

            IMemoryOwner<uint> sceneDataOwner = allocator.Allocate<uint>(sceneWordCount);
            try
            {
                PackSceneData(
                    layout,
                    pathTagWordCount,
                    encoding.PathTags.WrittenSpan,
                    encoding.PathData.WrittenSpan,
                    encoding.DrawTags.WrittenSpan,
                    encoding.DrawData.WrittenSpan,
                    encoding.Transforms.WrittenSpan,
                    encoding.Styles.WrittenSpan,
                    sceneDataOwner.Memory.Span[..sceneWordCount]);

                return new WebGPUEncodedScene(
                    targetBounds.Size,
                    encoding.InfoWordCount,
                    sceneDataOwner,
                    sceneWordCount,
                    DetachGradientPixels(ref encoding),
                    DetachPathGradientData(ref encoding),
                    pathGradientDataWordCount,
                    encoding.Images,
                    encoding.GradientRowCount,
                    layout,
                    encoding.FillCount,
                    encoding.PathCount,
                    encoding.LineCount,
                    pathTagByteCount,
                    pathTagWordCount,
                    pathDataWordCount,
                    drawTagCount,
                    drawDataWordCount,
                    transformWordCount,
                    styleWordCount,
                    encoding.ClipCount,
                    encoding.FillCount,
                    Math.Max(encoding.EstimatedPathRowCount, encoding.PathCount),
                    DivideRoundUp(targetBounds.Width, TileWidth),
                    DivideRoundUp(targetBounds.Height, TileHeight),
                    encoding.FineRasterizationMode,
                    encoding.FineCoverageThreshold);
            }
            catch
            {
                sceneDataOwner.Dispose();
                throw;
            }
        }

        /// <summary>
        /// Resolves ordered partition encodings into the final packed scene buffers.
        /// </summary>
        public static WebGPUEncodedScene Resolve(
            SceneEncodingPartition?[] partitions,
            in Rectangle targetBounds,
            MemoryAllocator allocator,
            RasterizationMode fineRasterizationMode,
            float fineCoverageThreshold)
        {
            int pathTagByteCount = 0;
            int pathDataWordCount = 0;
            int drawTagCount = 0;
            int drawDataWordCount = 0;
            int transformWordCount = 0;
            int styleWordCount = 0;
            int pathGradientDataWordCount = 0;
            int imageCount = 0;
            int gradientRowCount = 0;
            int fillCount = 0;
            int pathCount = 0;
            int lineCount = 0;
            int infoWordCount = 0;
            int clipCount = 0;
            long estimatedPathRowCount = 0;

            for (int i = 0; i < partitions.Length; i++)
            {
                SceneEncodingPartition partition = partitions[i]!;
                pathTagByteCount += partition.PathTagByteCount;
                pathDataWordCount += partition.PathDataWordCount;
                drawTagCount += partition.DrawTagCount;
                drawDataWordCount += partition.DrawDataWordCount;
                transformWordCount += partition.TransformWordCount;
                styleWordCount += partition.StyleWordCount;
                pathGradientDataWordCount += partition.PathGradientDataWordCount;
                imageCount += partition.Images.Count;
                gradientRowCount += partition.GradientRowCount;
                fillCount += partition.FillCount;
                pathCount += partition.PathCount;
                lineCount += partition.LineCount;
                infoWordCount += partition.InfoWordCount;
                clipCount += partition.ClipCount;
                estimatedPathRowCount = Math.Min(estimatedPathRowCount + partition.EstimatedPathRowCount, int.MaxValue);
            }

            int pathTagWordCount = AlignUp(DivideRoundUp(pathTagByteCount, 4), 256);
            int drawTagBase = pathTagWordCount + pathDataWordCount;
            int drawDataBase = drawTagBase + drawTagCount;
            int transformBase = drawDataBase + drawDataWordCount;
            int styleBase = transformBase + transformWordCount;
            int sceneWordCount = styleBase + styleWordCount;
            GpuSceneLayout layout = new(
                (uint)drawTagCount,
                (uint)pathCount,
                (uint)clipCount,
                (uint)(infoWordCount + pathGradientDataWordCount),
                (uint)infoWordCount,
                0U,
                0U,
                (uint)pathTagWordCount,
                (uint)drawTagBase,
                (uint)drawDataBase,
                (uint)transformBase,
                (uint)styleBase);

            IMemoryOwner<uint>? sceneDataOwner = allocator.Allocate<uint>(sceneWordCount);
            IMemoryOwner<uint>? gradientPixelsOwner = gradientRowCount == 0 ? null : allocator.Allocate<uint>(gradientRowCount * GradientWidth);
            IMemoryOwner<uint>? pathGradientDataOwner = pathGradientDataWordCount == 0 ? null : allocator.Allocate<uint>(pathGradientDataWordCount);
            try
            {
                Span<uint> sceneWords = sceneDataOwner.Memory.Span[..sceneWordCount];
                Span<byte> sceneBytes = MemoryMarshal.Cast<uint, byte>(sceneWords);
                List<GpuImageDescriptor> images = new(imageCount);
                int pathTagOffset = 0;
                int pathDataOffset = 0;
                int drawTagOffset = 0;
                int drawDataOffset = 0;
                int transformOffset = 0;
                int styleOffset = 0;
                int gradientRowOffset = 0;
                int gradientPixelOffset = 0;
                int pathGradientDataOffset = 0;

                for (int i = 0; i < partitions.Length; i++)
                {
                    SceneEncodingPartition partition = partitions[i]!;
                    partition.PathTags.CopyTo(sceneBytes[pathTagOffset..]);
                    pathTagOffset += partition.PathTagByteCount;

                    partition.PathData.CopyTo(sceneWords.Slice((int)layout.PathDataBase + pathDataOffset, partition.PathDataWordCount));
                    pathDataOffset += partition.PathDataWordCount;

                    partition.DrawTags.CopyTo(sceneWords.Slice((int)layout.DrawTagBase + drawTagOffset, partition.DrawTagCount));
                    drawTagOffset += partition.DrawTagCount;

                    CopyDrawDataWithOffsets(
                        partition,
                        sceneWords.Slice((int)layout.DrawDataBase + drawDataOffset, partition.DrawDataWordCount),
                        gradientRowOffset,
                        pathGradientDataOffset);

                    for (int imageIndex = 0; imageIndex < partition.Images.Count; imageIndex++)
                    {
                        GpuImageDescriptor image = partition.Images[imageIndex];
                        images.Add(new GpuImageDescriptor(image.Brush, drawDataOffset + image.DrawDataWordOffset));
                    }

                    drawDataOffset += partition.DrawDataWordCount;

                    partition.Transforms.CopyTo(sceneWords.Slice((int)layout.TransformBase + transformOffset, partition.TransformWordCount));
                    transformOffset += partition.TransformWordCount;

                    partition.Styles.CopyTo(sceneWords.Slice((int)layout.StyleBase + styleOffset, partition.StyleWordCount));
                    styleOffset += partition.StyleWordCount;

                    if (partition.GradientRowCount > 0)
                    {
                        partition.GradientPixels.CopyTo(gradientPixelsOwner!.Memory.Span.Slice(gradientPixelOffset, partition.GradientRowCount * GradientWidth));
                        gradientPixelOffset += partition.GradientRowCount * GradientWidth;
                    }

                    if (partition.PathGradientDataWordCount > 0)
                    {
                        partition.PathGradientData.CopyTo(pathGradientDataOwner!.Memory.Span.Slice(pathGradientDataOffset, partition.PathGradientDataWordCount));
                    }

                    gradientRowOffset += partition.GradientRowCount;
                    pathGradientDataOffset += partition.PathGradientDataWordCount;
                }

                sceneBytes[pathTagByteCount..(pathTagWordCount * sizeof(uint))].Clear();
                WebGPUEncodedScene resolved = new(
                    targetBounds.Size,
                    infoWordCount,
                    sceneDataOwner,
                    sceneWordCount,
                    gradientPixelsOwner,
                    pathGradientDataOwner,
                    pathGradientDataWordCount,
                    images,
                    gradientRowCount,
                    layout,
                    fillCount,
                    pathCount,
                    lineCount,
                    pathTagByteCount,
                    pathTagWordCount,
                    pathDataWordCount,
                    drawTagCount,
                    drawDataWordCount,
                    transformWordCount,
                    styleWordCount,
                    clipCount,
                    fillCount,
                    Math.Max((int)estimatedPathRowCount, pathCount),
                    DivideRoundUp(targetBounds.Width, TileWidth),
                    DivideRoundUp(targetBounds.Height, TileHeight),
                    fineRasterizationMode,
                    fineCoverageThreshold);

                sceneDataOwner = null;
                gradientPixelsOwner = null;
                pathGradientDataOwner = null;
                return resolved;
            }
            finally
            {
                sceneDataOwner?.Dispose();
                gradientPixelsOwner?.Dispose();
                pathGradientDataOwner?.Dispose();
            }
        }

        /// <summary>
        /// Copies draw-data while rebasing partition-local auxiliary payload offsets into final scene offsets.
        /// </summary>
        private static void CopyDrawDataWithOffsets(
            SceneEncodingPartition partition,
            Span<uint> destination,
            int gradientRowOffset,
            int pathGradientDataOffset)
        {
            ReadOnlySpan<uint> drawTags = partition.DrawTags;
            ReadOnlySpan<uint> drawData = partition.DrawData;
            int sourceOffset = 0;
            int destinationOffset = 0;

            for (int i = 0; i < drawTags.Length; i++)
            {
                uint drawTag = drawTags[i];
                int wordCount = GetDrawDataWordCount(drawTag);
                drawData.Slice(sourceOffset, wordCount).CopyTo(destination[destinationOffset..]);

                if (DrawTagUsesGradientRamp(drawTag))
                {
                    destination[destinationOffset] += (uint)(gradientRowOffset << 2);
                }
                else if (drawTag == GpuSceneDrawTag.FillPathGradient)
                {
                    destination[destinationOffset] += (uint)pathGradientDataOffset;
                }

                sourceOffset += wordCount;
                destinationOffset += wordCount;
            }
        }

        /// <summary>
        /// Detaches the gradient pixel payload when gradients were emitted for the flush.
        /// </summary>
        private static IMemoryOwner<uint>? DetachGradientPixels(ref SupportedSubsetSceneEncoding encoding)
        {
            if (encoding.GradientRowCount == 0)
            {
                return null;
            }

            encoding.MarkGradientPixelsDetached();
            return encoding.GradientPixels.DetachOwner();
        }

        private static IMemoryOwner<uint>? DetachPathGradientData(ref SupportedSubsetSceneEncoding encoding)
        {
            if (encoding.PathGradientData.Count == 0)
            {
                return null;
            }

            encoding.MarkPathGradientDataDetached();
            return encoding.PathGradientData.DetachOwner();
        }
    }

    /// <summary>
    /// Returns whether the staged scene encoder knows how to lower the supplied brush type.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsSupportedBrush(Brush brush)
        => brush is SolidBrush
            or RecolorBrush
            or LinearGradientBrush
            or RadialGradientBrush
            or EllipticGradientBrush
            or SweepGradientBrush
            or PathGradientBrush
            or PatternBrush
            or ImageBrush;

    /// <summary>
    /// Maps one prepared fill command to the draw-tag consumed by the staged scene pipeline.
    /// </summary>
    private static bool TryResolveCommand(in CompositionCommand command, out ResolvedPathCommand resolved)
    {
        if (command.Kind is not CompositionCommandKind.FillLayer)
        {
            resolved = default;
            return false;
        }

        resolved = new ResolvedPathCommand(
            command.SourcePath,
            command.Brush,
            command.GraphicsOptions,
            command.RasterizerOptions,
            command.DestinationOffset,
            command.Brush is ImageBrush ? command.RasterizerOptions.Interest : default,
            command.Transform);
        return true;
    }

    private static bool TryResolveCommand(in StrokePathCommand command, out ResolvedPathCommand resolved)
    {
        resolved = new ResolvedPathCommand(
            command.SourcePath,
            command.Brush,
            command.GraphicsOptions,
            command.RasterizerOptions,
            command.DestinationOffset,
            command.Brush is ImageBrush ? command.RasterizerOptions.Interest : default,
            command.Transform);
        return true;
    }

    private static bool TryResolveCommand(in StrokeLineSegmentCommand command, out ResolvedLineSegmentCommand resolved)
    {
        resolved = new ResolvedLineSegmentCommand(
            command.SourceStart,
            command.SourceEnd,
            command.Pen,
            command.Brush,
            command.GraphicsOptions,
            command.RasterizerOptions,
            command.DestinationOffset,
            command.Brush is ImageBrush ? command.RasterizerOptions.Interest : default);
        return true;
    }

    private static bool TryResolveCommand(in StrokePolylineCommand command, out ResolvedPolylineCommand resolved)
    {
        resolved = new ResolvedPolylineCommand(
            command.SourcePoints,
            command.Pen,
            command.Brush,
            command.GraphicsOptions,
            command.RasterizerOptions,
            command.DestinationOffset,
            command.Brush is ImageBrush ? command.RasterizerOptions.Interest : default);
        return true;
    }

    private readonly struct ResolvedPathCommand
    {
        public ResolvedPathCommand(
            IPath path,
            Brush brush,
            GraphicsOptions graphicsOptions,
            RasterizerOptions rasterizerOptions,
            Point destinationOffset,
            Rectangle brushBounds,
            Matrix4x4 transform)
        {
            this.Path = path;
            this.Brush = brush;
            this.GraphicsOptions = graphicsOptions;
            this.RasterizerOptions = rasterizerOptions;
            this.DestinationOffset = destinationOffset;
            this.BrushBounds = brushBounds;
            this.Transform = transform;
        }

        public IPath Path { get; }

        public Brush Brush { get; }

        public GraphicsOptions GraphicsOptions { get; }

        public RasterizerOptions RasterizerOptions { get; }

        public Point DestinationOffset { get; }

        public Rectangle BrushBounds { get; }

        public Matrix4x4 Transform { get; }
    }

    private readonly struct ResolvedLineSegmentCommand
    {
        public ResolvedLineSegmentCommand(
            PointF start,
            PointF end,
            Pen pen,
            Brush brush,
            GraphicsOptions graphicsOptions,
            RasterizerOptions rasterizerOptions,
            Point destinationOffset,
            Rectangle brushBounds)
        {
            this.Start = start;
            this.End = end;
            this.Pen = pen;
            this.Brush = brush;
            this.GraphicsOptions = graphicsOptions;
            this.RasterizerOptions = rasterizerOptions;
            this.DestinationOffset = destinationOffset;
            this.BrushBounds = brushBounds;
        }

        public PointF Start { get; }

        public PointF End { get; }

        public Pen Pen { get; }

        public Brush Brush { get; }

        public GraphicsOptions GraphicsOptions { get; }

        public RasterizerOptions RasterizerOptions { get; }

        public Point DestinationOffset { get; }

        public Rectangle BrushBounds { get; }
    }

    private readonly struct ResolvedPolylineCommand
    {
        public ResolvedPolylineCommand(
            PointF[] points,
            Pen pen,
            Brush brush,
            GraphicsOptions graphicsOptions,
            RasterizerOptions rasterizerOptions,
            Point destinationOffset,
            Rectangle brushBounds)
        {
            this.Points = points;
            this.Pen = pen;
            this.Brush = brush;
            this.GraphicsOptions = graphicsOptions;
            this.RasterizerOptions = rasterizerOptions;
            this.DestinationOffset = destinationOffset;
            this.BrushBounds = brushBounds;
        }

        public PointF[] Points { get; }

        public Pen Pen { get; }

        public Brush Brush { get; }

        public GraphicsOptions GraphicsOptions { get; }

        public RasterizerOptions RasterizerOptions { get; }

        public Point DestinationOffset { get; }

        public Rectangle BrushBounds { get; }
    }

    /// <summary>
    /// Maps one prepared brush to the draw-tag consumed by the staged scene pipeline.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint GetDrawTag(Brush brush)
        => brush switch
        {
            SolidBrush => GpuSceneDrawTag.FillColor,
            RecolorBrush => GpuSceneDrawTag.FillRecolor,
            LinearGradientBrush => GpuSceneDrawTag.FillLinGradient,
            RadialGradientBrush => GpuSceneDrawTag.FillRadGradient,
            EllipticGradientBrush => GpuSceneDrawTag.FillEllipticGradient,
            SweepGradientBrush => GpuSceneDrawTag.FillSweepGradient,
            PathGradientBrush => GpuSceneDrawTag.FillPathGradient,
            PatternBrush => GpuSceneDrawTag.FillImage,
            ImageBrush => GpuSceneDrawTag.FillImage,
            _ => throw new UnreachableException($"Unsupported brush type '{brush.GetType().Name}' should have been rejected before scene encoding.")
        };

    /// <summary>
    /// Encodes a lowered path into path-tag and path-data streams in target-local space.
    /// </summary>
    private static int EncodePath(
        in ResolvedPathCommand command,
        LinearGeometry geometry,
        in Rectangle rootTargetBounds,
        ref OwnedStream<byte> pathTags,
        ref OwnedStream<uint> pathData,
        out int lineCount)
    {
        float pointTranslateX = command.DestinationOffset.X - rootTargetBounds.X;
        float pointTranslateY = command.DestinationOffset.Y - rootTargetBounds.Y;
        lineCount = command.RasterizerOptions.SamplingOrigin == RasterizerSamplingOrigin.PixelCenter
            ? geometry.Info.NonHorizontalSegmentCountPixelCenter
            : geometry.Info.NonHorizontalSegmentCountPixelBoundary;

        for (int i = 0; i < geometry.Contours.Count; i++)
        {
            LinearContour contour = geometry.Contours[i];

            // Each contour writes one starting point, then one end point and one line tag per
            // derived segment. Reserving the exact slice up front lets the hot loop fill the
            // contiguous output directly instead of re-checking stream capacity per word/tag.
            Span<uint> contourData = pathData.GetAppendSpan(2 + (contour.SegmentCount * 2));
            Span<byte> contourTags = pathTags.GetAppendSpan(contour.SegmentCount);

            // dataIndex tracks the next uint slot within this contour's reserved point payload.
            // The tag index is just the segment index because there is exactly one emitted tag
            // for each derived segment in the contour.
            int dataIndex = 0;
            PointF firstPoint = geometry.Points[contour.PointStart];
            float firstX = firstPoint.X + pointTranslateX;
            float firstY = firstPoint.Y + pointTranslateY;
            contourData[dataIndex++] = BitcastSingle(firstX);
            contourData[dataIndex++] = BitcastSingle(firstY);

            for (int j = 0; j < contour.SegmentCount; j++)
            {
                int endPointIndex = contour.PointStart + ((j + 1) == contour.PointCount ? 0 : j + 1);
                PointF endPoint = geometry.Points[endPointIndex];
                float translatedEndX = endPoint.X + pointTranslateX;
                float translatedEndY = endPoint.Y + pointTranslateY;
                contourData[dataIndex++] = BitcastSingle(translatedEndX);
                contourData[dataIndex++] = BitcastSingle(translatedEndY);
                contourTags[j] = PackPathTag(PathTag.LineToF32);
            }

            if (contour.SegmentCount > 0)
            {
                contourTags[^1] |= PackPathTag(PathTag.SubpathEnd);
            }

            // The reserved slices are staged locally until the contour is complete so the stream
            // length only advances once, after the final subpath-end marker is in place.
            pathData.Advance(contourData.Length);
            pathTags.Advance(contourTags.Length);
        }

        if (geometry.Info.SegmentCount == 0)
        {
            return 0;
        }

        pathTags.Add(PackPathTag(PathTag.Path));
        return 1;
    }

    /// <summary>
    /// Encodes a stroke centerline into Vello-style path tags and path data in target-local space.
    /// </summary>
    private static int EncodeStrokePath(
        LinearGeometry geometry,
        Point destinationOffset,
        Pen pen,
        float widthScale,
        in Rectangle rootTargetBounds,
        ref OwnedStream<byte> pathTags,
        ref OwnedStream<uint> pathData,
        out int lineCount)
    {
        float pointTranslateX = destinationOffset.X - rootTargetBounds.X;
        float pointTranslateY = destinationOffset.Y - rootTargetBounds.Y;
        lineCount = EstimateStrokeLineCount(geometry, pen, widthScale);
        int encodedContourCount = 0;

        for (int i = 0; i < geometry.Contours.Count; i++)
        {
            LinearContour contour = geometry.Contours[i];
            if (TryGetPointStrokeContour(geometry, contour, out PointF point))
            {
                EncodePointStrokeContour(
                    point,
                    pointTranslateX,
                    pointTranslateY,
                    ref pathTags,
                    ref pathData);
                encodedContourCount++;
                continue;
            }

            int markerWordCount = contour.IsClosed ? 2 : 4;
            Span<uint> contourData = pathData.GetAppendSpan(2 + (contour.SegmentCount * 2) + markerWordCount);
            Span<byte> contourTags = pathTags.GetAppendSpan(contour.SegmentCount + 1);

            int dataIndex = 0;
            int tagIndex = 0;
            PointF firstPoint = geometry.Points[contour.PointStart];
            PointF firstTangentEndPoint = GetStrokeMarkerTangentPoint(geometry, contour);
            contourData[dataIndex++] = BitcastSingle(firstPoint.X + pointTranslateX);
            contourData[dataIndex++] = BitcastSingle(firstPoint.Y + pointTranslateY);

            for (int segmentIndex = 0; segmentIndex < contour.SegmentCount; segmentIndex++)
            {
                int endPointIndex = contour.PointStart + ((segmentIndex + 1) == contour.PointCount ? 0 : segmentIndex + 1);
                PointF endPoint = geometry.Points[endPointIndex];
                contourData[dataIndex++] = BitcastSingle(endPoint.X + pointTranslateX);
                contourData[dataIndex++] = BitcastSingle(endPoint.Y + pointTranslateY);
                contourTags[tagIndex++] = PackPathTag(PathTag.LineToF32);
            }

            if (!contour.IsClosed)
            {
                contourData[dataIndex++] = BitcastSingle(firstPoint.X + pointTranslateX);
                contourData[dataIndex++] = BitcastSingle(firstPoint.Y + pointTranslateY);
                contourData[dataIndex++] = BitcastSingle(firstTangentEndPoint.X + pointTranslateX);
                contourData[dataIndex++] = BitcastSingle(firstTangentEndPoint.Y + pointTranslateY);
                contourTags[tagIndex] = PackPathTag(PathTag.QuadToF32 | PathTag.SubpathEnd);
            }
            else
            {
                contourData[dataIndex++] = BitcastSingle(firstTangentEndPoint.X + pointTranslateX);
                contourData[dataIndex++] = BitcastSingle(firstTangentEndPoint.Y + pointTranslateY);
                contourTags[tagIndex] = PackPathTag(PathTag.LineToF32 | PathTag.SubpathEnd);
            }

            pathData.Advance(contourData.Length);
            pathTags.Advance(contourTags.Length);
            encodedContourCount++;
        }

        if (encodedContourCount == 0)
        {
            return 0;
        }

        pathTags.Add(PackPathTag(PathTag.Path));
        return 1;
    }

    /// <summary>
    /// Encodes one point-like stroke contour as a tiny centered open segment.
    /// </summary>
    private static void EncodePointStrokeContour(
        PointF point,
        float pointTranslateX,
        float pointTranslateY,
        ref OwnedStream<byte> pathTags,
        ref OwnedStream<uint> pathData)
    {
        PointF start = new(point.X - PointStrokeSegmentHalfLength, point.Y);
        PointF end = new(point.X + PointStrokeSegmentHalfLength, point.Y);
        PointF tangentEnd = GetStrokeMarkerTangentPoint(start, end);

        Span<uint> contourData = pathData.GetAppendSpan(8);
        Span<byte> contourTags = pathTags.GetAppendSpan(2);
        contourData[0] = BitcastSingle(start.X + pointTranslateX);
        contourData[1] = BitcastSingle(start.Y + pointTranslateY);
        contourData[2] = BitcastSingle(end.X + pointTranslateX);
        contourData[3] = BitcastSingle(end.Y + pointTranslateY);
        contourTags[0] = PackPathTag(PathTag.LineToF32);
        contourData[4] = BitcastSingle(start.X + pointTranslateX);
        contourData[5] = BitcastSingle(start.Y + pointTranslateY);
        contourData[6] = BitcastSingle(tangentEnd.X + pointTranslateX);
        contourData[7] = BitcastSingle(tangentEnd.Y + pointTranslateY);
        contourTags[1] = PackPathTag(PathTag.QuadToF32 | PathTag.SubpathEnd);
        pathData.Advance(contourData.Length);
        pathTags.Advance(contourTags.Length);
    }

    /// <summary>
    /// Encodes one explicit open two-point stroke centerline into Vello-style path tags and path data in target-local space.
    /// </summary>
    private static int EncodeOpenSegmentStrokePath(
        PointF start,
        PointF end,
        Point destinationOffset,
        Pen pen,
        float widthScale,
        in Rectangle rootTargetBounds,
        ref OwnedStream<byte> pathTags,
        ref OwnedStream<uint> pathData,
        out int lineCount)
    {
        float pointTranslateX = destinationOffset.X - rootTargetBounds.X;
        float pointTranslateY = destinationOffset.Y - rootTargetBounds.Y;
        NormalizePointStrokeSegment(ref start, ref end);
        PointF firstTangentEndPoint = GetStrokeMarkerTangentPoint(start, end);

        Span<uint> contourData = pathData.GetAppendSpan(8);
        Span<byte> contourTags = pathTags.GetAppendSpan(2);
        contourData[0] = BitcastSingle(start.X + pointTranslateX);
        contourData[1] = BitcastSingle(start.Y + pointTranslateY);
        contourData[2] = BitcastSingle(end.X + pointTranslateX);
        contourData[3] = BitcastSingle(end.Y + pointTranslateY);
        contourTags[0] = PackPathTag(PathTag.LineToF32);
        contourData[4] = BitcastSingle(start.X + pointTranslateX);
        contourData[5] = BitcastSingle(start.Y + pointTranslateY);
        contourData[6] = BitcastSingle(firstTangentEndPoint.X + pointTranslateX);
        contourData[7] = BitcastSingle(firstTangentEndPoint.Y + pointTranslateY);
        contourTags[1] = PackPathTag(PathTag.QuadToF32 | PathTag.SubpathEnd);
        pathData.Advance(contourData.Length);
        pathTags.Advance(contourTags.Length);
        pathTags.Add(PackPathTag(PathTag.Path));
        lineCount = EstimateStrokeLineCountForOpenSegment(pen, widthScale);
        return 1;
    }

    /// <summary>
    /// Gets whether a contour collapses to the point-stroke path used by the CPU rasterizer.
    /// </summary>
    private static bool TryGetPointStrokeContour(LinearGeometry geometry, LinearContour contour, out PointF point)
    {
        point = default;
        if (contour.PointCount == 0)
        {
            return false;
        }

        point = geometry.Points[contour.PointStart];
        for (int i = 1; i < contour.PointCount; i++)
        {
            PointF next = geometry.Points[contour.PointStart + i];
            if (Vector2.DistanceSquared(next, point) > StrokeMicroSegmentEpsilon * StrokeMicroSegmentEpsilon)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Replaces a point-like explicit stroke segment with the tiny tangent segment used for point strokes.
    /// </summary>
    private static void NormalizePointStrokeSegment(ref PointF start, ref PointF end)
    {
        if (Vector2.DistanceSquared(start, end) > StrokeMicroSegmentEpsilon * StrokeMicroSegmentEpsilon)
        {
            return;
        }

        PointF center = new((start.X + end.X) * 0.5F, (start.Y + end.Y) * 0.5F);
        start = new PointF(center.X - PointStrokeSegmentHalfLength, center.Y);
        end = new PointF(center.X + PointStrokeSegmentHalfLength, center.Y);
    }

    /// <summary>
    /// Encodes a rectangle as a fixed-size closed path.
    /// </summary>
    private static int EncodeRectanglePath(
        in Rectangle rectangle,
        ref OwnedStream<byte> pathTags,
        ref OwnedStream<uint> pathData,
        out int lineCount)
    {
        lineCount = 0;
        if (rectangle.Width <= 0 || rectangle.Height <= 0)
        {
            return 0;
        }

        float left = rectangle.Left;
        float top = rectangle.Top;
        float right = rectangle.Right;
        float bottom = rectangle.Bottom;

        Span<uint> data = pathData.GetAppendSpan(10);
        Span<byte> tags = pathTags.GetAppendSpan(5);
        data[0] = BitcastSingle(left);
        data[1] = BitcastSingle(top);

        data[2] = BitcastSingle(right);
        data[3] = BitcastSingle(top);
        tags[0] = PackPathTag(PathTag.LineToF32);

        data[4] = BitcastSingle(right);
        data[5] = BitcastSingle(bottom);
        tags[1] = PackPathTag(PathTag.LineToF32);

        data[6] = BitcastSingle(left);
        data[7] = BitcastSingle(bottom);
        tags[2] = PackPathTag(PathTag.LineToF32);

        data[8] = BitcastSingle(left);
        data[9] = BitcastSingle(top);
        tags[3] = PackPathTag(PathTag.LineToF32 | PathTag.SubpathEnd);

        tags[4] = PackPathTag(PathTag.Path);

        pathData.Advance(data.Length);
        pathTags.Advance(tags.Length);
        lineCount = 2;
        return 1;
    }

    /// <summary>
    /// Reserves the exact stream growth needed for one plain fill item.
    /// </summary>
    private static void ReservePlainFillCapacity(
        int contourCount,
        int segmentCount,
        uint drawTag,
        bool appendStyle,
        int pathGradientDataWordCount,
        ref OwnedStream<byte> pathTags,
        ref OwnedStream<uint> pathData,
        ref OwnedStream<uint> drawTags,
        ref OwnedStream<uint> drawData,
        ref OwnedStream<uint> styles,
        ref OwnedStream<uint> gradientPixels,
        ref OwnedStream<uint> pathGradientData)
    {
        int pathTagAdd = segmentCount + 1 + (appendStyle ? 1 : 0);
        int pathDataAdd = (contourCount * 2) + (segmentCount * 2);

        pathTags.EnsureAdditionalCapacity(pathTagAdd);
        pathData.EnsureAdditionalCapacity(pathDataAdd);
        drawTags.EnsureAdditionalCapacity(1);
        drawData.EnsureAdditionalCapacity(GetDrawDataWordCount(drawTag));

        if (appendStyle)
        {
            styles.EnsureAdditionalCapacity(StyleWordCount);
        }

        if (DrawTagUsesGradientRamp(drawTag))
        {
            gradientPixels.EnsureAdditionalCapacity(GradientWidth);
        }

        if (pathGradientDataWordCount > 0)
        {
            pathGradientData.EnsureAdditionalCapacity(pathGradientDataWordCount);
        }
    }

    /// <summary>
    /// Reserves the exact stream growth needed for one plain fill item.
    /// </summary>
    private static void ReservePlainFillCapacity(
        LinearGeometryInfo geometryInfo,
        uint drawTag,
        bool appendStyle,
        int pathGradientDataWordCount,
        ref OwnedStream<byte> pathTags,
        ref OwnedStream<uint> pathData,
        ref OwnedStream<uint> drawTags,
        ref OwnedStream<uint> drawData,
        ref OwnedStream<uint> styles,
        ref OwnedStream<uint> gradientPixels,
        ref OwnedStream<uint> pathGradientData)
        => ReservePlainFillCapacity(
            geometryInfo.ContourCount,
            geometryInfo.SegmentCount,
            drawTag,
            appendStyle,
            pathGradientDataWordCount,
            ref pathTags,
            ref pathData,
            ref drawTags,
            ref drawData,
            ref styles,
            ref gradientPixels,
            ref pathGradientData);

    /// <summary>
    /// Reserves the exact stream growth needed for one stroke item.
    /// </summary>
    private static void ReservePlainStrokeCapacity(
        LinearGeometry geometry,
        uint drawTag,
        bool appendStyle,
        int pathGradientDataWordCount,
        ref OwnedStream<byte> pathTags,
        ref OwnedStream<uint> pathData,
        ref OwnedStream<uint> drawTags,
        ref OwnedStream<uint> drawData,
        ref OwnedStream<uint> styles,
        ref OwnedStream<uint> gradientPixels,
        ref OwnedStream<uint> pathGradientData)
    {
        int markerTagCount = geometry.Info.ContourCount;
        int markerWordCount = 0;
        for (int i = 0; i < geometry.Contours.Count; i++)
        {
            markerWordCount += geometry.Contours[i].IsClosed ? 2 : 4;
        }

        int pathTagAdd = geometry.Info.SegmentCount + markerTagCount + 1 + (appendStyle ? 1 : 0);
        int pathDataAdd = (geometry.Info.ContourCount * 2) + (geometry.Info.SegmentCount * 2) + markerWordCount;

        pathTags.EnsureAdditionalCapacity(pathTagAdd);
        pathData.EnsureAdditionalCapacity(pathDataAdd);
        drawTags.EnsureAdditionalCapacity(1);
        drawData.EnsureAdditionalCapacity(GetDrawDataWordCount(drawTag));

        if (appendStyle)
        {
            styles.EnsureAdditionalCapacity(StyleWordCount);
        }

        if (DrawTagUsesGradientRamp(drawTag))
        {
            gradientPixels.EnsureAdditionalCapacity(GradientWidth);
        }

        if (pathGradientDataWordCount > 0)
        {
            pathGradientData.EnsureAdditionalCapacity(pathGradientDataWordCount);
        }
    }

    /// <summary>
    /// Reserves the exact stream growth needed for one explicit open line segment stroke.
    /// </summary>
    private static void ReservePlainStrokeCapacityForOpenSegment(
        uint drawTag,
        bool appendStyle,
        int pathGradientDataWordCount,
        ref OwnedStream<byte> pathTags,
        ref OwnedStream<uint> pathData,
        ref OwnedStream<uint> drawTags,
        ref OwnedStream<uint> drawData,
        ref OwnedStream<uint> styles,
        ref OwnedStream<uint> gradientPixels,
        ref OwnedStream<uint> pathGradientData)
    {
        pathTags.EnsureAdditionalCapacity(3 + (appendStyle ? 1 : 0));
        pathData.EnsureAdditionalCapacity(8);
        drawTags.EnsureAdditionalCapacity(1);
        drawData.EnsureAdditionalCapacity(GetDrawDataWordCount(drawTag));

        if (appendStyle)
        {
            styles.EnsureAdditionalCapacity(StyleWordCount);
        }

        if (DrawTagUsesGradientRamp(drawTag))
        {
            gradientPixels.EnsureAdditionalCapacity(GradientWidth);
        }

        if (pathGradientDataWordCount > 0)
        {
            pathGradientData.EnsureAdditionalCapacity(pathGradientDataWordCount);
        }
    }

    /// <summary>
    /// Estimates the flattened line workload for one stroke.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int EstimateStrokeLineCount(LinearGeometry geometry, Pen pen, float widthScale)
    {
        int joinCost = GetStrokeJoinLineCost(pen, widthScale);
        int capCost = GetStrokeCapLineCost(pen, widthScale);
        int total = 0;

        for (int i = 0; i < geometry.Contours.Count; i++)
        {
            LinearContour contour = geometry.Contours[i];

            total += contour.SegmentCount * 2;
            if (contour.IsClosed)
            {
                total += contour.SegmentCount * joinCost;
            }
            else
            {
                total += Math.Max(contour.SegmentCount - 1, 0) * joinCost;
                total += capCost * 2;
            }
        }

        return Math.Max(total, 1);
    }

    /// <summary>
    /// Estimates the flattened line workload for one explicit open two-point stroke segment.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int EstimateStrokeLineCountForOpenSegment(Pen pen, float widthScale)
        => Math.Max(2 + (GetStrokeCapLineCost(pen, widthScale) * 2), 1);

    /// <summary>
    /// Returns the conservative flattened line cost of one stroke join.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int GetStrokeJoinLineCost(Pen pen, float widthScale)
    {
        float halfWidth = pen.StrokeWidth * widthScale * 0.5F;
        int outerCost = pen.StrokeOptions.LineJoin switch
        {
            LineJoin.Miter => 3,
            LineJoin.MiterRound => GetStrokeArcLineCost(halfWidth, MathF.PI, pen.StrokeOptions.ArcDetailScale),
            LineJoin.MiterRevert => 2,
            LineJoin.Round => GetStrokeArcLineCost(halfWidth, MathF.PI, pen.StrokeOptions.ArcDetailScale),
            _ => 2
        };
        return outerCost + 2;
    }

    /// <summary>
    /// Returns the conservative flattened line cost of one stroke cap.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int GetStrokeCapLineCost(Pen pen, float widthScale)
    {
        float halfWidth = pen.StrokeWidth * widthScale * 0.5F;
        return pen.StrokeOptions.LineCap switch
        {
            LineCap.Square => 3,
            LineCap.Round => GetStrokeArcLineCost(halfWidth, MathF.PI, pen.StrokeOptions.ArcDetailScale),
            _ => 1
        };
    }

    /// <summary>
    /// Returns the conservative flattened line cost of one round arc in the stroke shaders.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int GetStrokeArcLineCost(float radius, float angle, double arcDetailScale)
    {
        float safeRadius = Math.Max(radius, 0.25F);
        float safeScale = (float)Math.Max(arcDetailScale, 0.01D);
        float ratio = Math.Clamp(safeRadius / (safeRadius + (0.125F / safeScale)), -1F, 1F);
        float theta = Math.Max(0.0001F, 2F * MathF.Acos(ratio));
        return Math.Max(1, (int)MathF.Ceiling(angle / theta));
    }

    /// <summary>
    /// Returns the encoded tangent point used by the staged stroker's cap marker segment.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static PointF GetStrokeMarkerTangentPoint(LinearGeometry geometry, LinearContour contour)
    {
        PointF firstPoint = geometry.Points[contour.PointStart];
        int nextPointIndex = contour.PointStart + (contour.PointCount > 1 ? 1 : 0);
        PointF nextPoint = geometry.Points[nextPointIndex];
        return new PointF(
            firstPoint.X + ((nextPoint.X - firstPoint.X) / 3F),
            firstPoint.Y + ((nextPoint.Y - firstPoint.Y) / 3F));
    }

    /// <summary>
    /// Returns the encoded tangent point used by the staged stroker's cap marker segment for one explicit open line segment.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static PointF GetStrokeMarkerTangentPoint(PointF start, PointF end)
        => new(
            start.X + ((end.X - start.X) / 3F),
            start.Y + ((end.Y - start.Y) / 3F));

    /// <summary>
    /// Reserves the exact stream growth needed for one begin-layer clip item.
    /// </summary>
    private static void ReserveBeginLayerCapacity(
        bool appendStyle,
        ref OwnedStream<byte> pathTags,
        ref OwnedStream<uint> pathData,
        ref OwnedStream<uint> drawTags,
        ref OwnedStream<uint> drawData,
        ref OwnedStream<uint> styles)
    {
        // Rectangles are emitted as:
        // - optional PathTagStyle
        // - four line tags for the rectangle edges
        // - one final PathTagPath terminator
        pathTags.EnsureAdditionalCapacity((appendStyle ? 1 : 0) + 5);

        // Rectangles write five points total:
        // bottom-left, bottom-right, top-right, top-left, then bottom-left again.
        // Each point is two uint words, so 5 * 2 = 10 words.
        pathData.EnsureAdditionalCapacity(10);
        drawTags.EnsureAdditionalCapacity(1);

        // BeginClip emits two draw-data words for blend mode / alpha state.
        drawData.EnsureAdditionalCapacity(2);

        if (appendStyle)
        {
            styles.EnsureAdditionalCapacity(StyleWordCount);
        }
    }

    /// <summary>
    /// Reserves the exact stream growth needed for one end-layer item.
    /// </summary>
    private static void ReserveEndLayerCapacity(
        ref OwnedStream<byte> pathTags,
        ref OwnedStream<uint> drawTags)
    {
        // End-layer emission appends only:
        // - one EndClip draw tag
        // - one PathTagPath marker
        drawTags.EnsureAdditionalCapacity(1);
        pathTags.EnsureAdditionalCapacity(1);
    }

    /// <summary>
    /// Gets the exact draw-data word count emitted for one draw tag.
    /// </summary>
    private static int GetDrawDataWordCount(uint drawTag)
        => drawTag switch
        {
            // Each case matches the exact number of uint words written by AppendDrawData.
            GpuSceneDrawTag.BeginClip => 2,
            GpuSceneDrawTag.FillColor => 1,
            GpuSceneDrawTag.FillRecolor => 3,
            GpuSceneDrawTag.FillLinGradient => 5,
            GpuSceneDrawTag.FillRadGradient => 7,
            GpuSceneDrawTag.FillEllipticGradient => 7,
            GpuSceneDrawTag.FillSweepGradient => 5,
            GpuSceneDrawTag.FillPathGradient => 4,
            GpuSceneDrawTag.FillImage => 5,
            GpuSceneDrawTag.EndClip => 0,
            _ => throw new UnreachableException($"Unsupported draw tag '{drawTag}' reached draw-data sizing.")
        };

    /// <summary>
    /// Gets the exact path-gradient payload word count emitted for one brush.
    /// </summary>
    private static int GetPathGradientDataWordCount(Brush brush)
        => brush is PathGradientBrush pathGradientBrush
            ? PathGradientHeaderWordCount + (pathGradientBrush.Points.Length * PathGradientEdgeWordCount)
            : 0;

    /// <summary>
    /// Returns a value indicating whether the draw tag emits one gradient ramp row.
    /// </summary>
    private static bool DrawTagUsesGradientRamp(uint drawTag)
        => drawTag is GpuSceneDrawTag.FillLinGradient
            or GpuSceneDrawTag.FillRadGradient
            or GpuSceneDrawTag.FillEllipticGradient
            or GpuSceneDrawTag.FillSweepGradient;

    /// <summary>
    /// Appends one 2x3 affine transform to the packed transform stream.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void AppendTransform(GpuSceneTransform transform, ref OwnedStream<uint> transforms)
    {
        transforms.Add(BitcastSingle(transform.M11));
        transforms.Add(BitcastSingle(transform.M12));
        transforms.Add(BitcastSingle(transform.M21));
        transforms.Add(BitcastSingle(transform.M22));
        transforms.Add(BitcastSingle(transform.Tx));
        transforms.Add(BitcastSingle(transform.Ty));
        transforms.Add(BitcastSingle(transform.M14));
        transforms.Add(BitcastSingle(transform.M24));
        transforms.Add(BitcastSingle(transform.M44));
    }

    /// <summary>
    /// Appends the identity transform to the packed transform stream.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void AppendIdentityTransform(ref OwnedStream<uint> transforms)
        => AppendTransform(GpuSceneTransform.Identity, ref transforms);

    /// <summary>
    /// Packs the style words that describe stroke behavior for one draw record.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static (uint Style0, uint Style1, uint Style2, uint Style3, uint Style4) GetStrokeStyle(GraphicsOptions options, Pen pen, float widthScale)
    {
        StyleFlags styleFlags = StyleFlags.Stroke
            | EncodeStrokeJoinFlags(pen.StrokeOptions.LineJoin)
            | EncodeStrokeCapFlags(pen.StrokeOptions.LineCap);

        return (
            (uint)styleFlags,
            BitcastSingle(pen.StrokeWidth * widthScale),
            PackStyleDrawFlags(options),
            BitcastSingle((float)pen.StrokeOptions.MiterLimit),
            BitcastSingle((float)pen.StrokeOptions.ArcDetailScale));
    }

    /// <summary>
    /// Returns the isotropic scale factor embedded in a drawing transform so stroke widths match device-space pixels.
    /// </summary>
    /// <remarks>
    /// Uses the square root of the absolute 2D determinant, the SVG-style fallback for non-uniform
    /// scale. Reduces to the uniform scale for pure scale/rotate/translate matrices.
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float GetTransformWidthScale(Matrix4x4 transform)
    {
        if (transform.IsIdentity)
        {
            return 1F;
        }

        float det = (transform.M11 * transform.M22) - (transform.M12 * transform.M21);
        return MathF.Sqrt(MathF.Abs(det));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector2 ExtractScale(Matrix4x4 matrix)
        => new(
            MathF.Sqrt((matrix.M11 * matrix.M11) + (matrix.M12 * matrix.M12)),
            MathF.Sqrt((matrix.M21 * matrix.M21) + (matrix.M22 * matrix.M22)));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Matrix4x4 ComputeResidual(Vector2 scale, Matrix4x4 matrix)
        => Matrix4x4.CreateScale(1F / scale.X, 1F / scale.Y, 1F) * matrix;

    /// <summary>
    /// Packs the stroke join flags consumed by the staged flatten shader.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static StyleFlags EncodeStrokeJoinFlags(LineJoin lineJoin)
        => lineJoin switch
        {
            LineJoin.Miter => StyleFlags.JoinMiter,
            LineJoin.MiterRevert => StyleFlags.JoinMiter | StyleFlags.JoinMiterRevert,
            LineJoin.MiterRound => StyleFlags.JoinMiter | StyleFlags.JoinMiterRound,
            LineJoin.Round => StyleFlags.JoinRound,
            _ => StyleFlags.None
        };

    /// <summary>
    /// Packs the start and end cap flags consumed by the staged flatten shader.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static StyleFlags EncodeStrokeCapFlags(LineCap lineCap)
        => lineCap switch
        {
            LineCap.Square => StyleFlags.StartCapSquare | StyleFlags.EndCapSquare,
            LineCap.Round => StyleFlags.StartCapRound | StyleFlags.EndCapRound,
            _ => StyleFlags.None
        };

    /// <summary>
    /// Packs the style words that describe fill behavior for one draw record.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static (uint Style0, uint Style1, uint Style2, uint Style3, uint Style4) GetFillStyle(GraphicsOptions options, IntersectionRule intersectionRule)
    {
        StyleFlags styleFlags = intersectionRule == IntersectionRule.EvenOdd ? StyleFlags.Fill : StyleFlags.None;
        return ((uint)styleFlags, 0U, PackStyleDrawFlags(options), 0U, 0U);
    }

    /// <summary>
    /// Packs the draw-flags word carried through flatten into coarse and fine.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint PackStyleDrawFlags(GraphicsOptions options)
        => (PackBlendMode(options) << 1) | (PackBlendAlpha(options.BlendPercentage) << 14);

    /// <summary>
    /// Packs the blend percentage into the shader draw-flags alpha field.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint PackBlendAlpha(float blendPercentage)
        => (uint)Math.Clamp((int)MathF.Round(Math.Clamp(blendPercentage, 0F, 1F) * 65535F), 0, 65535);

    /// <summary>
    /// Packs the individual scene streams into one final scene-word buffer using the resolved layout.
    /// </summary>
    private static void PackSceneData(
        GpuSceneLayout layout,
        int pathTagWordCount,
        ReadOnlySpan<byte> pathTags,
        ReadOnlySpan<uint> pathData,
        ReadOnlySpan<uint> drawTags,
        ReadOnlySpan<uint> drawData,
        ReadOnlySpan<uint> transforms,
        ReadOnlySpan<uint> styles,
        Span<uint> sceneWords)
    {
        // Path tags are byte-addressed in the front of the packed scene buffer, but the
        // overall scene layout is word-addressed. The padded prefix is therefore cleared
        // only for the tail bytes that are not overwritten by the copied tag payload.
        Span<byte> sceneBytes = MemoryMarshal.Cast<uint, byte>(sceneWords);
        int paddedPathTagBytes = checked(pathTagWordCount * sizeof(uint));
        pathTags.CopyTo(sceneBytes);
        sceneBytes[pathTags.Length..paddedPathTagBytes].Clear();
        pathData.CopyTo(sceneWords[(int)layout.PathDataBase..]);
        drawTags.CopyTo(sceneWords[(int)layout.DrawTagBase..]);
        drawData.CopyTo(sceneWords[(int)layout.DrawDataBase..]);
        transforms.CopyTo(sceneWords[(int)layout.TransformBase..]);
        styles.CopyTo(sceneWords[(int)layout.StyleBase..]);
    }

    /// <summary>
    /// Reinterprets one single-precision float as its raw IEEE 754 bit pattern.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint BitcastSingle(float value)
        => unchecked((uint)BitConverter.SingleToInt32Bits(value));

    /// <summary>
    /// Rounds up integer division for tile and buffer planning.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int DivideRoundUp(int value, int divisor)
        => (value + divisor - 1) / divisor;

    /// <summary>
    /// Rounds <paramref name="value"/> up to the next multiple of <paramref name="alignment"/>.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int AlignUp(int value, int alignment)
        => value + ((alignment - (value % alignment)) % alignment);

    /// <summary>
    /// Converts an absolute scene rectangle into root-target-local coordinates.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Rectangle ToTargetLocal(in Rectangle absoluteBounds, in Rectangle rootTargetBounds)
        => new(
            absoluteBounds.X - rootTargetBounds.X,
            absoluteBounds.Y - rootTargetBounds.Y,
            absoluteBounds.Width,
            absoluteBounds.Height);

    /// <summary>
    /// Packs one solid brush color into the staged scene's premultiplied RGBA8 payload format.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint PackSolidColor(SolidBrush solidBrush)
        => PackPremultipliedColor(solidBrush.Color);

    /// <summary>
    /// Appends the draw-data payload for one encoded draw tag.
    /// </summary>
    private static void AppendDrawData(
        Brush brush,
        Rectangle brushBounds,
        GraphicsOptions graphicsOptions,
        uint drawTag,
        Vector2 scale,
        Matrix4x4 transform,
        Rectangle rootTargetBounds,
        ref OwnedStream<uint> drawData,
        ref OwnedStream<uint> gradientPixels,
        ref OwnedStream<uint> pathGradientData,
        List<GpuImageDescriptor> images,
        ref int gradientRowCount)
    {
        // The draw tag selects the payload layout, so this switch is the single place
        // where the encoded draw-data stream shape is kept in sync with the sizing logic.
        switch (drawTag)
        {
            case GpuSceneDrawTag.BeginClip:
                AppendBeginClipData(graphicsOptions, ref drawData);
                break;
            case GpuSceneDrawTag.FillColor:
                drawData.Add(PackSolidColor((SolidBrush)brush));
                break;
            case GpuSceneDrawTag.FillRecolor:
                AppendRecolorData((RecolorBrush)brush, ref drawData);
                break;
            case GpuSceneDrawTag.FillLinGradient:
                AppendLinearGradientData((LinearGradientBrush)brush, scale, ref drawData, ref gradientPixels, ref gradientRowCount);
                break;
            case GpuSceneDrawTag.FillRadGradient:
                AppendRadialGradientData((RadialGradientBrush)brush, scale, ref drawData, ref gradientPixels, ref gradientRowCount);
                break;
            case GpuSceneDrawTag.FillEllipticGradient:
                AppendEllipticGradientData((EllipticGradientBrush)brush, scale, ref drawData, ref gradientPixels, ref gradientRowCount);
                break;
            case GpuSceneDrawTag.FillSweepGradient:
                AppendSweepGradientData((SweepGradientBrush)brush, scale, ref drawData, ref gradientPixels, ref gradientRowCount);
                break;
            case GpuSceneDrawTag.FillPathGradient:
                AppendPathGradientData((PathGradientBrush)brush, transform, rootTargetBounds, ref drawData, ref pathGradientData);
                break;
            case GpuSceneDrawTag.FillImage:
                AppendImageData(brush, brushBounds, ref drawData, images);
                break;
            default:
                throw new UnreachableException($"Unsupported draw tag '{drawTag}' reached scene draw-data encoding.");
        }
    }

    /// <summary>
    /// Appends the draw-data payload for a begin-clip record.
    /// </summary>
    private static void AppendBeginClipData(GraphicsOptions options, ref OwnedStream<uint> drawData)
    {
        drawData.Add(PackBlendMode(options));
        drawData.Add(BitcastSingle(Math.Clamp(options.BlendPercentage, 0F, 1F)));
    }

    /// <summary>
    /// Packs the color and alpha composition modes into the draw-data layout consumed by the shaders.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint PackBlendMode(GraphicsOptions options)
        => (MapColorBlendMode(options.ColorBlendingMode) << 8) | MapAlphaCompositionMode(options.AlphaCompositionMode);

    /// <summary>
    /// Maps ImageSharp's color blend mode enum to the staged-scene shader contract.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint MapColorBlendMode(PixelColorBlendingMode mode)
        => mode switch
        {
            PixelColorBlendingMode.Normal => 0U,
            PixelColorBlendingMode.Multiply => 1U,
            PixelColorBlendingMode.Add => 16U,
            PixelColorBlendingMode.Subtract => 17U,
            PixelColorBlendingMode.Screen => 2U,
            PixelColorBlendingMode.Darken => 4U,
            PixelColorBlendingMode.Lighten => 5U,
            PixelColorBlendingMode.Overlay => 3U,
            PixelColorBlendingMode.HardLight => 8U,
            _ => throw new UnreachableException($"Unsupported color blending mode '{mode}'.")
        };

    /// <summary>
    /// Maps ImageSharp's alpha composition mode enum to the staged-scene shader contract.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint MapAlphaCompositionMode(PixelAlphaCompositionMode mode)
        => mode switch
        {
            PixelAlphaCompositionMode.SrcOver => 3U,
            PixelAlphaCompositionMode.Src => 1U,
            PixelAlphaCompositionMode.SrcAtop => 9U,
            PixelAlphaCompositionMode.SrcIn => 5U,
            PixelAlphaCompositionMode.SrcOut => 7U,
            PixelAlphaCompositionMode.Dest => 2U,
            PixelAlphaCompositionMode.DestAtop => 10U,
            PixelAlphaCompositionMode.DestOver => 4U,
            PixelAlphaCompositionMode.DestIn => 6U,
            PixelAlphaCompositionMode.DestOut => 8U,
            PixelAlphaCompositionMode.Clear => 0U,
            PixelAlphaCompositionMode.Xor => 11U,
            _ => throw new UnreachableException($"Unsupported alpha composition mode '{mode}'.")
        };

    /// <summary>
    /// Appends the recolor brush payload.
    /// </summary>
    private static void AppendRecolorData(RecolorBrush brush, ref OwnedStream<uint> drawData)
    {
        drawData.Add(PackPremultipliedColor(brush.SourceColor));
        drawData.Add(PackPremultipliedColor(brush.TargetColor));
        drawData.Add(BitcastSingle(brush.Threshold * 4F));
    }

    /// <summary>
    /// Appends the deferred image payload placeholder and records the patch site.
    /// </summary>
    private static void AppendImageData(
        Brush brush,
        Rectangle brushBounds,
        ref OwnedStream<uint> drawData,
        List<GpuImageDescriptor> images)
    {
        // The image payload words are patched once the atlas is built because the
        // uploader is the first place that knows the concrete TPixel texture format.
        int payloadWordOffset = drawData.Count;
        drawData.Add(0);
        drawData.Add(0);
        drawData.Add(0);
        if (brush is ImageBrush imageBrush)
        {
            drawData.Add(BitcastSingle(brushBounds.Left + imageBrush.Offset.X));
            drawData.Add(BitcastSingle(brushBounds.Top + imageBrush.Offset.Y));
        }
        else
        {
            drawData.Add(0);
            drawData.Add(0);
        }

        images.Add(new GpuImageDescriptor(brush, payloadWordOffset));
    }

    /// <summary>
    /// Appends the linear gradient payload and its packed ramp row.
    /// </summary>
    private static void AppendLinearGradientData(
        LinearGradientBrush brush,
        Vector2 scale,
        ref OwnedStream<uint> drawData,
        ref OwnedStream<uint> gradientPixels,
        ref int gradientRowCount)
    {
        uint indexMode = ((uint)gradientRowCount << 2) | MapExtendMode(brush.RepetitionMode);
        AppendGradientRamp(brush.ColorStops, ref gradientPixels);
        gradientRowCount++;

        PointF start = new(brush.StartPoint.X * scale.X, brush.StartPoint.Y * scale.Y);
        PointF end = new(brush.EndPoint.X * scale.X, brush.EndPoint.Y * scale.Y);

        drawData.Add(indexMode);
        drawData.Add(BitcastSingle(start.X));
        drawData.Add(BitcastSingle(start.Y));
        drawData.Add(BitcastSingle(end.X));
        drawData.Add(BitcastSingle(end.Y));
    }

    /// <summary>
    /// Appends the radial gradient payload and its packed ramp row.
    /// </summary>
    private static void AppendRadialGradientData(
        RadialGradientBrush brush,
        Vector2 scale,
        ref OwnedStream<uint> drawData,
        ref OwnedStream<uint> gradientPixels,
        ref int gradientRowCount)
    {
        uint indexMode = ((uint)gradientRowCount << 2) | MapExtendMode(brush.RepetitionMode);
        AppendGradientRamp(brush.ColorStops, ref gradientPixels);
        gradientRowCount++;

        PointF center0;
        float radius0;
        PointF center1;
        float radius1;

        if (brush.IsTwoCircle)
        {
            center0 = brush.Center0;
            radius0 = brush.Radius0;
            center1 = brush.Center1!.Value;
            radius1 = brush.Radius1!.Value;
        }
        else
        {
            center0 = brush.Center0;
            radius0 = 0F;
            center1 = brush.Center0;
            radius1 = brush.Radius0;
        }

        float radiusScale = 0.5F * (scale.X + scale.Y);
        center0 = new(center0.X * scale.X, center0.Y * scale.Y);
        center1 = new(center1.X * scale.X, center1.Y * scale.Y);
        radius0 *= radiusScale;
        radius1 *= radiusScale;

        drawData.Add(indexMode);
        drawData.Add(BitcastSingle(center0.X));
        drawData.Add(BitcastSingle(center0.Y));
        drawData.Add(BitcastSingle(center1.X));
        drawData.Add(BitcastSingle(center1.Y));
        drawData.Add(BitcastSingle(radius0));
        drawData.Add(BitcastSingle(radius1));
    }

    /// <summary>
    /// Appends the elliptic gradient payload and its packed ramp row.
    /// </summary>
    private static void AppendEllipticGradientData(
        EllipticGradientBrush brush,
        Vector2 scale,
        ref OwnedStream<uint> drawData,
        ref OwnedStream<uint> gradientPixels,
        ref int gradientRowCount)
    {
        uint indexMode = ((uint)gradientRowCount << 2) | MapExtendMode(brush.RepetitionMode);
        AppendGradientRamp(brush.ColorStops, ref gradientPixels);
        gradientRowCount++;

        // The perpendicular axis of the ellipse must be constructed in local space where it is
        // geometrically orthogonal to the main axis; componentwise scale-bake does not commute with
        // rotation, so reconstructing it from the scale-baked main axis in the shader goes wrong
        // under non-uniform scale. Bake all three points instead and let the shader derive the
        // device-space axis ratio from the transformed distances.
        PointF localCenter = brush.Center;
        PointF localAxisEnd = brush.ReferenceAxisEnd;
        Vector2 localAxis = new(localAxisEnd.X - localCenter.X, localAxisEnd.Y - localCenter.Y);
        Vector2 localPerpendicular = new Vector2(-localAxis.Y, localAxis.X) * brush.AxisRatio;
        PointF localSecondEnd = new(localCenter.X + localPerpendicular.X, localCenter.Y + localPerpendicular.Y);

        PointF center = new(localCenter.X * scale.X, localCenter.Y * scale.Y);
        PointF axisEnd = new(localAxisEnd.X * scale.X, localAxisEnd.Y * scale.Y);
        PointF secondEnd = new(localSecondEnd.X * scale.X, localSecondEnd.Y * scale.Y);

        drawData.Add(indexMode);
        drawData.Add(BitcastSingle(center.X));
        drawData.Add(BitcastSingle(center.Y));
        drawData.Add(BitcastSingle(axisEnd.X));
        drawData.Add(BitcastSingle(axisEnd.Y));
        drawData.Add(BitcastSingle(secondEnd.X));
        drawData.Add(BitcastSingle(secondEnd.Y));
    }

    /// <summary>
    /// Appends the sweep gradient payload and its packed ramp row.
    /// </summary>
    private static void AppendSweepGradientData(
        SweepGradientBrush brush,
        Vector2 scale,
        ref OwnedStream<uint> drawData,
        ref OwnedStream<uint> gradientPixels,
        ref int gradientRowCount)
    {
        uint indexMode = ((uint)gradientRowCount << 2) | MapExtendMode(brush.RepetitionMode);
        AppendGradientRamp(brush.ColorStops, ref gradientPixels);
        gradientRowCount++;

        float t0 = brush.StartAngleDegrees / 360F;
        float t1 = brush.EndAngleDegrees / 360F;
        if (MathF.Abs(t1 - t0) < 1e-6F)
        {
            t1 = t0 + 1F;
        }

        PointF center = new(brush.Center.X * scale.X, brush.Center.Y * scale.Y);

        drawData.Add(indexMode);
        drawData.Add(BitcastSingle(center.X));
        drawData.Add(BitcastSingle(center.Y));
        drawData.Add(BitcastSingle(t0));
        drawData.Add(BitcastSingle(t1));
    }

    /// <summary>
    /// Appends the path-gradient payload in target-local coordinates.
    /// </summary>
    private static void AppendPathGradientData(
        PathGradientBrush brush,
        Matrix4x4 transform,
        Rectangle rootTargetBounds,
        ref OwnedStream<uint> drawData,
        ref OwnedStream<uint> pathGradientData)
    {
        ReadOnlySpan<PointF> points = brush.Points;
        ReadOnlySpan<Color> colors = brush.Colors;
        int edgeCount = points.Length;
        PointF center = default;

        for (int i = 0; i < edgeCount; i++)
        {
            PointF point = TransformPathGradientPoint(points[i], transform, rootTargetBounds);
            center = new PointF(center.X + point.X, center.Y + point.Y);
        }

        center = new PointF(center.X / edgeCount, center.Y / edgeCount);

        float maxDistance = 0F;
        for (int i = 0; i < edgeCount; i++)
        {
            PointF point = TransformPathGradientPoint(points[i], transform, rootTargetBounds);
            maxDistance = MathF.Max(maxDistance, Vector2.Distance(point, center));
        }

        int dataOffset = pathGradientData.Count;
        Span<uint> payload = pathGradientData.GetAppendSpan(PathGradientHeaderWordCount + (edgeCount * PathGradientEdgeWordCount));
        payload[0] = BitcastSingle(center.X);
        payload[1] = BitcastSingle(center.Y);
        payload[2] = BitcastSingle(maxDistance);
        payload[3] = PackUnpremultipliedColor(brush.CenterColor);

        for (int i = 0; i < edgeCount; i++)
        {
            int payloadOffset = PathGradientHeaderWordCount + (i * PathGradientEdgeWordCount);
            PointF start = TransformPathGradientPoint(points[i], transform, rootTargetBounds);
            PointF end = TransformPathGradientPoint(points[(i + 1) % edgeCount], transform, rootTargetBounds);

            payload[payloadOffset] = BitcastSingle(start.X);
            payload[payloadOffset + 1] = BitcastSingle(start.Y);
            payload[payloadOffset + 2] = BitcastSingle(end.X);
            payload[payloadOffset + 3] = BitcastSingle(end.Y);
            payload[payloadOffset + 4] = PackUnpremultipliedColor(colors[i % colors.Length]);
            payload[payloadOffset + 5] = PackUnpremultipliedColor(colors[(i + 1) % colors.Length]);
        }

        pathGradientData.Advance(payload.Length);
        drawData.Add((uint)dataOffset);
        drawData.Add((uint)edgeCount);
        drawData.Add(brush.HasExplicitCenterColor ? 1U : 0U);
        drawData.Add(0U);
    }

    /// <summary>
    /// Transforms one path-gradient point into the target-local coordinates consumed by the fine pass.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static PointF TransformPathGradientPoint(
        PointF point,
        Matrix4x4 transform,
        Rectangle rootTargetBounds)
    {
        PointF transformed = transform.IsIdentity ? point : PointF.Transform(point, transform);
        return new PointF(
            transformed.X - rootTargetBounds.X,
            transformed.Y - rootTargetBounds.Y);
    }

    /// <summary>
    /// Appends one packed gradient ramp row sampled across the fixed gradient width.
    /// </summary>
    private static void AppendGradientRamp(ReadOnlySpan<ColorStop> colorStops, ref OwnedStream<uint> gradientPixels)
    {
        for (int x = 0; x < GradientWidth; x++)
        {
            float t = x / (float)(GradientWidth - 1);
            gradientPixels.Add(EvaluateGradientColor(colorStops, t));
        }
    }

    /// <summary>
    /// Evaluates the packed premultiplied gradient color at the normalized parameter.
    /// </summary>
    private static uint EvaluateGradientColor(ReadOnlySpan<ColorStop> colorStops, float t)
    {
        // Walk the stop list once to find the enclosing interval, then interpolate within it.
        ColorStop from = colorStops[0];
        ColorStop to = colorStops[0];
        for (int i = 0; i < colorStops.Length; i++)
        {
            to = colorStops[i];
            if (to.Ratio > t)
            {
                break;
            }

            from = to;
        }

        if (from.Color.Equals(to.Color) || to.Ratio == from.Ratio)
        {
            return PackPremultipliedColor(from.Color);
        }

        float localT = (t - from.Ratio) / (to.Ratio - from.Ratio);
        Vector4 color = Vector4.Lerp(from.Color.ToScaledVector4(), to.Color.ToScaledVector4(), localT);
        return PackPremultipliedColor(Color.FromScaledVector(color));
    }

    /// <summary>
    /// Packs one premultiplied color into the staged scene's RGBA8 payload format.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint PackPremultipliedColor(in Color color)
    {
        // The staged scene keeps colors as one packed RGBA8 word. That gives up precision versus
        // carrying float channels through the scene buffer, but it keeps the encoded payload small
        // so the CPU writes less per draw and the shader reads less per sample.
        Vector4 vector = color.ToScaledVector4();
        Premultiply(ref vector);
        return Rgba32.FromScaledVector4(vector).Rgba;
    }

    /// <summary>
    /// Packs one straight-alpha color into RGBA8 payload format.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint PackUnpremultipliedColor(in Color color)
        => color.ToPixel<Rgba32>().Rgba;

    /// <summary>
    /// Premultiplies the RGB channels of a normalized RGBA vector by its alpha channel.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Premultiply(ref Vector4 source)
    {
        // Load into a local variable to prevent accessing the source from memory multiple times.
        Vector4 src = source;
        Vector4 alpha = PermuteW(src);
        source = WithW(src * alpha, alpha);
    }

    /// <summary>
    /// Broadcasts the W component of <paramref name="value"/> into every lane.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector4 PermuteW(Vector4 value)
    {
        if (Sse.IsSupported)
        {
            return Sse.Shuffle(value.AsVector128(), value.AsVector128(), 0b_11_11_11_11).AsVector4();
        }

        return new Vector4(value.W);
    }

    /// <summary>
    /// Replaces the W component of <paramref name="value"/> with the supplied broadcast vector.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector4 WithW(Vector4 value, Vector4 w)
    {
        if (Sse41.IsSupported)
        {
            return Sse41.Insert(value.AsVector128(), w.AsVector128(), 0b11_11_0000).AsVector4();
        }

        if (Sse.IsSupported)
        {
            // Create tmp as <w[3], w[0], value[2], value[0]>
            // Then return <value[0], value[1], tmp[2], tmp[0]> (which is <value[0], value[1], value[2], w[3]>)
            Vector128<float> tmp = Sse.Shuffle(w.AsVector128(), value.AsVector128(), 0b00_10_00_11);
            return Sse.Shuffle(value.AsVector128(), tmp, 0b00_10_01_00).AsVector4();
        }

        value.W = w.W;
        return value;
    }

    /// <summary>
    /// Maps ImageSharp's gradient repetition mode to the staged-scene shader contract.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint MapExtendMode(GradientRepetitionMode repetitionMode)
        => repetitionMode switch
        {
            GradientRepetitionMode.Repeat => 1U,
            GradientRepetitionMode.Reflect => 2U,
            _ => 0U
        };
}

/// <summary>
/// Flush-scoped encoded scene payload.
/// </summary>
internal sealed class WebGPUEncodedScene : IDisposable
{
    /// <summary>
    /// Gets the empty encoded scene instance.
    /// </summary>
    public static WebGPUEncodedScene Empty { get; } = new(
        Size.Empty,
        0,
        null,
        0,
        null,
        null,
        0,
        [],
        0,
        default,
        0,
        0,
        0,
        0,
        0,
        0,
        0,
        0,
        0,
        0,
        0,
        0,
        0,
        0,
        0,
        RasterizationMode.Antialiased,
        0F);

    private readonly IMemoryOwner<uint>? sceneDataOwner;
    private readonly IMemoryOwner<uint>? gradientPixelsOwner;
    private readonly IMemoryOwner<uint>? pathGradientDataOwner;
    private readonly List<GpuImageDescriptor> images;
    private bool disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="WebGPUEncodedScene"/> class.
    /// </summary>
    public WebGPUEncodedScene(
        Size targetSize,
        int infoWordCount,
        IMemoryOwner<uint>? sceneDataOwner,
        int sceneWordCount,
        IMemoryOwner<uint>? gradientPixelsOwner,
        IMemoryOwner<uint>? pathGradientDataOwner,
        int pathGradientDataWordCount,
        List<GpuImageDescriptor> images,
        int gradientRowCount,
        GpuSceneLayout layout,
        int fillCount,
        int pathCount,
        int lineCount,
        int pathTagByteCount,
        int pathTagWordCount,
        int pathDataWordCount,
        int drawTagCount,
        int drawDataWordCount,
        int transformWordCount,
        int styleWordCount,
        int clipCount,
        int uniqueDefinitionCount,
        int totalPathRowCount,
        int tileCountX,
        int tileCountY,
        RasterizationMode fineRasterizationMode,
        float fineCoverageThreshold)
    {
        this.TargetSize = targetSize;
        this.InfoWordCount = infoWordCount;
        this.sceneDataOwner = sceneDataOwner;
        this.gradientPixelsOwner = gradientPixelsOwner;
        this.pathGradientDataOwner = pathGradientDataOwner;
        this.PathGradientDataWordCount = pathGradientDataWordCount;
        this.images = images;
        this.SceneWordCount = sceneWordCount;
        this.GradientRowCount = gradientRowCount;
        this.Layout = layout;
        this.FillCount = fillCount;
        this.PathCount = pathCount;
        this.LineCount = lineCount;
        this.PathTagByteCount = pathTagByteCount;
        this.PathTagWordCount = pathTagWordCount;
        this.PathDataWordCount = pathDataWordCount;
        this.DrawTagCount = drawTagCount;
        this.DrawDataWordCount = drawDataWordCount;
        this.TransformWordCount = transformWordCount;
        this.StyleWordCount = styleWordCount;
        this.ClipCount = clipCount;
        this.UniqueDefinitionCount = uniqueDefinitionCount;
        this.TotalPathRowCount = totalPathRowCount;
        this.TileCountX = tileCountX;
        this.TileCountY = tileCountY;
        this.FineRasterizationMode = fineRasterizationMode;
        this.FineCoverageThreshold = fineCoverageThreshold;
    }

    /// <summary>
    /// Gets the target size for the encoded scene.
    /// </summary>
    public Size TargetSize { get; }

    /// <summary>
    /// Gets the packed scene-word buffer.
    /// </summary>
    public ReadOnlyMemory<uint> SceneData
        => this.sceneDataOwner is null ? ReadOnlyMemory<uint>.Empty : this.sceneDataOwner.Memory[..this.SceneWordCount];

    /// <summary>
    /// Gets the packed gradient-ramp pixels.
    /// </summary>
    public ReadOnlyMemory<uint> GradientPixels
        => this.gradientPixelsOwner is null ? ReadOnlyMemory<uint>.Empty : this.gradientPixelsOwner.Memory[..(this.GradientRowCount * 512)];

    /// <summary>
    /// Gets the encoded path-gradient payload stream.
    /// </summary>
    public ReadOnlyMemory<uint> PathGradientData
        => this.pathGradientDataOwner is null ? ReadOnlyMemory<uint>.Empty : this.pathGradientDataOwner.Memory[..this.PathGradientDataWordCount];

    /// <summary>
    /// Gets the encoded path-gradient payload word count.
    /// </summary>
    public int PathGradientDataWordCount { get; }

    /// <summary>
    /// Gets the combined info/path-gradient prefix word count before dynamic bin data.
    /// </summary>
    public int InfoBufferWordCount => this.InfoWordCount + this.PathGradientDataWordCount;

    /// <summary>
    /// Gets the deferred image descriptors that must be patched after atlas creation.
    /// </summary>
    public IReadOnlyList<GpuImageDescriptor> Images => this.images;

    /// <summary>
    /// Gets the number of fill records in the encoded scene.
    /// </summary>
    public int FillCount { get; }

    /// <summary>
    /// Gets the total info-word count for the encoded draw stream.
    /// </summary>
    public int InfoWordCount { get; }

    /// <summary>
    /// Gets the total number of uint words in the packed scene buffer.
    /// </summary>
    public int SceneWordCount { get; }

    /// <summary>
    /// Gets the number of gradient-ramp rows.
    /// </summary>
    public int GradientRowCount { get; }

    /// <summary>
    /// Gets the number of encoded paths.
    /// </summary>
    public int PathCount { get; }

    /// <summary>
    /// Gets the number of encoded non-horizontal lines.
    /// </summary>
    public int LineCount { get; }

    /// <summary>
    /// Gets the unpadded path-tag byte count.
    /// </summary>
    public int PathTagByteCount { get; }

    /// <summary>
    /// Gets the padded path-tag word count.
    /// </summary>
    public int PathTagWordCount { get; }

    /// <summary>
    /// Gets the path-data word count.
    /// </summary>
    public int PathDataWordCount { get; }

    /// <summary>
    /// Gets the draw-tag count.
    /// </summary>
    public int DrawTagCount { get; }

    /// <summary>
    /// Gets the draw-data word count.
    /// </summary>
    public int DrawDataWordCount { get; }

    /// <summary>
    /// Gets the transform word count.
    /// </summary>
    public int TransformWordCount { get; }

    /// <summary>
    /// Gets the style word count.
    /// </summary>
    public int StyleWordCount { get; }

    /// <summary>
    /// Gets the clip record count.
    /// </summary>
    public int ClipCount { get; }

    /// <summary>
    /// Gets the number of unique scene definitions.
    /// </summary>
    public int UniqueDefinitionCount { get; }

    /// <summary>
    /// Gets the CPU-side estimate of the number of active tile rows used by sparse path metadata.
    /// </summary>
    public int TotalPathRowCount { get; }

    /// <summary>
    /// Gets the tile count on the X axis.
    /// </summary>
    public int TileCountX { get; }

    /// <summary>
    /// Gets the tile count on the Y axis.
    /// </summary>
    public int TileCountY { get; }

    /// <summary>
    /// Gets the fine-pass rasterization mode selected for this flush.
    /// </summary>
    public RasterizationMode FineRasterizationMode { get; }

    /// <summary>
    /// Gets the scene-wide aliased coverage threshold consumed by the fine pass.
    /// </summary>
    public float FineCoverageThreshold { get; }

    /// <summary>
    /// Gets the total tile count.
    /// </summary>
    public int TileCount => this.TileCountX * this.TileCountY;

    /// <summary>
    /// Gets the packed GPU scene layout.
    /// </summary>
    public GpuSceneLayout Layout { get; }

    /// <summary>
    /// Overwrites one scene word in place.
    /// </summary>
    /// <param name="index">The scene-word index to update.</param>
    /// <param name="value">The replacement word value.</param>
    public void SetSceneWord(int index, uint value)
    {
        if (this.sceneDataOwner is null)
        {
            throw new InvalidOperationException("The scene buffer is not available.");
        }

        this.sceneDataOwner.Memory.Span[index] = value;
    }

    /// <summary>
    /// Releases the owned scene, gradient, and path-gradient buffers.
    /// </summary>
    public void Dispose()
    {
        if (this.disposed)
        {
            return;
        }

        this.disposed = true;
        this.sceneDataOwner?.Dispose();
        this.gradientPixelsOwner?.Dispose();
        this.pathGradientDataOwner?.Dispose();
    }
}

/// <summary>
/// Describes one deferred image payload patch site in the encoded draw-data stream.
/// </summary>
internal readonly struct GpuImageDescriptor
{
    /// <summary>
    /// Initializes a new instance of the <see cref="GpuImageDescriptor"/> struct.
    /// </summary>
    public GpuImageDescriptor(Brush brush, int drawDataWordOffset)
    {
        this.Brush = brush;
        this.DrawDataWordOffset = drawDataWordOffset;
    }

    /// <summary>
    /// Gets the image-producing brush instance.
    /// </summary>
    public Brush Brush { get; }

    /// <summary>
    /// Gets the draw-data word offset to patch once the image atlas is built.
    /// </summary>
    public int DrawDataWordOffset { get; }
}

/// <summary>
/// Represents one 2D affine transform encoded in the GPU scene format.
/// </summary>
internal readonly struct GpuSceneTransform : IEquatable<GpuSceneTransform>
{
    /// <summary>
    /// Gets the identity transform.
    /// </summary>
    public static readonly GpuSceneTransform Identity = new(1F, 0F, 0F, 1F, 0F, 0F, 0F, 0F, 1F);

    /// <summary>
    /// Initializes a new instance of the <see cref="GpuSceneTransform"/> struct.
    /// </summary>
    public GpuSceneTransform(float m11, float m12, float m21, float m22, float tx, float ty, float m14, float m24, float m44)
    {
        this.M11 = m11;
        this.M12 = m12;
        this.M21 = m21;
        this.M22 = m22;
        this.Tx = tx;
        this.Ty = ty;
        this.M14 = m14;
        this.M24 = m24;
        this.M44 = m44;
    }

    /// <summary>
    /// Creates a <see cref="GpuSceneTransform"/> from a <see cref="Matrix4x4"/>.
    /// Extracts the 9 elements needed for projective 2D transformation with z=0.
    /// </summary>
    public static GpuSceneTransform FromMatrix4x4(Matrix4x4 m)
        => new(m.M11, m.M12, m.M21, m.M22, m.M41, m.M42, m.M14, m.M24, m.M44);

    public float M11 { get; }

    public float M12 { get; }

    public float M21 { get; }

    public float M22 { get; }

    public float Tx { get; }

    public float Ty { get; }

    /// <summary>
    /// Gets the perspective element from row 1, column 4.
    /// </summary>
    public float M14 { get; }

    /// <summary>
    /// Gets the perspective element from row 2, column 4.
    /// </summary>
    public float M24 { get; }

    /// <summary>
    /// Gets the homogeneous scale element from row 4, column 4.
    /// </summary>
    public float M44 { get; }

    public bool Equals(GpuSceneTransform other)
        => this.M11 == other.M11
        && this.M12 == other.M12
        && this.M21 == other.M21
        && this.M22 == other.M22
        && this.Tx == other.Tx
        && this.Ty == other.Ty
        && this.M14 == other.M14
        && this.M24 == other.M24
        && this.M44 == other.M44;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is GpuSceneTransform other && this.Equals(other);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        HashCode hash = default;
        hash.Add(this.M11);
        hash.Add(this.M12);
        hash.Add(this.M21);
        hash.Add(this.M22);
        hash.Add(this.Tx);
        hash.Add(this.Ty);
        hash.Add(this.M14);
        hash.Add(this.M24);
        hash.Add(this.M44);
        return hash.ToHashCode();
    }
}

/// <summary>
/// Owns one growable contiguous stream backed by allocator storage.
/// </summary>
internal ref struct OwnedStream<T>
    where T : unmanaged
{
    private readonly MemoryAllocator allocator;
    private IMemoryOwner<T> owner;
    private Span<T> span;

    /// <summary>
    /// Initializes a new instance of the <see cref="OwnedStream{T}"/> struct.
    /// </summary>
    /// <param name="allocator">The allocator used for backing storage growth.</param>
    /// <param name="initialCapacity">The requested initial item capacity.</param>
    public OwnedStream(MemoryAllocator allocator, int initialCapacity)
    {
        this.allocator = allocator;
        this.owner = allocator.Allocate<T>(Math.Max(initialCapacity, 16));
        this.span = this.owner.Memory.Span;
        this.Count = 0;
    }

    /// <summary>
    /// Gets the number of written items in the stream.
    /// </summary>
    public int Count { get; private set; }

    /// <summary>
    /// Gets a reference to the item at the specified written index.
    /// </summary>
    /// <param name="index">The index to access.</param>
    /// <returns>A reference to the requested item.</returns>
    public ref T this[int index] => ref this.span[index];

    /// <summary>
    /// Gets the contiguous written prefix of the stream.
    /// </summary>
    public readonly ReadOnlySpan<T> WrittenSpan => this.span[..this.Count];

    /// <summary>
    /// Gets a writable reserved append window for a caller that will populate items in place.
    /// </summary>
    /// <param name="count">The number of contiguous items to reserve.</param>
    /// <returns>A writable span covering the reserved append window.</returns>
    public Span<T> GetAppendSpan(int count)
    {
        this.EnsureCapacity(this.Count + count);
        return this.span.Slice(this.Count, count);
    }

    /// <summary>
    /// Appends one item to the stream.
    /// </summary>
    /// <param name="value">The item to append.</param>
    public void Add(T value)
    {
        this.EnsureCapacity(this.Count + 1);
        this.span[this.Count++] = value;
    }

    /// <summary>
    /// Ensures the stream can append the requested additional item count without growing again.
    /// </summary>
    /// <param name="additionalCount">The number of additional items that will be appended.</param>
    public void EnsureAdditionalCapacity(int additionalCount)
    {
        if (additionalCount <= 0)
        {
            return;
        }

        this.EnsureCapacity(this.Count + additionalCount);
    }

    /// <summary>
    /// Resets the logical item count without reallocating the backing storage.
    /// </summary>
    /// <param name="count">The new logical item count.</param>
    public void SetCount(int count) => this.Count = count;

    /// <summary>
    /// Commits the requested number of previously reserved items to the logical stream length.
    /// </summary>
    /// <param name="count">The number of reserved items that were written.</param>
    public void Advance(int count) => this.Count += count;

    /// <summary>
    /// Disposes the current owner and clears the stream state.
    /// </summary>
    public void Dispose()
    {
        this.owner.Dispose();
        this.span = default;
        this.Count = 0;
    }

    /// <summary>
    /// Detaches the current owner from the stream without copying.
    /// </summary>
    /// <returns>The detached owner.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public IMemoryOwner<T> DetachOwner()
    {
        IMemoryOwner<T> detached = this.owner;
        this.span = default;
        this.Count = 0;
        return detached;
    }

    /// <summary>
    /// Grows the backing storage to satisfy the required total capacity.
    /// </summary>
    /// <param name="requiredCapacity">The total item capacity that must be available.</param>
    private void EnsureCapacity(int requiredCapacity)
    {
        if (requiredCapacity <= this.span.Length)
        {
            return;
        }

        // Grow by doubling for amortized append cost, but never below the exact
        // required size when a caller has already pre-reserved a larger single jump.
        IMemoryOwner<T> next = this.allocator.Allocate<T>(Math.Max(requiredCapacity, this.span.Length * 2));
        this.span[..this.Count].CopyTo(next.Memory.Span);
        this.owner.Dispose();
        this.owner = next;
        this.span = next.Memory.Span;
    }
}

#pragma warning restore SA1201
