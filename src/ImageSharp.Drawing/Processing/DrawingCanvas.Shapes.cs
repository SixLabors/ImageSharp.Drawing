// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Processing;

/// <content>
/// Convenience shape helpers that forward to the core <see cref="DrawingCanvas"/> primitives.
/// </content>
public abstract partial class DrawingCanvas
{
    /// <summary>
    /// Saves the current drawing state and begins an isolated compositing layer over the whole canvas.
    /// </summary>
    /// <returns>The save count after the layer state has been pushed.</returns>
    public int SaveLayer()
        => this.SaveLayer(new GraphicsOptions(), this.Bounds);

    /// <summary>
    /// Saves the current drawing state and begins an isolated compositing layer over the whole canvas.
    /// </summary>
    /// <param name="layerOptions">Graphics options controlling how the layer is composited on restore.</param>
    /// <returns>The save count after the layer state has been pushed.</returns>
    public int SaveLayer(GraphicsOptions layerOptions)
        => this.SaveLayer(layerOptions, this.Bounds);

    /// <summary>
    /// Fills the whole canvas using the given brush.
    /// </summary>
    /// <param name="brush">Brush used to shade destination pixels.</param>
    public void Fill(Brush brush)
    {
        Rectangle bounds = this.Bounds;

        this.Fill(brush, new RectanglePolygon(bounds));
    }

    /// <summary>
    /// Fills a local region using the given brush.
    /// </summary>
    /// <param name="brush">Brush used to shade destination pixels.</param>
    /// <param name="region">Region to fill in local coordinates.</param>
    public void Fill(Brush brush, Rectangle region)
        => this.Fill(brush, new RectanglePolygon(region));

    /// <summary>
    /// Clears the whole canvas using the given brush and clear-style composition options.
    /// </summary>
    /// <param name="brush">Brush used to shade destination pixels during clear.</param>
    public void Clear(Brush brush)
    {
        Rectangle bounds = this.Bounds;

        this.Clear(brush, new RectanglePolygon(bounds));
    }

    /// <summary>
    /// Clears a local region using the given brush and clear-style composition options.
    /// </summary>
    /// <param name="brush">Brush used to shade destination pixels during clear.</param>
    /// <param name="region">Region to clear in local coordinates.</param>
    public void Clear(Brush brush, Rectangle region)
        => this.Clear(brush, new RectanglePolygon(region));

    /// <summary>
    /// Fills all paths in a collection using the given brush.
    /// </summary>
    /// <param name="brush">Brush used to shade covered pixels.</param>
    /// <param name="paths">Path collection to fill.</param>
    public void Fill(Brush brush, IPathCollection paths)
    {
        Guard.NotNull(paths, nameof(paths));

        foreach (IPath path in paths)
        {
            this.Fill(brush, path);
        }
    }

    /// <summary>
    /// Fills a path built by the provided builder using the given brush.
    /// </summary>
    /// <param name="brush">Brush used to shade covered pixels.</param>
    /// <param name="pathBuilder">The path builder describing the fill region.</param>
    public void Fill(Brush brush, PathBuilder pathBuilder)
    {
        Guard.NotNull(pathBuilder, nameof(pathBuilder));

        this.Fill(brush, pathBuilder.Build());
    }

    /// <summary>
    /// Fills an ellipse using the provided brush.
    /// </summary>
    /// <param name="brush">Brush used to shade covered pixels.</param>
    /// <param name="center">Ellipse center point in local coordinates.</param>
    /// <param name="size">Ellipse width and height in local coordinates.</param>
    public void FillEllipse(Brush brush, PointF center, SizeF size)
        => this.Fill(brush, new EllipsePolygon(center, size));

    /// <summary>
    /// Fills the closed arc shape produced by joining the arc endpoints with a straight line.
    /// </summary>
    /// <param name="brush">Brush used to shade covered pixels.</param>
    /// <param name="center">Arc center point in local coordinates.</param>
    /// <param name="radius">Arc radii in local coordinates.</param>
    /// <param name="rotation">Ellipse rotation in degrees.</param>
    /// <param name="startAngle">Arc start angle in degrees.</param>
    /// <param name="sweepAngle">Arc sweep angle in degrees.</param>
    public void FillArc(Brush brush, PointF center, SizeF radius, float rotation, float startAngle, float sweepAngle)
        => this.Fill(brush, new Path(new ArcLineSegment(center, radius, rotation, startAngle, sweepAngle)));

    /// <summary>
    /// Fills a pie sector using the provided brush.
    /// </summary>
    /// <param name="brush">Brush used to shade covered pixels.</param>
    /// <param name="center">The center point of the pie sector in local coordinates.</param>
    /// <param name="radius">The x and y radii of the pie sector in local coordinates.</param>
    /// <param name="rotation">Ellipse rotation in degrees.</param>
    /// <param name="startAngle">The start angle of the pie sector in degrees.</param>
    /// <param name="sweepAngle">The sweep angle of the pie sector in degrees.</param>
    public void FillPie(Brush brush, PointF center, SizeF radius, float rotation, float startAngle, float sweepAngle)
        => this.Fill(brush, new PiePolygon(center, radius, rotation, startAngle, sweepAngle));

    /// <summary>
    /// Fills a pie sector using the provided brush.
    /// </summary>
    /// <param name="brush">Brush used to shade covered pixels.</param>
    /// <param name="center">The center point of the pie sector in local coordinates.</param>
    /// <param name="radius">The x and y radii of the pie sector in local coordinates.</param>
    /// <param name="startAngle">The start angle of the pie sector in degrees.</param>
    /// <param name="sweepAngle">The sweep angle of the pie sector in degrees.</param>
    public void FillPie(Brush brush, PointF center, SizeF radius, float startAngle, float sweepAngle)
        => this.Fill(brush, new PiePolygon(center, radius, startAngle, sweepAngle));

    /// <summary>
    /// Draws an arc outline using the provided pen.
    /// </summary>
    /// <param name="pen">Pen used to generate the arc outline.</param>
    /// <param name="center">Arc center point in local coordinates.</param>
    /// <param name="radius">Arc radii in local coordinates.</param>
    /// <param name="rotation">Ellipse rotation in degrees.</param>
    /// <param name="startAngle">Arc start angle in degrees.</param>
    /// <param name="sweepAngle">Arc sweep angle in degrees.</param>
    public void DrawArc(Pen pen, PointF center, SizeF radius, float rotation, float startAngle, float sweepAngle)
        => this.Draw(pen, new Path(new ArcLineSegment(center, radius, rotation, startAngle, sweepAngle)));

    /// <summary>
    /// Draws a cubic bezier outline using the provided pen.
    /// </summary>
    /// <param name="pen">Pen used to generate the bezier outline.</param>
    /// <param name="points">Bezier control points.</param>
    public void DrawBezier(Pen pen, params PointF[] points)
    {
        Guard.NotNull(points, nameof(points));

        this.Draw(pen, new Path(new CubicBezierLineSegment(points)));
    }

    /// <summary>
    /// Draws an ellipse outline using the provided pen.
    /// </summary>
    /// <param name="pen">Pen used to generate the ellipse outline.</param>
    /// <param name="center">Ellipse center point in local coordinates.</param>
    /// <param name="size">Ellipse width and height in local coordinates.</param>
    public void DrawEllipse(Pen pen, PointF center, SizeF size)
        => this.Draw(pen, new EllipsePolygon(center, size));

    /// <summary>
    /// Draws a pie sector outline using the provided pen.
    /// </summary>
    /// <param name="pen">Pen used to generate the pie outline.</param>
    /// <param name="center">The center point of the pie sector in local coordinates.</param>
    /// <param name="radius">The x and y radii of the pie sector in local coordinates.</param>
    /// <param name="rotation">Ellipse rotation in degrees.</param>
    /// <param name="startAngle">The start angle of the pie sector in degrees.</param>
    /// <param name="sweepAngle">The sweep angle of the pie sector in degrees.</param>
    public void DrawPie(Pen pen, PointF center, SizeF radius, float rotation, float startAngle, float sweepAngle)
        => this.Draw(pen, new PiePolygon(center, radius, rotation, startAngle, sweepAngle));

    /// <summary>
    /// Draws a pie sector outline using the provided pen.
    /// </summary>
    /// <param name="pen">Pen used to generate the pie outline.</param>
    /// <param name="center">The center point of the pie sector in local coordinates.</param>
    /// <param name="radius">The x and y radii of the pie sector in local coordinates.</param>
    /// <param name="startAngle">The start angle of the pie sector in degrees.</param>
    /// <param name="sweepAngle">The sweep angle of the pie sector in degrees.</param>
    public void DrawPie(Pen pen, PointF center, SizeF radius, float startAngle, float sweepAngle)
        => this.Draw(pen, new PiePolygon(center, radius, startAngle, sweepAngle));

    /// <summary>
    /// Draws a rectangular outline using the provided pen.
    /// </summary>
    /// <param name="pen">Pen used to generate the rectangle outline.</param>
    /// <param name="region">Rectangle region to stroke.</param>
    public void Draw(Pen pen, Rectangle region)
        => this.Draw(pen, new RectanglePolygon(region));

    /// <summary>
    /// Draws all paths in a collection using the provided pen.
    /// </summary>
    /// <param name="pen">Pen used to generate outlines.</param>
    /// <param name="paths">Path collection to stroke.</param>
    public void Draw(Pen pen, IPathCollection paths)
    {
        Guard.NotNull(paths, nameof(paths));

        foreach (IPath path in paths)
        {
            this.Draw(pen, path);
        }
    }

    /// <summary>
    /// Draws a path outline built by the provided builder using the given pen.
    /// </summary>
    /// <param name="pen">Pen used to generate the outline fill path.</param>
    /// <param name="pathBuilder">The path builder describing the path to stroke.</param>
    public void Draw(Pen pen, PathBuilder pathBuilder)
    {
        Guard.NotNull(pathBuilder, nameof(pathBuilder));

        this.Draw(pen, pathBuilder.Build());
    }
}
