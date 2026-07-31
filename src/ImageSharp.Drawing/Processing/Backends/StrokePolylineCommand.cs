// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// One explicit stroked open polyline command queued by the canvas batcher.
/// </summary>
/// <remarks>
/// Stroke commands carry pen geometry only; they do not carry clip state. Backends resolve
/// the active clip stack from the begin/end-clip commands surrounding each draw in the stream.
/// </remarks>
public readonly struct StrokePolylineCommand
{
    private readonly PointF[] sourcePoints;
    private readonly DrawingOptions drawingOptions;
    private readonly DrawingCanvasLayer? ownerLayer;

    /// <summary>
    /// Initializes a new instance of the <see cref="StrokePolylineCommand"/> struct.
    /// </summary>
    /// <param name="sourcePoints">The source polyline points.</param>
    /// <param name="brush">The brush used to shade the stroke.</param>
    /// <param name="drawingOptions">The drawing options (graphics, shape, transform) used during composition.</param>
    /// <param name="rasterizerOptions">The rasterizer options used to generate coverage.</param>
    /// <param name="targetBounds">The absolute bounds of the logical target.</param>
    /// <param name="destinationOffset">The absolute destination offset of the command.</param>
    /// <param name="pen">The stroke metadata.</param>
    /// <param name="isInsideLayer">True if the command was recorded inside a layer.</param>
    public StrokePolylineCommand(
        PointF[] sourcePoints,
        Brush brush,
        DrawingOptions drawingOptions,
        in RasterizerOptions rasterizerOptions,
        Rectangle targetBounds,
        Point destinationOffset,
        Pen pen,
        bool isInsideLayer)
        : this(
            sourcePoints,
            brush,
            drawingOptions,
            in rasterizerOptions,
            targetBounds,
            destinationOffset,
            pen,
            isInsideLayer,
            null)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="StrokePolylineCommand"/> struct with the owning layer state recorded by the canvas.
    /// </summary>
    /// <param name="sourcePoints">The source polyline points.</param>
    /// <param name="brush">The brush used to shade the stroke.</param>
    /// <param name="drawingOptions">The drawing options (graphics, shape, transform) used during composition.</param>
    /// <param name="rasterizerOptions">The rasterizer options used to generate coverage.</param>
    /// <param name="targetBounds">The absolute bounds of the logical target.</param>
    /// <param name="destinationOffset">The absolute destination offset of the command.</param>
    /// <param name="pen">The stroke metadata.</param>
    /// <param name="isInsideLayer">True if the command was recorded inside a layer.</param>
    /// <param name="ownerLayer">The layer that owned this command when it was recorded.</param>
    internal StrokePolylineCommand(
        PointF[] sourcePoints,
        Brush brush,
        DrawingOptions drawingOptions,
        in RasterizerOptions rasterizerOptions,
        Rectangle targetBounds,
        Point destinationOffset,
        Pen pen,
        bool isInsideLayer,
        DrawingCanvasLayer? ownerLayer)
    {
        ArgumentNullException.ThrowIfNull(sourcePoints);
        if (sourcePoints.Length < 2)
        {
            throw new ArgumentOutOfRangeException(nameof(sourcePoints), "Open stroke polylines require at least two points.");
        }

        this.sourcePoints = sourcePoints;
        this.drawingOptions = drawingOptions;
        this.ownerLayer = ownerLayer;
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
    /// Gets the source polyline points.
    /// </summary>
    public PointF[] SourcePoints => this.sourcePoints;

    /// <summary>
    /// Gets the command transform.
    /// </summary>
    public Matrix4x4 Transform => this.drawingOptions.Transform;

    /// <summary>
    /// Gets a value indicating whether the command was recorded inside a layer.
    /// </summary>
    public bool IsInsideLayer { get; }

    /// <summary>
    /// Gets the layer state for the layer that owned this command when it was recorded.
    /// </summary>
    internal DrawingCanvasLayer? OwnerLayer => this.ownerLayer;

    /// <summary>
    /// Computes the conservative stroked bounds of one open polyline.
    /// </summary>
    /// <param name="points">The polyline points.</param>
    /// <param name="pen">The stroke metadata.</param>
    /// <returns>The conservative stroked bounds.</returns>
    public static RectangleF GetConservativeBounds(PointF[] points, Pen pen)
    {
        ArgumentNullException.ThrowIfNull(points);
        if (points.Length == 0)
        {
            return RectangleF.Empty;
        }

        float minX = points[0].X;
        float minY = points[0].Y;
        float maxX = minX;
        float maxY = minY;

        for (int i = 1; i < points.Length; i++)
        {
            PointF point = points[i];
            minX = MathF.Min(minX, point.X);
            minY = MathF.Min(minY, point.Y);
            maxX = MathF.Max(maxX, point.X);
            maxY = MathF.Max(maxY, point.Y);
        }

        RectangleF bounds = RectangleF.FromLTRB(minX, minY, maxX, maxY);
        return InflateBounds(bounds, pen);
    }

    /// <summary>
    /// Inflates point bounds by the maximum distance the stroke outline can extend past the centerline.
    /// </summary>
    /// <param name="bounds">The tight bounds of the source points.</param>
    /// <param name="pen">The stroke metadata.</param>
    /// <returns>The inflated bounds.</returns>
    private static RectangleF InflateBounds(RectangleF bounds, Pen pen)
    {
        float halfWidth = pen.StrokeWidth * 0.5F;

        // Miter joins can extend up to halfWidth * miterLimit beyond the centerline; the limit is
        // clamped to at least 1 so the inflation never falls below the plain half stroke width.
        float inflate = pen.StrokeOptions.LineJoin switch
        {
            LineJoin.Miter or LineJoin.MiterRevert or LineJoin.MiterRound => (float)(halfWidth * Math.Max(pen.StrokeOptions.MiterLimit, 1D)),
            _ => halfWidth
        };

        bounds.Inflate(new SizeF(inflate, inflate));
        return bounds;
    }
}
