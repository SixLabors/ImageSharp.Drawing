// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System.Numerics;
using System.Runtime.CompilerServices;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace SixLabors.ImageSharp.Drawing.Tests
{
    internal class DebugDraw
    {
        private static readonly Brush TestBrush = Brushes.Solid(Color.Red);

        private static readonly IPen GridPen = Pens.Solid(Color.Aqua, 0.5f);

        private readonly string outputDir;

        public DebugDraw(string outputDir)
            => this.outputDir = outputDir;

        public void Polygon(IPath path, float gridSize = 10f, float scale = 10f, [CallerMemberName] string testMethod = "")
        {
            if (TestEnvironment.RunsOnCI)
            {
                return;
            }

            path = path.Transform(Matrix3x2.CreateScale(scale) * Matrix3x2.CreateTranslation(gridSize, gridSize));
            RectangleF bounds = path.Bounds;
            gridSize *= scale;

            using Image img = new Image<Rgba32>((int)(bounds.Right + (2 * gridSize)), (int)(bounds.Bottom + (2 * gridSize)));
            img.Mutate(ctx => DrawGrid(ctx.Fill(TestBrush, path), bounds, gridSize));

            string outDir = TestEnvironment.CreateOutputDirectory(this.outputDir);
            string outFile = System.IO.Path.Combine(outDir, testMethod + ".png");
            img.SaveAsPng(outFile);
        }

        private static PointF P(float x, float y) => new PointF(x, y);

        private static void DrawGrid(IImageProcessingContext ctx, RectangleF rect, float gridSize)
        {
            for (float x = rect.Left; x <= rect.Right; x += gridSize)
            {
                PointF[] line = { P(x, rect.Top), P(x, rect.Bottom) };
                ctx.DrawLines(GridPen, line);
            }

            for (float y = rect.Top; y <= rect.Bottom; y += gridSize)
            {
                PointF[] line = { P(rect.Left, y), P(rect.Right, y) };
                ctx.DrawLines(GridPen, line);
            }
        }
    }
}
