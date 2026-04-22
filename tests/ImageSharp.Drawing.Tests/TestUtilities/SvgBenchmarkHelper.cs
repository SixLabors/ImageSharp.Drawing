// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Numerics;
using System.Xml.Linq;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using ISColor = SixLabors.ImageSharp.Color;
using ISDrawingProcessing = SixLabors.ImageSharp.Drawing.Processing;

namespace SixLabors.ImageSharp.Drawing.Tests;

/// <summary>
/// Shared SVG parsing and ImageSharp element construction for SVG rendering benchmarks and samples.
/// The SkiaSharp- and System.Drawing-specific builders live in <c>SvgBenchmarkHelper.Backends.cs</c>.
/// </summary>
internal static partial class SvgBenchmarkHelper
{
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
            string? d = pathEl.Attribute("d")?.Value;
            if (string.IsNullOrWhiteSpace(d))
            {
                continue;
            }

            // Parse fill color (default black per SVG spec).
            string? fillStr = ResolveInheritedPresentationValue(pathEl, "fill");
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
            if (opacity is > 0 and < 1)
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
    /// Builds pre-parsed ImageSharp elements for benchmarking.
    /// </summary>
    internal static List<(IPath Path, ISDrawingProcessing.SolidBrush? Fill, SolidPen? Stroke)> BuildImageSharpElements(
        List<SvgElement> elements,
        float scale)
    {
        List<(IPath, ISDrawingProcessing.SolidBrush?, SolidPen?)> result = [];
        foreach (SvgElement el in elements)
        {
            if (!Path.TryParseSvgPath(el.PathData, out IPath? isPath))
            {
                continue;
            }

            Matrix4x4 scaleMatrix = Matrix4x4.CreateScale(scale, scale, 1);
            if (el.Transform.HasValue)
            {
                isPath = isPath.Transform(el.Transform.Value * scaleMatrix);
            }
            else
            {
                isPath = isPath.Transform(scaleMatrix);
            }

            Rgba32 fillPixel = el.Fill.ToPixel<Rgba32>();
            ISDrawingProcessing.SolidBrush? fill = fillPixel.A > 0
                ? new ISDrawingProcessing.SolidBrush(el.Fill)
                : null;

            Rgba32 strokePixel = el.Stroke.ToPixel<Rgba32>();
            SolidPen? stroke = strokePixel.A > 0 && el.StrokeWidth > 0
                ? new SolidPen(el.Stroke, el.StrokeWidth * scale)
                : null;

            result.Add((isPath, fill, stroke));
        }

        return result;
    }

    // ---- SVG transform resolution ----
    private static Matrix4x4? ResolveTransform(XElement element, XNamespace ns)
    {
        // Walk up the tree, collecting transforms from path → root.
        List<Matrix4x4>? transforms = null;
        XElement? current = element;
        while (current is not null)
        {
            string? transformStr = current.Attribute("transform")?.Value;
            if (transformStr is not null && TryParseTransform(transformStr, out Matrix4x4 m))
            {
                transforms ??= [];
                transforms.Add(m);
            }

            current = current?.Parent;
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
                values[0],
                values[1],
                0,
                0,
                values[2],
                values[3],
                0,
                0,
                0,
                0,
                1,
                0,
                values[4],
                values[5],
                0,
                1);

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

    private static ISColor ParseColor(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value == "none")
        {
            return ISColor.Transparent;
        }

        return ISColor.TryParse(value, out ISColor color) ? color : ISColor.Transparent;
    }

    private static string? ResolveInheritedPresentationValue(XElement element, string attributeName)
    {
        for (XElement? current = element; current is not null; current = current?.Parent)
        {
            if (TryGetPresentationValue(current, attributeName, out string? value))
            {
                return value;
            }
        }

        return null;
    }

    private static bool TryGetPresentationValue(XElement element, string attributeName, [NotNullWhen(true)] out string? value)
    {
        XAttribute? attribute = element.Attribute(attributeName);
        if (attribute is not null)
        {
            value = attribute.Value;
            return true;
        }

        string? style = element.Attribute("style")?.Value;
        if (TryGetStyleValue(style, attributeName, out value))
        {
            return true;
        }

        value = null;
        return false;
    }

    private static bool TryGetStyleValue(string? style, string attributeName, [NotNullWhen(true)] out string? value)
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
}
