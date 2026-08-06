// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;
using SixLabors.ImageSharp.Drawing.Helpers;

namespace SixLabors.ImageSharp.Drawing.Processing;

/// <summary>
/// Represents one clip primitive carried by a drawing command.
/// </summary>
public sealed class DrawingClipDescriptor
{
    private static readonly Rectangle[] EmptyIntegerRectangles = [];
    private static readonly RectangleF[] EmptyRectangles = [];
    private static readonly IPath[] EmptyPaths = [];
    private readonly IReadOnlyList<Rectangle>? integerRectangles;
    private readonly IReadOnlyList<RectangleF>? rectangles;
    private readonly IReadOnlyList<IPath>? paths;

    // Lazily cached linearization of the path operands at PathScale. Safe to cache because
    // every mutation (Translate/Transform) produces a new descriptor instance.
    private LinearGeometry? pathGeometry;

    /// <summary>
    /// Initializes a new instance of the <see cref="DrawingClipDescriptor"/> class.
    /// </summary>
    /// <param name="kind">The clip primitive kind.</param>
    /// <param name="rectangle">The rectangle for rectangle clips.</param>
    /// <param name="integerRectangles">The rectangles for integer region clips.</param>
    /// <param name="rectangles">The rectangles for floating-point region clips.</param>
    /// <param name="paths">The path operands for path clips.</param>
    /// <param name="pathScale">The path-space scale seen by curve subdivision.</param>
    /// <param name="pathTransform">The residual transform mapping scaled path geometry into descriptor coordinates.</param>
    /// <param name="operation">The operation used to combine this descriptor with the existing clip.</param>
    /// <param name="intersectionRule">The fill rule used for path geometry.</param>
    /// <param name="edgeMode">The clip edge mode.</param>
    private DrawingClipDescriptor(
        DrawingClipKind kind,
        RectangleF rectangle,
        IReadOnlyList<Rectangle>? integerRectangles,
        IReadOnlyList<RectangleF>? rectangles,
        IReadOnlyList<IPath>? paths,
        Vector2 pathScale,
        Matrix4x4 pathTransform,
        ClipOperation operation,
        IntersectionRule intersectionRule,
        DrawingClipEdgeMode edgeMode)
    {
        this.Kind = kind;
        this.Rectangle = rectangle;
        this.integerRectangles = integerRectangles;
        this.rectangles = rectangles;
        this.paths = paths;
        this.PathScale = pathScale;
        this.PathTransform = pathTransform;
        this.Operation = operation;
        this.IntersectionRule = intersectionRule;
        this.EdgeMode = edgeMode;
    }

    /// <summary>
    /// Gets the clip primitive kind.
    /// </summary>
    public DrawingClipKind Kind { get; }

    /// <summary>
    /// Gets the operation used to combine this descriptor with the existing clip.
    /// </summary>
    public ClipOperation Operation { get; }

    /// <summary>
    /// Gets the fill rule used when this clip is represented as path geometry.
    /// </summary>
    public IntersectionRule IntersectionRule { get; }

    /// <summary>
    /// Gets the edge mode used by rectangle and region clips.
    /// </summary>
    public DrawingClipEdgeMode EdgeMode { get; }

    /// <summary>
    /// Gets the rectangle for a rectangle clip.
    /// </summary>
    public RectangleF Rectangle { get; }

    /// <summary>
    /// Gets the integer rectangles for an integer region clip.
    /// </summary>
    public IReadOnlyList<Rectangle> IntegerRectangles => this.integerRectangles ?? EmptyIntegerRectangles;

    /// <summary>
    /// Gets the floating-point rectangles for a region clip.
    /// </summary>
    public IReadOnlyList<RectangleF> Rectangles => this.rectangles ?? EmptyRectangles;

    /// <summary>
    /// Gets the path operands for a path clip.
    /// </summary>
    public IReadOnlyList<IPath> Paths => this.paths ?? EmptyPaths;

    /// <summary>
    /// Gets the path-space scale represented by this descriptor.
    /// </summary>
    public Vector2 PathScale { get; }

    /// <summary>
    /// Gets the transform that maps scaled path geometry into descriptor coordinates.
    /// </summary>
    public Matrix4x4 PathTransform { get; }

    /// <summary>
    /// Creates a rectangle clip descriptor.
    /// </summary>
    /// <param name="rectangle">The rectangle in canvas-local coordinates.</param>
    /// <param name="operation">The operation used to combine the rectangle with the existing clip.</param>
    /// <param name="edgeMode">The clip edge mode.</param>
    /// <returns>The descriptor.</returns>
    public static DrawingClipDescriptor CreateRectangle(
        RectangleF rectangle,
        ClipOperation operation,
        DrawingClipEdgeMode edgeMode)
        => new(
            DrawingClipKind.Rectangle,
            rectangle,
            null,
            null,
            null,
            Vector2.One,
            Matrix4x4.Identity,
            operation,
            IntersectionRule.NonZero,
            edgeMode);

    /// <summary>
    /// Creates an integer region clip descriptor.
    /// </summary>
    /// <param name="rectangles">The region rectangles in canvas-local coordinates.</param>
    /// <param name="operation">The operation used to combine the region with the existing clip.</param>
    /// <returns>The descriptor.</returns>
    public static DrawingClipDescriptor CreateIntegerRegion(
        IReadOnlyList<Rectangle> rectangles,
        ClipOperation operation)
        => new(
            DrawingClipKind.IntegerRegion,
            default,
            rectangles,
            null,
            null,
            Vector2.One,
            Matrix4x4.Identity,
            operation,
            IntersectionRule.NonZero,
            DrawingClipEdgeMode.Hard);

    /// <summary>
    /// Creates a floating-point region clip descriptor.
    /// </summary>
    /// <param name="rectangles">The region rectangles in canvas-local coordinates.</param>
    /// <param name="operation">The operation used to combine the region with the existing clip.</param>
    /// <param name="edgeMode">The clip edge mode.</param>
    /// <returns>The descriptor.</returns>
    public static DrawingClipDescriptor CreateRegion(
        IReadOnlyList<RectangleF> rectangles,
        ClipOperation operation,
        DrawingClipEdgeMode edgeMode)
        => new(
            DrawingClipKind.Region,
            default,
            null,
            rectangles,
            null,
            Vector2.One,
            Matrix4x4.Identity,
            operation,
            IntersectionRule.NonZero,
            edgeMode);

    /// <summary>
    /// Creates a path clip descriptor.
    /// </summary>
    /// <param name="paths">The path operands interpreted as one clip region.</param>
    /// <param name="operation">The operation used to combine the paths with the existing clip.</param>
    /// <param name="edgeMode">The clip edge mode.</param>
    /// <returns>The descriptor.</returns>
    public static DrawingClipDescriptor CreatePath(
        IReadOnlyList<IPath> paths,
        ClipOperation operation,
        DrawingClipEdgeMode edgeMode)
        => CreatePath(paths, Vector2.One, Matrix4x4.Identity, operation, IntersectionRule.NonZero, edgeMode);

    /// <summary>
    /// Creates a path descriptor from operands plus the transform split used by path lowering.
    /// </summary>
    /// <param name="paths">The path operands interpreted as one clip region.</param>
    /// <param name="scale">The path-space scale seen by curve subdivision.</param>
    /// <param name="transform">The residual transform mapping scaled path geometry into descriptor coordinates.</param>
    /// <param name="operation">The operation used to combine the paths with the existing clip.</param>
    /// <param name="intersectionRule">The fill rule used for the path geometry.</param>
    /// <param name="edgeMode">The clip edge mode.</param>
    /// <returns>The descriptor.</returns>
    private static DrawingClipDescriptor CreatePath(
        IReadOnlyList<IPath> paths,
        Vector2 scale,
        Matrix4x4 transform,
        ClipOperation operation,
        IntersectionRule intersectionRule,
        DrawingClipEdgeMode edgeMode)
        => new(
            DrawingClipKind.Path,
            default,
            null,
            null,
            paths,
            scale,
            transform,
            operation,
            intersectionRule,
            edgeMode);

    /// <summary>
    /// Converts one incoming path to a clip descriptor.
    /// </summary>
    /// <param name="path">The incoming path.</param>
    /// <param name="operation">The operation used to combine the path with the existing clip.</param>
    /// <param name="edgeMode">The clip edge mode.</param>
    /// <returns>The descriptor.</returns>
    public static DrawingClipDescriptor FromPath(
        IPath path,
        ClipOperation operation,
        DrawingClipEdgeMode edgeMode)
    {
        if (path is IRegionPath regionPath)
        {
            return CreateIntegerRegion(regionPath.Rectangles, operation);
        }

        if (TryGetRectangle(path, out RectangleF rectangle))
        {
            return CreateRectangle(rectangle, operation, edgeMode);
        }

        return CreatePath([path], operation, edgeMode);
    }

    /// <summary>
    /// Translates this descriptor.
    /// </summary>
    /// <param name="offset">The translation offset.</param>
    /// <returns>The translated descriptor.</returns>
    public DrawingClipDescriptor Translate(Vector2 offset)
    {
        if (offset == Vector2.Zero)
        {
            return this;
        }

        switch (this.Kind)
        {
            case DrawingClipKind.Rectangle:
            {
                RectangleF translated = this.Rectangle;
                translated.Offset(offset.X, offset.Y);
                return CreateRectangle(translated, this.Operation, this.EdgeMode);
            }

            case DrawingClipKind.IntegerRegion:
            {
                IReadOnlyList<Rectangle> source = this.IntegerRectangles;
                if (offset.X == MathF.Truncate(offset.X) && offset.Y == MathF.Truncate(offset.Y))
                {
                    Rectangle[] translated = new Rectangle[source.Count];
                    Point pointOffset = new((int)offset.X, (int)offset.Y);
                    for (int i = 0; i < translated.Length; i++)
                    {
                        translated[i] = source[i];
                        translated[i].Offset(pointOffset);
                    }

                    return CreateIntegerRegion(translated, this.Operation);
                }

                RectangleF[] translatedRegion = new RectangleF[source.Count];
                for (int i = 0; i < translatedRegion.Length; i++)
                {
                    Rectangle rectangle = source[i];
                    translatedRegion[i] = new RectangleF(rectangle.X + offset.X, rectangle.Y + offset.Y, rectangle.Width, rectangle.Height);
                }

                return CreateRegion(translatedRegion, this.Operation, DrawingClipEdgeMode.Hard);
            }

            case DrawingClipKind.Region:
            {
                IReadOnlyList<RectangleF> source = this.Rectangles;
                RectangleF[] translated = new RectangleF[source.Count];
                for (int i = 0; i < translated.Length; i++)
                {
                    translated[i] = source[i];
                    translated[i].Offset(offset.X, offset.Y);
                }

                return CreateRegion(translated, this.Operation, this.EdgeMode);
            }

            default:
            {
                // Keep path operands stable and move translation into the residual transform.
                // Rebuilding transformed paths here would discard the scale used for curve
                // subdivision and force the backends back into Vector2.One lowering.
                Matrix4x4 translated = this.GetPathMatrix() * Matrix4x4.CreateTranslation(offset.X, offset.Y, 0);
                Vector2 scale = MatrixUtilities.GetScale(translated);
                Matrix4x4 residual = MatrixUtilities.GetResidual(scale, translated);

                return CreatePath(this.Paths, scale, residual, this.Operation, this.IntersectionRule, this.EdgeMode);
            }
        }
    }

    /// <summary>
    /// Transforms this descriptor.
    /// </summary>
    /// <param name="matrix">The transform matrix.</param>
    /// <returns>The transformed descriptor.</returns>
    public DrawingClipDescriptor Transform(Matrix4x4 matrix)
    {
        if (matrix.IsIdentity)
        {
            return this;
        }

        if (MatrixUtilities.PreservesAxisAlignedRectangles(matrix))
        {
            if (this.Kind == DrawingClipKind.Rectangle)
            {
                return CreateRectangle(TransformRectangle(this.Rectangle, matrix), this.Operation, this.EdgeMode);
            }

            if (this.Kind == DrawingClipKind.IntegerRegion)
            {
                IReadOnlyList<Rectangle> source = this.IntegerRectangles;
                RectangleF[] transformed = new RectangleF[source.Count];
                for (int i = 0; i < transformed.Length; i++)
                {
                    Rectangle rectangle = source[i];
                    RectangleF regionRectangle = new(rectangle.X, rectangle.Y, rectangle.Width, rectangle.Height);

                    transformed[i] = TransformRectangle(regionRectangle, matrix);
                }

                SortRegionRectangles(transformed);
                return CreateRegion(transformed, this.Operation, DrawingClipEdgeMode.Hard);
            }

            if (this.Kind == DrawingClipKind.Region)
            {
                IReadOnlyList<RectangleF> source = this.Rectangles;
                RectangleF[] transformed = new RectangleF[source.Count];
                for (int i = 0; i < transformed.Length; i++)
                {
                    transformed[i] = TransformRectangle(source[i], matrix);
                }

                SortRegionRectangles(transformed);
                return CreateRegion(transformed, this.Operation, this.EdgeMode);
            }
        }

        // For arbitrary path clips, preserve the same scale/residual split used by fill and
        // stroke commands: curve subdivision sees the scale, while backends apply the residual.
        Matrix4x4 pathMatrix = this.Kind == DrawingClipKind.Path ? this.GetPathMatrix() * matrix : matrix;
        Vector2 scale = MatrixUtilities.GetScale(pathMatrix);
        Matrix4x4 residual = MatrixUtilities.GetResidual(scale, pathMatrix);
        IReadOnlyList<IPath> sourcePaths = this.Kind == DrawingClipKind.Path ? this.Paths : [this.ToPath()];

        return CreatePath(sourcePaths, scale, residual, this.Operation, this.IntersectionRule, this.EdgeMode);
    }

    /// <summary>
    /// Converts a path clip descriptor to linear geometry.
    /// </summary>
    /// <param name="transform">The transform that maps the returned geometry into descriptor coordinates.</param>
    /// <returns>The scaled linear geometry for the path clip.</returns>
    /// <exception cref="InvalidOperationException">Thrown when this descriptor is not a path clip.</exception>
    public LinearGeometry ToLinearGeometry(out Matrix4x4 transform)
    {
        if (this.Kind != DrawingClipKind.Path)
        {
            throw new InvalidOperationException("Only path clip descriptors can be converted to linear geometry.");
        }

        LinearGeometry? cached = this.pathGeometry;
        if (cached is not null)
        {
            transform = this.PathTransform;
            return cached;
        }

        transform = this.PathTransform;

        IReadOnlyList<IPath> source = this.Paths;
        IPath path = source.Count == 1 ? source[0] : new ComplexPolygon(source);
        LinearGeometry geometry = path.ToLinearGeometry(this.PathScale);
        this.pathGeometry = geometry;
        return geometry;
    }

    /// <summary>
    /// Gets conservative integer bounds for this descriptor in absolute target coordinates.
    /// </summary>
    /// <param name="destinationOffset">The absolute destination offset for the owning command.</param>
    /// <returns>The conservative bounds.</returns>
    public Rectangle GetConservativeBounds(Point destinationOffset)
    {
        RectangleF bounds = this.Kind switch
        {
            DrawingClipKind.Rectangle => this.Rectangle,
            DrawingClipKind.IntegerRegion => GetIntegerRegionBounds(this.IntegerRectangles),
            DrawingClipKind.Region => GetRegionBounds(this.Rectangles),
            _ => GetPathBounds(this.Paths, this.PathScale, this.PathTransform)
        };

        bounds.Offset(destinationOffset.X, destinationOffset.Y);
        return ImageSharp.Rectangle.FromLTRB(
            (int)MathF.Floor(bounds.Left),
            (int)MathF.Floor(bounds.Top),
            (int)MathF.Ceiling(bounds.Right),
            (int)MathF.Ceiling(bounds.Bottom));
    }

    /// <summary>
    /// Converts this descriptor to path geometry.
    /// </summary>
    /// <returns>A path describing the descriptor area.</returns>
    public IPath ToPath()
    {
        switch (this.Kind)
        {
            case DrawingClipKind.Rectangle:
                return new RectanglePolygon(this.Rectangle);

            case DrawingClipKind.IntegerRegion:
                return new Region(this.IntegerRectangles).ToPath();

            case DrawingClipKind.Region:
            {
                IReadOnlyList<RectangleF> source = this.Rectangles;
                IPath[] rectanglePaths = new IPath[source.Count];
                for (int i = 0; i < rectanglePaths.Length; i++)
                {
                    rectanglePaths[i] = new RectanglePolygon(source[i]);
                }

                return new ComplexPolygon(rectanglePaths);
            }

            default:
            {
                IReadOnlyList<IPath> source = this.Paths;
                IPath path = source.Count == 1 ? source[0] : new ComplexPolygon(source);
                Matrix4x4 transform = this.GetPathMatrix();

                return transform.IsIdentity ? path : path.Transform(transform);
            }
        }
    }

    /// <summary>
    /// Gets the full path transform represented by the stored scale and residual transform.
    /// </summary>
    /// <returns>The combined scale and residual transform.</returns>
    private Matrix4x4 GetPathMatrix()
        => Matrix4x4.CreateScale(this.PathScale.X, this.PathScale.Y, 1F) * this.PathTransform;

    /// <summary>
    /// Attempts to read a path as an axis-aligned rectangle.
    /// </summary>
    /// <param name="path">The path to inspect.</param>
    /// <param name="rectangle">The rectangle represented by the path.</param>
    /// <returns><see langword="true"/> when the path is a rectangle.</returns>
    private static bool TryGetRectangle(IPath path, out RectangleF rectangle)
    {
        if (path is RectanglePolygon rectanglePolygon)
        {
            rectangle = rectanglePolygon.Bounds;
            return true;
        }

        rectangle = default;
        return false;
    }

    /// <summary>
    /// Transforms a rectangle and returns the axis-aligned bounds of its transformed corners.
    /// </summary>
    /// <param name="rectangle">The rectangle to transform.</param>
    /// <param name="matrix">The transform matrix.</param>
    /// <returns>The axis-aligned bounds of the transformed rectangle.</returns>
    private static RectangleF TransformRectangle(RectangleF rectangle, Matrix4x4 matrix)
    {
        Vector2 p0 = Vector2.Transform(new Vector2(rectangle.Left, rectangle.Top), matrix);
        Vector2 p1 = Vector2.Transform(new Vector2(rectangle.Right, rectangle.Top), matrix);
        Vector2 p2 = Vector2.Transform(new Vector2(rectangle.Right, rectangle.Bottom), matrix);
        Vector2 p3 = Vector2.Transform(new Vector2(rectangle.Left, rectangle.Bottom), matrix);

        float left = MathF.Min(MathF.Min(p0.X, p1.X), MathF.Min(p2.X, p3.X));
        float top = MathF.Min(MathF.Min(p0.Y, p1.Y), MathF.Min(p2.Y, p3.Y));
        float right = MathF.Max(MathF.Max(p0.X, p1.X), MathF.Max(p2.X, p3.X));
        float bottom = MathF.Max(MathF.Max(p0.Y, p1.Y), MathF.Max(p2.Y, p3.Y));

        return RectangleF.FromLTRB(left, top, right, bottom);
    }

    /// <summary>
    /// Gets the union bounds of an integer region.
    /// </summary>
    /// <param name="rectangles">The region rectangles.</param>
    /// <returns>The union bounds.</returns>
    private static RectangleF GetIntegerRegionBounds(IReadOnlyList<Rectangle> rectangles)
    {
        Rectangle bounds = rectangles[0];
        for (int i = 1; i < rectangles.Count; i++)
        {
            bounds = ImageSharp.Rectangle.Union(bounds, rectangles[i]);
        }

        return bounds;
    }

    /// <summary>
    /// Gets the union bounds of a floating-point region.
    /// </summary>
    /// <param name="rectangles">The region rectangles.</param>
    /// <returns>The union bounds.</returns>
    private static RectangleF GetRegionBounds(IReadOnlyList<RectangleF> rectangles)
    {
        RectangleF bounds = rectangles[0];
        for (int i = 1; i < rectangles.Count; i++)
        {
            bounds = RectangleF.Union(bounds, rectangles[i]);
        }

        return bounds;
    }

    /// <summary>
    /// Gets the union bounds of the path operands in path space.
    /// </summary>
    /// <param name="paths">The path operands.</param>
    /// <returns>The union bounds.</returns>
    private static RectangleF GetPathBounds(IReadOnlyList<IPath> paths)
    {
        RectangleF bounds = paths[0].Bounds;
        for (int i = 1; i < paths.Count; i++)
        {
            bounds = RectangleF.Union(bounds, paths[i].Bounds);
        }

        return bounds;
    }

    /// <summary>
    /// Gets the union bounds of the path operands mapped through the scale and residual transform.
    /// </summary>
    /// <param name="paths">The path operands.</param>
    /// <param name="scale">The path-space scale.</param>
    /// <param name="transform">The residual transform mapping scaled geometry into descriptor coordinates.</param>
    /// <returns>The union bounds in descriptor coordinates.</returns>
    private static RectangleF GetPathBounds(IReadOnlyList<IPath> paths, Vector2 scale, Matrix4x4 transform)
    {
        RectangleF bounds = GetPathBounds(paths);
        RectangleF scaled = new(bounds.X * scale.X, bounds.Y * scale.Y, bounds.Width * scale.X, bounds.Height * scale.Y);

        return transform.IsIdentity ? scaled : RectangleF.Transform(scaled, transform);
    }

    /// <summary>
    /// Sorts region rectangles into the top-to-bottom, left-to-right order used by the
    /// canonical region model.
    /// </summary>
    /// <param name="rectangles">The rectangles to sort in place.</param>
    private static void SortRegionRectangles(RectangleF[] rectangles)
        => Array.Sort(
            rectangles,
            static (left, right) =>
            {
                int top = left.Top.CompareTo(right.Top);
                return top != 0 ? top : left.Left.CompareTo(right.Left);
            });
}
