// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Buffers;
using System.Numerics;
using System.Runtime.CompilerServices;
using SixLabors.ImageSharp.Memory;

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

internal static partial class DefaultRasterizer
{
    /// <summary>
    /// Base retained stroke linearizer that expands stroked centerlines once into row-local line storage.
    /// </summary>
    /// <remarks>
    /// The stroke expansion below is a port of AGG's <c>PolygonStroker</c> and is the reference
    /// implementation for the GPU stroker: <c>path_lowering.wgsl</c>'s <c>stroke_side_join</c>,
    /// <c>stroke_calc_miter</c>, <c>stroke_calc_arc</c>, and <c>stroke_chain_point</c> are direct
    /// ports of the corresponding members here, and <c>WebGPUSceneEncoder.EncodeStrokePath</c>
    /// ports the contour preprocessing. Any behavioral change here must be mirrored there.
    /// </remarks>
    /// <typeparam name="TL">The mutable per-row retained line collector type.</typeparam>
    private abstract class StrokeLinearizer<TL> : Linearizer<TL>
        where TL : class
    {
        /// <summary>
        /// The 1/64 px length below which a contour segment is collapsed forward onto the last
        /// kept point so every surviving segment carries a well conditioned tangent. Mirrored by
        /// the GPU encoder's <c>WebGPUSceneEncoder.StrokeMicroSegmentEpsilon</c>.
        /// </summary>
        private const float StrokeMicroSegmentEpsilon = 1F / 64F;

        private readonly StrokeStyle stroke;

        /// <summary>
        /// Initializes a new instance of the <see cref="StrokeLinearizer{TL}"/> class.
        /// </summary>
        /// <param name="geometry">The stroked centerline geometry.</param>
        /// <param name="residual">The residual transform applied to each source point during emission.</param>
        /// <param name="stroke">The stroke style.</param>
        /// <param name="translateX">The destination-space X translation applied at composition time.</param>
        /// <param name="translateY">The destination-space Y translation applied at composition time.</param>
        /// <param name="minX">The minimum destination X bound after clipping.</param>
        /// <param name="minY">The minimum destination Y bound after clipping.</param>
        /// <param name="width">The visible destination width in pixels.</param>
        /// <param name="height">The visible destination height in pixels.</param>
        /// <param name="firstBandIndex">The first retained row-band index.</param>
        /// <param name="rowBandCount">The retained row-band count.</param>
        /// <param name="keepHorizontalLines">Whether to retain horizontal outline edges for aliased thin-feature recovery between row centres.</param>
        /// <param name="allocator">The allocator used for retained start-cover storage.</param>
        protected StrokeLinearizer(
            LinearGeometry geometry,
            Matrix4x4 residual,
            StrokeStyle stroke,
            int translateX,
            int translateY,
            int minX,
            int minY,
            int width,
            int height,
            int firstBandIndex,
            int rowBandCount,
            bool keepHorizontalLines,
            MemoryAllocator allocator)
            : base(geometry, residual, translateX, translateY, minX, minY, width, height, firstBandIndex, rowBandCount, allocator, keepHorizontalLines)
            => this.stroke = stroke;

        /// <summary>
        /// Classifies one stroked contour's inflated bounds against the clipped interest bounds.
        /// </summary>
        private enum ContourInterest
        {
            /// <summary>
            /// The stroked contour cannot touch the interest bounds and is skipped entirely.
            /// </summary>
            Outside,

            /// <summary>
            /// The stroked contour straddles the interest bounds; emitted lines require clipping.
            /// </summary>
            Clipped,

            /// <summary>
            /// The stroked contour lies fully inside the interest bounds; lines are emitted unclipped.
            /// </summary>
            Contained
        }

        /// <inheritdoc />
        protected override bool ProcessCore()
        {
            ReadOnlySpan<LinearContour> contours = this.Geometry.GetContours();
            for (int contourIndex = 0; contourIndex < contours.Length; contourIndex++)
            {
                LinearContour contour = contours[contourIndex];
                if (contour.PointCount == 0)
                {
                    continue;
                }

                ReadOnlySpan<PointF> contourPoints = this.Geometry.GetContourPoints(contour);
                ContourInterest contourInterest = this.GetContourInterest(contourPoints);
                if (contourInterest == ContourInterest.Outside)
                {
                    continue;
                }

                bool isClosed = this.IsContourClosedForEmission(contourPoints, contour.IsClosed);

                this.ProcessContour(contourPoints, isClosed, contourInterest == ContourInterest.Contained);
            }

            if (!this.HasAnyCoverage)
            {
                return false;
            }

            this.FinalizeLines();
            return true;
        }

        /// <summary>
        /// Classifies one stroked contour against the interest bounds.
        /// </summary>
        /// <param name="contourPoints">The contour points.</param>
        /// <returns>The contour's relationship to the interest bounds.</returns>
        private ContourInterest GetContourInterest(ReadOnlySpan<PointF> contourPoints)
        {
            RectangleF translatedBounds = InflateStrokeBounds(this.GetPointBounds(contourPoints), this.stroke);
            translatedBounds.Offset(this.TranslateX - this.MinX, this.TranslateY - this.MinY);

            if (translatedBounds.Right <= 0F ||
                translatedBounds.Bottom <= 0F ||
                translatedBounds.Left >= this.Width ||
                translatedBounds.Top >= this.Height)
            {
                return ContourInterest.Outside;
            }

            if (translatedBounds.Left >= 0F &&
                translatedBounds.Top >= 0F &&
                translatedBounds.Right <= this.Width &&
                translatedBounds.Bottom <= this.Height)
            {
                return ContourInterest.Contained;
            }

            return ContourInterest.Clipped;
        }

        /// <summary>
        /// Returns whether a contour should be treated as closed when emitting stroke geometry.
        /// A contour is closed when it is declared closed, its first and last points coincide, or
        /// the endpoint gap is at most max(strokeWidth, 1e-3). Mirrored at encode time by
        /// <c>WebGPUSceneEncoder.EncodeStrokePath</c>; both must stay in lockstep.
        /// </summary>
        /// <param name="contourPoints">The contour points.</param>
        /// <param name="isDeclaredClosed">Indicates whether the contour is explicitly closed.</param>
        /// <returns><see langword="true"/> when the contour should be stroked as closed; otherwise <see langword="false"/>.</returns>
        private bool IsContourClosedForEmission(ReadOnlySpan<PointF> contourPoints, bool isDeclaredClosed)
        {
            // Fewer than three points cannot enclose area, so such contours always stroke open.
            if (contourPoints.Length < 3)
            {
                return false;
            }

            PointF first = this.TransformPoint(contourPoints[0]);
            PointF last = this.TransformPoint(contourPoints[^1]);

            if (isDeclaredClosed || first == last)
            {
                return true;
            }

            // Near-closure: a gap no wider than the stroke itself is visually sealed by the
            // stroke body, so emit a wrap join instead of two caps facing across a slit.
            Vector2 delta = first - last;
            float closeThreshold = MathF.Max(this.stroke.Width, 1E-3F);
            return delta.LengthSquared() <= closeThreshold * closeThreshold;
        }

        /// <summary>
        /// Applies the residual transform to one source point when one is present.
        /// </summary>
        /// <param name="point">The source point.</param>
        /// <returns>The transformed point.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private PointF TransformPoint(PointF point)
            => this.HasResidual ? PointF.Transform(point, this.Residual) : point;

        /// <summary>
        /// Processes one centerline contour.
        /// </summary>
        /// <param name="contourPoints">The contiguous contour points.</param>
        /// <param name="isClosed">Indicates whether the contour is closed.</param>
        /// <param name="contained">Indicates whether the stroked contour is fully contained within the interest.</param>
        private void ProcessContour(ReadOnlySpan<PointF> contourPoints, bool isClosed, bool contained)
        {
            using IMemoryOwner<StrokeContourSegment> rentedSegmentsOwner = this.Allocator.Allocate<StrokeContourSegment>(contourPoints.Length);
            Span<StrokeContourSegment> rentedSegments = rentedSegmentsOwner.Memory.Span;
            int segmentCount = this.BuildContourSegments(
                contourPoints,
                isClosed,
                rentedSegments,
                out int distinctPointCount,
                out PointF pointLike);

            if (segmentCount == 0)
            {
                this.EmitPointStrokeContour(pointLike, contained);
                return;
            }

            if (segmentCount == 1 || distinctPointCount == 2)
            {
                StrokeContourSegment segment = rentedSegments[0];
                this.EmitOpenSegmentStrokeContour(segment.Start, segment.End, contained);
                return;
            }

            if (isClosed)
            {
                this.EmitClosedStrokeContour(rentedSegments[..segmentCount], contained);
                return;
            }

            this.EmitOpenStrokeContour(rentedSegments[..segmentCount], contained);
        }

        /// <summary>
        /// Builds one contour-local stroke segment array while collapsing immediate duplicate points.
        /// </summary>
        /// <remarks>
        /// Exact duplicates are skipped outright and sub-1/64 px micro-segments are collapsed
        /// forward onto the last kept point so every surviving segment carries a well conditioned
        /// tangent for join math. Ported to encode time on the GPU path by
        /// <c>WebGPUSceneEncoder.EncodeStrokePath</c>; both must stay in lockstep.
        /// </remarks>
        /// <param name="contourPoints">The contiguous contour points.</param>
        /// <param name="isClosed">Indicates whether the contour is closed.</param>
        /// <param name="segments">The destination segment buffer.</param>
        /// <param name="distinctPointCount">Receives the number of distinct contour points.</param>
        /// <param name="pointLike">Receives the fallback point for degenerate contours.</param>
        /// <returns>The number of emitted contour segments.</returns>
        private int BuildContourSegments(
            ReadOnlySpan<PointF> contourPoints,
            bool isClosed,
            Span<StrokeContourSegment> segments,
            out int distinctPointCount,
            out PointF pointLike)
        {
            pointLike = default;
            distinctPointCount = 0;
            if (contourPoints.IsEmpty)
            {
                return 0;
            }

            Matrix4x4 residual = this.Residual;
            bool hasResidual = this.HasResidual;
            PointF firstPoint = hasResidual ? PointF.Transform(contourPoints[0], residual) : contourPoints[0];
            PointF previousPoint = firstPoint;
            pointLike = firstPoint;
            distinctPointCount = 1;
            int segmentCount = 0;

            for (int i = 1; i < contourPoints.Length; i++)
            {
                PointF point = hasResidual ? PointF.Transform(contourPoints[i], residual) : contourPoints[i];
                if (point == previousPoint)
                {
                    continue;
                }

                // When the candidate segment is rejected as a micro-segment, previousPoint stays
                // anchored on the last kept point so the run collapses forward; pointLike still
                // tracks the newest source point so a fully degenerate contour falls back to it.
                if (TryCreateStrokeContourSegment(previousPoint, point, out StrokeContourSegment segment))
                {
                    distinctPointCount++;
                    segments[segmentCount++] = segment;
                    previousPoint = point;
                }

                pointLike = point;
            }

            // A closed contour that repeats its first point as its last would otherwise count
            // the shared vertex twice.
            if (isClosed &&
                distinctPointCount > 1 &&
                previousPoint == firstPoint)
            {
                distinctPointCount--;
            }

            // Bridge back to the first point when a closed contour does not already end on it.
            // A sub-1/64 px gap fails TryCreateStrokeContourSegment and is bridged by the wrap
            // join instead, matching the GPU encoder.
            if (isClosed &&
                segmentCount > 1 &&
                previousPoint != firstPoint &&
                TryCreateStrokeContourSegment(previousPoint, firstPoint, out StrokeContourSegment closingSegment))
            {
                segments[segmentCount++] = closingSegment;
            }

            return segmentCount;
        }

        /// <summary>
        /// Creates one contour-local stroke segment descriptor.
        /// </summary>
        /// <param name="start">The segment start point.</param>
        /// <param name="end">The segment end point.</param>
        /// <param name="segment">Receives the segment descriptor.</param>
        /// <returns><see langword="true"/> when a non-degenerate segment exists.</returns>
        private static bool TryCreateStrokeContourSegment(PointF start, PointF end, out StrokeContourSegment segment)
        {
            // Segments shorter than 1/64 px cannot produce a numerically stable tangent, so
            // they are rejected here and collapsed forward by the caller.
            if (Vector2.DistanceSquared(start, end) <= StrokeMicroSegmentEpsilon * StrokeMicroSegmentEpsilon)
            {
                segment = default;
                return false;
            }

            if (!TryGetDirection(start, end, out Vector2 tangent, out float length))
            {
                segment = default;
                return false;
            }

            segment = new StrokeContourSegment(start, end, tangent, length);
            return true;
        }

        /// <summary>
        /// Gets the point bounds for one contour.
        /// </summary>
        /// <param name="contourPoints">The contiguous contour points.</param>
        /// <returns>The contour point bounds.</returns>
        private RectangleF GetPointBounds(ReadOnlySpan<PointF> contourPoints)
        {
            Matrix4x4 residual = this.Residual;
            bool hasResidual = this.HasResidual;
            PointF first = hasResidual ? PointF.Transform(contourPoints[0], residual) : contourPoints[0];
            float minX = first.X;
            float minY = first.Y;
            float maxX = minX;
            float maxY = minY;

            for (int i = 1; i < contourPoints.Length; i++)
            {
                PointF point = hasResidual ? PointF.Transform(contourPoints[i], residual) : contourPoints[i];
                minX = MathF.Min(minX, point.X);
                minY = MathF.Min(minY, point.Y);
                maxX = MathF.Max(maxX, point.X);
                maxY = MathF.Max(maxY, point.Y);
            }

            return RectangleF.FromLTRB(minX, minY, maxX, maxY);
        }

        /// <summary>
        /// Emits one stroked open segment.
        /// </summary>
        /// <param name="start">The segment start point.</param>
        /// <param name="end">The segment end point.</param>
        /// <param name="contained">Indicates whether the segment is fully contained within the interest.</param>
        private void EmitOpenSegmentStrokeContour(PointF start, PointF end, bool contained)
        {
            if (!TryGetDirection(start, end, out Vector2 tangent, out _))
            {
                this.EmitPointStrokeContour(start, contained);
                return;
            }

            float halfWidth = this.stroke.HalfWidth;
            Vector2 normal = GetStrokeOffsetNormal(tangent) * halfWidth;
            Vector2 extension = this.stroke.LineCap == LineCap.Square ? tangent * halfWidth : Vector2.Zero;
            Vector2 startVector = start;
            Vector2 endVector = end;
            PointF p0 = startVector + normal - extension;
            PointF p1 = endVector + normal + extension;
            PointF p2 = endVector - normal + extension;
            PointF p3 = startVector - normal - extension;

            this.EmitLine(p0, p1, contained);

            if (this.stroke.LineCap == LineCap.Round)
            {
                this.EmitDirectedArcContour(endVector, normal, -normal, contained);
            }
            else
            {
                this.EmitLine(p1, p2, contained);
            }

            this.EmitLine(p2, p3, contained);

            if (this.stroke.LineCap == LineCap.Round)
            {
                this.EmitDirectedArcContour(startVector, -normal, normal, contained);
            }
            else
            {
                this.EmitLine(p3, p0, contained);
            }
        }

        /// <summary>
        /// Emits one stroked open multi-segment contour from precomputed contour-local segments.
        /// </summary>
        /// <remarks>
        /// Two-pass outline: the forward pass walks the segments emitting one offset side and its
        /// joins followed by the end cap, then the reverse pass walks back emitting the opposite
        /// side followed by the start cap, producing a single closed outline. Join overshoots
        /// retrace shared offset lines in opposite directions, so they cancel in the signed
        /// winding sum under the non-zero fill rule.
        /// </remarks>
        /// <param name="segments">The precomputed contour-local segments.</param>
        /// <param name="contained">Indicates whether the contour is fully contained within the interest.</param>
        private void EmitOpenStrokeContour(ReadOnlySpan<StrokeContourSegment> segments, bool contained)
        {
            StrokeContourSegment startSegment = segments[0];
            StrokeContourSegment endSegment = segments[^1];
            float halfWidth = this.stroke.HalfWidth;
            Vector2 startNormal = startSegment.Normal * halfWidth;
            Vector2 endNormal = endSegment.Normal * halfWidth;
            Vector2 startExtension = this.stroke.LineCap == LineCap.Square ? startSegment.Tangent * halfWidth : Vector2.Zero;
            Vector2 endExtension = this.stroke.LineCap == LineCap.Square ? endSegment.Tangent * halfWidth : Vector2.Zero;
            Vector2 startPoint = startSegment.Start;
            Vector2 endPoint = endSegment.End;
            ContourState strokeContour = default;
            this.AppendContourPoint(ref strokeContour, startPoint + startNormal - startExtension, contained);

            for (int i = 1; i < segments.Length; i++)
            {
                StrokeContourSegment previousSegment = segments[i - 1];
                StrokeContourSegment nextSegment = segments[i];

                // Forward traversal: (v0, v1, v2) = (prev.Start, shared_vertex, next.End).
                this.AppendSideJoinContour(
                    ref strokeContour,
                    previousSegment.Start,
                    previousSegment.End,
                    nextSegment.End,
                    previousSegment.Length,
                    nextSegment.Length,
                    contained);
            }

            this.AppendContourPoint(ref strokeContour, endPoint + endNormal + endExtension, contained);

            if (this.stroke.LineCap == LineCap.Round)
            {
                this.AppendDirectedArcContour(ref strokeContour, endPoint, endNormal, -endNormal, contained);
            }
            else
            {
                this.AppendContourPoint(ref strokeContour, endPoint - endNormal + endExtension, contained);
            }

            for (int i = segments.Length - 1; i >= 1; i--)
            {
                StrokeContourSegment previousSegment = segments[i];
                StrokeContourSegment nextSegment = segments[i - 1];

                // Reverse traversal: vertex order reversed so PolygonStroker's Outline2
                // state machine lines up with the port below.
                this.AppendSideJoinContour(
                    ref strokeContour,
                    previousSegment.End,
                    previousSegment.Start,
                    nextSegment.Start,
                    previousSegment.Length,
                    nextSegment.Length,
                    contained);
            }

            this.AppendContourPoint(ref strokeContour, startPoint - startNormal - startExtension, contained);

            if (this.stroke.LineCap == LineCap.Round)
            {
                this.AppendDirectedArcContour(ref strokeContour, startPoint, -startNormal, startNormal, contained);
            }

            this.CloseContour(ref strokeContour, contained);
        }

        /// <summary>
        /// Emits the two stroked contours for a closed contour from precomputed contour-local segments.
        /// </summary>
        /// <remarks>
        /// A closed centerline strokes to a ring: the forward pass emits one boundary and the
        /// reversed pass emits the other with opposite winding, so the ring interior fills and
        /// the centerline hole cancels in the winding sum.
        /// </remarks>
        /// <param name="segments">The precomputed contour-local segments.</param>
        /// <param name="contained">Indicates whether the contour is fully contained within the interest.</param>
        private void EmitClosedStrokeContour(ReadOnlySpan<StrokeContourSegment> segments, bool contained)
        {
            ContourState leftContour = default;
            for (int i = 0; i < segments.Length; i++)
            {
                StrokeContourSegment previousSegment = i == 0 ? segments[^1] : segments[i - 1];
                StrokeContourSegment nextSegment = segments[i];

                this.AppendSideJoinContour(
                    ref leftContour,
                    previousSegment.Start,
                    nextSegment.Start,
                    nextSegment.End,
                    previousSegment.Length,
                    nextSegment.Length,
                    contained);
            }

            this.CloseContour(ref leftContour, contained);

            ContourState reversedContour = default;
            for (int i = segments.Length - 1; i >= 0; i--)
            {
                StrokeContourSegment previousSegment = segments[i];
                StrokeContourSegment nextSegment = i == 0 ? segments[^1] : segments[i - 1];

                this.AppendSideJoinContour(
                    ref reversedContour,
                    previousSegment.End,
                    previousSegment.Start,
                    nextSegment.Start,
                    previousSegment.Length,
                    nextSegment.Length,
                    contained);
            }

            this.CloseContour(ref reversedContour, contained);
        }

        /// <summary>
        /// Emits a point-like stroke as a cap contour.
        /// </summary>
        /// <param name="point">The point-like stroke location.</param>
        /// <param name="contained">Indicates whether the contour is fully contained within the interest.</param>
        private void EmitPointStrokeContour(PointF point, bool contained)
        {
            Vector2 center = point;
            float halfWidth = this.stroke.HalfWidth;
            if (this.stroke.LineCap == LineCap.Round)
            {
                // A round-capped point is a full circle emitted as two half arcs.
                Vector2 startOffset = new(halfWidth, 0F);
                this.EmitDirectedArcContour(center, startOffset, -startOffset, contained);
                this.EmitDirectedArcContour(center, -startOffset, startOffset, contained);
                return;
            }

            // Butt and square caps have no tangent to orient by, so the footprint degenerates
            // to an axis-aligned halfWidth square around the point.
            PointF p0 = center + new Vector2(-halfWidth, -halfWidth);
            PointF p1 = center + new Vector2(halfWidth, -halfWidth);
            PointF p2 = center + new Vector2(halfWidth, halfWidth);
            PointF p3 = center + new Vector2(-halfWidth, halfWidth);
            this.EmitLine(p0, p1, contained);
            this.EmitLine(p1, p2, contained);
            this.EmitLine(p2, p3, contained);
            this.EmitLine(p3, p0, contained);
        }

        /// <summary>
        /// Emits one round cap or join arc directly into the retained line storage.
        /// </summary>
        /// <param name="center">The arc center.</param>
        /// <param name="fromOffset">The start offset from the center.</param>
        /// <param name="toOffset">The end offset from the center.</param>
        /// <param name="contained">Indicates whether the arc is fully contained within the interest.</param>
        private void EmitDirectedArcContour(
            Vector2 center,
            Vector2 fromOffset,
            Vector2 toOffset,
            bool contained)
        {
            if (fromOffset == Vector2.Zero || toOffset == Vector2.Zero)
            {
                this.EmitLine(center + fromOffset, center + toOffset, contained);
                return;
            }

            float radius = fromOffset.Length();
            if (radius <= StrokeDirectionEpsilon)
            {
                this.EmitLine(center + fromOffset, center + toOffset, contained);
                return;
            }

            double startAngle = Math.Atan2(fromOffset.Y, fromOffset.X);
            double endAngle = Math.Atan2(toOffset.Y, toOffset.X);
            double sweep = NormalizePositiveAngle(endAngle - startAngle);
            int subdivisionCount = GetArcSubdivisionCount(radius, sweep, this.stroke.ArcDetailScale);
            double step = sweep / (subdivisionCount + 1);

            PointF previousPoint = center + fromOffset;
            for (int i = 1; i <= subdivisionCount; i++)
            {
                float angle = (float)(startAngle + (step * i));
                PointF point = center + new Vector2(MathF.Cos(angle) * radius, MathF.Sin(angle) * radius);
                this.EmitLine(previousPoint, point, contained);
                previousPoint = point;
            }

            this.EmitLine(previousPoint, center + toOffset, contained);
        }

        /// <summary>
        /// Appends a contour arc directly to the active stroke contour. Ported to the GPU as
        /// <c>stroke_chain_arc</c> in <c>path_lowering.wgsl</c>; changes here must be mirrored there.
        /// </summary>
        /// <param name="contour">The active contour state.</param>
        /// <param name="center">The arc center.</param>
        /// <param name="fromOffset">The start offset from the center.</param>
        /// <param name="toOffset">The end offset from the center.</param>
        /// <param name="contained">Indicates whether the arc is fully contained within the interest.</param>
        private void AppendDirectedArcContour(
            ref ContourState contour,
            Vector2 center,
            Vector2 fromOffset,
            Vector2 toOffset,
            bool contained)
        {
            if (fromOffset == Vector2.Zero || toOffset == Vector2.Zero)
            {
                this.AppendContourPoint(ref contour, center + toOffset, contained);
                return;
            }

            float radius = fromOffset.Length();
            if (radius <= StrokeDirectionEpsilon)
            {
                this.AppendContourPoint(ref contour, center + toOffset, contained);
                return;
            }

            double startAngle = Math.Atan2(fromOffset.Y, fromOffset.X);
            double endAngle = Math.Atan2(toOffset.Y, toOffset.X);
            double sweep = NormalizePositiveAngle(endAngle - startAngle);
            int subdivisionCount = GetArcSubdivisionCount(radius, sweep, this.stroke.ArcDetailScale);
            double step = sweep / (subdivisionCount + 1);

            for (int i = 1; i <= subdivisionCount; i++)
            {
                float angle = (float)(startAngle + (step * i));
                this.AppendContourPoint(
                    ref contour,
                    center + new Vector2(MathF.Cos(angle) * radius, MathF.Sin(angle) * radius),
                    contained);
            }

            this.AppendContourPoint(ref contour, center + toOffset, contained);
        }

        /// <summary>
        /// Appends one side join point sequence directly to the active stroke contour.
        /// </summary>
        /// <remarks>
        /// Direct port of <c>PolygonStroker.CalcJoin</c> so the rasterizer emits the same
        /// join geometry as the reference CPU stroker. Each side of the outline calls this
        /// once per source vertex; the reverse side reverses the vertex order exactly
        /// like PolygonStroker's Outline2 state. Ported to the GPU as <c>stroke_side_join</c>
        /// in <c>path_lowering.wgsl</c>; changes here must be mirrored there.
        /// </remarks>
        /// <param name="contour">The active contour state.</param>
        /// <param name="v0">Previous source vertex in the emission's traversal order.</param>
        /// <param name="v1">Current source vertex (the corner).</param>
        /// <param name="v2">Next source vertex in the emission's traversal order.</param>
        /// <param name="len1">Length of segment v0-v1.</param>
        /// <param name="len2">Length of segment v1-v2.</param>
        /// <param name="contained">Indicates whether the join is fully contained within the interest.</param>
        private void AppendSideJoinContour(
            ref ContourState contour,
            Vector2 v0,
            Vector2 v1,
            Vector2 v2,
            float len1,
            float len2,
            bool contained)
        {
            float eps = StrokeDirectionEpsilon;
            float halfWidth = this.stroke.HalfWidth;
            float widthAbs = halfWidth;
            float strokeWidth = halfWidth;

            if (len1 < eps || len2 < eps)
            {
                // Degenerate neighborhood: fall back to best available segment direction.
                float l1 = len1 >= eps ? len1 : len2;
                float l2 = len2 >= eps ? len2 : len1;
                float invL1 = strokeWidth / l1;
                float invL2 = strokeWidth / l2;

                Vector2 seg1 = v1 - v0;
                Vector2 seg2 = v2 - v1;

                float offX1 = seg1.Y * invL1;
                float offY1 = seg1.X * invL1;
                float offX2 = seg2.Y * invL2;
                float offY2 = seg2.X * invL2;

                this.AppendContourPoint(ref contour, new Vector2(v1.X + offX1, v1.Y - offY1), contained);
                this.AppendContourPoint(ref contour, new Vector2(v1.X + offX2, v1.Y - offY2), contained);
                return;
            }

            Vector2 segForward = v1 - v0;
            Vector2 segNext = v2 - v1;
            float invLen1 = strokeWidth / len1;
            float invLen2 = strokeWidth / len2;
            float dx1 = segForward.Y * invLen1;
            float dy1 = segForward.X * invLen1;
            float dx2 = segNext.Y * invLen2;
            float dy2 = segNext.X * invLen2;

            float cp = Cross(segNext, segForward);

            if (MathF.Abs(cp) > float.Epsilon && cp > 0F)
            {
                // Inner corner: always miter to the offset-line intersection, reverting past a
                // limit derived from the local segment lengths. The reverted points overshoot
                // along the shared offset lines and are retraced by the opposite pass, so the
                // excess cancels in the signed winding sum under the non-zero fill rule.
                float limit = MathF.Min(len1, len2) / widthAbs;
                if (limit < 1.01F)
                {
                    limit = 1.01F;
                }

                this.CalcMiter(ref contour, v0, v1, v2, dx1, dy1, dx2, dy2, LineJoin.MiterRevert, limit, 0F, contained);
                return;
            }

            // Outer corner.
            Vector2 averageOffset = new Vector2(dx1 + dx2, dy1 + dy2) * 0.5F;
            float bevelDistance = averageOffset.Length();

            // Nearly straight corner: when the round/bevel join would deviate from a straight
            // offset edge by less than halfWidth / 1024 (scaled by the arc detail), a single
            // offset-line intersection point is indistinguishable from the full join.
            float widthEps = widthAbs / 1024F;
            if ((this.stroke.LineJoin is LineJoin.Round or LineJoin.Bevel) &&
                ((float)this.stroke.ArcDetailScale * (widthAbs - bevelDistance)) < widthEps)
            {
                Vector2 outerOffset1 = new(dx1, -dy1);
                Vector2 outerOffset2 = new(dx2, -dy2);
                if (TryCalcIntersection(v0 + outerOffset1, v1 + outerOffset1, v1 + outerOffset2, v2 + outerOffset2, out Vector2 intersection))
                {
                    this.AppendContourPoint(ref contour, intersection, contained);
                }
                else
                {
                    this.AppendContourPoint(ref contour, new Vector2(v1.X + dx1, v1.Y - dy1), contained);
                }

                return;
            }

            switch (this.stroke.LineJoin)
            {
                case LineJoin.Miter:
                case LineJoin.MiterRevert:
                case LineJoin.MiterRound:
                    this.CalcMiter(ref contour, v0, v1, v2, dx1, dy1, dx2, dy2, this.stroke.LineJoin, (float)this.stroke.MiterLimit, bevelDistance, contained);
                    break;

                case LineJoin.Round:
                    this.CalcArc(ref contour, v1.X, v1.Y, dx1, -dy1, dx2, -dy2, contained);
                    break;

                default:
                    this.AppendContourPoint(ref contour, new Vector2(v1.X + dx1, v1.Y - dy1), contained);
                    this.AppendContourPoint(ref contour, new Vector2(v1.X + dx2, v1.Y - dy2), contained);
                    break;
            }
        }

        /// <summary>
        /// Direct port of <c>PolygonStroker.CalcMiter</c>. Emits the miter apex (or the
        /// configured overflow fallback) at the join vertex. Ported to the GPU as
        /// <c>stroke_calc_miter</c> in <c>path_lowering.wgsl</c>; changes here must be mirrored there.
        /// </summary>
        /// <param name="contour">The active contour state.</param>
        /// <param name="v0">Previous source vertex in the emission's traversal order.</param>
        /// <param name="v1">Current source vertex (the corner).</param>
        /// <param name="v2">Next source vertex in the emission's traversal order.</param>
        /// <param name="dx1">The X component of the scaled offset for segment v0-v1; the offset vector is (dx1, -dy1).</param>
        /// <param name="dy1">The negated Y component of the scaled offset for segment v0-v1.</param>
        /// <param name="dx2">The X component of the scaled offset for segment v1-v2; the offset vector is (dx2, -dy2).</param>
        /// <param name="dy2">The negated Y component of the scaled offset for segment v1-v2.</param>
        /// <param name="lineJoin">The overflow behavior applied when the miter limit is exceeded.</param>
        /// <param name="miterLimit">The miter limit expressed in half-width units.</param>
        /// <param name="bevelDistance">The distance from the corner to the bevel midpoint, used to interpolate the clipped miter.</param>
        /// <param name="contained">Indicates whether the join is fully contained within the interest.</param>
        private void CalcMiter(
            ref ContourState contour,
            Vector2 v0,
            Vector2 v1,
            Vector2 v2,
            float dx1,
            float dy1,
            float dx2,
            float dy2,
            LineJoin lineJoin,
            float miterLimit,
            float bevelDistance,
            bool contained)
        {
            Vector2 p0 = v0;
            Vector2 p1 = v1;
            Vector2 p2 = v2;
            Vector2 offset1 = new(dx1, -dy1);
            Vector2 offset2 = new(dx2, -dy2);

            float xi = v1.X;
            float yi = v1.Y;
            float intersectionDistance = 1F;
            float limit = this.stroke.HalfWidth * miterLimit;
            bool miterLimitExceeded = true;
            bool intersectionFailed = true;

            if (TryCalcIntersection(p0 + offset1, p1 + offset1, p1 + offset2, p2 + offset2, out Vector2 intersection))
            {
                xi = intersection.X;
                yi = intersection.Y;
                intersectionDistance = Vector2.Distance(p1, intersection);
                if (intersectionDistance <= limit)
                {
                    this.AppendContourPoint(ref contour, intersection, contained);
                    miterLimitExceeded = false;
                }

                intersectionFailed = false;
            }
            else
            {
                // Parallel/near-parallel fallback: probe a candidate offset point.
                Vector2 probe = new(v1.X + dx1, v1.Y - dy1);
                if ((CrossProduct(v0, v1, probe) < 0F) == (CrossProduct(v1, v2, probe) < 0F))
                {
                    this.AppendContourPoint(ref contour, probe, contained);
                    miterLimitExceeded = false;
                }
            }

            if (!miterLimitExceeded)
            {
                return;
            }

            switch (lineJoin)
            {
                case LineJoin.MiterRevert:
                    this.AppendContourPoint(ref contour, new Vector2(v1.X + dx1, v1.Y - dy1), contained);
                    this.AppendContourPoint(ref contour, new Vector2(v1.X + dx2, v1.Y - dy2), contained);
                    break;

                case LineJoin.MiterRound:
                    this.CalcArc(ref contour, v1.X, v1.Y, dx1, -dy1, dx2, -dy2, contained);
                    break;

                default:
                    if (intersectionFailed)
                    {
                        // No reliable apex: project a clipped bevel using local tangent/perpendicular vectors.
                        this.AppendContourPoint(
                            ref contour,
                            new Vector2(v1.X + dx1 + (dy1 * miterLimit), v1.Y - dy1 + (dx1 * miterLimit)),
                            contained);
                        this.AppendContourPoint(
                            ref contour,
                            new Vector2(v1.X + dx2 - (dy2 * miterLimit), v1.Y - dy2 - (dx2 * miterLimit)),
                            contained);
                    }
                    else
                    {
                        float x1 = v1.X + dx1;
                        float y1 = v1.Y - dy1;
                        float x2 = v1.X + dx2;
                        float y2 = v1.Y - dy2;
                        float ratio = (limit - bevelDistance) / (intersectionDistance - bevelDistance);
                        this.AppendContourPoint(ref contour, new Vector2(x1 + ((xi - x1) * ratio), y1 + ((yi - y1) * ratio)), contained);
                        this.AppendContourPoint(ref contour, new Vector2(x2 + ((xi - x2) * ratio), y2 + ((yi - y2) * ratio)), contained);
                    }

                    break;
            }
        }

        /// <summary>
        /// Direct port of <c>PolygonStroker.CalcArc</c>. Emits intermediate arc vertices
        /// around a join center between two offset vectors. Ported to the GPU as
        /// <c>stroke_calc_arc</c> in <c>path_lowering.wgsl</c>; changes here must be mirrored there.
        /// </summary>
        /// <param name="contour">The active contour state.</param>
        /// <param name="x">The join center X coordinate.</param>
        /// <param name="y">The join center Y coordinate.</param>
        /// <param name="dx1">The X component of the arc start offset from the center.</param>
        /// <param name="dy1">The Y component of the arc start offset from the center.</param>
        /// <param name="dx2">The X component of the arc end offset from the center.</param>
        /// <param name="dy2">The Y component of the arc end offset from the center.</param>
        /// <param name="contained">Indicates whether the arc is fully contained within the interest.</param>
        private void CalcArc(
            ref ContourState contour,
            float x,
            float y,
            float dx1,
            float dy1,
            float dx2,
            float dy2,
            bool contained)
        {
            float strokeWidth = this.stroke.HalfWidth;
            double a1 = Math.Atan2(dy1, dx1);
            double a2 = Math.Atan2(dy2, dx2);

            // AGG's chordal-error step: da is the largest angular step whose chord stays within
            // 0.125 / arc-detail-scale pixels of the true arc.
            double widthAbs = strokeWidth;
            double da = Math.Acos(widthAbs / (widthAbs + (0.125D / this.stroke.ArcDetailScale))) * 2D;
            this.AppendContourPoint(ref contour, new Vector2(x + dx1, y + dy1), contained);

            // Wrap the end angle forward so the sweep is always positive.
            if (a1 > a2)
            {
                a2 += Math.PI * 2D;
            }

            // Distribute the sweep evenly over n interior points so the last step lands exactly
            // on the end offset.
            int n = (int)((a2 - a1) / da);
            da = (a2 - a1) / (n + 1);
            a1 += da;
            for (int i = 0; i < n; i++)
            {
                this.AppendContourPoint(
                    ref contour,
                    new Vector2((float)(x + (Math.Cos(a1) * strokeWidth)), (float)(y + (Math.Sin(a1) * strokeWidth))),
                    contained);
                a1 += da;
            }

            this.AppendContourPoint(ref contour, new Vector2(x + dx2, y + dy2), contained);
        }

        /// <summary>
        /// Signed area of triangle (a, b, point), matching <c>PolygonStroker.CrossProduct</c>;
        /// used as a side-of-line test for <paramref name="point"/> against the line through
        /// <paramref name="a"/> and <paramref name="b"/>. Ported to the GPU as
        /// <c>stroke_cross_product</c> in <c>path_lowering.wgsl</c>.
        /// </summary>
        /// <param name="a">The first line point.</param>
        /// <param name="b">The second line point.</param>
        /// <param name="point">The point to test.</param>
        /// <returns>The signed triangle area scalar.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float CrossProduct(Vector2 a, Vector2 b, Vector2 point)
            => ((point.X - b.X) * (b.Y - a.Y)) - ((point.Y - b.Y) * (b.X - a.X));

        /// <summary>
        /// Intersects two infinite lines defined by point pairs (a, b) and (c, d),
        /// matching <c>PolygonStroker.TryCalcIntersection</c>. Ported to the GPU as
        /// <c>stroke_try_intersect</c> in <c>path_lowering.wgsl</c>.
        /// </summary>
        /// <param name="a">The first point on the first line.</param>
        /// <param name="b">The second point on the first line.</param>
        /// <param name="c">The first point on the second line.</param>
        /// <param name="d">The second point on the second line.</param>
        /// <param name="intersection">Receives the intersection when the lines are not near-parallel.</param>
        /// <returns><see langword="true"/> when the lines intersect; otherwise <see langword="false"/>.</returns>
        private static bool TryCalcIntersection(Vector2 a, Vector2 b, Vector2 c, Vector2 d, out Vector2 intersection)
        {
            const float eps = 1e-7F;
            Vector2 ab = b - a;
            Vector2 cd = d - c;
            float denominator = Cross(ab, cd);
            if (MathF.Abs(denominator) < eps)
            {
                intersection = default;
                return false;
            }

            float t = Cross(c - a, cd) / denominator;
            intersection = a + (ab * t);
            return true;
        }

        /// <summary>
        /// Appends one point to the active contour, emitting a line from the previously appended
        /// point. Ported to the GPU as <c>stroke_chain_point</c> in <c>path_lowering.wgsl</c>; changes
        /// here must be mirrored there.
        /// </summary>
        /// <param name="state">The active contour state.</param>
        /// <param name="point">The point to append.</param>
        /// <param name="contained">Indicates whether the contour is fully contained within the interest.</param>
        private void AppendContourPoint(ref ContourState state, PointF point, bool contained)
        {
            if (!state.HasPoint)
            {
                state.HasPoint = true;
                state.FirstPoint = point;
                state.PreviousPoint = point;
                return;
            }

            // Coincident points are skipped so the chain never emits zero-length lines.
            if (state.PreviousPoint == point)
            {
                return;
            }

            this.EmitLine(state.PreviousPoint, point, contained);
            state.PreviousPoint = point;
        }

        /// <summary>
        /// Closes the active contour.
        /// </summary>
        /// <param name="state">The active contour state.</param>
        /// <param name="contained">Indicates whether the contour is fully contained within the interest.</param>
        private void CloseContour(ref ContourState state, bool contained)
        {
            if (!state.HasPoint || state.PreviousPoint == state.FirstPoint)
            {
                return;
            }

            this.EmitLine(state.PreviousPoint, state.FirstPoint, contained);
            state.PreviousPoint = state.FirstPoint;
        }

        /// <summary>
        /// Emits one stroked boundary edge into retained line storage.
        /// </summary>
        /// <param name="start">The edge start point.</param>
        /// <param name="end">The edge end point.</param>
        /// <param name="contained">Indicates whether the edge is fully contained within the interest.</param>
        private void EmitLine(PointF start, PointF end, bool contained)
        {
            // The stroker creates new outline edges, so they have no profiles from the source
            // geometry. Sentinel tags keep centre-free stroke intervals visible.
            if (contained)
            {
                this.AddContainedLineF24Dot8(
                    FloatToFixed24Dot8((start.X + this.TranslateX) - this.MinX),
                    FloatToFixed24Dot8((start.Y + this.TranslateY) - this.MinY),
                    FloatToFixed24Dot8((end.X + this.TranslateX) - this.MinX),
                    FloatToFixed24Dot8((end.Y + this.TranslateY) - this.MinY),
                    LinearGeometryProfiles.SentinelTag);
                return;
            }

            this.AddUncontainedLine(
                (start.X + this.TranslateX) - this.MinX,
                (start.Y + this.TranslateY) - this.MinY,
                (end.X + this.TranslateX) - this.MinX,
                (end.Y + this.TranslateY) - this.MinY,
                LinearGeometryProfiles.SentinelTag);
        }

        /// <summary>
        /// Returns the stroke offset normal matching PolygonStroker's dx/dy convention.
        /// </summary>
        /// <param name="tangent">The normalized segment tangent.</param>
        /// <returns>The stroke-side offset normal.</returns>
        private static Vector2 GetStrokeOffsetNormal(Vector2 tangent) => new(tangent.Y, -tangent.X);

        /// <summary>
        /// One preprocessed centerline segment with its cached tangent, offset normal, and length.
        /// </summary>
        private readonly struct StrokeContourSegment
        {
            /// <summary>
            /// Initializes a new instance of the <see cref="StrokeContourSegment"/> struct.
            /// </summary>
            /// <param name="start">The segment start point.</param>
            /// <param name="end">The segment end point.</param>
            /// <param name="tangent">The normalized segment tangent.</param>
            /// <param name="length">The segment length.</param>
            public StrokeContourSegment(PointF start, PointF end, Vector2 tangent, float length)
            {
                this.Start = start;
                this.End = end;
                this.Tangent = tangent;
                this.Normal = GetStrokeOffsetNormal(tangent);
                this.Length = length;
            }

            /// <summary>
            /// Gets the segment start point.
            /// </summary>
            public PointF Start { get; }

            /// <summary>
            /// Gets the segment end point.
            /// </summary>
            public PointF End { get; }

            /// <summary>
            /// Gets the normalized segment tangent.
            /// </summary>
            public Vector2 Tangent { get; }

            /// <summary>
            /// Gets the unit offset normal following PolygonStroker's (tangent.Y, -tangent.X) convention.
            /// </summary>
            public Vector2 Normal { get; }

            /// <summary>
            /// Gets the segment length.
            /// </summary>
            public float Length { get; }
        }

        /// <summary>
        /// Mutable chain state for one emitted stroke outline contour.
        /// </summary>
        private struct ContourState
        {
            /// <summary>
            /// Indicates whether any point has been appended to the contour yet.
            /// </summary>
            public bool HasPoint;

            /// <summary>
            /// The first appended point, used to close the contour.
            /// </summary>
            public PointF FirstPoint;

            /// <summary>
            /// The most recently appended point; the start of the next emitted line.
            /// </summary>
            public PointF PreviousPoint;
        }
    }

    /// <summary>
    /// Stroke linearizer that finalizes retained lines into the 32-bit-X encoding.
    /// </summary>
    private sealed class StrokeLinearizerX32Y16 : StrokeLinearizer<LineArrayX32Y16>
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="StrokeLinearizerX32Y16"/> class.
        /// </summary>
        /// <param name="geometry">The stroked centerline geometry.</param>
        /// <param name="residual">The residual transform applied to each source point during emission.</param>
        /// <param name="stroke">The stroke style.</param>
        /// <param name="translateX">The destination-space X translation applied at composition time.</param>
        /// <param name="translateY">The destination-space Y translation applied at composition time.</param>
        /// <param name="minX">The minimum destination X bound after clipping.</param>
        /// <param name="minY">The minimum destination Y bound after clipping.</param>
        /// <param name="width">The visible destination width in pixels.</param>
        /// <param name="height">The visible destination height in pixels.</param>
        /// <param name="firstBandIndex">The first retained row-band index.</param>
        /// <param name="rowBandCount">The retained row-band count.</param>
        /// <param name="keepHorizontalLines">Whether to retain horizontal outline edges for aliased thin-feature recovery between row centres.</param>
        /// <param name="allocator">The allocator used for retained start-cover storage.</param>
        public StrokeLinearizerX32Y16(
            LinearGeometry geometry,
            Matrix4x4 residual,
            StrokeStyle stroke,
            int translateX,
            int translateY,
            int minX,
            int minY,
            int width,
            int height,
            int firstBandIndex,
            int rowBandCount,
            bool keepHorizontalLines,
            MemoryAllocator allocator)
            : base(geometry, residual, stroke, translateX, translateY, minX, minY, width, height, firstBandIndex, rowBandCount, keepHorizontalLines, allocator)
            => this.FinalLines = new LineArrayX32Y16Block?[rowBandCount];

        /// <summary>
        /// Gets the finalized retained line blocks for each row band.
        /// </summary>
        public LineArrayX32Y16Block?[] FinalLines { get; }

        /// <inheritdoc />
        protected override LineArrayX32Y16 CreateLineArray() => new(false);

        /// <inheritdoc />
        protected override void AppendLine(int rowIndex, int x0, int y0, int x1, int y1, uint tag)
            => this.GetOrCreateLineArray(rowIndex).AppendLine(x0, y0, x1, y1, tag);

        /// <inheritdoc />
        protected override void FinalizeLines()
        {
            for (int i = 0; i < this.RowBandCount; i++)
            {
                LineArrayX32Y16? lineArray = this.LineArrays[i];
                this.FinalLines[i] = lineArray?.GetFrontBlock();
                this.FirstBlockLineCounts[i] = lineArray?.GetFrontBlockLineCount() ?? 0;
            }
        }

        /// <summary>
        /// Executes the retained stroke linearization pass and returns the finalized payload.
        /// </summary>
        /// <param name="result">The finalized retained raster data.</param>
        /// <returns><see langword="true"/> when retained coverage was produced; otherwise <see langword="false"/>.</returns>
        public bool TryProcess(out LinearizedRasterData<LineArrayX32Y16Block> result)
        {
            if (!this.ProcessCore())
            {
                result = null!;
                return false;
            }

            result = new LinearizedRasterData<LineArrayX32Y16Block>(
                this.Geometry,
                new TileBounds(this.MinX, this.FirstBandIndex, this.Width, this.RowBandCount),
                this.FinalLines,
                this.FirstBlockLineCounts,
                this.StartCoverTable,
                this.Profiles,
                this.ProfileTranslateX,
                this.ProfileTranslateY);

            return true;
        }
    }

    /// <summary>
    /// Stroke linearizer that finalizes retained lines into the packed 16-bit-X encoding.
    /// </summary>
    private sealed class StrokeLinearizerX16Y16 : StrokeLinearizer<LineArrayX16Y16>
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="StrokeLinearizerX16Y16"/> class.
        /// </summary>
        /// <param name="geometry">The stroked centerline geometry.</param>
        /// <param name="residual">The residual transform applied to each source point during emission.</param>
        /// <param name="stroke">The stroke style.</param>
        /// <param name="translateX">The destination-space X translation applied at composition time.</param>
        /// <param name="translateY">The destination-space Y translation applied at composition time.</param>
        /// <param name="minX">The minimum destination X bound after clipping.</param>
        /// <param name="minY">The minimum destination Y bound after clipping.</param>
        /// <param name="width">The visible destination width in pixels.</param>
        /// <param name="height">The visible destination height in pixels.</param>
        /// <param name="firstBandIndex">The first retained row-band index.</param>
        /// <param name="rowBandCount">The retained row-band count.</param>
        /// <param name="keepHorizontalLines">Whether to retain horizontal outline edges for aliased thin-feature recovery between row centres.</param>
        /// <param name="allocator">The allocator used for retained start-cover storage.</param>
        public StrokeLinearizerX16Y16(
            LinearGeometry geometry,
            Matrix4x4 residual,
            StrokeStyle stroke,
            int translateX,
            int translateY,
            int minX,
            int minY,
            int width,
            int height,
            int firstBandIndex,
            int rowBandCount,
            bool keepHorizontalLines,
            MemoryAllocator allocator)
            : base(geometry, residual, stroke, translateX, translateY, minX, minY, width, height, firstBandIndex, rowBandCount, keepHorizontalLines, allocator)
            => this.FinalLines = new LineArrayX16Y16Block?[rowBandCount];

        /// <summary>
        /// Gets the finalized retained line blocks for each row band.
        /// </summary>
        public LineArrayX16Y16Block?[] FinalLines { get; }

        /// <inheritdoc />
        protected override LineArrayX16Y16 CreateLineArray() => new(false);

        /// <inheritdoc />
        protected override void AppendLine(int rowIndex, int x0, int y0, int x1, int y1, uint tag)
            => this.GetOrCreateLineArray(rowIndex).AppendLine(x0, y0, x1, y1, tag);

        /// <inheritdoc />
        protected override void FinalizeLines()
        {
            for (int i = 0; i < this.RowBandCount; i++)
            {
                LineArrayX16Y16? lineArray = this.LineArrays[i];
                this.FinalLines[i] = lineArray?.GetFrontBlock();
                this.FirstBlockLineCounts[i] = lineArray?.GetFrontBlockLineCount() ?? 0;
            }
        }

        /// <summary>
        /// Executes the retained stroke linearization pass and returns the finalized payload.
        /// </summary>
        /// <param name="result">The finalized retained raster data.</param>
        /// <returns><see langword="true"/> when retained coverage was produced; otherwise <see langword="false"/>.</returns>
        public bool TryProcess(out LinearizedRasterData<LineArrayX16Y16Block> result)
        {
            if (!this.ProcessCore())
            {
                result = null!;
                return false;
            }

            result = new LinearizedRasterData<LineArrayX16Y16Block>(
                this.Geometry,
                new TileBounds(this.MinX, this.FirstBandIndex, this.Width, this.RowBandCount),
                this.FinalLines,
                this.FirstBlockLineCounts,
                this.StartCoverTable,
                this.Profiles,
                this.ProfileTranslateX,
                this.ProfileTranslateY);

            return true;
        }
    }
}
