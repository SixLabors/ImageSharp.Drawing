// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Numerics;
using SixLabors.ImageSharp.Drawing.Processing.Backends;
using SixLabors.ImageSharp.PixelFormats;
using SkiaSharp;
using SDColor = System.Drawing.Color;
using SDPen = System.Drawing.Pen;
using SDSolidBrush = System.Drawing.SolidBrush;

namespace SixLabors.ImageSharp.Drawing.Tests;

/// <summary>
/// SkiaSharp and System.Drawing backend builders plus verification/export helpers.
/// Kept separate from the parsing half so samples can link only the ImageSharp-facing code without pulling in SkiaSharp/GDI+.
/// </summary>
internal static partial class SvgBenchmarkHelper
{
    /// <summary>
    /// Builds pre-parsed SkiaSharp elements for benchmarking.
    /// </summary>
    internal static List<(SKPath Path, SKPaint FillPaint, SKPaint StrokePaint)> BuildSkiaElements(
        List<SvgElement> elements,
        float scale)
    {
        List<(SKPath, SKPaint, SKPaint)> result = [];
        foreach (SvgElement el in elements)
        {
            SKPath skPath = SKPath.ParseSvgPathData(el.PathData);
            if (skPath is null)
            {
                continue;
            }

            SKMatrix skMatrix = SKMatrix.CreateScale(scale, scale);
            if (el.Transform.HasValue)
            {
                Matrix4x4 m = el.Transform.Value;
                SKMatrix elMatrix = new(m.M11, m.M21, m.M41, m.M12, m.M22, m.M42, 0, 0, 1);
                skMatrix = SKMatrix.Concat(skMatrix, elMatrix);
            }

            skPath.Transform(skMatrix);

            Rgba32 fillPixel = el.Fill.ToPixel<Rgba32>();
            SKPaint fillPaint = fillPixel.A > 0
                ? new SKPaint
                {
                    Style = SKPaintStyle.Fill,
                    Color = new SKColor(fillPixel.R, fillPixel.G, fillPixel.B, fillPixel.A),
                    IsAntialias = true,
                }
                : null;

            Rgba32 strokePixel = el.Stroke.ToPixel<Rgba32>();
            SKPaint strokePaint = strokePixel.A > 0 && el.StrokeWidth > 0
                ? new SKPaint
                {
                    Style = SKPaintStyle.Stroke,
                    Color = new SKColor(strokePixel.R, strokePixel.G, strokePixel.B, strokePixel.A),
                    StrokeWidth = el.StrokeWidth * scale,
                    IsAntialias = true,
                }
                : null;

            result.Add((skPath, fillPaint, strokePaint));
        }

        return result;
    }

    /// <summary>
    /// Builds pre-parsed System.Drawing elements for benchmarking.
    /// </summary>
    internal static List<(GraphicsPath Path, SDSolidBrush Fill, SDPen Stroke)> BuildSystemDrawingElements(
        List<SvgElement> elements,
        float scale)
    {
        List<(GraphicsPath, SDSolidBrush, SDPen)> result = [];
        foreach (SvgElement el in elements)
        {
            GraphicsPath sdPath = SvgPathDataToGraphicsPath(el.PathData, scale, el.Transform);

            Rgba32 fillPixel = el.Fill.ToPixel<Rgba32>();
            SDSolidBrush fill = fillPixel.A > 0
                ? new SDSolidBrush(SDColor.FromArgb(fillPixel.A, fillPixel.R, fillPixel.G, fillPixel.B))
                : null;

            Rgba32 strokePixel = el.Stroke.ToPixel<Rgba32>();
            SDPen stroke = strokePixel.A > 0 && el.StrokeWidth > 0
                ? new SDPen(SDColor.FromArgb(strokePixel.A, strokePixel.R, strokePixel.G, strokePixel.B), el.StrokeWidth * scale)
                : null;

            result.Add((sdPath, fill, stroke));
        }

        return result;
    }

    /// <summary>
    /// Saves a verification image from each backend.
    /// </summary>
    internal static void VerifyOutput(
        string name,
        int width,
        int height,
        SKSurface skSurface,
        Bitmap sdBitmap,
        Image<Rgba32> isImage,
        WebGPURenderTarget webGpuTarget)
    {
        string outDir = System.IO.Path.Combine(AppContext.BaseDirectory, $"{name}-verify");
        Directory.CreateDirectory(outDir);

        // SkiaSharp
        using (SKImage skImage = skSurface.Snapshot())
        using (SKData skData = skImage.Encode(SKEncodedImageFormat.Png, 100))
        using (FileStream fs = File.Create(System.IO.Path.Combine(outDir, $"{name}-skia.png")))
        {
            skData.SaveTo(fs);
        }

        Console.WriteLine($"Saved {name}-skia.png");

        // System.Drawing
        sdBitmap.Save(System.IO.Path.Combine(outDir, $"{name}-systemdrawing.png"));
        Console.WriteLine($"Saved {name}-systemdrawing.png");

        // ImageSharp (CPU)
        isImage.SaveAsPng(System.IO.Path.Combine(outDir, $"{name}-imagesharp.png"));
        Console.WriteLine($"Saved {name}-imagesharp.png");

        // ImageSharp (WebGPU)
        using Image<Rgba32> gpuImage = webGpuTarget.ReadbackImage<Rgba32>();
        gpuImage.SaveAsPng(System.IO.Path.Combine(outDir, $"{name}-webgpu.png"));
        Console.WriteLine($"Saved {name}-webgpu.png");

        Console.WriteLine($"Output saved to: {outDir}");
    }

    // ---- System.Drawing SVG path parser ----
    internal static GraphicsPath SvgPathDataToGraphicsPath(string pathData, float scale, Matrix4x4? elementTransform)
    {
        GraphicsPath gp = new(FillMode.Winding);
        float cx = 0, cy = 0;
        float sx = 0, sy = 0;
        float lcx = 0, lcy = 0;

        ReadOnlySpan<char> span = pathData.AsSpan().Trim();
        char lastCmd = '\0';

        while (span.Length > 0)
        {
            span = span.TrimStart();
            if (span.Length == 0)
            {
                break;
            }

            char ch = span[0];
            char cmd;
            if (char.IsLetter(ch))
            {
                cmd = ch;
                span = span[1..].TrimStart();
                lastCmd = cmd;
            }
            else
            {
                cmd = lastCmd;
                if (cmd == '\0')
                {
                    break;
                }
            }

            bool rel = char.IsLower(cmd);
            char op = char.ToUpperInvariant(cmd);

            switch (op)
            {
                case 'M':
                {
                    float x = ReadFloat(ref span);
                    TrimComma(ref span);
                    float y = ReadFloat(ref span);
                    if (rel)
                    {
                        x += cx;
                        y += cy;
                    }

                    gp.StartFigure();
                    cx = x;
                    cy = y;
                    sx = cx;
                    sy = cy;
                    lcx = cx;
                    lcy = cy;
                    lastCmd = rel ? 'l' : 'L';
                    break;
                }

                case 'L':
                {
                    float x = ReadFloat(ref span);
                    TrimComma(ref span);
                    float y = ReadFloat(ref span);
                    if (rel)
                    {
                        x += cx;
                        y += cy;
                    }

                    gp.AddLine(cx * scale, cy * scale, x * scale, y * scale);
                    cx = x;
                    cy = y;
                    lcx = cx;
                    lcy = cy;
                    break;
                }

                case 'H':
                {
                    float x = ReadFloat(ref span);
                    if (rel)
                    {
                        x += cx;
                    }

                    gp.AddLine(cx * scale, cy * scale, x * scale, cy * scale);
                    cx = x;
                    lcx = cx;
                    lcy = cy;
                    break;
                }

                case 'V':
                {
                    float y = ReadFloat(ref span);
                    if (rel)
                    {
                        y += cy;
                    }

                    gp.AddLine(cx * scale, cy * scale, cx * scale, y * scale);
                    cy = y;
                    lcx = cx;
                    lcy = cy;
                    break;
                }

                case 'C':
                {
                    float x1 = ReadFloat(ref span);
                    TrimComma(ref span);
                    float y1 = ReadFloat(ref span);
                    TrimComma(ref span);
                    float x2 = ReadFloat(ref span);
                    TrimComma(ref span);
                    float y2 = ReadFloat(ref span);
                    TrimComma(ref span);
                    float x = ReadFloat(ref span);
                    TrimComma(ref span);
                    float y = ReadFloat(ref span);
                    if (rel)
                    {
                        x1 += cx;
                        y1 += cy;
                        x2 += cx;
                        y2 += cy;
                        x += cx;
                        y += cy;
                    }

                    gp.AddBezier(cx * scale, cy * scale, x1 * scale, y1 * scale, x2 * scale, y2 * scale, x * scale, y * scale);
                    lcx = x2;
                    lcy = y2;
                    cx = x;
                    cy = y;
                    break;
                }

                case 'S':
                {
                    float x2 = ReadFloat(ref span);
                    TrimComma(ref span);
                    float y2 = ReadFloat(ref span);
                    TrimComma(ref span);
                    float x = ReadFloat(ref span);
                    TrimComma(ref span);
                    float y = ReadFloat(ref span);
                    if (rel)
                    {
                        x2 += cx;
                        y2 += cy;
                        x += cx;
                        y += cy;
                    }

                    float x1 = (2 * cx) - lcx;
                    float y1 = (2 * cy) - lcy;

                    gp.AddBezier(cx * scale, cy * scale, x1 * scale, y1 * scale, x2 * scale, y2 * scale, x * scale, y * scale);
                    lcx = x2;
                    lcy = y2;
                    cx = x;
                    cy = y;
                    break;
                }

                case 'Q':
                {
                    float qx1 = ReadFloat(ref span);
                    TrimComma(ref span);
                    float qy1 = ReadFloat(ref span);
                    TrimComma(ref span);
                    float x = ReadFloat(ref span);
                    TrimComma(ref span);
                    float y = ReadFloat(ref span);
                    if (rel)
                    {
                        qx1 += cx;
                        qy1 += cy;
                        x += cx;
                        y += cy;
                    }

                    float cx1 = cx + (2f / 3f * (qx1 - cx));
                    float cy1 = cy + (2f / 3f * (qy1 - cy));
                    float cx2 = x + (2f / 3f * (qx1 - x));
                    float cy2 = y + (2f / 3f * (qy1 - y));

                    gp.AddBezier(
                        cx * scale, cy * scale,
                        cx1 * scale, cy1 * scale,
                        cx2 * scale, cy2 * scale,
                        x * scale, y * scale);
                    lcx = qx1;
                    lcy = qy1;
                    cx = x;
                    cy = y;
                    break;
                }

                case 'T':
                {
                    float x = ReadFloat(ref span);
                    TrimComma(ref span);
                    float y = ReadFloat(ref span);
                    if (rel)
                    {
                        x += cx;
                        y += cy;
                    }

                    float qx1 = (2 * cx) - lcx;
                    float qy1 = (2 * cy) - lcy;

                    float cx1 = cx + (2f / 3f * (qx1 - cx));
                    float cy1 = cy + (2f / 3f * (qy1 - cy));
                    float cx2 = x + (2f / 3f * (qx1 - x));
                    float cy2 = y + (2f / 3f * (qy1 - y));

                    gp.AddBezier(
                        cx * scale, cy * scale,
                        cx1 * scale, cy1 * scale,
                        cx2 * scale, cy2 * scale,
                        x * scale, y * scale);
                    lcx = qx1;
                    lcy = qy1;
                    cx = x;
                    cy = y;
                    break;
                }

                case 'A':
                {
                    float rx = ReadFloat(ref span);
                    TrimComma(ref span);
                    float ry = ReadFloat(ref span);
                    TrimComma(ref span);
                    float xRotation = ReadFloat(ref span);
                    TrimComma(ref span);
                    float largeArcFlag = ReadFloat(ref span);
                    TrimComma(ref span);
                    float sweepFlag = ReadFloat(ref span);
                    TrimComma(ref span);
                    float x = ReadFloat(ref span);
                    TrimComma(ref span);
                    float y = ReadFloat(ref span);
                    if (rel)
                    {
                        x += cx;
                        y += cy;
                    }

                    AddSvgArc(gp, cx, cy, rx, ry, xRotation, largeArcFlag != 0, sweepFlag != 0, x, y, scale);
                    cx = x;
                    cy = y;
                    lcx = cx;
                    lcy = cy;
                    break;
                }

                case 'Z':
                {
                    gp.CloseFigure();
                    cx = sx;
                    cy = sy;
                    lcx = cx;
                    lcy = cy;
                    break;
                }

                default:
                    if (span.Length > 0)
                    {
                        span = span[1..];
                    }

                    break;
            }

            TrimComma(ref span);
        }

        // Apply per-element transform via System.Drawing matrix.
        if (elementTransform.HasValue)
        {
            Matrix4x4 m = elementTransform.Value;
            using Matrix sdMatrix = new(m.M11, m.M12, m.M21, m.M22, m.M41 * scale, m.M42 * scale);
            gp.Transform(sdMatrix);
        }

        return gp;
    }

    private static float ReadFloat(ref ReadOnlySpan<char> span)
    {
        span = span.TrimStart();
        int len = 0;
        if (len < span.Length && span[len] is '-' or '+')
        {
            len++;
        }

        bool hasDot = false;
        while (len < span.Length)
        {
            char c = span[len];
            if (c == '.' && !hasDot)
            {
                hasDot = true;
                len++;
            }
            else if (char.IsDigit(c))
            {
                len++;
            }
            else if (c is 'e' or 'E')
            {
                len++;
                if (len < span.Length && span[len] is '+' or '-')
                {
                    len++;
                }

                while (len < span.Length && char.IsDigit(span[len]))
                {
                    len++;
                }

                break;
            }
            else
            {
                break;
            }
        }

        float result = float.Parse(span[..len], NumberStyles.Float, CultureInfo.InvariantCulture);
        span = span[len..];
        return result;
    }

    private static void TrimComma(ref ReadOnlySpan<char> span)
    {
        span = span.TrimStart();
        if (span.Length > 0 && span[0] == ',')
        {
            span = span[1..].TrimStart();
        }
    }

    private static void AddSvgArc(
        GraphicsPath gp,
        float x1,
        float y1,
        float rx,
        float ry,
        float xRotationDeg,
        bool largeArc,
        bool sweep,
        float x2,
        float y2,
        float scale)
    {
        float dx = x2 - x1;
        float dy = y2 - y1;
        if ((dx * dx) + (dy * dy) < 1e-10f)
        {
            return;
        }

        rx = MathF.Abs(rx);
        ry = MathF.Abs(ry);
        if (rx < 1e-5f || ry < 1e-5f)
        {
            gp.AddLine(x1 * scale, y1 * scale, x2 * scale, y2 * scale);
            return;
        }

        float xRot = xRotationDeg * MathF.PI / 180f;
        float cosR = MathF.Cos(xRot);
        float sinR = MathF.Sin(xRot);

        float dx2 = (x1 - x2) / 2f;
        float dy2 = (y1 - y2) / 2f;
        float x1p = (cosR * dx2) + (sinR * dy2);
        float y1p = (-sinR * dx2) + (cosR * dy2);

        float rxSq = rx * rx;
        float rySq = ry * ry;
        float x1pSq = x1p * x1p;
        float y1pSq = y1p * y1p;

        float cr = (x1pSq / rxSq) + (y1pSq / rySq);
        if (cr > 1)
        {
            float s = MathF.Sqrt(cr);
            rx *= s;
            ry *= s;
            rxSq = rx * rx;
            rySq = ry * ry;
        }

        float dq = (rxSq * y1pSq) + (rySq * x1pSq);
        float pq = MathF.Max(0, ((rxSq * rySq) - dq) / dq);
        float q = MathF.Sqrt(pq);
        if (largeArc == sweep)
        {
            q = -q;
        }

        float cxp = q * rx * y1p / ry;
        float cyp = -q * ry * x1p / rx;

        float arcCx = (cosR * cxp) - (sinR * cyp) + ((x1 + x2) / 2f);
        float arcCy = (sinR * cxp) + (cosR * cyp) + ((y1 + y2) / 2f);

        float theta = SvgAngle(1, 0, (x1p - cxp) / rx, (y1p - cyp) / ry);
        float delta = SvgAngle((x1p - cxp) / rx, (y1p - cyp) / ry, (-x1p - cxp) / rx, (-y1p - cyp) / ry);
        delta %= MathF.PI * 2;

        if (!sweep && delta > 0)
        {
            delta -= 2 * MathF.PI;
        }

        if (sweep && delta < 0)
        {
            delta += 2 * MathF.PI;
        }

        float t = theta;
        float remain = MathF.Abs(delta);
        float sign = delta < 0 ? -1f : 1f;
        float prevX = x1, prevY = y1;

        while (remain > 1e-5f)
        {
            float step = MathF.Min(remain, MathF.PI / 4f);
            float signStep = step * sign;
            float alphaT = MathF.Tan(signStep / 2f);
            float alpha = MathF.Sin(signStep) * (MathF.Sqrt(4f + (3f * alphaT * alphaT)) - 1f) / 3f;

            float p2x = arcCx + (rx * MathF.Cos(xRot) * MathF.Cos(t + signStep)) - (ry * MathF.Sin(xRot) * MathF.Sin(t + signStep));
            float p2y = arcCy + (rx * MathF.Sin(xRot) * MathF.Cos(t + signStep)) + (ry * MathF.Cos(xRot) * MathF.Sin(t + signStep));

            float d1x = (-rx * MathF.Cos(xRot) * MathF.Sin(t)) - (ry * MathF.Sin(xRot) * MathF.Cos(t));
            float d1y = (-rx * MathF.Sin(xRot) * MathF.Sin(t)) + (ry * MathF.Cos(xRot) * MathF.Cos(t));
            float d2x = (-rx * MathF.Cos(xRot) * MathF.Sin(t + signStep)) - (ry * MathF.Sin(xRot) * MathF.Cos(t + signStep));
            float d2y = (-rx * MathF.Sin(xRot) * MathF.Sin(t + signStep)) + (ry * MathF.Cos(xRot) * MathF.Cos(t + signStep));

            float cp1x = prevX + (alpha * d1x);
            float cp1y = prevY + (alpha * d1y);
            float cp2x = p2x - (alpha * d2x);
            float cp2y = p2y - (alpha * d2y);

            gp.AddBezier(
                prevX * scale, prevY * scale,
                cp1x * scale, cp1y * scale,
                cp2x * scale, cp2y * scale,
                p2x * scale, p2y * scale);

            prevX = p2x;
            prevY = p2y;
            t += signStep;
            remain -= step;
        }
    }

    private static float SvgAngle(float ux, float uy, float vx, float vy)
    {
        float dot = (ux * vx) + (uy * vy);
        float len = MathF.Sqrt(((ux * ux) + (uy * uy)) * ((vx * vx) + (vy * vy)));
        float ang = MathF.Acos(Math.Clamp(dot / len, -1f, 1f));
        if ((ux * vy) - (uy * vx) < 0)
        {
            ang = -ang;
        }

        return ang;
    }
}
