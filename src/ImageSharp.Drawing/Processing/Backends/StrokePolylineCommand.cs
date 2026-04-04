// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// One explicit stroked open polyline command queued by the canvas batcher.
/// </summary>
public readonly struct StrokePolylineCommand
{
    private readonly PointF[] sourcePoints;
    private readonly Matrix4x4 transform;

    /// <summary>
    /// Initializes a new instance of the <see cref="StrokePolylineCommand"/> struct.
    /// </summary>
    /// <param name="sourcePoints">The source polyline points.</param>
    /// <param name="brush">The brush used to shade the stroke.</param>
    /// <param name="graphicsOptions">The graphics options used during composition.</param>
    /// <param name="rasterizerOptions">The rasterizer options used to generate coverage.</param>
    /// <param name="targetBounds">The absolute bounds of the logical target.</param>
    /// <param name="destinationOffset">The absolute destination offset of the command.</param>
    /// <param name="pen">The stroke metadata.</param>
    /// <param name="transform">The transform applied during preparation.</param>
    public StrokePolylineCommand(
        PointF[] sourcePoints,
        Brush brush,
        GraphicsOptions graphicsOptions,
        in RasterizerOptions rasterizerOptions,
        Rectangle targetBounds,
        Point destinationOffset,
        Pen pen,
        Matrix4x4 transform)
    {
        ArgumentNullException.ThrowIfNull(sourcePoints);
        if (sourcePoints.Length < 2)
        {
            throw new ArgumentOutOfRangeException(nameof(sourcePoints), "Open stroke polylines require at least two points.");
        }

        this.sourcePoints = sourcePoints;
        this.transform = transform;
        this.Brush = brush;
        this.GraphicsOptions = graphicsOptions;
        this.RasterizerOptions = rasterizerOptions;
        this.TargetBounds = targetBounds;
        this.DestinationOffset = destinationOffset;
        this.Pen = pen;
    }

    /// <summary>
    /// Gets the brush used during composition.
    /// </summary>
    public Brush Brush { get; }

    /// <summary>
    /// Gets the graphics options used during composition.
    /// </summary>
    public GraphicsOptions GraphicsOptions { get; }

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
    public Matrix4x4 Transform => this.transform;

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
