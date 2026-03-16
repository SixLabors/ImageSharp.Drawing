// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using System.Xml.Linq;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Drawing.Processing.Backends;
using SixLabors.ImageSharp.Memory;
using SixLabors.ImageSharp.PixelFormats;

namespace SixLabors.ImageSharp.Drawing.Diagnostics;

/// <summary>
/// Profiles the CPU drawing pipeline for SVG scenes with solid fills.
/// </summary>
public static class SvgPerformanceProfiler
{
    /// <summary>
    /// Loads an SVG, renders it through the default CPU backend, and returns a timing report
    /// covering queueing, preparation, batch planning, rasterization, and composition stages.
    /// </summary>
    /// <param name="svgFilePath">The SVG file to profile.</param>
    /// <param name="width">The target image width.</param>
    /// <param name="height">The target image height.</param>
    /// <param name="scale">The SVG scale factor to apply before profiling.</param>
    /// <param name="warmupCount">The number of warmup iterations to execute.</param>
    /// <param name="iterationCount">The number of measured iterations to execute.</param>
    /// <returns>A formatted multi-line timing report.</returns>
    public static string ProfileSolidFillSvgReport(
        string svgFilePath,
        int width,
        int height,
        float scale = 1F,
        int warmupCount = 1,
        int iterationCount = 3)
    {
        List<(IPath Path, SolidBrush Fill)> elements = LoadSolidFillElements(svgFilePath, scale);
        Configuration configuration = Configuration.Default.Clone();
        configuration.SetDrawingBackend(DefaultDrawingBackend.Instance);
        DrawingOptions drawingOptions = new();
        List<CompositionCommand> commandTemplates = CreateCommandTemplates(elements, drawingOptions);

        for (int i = 0; i < warmupCount; i++)
        {
            _ = ProfilePublicCanvas(configuration, drawingOptions, elements, width, height);
            _ = ProfileInternalPipeline(configuration, drawingOptions, commandTemplates, width, height);
        }

        List<PublicCanvasProfile> publicProfiles = [];
        List<InternalPipelineProfile> internalProfiles = [];
        for (int i = 0; i < iterationCount; i++)
        {
            publicProfiles.Add(ProfilePublicCanvas(configuration, drawingOptions, elements, width, height));
            internalProfiles.Add(ProfileInternalPipeline(configuration, drawingOptions, commandTemplates, width, height));
        }

        PublicCanvasProfile publicMean = PublicCanvasProfile.Average(publicProfiles);
        InternalPipelineProfile internalMean = InternalPipelineProfile.Average(internalProfiles);

        StringBuilder sb = new();
        _ = sb.AppendLine(FormattableString.Invariant($"SVG path: {svgFilePath}"));
        _ = sb.AppendLine(FormattableString.Invariant($"Size: {width}x{height}"));
        _ = sb.AppendLine(FormattableString.Invariant($"Elements: {elements.Count:N0}"));
        _ = sb.AppendLine(FormattableString.Invariant($"Fill commands: {commandTemplates.Count:N0}"));
        _ = sb.AppendLine();
        _ = sb.AppendLine("Public canvas:");
        _ = sb.AppendLine(FormattableString.Invariant($"  Queue fills:      {publicMean.QueueMilliseconds,8:F2} ms"));
        _ = sb.AppendLine(FormattableString.Invariant($"  Flush:            {publicMean.FlushMilliseconds,8:F2} ms"));
        _ = sb.AppendLine(FormattableString.Invariant($"  Total:            {publicMean.TotalMilliseconds,8:F2} ms"));
        _ = sb.AppendLine();
        _ = sb.AppendLine("Internal pipeline:");
        _ = sb.AppendLine(FormattableString.Invariant($"  Create commands:  {internalMean.CreateCommandsMilliseconds,8:F2} ms"));
        _ = sb.AppendLine(FormattableString.Invariant($"  Prepare:          {internalMean.PrepareMilliseconds,8:F2} ms"));
        _ = sb.AppendLine(FormattableString.Invariant($"  Plan batches:     {internalMean.PlanMilliseconds,8:F2} ms"));
        _ = sb.AppendLine(FormattableString.Invariant($"  Prebuild edges:   {internalMean.PrebuildMilliseconds,8:F2} ms"));
        _ = sb.AppendLine(FormattableString.Invariant($"  Raster no-op:     {internalMean.RasterizeNoOpMilliseconds,8:F2} ms"));
        _ = sb.AppendLine(FormattableString.Invariant($"  Create applic.:   {internalMean.CreateApplicatorsMilliseconds,8:F2} ms"));
        _ = sb.AppendLine(FormattableString.Invariant($"  Raster+compose:   {internalMean.ComposeMilliseconds,8:F2} ms"));
        _ = sb.AppendLine(FormattableString.Invariant($"  Dispose applic.:  {internalMean.DisposeApplicatorsMilliseconds,8:F2} ms"));
        _ = sb.AppendLine(FormattableString.Invariant($"  Total:            {internalMean.TotalMilliseconds,8:F2} ms"));
        _ = sb.AppendLine();
        _ = sb.AppendLine("Scene stats:");
        _ = sb.AppendLine(FormattableString.Invariant($"  Batches:          {internalMean.BatchCount:N0}"));
        _ = sb.AppendLine(FormattableString.Invariant($"  Total edges:      {internalMean.TotalEdgeCount:N0}"));
        _ = sb.AppendLine(FormattableString.Invariant($"  Avg cmds/batch:   {internalMean.AverageCommandsPerBatch:F2}"));
        _ = sb.AppendLine(FormattableString.Invariant($"  Single-cmd ratio: {internalMean.SingleCommandBatchRatio:P2}"));
        return sb.ToString();
    }

    private static List<(IPath Path, SolidBrush Fill)> LoadSolidFillElements(string svgFilePath, float scale)
    {
        XDocument doc = XDocument.Load(svgFilePath);
        XNamespace ns = "http://www.w3.org/2000/svg";
        List<(IPath Path, SolidBrush Fill)> result = [];

        foreach (XElement pathElement in doc.Descendants(ns + "path"))
        {
            string? pathData = pathElement.Attribute("d")?.Value;
            if (string.IsNullOrWhiteSpace(pathData) || !Path.TryParseSvgPath(pathData, out IPath? path))
            {
                continue;
            }

            Color fill = ParseFill(pathElement);
            if (fill.ToPixel<Rgba32>().A == 0)
            {
                continue;
            }

            Matrix4x4 scaleMatrix = Matrix4x4.CreateScale(scale, scale, 1F);
            if (TryResolveTransform(pathElement, out Matrix4x4 transform))
            {
                path = path.Transform(transform * scaleMatrix);
            }
            else
            {
                path = path.Transform(scaleMatrix);
            }

            result.Add((path, new SolidBrush(fill)));
        }

        return result;
    }

    private static Color ParseFill(XElement pathElement)
    {
        string? fillText = pathElement.Attribute("fill")?.Value;
        Color fill = fillText switch
        {
            null => Color.Black,
            "none" => Color.Transparent,
            _ when Color.TryParse(fillText, out Color parsed) => parsed,
            _ => Color.Black
        };

        if (float.TryParse(pathElement.Attribute("fill-opacity")?.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out float fillOpacity))
        {
            Rgba32 pixel = fill.ToPixel<Rgba32>();
            pixel.A = (byte)(pixel.A * Math.Clamp(fillOpacity, 0F, 1F));
            fill = Color.FromPixel(pixel);
        }

        if (float.TryParse(pathElement.Attribute("opacity")?.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out float opacity))
        {
            Rgba32 pixel = fill.ToPixel<Rgba32>();
            pixel.A = (byte)(pixel.A * Math.Clamp(opacity, 0F, 1F));
            fill = Color.FromPixel(pixel);
        }

        return fill;
    }

    private static bool TryResolveTransform(XElement element, out Matrix4x4 result)
    {
        List<Matrix4x4>? transforms = null;
        XElement? current = element;
        while (current is not null)
        {
            string? transformText = current.Attribute("transform")?.Value;
            if (transformText is not null && TryParseTransform(transformText, out Matrix4x4 transform))
            {
                transforms ??= [];
                transforms.Add(transform);
            }

            current = current.Parent;
        }

        if (transforms is null)
        {
            result = Matrix4x4.Identity;
            return false;
        }

        result = Matrix4x4.Identity;
        for (int i = transforms.Count - 1; i >= 0; i--)
        {
            result *= transforms[i];
        }

        return true;
    }

    private static bool TryParseTransform(string value, out Matrix4x4 result)
    {
        result = Matrix4x4.Identity;
        ReadOnlySpan<char> span = value.AsSpan().Trim();

        if (span.StartsWith("matrix(") && span.EndsWith(")"))
        {
            span = span[7..^1];
            Span<float> values = stackalloc float[6];
            for (int i = 0; i < values.Length; i++)
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
            int separator = trimmed.IndexOfAny(',', ' ');
            float tx = float.Parse(separator < 0 ? trimmed : trimmed[..separator], CultureInfo.InvariantCulture);
            float ty = separator < 0 ? 0F : float.Parse(trimmed[(separator + 1)..].Trim(), CultureInfo.InvariantCulture);
            result = Matrix4x4.CreateTranslation(tx, ty, 0);
            return true;
        }

        if (span.StartsWith("scale(") && span.EndsWith(")"))
        {
            span = span[6..^1];
            ReadOnlySpan<char> trimmed = span.Trim();
            int separator = trimmed.IndexOfAny(',', ' ');
            float sx = float.Parse(separator < 0 ? trimmed : trimmed[..separator], CultureInfo.InvariantCulture);
            float sy = separator < 0 ? sx : float.Parse(trimmed[(separator + 1)..].Trim(), CultureInfo.InvariantCulture);
            result = Matrix4x4.CreateScale(sx, sy, 1);
            return true;
        }

        return false;
    }

    private static PublicCanvasProfile ProfilePublicCanvas(
        Configuration configuration,
        DrawingOptions drawingOptions,
        List<(IPath Path, SolidBrush Fill)> elements,
        int width,
        int height)
    {
        using Image<Rgba32> targetImage = new(width, height);
        using DrawingCanvas<Rgba32> canvas = new(
            configuration,
            new Buffer2DRegion<Rgba32>(targetImage.Frames.RootFrame.PixelBuffer),
            drawingOptions);

        Stopwatch sw = Stopwatch.StartNew();
        foreach ((IPath path, SolidBrush fill) in elements)
        {
            canvas.Fill(fill, path);
        }

        double queueMs = sw.Elapsed.TotalMilliseconds;

        sw.Restart();
        canvas.Flush();
        double flushMs = sw.Elapsed.TotalMilliseconds;
        return new PublicCanvasProfile(queueMs, flushMs);
    }

    private static InternalPipelineProfile ProfileInternalPipeline(
        Configuration configuration,
        DrawingOptions drawingOptions,
        List<CompositionCommand> commandTemplates,
        int width,
        int height)
    {
        using Image<Rgba32> targetImage = new(width, height);
        Buffer2DRegion<Rgba32> targetRegion = new(targetImage.Frames.RootFrame.PixelBuffer);
        MemoryCanvasFrame<Rgba32> frame = new(targetRegion);

        Stopwatch sw = Stopwatch.StartNew();
        List<CompositionCommand> commands = new(commandTemplates);
        double createCommandsMs = sw.Elapsed.TotalMilliseconds;

        sw.Restart();
        PrepareCommands(commands);
        double prepareMs = sw.Elapsed.TotalMilliseconds;

        sw.Restart();
        List<CompositionBatch> batches = CompositionScenePlanner.CreatePreparedBatches(commands, frame.Bounds);
        double planMs = sw.Elapsed.TotalMilliseconds;

        sw.Restart();
        DefaultRasterizer.PrebuiltEdgeTable[] prebuiltEdges = PrebuildEdges(configuration.MemoryAllocator, batches);
        double prebuildMs = sw.Elapsed.TotalMilliseconds;

        long totalEdgeCount = 0;
        int singleCommandBatchCount = 0;
        for (int i = 0; i < prebuiltEdges.Length; i++)
        {
            totalEdgeCount += prebuiltEdges[i].EdgeCount;
            if (batches[i].Commands.Count == 1)
            {
                singleCommandBatchCount++;
            }
        }

        try
        {
            sw.Restart();
            RasterizeNoOp(configuration.MemoryAllocator, batches, prebuiltEdges);
            double rasterizeNoOpMs = sw.Elapsed.TotalMilliseconds;

            sw.Restart();
            CompositionExecutionProfile composeProfile = ComposeBatches(configuration, frame, batches, prebuiltEdges);
            double composeTotalMs = sw.Elapsed.TotalMilliseconds;

            return new InternalPipelineProfile(
                createCommandsMs,
                prepareMs,
                planMs,
                prebuildMs,
                rasterizeNoOpMs,
                composeProfile.CreateApplicatorsMilliseconds,
                composeProfile.RasterizeAndComposeMilliseconds,
                composeProfile.DisposeApplicatorsMilliseconds,
                createCommandsMs + prepareMs + planMs + prebuildMs + rasterizeNoOpMs + composeTotalMs,
                batches.Count,
                totalEdgeCount,
                batches.Count == 0 ? 0 : (double)commands.Count / batches.Count,
                batches.Count == 0 ? 0 : (double)singleCommandBatchCount / batches.Count);
        }
        finally
        {
            for (int i = 0; i < prebuiltEdges.Length; i++)
            {
                prebuiltEdges[i].Dispose();
            }
        }
    }

    private static List<CompositionCommand> CreateCommandTemplates(
        List<(IPath Path, SolidBrush Fill)> elements,
        DrawingOptions drawingOptions)
    {
        List<CompositionCommand> commands = new(elements.Count);
        GraphicsOptions graphicsOptions = drawingOptions.GraphicsOptions;
        ShapeOptions shapeOptions = drawingOptions.ShapeOptions;
        RasterizationMode rasterizationMode = graphicsOptions.Antialias
            ? RasterizationMode.Antialiased
            : RasterizationMode.Aliased;

        foreach ((IPath path, SolidBrush fill) in elements)
        {
            RectangleF bounds = path.Bounds;
            Rectangle interest = Rectangle.FromLTRB(
                (int)MathF.Floor(bounds.Left),
                (int)MathF.Floor(bounds.Top),
                (int)MathF.Ceiling(bounds.Right),
                (int)MathF.Ceiling(bounds.Bottom));

            RasterizerOptions rasterizerOptions = new(
                interest,
                shapeOptions.IntersectionRule,
                rasterizationMode,
                RasterizerSamplingOrigin.PixelBoundary,
                graphicsOptions.AntialiasThreshold);

            commands.Add(
                CompositionCommand.Create(
                    path,
                    fill,
                    graphicsOptions,
                    in rasterizerOptions,
                    shapeOptions,
                    Matrix4x4.Identity));
        }

        return commands;
    }

    private static void PrepareCommands(List<CompositionCommand> commands)
        => Parallel.ForEach(Partitioner.Create(0, commands.Count), range =>
        {
            Span<CompositionCommand> span = CollectionsMarshal.AsSpan(commands);
            for (int i = range.Item1; i < range.Item2; i++)
            {
                span[i].Prepare();
            }
        });

    private static DefaultRasterizer.PrebuiltEdgeTable[] PrebuildEdges(
        MemoryAllocator allocator,
        List<CompositionBatch> batches)
    {
        DefaultRasterizer.PrebuiltEdgeTable[] prebuiltEdges = new DefaultRasterizer.PrebuiltEdgeTable[batches.Count];
        Parallel.ForEach(Partitioner.Create(0, batches.Count), range =>
        {
            for (int i = range.Item1; i < range.Item2; i++)
            {
                prebuiltEdges[i] = DefaultRasterizer.PreBuildEdgeTable(
                    batches[i].Definition.Geometry,
                    batches[i].Definition.RasterizerOptions,
                    allocator);
            }
        });

        return prebuiltEdges;
    }

    private static void RasterizeNoOp(
        MemoryAllocator allocator,
        List<CompositionBatch> batches,
        DefaultRasterizer.PrebuiltEdgeTable[] prebuiltEdges)
    {
        DefaultRasterizer.WorkerScratch? scratch = null;
        try
        {
            for (int i = 0; i < batches.Count; i++)
            {
                DefaultRasterizer.RasterizeRows(
                    in prebuiltEdges[i],
                    batches[i].Definition.RasterizerOptions,
                    allocator,
                    static (int y, int startX, Span<float> coverage) => { },
                    ref scratch);
            }
        }
        finally
        {
            scratch?.Dispose();
        }
    }

    private static CompositionExecutionProfile ComposeBatches(
        Configuration configuration,
        MemoryCanvasFrame<Rgba32> target,
        List<CompositionBatch> batches,
        DefaultRasterizer.PrebuiltEdgeTable[] prebuiltEdges)
    {
        if (!target.TryGetCpuRegion(out Buffer2DRegion<Rgba32> destinationFrame))
        {
            throw new NotSupportedException("SVG profiling requires CPU-accessible target frames.");
        }

        MemoryAllocator allocator = configuration.MemoryAllocator;
        double createApplicatorsMs = 0;
        double rasterizeComposeMs = 0;
        double disposeApplicatorsMs = 0;
        DefaultRasterizer.WorkerScratch? scratch = null;

        try
        {
            Rectangle destinationBounds = destinationFrame.Rectangle;
            Stopwatch sw = new();

            for (int i = 0; i < batches.Count; i++)
            {
                CompositionBatch batch = batches[i];
                List<PreparedCompositionCommand> commands = batch.Commands;
                if (commands.Count == 0)
                {
                    continue;
                }

                BrushRenderer<Rgba32>[] applicators = new BrushRenderer<Rgba32>[commands.Count];
                try
                {
                    sw.Restart();
                    for (int c = 0; c < commands.Count; c++)
                    {
                        PreparedCompositionCommand command = commands[c];
                        applicators[c] = command.Brush.CreateRenderer<Rgba32>(
                            configuration,
                            command.GraphicsOptions,
                            destinationFrame.Width,
                            command.BrushBounds);
                    }

                    sw.Stop();
                    createApplicatorsMs += sw.Elapsed.TotalMilliseconds;

                    using BrushWorkspace<Rgba32> workspace = new(configuration.MemoryAllocator, destinationBounds.Width);
                    RowOperation operation = new(
                        commands,
                        applicators,
                        workspace,
                        destinationFrame,
                        batch.Definition.RasterizerOptions.Interest.Top);

                    sw.Restart();
                    DefaultRasterizer.RasterizeRows(
                        in prebuiltEdges[i],
                        batch.Definition.RasterizerOptions,
                        allocator,
                        operation.InvokeCoverageRow,
                        ref scratch);
                    sw.Stop();
                    rasterizeComposeMs += sw.Elapsed.TotalMilliseconds;
                }
                finally
                {
                    sw.Restart();
                    for (int c = 0; c < applicators.Length; c++)
                    {
                        applicators[c]?.Dispose();
                    }

                    sw.Stop();
                    disposeApplicatorsMs += sw.Elapsed.TotalMilliseconds;
                }
            }
        }
        finally
        {
            scratch?.Dispose();
        }

        return new CompositionExecutionProfile(createApplicatorsMs, rasterizeComposeMs, disposeApplicatorsMs);
    }

    private static Span<TPixel> GetDestinationRow<TPixel>(
        Buffer2DRegion<TPixel> destinationFrame,
        int x,
        int y,
        int length)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        int localY = y - destinationFrame.Rectangle.Y;
        int localX = x - destinationFrame.Rectangle.X;
        return destinationFrame.DangerousGetRowSpan(localY).Slice(localX, length);
    }

    private readonly record struct PublicCanvasProfile(
        double QueueMilliseconds,
        double FlushMilliseconds)
    {
        public double TotalMilliseconds => this.QueueMilliseconds + this.FlushMilliseconds;

        public static PublicCanvasProfile Average(List<PublicCanvasProfile> profiles)
        {
            double count = profiles.Count;
            return new PublicCanvasProfile(
                profiles.Sum(x => x.QueueMilliseconds) / count,
                profiles.Sum(x => x.FlushMilliseconds) / count);
        }
    }

    private readonly record struct InternalPipelineProfile(
        double CreateCommandsMilliseconds,
        double PrepareMilliseconds,
        double PlanMilliseconds,
        double PrebuildMilliseconds,
        double RasterizeNoOpMilliseconds,
        double CreateApplicatorsMilliseconds,
        double ComposeMilliseconds,
        double DisposeApplicatorsMilliseconds,
        double TotalMilliseconds,
        double BatchCount,
        double TotalEdgeCount,
        double AverageCommandsPerBatch,
        double SingleCommandBatchRatio)
    {
        public static InternalPipelineProfile Average(List<InternalPipelineProfile> profiles)
        {
            double count = profiles.Count;
            return new InternalPipelineProfile(
                profiles.Sum(x => x.CreateCommandsMilliseconds) / count,
                profiles.Sum(x => x.PrepareMilliseconds) / count,
                profiles.Sum(x => x.PlanMilliseconds) / count,
                profiles.Sum(x => x.PrebuildMilliseconds) / count,
                profiles.Sum(x => x.RasterizeNoOpMilliseconds) / count,
                profiles.Sum(x => x.CreateApplicatorsMilliseconds) / count,
                profiles.Sum(x => x.ComposeMilliseconds) / count,
                profiles.Sum(x => x.DisposeApplicatorsMilliseconds) / count,
                profiles.Sum(x => x.TotalMilliseconds) / count,
                profiles.Sum(x => x.BatchCount) / count,
                profiles.Sum(x => x.TotalEdgeCount) / count,
                profiles.Sum(x => x.AverageCommandsPerBatch) / count,
                profiles.Sum(x => x.SingleCommandBatchRatio) / count);
        }
    }

    private readonly record struct CompositionExecutionProfile(
        double CreateApplicatorsMilliseconds,
        double RasterizeAndComposeMilliseconds,
        double DisposeApplicatorsMilliseconds);

    private readonly struct RowOperation
    {
        private readonly List<PreparedCompositionCommand> commands;
        private readonly BrushRenderer<Rgba32>[] applicators;
        private readonly BrushWorkspace<Rgba32> workspace;
        private readonly Buffer2DRegion<Rgba32> destinationFrame;
        private readonly int coverageTop;

        public RowOperation(
            List<PreparedCompositionCommand> commands,
            BrushRenderer<Rgba32>[] applicators,
            BrushWorkspace<Rgba32> workspace,
            Buffer2DRegion<Rgba32> destinationFrame,
            int coverageTop)
        {
            this.commands = commands;
            this.applicators = applicators;
            this.workspace = workspace;
            this.destinationFrame = destinationFrame;
            this.coverageTop = coverageTop;
        }

        public void InvokeCoverageRow(int y, int startX, Span<float> coverage)
        {
            int sourceY = y - this.coverageTop;
            int rowStart = startX;
            int rowEnd = startX + coverage.Length;

            BrushRenderer<Rgba32>[] applicators = this.applicators;
            for (int i = 0; i < this.commands.Count; i++)
            {
                PreparedCompositionCommand command = this.commands[i];
                Rectangle commandDestination = command.DestinationRegion;

                int commandY = sourceY - command.SourceOffset.Y;
                if ((uint)commandY >= (uint)commandDestination.Height)
                {
                    continue;
                }

                int sourceStartX = command.SourceOffset.X;
                int sourceEndX = sourceStartX + commandDestination.Width;
                int overlapStart = Math.Max(rowStart, sourceStartX);
                int overlapEnd = Math.Min(rowEnd, sourceEndX);
                if (overlapEnd <= overlapStart)
                {
                    continue;
                }

                int localStart = overlapStart - rowStart;
                int localLength = overlapEnd - overlapStart;
                int destinationX = this.destinationFrame.Rectangle.X + commandDestination.X + (overlapStart - sourceStartX);
                int destinationY = this.destinationFrame.Rectangle.Y + commandDestination.Y + commandY;
                Span<Rgba32> destinationRow = GetDestinationRow(this.destinationFrame, destinationX, destinationY, localLength);
                applicators[i].Apply(destinationRow, coverage.Slice(localStart, localLength), destinationX, destinationY, this.workspace);
            }
        }
    }
}
