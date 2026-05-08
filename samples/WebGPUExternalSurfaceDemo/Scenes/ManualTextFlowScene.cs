// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.Fonts;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;
using Brushes = SixLabors.ImageSharp.Drawing.Processing.Brushes;
using Color = SixLabors.ImageSharp.Color;
using Font = SixLabors.Fonts.Font;
using FontFamily = SixLabors.Fonts.FontFamily;
using FontStyle = SixLabors.Fonts.FontStyle;
using Pens = SixLabors.ImageSharp.Drawing.Processing.Pens;
using PointF = SixLabors.ImageSharp.PointF;
using Size = SixLabors.ImageSharp.Size;
using SizeF = SixLabors.ImageSharp.SizeF;
using SystemFonts = SixLabors.Fonts.SystemFonts;

namespace WebGPUExternalSurfaceDemo.Scenes;

/// <summary>
/// Interactive manual text-flow scene. Moving the mouse moves the circular obstacle, and the text is
/// reflowed line-by-line into the remaining horizontal slots.
/// </summary>
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

    private readonly Font bodyFont;
    private readonly TextBlock textBlock;
    private PointF circleCenter;
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

        this.textBlock = new TextBlock(Text, options);
    }

    public override string DisplayName => "Manual Text Flow";

    public override void Paint(DrawingCanvas canvas, TimeSpan deltaTime)
    {
        Size viewportSize = canvas.Bounds.Size;
        canvas.Fill(Brushes.Solid(BackgroundColor), canvas.Bounds);

        float pageLeft = 48F;
        float pageTop = 48F;
        float pageRight = MathF.Max(pageLeft, viewportSize.Width - 48F);
        float pageBottom = MathF.Max(pageTop, viewportSize.Height - 48F);
        PointF obstacleCenter = this.hasPointer
            ? this.circleCenter
            : new PointF(pageLeft + ((pageRight - pageLeft) * .58F), pageTop + ((pageBottom - pageTop) * .46F));

        float circleRadius = MathF.Max(48F, MathF.Min(viewportSize.Width, viewportSize.Height) * .12F);
        float circlePadding = 18F;
        float minSlotWidth = MathF.Max(120F, this.bodyFont.Size * 5F);
        float y = pageTop;
        bool hasMoreText = true;
        LineLayoutEnumerator enumerator = this.textBlock.EnumerateLineLayouts();

        canvas.Fill(Brushes.Solid(PageColor), new RectangularPolygon(pageLeft, pageTop, pageRight - pageLeft, pageBottom - pageTop));
        canvas.Draw(Pens.Solid(PageOutlineColor, 1.5F), new RectangularPolygon(pageLeft, pageTop, pageRight - pageLeft, pageBottom - pageTop));
        canvas.Fill(Brushes.Solid(CircleColor), new EllipsePolygon(obstacleCenter, new SizeF(circleRadius, circleRadius)));
        canvas.Draw(Pens.Solid(Color.SteelBlue, 2F), new EllipsePolygon(obstacleCenter, new SizeF(circleRadius, circleRadius)));

        // A horizontal row can be split by the circular obstacle into at most
        // two usable slots. Keep these buffers outside the row loop so pointer
        // movement does not introduce per-line stackalloc work.
        Span<float> slotLefts = stackalloc float[2];
        Span<float> slotRights = stackalloc float[2];

        while (hasMoreText && y < pageBottom)
        {
            float rowProbeHeight = this.bodyFont.Size * 1.45F;
            float bandTop = y;
            float bandBottom = y + rowProbeHeight;
            float blockedLeft = float.NaN;
            float blockedRight = float.NaN;

            // The obstacle is converted into the horizontal interval that intersects
            // this row band. Everything outside that interval is ordinary rectangular
            // text layout, which is why the text engine only needs a width per line.
            if (bandTop < obstacleCenter.Y + circleRadius && bandBottom > obstacleCenter.Y - circleRadius)
            {
                float closestY = Math.Clamp(obstacleCenter.Y, bandTop, bandBottom);
                float dy = Math.Abs(closestY - obstacleCenter.Y);
                float dx = MathF.Sqrt((circleRadius * circleRadius) - (dy * dy));
                blockedLeft = obstacleCenter.X - dx - circlePadding;
                blockedRight = obstacleCenter.X + dx + circlePadding;
            }

            int slotCount = 0;
            if (float.IsNaN(blockedLeft))
            {
                slotLefts[slotCount] = pageLeft;
                slotRights[slotCount++] = pageRight;
            }
            else
            {
                if (blockedLeft - pageLeft >= minSlotWidth)
                {
                    slotLefts[slotCount] = pageLeft;
                    slotRights[slotCount++] = blockedLeft;
                }

                if (pageRight - blockedRight >= minSlotWidth)
                {
                    slotLefts[slotCount] = blockedRight;
                    slotRights[slotCount++] = pageRight;
                }
            }

            float rowHeight = rowProbeHeight;
            for (int i = 0; i < slotCount && hasMoreText; i++)
            {
                float slotLeft = slotLefts[i];
                float slotRight = slotRights[i];
                float slotWidth = slotRight - slotLeft;

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
                canvas.Fill(Brushes.Solid(SlotColor), new RectangularPolygon(slotLeft, y, slotWidth, lineHeight));
                canvas.DrawText(line, new PointF(slotLeft, y), Brushes.Solid(TextColor), pen: null);

                rowHeight = MathF.Max(rowHeight, lineHeight);
            }

            y += rowHeight;
        }
    }

    public override void OnMouseDown(MouseEventArgs e) => this.SetCircleCenter(e);

    public override void OnMouseMove(MouseEventArgs e) => this.SetCircleCenter(e);

    private void SetCircleCenter(MouseEventArgs e)
    {
        this.circleCenter = new PointF(e.X, e.Y);
        this.hasPointer = true;
    }
}
