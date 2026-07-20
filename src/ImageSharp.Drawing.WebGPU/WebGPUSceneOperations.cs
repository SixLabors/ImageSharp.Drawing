// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

#pragma warning disable SA1201 // Ordered WebGPU Apply metadata is kept together for readability.
#pragma warning disable SA1649 // This file groups the operation, range, and payload types used by one scene feature.

using SixLabors.ImageSharp.Processing;

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// Identifies one ordered operation inside a single retained WebGPU scene.
/// </summary>
internal enum WebGPUSceneOperationKind
{
    /// <summary>
    /// Renders one draw range into the current target.
    /// </summary>
    RenderRange,

    /// <summary>
    /// Applies an ImageSharp processor to the current target and draws it back through WebGPU scene data.
    /// </summary>
    Apply,

    /// <summary>
    /// Executes a native layer-effect plan and draws its working texture back through retained scene data.
    /// </summary>
    ShaderEffect,

    /// <summary>
    /// Begins rendering into a scoped-layer target.
    /// </summary>
    BeginLayer,

    /// <summary>
    /// Composites a completed scoped-layer target into its parent target.
    /// </summary>
    EndLayer
}

/// <summary>
/// One ordered operation retained by a single WebGPU scene.
/// </summary>
internal sealed class WebGPUSceneOperation
{
    /// <summary>
    /// Initializes a new instance of the <see cref="WebGPUSceneOperation"/> class.
    /// </summary>
    /// <param name="range">The encoded draw range to render.</param>
    public WebGPUSceneOperation(WebGPUSceneRange range)
    {
        this.Kind = WebGPUSceneOperationKind.RenderRange;
        this.Range = range;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="WebGPUSceneOperation"/> class.
    /// </summary>
    /// <param name="apply">The retained Apply operation data.</param>
    public WebGPUSceneOperation(WebGPUApplySceneItem apply)
    {
        this.Kind = WebGPUSceneOperationKind.Apply;
        this.Apply = apply;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="WebGPUSceneOperation"/> class.
    /// </summary>
    /// <param name="shaderEffect">The retained native shader-effect operation data.</param>
    public WebGPUSceneOperation(WebGPUShaderEffectSceneItem shaderEffect)
    {
        this.Kind = WebGPUSceneOperationKind.ShaderEffect;
        this.ShaderEffect = shaderEffect;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="WebGPUSceneOperation"/> class for a scoped-layer entry.
    /// </summary>
    /// <param name="layerBounds">The absolute bounds of the scoped layer.</param>
    /// <param name="layerTargetId">The stable target identity assigned to the scoped layer.</param>
    /// <param name="parentTargetId">The stable target identity of the containing target.</param>
    public WebGPUSceneOperation(Rectangle layerBounds, int layerTargetId, int parentTargetId)
    {
        this.Kind = WebGPUSceneOperationKind.BeginLayer;
        this.LayerBounds = layerBounds;
        this.LayerTargetId = layerTargetId;
        this.ParentTargetId = parentTargetId;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="WebGPUSceneOperation"/> class for a scoped-layer exit.
    /// </summary>
    /// <param name="compositeRange">The encoded range that composites the layer into its parent.</param>
    /// <param name="layerTargetId">The stable target identity assigned to the scoped layer.</param>
    /// <param name="parentTargetId">The stable target identity of the containing target.</param>
    public WebGPUSceneOperation(WebGPUSceneRange compositeRange, int layerTargetId, int parentTargetId)
    {
        this.Kind = WebGPUSceneOperationKind.EndLayer;
        this.Range = compositeRange;
        this.LayerTargetId = layerTargetId;
        this.ParentTargetId = parentTargetId;
    }

    /// <summary>
    /// Gets the operation kind.
    /// </summary>
    public WebGPUSceneOperationKind Kind { get; }

    /// <summary>
    /// Gets the encoded draw range for a render operation.
    /// Only meaningful when <see cref="Kind"/> is <see cref="WebGPUSceneOperationKind.RenderRange"/>; default otherwise.
    /// </summary>
    public WebGPUSceneRange Range { get; }

    /// <summary>
    /// Gets the retained Apply data for an Apply operation.
    /// Non-null only when <see cref="Kind"/> is <see cref="WebGPUSceneOperationKind.Apply"/>.
    /// </summary>
    public WebGPUApplySceneItem? Apply { get; }

    /// <summary>
    /// Gets the retained native effect data for a shader-effect operation.
    /// Non-null only when <see cref="Kind"/> is <see cref="WebGPUSceneOperationKind.ShaderEffect"/>.
    /// </summary>
    public WebGPUShaderEffectSceneItem? ShaderEffect { get; }

    /// <summary>
    /// Gets the absolute bounds of a scoped layer.
    /// Only meaningful when <see cref="Kind"/> is <see cref="WebGPUSceneOperationKind.BeginLayer"/>.
    /// </summary>
    public Rectangle LayerBounds { get; }

    /// <summary>
    /// Gets the stable target identity for a scoped-layer entry or exit.
    /// </summary>
    public int LayerTargetId { get; }

    /// <summary>
    /// Gets the stable parent target identity for a scoped-layer entry or exit.
    /// </summary>
    public int ParentTargetId { get; }

    /// <summary>
    /// Gets the number of consecutive dependency-independent Apply operations in the group that
    /// begins at this operation. The value is non-zero only for an Apply group head.
    /// </summary>
    public int ApplyGroupCount { get; private set; }

    /// <summary>
    /// Gets the maximum number of scheduling-status records awaiting validation immediately
    /// before this Apply group reads its source pixels.
    /// </summary>
    public int PendingStatusCapacity { get; private set; }

    /// <summary>
    /// Gets the dense zero-based Apply index used by the flush readback layout.
    /// Only meaningful when <see cref="Kind"/> is <see cref="WebGPUSceneOperationKind.Apply"/>.
    /// </summary>
    public int ApplyIndex { get; private set; }

    /// <summary>
    /// Completes the retained barrier metadata for an Apply group head.
    /// </summary>
    /// <param name="applyGroupCount">The number of Apply operations sharing the source snapshot.</param>
    /// <param name="pendingStatusCapacity">The maximum number of status records produced before the shared readback.</param>
    public void SetApplyGroup(int applyGroupCount, int pendingStatusCapacity)
    {
        this.ApplyGroupCount = applyGroupCount;
        this.PendingStatusCapacity = pendingStatusCapacity;
    }

    /// <summary>
    /// Assigns the dense Apply index used by the generic render-time layout.
    /// </summary>
    /// <param name="applyIndex">The zero-based Apply index.</param>
    public void SetApplyIndex(int applyIndex) => this.ApplyIndex = applyIndex;
}

/// <summary>
/// Describes a draw range inside the packed WebGPU scene streams.
/// </summary>
internal readonly struct WebGPUSceneRange
{
    /// <summary>
    /// Initializes a new instance of the <see cref="WebGPUSceneRange"/> struct.
    /// </summary>
    /// <param name="targetBounds">The target bounds used when this range was lowered.</param>
    /// <param name="pathTagWordStart">The word offset of the range's path tags in the packed scene buffer.</param>
    /// <param name="pathTagByteCount">The byte count of the range's path tags.</param>
    /// <param name="pathDataWordStart">The word offset of the range's path data in the packed scene buffer.</param>
    /// <param name="pathDataWordCount">The word count of the range's path data.</param>
    /// <param name="drawTagStart">The draw-tag offset of the range.</param>
    /// <param name="drawTagCount">The draw-tag count of the range.</param>
    /// <param name="drawDataWordStart">The draw-data word offset of the range.</param>
    /// <param name="drawDataWordCount">The draw-data word count of the range.</param>
    /// <param name="transformWordStart">The transform word offset of the range.</param>
    /// <param name="transformWordCount">The transform word count of the range.</param>
    /// <param name="styleWordStart">The style word offset of the range.</param>
    /// <param name="styleWordCount">The style word count of the range.</param>
    /// <param name="infoWordCount">The range-local info word count.</param>
    /// <param name="pathCount">The path count in the range.</param>
    /// <param name="clipCount">The clip count in the range.</param>
    /// <param name="fillCount">The visible fill count in the range.</param>
    /// <param name="lineCount">The line count in the range.</param>
    /// <param name="totalPathRowCount">The estimated sparse row count for the range.</param>
    /// <param name="estimatedTileCrossings">The CPU-side upper bound for the range's tile-boundary crossings.</param>
    /// <param name="estimatedBinFootprint">The CPU-side upper bound for the range's per-(draw, bin) binning records.</param>
    public WebGPUSceneRange(
        Rectangle targetBounds,
        int pathTagWordStart,
        int pathTagByteCount,
        int pathDataWordStart,
        int pathDataWordCount,
        int drawTagStart,
        int drawTagCount,
        int drawDataWordStart,
        int drawDataWordCount,
        int transformWordStart,
        int transformWordCount,
        int styleWordStart,
        int styleWordCount,
        int infoWordCount,
        int pathCount,
        int clipCount,
        int fillCount,
        int lineCount,
        int totalPathRowCount,
        long estimatedTileCrossings,
        long estimatedBinFootprint)
    {
        this.TargetBounds = targetBounds;
        this.PathTagWordStart = pathTagWordStart;
        this.PathTagByteCount = pathTagByteCount;
        this.PathDataWordStart = pathDataWordStart;
        this.PathDataWordCount = pathDataWordCount;
        this.DrawTagStart = drawTagStart;
        this.DrawTagCount = drawTagCount;
        this.DrawDataWordStart = drawDataWordStart;
        this.DrawDataWordCount = drawDataWordCount;
        this.TransformWordStart = transformWordStart;
        this.TransformWordCount = transformWordCount;
        this.StyleWordStart = styleWordStart;
        this.StyleWordCount = styleWordCount;
        this.InfoWordCount = infoWordCount;
        this.PathCount = pathCount;
        this.ClipCount = clipCount;
        this.FillCount = fillCount;
        this.LineCount = lineCount;
        this.TotalPathRowCount = totalPathRowCount;
        this.EstimatedTileCrossings = estimatedTileCrossings;
        this.EstimatedBinFootprint = estimatedBinFootprint;
    }

    /// <summary>
    /// Gets the target bounds used when this range was lowered.
    /// </summary>
    public Rectangle TargetBounds { get; }

    /// <summary>
    /// Gets the word offset of the range's path tags in the packed scene buffer.
    /// </summary>
    public int PathTagWordStart { get; }

    /// <summary>
    /// Gets the byte count of the range's path tags.
    /// </summary>
    public int PathTagByteCount { get; }

    /// <summary>
    /// Gets the word offset of the range's path data in the packed scene buffer.
    /// </summary>
    public int PathDataWordStart { get; }

    /// <summary>
    /// Gets the word count of the range's path data.
    /// </summary>
    public int PathDataWordCount { get; }

    /// <summary>
    /// Gets the draw-tag offset of the range.
    /// </summary>
    public int DrawTagStart { get; }

    /// <summary>
    /// Gets the draw-tag count of the range.
    /// </summary>
    public int DrawTagCount { get; }

    /// <summary>
    /// Gets the draw-data word offset of the range.
    /// </summary>
    public int DrawDataWordStart { get; }

    /// <summary>
    /// Gets the draw-data word count of the range.
    /// </summary>
    public int DrawDataWordCount { get; }

    /// <summary>
    /// Gets the transform word offset of the range.
    /// </summary>
    public int TransformWordStart { get; }

    /// <summary>
    /// Gets the transform word count of the range.
    /// </summary>
    public int TransformWordCount { get; }

    /// <summary>
    /// Gets the style word offset of the range.
    /// </summary>
    public int StyleWordStart { get; }

    /// <summary>
    /// Gets the style word count of the range.
    /// </summary>
    public int StyleWordCount { get; }

    /// <summary>
    /// Gets the range-local info word count.
    /// </summary>
    public int InfoWordCount { get; }

    /// <summary>
    /// Gets the path count in the range.
    /// </summary>
    public int PathCount { get; }

    /// <summary>
    /// Gets the clip count in the range.
    /// </summary>
    public int ClipCount { get; }

    /// <summary>
    /// Gets the visible fill count in the range.
    /// </summary>
    public int FillCount { get; }

    /// <summary>
    /// Gets the line count in the range.
    /// </summary>
    public int LineCount { get; }

    /// <summary>
    /// Gets the estimated sparse row count for the range.
    /// </summary>
    public int TotalPathRowCount { get; }

    /// <summary>
    /// Gets the CPU-side upper bound for the range's tile-boundary crossings.
    /// </summary>
    public long EstimatedTileCrossings { get; }

    /// <summary>
    /// Gets the CPU-side upper bound for the range's per-(draw, bin) binning records.
    /// </summary>
    public long EstimatedBinFootprint { get; }

    /// <summary>
    /// Gets the maximum number of allocator-status records produced by this range. The staged
    /// pipeline emits one record per chunk, and one tile row is the smallest legal chunk.
    /// </summary>
    public int MaximumStatusRecordCount
        => this.TargetBounds.Height > 0
            ? ((this.TargetBounds.Height - 1) / WebGPUSceneEncoder.TileHeight) + 1
            : 1;
}

/// <summary>
/// Retained WebGPU data for one Apply operation.
/// </summary>
internal sealed class WebGPUApplySceneItem
{
    /// <summary>
    /// Initializes a new instance of the <see cref="WebGPUApplySceneItem"/> class.
    /// </summary>
    /// <param name="operation">The ImageSharp processor operation.</param>
    /// <param name="effect">The layer effect represented by the operation, or <see langword="null"/> for a direct Apply operation.</param>
    /// <param name="inputRect">The rectangle containing the source pixels supplied to the operation.</param>
    /// <param name="outputRect">The bounds within which the processed pixels are written.</param>
    /// <param name="readOffset">The offset subtracted from <paramref name="inputRect"/> when reading the source pixels.</param>
    /// <param name="drawRange">The encoded image-fill draw range inside the retained scene.</param>
    public WebGPUApplySceneItem(
        Action<IImageProcessingContext> operation,
        LayerEffect? effect,
        Rectangle inputRect,
        Rectangle outputRect,
        Point readOffset,
        WebGPUSceneRange drawRange)
    {
        this.Operation = operation;
        this.Effect = effect;
        this.InputRect = inputRect;
        this.OutputRect = outputRect;
        this.ReadOffset = readOffset;
        this.DrawRange = drawRange;
    }

    /// <summary>
    /// Gets the ImageSharp processor operation.
    /// </summary>
    public Action<IImageProcessingContext> Operation { get; }

    /// <summary>
    /// Gets the layer effect represented by the operation, or <see langword="null"/> for a direct Apply operation.
    /// </summary>
    public LayerEffect? Effect { get; }

    /// <summary>
    /// Gets the rectangle containing the source pixels supplied to the operation.
    /// </summary>
    public Rectangle InputRect { get; }

    /// <summary>
    /// Gets the bounds within which the processed pixels are written.
    /// </summary>
    public Rectangle OutputRect { get; }

    /// <summary>
    /// Gets the offset subtracted from <see cref="InputRect"/> when reading the source pixels, so
    /// a write-back recorded at an offset still reads the pre-offset region.
    /// </summary>
    public Point ReadOffset { get; }

    /// <summary>
    /// Gets the encoded image-fill draw range inside the retained scene.
    /// </summary>
    public WebGPUSceneRange DrawRange { get; }
}

/// <summary>
/// Retained WebGPU data for one native layer-effect operation.
/// </summary>
internal sealed class WebGPUShaderEffectSceneItem
{
    /// <summary>
    /// Initializes a new instance of the <see cref="WebGPUShaderEffectSceneItem"/> class.
    /// </summary>
    /// <param name="effect">The shader effect that owns the ordered GPU passes.</param>
    /// <param name="inputRect">The rectangle represented by the effect working texture.</param>
    /// <param name="outputRect">The bounds within which the working texture is written back.</param>
    /// <param name="readOffset">The offset subtracted from <paramref name="inputRect"/> when reading source pixels.</param>
    /// <param name="drawRange">The encoded working-texture write-back range.</param>
    public WebGPUShaderEffectSceneItem(IWebGPUShaderEffectSource effect, Rectangle inputRect, Rectangle outputRect, Point readOffset, WebGPUSceneRange drawRange)
    {
        this.Effect = effect;
        this.InputRect = inputRect;
        this.OutputRect = outputRect;
        this.ReadOffset = readOffset;
        this.DrawRange = drawRange;
    }

    /// <summary>
    /// Gets the shader effect that owns the ordered GPU passes.
    /// </summary>
    public IWebGPUShaderEffectSource Effect { get; }

    /// <summary>
    /// Gets the rectangle represented by the effect working texture.
    /// </summary>
    public Rectangle InputRect { get; }

    /// <summary>
    /// Gets the bounds within which the working texture is written back.
    /// </summary>
    public Rectangle OutputRect { get; }

    /// <summary>
    /// Gets the offset subtracted from <see cref="InputRect"/> when resolving the source snapshot.
    /// </summary>
    public Point ReadOffset { get; }

    /// <summary>
    /// Gets the encoded working-texture write-back range.
    /// </summary>
    public WebGPUSceneRange DrawRange { get; }
}

#pragma warning restore SA1649
#pragma warning restore SA1201
