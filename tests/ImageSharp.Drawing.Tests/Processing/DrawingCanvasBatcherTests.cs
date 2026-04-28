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
        using Image<Rgba32> image = new(configuration, 40, 40);

        IPath path = new RectangularPolygon(4, 6, 18, 12);
        DrawingOptions options = new();
        using DrawingCanvas canvas = image.CreateCanvas(options);
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
        using Image<Rgba32> image = new(configuration, 40, 40);

        IPath path = new RectangularPolygon(4, 6, 18, 12);
        DrawingOptions options = new();
        using DrawingCanvas canvas = image.CreateCanvas(options);

        canvas.Fill(Brushes.Solid(Color.Red), path);
        canvas.Fill(Brushes.Solid(Color.Blue), path);
        canvas.Flush();

        Assert.Equal(2, backend.PreparedCommands.Count);

        PathCompositionSceneCommand firstCommand = Assert.IsType<PathCompositionSceneCommand>(backend.PreparedCommands[0]);
        PathCompositionSceneCommand secondCommand = Assert.IsType<PathCompositionSceneCommand>(backend.PreparedCommands[1]);
        Assert.NotNull(firstCommand.Command.SourcePath);
        Assert.Same(firstCommand.Command.SourcePath, secondCommand.Command.SourcePath);
    }

    [Fact]
    public void Flush_SamePathReusedMultipleTimes_GroupsCommandsByCoverageDefinition()
    {
        Configuration configuration = new();
        CapturingBackend backend = new();
        configuration.SetDrawingBackend(backend);
        using Image<Rgba32> image = new(configuration, 100, 100);

        IPath path = new RectangularPolygon(10, 10, 40, 40);
        DrawingOptions options = new();
        using DrawingCanvas canvas = image.CreateCanvas(options);

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
        using Image<Rgba32> image = new(configuration, 420, 220);

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

        using DrawingCanvas canvas = image.CreateCanvas(drawingOptions);
        canvas.DrawText(textOptions, text, brush, pen: null);
        canvas.Flush();

        int totalCommands = backend.PreparedCommands.Count;
        Assert.True(totalCommands > 0);
        Assert.True(
            backend.Definitions.Count < 200,
            $"Expected coverage reuse but got {backend.Definitions.Count} coverage definitions for 200 glyphs.");
    }

    [Fact]
    public void Flush_SupportedSolidStroke_PreparesLineSegmentSceneCommand()
    {
        Configuration configuration = new();
        CapturingBackend backend = new();
        configuration.SetDrawingBackend(backend);
        using Image<Rgba32> image = new(configuration, 80, 80);

        using DrawingCanvas canvas = image.CreateCanvas(new DrawingOptions());
        canvas.DrawLine(new SolidPen(Color.Red, 5F), new PointF(8, 12), new PointF(70, 64));
        canvas.Flush();

        Assert.Single(backend.PreparedCommands);

        LineSegmentCompositionSceneCommand command = Assert.IsType<LineSegmentCompositionSceneCommand>(backend.PreparedCommands[0]);
        Assert.Equal(new PointF(8, 12), command.Command.SourceStart);
        Assert.Equal(new PointF(70, 64), command.Command.SourceEnd);
        Assert.NotNull(command.Command.Pen);
    }

    [Fact]
    public void Flush_DashedStroke_PreparesStrokePathSceneCommand()
    {
        Configuration configuration = new();
        CapturingBackend backend = new();
        configuration.SetDrawingBackend(backend);
        using Image<Rgba32> image = new(configuration, 80, 80);

        using DrawingCanvas canvas = image.CreateCanvas(new DrawingOptions());
        canvas.DrawLine(Pens.Dash(Color.Red, 5F), new PointF(8, 12), new PointF(70, 64));
        canvas.Flush();

        Assert.Single(backend.PreparedCommands);

        StrokePathCompositionSceneCommand command = Assert.IsType<StrokePathCompositionSceneCommand>(backend.PreparedCommands[0]);
        Assert.NotNull(command.Command.SourcePath);
        Assert.NotNull(command.Command.Pen);
    }

    [Fact]
    public void Flush_MiterStroke_PreparesStrokePathSceneCommand()
    {
        Configuration configuration = new();
        CapturingBackend backend = new();
        configuration.SetDrawingBackend(backend);
        using Image<Rgba32> image = new(configuration, 80, 80);

        PenOptions options = new(Color.Red, 5F)
        {
            StrokeOptions = new StrokeOptions
            {
                LineJoin = LineJoin.Miter
            }
        };

        using DrawingCanvas canvas = image.CreateCanvas(new DrawingOptions());
        canvas.Draw(new SolidPen(options), new Path([new PointF(8, 40), new PointF(40, 8), new PointF(72, 40)]));
        canvas.Flush();

        Assert.Single(backend.PreparedCommands);

        StrokePathCompositionSceneCommand command = Assert.IsType<StrokePathCompositionSceneCommand>(backend.PreparedCommands[0]);
        Assert.NotNull(command.Command.SourcePath);
        Assert.NotNull(command.Command.Pen);
    }

    private sealed class CapturingBackend : IDrawingBackend
    {
        public List<CapturedCoverageDefinition> Definitions { get; } = [];

        public IReadOnlyList<CompositionSceneCommand> PreparedCommands { get; private set; } = Array.Empty<CompositionSceneCommand>();

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
            this.PreparedCommands = compositionScene.Commands.ToArray();

            Dictionary<CoverageDefinitionKey, int> definitionIndices = [];
            for (int i = 0; i < this.PreparedCommands.Count; i++)
            {
                if (this.PreparedCommands[i] is not PathCompositionSceneCommand pathCommand)
                {
                    continue;
                }

                CompositionCommand command = pathCommand.Command;
                IPath sourcePath = command.SourcePath;
                if (sourcePath is null)
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
                            sourcePath,
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

        public void ReadRegion<TPixel>(
            Configuration configuration,
            ICanvasFrame<TPixel> target,
            Rectangle sourceRectangle,
            Buffer2DRegion<TPixel> destination)
            where TPixel : unmanaged, IPixel<TPixel>
            => throw new NotSupportedException();

        public sealed class CapturedCoverageDefinition
        {
            public CapturedCoverageDefinition(
                IPath sourcePath,
                RasterizerOptions rasterizerOptions,
                Point destinationOffset,
                List<CompositionCommand> commands)
            {
                this.SourcePath = sourcePath;
                this.RasterizerOptions = rasterizerOptions;
                this.DestinationOffset = destinationOffset;
                this.Commands = commands;
            }

            public IPath SourcePath { get; }

            public RasterizerOptions RasterizerOptions { get; }

            public Point DestinationOffset { get; }

            public List<CompositionCommand> Commands { get; }
        }

        private readonly struct CoverageDefinitionKey : IEquatable<CoverageDefinitionKey>
        {
            private readonly int sourcePathIdentity;
            private readonly Rectangle interest;
            private readonly IntersectionRule intersectionRule;
            private readonly RasterizationMode rasterizationMode;
            private readonly RasterizerSamplingOrigin samplingOrigin;
            private readonly int antialiasThresholdBits;

            public CoverageDefinitionKey(CompositionCommand command)
            {
                this.sourcePathIdentity = RuntimeHelpers.GetHashCode(command.SourcePath);
                this.interest = command.RasterizerOptions.Interest;
                this.intersectionRule = command.RasterizerOptions.IntersectionRule;
                this.rasterizationMode = command.RasterizerOptions.RasterizationMode;
                this.samplingOrigin = command.RasterizerOptions.SamplingOrigin;
                this.antialiasThresholdBits = BitConverter.SingleToInt32Bits(command.RasterizerOptions.AntialiasThreshold);
            }

            public bool Equals(CoverageDefinitionKey other)
                => this.sourcePathIdentity == other.sourcePathIdentity &&
                   this.interest.Equals(other.interest) &&
                   this.intersectionRule == other.intersectionRule &&
                   this.rasterizationMode == other.rasterizationMode &&
                   this.samplingOrigin == other.samplingOrigin &&
                   this.antialiasThresholdBits == other.antialiasThresholdBits;

            public override bool Equals(object obj)
                => obj is CoverageDefinitionKey other && this.Equals(other);

            public override int GetHashCode()
                => HashCode.Combine(
                    this.sourcePathIdentity,
                    this.interest,
                    (int)this.intersectionRule,
                    (int)this.rasterizationMode,
                    (int)this.samplingOrigin,
                    this.antialiasThresholdBits);
        }
    }
}
