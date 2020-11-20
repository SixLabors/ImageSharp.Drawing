// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using SixLabors.ImageSharp.Drawing.Utilities;

namespace SixLabors.ImageSharp.Drawing.Shapes.Rasterization
{
    internal static class RasterizerExtensions
    {
        public static bool ScanCurrentPixelLineInto(this ref PolygonScanner scanner, int minX, float xOffset, Span<float> scanline)
        {
            bool scanlineDirty = false;
            while (scanner.MoveToNextSubpixelScanLine())
            {
                scanner.ScanCurrentSubpixelLineInto(minX, xOffset, scanline, ref scanlineDirty);
            }

            return scanlineDirty;
        }

        private static void ScanCurrentSubpixelLineInto(this ref PolygonScanner scanner, int minX, float xOffset, Span<float> scanline, ref bool scanlineDirty)
        {
            ReadOnlySpan<float> points = scanner.ScanCurrentLine();
            if (points.Length == 0)
            {
                // nothing on this line, skip
                return;
            }

            for (int point = 0; point < points.Length - 1; point += 2)
            {
                // points will be paired up
                float scanStart = points[point] - minX;
                float scanEnd = points[point + 1] - minX;
                int startX = (int)MathF.Floor(scanStart + xOffset);
                int endX = (int)MathF.Floor(scanEnd + xOffset);

                if (startX >= 0 && startX < scanline.Length)
                {
                    // Originally, this was implemented by a loop.
                    // It's possible to emulate the old behavior with MathF.Ceiling,
                    // but omitting the rounding seems to produce more accurate results.
                    // float subpixelWidth = MathF.Ceiling((startX + 1 - scanStart) / scanner.SubpixelDistance);
                    float subpixelWidth = (startX + 1 - scanStart) / scanner.SubpixelDistance;

                    scanline[startX] += subpixelWidth * scanner.SubpixelArea;
                    scanlineDirty = subpixelWidth > 0;
                }

                if (endX >= 0 && endX < scanline.Length)
                {
                    // float subpixelWidth = MathF.Ceiling((scanEnd - endX) / scanner.SubpixelDistance);
                    float subpixelWidth = (scanEnd - endX) / scanner.SubpixelDistance;

                    scanline[endX] += subpixelWidth * scanner.SubpixelArea;
                    scanlineDirty = subpixelWidth > 0;
                }

                int nextX = startX + 1;
                endX = Math.Min(endX, scanline.Length); // reduce to end to the right edge
                nextX = Math.Max(nextX, 0);

                if (endX > nextX)
                {
                    scanline.Slice(nextX, endX - nextX).AddToAllElements(scanner.SubpixelDistance);
                    scanlineDirty = true;
                }
            }
        }
    }
}