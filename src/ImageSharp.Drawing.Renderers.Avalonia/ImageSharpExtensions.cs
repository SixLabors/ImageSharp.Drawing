// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.IO.Compression;
using System.Numerics;
using Avalonia.Media.Imaging;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Metadata;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Processing.Processors.Transforms;

namespace SixLabors.ImageSharp.Drawing.Renderers.Avalonia;

/// <summary>
/// Conversions between Avalonia and ImageSharp primitive types.
/// </summary>
internal static class ImageSharpExtensions
{
    /// <summary>
    /// Saves an ImageSharp image using the format and settings specified by Avalonia encoder options.
    /// </summary>
    /// <param name="image">The image to save.</param>
    /// <param name="stream">The stream to which the encoded image is written.</param>
    /// <param name="options">The encoder options that select the output format and its settings.</param>
    public static void Save(this Image image, Stream stream, BitmapEncoderOptions options)
    {
        ImageEncoder encoder = options switch
        {
            PngBitmapEncoderOptions pngOptions => new PngEncoder
            {
                CompressionLevel = pngOptions.CompressionLevel switch
                {
                    CompressionLevel.Fastest => PngCompressionLevel.BestSpeed,
                    CompressionLevel.NoCompression => PngCompressionLevel.NoCompression,
                    CompressionLevel.SmallestSize => PngCompressionLevel.BestCompression,
                    _ => PngCompressionLevel.DefaultCompression
                }
            },
            JpegBitmapEncoderOptions jpegOptions => new JpegEncoder
            {
                Quality = Math.Clamp(jpegOptions.Quality, 1, 100)
            },
            _ => throw new ArgumentOutOfRangeException(nameof(options), options, "Unknown encoder options type")
        };

        image.Save(stream, encoder);
    }

    /// <summary>
    /// Converts an Avalonia colour to an ImageSharp colour with the supplied opacity applied.
    /// </summary>
    /// <param name="color">The Avalonia colour.</param>
    /// <param name="opacity">The opacity multiplier.</param>
    /// <returns>The converted ImageSharp colour.</returns>
    public static Color ToColor(this AvaloniaColor color, double opacity = 1)
    {
        byte alpha = (byte)Math.Clamp(color.A * opacity, 0, 255);
        return Color.FromPixel(new Bgra32(color.R, color.G, color.B, alpha));
    }

    /// <summary>
    /// Converts ImageSharp metadata resolution to Avalonia DPI.
    /// </summary>
    /// <param name="metadata">The ImageSharp image metadata.</param>
    /// <returns>The converted Avalonia DPI.</returns>
    public static AvaloniaVector ToDpi(this ImageMetadata metadata)
        => metadata.ResolutionUnits switch
        {
            PixelResolutionUnit.PixelsPerCentimeter => new AvaloniaVector(metadata.HorizontalResolution * 2.54, metadata.VerticalResolution * 2.54),
            PixelResolutionUnit.PixelsPerMeter => new AvaloniaVector(metadata.HorizontalResolution * 0.0254, metadata.VerticalResolution * 0.0254),
            PixelResolutionUnit.PixelsPerInch => new AvaloniaVector(metadata.HorizontalResolution, metadata.VerticalResolution),
            _ => new AvaloniaVector(ImageMetadata.DefaultHorizontalResolution, ImageMetadata.DefaultVerticalResolution)
        };

    /// <summary>
    /// Converts an Avalonia point to an ImageSharp point.
    /// </summary>
    /// <param name="point">The Avalonia point.</param>
    /// <returns>The converted ImageSharp point.</returns>
    public static PointF ToPointF(this AvaloniaPoint point) => new((float)point.X, (float)point.Y);

    /// <summary>
    /// Converts an Avalonia size to an ImageSharp size.
    /// </summary>
    /// <param name="size">The Avalonia size.</param>
    /// <returns>The converted ImageSharp size.</returns>
    public static SizeF ToSizeF(this AvaloniaSize size) => new((float)size.Width, (float)size.Height);

    /// <summary>
    /// Converts an Avalonia rectangle to an ImageSharp rectangle.
    /// </summary>
    /// <param name="rect">The Avalonia rectangle.</param>
    /// <returns>The converted ImageSharp rectangle.</returns>
    public static RectangleF ToRectangleF(this AvaloniaRect rect) => new((float)rect.X, (float)rect.Y, (float)rect.Width, (float)rect.Height);

    /// <summary>
    /// Converts an ImageSharp rectangle to an Avalonia rectangle.
    /// </summary>
    /// <param name="rect">The ImageSharp rectangle.</param>
    /// <returns>The converted Avalonia rectangle.</returns>
    public static AvaloniaRect ToAvaloniaRect(this RectangleF rect) => new(rect.X, rect.Y, rect.Width, rect.Height);

    /// <summary>
    /// Converts an Avalonia fill rule to the equivalent ImageSharp intersection rule.
    /// </summary>
    /// <param name="fillRule">The Avalonia fill rule.</param>
    /// <returns>The converted ImageSharp intersection rule.</returns>
    public static IntersectionRule ToIntersectionRule(this FillRule fillRule)
        => fillRule == FillRule.NonZero ? IntersectionRule.NonZero : IntersectionRule.EvenOdd;

    /// <summary>
    /// Converts an Avalonia bitmap interpolation mode to the closest ImageSharp resampler.
    /// </summary>
    /// <param name="interpolationMode">The Avalonia interpolation mode.</param>
    /// <param name="isUpscaling">Whether the image is being enlarged.</param>
    /// <returns>The converted ImageSharp resampler.</returns>
    public static IResampler ToResampler(this BitmapInterpolationMode interpolationMode, bool isUpscaling)
        => interpolationMode switch
        {
            BitmapInterpolationMode.None => KnownResamplers.NearestNeighbor,
            BitmapInterpolationMode.HighQuality when isUpscaling => KnownResamplers.MitchellNetravali,
            _ => KnownResamplers.Triangle
        };

    /// <summary>
    /// Converts an Avalonia bitmap blending mode to an ImageSharp alpha composition mode.
    /// </summary>
    /// <param name="blendingMode">The Avalonia bitmap blending mode.</param>
    /// <returns>The converted ImageSharp alpha composition mode.</returns>
    public static PixelAlphaCompositionMode ToAlphaCompositionMode(this BitmapBlendingMode blendingMode)
        => blendingMode switch
        {
            BitmapBlendingMode.Source => PixelAlphaCompositionMode.Src,
            BitmapBlendingMode.SourceAtop => PixelAlphaCompositionMode.SrcAtop,
            BitmapBlendingMode.SourceIn => PixelAlphaCompositionMode.SrcIn,
            BitmapBlendingMode.SourceOut => PixelAlphaCompositionMode.SrcOut,
            BitmapBlendingMode.Destination => PixelAlphaCompositionMode.Dest,
            BitmapBlendingMode.DestinationAtop => PixelAlphaCompositionMode.DestAtop,
            BitmapBlendingMode.DestinationOver => PixelAlphaCompositionMode.DestOver,
            BitmapBlendingMode.DestinationIn => PixelAlphaCompositionMode.DestIn,
            BitmapBlendingMode.DestinationOut => PixelAlphaCompositionMode.DestOut,
            BitmapBlendingMode.Xor => PixelAlphaCompositionMode.Xor,

            // TODO: Implement missing modes. Hue etc.
            _ => PixelAlphaCompositionMode.SrcOver
        };

    /// <summary>
    /// Converts an Avalonia matrix to an ImageSharp-compatible matrix.
    /// </summary>
    /// <param name="matrix">The Avalonia matrix.</param>
    /// <returns>The converted matrix.</returns>
    public static Matrix4x4 ToMatrix4x4(this AvaloniaMatrix matrix)
        => new(
            (float)matrix.M11,
            (float)matrix.M12,
            0,
            0,
            (float)matrix.M21,
            (float)matrix.M22,
            0,
            0,
            0,
            0,
            1,
            0,
            (float)matrix.M31,
            (float)matrix.M32,
            0,
            1);

    /// <summary>
    /// Converts an Avalonia rectangle to an integer ImageSharp rectangle.
    /// </summary>
    /// <param name="r">The Avalonia rectangle.</param>
    /// <returns>The converted ImageSharp rectangle.</returns>
    public static Rectangle ToRectangle(this AvaloniaRect r) => (Rectangle)r.ToRectangleF();

    /// <summary>
    /// Converts an Avalonia rounded rectangle to an ImageSharp rounded rectangle polygon.
    /// </summary>
    /// <param name="rect">The Avalonia rounded rectangle.</param>
    /// <returns>The converted ImageSharp rounded rectangle polygon.</returns>
    public static RoundedRectanglePolygon ToRoundedRectanglePath(this AvaloniaRoundedRect rect)
        => new(
            rect.Rect.ToRectangleF(),
            new SizeF((float)rect.RadiiTopLeft.X, (float)rect.RadiiTopLeft.Y),
            new SizeF((float)rect.RadiiTopRight.X, (float)rect.RadiiTopRight.Y),
            new SizeF((float)rect.RadiiBottomRight.X, (float)rect.RadiiBottomRight.Y),
            new SizeF((float)rect.RadiiBottomLeft.X, (float)rect.RadiiBottomLeft.Y));

    /// <summary>
    /// Converts an Avalonia gradient spread method to an ImageSharp gradient repetition mode.
    /// </summary>
    /// <param name="spreadMethod">The Avalonia spread method.</param>
    /// <returns>The converted ImageSharp repetition mode.</returns>
    public static GradientRepetitionMode ToGradientRepetitionMode(this GradientSpreadMethod spreadMethod)
        => spreadMethod switch
        {
            GradientSpreadMethod.Reflect => GradientRepetitionMode.Reflect,
            GradientSpreadMethod.Repeat => GradientRepetitionMode.Repeat,
            _ => GradientRepetitionMode.None
        };

    /// <summary>
    /// Converts an Avalonia geometry combine mode to an ImageSharp boolean operation.
    /// </summary>
    /// <param name="combineMode">The Avalonia combine mode.</param>
    /// <returns>The converted ImageSharp boolean operation.</returns>
    public static BooleanOperation ToBooleanOperation(this GeometryCombineMode combineMode)
        => combineMode switch
        {
            GeometryCombineMode.Intersect => BooleanOperation.Intersection,
            GeometryCombineMode.Xor => BooleanOperation.Xor,
            GeometryCombineMode.Exclude => BooleanOperation.Difference,
            _ => BooleanOperation.Union
        };

    /// <summary>
    /// Converts an Avalonia pen to ImageSharp stroke options.
    /// </summary>
    /// <param name="pen">The Avalonia pen.</param>
    /// <returns>The converted ImageSharp stroke options.</returns>
    public static StrokeOptions ToStrokeOptions(this IPen pen)
        => new()
        {
            MiterLimit = pen.MiterLimit,
            LineCap = pen.LineCap switch
            {
                PenLineCap.Round => LineCap.Round,
                PenLineCap.Square => LineCap.Square,
                _ => LineCap.Butt
            },
            LineJoin = pen.LineJoin switch
            {
                PenLineJoin.Round => LineJoin.Round,
                PenLineJoin.Bevel => LineJoin.Bevel,
                _ => LineJoin.Miter
            }
        };

    /// <summary>
    /// Converts an Avalonia dash style to an ImageSharp stroke pattern.
    /// </summary>
    /// <param name="pen">The Avalonia pen.</param>
    /// <returns>The converted stroke pattern.</returns>
    public static float[] ToStrokePattern(this IPen pen)
    {
        IReadOnlyList<double>? dashes = pen.DashStyle?.Dashes;
        if (dashes is null || dashes.Count < 2)
        {
            return [];
        }

        float[] pattern = new float[dashes.Count];
        for (int i = 0; i < pattern.Length; i++)
        {
            pattern[i] = (float)dashes[i];
        }

        return pattern;
    }

    /// <summary>
    /// Converts an Avalonia dash offset to an ImageSharp stroke pattern offset.
    /// </summary>
    /// <param name="pen">The Avalonia pen.</param>
    /// <returns>The converted stroke pattern offset.</returns>
    public static float ToStrokePatternOffset(this IPen pen)
        => pen.DashStyle is null ? 0 : (float)pen.DashStyle.Offset;

    /// <summary>
    /// Creates a stroked ImageSharp path from the supplied source path and Avalonia pen.
    /// </summary>
    /// <param name="path">The source ImageSharp path.</param>
    /// <param name="pen">The Avalonia pen.</param>
    /// <returns>The stroked ImageSharp path.</returns>
    public static IPath CreateStrokePath(this IPath path, IPen pen)
    {
        float[] pattern = pen.ToStrokePattern();
        StrokeOptions options = pen.ToStrokeOptions();

        return pattern.Length >= 2
            ? path.GenerateOutline((float)pen.Thickness, pattern, pen.ToStrokePatternOffset(), options)
            : path.GenerateOutline((float)pen.Thickness, options);
    }

    /// <summary>
    /// Converts an Avalonia pen and resolved brush to an ImageSharp pen.
    /// </summary>
    /// <param name="pen">The Avalonia pen.</param>
    /// <param name="brush">The resolved ImageSharp brush.</param>
    /// <returns>The converted ImageSharp pen.</returns>
    public static Pen ToPen(this IPen pen, Brush brush)
    {
        float[] pattern = pen.ToStrokePattern();
        PenOptions options = new(brush, (float)pen.Thickness, pattern)
        {
            StrokePatternOffset = pen.ToStrokePatternOffset(),
            StrokeOptions = pen.ToStrokeOptions()
        };

        return pattern.Length >= 2 ? new PatternPen(options) : new SolidPen(options);
    }

    /// <summary>
    /// Creates an ImageSharp font for the supplied family name and size.
    /// </summary>
    /// <param name="familyName">The font family name.</param>
    /// <param name="size">The font size.</param>
    /// <returns>The created ImageSharp font.</returns>
    public static Font ToFont(this string familyName, float size)
    {
        if (SystemFonts.TryGet(familyName, out FontFamily family))
        {
            return family.CreateFont(size);
        }

        return SystemFonts.CreateFont("Segoe UI", size);
    }
}
