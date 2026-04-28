// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// One explicit stroked two-point line-segment command queued by the canvas batcher.
/// </summary>
public readonly struct StrokeLineSegmentCommand
{
    private readonly PointF sourceStart;
    private readonly PointF sourceEnd;
    private readonly DrawingOptions drawingOptions;

    /// <summary>
    /// Initializes a new instance of the <see cref="StrokeLineSegmentCommand"/> struct.
    /// </summary>
    /// <param name="sourceStart">The source line start point.</param>
    /// <param name="sourceEnd">The source line end point.</param>
    /// <param name="brush">The brush used to shade the stroke.</param>
    /// <param name="drawingOptions">The drawing options (graphics, shape, transform) used during composition.</param>
    /// <param name="rasterizerOptions">The rasterizer options used to generate coverage.</param>
    /// <param name="targetBounds">The absolute bounds of the logical target.</param>
    /// <param name="destinationOffset">The absolute destination offset of the command.</param>
    /// <param name="pen">The stroke metadata.</param>
    /// <param name="isInsideLayer">True if the command was recorded inside a layer.</param>
    public StrokeLineSegmentCommand(
        PointF sourceStart,
        PointF sourceEnd,
        Brush brush,
        DrawingOptions drawingOptions,
        in RasterizerOptions rasterizerOptions,
        Rectangle targetBounds,
        Point destinationOffset,
        Pen pen,
        bool isInsideLayer)
    {
        this.sourceStart = sourceStart;
        this.sourceEnd = sourceEnd;
        this.drawingOptions = drawingOptions;
        this.Brush = brush;
        this.RasterizerOptions = rasterizerOptions;
        this.TargetBounds = targetBounds;
        this.DestinationOffset = destinationOffset;
        this.Pen = pen;
        this.IsInsideLayer = isInsideLayer;
    }

    /// <summary>
    /// Gets the brush used during composition.
    /// </summary>
    public Brush Brush { get; }

    /// <summary>
    /// Gets the drawing options carried by the command.
    /// </summary>
    public DrawingOptions DrawingOptions => this.drawingOptions;

    /// <summary>
    /// Gets the graphics options used during composition.
    /// </summary>
    public GraphicsOptions GraphicsOptions => this.drawingOptions.GraphicsOptions;

    /// <summary>
    /// Gets the rasterizer options used to generate coverage.
    /// </summary>
    public RasterizerOptions RasterizerOptions { get; }

    /// <summary>
    /// Gets the absolute bounds of the logical target for this command.
    /// </summary>
    public Rectangle TargetBounds { get; }

    /// <summary>
    /// Gets the absolute destination offset where the local coverage should be composited.
    /// </summary>
    public Point DestinationOffset { get; }

    /// <summary>
    /// Gets the stroke metadata for this command.
    /// </summary>
    public Pen Pen { get; }

    /// <summary>
    /// Gets the source line start point.
    /// </summary>
    public PointF SourceStart => this.sourceStart;

    /// <summary>
    /// Gets the source line end point.
    /// </summary>
    public PointF SourceEnd => this.sourceEnd;

    /// <summary>
    /// Gets the command transform.
    /// </summary>
    public Matrix4x4 Transform => this.drawingOptions.Transform;

    /// <summary>
    /// Gets a value indicating whether the command was recorded inside a layer.
    /// </summary>
    public bool IsInsideLayer { get; }

    /// <summary>
    /// Computes the conservative stroked bounds of one two-point line segment.
    /// </summary>
    /// <param name="start">The line start point.</param>
    /// <param name="end">The line end point.</param>
    /// <param name="pen">The stroke metadata.</param>
    /// <returns>The conservative stroked bounds.</returns>
    public static RectangleF GetConservativeBounds(PointF start, PointF end, Pen pen)
    {
        float left = MathF.Min(start.X, end.X);
        float top = MathF.Min(start.Y, end.Y);
        float right = MathF.Max(start.X, end.X);
        float bottom = MathF.Max(start.Y, end.Y);
        RectangleF bounds = RectangleF.FromLTRB(left, top, right, bottom);
        return InflateBounds(bounds, pen);
    }

    private static RectangleF InflateBounds(RectangleF bounds, Pen pen)
    {
        float halfWidth = pen.StrokeWidth * 0.5F;
        float inflate = pen.StrokeOptions.LineJoin switch
        {
            LineJoin.Miter or LineJoin.MiterRevert or LineJoin.MiterRound => (float)(halfWidth * Math.Max(pen.StrokeOptions.MiterLimit, 1D)),
            _ => halfWidth
        };

        bounds.Inflate(new SizeF(inflate, inflate));
        return bounds;
    }
}
