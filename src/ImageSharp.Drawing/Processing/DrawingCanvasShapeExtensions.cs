// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Processing;

/// <summary>
/// Convenience canvas helpers that forward to the core <see cref="IDrawingCanvas"/> primitives.
/// </summary>
public static class DrawingCanvasShapeExtensions
{
    /// <summary>
    /// Saves the current drawing state and begins an isolated compositing layer over the whole canvas.
    /// </summary>
    /// <param name="canvas">The destination canvas.</param>
    /// <returns>The save count after the layer state has been pushed.</returns>
    public static int SaveLayer(this IDrawingCanvas canvas)
        => canvas.SaveLayer(new GraphicsOptions(), canvas.Bounds);

    /// <summary>
    /// Saves the current drawing state and begins an isolated compositing layer over the whole canvas.
    /// </summary>
    /// <param name="canvas">The destination canvas.</param>
    /// <param name="layerOptions">Graphics options controlling how the layer is composited on restore.</param>
    /// <returns>The save count after the layer state has been pushed.</returns>
    public static int SaveLayer(this IDrawingCanvas canvas, GraphicsOptions layerOptions)
        => canvas.SaveLayer(layerOptions, canvas.Bounds);

    /// <summary>
    /// Fills the whole canvas using the given brush.
    /// </summary>
    /// <param name="canvas">The destination canvas.</param>
    /// <param name="brush">Brush used to shade destination pixels.</param>
    public static void Fill(this IDrawingCanvas canvas, Brush brush)
    {
        Rectangle bounds = canvas.Bounds;
        canvas.Fill(brush, new RectangularPolygon(bounds));
    }

    /// <summary>
    /// Fills a local region using the given brush.
    /// </summary>
    /// <param name="canvas">The destination canvas.</param>
    /// <param name="brush">Brush used to shade destination pixels.</param>
    /// <param name="region">Region to fill in local coordinates.</param>
    public static void Fill(this IDrawingCanvas canvas, Brush brush, Rectangle region)
        => canvas.Fill(brush, new RectangularPolygon(region));

    /// <summary>
    /// Clears the whole canvas using the given brush and clear-style composition options.
    /// </summary>
    /// <param name="canvas">The destination canvas.</param>
    /// <param name="brush">Brush used to shade destination pixels during clear.</param>
    public static void Clear(this IDrawingCanvas canvas, Brush brush)
    {
        Rectangle bounds = canvas.Bounds;
        canvas.Clear(brush, new RectangularPolygon(bounds));
    }

    /// <summary>
    /// Clears a local region using the given brush and clear-style composition options.
    /// </summary>
    /// <param name="canvas">The destination canvas.</param>
    /// <param name="brush">Brush used to shade destination pixels during clear.</param>
    /// <param name="region">Region to clear in local coordinates.</param>
    public static void Clear(this IDrawingCanvas canvas, Brush brush, Rectangle region)
        => canvas.Clear(brush, new RectangularPolygon(region));

    /// <summary>
    /// Fills all paths in a collection using the given brush.
    /// </summary>
    /// <param name="canvas">The destination canvas.</param>
    /// <param name="brush">Brush used to shade covered pixels.</param>
    /// <param name="paths">Path collection to fill.</param>
    public static void Fill(this IDrawingCanvas canvas, Brush brush, IPathCollection paths)
    {
        Guard.NotNull(paths, nameof(paths));

        foreach (IPath path in paths)
        {
            canvas.Fill(brush, path);
        }
    }

    /// <summary>
    /// Fills a path built by the provided builder using the given brush.
    /// </summary>
    /// <param name="canvas">The destination canvas.</param>
    /// <param name="brush">Brush used to shade covered pixels.</param>
    /// <param name="pathBuilder">The path builder describing the fill region.</param>
    public static void Fill(this IDrawingCanvas canvas, Brush brush, PathBuilder pathBuilder)
    {
        Guard.NotNull(pathBuilder, nameof(pathBuilder));
        canvas.Fill(brush, pathBuilder.Build());
    }

    /// <summary>
    /// Fills an ellipse using the provided brush.
    /// </summary>
    /// <param name="canvas">The destination canvas.</param>
    /// <param name="brush">Brush used to shade covered pixels.</param>
    /// <param name="center">Ellipse center point in local coordinates.</param>
    /// <param name="size">Ellipse width and height in local coordinates.</param>
    public static void FillEllipse(this IDrawingCanvas canvas, Brush brush, PointF center, SizeF size)
        => canvas.Fill(brush, new EllipsePolygon(center, size));

    /// <summary>
    /// Fills the closed arc shape produced by joining the arc endpoints with a straight line.
    /// </summary>
    /// <param name="canvas">The destination canvas.</param>
    /// <param name="brush">Brush used to shade covered pixels.</param>
    /// <param name="center">Arc center point in local coordinates.</param>
    /// <param name="radius">Arc radii in local coordinates.</param>
    /// <param name="rotation">Ellipse rotation in degrees.</param>
    /// <param name="startAngle">Arc start angle in degrees.</param>
    /// <param name="sweepAngle">Arc sweep angle in degrees.</param>
    public static void FillArc(this IDrawingCanvas canvas, Brush brush, PointF center, SizeF radius, float rotation, float startAngle, float sweepAngle)
        => canvas.Fill(brush, new Path(new ArcLineSegment(center, radius, rotation, startAngle, sweepAngle)));

    /// <summary>
    /// Fills a pie sector using the provided brush.
    /// </summary>
    /// <param name="canvas">The destination canvas.</param>
    /// <param name="brush">Brush used to shade covered pixels.</param>
    /// <param name="center">Pie center point in local coordinates.</param>
    /// <param name="radius">Pie radii in local coordinates.</param>
    /// <param name="rotation">Ellipse rotation in degrees.</param>
    /// <param name="startAngle">Pie start angle in degrees.</param>
    /// <param name="sweepAngle">Pie sweep angle in degrees.</param>
    public static void FillPie(this IDrawingCanvas canvas, Brush brush, PointF center, SizeF radius, float rotation, float startAngle, float sweepAngle)
        => canvas.Fill(brush, new Pie(center, radius, rotation, startAngle, sweepAngle));

    /// <summary>
    /// Fills a pie sector using the provided brush.
    /// </summary>
    /// <param name="canvas">The destination canvas.</param>
    /// <param name="brush">Brush used to shade covered pixels.</param>
    /// <param name="center">Pie center point in local coordinates.</param>
    /// <param name="radius">Pie radii in local coordinates.</param>
    /// <param name="startAngle">Pie start angle in degrees.</param>
    /// <param name="sweepAngle">Pie sweep angle in degrees.</param>
    public static void FillPie(this IDrawingCanvas canvas, Brush brush, PointF center, SizeF radius, float startAngle, float sweepAngle)
        => canvas.Fill(brush, new Pie(center, radius, startAngle, sweepAngle));

    /// <summary>
    /// Draws an arc outline using the provided pen.
    /// </summary>
    /// <param name="canvas">The destination canvas.</param>
    /// <param name="pen">Pen used to generate the arc outline.</param>
    /// <param name="center">Arc center point in local coordinates.</param>
    /// <param name="radius">Arc radii in local coordinates.</param>
    /// <param name="rotation">Ellipse rotation in degrees.</param>
    /// <param name="startAngle">Arc start angle in degrees.</param>
    /// <param name="sweepAngle">Arc sweep angle in degrees.</param>
    public static void DrawArc(this IDrawingCanvas canvas, Pen pen, PointF center, SizeF radius, float rotation, float startAngle, float sweepAngle)
        => canvas.Draw(pen, new Path(new ArcLineSegment(center, radius, rotation, startAngle, sweepAngle)));

    /// <summary>
    /// Draws a cubic bezier outline using the provided pen.
    /// </summary>
    /// <param name="canvas">The destination canvas.</param>
    /// <param name="pen">Pen used to generate the bezier outline.</param>
    /// <param name="points">Bezier control points.</param>
    public static void DrawBezier(this IDrawingCanvas canvas, Pen pen, params PointF[] points)
    {
        Guard.NotNull(points, nameof(points));
        canvas.Draw(pen, new Path(new CubicBezierLineSegment(points)));
    }

    /// <summary>
    /// Draws an ellipse outline using the provided pen.
    /// </summary>
    /// <param name="canvas">The destination canvas.</param>
    /// <param name="pen">Pen used to generate the ellipse outline.</param>
    /// <param name="center">Ellipse center point in local coordinates.</param>
    /// <param name="size">Ellipse width and height in local coordinates.</param>
    public static void DrawEllipse(this IDrawingCanvas canvas, Pen pen, PointF center, SizeF size)
        => canvas.Draw(pen, new EllipsePolygon(center, size));

    /// <summary>
    /// Draws a pie sector outline using the provided pen.
    /// </summary>
    /// <param name="canvas">The destination canvas.</param>
    /// <param name="pen">Pen used to generate the pie outline.</param>
    /// <param name="center">Pie center point in local coordinates.</param>
    /// <param name="radius">Pie radii in local coordinates.</param>
    /// <param name="rotation">Ellipse rotation in degrees.</param>
    /// <param name="startAngle">Pie start angle in degrees.</param>
    /// <param name="sweepAngle">Pie sweep angle in degrees.</param>
    public static void DrawPie(this IDrawingCanvas canvas, Pen pen, PointF center, SizeF radius, float rotation, float startAngle, float sweepAngle)
        => canvas.Draw(pen, new Pie(center, radius, rotation, startAngle, sweepAngle));

    /// <summary>
    /// Draws a pie sector outline using the provided pen.
    /// </summary>
    /// <param name="canvas">The destination canvas.</param>
    /// <param name="pen">Pen used to generate the pie outline.</param>
    /// <param name="center">Pie center point in local coordinates.</param>
    /// <param name="radius">Pie radii in local coordinates.</param>
    /// <param name="startAngle">Pie start angle in degrees.</param>
    /// <param name="sweepAngle">Pie sweep angle in degrees.</param>
    public static void DrawPie(this IDrawingCanvas canvas, Pen pen, PointF center, SizeF radius, float startAngle, float sweepAngle)
        => canvas.Draw(pen, new Pie(center, radius, startAngle, sweepAngle));

    /// <summary>
    /// Draws a rectangular outline using the provided pen.
    /// </summary>
    /// <param name="canvas">The destination canvas.</param>
    /// <param name="pen">Pen used to generate the rectangle outline.</param>
    /// <param name="region">Rectangle region to stroke.</param>
    public static void Draw(this IDrawingCanvas canvas, Pen pen, Rectangle region)
        => canvas.Draw(pen, new RectangularPolygon(region));

    /// <summary>
    /// Draws all paths in a collection using the provided pen.
    /// </summary>
    /// <param name="canvas">The destination canvas.</param>
    /// <param name="pen">Pen used to generate outlines.</param>
    /// <param name="paths">Path collection to stroke.</param>
    public static void Draw(this IDrawingCanvas canvas, Pen pen, IPathCollection paths)
    {
        Guard.NotNull(paths, nameof(paths));

        foreach (IPath path in paths)
        {
            canvas.Draw(pen, path);
        }
    }

    /// <summary>
    /// Draws a path outline built by the provided builder using the given pen.
    /// </summary>
    /// <param name="canvas">The destination canvas.</param>
    /// <param name="pen">Pen used to generate the outline fill path.</param>
    /// <param name="pathBuilder">The path builder describing the path to stroke.</param>
    public static void Draw(this IDrawingCanvas canvas, Pen pen, PathBuilder pathBuilder)
    {
        Guard.NotNull(pathBuilder, nameof(pathBuilder));
        canvas.Draw(pen, pathBuilder.Build());
    }
}
