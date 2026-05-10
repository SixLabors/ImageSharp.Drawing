// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Brushes = SixLabors.ImageSharp.Drawing.Processing.Brushes;
using Color = SixLabors.ImageSharp.Color;
using Pen = SixLabors.ImageSharp.Drawing.Processing.Pen;
using Pens = SixLabors.ImageSharp.Drawing.Processing.Pens;
using PointF = SixLabors.ImageSharp.PointF;
using Rectangle = SixLabors.ImageSharp.Rectangle;
using Size = SixLabors.ImageSharp.Size;
using SizeF = SixLabors.ImageSharp.SizeF;

namespace WebGPUExternalSurfaceDemo.Scenes;

/// <summary>
/// External surface scene that exercises canvas readback by applying CPU image processors to regions of the current frame.
/// Pointer movement changes the processed regions so readback cost can be assessed interactively.
/// </summary>
internal sealed class ApplyReadbackScene : RenderScene
{
    private static readonly Color BackgroundColor = Color.MidnightBlue;
    private static readonly Color StripeA = Color.LimeGreen;
    private static readonly Color StripeB = Color.DodgerBlue;
    private static readonly Color StripeC = Color.Orange;
    private static readonly Color OutlineColor = Color.White;
    private static readonly Color GuideLineColor = Color.White.WithAlpha(.5F);

    private PointF pointer;
    private bool hasPointer;
    private float regionScale = 1F;

    public override string DisplayName => "Apply";

    public override void Paint(DrawingCanvas canvas, TimeSpan deltaTime)
    {
        Size viewportSize = canvas.Bounds.Size;

        canvas.Fill(Brushes.Solid(BackgroundColor), canvas.Bounds);
        DrawPattern(canvas, viewportSize);

        // Keep both regions tied to the pointer so every movement exercises readback from a different part
        // of the surface texture instead of repeatedly processing a static rectangle.
        PointF focus = this.hasPointer ? this.pointer : new PointF(viewportSize.Width * .5F, viewportSize.Height * .5F);
        float effectSize = MathF.Max(48F, MathF.Min(viewportSize.Width, viewportSize.Height) * .26F * this.regionScale);
        PointF blurCenter = new(
            ClampCenter(viewportSize.Width - focus.X, effectSize * 1.25F, viewportSize.Width),
            ClampCenter(focus.Y, effectSize, viewportSize.Height));

        Rectangle edgeRegion = CreateRegion(viewportSize, focus, effectSize);
        IPath blurRegion = new EllipsePolygon(
            blurCenter,
            new SizeF(effectSize * 1.25F, effectSize));

        // Apply reads the affected pixels back from the current WebGPU frame, runs the CPU processor,
        // then writes the processed region back before the frame is presented.
        canvas.Apply(edgeRegion, ctx => ctx.DetectEdges());
        canvas.Apply(blurRegion, ctx => ctx.GaussianBlur(Math.Max(3F, Math.Min(viewportSize.Width, viewportSize.Height) / 120F)));

        canvas.Draw(Pens.Solid(OutlineColor, 3), new RectangularPolygon(edgeRegion));
        canvas.Draw(Pens.Solid(OutlineColor, 3), blurRegion);
    }

    public override void OnMouseDown(MouseEventArgs e) => this.SetPointer(e);

    public override void OnMouseMove(MouseEventArgs e) => this.SetPointer(e);

    public override void OnMouseWheel(MouseEventArgs e)
    {
        this.SetPointer(e);

        // Wheel resizing changes the amount of data read back and written back each frame.
        float factor = e.Delta > 0 ? 1.12F : 1F / 1.12F;
        this.regionScale = Math.Clamp(this.regionScale * factor, .5F, 2.4F);
    }

    private static void DrawPattern(DrawingCanvas canvas, Size viewportSize)
    {
        float stripeWidth = Math.Max(18F, viewportSize.Width / 18F);
        float height = viewportSize.Height;

        for (int i = -2; i < (viewportSize.Width / stripeWidth) + 3; i++)
        {
            Color color = (Math.Abs(i) % 3) switch
            {
                0 => StripeA,
                1 => StripeB,
                _ => StripeC,
            };

            float x = i * stripeWidth;
            IPath stripe = new Polygon(
                new LinearLineSegment(
                    new PointF(x, 0),
                    new PointF(x + (stripeWidth * 1.6F), 0),
                    new PointF(x + (stripeWidth * .6F), height),
                    new PointF(x - stripeWidth, height)));

            canvas.Fill(Brushes.Solid(color), stripe);
        }

        Pen guideLinePen = Pens.Solid(GuideLineColor, 1.5F);
        for (int y = 0; y < viewportSize.Height; y += Math.Max(24, viewportSize.Height / 14))
        {
            canvas.DrawLine(
                guideLinePen,
                new PointF(0, y),
                new PointF(viewportSize.Width, y + (viewportSize.Width * .08F)));
        }
    }

    private static Rectangle CreateRegion(Size viewportSize, PointF center, float size)
    {
        int width = Math.Max(1, Math.Min(viewportSize.Width, (int)size));
        int height = Math.Max(1, Math.Min(viewportSize.Height, (int)(size * .75F)));
        int x = Math.Clamp((int)(center.X - (width * .5F)), 0, Math.Max(0, viewportSize.Width - width));
        int y = Math.Clamp((int)(center.Y - (height * .5F)), 0, Math.Max(0, viewportSize.Height - height));

        return new Rectangle(x, y, width, height);
    }

    private static float ClampCenter(float value, float size, float limit)
    {
        float radius = MathF.Min(size * .5F, limit * .5F);

        return Math.Clamp(value, radius, limit - radius);
    }

    private void SetPointer(MouseEventArgs e)
    {
        this.pointer = new PointF(e.X, e.Y);
        this.hasPointer = true;
    }
}
