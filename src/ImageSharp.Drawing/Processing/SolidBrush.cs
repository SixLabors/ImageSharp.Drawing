// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Processing;

/// <summary>
/// Provides an implementation of a solid brush for painting solid color areas.
/// </summary>
public sealed class SolidBrush : Brush
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SolidBrush"/> class.
    /// </summary>
    /// <param name="color">The color.</param>
    public SolidBrush(Color color) => this.Color = color;

    /// <summary>
    /// Gets the color.
    /// </summary>
    public Color Color { get; }

    /// <inheritdoc />
    public override BrushRenderer<TPixel> CreateRenderer<TPixel>(
        Configuration configuration,
        GraphicsOptions options,
        int canvasWidth,
        RectangleF region)
        => new SolidBrushRenderer<TPixel>(configuration, options, canvasWidth, this.Color.ToPixel<TPixel>());

    /// <inheritdoc/>
    public override bool Equals(Brush? other)
    {
        if (other is SolidBrush sb)
        {
            return sb.Color.Equals(this.Color);
        }

        return false;
    }

    /// <inheritdoc/>
    public override int GetHashCode() => this.Color.GetHashCode();

    /// <summary>
    /// The solid brush applicator.
    /// </summary>
    /// <typeparam name="TPixel">The pixel format.</typeparam>
    private sealed class SolidBrushRenderer<TPixel> : BrushRenderer<TPixel>
        where TPixel : unmanaged, IPixel<TPixel>
    {
        private readonly TPixel color;

        /// <summary>
        /// Initializes a new instance of the <see cref="SolidBrushRenderer{TPixel}"/> class.
        /// </summary>
        /// <param name="configuration">The configuration instance to use when performing operations.</param>
        /// <param name="options">The graphics options.</param>
        /// <param name="canvasWidth">The canvas width for the current render pass.</param>
        /// <param name="color">The color.</param>
        public SolidBrushRenderer(
            Configuration configuration,
            GraphicsOptions options,
            int canvasWidth,
            TPixel color)
            : base(configuration, options, canvasWidth)
            => this.color = color;

        /// <inheritdoc />
        public override void Apply(
            Span<TPixel> destinationRow,
            ReadOnlySpan<float> scanline,
            int x,
            int y,
            BrushWorkspace<TPixel> workspace)
        {
            // Constrain the spans to each other
            if (destinationRow.Length > scanline.Length)
            {
                destinationRow = destinationRow[..scanline.Length];
            }
            else
            {
                scanline = scanline[..destinationRow.Length];
            }

            Configuration configuration = this.Configuration;
            if (this.Options.BlendPercentage == 1F)
            {
                this.Blender.Blend(
                    configuration,
                    destinationRow,
                    destinationRow,
                    this.color,
                    scanline,
                    workspace.GetBlendScratch(scanline.Length, 2));
            }
            else
            {
                Span<float> amounts = workspace.GetAmounts(scanline.Length);

                for (int i = 0; i < scanline.Length; i++)
                {
                    amounts[i] = scanline[i] * this.Options.BlendPercentage;
                }

                this.Blender.Blend(
                    configuration,
                    destinationRow,
                    destinationRow,
                    this.color,
                    amounts,
                    workspace.GetBlendScratch(scanline.Length, 2));
            }
        }
    }
}
