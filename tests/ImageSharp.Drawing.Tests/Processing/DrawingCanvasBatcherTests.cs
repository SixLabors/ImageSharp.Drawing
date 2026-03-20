// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

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
        Assert.NotNull(backend.LastDefinition.Definition.PreparedPath);
        Assert.Equal(2, backend.LastDefinition.Commands.Count);
        Assert.Same(brushA, backend.LastDefinition.Commands[0].Brush);
        Assert.Same(brushB, backend.LastDefinition.Commands[1].Brush);
    }

    [Fact]
    public void Flush_SamePathDifferentBrushes_ReusesPreparedPath()
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
        Assert.NotNull(backend.PreparedCommands[0].PreparedPath);
        Assert.Same(backend.PreparedCommands[0].PreparedPath, backend.PreparedCommands[1].PreparedPath);
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

    private sealed class CapturingBackend : IDrawingBackend
    {
        public List<CapturedCoverageDefinition> Definitions { get; } = [];

        public IReadOnlyList<CompositionCommand> PreparedCommands { get; private set; } = Array.Empty<CompositionCommand>();

        public bool HasDefinition { get; private set; }

        public CapturedCoverageDefinition LastDefinition { get; private set; } = new(
            new CompositionCoverageDefinition(
                0,
                EmptyPath.ClosedPath,
                new RasterizerOptions(
                    Rectangle.Empty,
                    IntersectionRule.NonZero,
                    RasterizationMode.Aliased,
                    RasterizerSamplingOrigin.PixelBoundary,
                    0.5f)),
            []);

        public void FlushCompositions<TPixel>(
            Configuration configuration,
            ICanvasFrame<TPixel> target,
            CompositionScene compositionScene)
            where TPixel : unmanaged, IPixel<TPixel>
        {
            this.PreparedCommands = compositionScene.Commands.ToArray();

            Dictionary<CoverageDefinitionKey, int> definitionIndices = [];
            for (int i = 0; i < compositionScene.Commands.Count; i++)
            {
                CompositionCommand command = compositionScene.Commands[i];
                if (!command.IsVisible)
                {
                    continue;
                }

                IPath preparedPath = command.PreparedPath
                    ?? throw new InvalidOperationException("Composition commands must be prepared before backend flush.");
                RasterizerOptions rasterizerOptions = command.RasterizerOptions;

                CoverageDefinitionKey key = new(command);
                if (!definitionIndices.TryGetValue(key, out int definitionIndex))
                {
                    definitionIndex = this.Definitions.Count;
                    definitionIndices.Add(key, definitionIndex);
                    this.Definitions.Add(
                        new CapturedCoverageDefinition(
                            new CompositionCoverageDefinition(
                                command.DefinitionKey,
                                preparedPath,
                                in rasterizerOptions,
                                command.DestinationOffset),
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

        public sealed class CapturedCoverageDefinition(CompositionCoverageDefinition definition, List<CompositionCommand> commands)
        {
            public CompositionCoverageDefinition Definition { get; } = definition;

            public List<CompositionCommand> Commands { get; } = commands;
        }

        private readonly struct CoverageDefinitionKey : IEquatable<CoverageDefinitionKey>
        {
            private readonly int definitionKey;
            private readonly Rectangle interest;
            private readonly IntersectionRule intersectionRule;
            private readonly RasterizationMode rasterizationMode;
            private readonly RasterizerSamplingOrigin samplingOrigin;
            private readonly int antialiasThresholdBits;

            public CoverageDefinitionKey(CompositionCommand command)
            {
                this.definitionKey = command.DefinitionKey;
                this.interest = command.RasterizerOptions.Interest;
                this.intersectionRule = command.RasterizerOptions.IntersectionRule;
                this.rasterizationMode = command.RasterizerOptions.RasterizationMode;
                this.samplingOrigin = command.RasterizerOptions.SamplingOrigin;
                this.antialiasThresholdBits = BitConverter.SingleToInt32Bits(command.RasterizerOptions.AntialiasThreshold);
            }

            public bool Equals(CoverageDefinitionKey other)
                => this.definitionKey == other.definitionKey &&
                   this.interest.Equals(other.interest) &&
                   this.intersectionRule == other.intersectionRule &&
                   this.rasterizationMode == other.rasterizationMode &&
                   this.samplingOrigin == other.samplingOrigin &&
                   this.antialiasThresholdBits == other.antialiasThresholdBits;

            public override bool Equals(object? obj)
                => obj is CoverageDefinitionKey other && this.Equals(other);

            public override int GetHashCode()
                => HashCode.Combine(
                    this.definitionKey,
                    this.interest,
                    (int)this.intersectionRule,
                    (int)this.rasterizationMode,
                    (int)this.samplingOrigin,
                    this.antialiasThresholdBits);
        }
    }
}
