// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Runtime.CompilerServices;
using SixLabors.Fonts;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Drawing.Processing.Backends;
using SixLabors.ImageSharp.Memory;
using SixLabors.ImageSharp.PixelFormats;

namespace SixLabors.ImageSharp.Drawing.Tests.Processing;

public class DrawingCanvasBatcherTests
{
    [Fact]
    public void Flush_SamePathDifferentBrushes_UsesSingleCoverageDefinition()
    {
        Configuration configuration = new();
        CapturingBackend backend = new();
        configuration.SetDrawingBackend(backend);
        using Image<Rgba32> image = new(40, 40);
        Buffer2DRegion<Rgba32> region = new(image.Frames.RootFrame.PixelBuffer, image.Bounds);

        IPath path = new RectangularPolygon(4, 6, 18, 12);
        DrawingOptions options = new();
        using DrawingCanvas<Rgba32> canvas = new(configuration, region, options);
        Brush brushA = Brushes.Solid(Color.Red);
        Brush brushB = Brushes.Solid(Color.Blue);

        canvas.Fill(brushA, path);
        canvas.Fill(brushB, path);
        canvas.Flush();

        Assert.True(backend.HasDefinition);
        Assert.NotNull(backend.LastDefinition.SourcePath);
        Assert.Equal(2, backend.LastDefinition.Commands.Count);
        Assert.Same(brushA, backend.LastDefinition.Commands[0].Brush);
        Assert.Same(brushB, backend.LastDefinition.Commands[1].Brush);
    }

    [Fact]
    public void Flush_SamePathDifferentBrushes_ReusesSourcePath()
    {
        Configuration configuration = new();
        CapturingBackend backend = new();
        configuration.SetDrawingBackend(backend);
        using Image<Rgba32> image = new(40, 40);
        Buffer2DRegion<Rgba32> region = new(image.Frames.RootFrame.PixelBuffer, image.Bounds);

        IPath path = new RectangularPolygon(4, 6, 18, 12);
        DrawingOptions options = new();
        using DrawingCanvas<Rgba32> canvas = new(configuration, region, options);

        canvas.Fill(Brushes.Solid(Color.Red), path);
        canvas.Fill(Brushes.Solid(Color.Blue), path);
        canvas.Flush();

        Assert.Equal(2, backend.PreparedCommands.Count);
        Assert.NotNull(backend.PreparedCommands[0].SourcePath);
        Assert.Same(backend.PreparedCommands[0].SourcePath, backend.PreparedCommands[1].SourcePath);
    }

    [Fact]
    public void Flush_SamePathReusedMultipleTimes_GroupsCommandsByCoverageDefinition()
    {
        Configuration configuration = new();
        CapturingBackend backend = new();
        configuration.SetDrawingBackend(backend);
        using Image<Rgba32> image = new(100, 100);
        Buffer2DRegion<Rgba32> region = new(image.Frames.RootFrame.PixelBuffer, image.Bounds);

        IPath path = new RectangularPolygon(10, 10, 40, 40);
        DrawingOptions options = new();
        using DrawingCanvas<Rgba32> canvas = new(configuration, region, options);

        for (int i = 0; i < 10; i++)
        {
            canvas.Fill(Brushes.Solid(Color.FromPixel(new Rgba32((byte)i, 0, 0, 255))), path);
        }

        canvas.Flush();

        Assert.Single(backend.Definitions);
        Assert.Equal(10, backend.Definitions[0].Commands.Count);
    }

    [Fact]
    public void Flush_RepeatedGlyphs_ReusesCoverageDefinitions()
    {
        Configuration configuration = new();
        CapturingBackend backend = new();
        configuration.SetDrawingBackend(backend);
        using Image<Rgba32> image = new(420, 220);
        Buffer2DRegion<Rgba32> region = new(image.Frames.RootFrame.PixelBuffer, image.Bounds);

        Font font = TestFontUtilities.GetFont(TestFonts.OpenSans, 48);
        RichTextOptions textOptions = new(font)
        {
            Origin = new PointF(8, 8),
            WrappingLength = 400
        };

        DrawingOptions drawingOptions = new()
        {
            GraphicsOptions = new GraphicsOptions { Antialias = true }
        };

        string text = new('A', 200);
        Brush brush = Brushes.Solid(Color.Black);

        using DrawingCanvas<Rgba32> canvas = new(configuration, region, drawingOptions);
        canvas.DrawText(textOptions, text, brush, pen: null);
        canvas.Flush();

        int totalCommands = backend.Definitions.Sum(b => b.Commands.Count);
        Assert.True(totalCommands > 0);
        Assert.True(
            backend.Definitions.Count < 200,
            $"Expected coverage reuse but got {backend.Definitions.Count} coverage definitions for 200 glyphs.");
    }

    [Fact]
    public void Flush_SupportedSolidStroke_PreparesCenterlinePath()
    {
        Configuration configuration = new();
        CapturingBackend backend = new();
        configuration.SetDrawingBackend(backend);
        using Image<Rgba32> image = new(80, 80);
        Buffer2DRegion<Rgba32> region = new(image.Frames.RootFrame.PixelBuffer, image.Bounds);

        using DrawingCanvas<Rgba32> canvas = new(configuration, region, new DrawingOptions());
        canvas.DrawLine(new SolidPen(Color.Red, 5F), new PointF(8, 12), new PointF(70, 64));
        canvas.Flush();

        Assert.Single(backend.PreparedCommands);
        Assert.NotNull(backend.PreparedCommands[0].SourcePath);
        Assert.NotNull(backend.PreparedCommands[0].Pen);
    }

    [Fact]
    public void Flush_DashedStroke_PreparesCenterlinePath()
    {
        Configuration configuration = new();
        CapturingBackend backend = new();
        configuration.SetDrawingBackend(backend);
        using Image<Rgba32> image = new(80, 80);
        Buffer2DRegion<Rgba32> region = new(image.Frames.RootFrame.PixelBuffer, image.Bounds);

        using DrawingCanvas<Rgba32> canvas = new(configuration, region, new DrawingOptions());
        canvas.DrawLine(Pens.Dash(Color.Red, 5F), new PointF(8, 12), new PointF(70, 64));
        canvas.Flush();

        Assert.Single(backend.PreparedCommands);
        Assert.NotNull(backend.PreparedCommands[0].SourcePath);
        Assert.NotNull(backend.PreparedCommands[0].Pen);
    }

    [Fact]
    public void Flush_MiterStroke_PreparesCenterlinePath()
    {
        Configuration configuration = new();
        CapturingBackend backend = new();
        configuration.SetDrawingBackend(backend);
        using Image<Rgba32> image = new(80, 80);
        Buffer2DRegion<Rgba32> region = new(image.Frames.RootFrame.PixelBuffer, image.Bounds);

        PenOptions options = new(Color.Red, 5F)
        {
            StrokeOptions = new StrokeOptions
            {
                LineJoin = LineJoin.Miter
            }
        };

        using DrawingCanvas<Rgba32> canvas = new(configuration, region, new DrawingOptions());
        canvas.Draw(new SolidPen(options), new Path(new[] { new PointF(8, 40), new PointF(40, 8), new PointF(72, 40) }));
        canvas.Flush();

        Assert.Single(backend.PreparedCommands);
        Assert.NotNull(backend.PreparedCommands[0].SourcePath);
        Assert.NotNull(backend.PreparedCommands[0].Pen);
    }

    private sealed class CapturingBackend : IDrawingBackend
    {
        public List<CapturedCoverageDefinition> Definitions { get; } = [];

        public IReadOnlyList<CompositionCommand> PreparedCommands { get; private set; } = Array.Empty<CompositionCommand>();

        public bool HasDefinition { get; private set; }

        public CapturedCoverageDefinition LastDefinition { get; private set; } = new(
            EmptyPath.ClosedPath,
            new RasterizerOptions(
                Rectangle.Empty,
                IntersectionRule.NonZero,
                RasterizationMode.Aliased,
                RasterizerSamplingOrigin.PixelBoundary,
                0.5f),
            default,
            []);

        public void FlushCompositions<TPixel>(
            Configuration configuration,
            ICanvasFrame<TPixel> target,
            CompositionScene compositionScene)
            where TPixel : unmanaged, IPixel<TPixel>
        {
            List<CompositionCommand> preparedCommands = [];
            for (int i = 0; i < compositionScene.Commands.Count; i++)
            {
                if (compositionScene.Commands[i] is PathCompositionSceneCommand pathCommand)
                {
                    preparedCommands.Add(pathCommand.Command);
                }
            }

            this.PreparedCommands = preparedCommands;

            Dictionary<CoverageDefinitionKey, int> definitionIndices = [];
            for (int i = 0; i < compositionScene.Commands.Count; i++)
            {
                if (compositionScene.Commands[i] is not PathCompositionSceneCommand pathCommand)
                {
                    continue;
                }

                CompositionCommand command = pathCommand.Command;
                // if (!command.IsVisible)
                // {
                //     continue;
                // }

                IPath? SourcePath = command.SourcePath;
                if (SourcePath is null)
                {
                    continue;
                }

                RasterizerOptions rasterizerOptions = command.RasterizerOptions;

                CoverageDefinitionKey key = new(command);
                if (!definitionIndices.TryGetValue(key, out int definitionIndex))
                {
                    definitionIndex = this.Definitions.Count;
                    definitionIndices.Add(key, definitionIndex);
                    this.Definitions.Add(
                        new CapturedCoverageDefinition(
                            SourcePath,
                            rasterizerOptions,
                            command.DestinationOffset,
                            [command]));
                }
                else
                {
                    this.Definitions[definitionIndex].Commands.Add(command);
                }
            }

            if (this.Definitions.Count == 0)
            {
                return;
            }

            this.LastDefinition = this.Definitions[^1];
            this.HasDefinition = true;
        }

        public bool TryReadRegion<TPixel>(
            Configuration configuration,
            ICanvasFrame<TPixel> target,
            Rectangle sourceRectangle,
            Buffer2DRegion<TPixel> destination)
            where TPixel : unmanaged, IPixel<TPixel>
            => false;

        public sealed class CapturedCoverageDefinition(IPath SourcePath, RasterizerOptions rasterizerOptions, Point destinationOffset, List<CompositionCommand> commands)
        {
            public IPath SourcePath { get; } = SourcePath;

            public RasterizerOptions RasterizerOptions { get; } = rasterizerOptions;

            public Point DestinationOffset { get; } = destinationOffset;

            public List<CompositionCommand> Commands { get; } = commands;
        }

        private readonly struct CoverageDefinitionKey : IEquatable<CoverageDefinitionKey>
        {
            private readonly int SourcePathIdentity;
            private readonly Rectangle interest;
            private readonly IntersectionRule intersectionRule;
            private readonly RasterizationMode rasterizationMode;
            private readonly RasterizerSamplingOrigin samplingOrigin;
            private readonly int antialiasThresholdBits;

            public CoverageDefinitionKey(CompositionCommand command)
            {
                this.SourcePathIdentity = RuntimeHelpers.GetHashCode(command.SourcePath!);
                this.interest = command.RasterizerOptions.Interest;
                this.intersectionRule = command.RasterizerOptions.IntersectionRule;
                this.rasterizationMode = command.RasterizerOptions.RasterizationMode;
                this.samplingOrigin = command.RasterizerOptions.SamplingOrigin;
                this.antialiasThresholdBits = BitConverter.SingleToInt32Bits(command.RasterizerOptions.AntialiasThreshold);
            }

            public bool Equals(CoverageDefinitionKey other)
                => this.SourcePathIdentity == other.SourcePathIdentity &&
                   this.interest.Equals(other.interest) &&
                   this.intersectionRule == other.intersectionRule &&
                   this.rasterizationMode == other.rasterizationMode &&
                   this.samplingOrigin == other.samplingOrigin &&
                   this.antialiasThresholdBits == other.antialiasThresholdBits;

            public override bool Equals(object? obj)
                => obj is CoverageDefinitionKey other && this.Equals(other);

            public override int GetHashCode()
                => HashCode.Combine(
                    this.SourcePathIdentity,
                    this.interest,
                    (int)this.intersectionRule,
                    (int)this.rasterizationMode,
                    (int)this.samplingOrigin,
                    this.antialiasThresholdBits);
        }
    }
}
