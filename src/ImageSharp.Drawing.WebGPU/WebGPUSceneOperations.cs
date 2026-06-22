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
    /// Renders a scoped layer into a temporary target and composites it back into the parent target.
    /// </summary>
    ScopedLayer
}

/// <summary>
/// One ordered operation retained by a single WebGPU scene.
/// </summary>
internal sealed class WebGPUSceneOperation : IDisposable
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
    /// <param name="layer">The retained scoped-layer operation data.</param>
    public WebGPUSceneOperation(WebGPUScopedLayerSceneItem layer)
    {
        this.Kind = WebGPUSceneOperationKind.ScopedLayer;
        this.Layer = layer;
    }

    /// <summary>
    /// Gets the operation kind.
    /// </summary>
    public WebGPUSceneOperationKind Kind { get; }

    /// <summary>
    /// Gets the encoded draw range for a render operation.
    /// </summary>
    public WebGPUSceneRange Range { get; }

    /// <summary>
    /// Gets the retained Apply data for an Apply operation.
    /// </summary>
    public WebGPUApplySceneItem? Apply { get; }

    /// <summary>
    /// Gets the retained layer data for a scoped-layer operation.
    /// </summary>
    public WebGPUScopedLayerSceneItem? Layer { get; }

    /// <inheritdoc />
    public void Dispose()
    {
        this.Apply?.Dispose();
        this.Layer?.Dispose();
    }
}

/// <summary>
/// Describes a draw range inside the packed WebGPU scene streams.
/// </summary>
internal readonly struct WebGPUSceneRange
{
    /// <summary>
    /// Initializes a new instance of the <see cref="WebGPUSceneRange"/> struct.
    /// </summary>
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
        int totalPathRowCount)
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
}

/// <summary>
/// Retained WebGPU data for one Apply operation.
/// </summary>
internal sealed class WebGPUApplySceneItem : IDisposable
{
    /// <summary>
    /// Initializes a new instance of the <see cref="WebGPUApplySceneItem"/> class.
    /// </summary>
    /// <param name="operation">The ImageSharp processor operation.</param>
    /// <param name="sourceRect">The source rectangle copied from the current target.</param>
    /// <param name="drawRange">The encoded image-fill draw range inside the retained scene.</param>
    public WebGPUApplySceneItem(
        Action<IImageProcessingContext> operation,
        Rectangle sourceRect,
        WebGPUSceneRange drawRange)
    {
        this.Operation = operation;
        this.SourceRect = sourceRect;
        this.DrawRange = drawRange;
    }

    /// <summary>
    /// Gets the ImageSharp processor operation.
    /// </summary>
    public Action<IImageProcessingContext> Operation { get; }

    /// <summary>
    /// Gets the source rectangle copied from the current target.
    /// </summary>
    public Rectangle SourceRect { get; }

    /// <summary>
    /// Gets the encoded image-fill draw range inside the retained scene.
    /// </summary>
    public WebGPUSceneRange DrawRange { get; }

    /// <inheritdoc />
    public void Dispose()
    {
    }
}

#pragma warning restore SA1649
#pragma warning restore SA1201

/// <summary>
/// Retained WebGPU data for one layer that must be materialized because it contains Apply.
/// </summary>
internal sealed class WebGPUScopedLayerSceneItem : IDisposable
{
    /// <summary>
    /// Initializes a new instance of the <see cref="WebGPUScopedLayerSceneItem"/> class.
    /// </summary>
    /// <param name="bounds">The absolute layer bounds.</param>
    public WebGPUScopedLayerSceneItem(Rectangle bounds)
    {
        this.Bounds = bounds;
    }

    /// <summary>
    /// Gets the absolute layer bounds.
    /// </summary>
    public Rectangle Bounds { get; }

    /// <summary>
    /// Gets the number of operations rendered into the temporary layer target.
    /// </summary>
    public int OperationCount { get; private set; }

    /// <summary>
    /// Gets the encoded image-fill draw range that composites the layer texture into the parent target.
    /// </summary>
    public WebGPUSceneRange CompositeRange { get; private set; }

    /// <inheritdoc />
    public void Dispose()
    {
    }

    /// <summary>
    /// Completes the retained layer metadata after the child and composite ranges have been lowered.
    /// </summary>
    /// <param name="operationCount">The number of operations rendered into the temporary layer target.</param>
    /// <param name="compositeRange">The encoded image-fill draw range that composites the layer texture into the parent target.</param>
    public void SetComposite(int operationCount, WebGPUSceneRange compositeRange)
    {
        this.OperationCount = operationCount;
        this.CompositeRange = compositeRange;
    }
}
