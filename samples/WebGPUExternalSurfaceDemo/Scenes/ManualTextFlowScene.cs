// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;
using SixLabors.Fonts;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;
using Brush = SixLabors.ImageSharp.Drawing.Processing.Brush;
using Brushes = SixLabors.ImageSharp.Drawing.Processing.Brushes;
using Color = SixLabors.ImageSharp.Color;
using Font = SixLabors.Fonts.Font;
using FontFamily = SixLabors.Fonts.FontFamily;
using FontStyle = SixLabors.Fonts.FontStyle;
using Pen = SixLabors.ImageSharp.Drawing.Processing.Pen;
using Pens = SixLabors.ImageSharp.Drawing.Processing.Pens;
using PointF = SixLabors.ImageSharp.PointF;
using Size = SixLabors.ImageSharp.Size;
using SizeF = SixLabors.ImageSharp.SizeF;
using SystemFonts = SixLabors.Fonts.SystemFonts;

namespace WebGPUExternalSurfaceDemo.Scenes;

/// <summary>
/// Interactive manual text-flow scene. Moving the mouse moves the selected obstacle, and the text is
/// reflowed line-by-line into the remaining horizontal slots.
/// </summary>
/// <remarks>
/// This scene is intentionally written as sample code rather than a hidden helper API. It shows the whole pipeline:
/// prepare a reusable <see cref="TextBlock"/>, convert a closed obstacle <see cref="IPath"/> to linear geometry,
/// split each row into unobstructed slots, then ask <see cref="LineLayoutEnumerator"/> for one line at the width
/// of the current slot.
/// </remarks>
internal sealed class ManualTextFlowScene : RenderScene
{
    private const string Text =
        "Lorem ipsum dolor sit amet, consectetur adipiscing elit. Integer feugiat, arcu vitae " +
        "pulvinar volutpat, neque mauris finibus massa, vitae tincidunt justo lectus non lectus. " +
        "Suspendisse potenti. Praesent luctus, mi vitae sollicitudin mattis, ipsum lorem sodales " +
        "tellus, sit amet faucibus turpis nunc nec libero. Duis dignissim, dolor sed blandit " +
        "ultricies, lorem augue tempor velit, sed pellentesque neque odio sed lacus. " +
        "Curabitur venenatis, sem quis dignissim fringilla, leo justo congue magna, in congue " +
        "erat dolor non velit. Vestibulum ante ipsum primis in faucibus orci luctus et ultrices " +
        "posuere cubilia curae; Morbi vehicula, neque ac aliquam feugiat, augue arcu tincidunt " +
        "urna, at congue erat justo vitae ipsum. Maecenas in lorem nec odio lacinia aliquet. " +
        "Aliquam erat volutpat. Sed non neque sed risus iaculis hendrerit. Etiam fermentum " +
        "nibh nec sapien dictum, non convallis lectus lacinia. Donec pharetra, quam et faucibus " +
        "tempor, massa mi cursus arcu, id cursus risus neque in urna. Nam laoreet lectus nec " +
        "sem luctus, id rhoncus justo euismod. Vivamus dictum, tortor sed feugiat volutpat, " +
        "erat nibh convallis tortor, vitae tempus nibh massa non magna. Nulla facilisi. " +
        "Phasellus molestie, mauris vel faucibus commodo, turpis ligula facilisis mauris, " +
        "ac bibendum neque sem et metus. Pellentesque habitant morbi tristique senectus et " +
        "netus et malesuada fames ac turpis egestas. Cras nec tortor at velit interdum " +
        "porttitor. Aenean facilisis, metus sed dictum efficitur, neque libero porta eros, " +
        "vitae suscipit urna arcu vitae arcu. Fusce vel mauris sed mauris ultricies " +
        "malesuada. Donec ut sem eu nunc vulputate porttitor at vitae augue. " +
        "Morbi luctus justo vitae lectus tincidunt, ac feugiat lorem varius. Integer posuere " +
        "ornare arcu, in pretium lacus gravida non. Sed vitae neque non erat posuere " +
        "placerat. Curabitur blandit, sapien at vehicula facilisis, turpis sem " +
        "hendrerit ipsum, a tincidunt lacus risus at neque. Vivamus feugiat erat " +
        "sit amet nibh luctus. Quisque sed lectus vitae ligula aliquet convallis. " +
        "Praesent viverra ipsum at dui ultrices, a facilisis turpis fermentum. " +
        "Vestibulum ante ipsum primis in faucibus orci luctus et ultrices posuere " +
        "cubilia curae; Integer consequat, lorem at tincidunt pharetra, erat " +
        "sapien porta enim, eget dignissim elit nisi ac turpis.";

    private static readonly Color BackgroundColor = Color.ParseHex("#F7F4EC");
    private static readonly Color PageColor = Color.White;
    private static readonly Color PageOutlineColor = Color.ParseHex("#324154");
    private static readonly Color SlotColor = Color.SteelBlue.WithAlpha(.24F);
    private static readonly Color CircleColor = Color.SteelBlue.WithAlpha(.18F);
    private static readonly Color TextColor = Color.ParseHex("#111827");
    private static readonly Brush BackgroundBrush = Brushes.Solid(BackgroundColor);
    private static readonly Brush PageBrush = Brushes.Solid(PageColor);
    private static readonly Brush SlotBrush = Brushes.Solid(SlotColor);
    private static readonly Brush ObstacleBrush = Brushes.Solid(CircleColor);
    private static readonly Brush TextBrush = Brushes.Solid(TextColor);
    private static readonly Pen PageOutlinePen = Pens.Solid(PageOutlineColor, 1.5F);
    private static readonly Pen ObstacleOutlinePen = Pens.Solid(Color.SteelBlue, 2F);

    private readonly Font bodyFont;
    private readonly TextBlock textBlock;
    private readonly List<BandInterval> scanBlockedIntervals = new(16);
    private readonly List<BandInterval> blockedIntervals = new(16);
    private readonly List<BandInterval> slots = new(8);
    private readonly List<float> scanYs = new(8);
    private readonly List<float> intersections = new(16);
    private PointF obstacleCenter;
    private ManualTextFlowObstacleShape obstacleShape = ManualTextFlowObstacleShape.Circle;
    private IPath? cachedObstaclePath;
    private LinearGeometry? cachedObstacleGeometry;
    private PointF cachedObstacleCenter;
    private float cachedObstacleSize;
    private ManualTextFlowObstacleShape cachedObstacleShape;
    private bool hasPointer;

    public ManualTextFlowScene()
    {
        FontFamily family = SystemFonts.Collection.Families.FirstOrDefault();
        this.bodyFont = family.Name is null
            ? SystemFonts.CreateFont(SystemFonts.Families.First().Name, 24F, FontStyle.Regular)
            : family.CreateFont(24F, FontStyle.Regular);

        TextOptions options = new(this.bodyFont)
        {
            Origin = PointF.Empty,
            WrappingLength = -1,
            LineSpacing = 1.15F
        };

        // The paragraph is shaped once. During painting we only ask the block for
        // successive line layouts at different widths, which is the key behavior
        // demonstrated by this scene.
        this.textBlock = new TextBlock(Text, options);
    }

    public override string DisplayName => "Manual Text Flow";

    /// <summary>
    /// Gets or sets the closed shape used as the flow obstacle.
    /// </summary>
    public ManualTextFlowObstacleShape ObstacleShape
    {
        get => this.obstacleShape;
        set
        {
            if (this.obstacleShape == value)
            {
                return;
            }

            this.obstacleShape = value;
            this.cachedObstaclePath = null;
            this.cachedObstacleGeometry = null;
        }
    }

    public override void Paint(DrawingCanvas canvas, TimeSpan deltaTime)
    {
        Size viewportSize = canvas.Bounds.Size;
        canvas.Fill(BackgroundBrush, canvas.Bounds);

        // The page rectangle is the region we subtract obstacle coverage from.
        // Text slots are always produced within these left/right limits.
        float pageLeft = 48F;
        float pageTop = 48F;
        float pageRight = MathF.Max(pageLeft, viewportSize.Width - 48F);
        float pageBottom = MathF.Max(pageTop, viewportSize.Height - 48F);

        // Until the pointer enters the scene, place the obstacle in a useful
        // default position so the flow-around behavior is visible immediately.
        PointF obstacleCenter = this.hasPointer
            ? this.obstacleCenter
            : new PointF(pageLeft + ((pageRight - pageLeft) * .58F), pageTop + ((pageBottom - pageTop) * .46F));

        float obstacleSize = MathF.Max(48F, MathF.Min(viewportSize.Width, viewportSize.Height) * .12F) * 1.33F;
        float obstaclePadding = 18F;
        float minSlotWidth = MathF.Max(120F, this.bodyFont.Size * 5F);
        float y = pageTop;
        bool hasMoreText = true;

        // The enumerator owns the "next source slice" state. Each MoveNext(width)
        // call consumes the next line that fits that width, so rows can have one
        // slot, two slots, or many slots without rebuilding the TextBlock.
        LineLayoutEnumerator enumerator = this.textBlock.EnumerateLineLayouts();

        // The visible obstacle is deliberately just an IPath. The text-flow code
        // below never needs to know whether this came from a circle, star, rectangle,
        // or any other closed shape that can be linearized by ImageSharp.Drawing.
        // Linearization is the expensive part, so reuse it until the obstacle input
        // changes through pointer movement, resizing, or the shape selector.
        IPath? obstaclePath = this.cachedObstaclePath;
        LinearGeometry? obstacleGeometry = this.cachedObstacleGeometry;
        if (obstaclePath is null
            || obstacleGeometry is null
            || !this.cachedObstacleCenter.Equals(obstacleCenter)
            || this.cachedObstacleSize != obstacleSize
            || this.cachedObstacleShape != this.obstacleShape)
        {
            obstaclePath = this.CreateObstaclePath(obstacleCenter, obstacleSize);
            obstacleGeometry = obstaclePath.ToLinearGeometry(Vector2.One);
            this.cachedObstaclePath = obstaclePath;
            this.cachedObstacleGeometry = obstacleGeometry;
            this.cachedObstacleCenter = obstacleCenter;
            this.cachedObstacleSize = obstacleSize;
            this.cachedObstacleShape = this.obstacleShape;
        }

        canvas.Fill(PageBrush, new RectanglePolygon(pageLeft, pageTop, pageRight - pageLeft, pageBottom - pageTop));
        canvas.Draw(PageOutlinePen, new RectanglePolygon(pageLeft, pageTop, pageRight - pageLeft, pageBottom - pageTop));
        canvas.Fill(ObstacleBrush, obstaclePath);
        canvas.Draw(ObstacleOutlinePen, obstaclePath);

        // Rows can be split into several usable slots by an arbitrary closed path.
        // The scene owns the temporary buffers so continuous repainting does not
        // allocate fresh lists for every row of every frame.
        while (hasMoreText && y < pageBottom)
        {
            float rowProbeHeight = this.bodyFont.Size * 1.45F;
            float bandTop = y;
            float bandBottom = y + rowProbeHeight;

            // Convert the current row into a set of usable horizontal slots.
            // The returned slots are ordinary rectangles; the text engine does
            // not need to understand arbitrary paths, only the width available
            // for the next line.
            BuildSlots(
                obstacleGeometry,
                bandTop,
                bandBottom,
                pageLeft,
                pageRight,
                obstaclePadding,
                minSlotWidth,
                this.scanBlockedIntervals,
                this.blockedIntervals,
                this.slots,
                this.scanYs,
                this.intersections);

            float rowHeight = rowProbeHeight;
            for (int i = 0; i < this.slots.Count && hasMoreText; i++)
            {
                BandInterval slot = this.slots[i];
                float slotWidth = slot.Width;

                // Each MoveNext call consumes just enough prepared text for the
                // supplied slot width. The next slot or row can use a different
                // width without reshaping the original paragraph.
                hasMoreText = enumerator.MoveNext(slotWidth);
                if (!hasMoreText)
                {
                    break;
                }

                LineLayout line = enumerator.Current;
                float lineHeight = line.LineMetrics.LineHeight;

                // The translucent slot fill is a visual aid for the sample. It
                // makes the row splitting visible so readers can compare the
                // available rectangles with the selected obstacle shape.
                canvas.Fill(SlotBrush, new RectanglePolygon(slot.Left, y, slotWidth, lineHeight));
                canvas.DrawText(line, new PointF(slot.Left, y), TextBrush, pen: null);

                rowHeight = MathF.Max(rowHeight, lineHeight);
            }

            y += rowHeight;
        }
    }

    public override void OnMouseDown(MouseEventArgs e) => this.SetObstacleCenter(e);

    public override void OnMouseMove(MouseEventArgs e) => this.SetObstacleCenter(e);

    private void SetObstacleCenter(MouseEventArgs e)
    {
        this.obstacleCenter = new PointF(e.X, e.Y);
        this.hasPointer = true;
    }

    /// <summary>
    /// Creates the currently selected obstacle as a closed path.
    /// </summary>
    /// <param name="center">The obstacle center in canvas coordinates.</param>
    /// <param name="size">The obstacle width and height in pixels.</param>
    /// <returns>The selected closed obstacle path.</returns>
    private IPath CreateObstaclePath(PointF center, float size)
    {
        float radius = size * .5F;

        // Every choice returns an IPath so the flow algorithm below can stay
        // shape-agnostic. That is the point of the sample: once a shape can be
        // linearized, text flow only needs row intersections, not shape-specific
        // circle or rectangle math.
        return this.ObstacleShape switch
        {
            ManualTextFlowObstacleShape.Rectangle => new RectanglePolygon(
                center.X - radius,
                center.Y - radius,
                size,
                size),
            ManualTextFlowObstacleShape.Triangle => new RegularPolygon(center, 3, radius, 180F),
            ManualTextFlowObstacleShape.Diamond => new RegularPolygon(center, 4, radius, 0F),
            ManualTextFlowObstacleShape.Star => new StarPolygon(center, 5, radius * .45F, radius, -18F),
            _ => new EllipsePolygon(center, new SizeF(size, size))
        };
    }

    /// <summary>
    /// Builds the row slots that remain after subtracting the closed obstacle from one horizontal band.
    /// </summary>
    /// <param name="obstacleGeometry">The linearized obstacle path.</param>
    /// <param name="bandTop">The top of the row band.</param>
    /// <param name="bandBottom">The bottom of the row band.</param>
    /// <param name="pageLeft">The left edge of the page.</param>
    /// <param name="pageRight">The right edge of the page.</param>
    /// <param name="obstaclePadding">Extra horizontal clearance to apply around the obstacle.</param>
    /// <param name="minSlotWidth">The minimum slot width worth using for text.</param>
    /// <param name="scanBlockedIntervals">Reusable temporary intervals produced by individual scanlines.</param>
    /// <param name="blockedIntervals">Reusable merged obstacle intervals for the whole band.</param>
    /// <param name="slots">Reusable output list containing the final usable text slots.</param>
    /// <param name="scanYs">Reusable scanline Y positions for this band.</param>
    /// <param name="intersections">Reusable scanline X intersections.</param>
    private static void BuildSlots(
        LinearGeometry obstacleGeometry,
        float bandTop,
        float bandBottom,
        float pageLeft,
        float pageRight,
        float obstaclePadding,
        float minSlotWidth,
        List<BandInterval> scanBlockedIntervals,
        List<BandInterval> blockedIntervals,
        List<BandInterval> slots,
        List<float> scanYs,
        List<float> intersections)
    {
        scanBlockedIntervals.Clear();
        blockedIntervals.Clear();
        slots.Clear();
        scanYs.Clear();

        // Step 1: choose scanlines that represent this row. A single text row has
        // finite height, so one scanline through the row is not enough for curved
        // or angled shapes. We sample the top, middle, bottom, and any flattened
        // vertices inside the row, then union the blocked intervals. That
        // deliberately overestimates the obstacle a bit, which is preferable for
        // a flow-around demo because text should not visibly collide with the shape.
        AddBandScanLines(obstacleGeometry, bandTop, bandBottom, scanYs);

        // Step 2: for each scanline, run an even-odd fill test and collect the
        // horizontal ranges occupied by the closed path at that Y coordinate.
        for (int i = 0; i < scanYs.Count; i++)
        {
            AddBlockedIntervalsAtY(obstacleGeometry, scanYs[i], obstaclePadding, scanBlockedIntervals, intersections);
        }

        // Step 3: each scanline reports the shape coverage at one Y coordinate.
        // Merge all sampled coverage into one row-level set of blocked intervals
        // before subtracting those intervals from the page width.
        MergeBlockedIntervals(scanBlockedIntervals, pageLeft, pageRight, blockedIntervals);

        // Step 4: subtract the blocked intervals from the page bounds. Any gap
        // wide enough for useful text becomes a slot passed to the line enumerator.
        float cursor = pageLeft;
        for (int i = 0; i < blockedIntervals.Count; i++)
        {
            BandInterval blocked = blockedIntervals[i];
            if (blocked.Left - cursor >= minSlotWidth)
            {
                slots.Add(new BandInterval(cursor, blocked.Left));
            }

            cursor = MathF.Max(cursor, blocked.Right);
        }

        if (pageRight - cursor >= minSlotWidth)
        {
            slots.Add(new BandInterval(cursor, pageRight));
        }
    }

    /// <summary>
    /// Chooses the scanlines used to approximate obstacle coverage across one row band.
    /// </summary>
    /// <param name="obstacleGeometry">The linearized obstacle path.</param>
    /// <param name="bandTop">The top of the row band.</param>
    /// <param name="bandBottom">The bottom of the row band.</param>
    /// <param name="scanYs">The output scanline list.</param>
    private static void AddBandScanLines(
        LinearGeometry obstacleGeometry,
        float bandTop,
        float bandBottom,
        List<float> scanYs)
    {
        const float Epsilon = 0.01F;

        // Top and bottom are nudged inside the band. That avoids ambiguous edge
        // cases where a scanline lies exactly on a path vertex or on the row edge.
        AddScanY(bandTop + Epsilon, bandTop, bandBottom, scanYs);
        AddScanY((bandTop + bandBottom) * .5F, bandTop, bandBottom, scanYs);
        AddScanY(bandBottom - Epsilon, bandTop, bandBottom, scanYs);

        // Vertices inside the band are local extrema for the flattened outline.
        // Sampling them keeps the row projection conservative for curved shapes
        // without building a separate polygon clipping pipeline for the demo.
        SegmentEnumerator segments = obstacleGeometry.GetSegments();
        while (segments.MoveNext())
        {
            LinearSegment segment = segments.Current;
            AddScanY(segment.Start.Y, bandTop, bandBottom, scanYs);
            AddScanY(segment.End.Y, bandTop, bandBottom, scanYs);
        }
    }

    /// <summary>
    /// Adds one scanline position when it lies inside the band and is not already represented.
    /// </summary>
    /// <param name="y">The scanline Y coordinate.</param>
    /// <param name="bandTop">The top of the row band.</param>
    /// <param name="bandBottom">The bottom of the row band.</param>
    /// <param name="scanYs">The scanline collection for the current band.</param>
    private static void AddScanY(float y, float bandTop, float bandBottom, List<float> scanYs)
    {
        const float DuplicateTolerance = 0.5F;

        // Scanlines exactly on the edge of the row do not tell us whether the
        // row itself is obstructed. The top/bottom samples are added with a small
        // epsilon so the useful samples are always strictly inside the row.
        if (y <= bandTop || y >= bandBottom)
        {
            return;
        }

        // Flattened curves can contain many nearby points. Coalescing close
        // scanlines keeps the sample readable and prevents dense curves from
        // doing far more work than polygons in this demo.
        for (int i = 0; i < scanYs.Count; i++)
        {
            if (MathF.Abs(scanYs[i] - y) < DuplicateTolerance)
            {
                return;
            }
        }

        scanYs.Add(y);
    }

    /// <summary>
    /// Adds the blocked horizontal intervals for a single scanline.
    /// </summary>
    /// <param name="obstacleGeometry">The linearized obstacle path.</param>
    /// <param name="y">The scanline Y coordinate.</param>
    /// <param name="obstaclePadding">Extra horizontal clearance to apply around blocked spans.</param>
    /// <param name="blockedIntervals">The collected blocked spans for all scanlines in the current band.</param>
    /// <param name="intersections">Reusable storage for this scanline's X intersections.</param>
    private static void AddBlockedIntervalsAtY(
        LinearGeometry obstacleGeometry,
        float y,
        float obstaclePadding,
        List<BandInterval> blockedIntervals,
        List<float> intersections)
    {
        intersections.Clear();

        // Standard even-odd scanline fill: collect every edge crossing at this Y,
        // sort the X coordinates, then pair them. The pairs represent the filled
        // horizontal spans of the closed path at this scanline.
        SegmentEnumerator segments = obstacleGeometry.GetSegments();
        while (segments.MoveNext())
        {
            LinearSegment segment = segments.Current;

            // Horizontal edges do not add crossings in the usual even-odd
            // scanline rule. Including them would double-count vertices shared
            // by neighboring non-horizontal edges.
            if (segment.IsHorizontal)
            {
                continue;
            }

            // Use a half-open Y interval so a scanline passing through a vertex
            // counts exactly one of the two incident edges. That is the standard
            // way to avoid odd/even flicker at polygon vertices.
            bool crosses =
                (segment.Start.Y <= y && segment.End.Y > y)
                || (segment.End.Y <= y && segment.Start.Y > y);
            if (!crosses)
            {
                continue;
            }

            // The geometry is already flattened, so each crossing is found by
            // simple linear interpolation along the segment.
            float t = (y - segment.Start.Y) / (segment.End.Y - segment.Start.Y);
            intersections.Add(segment.Start.X + ((segment.End.X - segment.Start.X) * t));
        }

        intersections.Sort();
        for (int i = 0; i + 1 < intersections.Count; i += 2)
        {
            // Pair sorted intersections as filled spans. Padding is applied here
            // so the later merge step can combine overlapping padded spans from
            // several scanlines into a single row-level exclusion range.
            float left = intersections[i] - obstaclePadding;
            float right = intersections[i + 1] + obstaclePadding;
            if (right > left)
            {
                blockedIntervals.Add(new BandInterval(left, right));
            }
        }
    }

    /// <summary>
    /// Merges scanline intervals into clipped row-level blocked intervals.
    /// </summary>
    /// <param name="source">The padded blocked intervals produced by every scanline in the row band.</param>
    /// <param name="pageLeft">The left page boundary.</param>
    /// <param name="pageRight">The right page boundary.</param>
    /// <param name="destination">The merged row-level blocked intervals.</param>
    private static void MergeBlockedIntervals(
        List<BandInterval> source,
        float pageLeft,
        float pageRight,
        List<BandInterval> destination)
    {
        source.Sort(static (x, y) => x.Left.CompareTo(y.Left));

        // The scanline samples are intentionally conservative and may overlap.
        // Sorting by left edge lets us merge them in one pass.
        for (int i = 0; i < source.Count; i++)
        {
            BandInterval current = source[i];
            if (current.Right <= pageLeft || current.Left >= pageRight)
            {
                continue;
            }

            current = new BandInterval(
                MathF.Max(pageLeft, current.Left),
                MathF.Min(pageRight, current.Right));

            if (destination.Count == 0)
            {
                destination.Add(current);
                continue;
            }

            BandInterval previous = destination[^1];
            if (current.Left <= previous.Right)
            {
                // Adjacent or overlapping blocked spans are one obstacle region
                // for this row. Keeping them merged makes slot subtraction simple.
                destination[^1] = new BandInterval(previous.Left, MathF.Max(previous.Right, current.Right));
                continue;
            }

            destination.Add(current);
        }
    }

    /// <summary>
    /// Represents a horizontal interval in canvas coordinates.
    /// </summary>
    private readonly struct BandInterval
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="BandInterval"/> struct.
        /// </summary>
        /// <param name="left">The inclusive left edge.</param>
        /// <param name="right">The exclusive right edge.</param>
        public BandInterval(float left, float right)
        {
            this.Left = left;
            this.Right = right;
        }

        /// <summary>
        /// Gets the left edge of the interval.
        /// </summary>
        public float Left { get; }

        /// <summary>
        /// Gets the right edge of the interval.
        /// </summary>
        public float Right { get; }

        /// <summary>
        /// Gets the interval width.
        /// </summary>
        public float Width => this.Right - this.Left;
    }
}

/// <summary>
/// Closed obstacle shapes demonstrated by the manual text-flow scene.
/// </summary>
internal enum ManualTextFlowObstacleShape
{
    /// <summary>
    /// A circular obstacle backed by <see cref="EllipsePolygon"/>.
    /// </summary>
    Circle,

    /// <summary>
    /// A rectangular obstacle backed by <see cref="RectanglePolygon"/>.
    /// </summary>
    Rectangle,

    /// <summary>
    /// A triangular obstacle backed by <see cref="RegularPolygon"/>.
    /// </summary>
    Triangle,

    /// <summary>
    /// A diamond obstacle backed by <see cref="RegularPolygon"/>.
    /// </summary>
    Diamond,

    /// <summary>
    /// A concave star obstacle backed by <see cref="StarPolygon"/>.
    /// </summary>
    Star
}
