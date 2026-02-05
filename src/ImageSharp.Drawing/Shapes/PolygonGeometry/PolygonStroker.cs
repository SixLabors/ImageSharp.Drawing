// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Runtime.CompilerServices;
using SixLabors.ImageSharp.Drawing.Processing;

#pragma warning disable SA1201 // Elements should appear in the correct order
namespace SixLabors.ImageSharp.Drawing.Shapes.PolygonGeometry;

/// <summary>
/// Generates polygonal stroke outlines for vector paths using analytic joins and caps.
/// </summary>
/// <remarks>
/// <para>
/// This class performs geometric stroking of input paths, producing an explicit polygonal
/// outline suitable for filling or clipping. It replicates the behavior of analytic stroking
/// as implemented in vector renderers (e.g., AGG, Skia), without relying on rasterization.
/// </para>
/// <para>
/// The stroker supports multiple join and cap styles, adjustable miter limits, and an
/// approximation scale for arc and round joins. It operates entirely in double precision
/// for numerical stability, emitting <see cref="PointF"/> coordinates for downstream use
/// in polygon merging or clipping operations.
/// </para>
/// <para>
/// Used by higher-level utility<see cref="StrokedShapeGenerator"/> to produce consistent,
/// merged outlines for stroked paths and dashed spans.
/// </para>
/// </remarks>
internal sealed class PolygonStroker
{
    private ArrayBuilder<PointF> outVertices = new(1);
    private ArrayBuilder<VertexDistance> srcVertices = new(16);
    private int closed;
    private int outVertex;
    private Status prevStatus;
    private int srcVertex;
    private Status status;
    private double strokeWidth = 0.5;
    private double widthAbs = 0.5;
    private double widthEps = 0.5 / 1024.0;
    private int widthSign = 1;

    /// <summary>
    /// Initializes a new instance of the <see cref="PolygonStroker"/> class with the specified stroke options.
    /// </summary>
    /// <param name="options">
    /// The stroke options to use for configuring line joins, caps, miter limits, and approximation scale.
    /// Cannot be <see langword="null"/>.
    /// </param>
    public PolygonStroker(StrokeOptions options)
    {
        this.LineJoin = options.LineJoin;
        this.InnerJoin = options.InnerJoin;
        this.LineCap = options.LineCap;
        this.MiterLimit = options.MiterLimit;
        this.InnerMiterLimit = options.InnerMiterLimit;
        this.ApproximationScale = options.ApproximationScale;
    }

    /// <summary>
    /// Gets the miter limit used to clamp outer miter joins.
    /// </summary>
    public double MiterLimit { get; }

    /// <summary>
    /// Gets the inner miter limit used to clamp joins on acute interior angles.
    /// </summary>
    public double InnerMiterLimit { get; }

    /// <summary>
    /// Gets the arc approximation scale used for round joins and caps.
    /// </summary>
    public double ApproximationScale { get; }

    /// <summary>
    /// Gets the outer line join style used for stroking corners.
    /// </summary>
    public LineJoin LineJoin { get; }

    /// <summary>
    /// Gets the line cap style used for open path ends.
    /// </summary>
    public LineCap LineCap { get; }

    /// <summary>
    /// Gets the join style used for sharp interior angles.
    /// </summary>
    public InnerJoin InnerJoin { get; }

    /// <summary>
    /// Gets or sets the stroke width in the caller's coordinate space.
    /// </summary>
    public double Width
    {
        get => this.strokeWidth * 2.0;
        set
        {
            this.strokeWidth = value * 0.5;
            if (this.strokeWidth < 0)
            {
                this.widthAbs = -this.strokeWidth;
                this.widthSign = -1;
            }
            else
            {
                this.widthAbs = this.strokeWidth;
                this.widthSign = 1;
            }

            this.widthEps = this.strokeWidth / 1024.0;
        }
    }

    /// <summary>
    /// Strokes the provided polyline or polygon and returns the outline vertices.
    /// </summary>
    /// <param name="linePoints">The input points to stroke.</param>
    /// <param name="isClosed">Whether the input is a closed ring.</param>
    /// <returns>The stroked outline as a closed point array.</returns>
    /// <remarks>
    /// When a 2-point input contains identical points (degenerate case), this method generates
    /// a cap shape at that point: a circle for round caps or a square for square/butt caps.
    /// This ensures that even degenerate input produces visible output when stroked.
    /// </remarks>
    public PointF[] ProcessPath(ReadOnlySpan<PointF> linePoints, bool isClosed)
    {
        if (linePoints.Length < 2)
        {
            return [];
        }

        // Special case: for 2-point inputs, check if both points are identical (degenerate case)
        // This avoids overhead for longer paths where the filtering logic handles near-duplicates
        if (linePoints.Length == 2)
        {
            PointF p0 = linePoints[0];
            PointF p1 = linePoints[1];

            if (Math.Abs(p1.X - p0.X) <= Constants.Misc.VertexDistanceEpsilon &&
                Math.Abs(p1.Y - p0.Y) <= Constants.Misc.VertexDistanceEpsilon)
            {
                // Both points are identical - generate a point cap shape
                return this.GeneratePointCap(p0.X, p0.Y);
            }
        }

        this.Reset();
        this.AddLinePath(linePoints);

        if (isClosed)
        {
            this.ClosePath();
        }

        List<PointF> results = new(linePoints.Length * 3);
        this.FinishPath(results);
        return [.. results];
    }

    /// <summary>
    /// Adds a sequence of line segments to the current stroker state.
    /// </summary>
    /// <param name="linePoints">The input points to add as line segments.</param>
    public void AddLinePath(ReadOnlySpan<PointF> linePoints)
    {
        for (int i = 0; i < linePoints.Length; i++)
        {
            PointF point = linePoints[i];
            this.AddVertex(point.X, point.Y, PathCommand.LineTo);
        }
    }

    /// <summary>
    /// Marks the current path as closed before finishing the outline.
    /// </summary>
    public void ClosePath()
    {
        // Mark the current src path as closed; no geometry is pushed here.
        this.closed = (int)PathFlags.Close;
        this.status = Status.Initial;
    }

    /// <summary>
    /// Finalizes stroking and appends output points to the provided list.
    /// </summary>
    /// <param name="results">The list that receives the stroked outline vertices.</param>
    public void FinishPath(List<PointF> results)
    {
        PointF currentPoint = new(0, 0);
        int startIndex = 0;
        PointF? lastPoint = null;
        PathCommand command;

        while (!(command = this.Accumulate(ref currentPoint)).Stop())
        {
            if (command.EndPoly() && results.Count > 0)
            {
                PointF initial = results[startIndex];
                results.Add(initial);
                startIndex = results.Count;
            }
            else
            {
                if (currentPoint != lastPoint)
                {
                    results.Add(currentPoint);
                    lastPoint = currentPoint;
                }
            }
        }
    }

    /// <summary>
    /// Resets the stroker state so it can be reused for a new path.
    /// </summary>
    public void Reset()
    {
        this.srcVertices.Clear();
        this.outVertices.Clear();
        this.srcVertex = 0;
        this.outVertex = 0;
        this.closed = 0;
        this.status = Status.Initial;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void AddVertex(double x, double y, PathCommand cmd)
    {
        this.status = Status.Initial;
        if (cmd.MoveTo())
        {
            if (this.srcVertices.Length != 0)
            {
                this.srcVertices.RemoveLast();
            }

            this.AddVertex(x, y);
        }
        else if (cmd.Vertex())
        {
            this.AddVertex(x, y);
        }
        else
        {
            this.closed = cmd.GetCloseFlag();
        }
    }

    private PathCommand Accumulate(ref PointF point)
    {
        PathCommand cmd = PathCommand.LineTo;
        while (!cmd.Stop())
        {
            switch (this.status)
            {
                case Status.Initial:
                    this.CloseVertexPath(this.closed != 0);

                    if (this.srcVertices.Length < 3)
                    {
                        this.closed = 0;
                    }

                    this.status = Status.Ready;

                    break;

                case Status.Ready:
                    if (this.srcVertices.Length < 2 + (this.closed != 0 ? 1 : 0))
                    {
                        cmd = PathCommand.Stop;

                        break;
                    }

                    this.status = this.closed != 0 ? Status.Outline1 : Status.Cap1;
                    cmd = PathCommand.MoveTo;
                    this.srcVertex = 0;
                    this.outVertex = 0;

                    break;

                case Status.Cap1:
                    this.CalcCap(ref this.srcVertices[0], ref this.srcVertices[1], this.srcVertices[0].Distance);
                    this.srcVertex = 1;
                    this.prevStatus = Status.Outline1;
                    this.status = Status.OutVertices;
                    this.outVertex = 0;

                    break;

                case Status.Cap2:
                    this.CalcCap(ref this.srcVertices[^1], ref this.srcVertices[^2], this.srcVertices[^2].Distance);
                    this.prevStatus = Status.Outline2;
                    this.status = Status.OutVertices;
                    this.outVertex = 0;

                    break;

                case Status.Outline1:
                    if (this.closed != 0)
                    {
                        if (this.srcVertex >= this.srcVertices.Length)
                        {
                            this.prevStatus = Status.CloseFirst;
                            this.status = Status.EndPoly1;

                            break;
                        }
                    }
                    else if (this.srcVertex >= this.srcVertices.Length - 1)
                    {
                        this.status = Status.Cap2;

                        break;
                    }

                    this.CalcJoin(
                        ref this.srcVertices[(this.srcVertex + this.srcVertices.Length - 1) % this.srcVertices.Length],
                        ref this.srcVertices[this.srcVertex],
                        ref this.srcVertices[(this.srcVertex + 1) % this.srcVertices.Length],
                        this.srcVertices[(this.srcVertex + this.srcVertices.Length - 1) % this.srcVertices.Length].Distance,
                        this.srcVertices[this.srcVertex].Distance);

                    ++this.srcVertex;

                    this.prevStatus = this.status;
                    this.status = Status.OutVertices;
                    this.outVertex = 0;

                    break;

                case Status.CloseFirst:
                    this.status = Status.Outline2;
                    cmd = PathCommand.MoveTo;
                    this.status = Status.Outline2;

                    break;

                case Status.Outline2:
                    if (this.srcVertex <= (this.closed == 0 ? 1 : 0))
                    {
                        this.status = Status.EndPoly2;
                        this.prevStatus = Status.Stop;

                        break;
                    }

                    --this.srcVertex;

                    this.CalcJoin(
                        ref this.srcVertices[(this.srcVertex + 1) % this.srcVertices.Length],
                        ref this.srcVertices[this.srcVertex],
                        ref this.srcVertices[(this.srcVertex + this.srcVertices.Length - 1) % this.srcVertices.Length],
                        this.srcVertices[this.srcVertex].Distance,
                        this.srcVertices[(this.srcVertex + this.srcVertices.Length - 1) % this.srcVertices.Length].Distance);

                    this.prevStatus = this.status;
                    this.status = Status.OutVertices;
                    this.outVertex = 0;

                    break;

                case Status.OutVertices:
                    if (this.outVertex >= this.outVertices.Length)
                    {
                        this.status = this.prevStatus;
                    }
                    else
                    {
                        PointF c = this.outVertices[this.outVertex++];
                        point = c;

                        return cmd;
                    }

                    break;

                case Status.EndPoly1:
                    this.status = this.prevStatus;

                    return PathCommand.EndPoly | (PathCommand)(PathFlags.Close | PathFlags.Ccw);

                case Status.EndPoly2:
                    this.status = this.prevStatus;

                    return PathCommand.EndPoly | (PathCommand)(PathFlags.Close | PathFlags.Cw);

                case Status.Stop:
                    cmd = PathCommand.Stop;

                    break;
            }
        }

        return cmd;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void AddVertex(double x, double y, double distance = 0)
    {
        if (this.srcVertices.Length > 1)
        {
            ref VertexDistance vd1 = ref this.srcVertices[^2];
            ref VertexDistance vd2 = ref this.srcVertices[^1];
            bool ret = vd1.Measure(vd2);
            if (!ret && this.srcVertices.Length != 0)
            {
                this.srcVertices.RemoveLast();
            }
        }

        this.srcVertices.Add(new VertexDistance(x, y, distance));
    }

    private void CloseVertexPath(bool closed)
    {
        while (this.srcVertices.Length > 1)
        {
            ref VertexDistance vd1 = ref this.srcVertices[^2];
            ref VertexDistance vd2 = ref this.srcVertices[^1];
            bool ret = vd1.Measure(vd2);

            if (ret)
            {
                break;
            }

            VertexDistance t = this.srcVertices[^1];
            if (this.srcVertices.Length != 0)
            {
                this.srcVertices.RemoveLast();
            }

            // Remove the tail pair (vd2 and its predecessor vd1) and re-add the tail 't'.
            // Re-adding forces a fresh Measure() against the new predecessor, collapsing zero-length edges.
            if (this.srcVertices.Length != 0)
            {
                this.srcVertices.RemoveLast();
            }

            this.AddVertex(t.X, t.Y, t.Distance);
        }

        if (!closed)
        {
            return;
        }

        // TODO: Why check again? Doesn't the while loop above already ensure this?
        while (this.srcVertices.Length > 1)
        {
            ref VertexDistance vd1 = ref this.srcVertices[^1];
            ref VertexDistance vd2 = ref this.srcVertices[0];
            bool ret = vd1.Measure(vd2);

            if (ret)
            {
                break;
            }

            if (this.srcVertices.Length != 0)
            {
                this.srcVertices.RemoveLast();
            }
        }
    }

    private void CalcArc(double x, double y, double dx1, double dy1, double dx2, double dy2)
    {
        double a1 = Math.Atan2(dy1 * this.widthSign, dx1 * this.widthSign);
        double a2 = Math.Atan2(dy2 * this.widthSign, dx2 * this.widthSign);
        int i, n;

        double da = Math.Acos(this.widthAbs / (this.widthAbs + (0.125 / this.ApproximationScale))) * 2;

        this.AddPoint(x + dx1, y + dy1);
        if (this.widthSign > 0)
        {
            if (a1 > a2)
            {
                a2 += Constants.Misc.PiMul2;
            }

            n = (int)((a2 - a1) / da);
            da = (a2 - a1) / (n + 1);
            a1 += da;
            for (i = 0; i < n; i++)
            {
                this.AddPoint(x + (Math.Cos(a1) * this.strokeWidth), y + (Math.Sin(a1) * this.strokeWidth));
                a1 += da;
            }
        }
        else
        {
            if (a1 < a2)
            {
                a2 -= Constants.Misc.PiMul2;
            }

            n = (int)((a1 - a2) / da);
            da = (a1 - a2) / (n + 1);
            a1 -= da;
            for (i = 0; i < n; i++)
            {
                this.AddPoint(x + (Math.Cos(a1) * this.strokeWidth), y + (Math.Sin(a1) * this.strokeWidth));
                a1 -= da;
            }
        }

        this.AddPoint(x + dx2, y + dy2);
    }

    private void CalcMiter(
        ref VertexDistance v0,
        ref VertexDistance v1,
        ref VertexDistance v2,
        double dx1,
        double dy1,
        double dx2,
        double dy2,
        LineJoin lj,
        double mlimit,
        double dbevel)
    {
        double xi = v1.X;
        double yi = v1.Y;
        double di = 1.0;
        double lim = this.widthAbs * mlimit;
        bool miterLimitExceeded = true;
        bool intersectionFailed = true;

        if (UtilityMethods.CalcIntersection(v0.X + dx1, v0.Y - dy1, v1.X + dx1, v1.Y - dy1, v1.X + dx2, v1.Y - dy2, v2.X + dx2, v2.Y - dy2, ref xi, ref yi))
        {
            di = UtilityMethods.CalcDistance(v1.X, v1.Y, xi, yi);
            if (di <= lim)
            {
                this.AddPoint(xi, yi);
                miterLimitExceeded = false;
            }

            intersectionFailed = false;
        }
        else
        {
            double x2 = v1.X + dx1;
            double y2 = v1.Y - dy1;
            if ((UtilityMethods.CrossProduct(v0.X, v0.Y, v1.X, v1.Y, x2, y2) < 0.0) == (UtilityMethods.CrossProduct(v1.X, v1.Y, v2.X, v2.Y, x2, y2) < 0.0))
            {
                this.AddPoint(v1.X + dx1, v1.Y - dy1);
                miterLimitExceeded = false;
            }
        }

        if (!miterLimitExceeded)
        {
            return;
        }

        switch (lj)
        {
            case LineJoin.MiterRevert:

                this.AddPoint(v1.X + dx1, v1.Y - dy1);
                this.AddPoint(v1.X + dx2, v1.Y - dy2);

                break;

            case LineJoin.MiterRound:
                this.CalcArc(v1.X, v1.Y, dx1, -dy1, dx2, -dy2);

                break;

            default:
                if (intersectionFailed)
                {
                    mlimit *= this.widthSign;
                    this.AddPoint(v1.X + dx1 + (dy1 * mlimit), v1.Y - dy1 + (dx1 * mlimit));
                    this.AddPoint(v1.X + dx2 - (dy2 * mlimit), v1.Y - dy2 - (dx2 * mlimit));
                }
                else
                {
                    double x1 = v1.X + dx1;
                    double y1 = v1.Y - dy1;
                    double x2 = v1.X + dx2;
                    double y2 = v1.Y - dy2;
                    di = (lim - dbevel) / (di - dbevel);
                    this.AddPoint(x1 + ((xi - x1) * di), y1 + ((yi - y1) * di));
                    this.AddPoint(x2 + ((xi - x2) * di), y2 + ((yi - y2) * di));
                }

                break;
        }
    }

    private void CalcCap(ref VertexDistance v0, ref VertexDistance v1, double len)
    {
        this.outVertices.Clear();

        if (len < Constants.Misc.VertexDistanceEpsilon)
        {
            // Degenerate cap: emit a symmetric butt cap of zero span.
            // This avoids div-by-zero in direction computation.
            this.AddPoint(v0.X, v0.Y);
            this.AddPoint(v1.X, v1.Y);
            return;
        }

        double dx1 = (v1.Y - v0.Y) / len;
        double dy1 = (v1.X - v0.X) / len;
        double dx2 = 0;
        double dy2 = 0;

        dx1 *= this.strokeWidth;
        dy1 *= this.strokeWidth;

        if (this.LineCap != LineCap.Round)
        {
            if (this.LineCap == LineCap.Square)
            {
                dx2 = dy1 * this.widthSign;
                dy2 = dx1 * this.widthSign;
            }

            this.AddPoint(v0.X - dx1 - dx2, v0.Y + dy1 - dy2);
            this.AddPoint(v0.X + dx1 - dx2, v0.Y - dy1 - dy2);
        }
        else
        {
            double da = Math.Acos(this.widthAbs / (this.widthAbs + (0.125 / this.ApproximationScale))) * 2;
            double a1;
            int i;
            int n = (int)(Constants.Misc.Pi / da);

            da = Constants.Misc.Pi / (n + 1);
            this.AddPoint(v0.X - dx1, v0.Y + dy1);
            if (this.widthSign > 0)
            {
                a1 = Math.Atan2(dy1, -dx1);
                a1 += da;
                for (i = 0; i < n; i++)
                {
                    this.AddPoint(v0.X + (Math.Cos(a1) * this.strokeWidth), v0.Y + (Math.Sin(a1) * this.strokeWidth));
                    a1 += da;
                }
            }
            else
            {
                a1 = Math.Atan2(-dy1, dx1);
                a1 -= da;
                for (i = 0; i < n; i++)
                {
                    this.AddPoint(v0.X + (Math.Cos(a1) * this.strokeWidth), v0.Y + (Math.Sin(a1) * this.strokeWidth));
                    a1 -= da;
                }
            }

            this.AddPoint(v0.X + dx1, v0.Y - dy1);
        }
    }

    private void CalcJoin(ref VertexDistance v0, ref VertexDistance v1, ref VertexDistance v2, double len1, double len2)
    {
        const double eps = Constants.Misc.VertexDistanceEpsilon;
        if (len1 < eps || len2 < eps)
        {
            // Degenerate join: reuse the non-degenerate edge length for both offsets
            // to emit a simple bevel and avoid unstable direction math.
            this.outVertices.Clear();

            double l1 = len1 >= eps ? len1 : len2;
            double l2 = len2 >= eps ? len2 : len1;

            double offX1 = this.strokeWidth * (v1.Y - v0.Y) / l1;
            double offY1 = this.strokeWidth * (v1.X - v0.X) / l1;
            double offX2 = this.strokeWidth * (v2.Y - v1.Y) / l2;
            double offY2 = this.strokeWidth * (v2.X - v1.X) / l2;

            this.AddPoint(v1.X + offX1, v1.Y - offY1);
            this.AddPoint(v1.X + offX2, v1.Y - offY2);
            return;
        }

        double dx1 = this.strokeWidth * (v1.Y - v0.Y) / len1;
        double dy1 = this.strokeWidth * (v1.X - v0.X) / len1;
        double dx2 = this.strokeWidth * (v2.Y - v1.Y) / len2;
        double dy2 = this.strokeWidth * (v2.X - v1.X) / len2;

        this.outVertices.Clear();

        double cp = UtilityMethods.CrossProduct(v0.X, v0.Y, v1.X, v1.Y, v2.X, v2.Y);
        if (Math.Abs(cp) > double.Epsilon && (cp > 0) == (this.strokeWidth > 0))
        {
            double limit = (len1 < len2 ? len1 : len2) / this.widthAbs;
            if (limit < this.InnerMiterLimit)
            {
                limit = this.InnerMiterLimit;
            }

            switch (this.InnerJoin)
            {
                default: // inner_bevel
                    this.AddPoint(v1.X + dx1, v1.Y - dy1);
                    this.AddPoint(v1.X + dx2, v1.Y - dy2);

                    break;

                case InnerJoin.Miter:
                    this.CalcMiter(ref v0, ref v1, ref v2, dx1, dy1, dx2, dy2, LineJoin.MiterRevert, limit, 0);

                    break;

                case InnerJoin.Jag:
                case InnerJoin.Round:
                    cp = ((dx1 - dx2) * (dx1 - dx2)) + ((dy1 - dy2) * (dy1 - dy2));
                    if (cp < len1 * len1 && cp < len2 * len2)
                    {
                        this.CalcMiter(ref v0, ref v1, ref v2, dx1, dy1, dx2, dy2, LineJoin.MiterRevert, limit, 0);
                    }
                    else if (this.InnerJoin == InnerJoin.Jag)
                    {
                        this.AddPoint(v1.X + dx1, v1.Y - dy1);
                        this.AddPoint(v1.X, v1.Y);
                        this.AddPoint(v1.X + dx2, v1.Y - dy2);
                    }
                    else
                    {
                        this.AddPoint(v1.X + dx1, v1.Y - dy1);
                        this.AddPoint(v1.X, v1.Y);
                        this.CalcArc(v1.X, v1.Y, dx2, -dy2, dx1, -dy1);
                        this.AddPoint(v1.X, v1.Y);
                        this.AddPoint(v1.X + dx2, v1.Y - dy2);
                    }

                    break;
            }
        }
        else
        {
            double dx = (dx1 + dx2) / 2;
            double dy = (dy1 + dy2) / 2;
            double dbevel = Math.Sqrt((dx * dx) + (dy * dy));

            if (this.LineJoin is LineJoin.Round or LineJoin.Bevel && this.ApproximationScale * (this.widthAbs - dbevel) < this.widthEps)
            {
                if (UtilityMethods.CalcIntersection(v0.X + dx1, v0.Y - dy1, v1.X + dx1, v1.Y - dy1, v1.X + dx2, v1.Y - dy2, v2.X + dx2, v2.Y - dy2, ref dx, ref dy))
                {
                    this.AddPoint(dx, dy);
                }
                else
                {
                    this.AddPoint(v1.X + dx1, v1.Y - dy1);
                }

                return;
            }

            switch (this.LineJoin)
            {
                case LineJoin.Miter:
                case LineJoin.MiterRevert:
                case LineJoin.MiterRound:
                    this.CalcMiter(ref v0, ref v1, ref v2, dx1, dy1, dx2, dy2, this.LineJoin, this.MiterLimit, dbevel);

                    break;

                case LineJoin.Round:
                    this.CalcArc(v1.X, v1.Y, dx1, -dy1, dx2, -dy2);

                    break;

                default:
                    this.AddPoint(v1.X + dx1, v1.Y - dy1);
                    this.AddPoint(v1.X + dx2, v1.Y - dy2);

                    break;
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void AddPoint(double x, double y) => this.outVertices.Add(new PointF((float)x, (float)y));

    /// <summary>
    /// Generates a cap shape for a degenerate point (when all input points are identical).
    /// Creates a circle for round caps or a square for square/butt caps.
    /// </summary>
    /// <param name="x">The X coordinate of the point.</param>
    /// <param name="y">The Y coordinate of the point.</param>
    /// <returns>The vertices forming the cap shape.</returns>
    private PointF[] GeneratePointCap(double x, double y)
    {
        if (this.LineCap == LineCap.Round)
        {
            // Generate a circle with radius = strokeWidth
            double da = Math.Acos(this.widthAbs / (this.widthAbs + (0.125 / this.ApproximationScale))) * 2;
            int n = Math.Max(4, (int)(Constants.Misc.PiMul2 / da));
            double angleStep = Constants.Misc.PiMul2 / n;

            PointF[] points = new PointF[n + 1];

            for (int i = 0; i < n; i++)
            {
                double angle = i * angleStep;
                points[i] = new PointF(
                    (float)(x + (Math.Cos(angle) * this.strokeWidth)),
                    (float)(y + (Math.Sin(angle) * this.strokeWidth)));
            }

            // Close the circle
            points[n] = points[0];

            return points;
        }
        else
        {
            // Generate a square cap (used for both Square and Butt caps)
            double w = this.strokeWidth;
            return
            [
                new PointF((float)(x - w), (float)(y - w)),
                new PointF((float)(x + w), (float)(y - w)),
                new PointF((float)(x + w), (float)(y + w)),
                new PointF((float)(x - w), (float)(y + w)),
                new PointF((float)(x - w), (float)(y - w)) // Close the square
            ];
        }
    }

    private enum Status
    {
        Initial,
        Ready,
        Cap1,
        Cap2,
        Outline1,
        CloseFirst,
        Outline2,
        OutVertices,
        EndPoly1,
        EndPoly2,
        Stop
    }
}

[Flags]
internal enum PathCommand : byte
{
    Stop = 0,
    MoveTo = 1,
    LineTo = 2,
    Curve3 = 3,
    Curve4 = 4,
    CurveN = 5,
    Catrom = 6,
    Spline = 7,
    EndPoly = 0x0F,
    Mask = 0x0F
}

[Flags]
internal enum PathFlags : byte
{
    None = 0,
    Ccw = 0x10,
    Cw = 0x20,
    Close = 0x40,
    Mask = 0xF0
}

internal static class PathCommandExtensions
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool Vertex(this PathCommand c) => c is >= PathCommand.MoveTo and < PathCommand.EndPoly;

    public static bool Drawing(this PathCommand c) => c is >= PathCommand.LineTo and < PathCommand.EndPoly;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool Stop(this PathCommand c) => c == PathCommand.Stop;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool MoveTo(this PathCommand c) => c == PathCommand.MoveTo;

    public static bool LineTo(this PathCommand c) => c == PathCommand.LineTo;

    public static bool Curve(this PathCommand c) => c is PathCommand.Curve3 or PathCommand.Curve4;

    public static bool Curve3(this PathCommand c) => c == PathCommand.Curve3;

    public static bool Curve4(this PathCommand c) => c == PathCommand.Curve4;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool EndPoly(this PathCommand c) => (c & PathCommand.Mask) == PathCommand.EndPoly;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool Closed(this PathCommand c) => ((int)c & ~((int)PathFlags.Cw | (int)PathFlags.Ccw)) == ((int)PathCommand.EndPoly | (int)PathFlags.Close);

    public static bool NextPoly(this PathCommand c) => Stop(c) || MoveTo(c) || EndPoly(c);

    public static bool Oriented(int c) => (c & (int)(PathFlags.Cw | PathFlags.Ccw)) != 0;

    public static bool Cw(int c) => (c & (int)PathFlags.Cw) != 0;

    public static bool Ccw(int c) => (c & (int)PathFlags.Ccw) != 0;

    public static int CloseFlag(this PathCommand c) => (int)c & (int)PathFlags.Close;

    public static int GetOrientation(this PathCommand c) => (int)c & (int)(PathFlags.Cw | PathFlags.Ccw);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int ClearOrientation(this PathCommand c) => (int)c & ~(int)(PathFlags.Cw | PathFlags.Ccw);

    public static int SetOrientation(this PathCommand c, PathFlags o) => ClearOrientation(c) | (int)o;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int GetCloseFlag(this PathCommand c) => (int)c & (int)PathFlags.Close;
}
#pragma warning restore SA1201 // Elements should appear in the correct order
