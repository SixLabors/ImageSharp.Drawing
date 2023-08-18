// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;
using SixLabors.Fonts;
using SixLabors.ImageSharp.Memory;
using SixLabors.ImageSharp.Processing.Processors;

namespace SixLabors.ImageSharp.Drawing.Processing.Processors.Text;

/// <summary>
/// Using the brush as a source of pixels colors blends the brush color with source.
/// </summary>
/// <typeparam name="TPixel">The pixel format.</typeparam>
internal class DrawTextProcessor<TPixel> : ImageProcessor<TPixel>
    where TPixel : unmanaged, IPixel<TPixel>
{
    private RichTextGlyphRenderer? textRenderer;
    private readonly DrawTextProcessor definition;

    public DrawTextProcessor(Configuration configuration, DrawTextProcessor definition, Image<TPixel> source, Rectangle sourceRectangle)
        : base(configuration, source, sourceRectangle)
        => this.definition = definition;

    protected override void BeforeImageApply()
    {
        base.BeforeImageApply();

        // Do everything at the image level as we are delegating
        // the processing down to other processors
        RichTextOptions textOptions = ConfigureOptions(this.definition.TextOptions);

        this.textRenderer = new RichTextGlyphRenderer(
            textOptions,
            this.definition.DrawingOptions,
            this.Configuration.MemoryAllocator,
            this.definition.Pen,
            this.definition.Brush);

        TextRenderer renderer = new(this.textRenderer);
        renderer.RenderText(this.definition.Text, textOptions);
    }

    protected override void AfterImageApply()
    {
        base.AfterImageApply();
        this.textRenderer?.Dispose();
        this.textRenderer = null;
    }

    /// <inheritdoc/>
    protected override void OnFrameApply(ImageFrame<TPixel> source)
    {
        void Draw(IEnumerable<DrawingOperation> operations)
        {
            foreach (DrawingOperation operation in operations)
            {
                using BrushApplicator<TPixel> app = operation.Brush.CreateApplicator(
                         this.Configuration,
                         this.definition.DrawingOptions.GraphicsOptions,
                         source,
                         this.SourceRectangle);

                Buffer2D<float> buffer = operation.Map;
                int startY = operation.RenderLocation.Y;
                int startX = operation.RenderLocation.X;
                int offsetSpan = 0;

                if (startY + buffer.Height < 0)
                {
                    continue;
                }

                if (startX + buffer.Width < 0)
                {
                    continue;
                }

                if (startX < 0)
                {
                    offsetSpan = -startX;
                    startX = 0;
                }

                if (startX >= source.Width)
                {
                    continue;
                }

                int firstRow = 0;
                if (startY < 0)
                {
                    firstRow = -startY;
                }

                int maxHeight = source.Height - startY;
                int end = Math.Min(operation.Map.Height, maxHeight);

                for (int row = firstRow; row < end; row++)
                {
                    int y = startY + row;
                    Span<float> span = buffer.DangerousGetRowSpan(row)[offsetSpan..];
                    app.Apply(span, startX, y);
                }
            }
        }

        if (this.textRenderer.DrawingOperations.Count > 0)
        {
            Draw(this.textRenderer.DrawingOperations.OrderBy(x => x.RenderPass));
        }
    }

    private static RichTextOptions ConfigureOptions(RichTextOptions options)
    {
        // When a path is specified we should explicitly follow that path
        // and not adjust the origin. Any translation should be applied to the path.
        if (options.Path is not null && options.Origin != Vector2.Zero)
        {
            return new(options)
            {
                Origin = Vector2.Zero,
                Path = options.Path.Translate(options.Origin)
            };
        }

        return options;
    }
}
