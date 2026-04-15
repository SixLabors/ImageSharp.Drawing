// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Numerics;
using System.Xml.Linq;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Drawing.Processing.Backends;
using SixLabors.ImageSharp.PixelFormats;
using SkiaSharp;
using ISColor = SixLabors.ImageSharp.Color;
using ISDrawingProcessing = SixLabors.ImageSharp.Drawing.Processing;
using SDColor = System.Drawing.Color;
using SDPen = System.Drawing.Pen;
using SDSolidBrush = System.Drawing.SolidBrush;

namespace SixLabors.ImageSharp.Drawing.Tests;

/// <summary>
/// Shared SVG parsing and per-backend setup for SVG rendering benchmarks.
/// </summary>
internal static class SvgBenchmarkHelper
{
    private const float NeighborhoodPadding = 12F;

    /// <summary>
    /// A single parsed SVG path element with fill, stroke, and per-element transform.
    /// </summary>
    internal readonly record struct SvgElement(
        string PathData,
        ISColor Fill,
        ISColor Stroke,
        float StrokeWidth,
        Matrix4x4? Transform);

    /// <summary>
    /// Parses an SVG file into a list of <see cref="SvgElement"/>s.
    /// Handles fill, fill-opacity, stroke, stroke-width, opacity, and per-path transform attributes.
    /// Group-level transforms are composed with per-path transforms.
    /// </summary>
    internal static List<SvgElement> ParseSvg(string filePath)
    {
        XDocument doc = XDocument.Load(filePath);
        XNamespace ns = "http://www.w3.org/2000/svg";
        List<SvgElement> result = [];

        // Collect group transforms into a stack.
        // For simplicity, iterate all path elements and walk up to resolve inherited transforms.
        foreach (XElement pathEl in doc.Descendants(ns + "path"))
        {
            string d = pathEl.Attribute("d")?.Value;
            if (string.IsNullOrWhiteSpace(d))
            {
                continue;
            }

            // Parse fill color (default black per SVG spec).
            string fillStr = ResolveInheritedPresentationValue(pathEl, "fill");
            ISColor fill;
            if (fillStr is null)
            {
                fill = ISColor.Black;
            }
            else if (fillStr == "none")
            {
                fill = ISColor.Transparent;
            }
            else if (ISColor.TryParse(fillStr, out ISColor parsed))
            {
                fill = parsed;
            }
            else
            {
                fill = ISColor.Black;
            }

            // Apply fill-opacity.
            if (float.TryParse(
                    ResolveInheritedPresentationValue(pathEl, "fill-opacity"),
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out float fillOpacity))
            {
                Rgba32 fp = fill.ToPixel<Rgba32>();
                fp.A = (byte)(fp.A * Math.Clamp(fillOpacity, 0f, 1f));
                fill = ISColor.FromPixel(fp);
            }

            // Apply element-level opacity to fill alpha.
            if (float.TryParse(
                    pathEl.Attribute("opacity")?.Value,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out float opacity))
            {
                Rgba32 fp = fill.ToPixel<Rgba32>();
                fp.A = (byte)(fp.A * Math.Clamp(opacity, 0f, 1f));
                fill = ISColor.FromPixel(fp);
            }

            // Parse stroke.
            ISColor stroke = ParseColor(ResolveInheritedPresentationValue(pathEl, "stroke"));
            float strokeWidth = float.TryParse(
                ResolveInheritedPresentationValue(pathEl, "stroke-width"),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out float sw) ? sw : 0f;

            // Apply element-level opacity to stroke alpha.
            if (opacity > 0 && opacity < 1)
            {
                Rgba32 sp = stroke.ToPixel<Rgba32>();
                sp.A = (byte)(sp.A * Math.Clamp(opacity, 0f, 1f));
                stroke = ISColor.FromPixel(sp);
            }

            // Resolve transform: compose per-path transform with ancestor group transforms.
            Matrix4x4? transform = ResolveTransform(pathEl, ns);

            result.Add(new SvgElement(d, fill, stroke, strokeWidth, transform));
        }

        return result;
    }

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
    /// Builds pre-parsed ImageSharp elements for benchmarking.
    /// </summary>
    internal static List<(IPath Path, ISDrawingProcessing.SolidBrush Fill, SolidPen Stroke)> BuildImageSharpElements(
        List<SvgElement> elements,
        float scale)
    {
        List<(IPath, ISDrawingProcessing.SolidBrush, SolidPen)> result = [];
        foreach (SvgElement el in elements)
        {
            if (!Path.TryParseSvgPath(el.PathData, out IPath isPath))
            {
                continue;
            }

            Matrix3x2 scaleMatrix = Matrix3x2.CreateScale(scale);
            if (el.Transform.HasValue)
            {
                isPath = isPath.Transform(el.Transform.Value * scaleMatrix);
            }
            else
            {
                isPath = isPath.Transform(scaleMatrix);
            }

            Rgba32 fillPixel = el.Fill.ToPixel<Rgba32>();
            ISDrawingProcessing.SolidBrush fill = fillPixel.A > 0
                ? new ISDrawingProcessing.SolidBrush(el.Fill)
                : null;

            Rgba32 strokePixel = el.Stroke.ToPixel<Rgba32>();
            SolidPen stroke = strokePixel.A > 0 && el.StrokeWidth > 0
                ? new SolidPen(el.Stroke, el.StrokeWidth * scale)
                : null;

            result.Add((isPath, fill, stroke));
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
        nint webGpuTextureHandle)
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
        if (WebGPUTextureTransfer.TryReadTexture(
                webGpuTextureHandle,
                width,
                height,
                out Image<Rgba32> gpuImage,
                out string readError))
        {
            gpuImage.SaveAsPng(System.IO.Path.Combine(outDir, $"{name}-webgpu.png"));
            gpuImage.Dispose();
            Console.WriteLine($"Saved {name}-webgpu.png");
        }
        else
        {
            Console.WriteLine($"WebGPU readback failed: {readError}");
        }

        Console.WriteLine($"Output saved to: {outDir}");
    }

    /// <summary>
    /// Writes a spatial neighborhood SVG for the requested path using the parsed SVG elements.
    /// </summary>
    internal static void WriteNeighborhoodSvg(
        string name,
        IReadOnlyList<SvgElement> elements,
        string targetPathData,
        int width,
        int height)
    {
        string outDir = System.IO.Path.Combine(AppContext.BaseDirectory, $"{name}-verify");
        Directory.CreateDirectory(outDir);

        List<(SvgElement Element, RectangleF Bounds)> candidates = [];
        (SvgElement Element, RectangleF Bounds)? target = null;

        foreach (SvgElement element in elements)
        {
            if (!TryGetTransformedBounds(element, out RectangleF bounds))
            {
                continue;
            }

            candidates.Add((element, bounds));

            if (target is null && string.Equals(element.PathData, targetPathData, StringComparison.Ordinal))
            {
                target = (element, bounds);
            }
        }

        if (target is null)
        {
            return;
        }

        RectangleF viewport = Inflate(target.Value.Bounds, NeighborhoodPadding);
        List<SvgElement> neighborhood = [];
        foreach ((SvgElement element, RectangleF bounds) in candidates)
        {
            if (bounds.IntersectsWith(viewport))
            {
                neighborhood.Add(element);
                viewport = RectangleF.Union(viewport, bounds);
            }
        }

        viewport = RectangleF.Intersect(Inflate(viewport, NeighborhoodPadding), new RectangleF(0, 0, width, height));

        XNamespace ns = "http://www.w3.org/2000/svg";
        XElement svg = new(
            ns + "svg",
            new XAttribute("xmlns", ns.NamespaceName),
            new XAttribute("viewBox", FormattableString.Invariant($"{viewport.X} {viewport.Y} {viewport.Width} {viewport.Height}")),
            new XAttribute("width", FormattableString.Invariant($"{viewport.Width}")),
            new XAttribute("height", FormattableString.Invariant($"{viewport.Height}")));

        foreach (SvgElement element in neighborhood)
        {
            svg.Add(CreatePathElement(ns, element));
        }

        XDocument document = new(new XDeclaration("1.0", "utf-8", null), svg);
        document.Save(System.IO.Path.Combine(outDir, $"{name}-neighborhood.svg"));
    }

    // ---- SVG transform resolution ----

    private static Matrix4x4? ResolveTransform(XElement element, XNamespace ns)
    {
        // Walk up the tree, collecting transforms from path → root.
        List<Matrix4x4> transforms = null;
        XElement current = element;
        while (current is not null)
        {
            string transformStr = current.Attribute("transform")?.Value;
            if (transformStr is not null && TryParseTransform(transformStr, out Matrix4x4 m))
            {
                transforms ??= [];
                transforms.Add(m);
            }

            current = current.Parent;
        }

        if (transforms is null)
        {
            return null;
        }

        // Compose from root → leaf (reverse of collection order).
        Matrix4x4 result = Matrix4x4.Identity;
        for (int i = transforms.Count - 1; i >= 0; i--)
        {
            result *= transforms[i];
        }

        return result;
    }

    private static bool TryParseTransform(string value, out Matrix4x4 result)
    {
        result = Matrix4x4.Identity;
        ReadOnlySpan<char> span = value.AsSpan().Trim();

        if (span.StartsWith("matrix(") && span.EndsWith(")"))
        {
            span = span[7..^1];
            Span<float> values = stackalloc float[6];
            for (int i = 0; i < 6; i++)
            {
                span = span.TrimStart();
                if (span.Length > 0 && span[0] == ',')
                {
                    span = span[1..].TrimStart();
                }

                int end = 0;
                if (end < span.Length && (span[end] is '-' or '+'))
                {
                    end++;
                }

                bool hasDot = false;
                while (end < span.Length)
                {
                    char c = span[end];
                    if (c == '.' && !hasDot)
                    {
                        hasDot = true;
                        end++;
                    }
                    else if (char.IsDigit(c))
                    {
                        end++;
                    }
                    else if (c is 'e' or 'E')
                    {
                        end++;
                        if (end < span.Length && span[end] is '+' or '-')
                        {
                            end++;
                        }

                        while (end < span.Length && char.IsDigit(span[end]))
                        {
                            end++;
                        }

                        break;
                    }
                    else
                    {
                        break;
                    }
                }

                if (end == 0)
                {
                    return false;
                }

                values[i] = float.Parse(span[..end], CultureInfo.InvariantCulture);
                span = span[end..];
            }

            // SVG matrix(a,b,c,d,e,f) maps to:
            // | a c e |     M11=a  M21=c  M41=e
            // | b d f |  →  M12=b  M22=d  M42=f
            // | 0 0 1 |     rest = identity
            result = new Matrix4x4(
                values[0], values[1], 0, 0,
                values[2], values[3], 0, 0,
                0, 0, 1, 0,
                values[4], values[5], 0, 1);
            return true;
        }

        if (span.StartsWith("translate(") && span.EndsWith(")"))
        {
            span = span[10..^1];
            ReadOnlySpan<char> trimmed = span.Trim();
            int sep = trimmed.IndexOfAny(',', ' ');
            float tx = float.Parse(sep < 0 ? trimmed : trimmed[..sep], CultureInfo.InvariantCulture);
            float ty = sep < 0 ? 0 : float.Parse(trimmed[(sep + 1)..].Trim(), CultureInfo.InvariantCulture);
            result = Matrix4x4.CreateTranslation(tx, ty, 0);
            return true;
        }

        if (span.StartsWith("scale(") && span.EndsWith(")"))
        {
            span = span[6..^1];
            ReadOnlySpan<char> trimmed = span.Trim();
            int sep = trimmed.IndexOfAny(',', ' ');
            float sx = float.Parse(sep < 0 ? trimmed : trimmed[..sep], CultureInfo.InvariantCulture);
            float sy = sep < 0 ? sx : float.Parse(trimmed[(sep + 1)..].Trim(), CultureInfo.InvariantCulture);
            result = Matrix4x4.CreateScale(sx, sy, 1);
            return true;
        }

        return false;
    }

    private static ISColor ParseColor(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value == "none")
        {
            return ISColor.Transparent;
        }

        return ISColor.TryParse(value, out ISColor color) ? color : ISColor.Transparent;
    }

    private static string ResolveInheritedPresentationValue(XElement element, string attributeName)
    {
        for (XElement current = element; current is not null; current = current.Parent)
        {
            if (TryGetPresentationValue(current, attributeName, out string value))
            {
                return value;
            }
        }

        return null;
    }

    private static bool TryGetPresentationValue(XElement element, string attributeName, out string value)
    {
        XAttribute attribute = element.Attribute(attributeName);
        if (attribute is not null)
        {
            value = attribute.Value;
            return true;
        }

        string style = element.Attribute("style")?.Value;
        if (TryGetStyleValue(style, attributeName, out value))
        {
            return true;
        }

        value = null;
        return false;
    }

    private static bool TryGetStyleValue(string style, string attributeName, out string value)
    {
        if (string.IsNullOrWhiteSpace(style))
        {
            value = null;
            return false;
        }

        foreach (string entry in style.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            int separatorIndex = entry.IndexOf(':');
            if (separatorIndex <= 0 || separatorIndex >= entry.Length - 1)
            {
                continue;
            }

            if (!entry[..separatorIndex].Equals(attributeName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            value = entry[(separatorIndex + 1)..].Trim();
            return true;
        }

        value = null;
        return false;
    }

    private static XElement CreatePathElement(XNamespace ns, SvgElement element)
    {
        XElement path = new(
            ns + "path",
            new XAttribute("d", element.PathData));

        Rgba32 fillPixel = element.Fill.ToPixel<Rgba32>();
        if (fillPixel.A == 0)
        {
            path.SetAttributeValue("fill", "none");
        }
        else
        {
            path.SetAttributeValue("fill", ToSvgColor(fillPixel));
            if (fillPixel.A < byte.MaxValue)
            {
                path.SetAttributeValue("fill-opacity", FormattableString.Invariant($"{fillPixel.A / 255F:0.######}"));
            }
        }

        Rgba32 strokePixel = element.Stroke.ToPixel<Rgba32>();
        if (strokePixel.A == 0 || element.StrokeWidth <= 0)
        {
            path.SetAttributeValue("stroke", "none");
        }
        else
        {
            path.SetAttributeValue("stroke", ToSvgColor(strokePixel));
            path.SetAttributeValue("stroke-width", FormattableString.Invariant($"{element.StrokeWidth:0.######}"));
            if (strokePixel.A < byte.MaxValue)
            {
                path.SetAttributeValue("stroke-opacity", FormattableString.Invariant($"{strokePixel.A / 255F:0.######}"));
            }
        }

        if (element.Transform is Matrix4x4 transform)
        {
            path.SetAttributeValue(
                "transform",
                FormattableString.Invariant(
                    $"matrix({transform.M11:0.######} {transform.M12:0.######} {transform.M21:0.######} {transform.M22:0.######} {transform.M41:0.######} {transform.M42:0.######})"));
        }

        return path;
    }

    private static RectangleF Inflate(RectangleF rectangle, float amount) =>
        new(
            rectangle.X - amount,
            rectangle.Y - amount,
            rectangle.Width + (amount * 2),
            rectangle.Height + (amount * 2));

    private static string ToSvgColor(Rgba32 pixel) =>
        FormattableString.Invariant($"#{pixel.R:x2}{pixel.G:x2}{pixel.B:x2}");

    private static bool TryGetTransformedBounds(SvgElement element, out RectangleF bounds)
    {
        bounds = default;
        if (!Path.TryParseSvgPath(element.PathData, out IPath path))
        {
            return false;
        }

        if (element.Transform is Matrix4x4 transform)
        {
            path = path.Transform(transform);
        }

        bounds = path.Bounds;
        return true;
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

                    gp.AddBezier(
                        cx * scale, cy * scale,
                        x1 * scale, y1 * scale,
                        x2 * scale, y2 * scale,
                        x * scale, y * scale);
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

                    gp.AddBezier(
                        cx * scale, cy * scale,
                        x1 * scale, y1 * scale,
                        x2 * scale, y2 * scale,
                        x * scale, y * scale);
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
