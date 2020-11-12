// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;

namespace SixLabors.ImageSharp.Drawing.Shapes.Rasterization
{
    internal static class RasterizerExtensions
    {
        public static bool ScanCurrentLineInto(this PolygonScanner scanner, float minX, float xOffset, Span<float> scanline)
        {
            ReadOnlySpan<float> points = scanner.ScanCurrentLine();
            if (points.Length == 0)
            {
                // nothing on this line, skip
                return false;
            }

            bool scanlineDirty = false;

            for (int point = 0; point < points.Length - 1; point += 2)
            {
                // points will be paired up
                float scanStart = points[point] - minX;
                float scanEnd = points[point + 1] - minX;
                int startX = (int)MathF.Floor(scanStart + xOffset);
                int endX = (int)MathF.Floor(scanEnd + xOffset);

                if (startX >= 0 && startX < scanline.Length)
                {
                    for (float x = scanStart; x < startX + 1; x += scanner.SubpixelFraction)
                    {
                        scanline[startX] += scanner.SubpixelFractionPoint;
                        scanlineDirty = true;
                    }
                }

                if (endX >= 0 && endX < scanline.Length)
                {
                    for (float x = endX; x < scanEnd; x += scanner.SubpixelFraction)
                    {
                        scanline[endX] += scanner.SubpixelFractionPoint;
                        scanlineDirty = true;
                    }
                }

                int nextX = startX + 1;
                endX = Math.Min(endX, scanline.Length); // reduce to end to the right edge
                nextX = Math.Max(nextX, 0);
                for (int x = nextX; x < endX; x++)
                {
                    scanline[x] += scanner.SubpixelFraction;
                    scanlineDirty = true;
                }
            }

            return scanlineDirty;
        }
    }
}