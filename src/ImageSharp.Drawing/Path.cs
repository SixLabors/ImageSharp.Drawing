// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Numerics;

namespace SixLabors.ImageSharp.Drawing;

/// <summary>
/// A aggregate of <see cref="ILineSegment"/>s making a single logical path.
/// </summary>
/// <seealso cref="IPath" />
public class Path : IPath, ISimplePath, IPathInternals, IInternalPathOwner
{
    private readonly ILineSegment[] lineSegments;
    private InternalPath? innerPath;
    private IReadOnlyList<InternalPath>? internalPathRings;
    private IPath? closedPath;
    private LinearGeometryCache geometryCache;
    private RectangleF? bounds;

    /// <summary>
    /// Initializes a new instance of the <see cref="Path"/> class.
    /// </summary>
    /// <param name="points">The collection of points; processed as a series of linear line segments.</param>
    public Path(PointF[] points)
        : this(new LinearLineSegment(points))
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Path"/> class.
    /// </summary>
    /// <param name="segments">The segments.</param>
    public Path(IEnumerable<ILineSegment> segments)
        : this(GetSegmentArray(segments))
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Path" /> class.
    /// </summary>
    /// <param name="path">The path.</param>
    public Path(Path path)
        : this(path.LineSegments)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Path"/> class.
    /// </summary>
    /// <param name="segments">The segments.</param>
    public Path(params ILineSegment[] segments)
    {
        ArgumentNullException.ThrowIfNull(segments);
        this.lineSegments = segments;
    }

    /// <summary>
    /// Gets the default empty path.
    /// </summary>
    public static IPath Empty { get; } = EmptyPath.OpenPath;

    /// <inheritdoc/>
    bool ISimplePath.IsClosed => this.IsClosed;

    /// <inheritdoc cref="ISimplePath.IsClosed"/>
    public virtual bool IsClosed => false;

    /// <inheritdoc/>
    public ReadOnlyMemory<PointF> Points => this.InnerPath.Points();

    /// <inheritdoc />
    public RectangleF Bounds => this.bounds ??= this.CalculateBounds();

    /// <inheritdoc />
    public PathTypes PathType => this.IsClosed ? PathTypes.Closed : PathTypes.Open;

    /// <summary>
    /// Gets the maximum number intersections that a shape can have when testing a line.
    /// </summary>
    internal int MaxIntersections => this.InnerPath.PointCount;

    /// <summary>
    /// Gets readonly collection of line segments.
    /// </summary>
    public IReadOnlyList<ILineSegment> LineSegments => this.lineSegments;

    /// <summary>
    /// Gets or sets a value indicating whether close or collinear vertices should be removed. TEST ONLY!
    /// </summary>
    internal bool RemoveCloseAndCollinearPoints { get; set; } = true;

    private protected InternalPath InnerPath =>
        this.innerPath ??= new InternalPath(this.lineSegments, this.IsClosed, this.RemoveCloseAndCollinearPoints);

    /// <inheritdoc />
    public virtual IPath Transform(Matrix4x4 matrix)
    {
        if (matrix.IsIdentity)
        {
            return this;
        }

        ILineSegment[] segments = new ILineSegment[this.lineSegments.Length];

        for (int i = 0; i < segments.Length; i++)
        {
            segments[i] = this.lineSegments[i].Transform(matrix);
        }

        return new Path(segments);
    }

    /// <inheritdoc />
    public IPath AsClosedPath()
    {
        if (this.IsClosed)
        {
            return this;
        }

        return this.closedPath ??= new Polygon(this.LineSegments);
    }

    /// <inheritdoc />
    public IEnumerable<ISimplePath> Flatten()
    {
        yield return this;
    }

    /// <inheritdoc/>
    public virtual LinearGeometry ToLinearGeometry(Vector2 scale)
        => this.geometryCache.TryGet(scale, out LinearGeometry? hit)
            ? hit
            : this.geometryCache.Store(scale, this.BuildLinearGeometry(scale));

    private LinearGeometry BuildLinearGeometry(Vector2 scale)
    {
        if (this.lineSegments.Length == 0)
        {
            return new LinearGeometry(
                new LinearGeometryInfo
                {
                    Bounds = RectangleF.Empty,
                    ContourCount = 0,
                    PointCount = 0,
                    SegmentCount = 0,
                    NonHorizontalSegmentCountPixelBoundary = 0,
                    NonHorizontalSegmentCountPixelCenter = 0
                },
                [],
                []);
        }

        PointF? lastEndPoint = null;
        int pointCount = 0;

        for (int i = 0; i < this.lineSegments.Length; i++)
        {
            ILineSegment segment = this.lineSegments[i];
            bool skipFirstPoint = lastEndPoint?.Equals(segment.StartPoint) == true;
            pointCount += segment.LinearVertexCount(scale) - (skipFirstPoint ? 1 : 0);
            lastEndPoint = segment.EndPoint;
        }

        PointF[] points = new PointF[pointCount];
        LinearContour[] contours = pointCount == 0 ? [] : new LinearContour[1];

        bool hasBounds = false;
        float minX = float.MaxValue;
        float minY = float.MaxValue;
        float maxX = float.MinValue;
        float maxY = float.MinValue;
        int nonHorizontalSegmentCountPixelBoundary = 0;
        int nonHorizontalSegmentCountPixelCenter = 0;
        int pointIndex = 0;
        lastEndPoint = null;

        for (int i = 0; i < this.lineSegments.Length; i++)
        {
            ILineSegment segment = this.lineSegments[i];
            bool skipFirstPoint = lastEndPoint?.Equals(segment.StartPoint) == true;
            int contributionCount = segment.LinearVertexCount(scale) - (skipFirstPoint ? 1 : 0);
            Span<PointF> destination = points.AsSpan(pointIndex, contributionCount);

            segment.CopyTo(destination, skipFirstPoint, scale);
            lastEndPoint = segment.EndPoint;

            for (int p = 0; p < destination.Length; p++)
            {
                PointF point = destination[p];
                minX = MathF.Min(minX, point.X);
                minY = MathF.Min(minY, point.Y);
                maxX = MathF.Max(maxX, point.X);
                maxY = MathF.Max(maxY, point.Y);
                hasBounds = true;
            }

            pointIndex += contributionCount;
        }

        int segmentCount = pointCount == 0 ? 0 : this.IsClosed ? pointCount : pointCount - 1;
        CountNonHorizontalSegments(points, pointCount, this.IsClosed, ref nonHorizontalSegmentCountPixelBoundary, ref nonHorizontalSegmentCountPixelCenter);

        if (pointCount > 0)
        {
            contours[0] = new LinearContour
            {
                PointStart = 0,
                PointCount = pointCount,
                SegmentStart = 0,
                SegmentCount = segmentCount,
                IsClosed = this.IsClosed
            };
        }

        RectangleF bounds = hasBounds ? RectangleF.FromLTRB(minX, minY, maxX, maxY) : RectangleF.Empty;

        return new LinearGeometry(
            new LinearGeometryInfo
            {
                Bounds = bounds,
                ContourCount = contours.Length,
                PointCount = points.Length,
                SegmentCount = segmentCount,
                NonHorizontalSegmentCountPixelBoundary = nonHorizontalSegmentCountPixelBoundary,
                NonHorizontalSegmentCountPixelCenter = nonHorizontalSegmentCountPixelCenter
            },
            contours,
            points);
    }

    /// <inheritdoc/>
    SegmentInfo IPathInternals.PointAlongPath(float distance)
       => this.InnerPath.PointAlongPath(distance);

    /// <inheritdoc/>
    IReadOnlyList<InternalPath> IInternalPathOwner.GetRingsAsInternalPath()
        => this.internalPathRings ??= [this.InnerPath];

    /// <summary>
    /// Computes path bounds directly from segment bounds without materializing <see cref="InternalPath"/>.
    /// </summary>
    private RectangleF CalculateBounds()
    {
        if (this.lineSegments.Length == 0)
        {
            return RectangleF.Empty;
        }

        RectangleF bounds = this.lineSegments[0].Bounds;

        for (int i = 1; i < this.lineSegments.Length; i++)
        {
            bounds = RectangleF.Union(bounds, this.lineSegments[i].Bounds);
        }

        return bounds;
    }

    /// <summary>
    /// Materializes the segment sequence into the retained array used by the path.
    /// </summary>
    /// <param name="segments">The segment sequence to materialize.</param>
    /// <returns>The retained segment array.</returns>
    private static ILineSegment[] GetSegmentArray(IEnumerable<ILineSegment> segments)
    {
        Guard.NotNull(segments, nameof(segments));
        return segments as ILineSegment[] ?? [.. segments];
    }

    /// <summary>
    /// Counts how many derived segments survive as non-horizontal raster work for each sampling origin.
    /// </summary>
    /// <param name="points">The retained contour point run.</param>
    /// <param name="pointCount">The number of retained points in the contour.</param>
    /// <param name="isClosed">Whether the contour closes back to its first point.</param>
    /// <param name="nonHorizontalSegmentCountPixelBoundary">The accumulated pixel-boundary count to update.</param>
    /// <param name="nonHorizontalSegmentCountPixelCenter">The accumulated pixel-center count to update.</param>
    private static void CountNonHorizontalSegments(
        ReadOnlySpan<PointF> points,
        int pointCount,
        bool isClosed,
        ref int nonHorizontalSegmentCountPixelBoundary,
        ref int nonHorizontalSegmentCountPixelCenter)
    {
        if (pointCount <= 1)
        {
            return;
        }

        int segmentCount = isClosed ? pointCount : pointCount - 1;
        for (int i = 0; i < segmentCount; i++)
        {
            PointF start = points[i];
            PointF end = points[(i + 1) == pointCount ? 0 : i + 1];
            if (ToFixedBoundary(start.Y) != ToFixedBoundary(end.Y))
            {
                nonHorizontalSegmentCountPixelBoundary++;
            }

            if (ToFixedCenter(start.Y) != ToFixedCenter(end.Y))
            {
                nonHorizontalSegmentCountPixelCenter++;
            }
        }
    }

    /// <summary>
    /// Converts a coordinate to the fixed-point row space used by boundary-sampled raster work.
    /// </summary>
    /// <param name="value">The coordinate to convert.</param>
    /// <returns>The rounded 24.8 fixed-point value.</returns>
    private static int ToFixedBoundary(float value) => (int)MathF.Round(value * 256F);

    /// <summary>
    /// Converts a coordinate to the fixed-point row space used by center-sampled raster work.
    /// </summary>
    /// <param name="value">The coordinate to convert.</param>
    /// <returns>The rounded 24.8 fixed-point value after the half-pixel sampling offset is applied.</returns>
    private static int ToFixedCenter(float value) => (int)MathF.Round((value + 0.5F) * 256F);

    /// <summary>
    /// Converts an SVG path string into an <see cref="IPath"/>.
    /// </summary>
    /// <param name="svgPath">The string containing the SVG path data.</param>
    /// <param name="value">
    /// When this method returns, contains the logic path converted from the given SVG path string; otherwise, <see langword="null"/>.
    /// This parameter is passed uninitialized.
    /// </param>
    /// <returns><see langword="true"/> if the input value can be parsed and converted; otherwise, <see langword="false"/>.</returns>
    public static bool TryParseSvgPath(string svgPath, [NotNullWhen(true)] out IPath? value)
        => TryParseSvgPath(svgPath.AsSpan(), out value);

    /// <summary>
    /// Converts an SVG path string into an <see cref="IPath"/>.
    /// </summary>
    /// <param name="svgPath">The string containing the SVG path data.</param>
    /// <param name="value">
    /// When this method returns, contains the logic path converted from the given SVG path string; otherwise, <see langword="null"/>.
    /// This parameter is passed uninitialized.
    /// </param>
    /// <returns><see langword="true"/> if the input value can be parsed and converted; otherwise, <see langword="false"/>.</returns>
    public static bool TryParseSvgPath(ReadOnlySpan<char> svgPath, [NotNullWhen(true)] out IPath? value)
    {
        value = null;

        PathBuilder builder = new();

        PointF first = PointF.Empty;
        PointF c = PointF.Empty;
        PointF lastc = PointF.Empty;
        PointF point1;
        PointF point2;
        PointF point3;

        char op = '\0';
        char previousOp = '\0';
        bool relative = false;
        while (true)
        {
            svgPath = svgPath.TrimStart();
            if (svgPath.Length == 0)
            {
                break;
            }

            char ch = svgPath[0];
            if (char.IsDigit(ch) || ch == '-' || ch == '+' || ch == '.')
            {
                // Are we are the end of the string or we are at the end of the path?
                if (svgPath.Length == 0 || op == 'Z')
                {
                    return false;
                }
            }
            else if (IsSeparator(ch))
            {
                svgPath = TrimSeparator(svgPath);
            }
            else
            {
                op = ch;
                relative = false;
                if (char.IsLower(op))
                {
                    op = char.ToUpper(op, CultureInfo.InvariantCulture);
                    relative = true;
                }

                svgPath = TrimSeparator(svgPath[1..]);
            }

            switch (op)
            {
                case 'M':
                    svgPath = FindPoint(svgPath, relative, c, out point1);
                    builder.MoveTo(point1);
                    previousOp = '\0';
                    op = 'L';
                    c = point1;
                    break;
                case 'L':
                    svgPath = FindPoint(svgPath, relative, c, out point1);
                    builder.LineTo(point1);
                    c = point1;
                    break;
                case 'H':
                    svgPath = FindScaler(svgPath, out float x);
                    if (relative)
                    {
                        x += c.X;
                    }

                    builder.LineTo(x, c.Y);
                    c.X = x;
                    break;
                case 'V':
                    svgPath = FindScaler(svgPath, out float y);
                    if (relative)
                    {
                        y += c.Y;
                    }

                    builder.LineTo(c.X, y);
                    c.Y = y;
                    break;
                case 'C':
                    svgPath = FindPoint(svgPath, relative, c, out point1);
                    svgPath = FindPoint(svgPath, relative, c, out point2);
                    svgPath = FindPoint(svgPath, relative, c, out point3);
                    builder.CubicBezierTo(point1, point2, point3);
                    lastc = point2;
                    c = point3;
                    break;
                case 'S':
                    svgPath = FindPoint(svgPath, relative, c, out point2);
                    svgPath = FindPoint(svgPath, relative, c, out point3);
                    point1 = c;
                    if (previousOp is 'C' or 'S')
                    {
                        point1.X -= lastc.X - c.X;
                        point1.Y -= lastc.Y - c.Y;
                    }

                    builder.CubicBezierTo(point1, point2, point3);
                    lastc = point2;
                    c = point3;
                    break;
                case 'Q': // Quadratic Bezier Curve
                    svgPath = FindPoint(svgPath, relative, c, out point1);
                    svgPath = FindPoint(svgPath, relative, c, out point2);
                    builder.QuadraticBezierTo(point1, point2);
                    lastc = point1;
                    c = point2;
                    break;
                case 'T':
                    svgPath = FindPoint(svgPath, relative, c, out point2);
                    point1 = c;
                    if (previousOp is 'Q' or 'T')
                    {
                        point1.X -= lastc.X - c.X;
                        point1.Y -= lastc.Y - c.Y;
                    }

                    builder.QuadraticBezierTo(point1, point2);
                    lastc = point1;
                    c = point2;
                    break;
                case 'A':
                    if (TryFindScaler(ref svgPath, out float radiiX)
                        && TryTrimSeparator(ref svgPath)
                        && TryFindScaler(ref svgPath, out float radiiY)
                        && TryTrimSeparator(ref svgPath)
                        && TryFindScaler(ref svgPath, out float angle)
                        && TryTrimSeparator(ref svgPath)
                        && TryFindScaler(ref svgPath, out float largeArc)
                        && TryTrimSeparator(ref svgPath)
                        && TryFindScaler(ref svgPath, out float sweep)
                        && TryFindPoint(ref svgPath, relative, c, out PointF point))
                    {
                        builder.ArcTo(radiiX, radiiY, angle, largeArc == 1, sweep == 1, point);
                        c = point;
                    }

                    break;
                case 'Z':
                    builder.CloseFigure();
                    c = first;
                    break;
                case '~':
                    svgPath = FindPoint(svgPath, relative, c, out point1);
                    svgPath = FindPoint(svgPath, relative, c, out point2);
                    builder.MoveTo(point1).LineTo(point2);
                    break;
                default:
                    return false;
            }

            if (previousOp == 0)
            {
                first = c;
            }

            previousOp = op;
        }

        value = builder.Build();
        return true;
    }

    private static bool TryTrimSeparator(ref ReadOnlySpan<char> str)
    {
        ReadOnlySpan<char> result = TrimSeparator(str);
        if (str[^result.Length..].StartsWith(result))
        {
            str = result;
            return true;
        }

        return false;
    }

    private static bool TryFindScaler(ref ReadOnlySpan<char> str, out float value)
    {
        ReadOnlySpan<char> result = FindScaler(str, out float valueInner);
        if (str[^result.Length..].StartsWith(result))
        {
            value = valueInner;
            str = result;
            return true;
        }

        value = default;
        return false;
    }

    private static bool TryFindPoint(ref ReadOnlySpan<char> str, bool relative, PointF current, out PointF value)
    {
        ReadOnlySpan<char> result = FindPoint(str, relative, current, out PointF valueInner);
        if (str[^result.Length..].StartsWith(result))
        {
            value = valueInner;
            str = result;
            return true;
        }

        value = default;
        return false;
    }

    private static ReadOnlySpan<char> FindPoint(ReadOnlySpan<char> str, bool isRelative, PointF relative, out PointF value)
    {
        str = FindScaler(str, out float x);
        str = FindScaler(str, out float y);
        if (isRelative)
        {
            x += relative.X;
            y += relative.Y;
        }

        value = new PointF(x, y);
        return str;
    }

    private static ReadOnlySpan<char> FindScaler(ReadOnlySpan<char> str, out float scaler)
    {
        str = TrimSeparator(str);
        scaler = 0;

        bool hasDot = false;
        for (int i = 0; i < str.Length; i++)
        {
            char ch = str[i];

            if (IsSeparator(ch))
            {
                scaler = ParseFloat(str[..i]);
                return str[i..];
            }

            if (ch == '.')
            {
                if (hasDot)
                {
                    // Second decimal point starts a new number.
                    scaler = ParseFloat(str[..i]);
                    return str[i..];
                }

                hasDot = true;
            }
            else if ((ch is '-' or '+') && i > 0)
            {
                // A sign character mid-number starts a new number,
                // unless it follows an exponent indicator.
                char prev = str[i - 1];
                if (prev is not 'e' and not 'E')
                {
                    scaler = ParseFloat(str[..i]);
                    return str[i..];
                }
            }
            else if (char.IsLetter(ch))
            {
                // Hit a command letter; end this number.
                scaler = ParseFloat(str[..i]);
                return str[i..];
            }
        }

        if (str.Length > 0)
        {
            scaler = ParseFloat(str);
        }

        return [];
    }

    private static bool IsSeparator(char ch)
        => char.IsWhiteSpace(ch) || ch == ',';

    private static ReadOnlySpan<char> TrimSeparator(ReadOnlySpan<char> data)
    {
        if (data.Length == 0)
        {
            return data;
        }

        int idx = 0;
        for (; idx < data.Length; idx++)
        {
            if (!IsSeparator(data[idx]))
            {
                break;
            }
        }

        return data[idx..];
    }

    private static float ParseFloat(ReadOnlySpan<char> str)
        => float.Parse(str, provider: CultureInfo.InvariantCulture);
}
