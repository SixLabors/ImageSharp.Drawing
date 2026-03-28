// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;
using SixLabors.ImageSharp.Memory;

namespace SixLabors.ImageSharp.Drawing.Processing;

/// <summary>
/// Provides an implementation of a brush that can recolor an image
/// </summary>
public sealed class RecolorBrush : Brush
{
    /// <summary>
    /// Initializes a new instance of the <see cref="RecolorBrush" /> class.
    /// </summary>
    /// <param name="sourceColor">Color of the source.</param>
    /// <param name="targetColor">Color of the target.</param>
    /// <param name="threshold">The threshold as a value between 0 and 1.</param>
    public RecolorBrush(Color sourceColor, Color targetColor, float threshold)
    {
        this.SourceColor = sourceColor;
        this.Threshold = threshold;
        this.TargetColor = targetColor;
    }

    /// <summary>
    /// Gets the threshold.
    /// </summary>
    public float Threshold { get; }

    /// <summary>
    /// Gets the source color.
    /// </summary>
    public Color SourceColor { get; }

    /// <summary>
    /// Gets the target color.
    /// </summary>
    public Color TargetColor { get; }

    /// <inheritdoc />
    public override BrushRenderer<TPixel> CreateRenderer<TPixel>(
        Configuration configuration,
        GraphicsOptions options,
        int canvasWidth,
        RectangleF region)
        => new RecolorBrushRenderer<TPixel>(
            configuration,
            options,
            canvasWidth,
            this.SourceColor.ToPixel<TPixel>(),
            this.TargetColor.ToPixel<TPixel>(),
            this.Threshold);

    /// <inheritdoc />
    public override bool Equals(Brush? other)
    {
        if (other is RecolorBrush brush)
        {
            return this.SourceColor.Equals(brush.SourceColor)
                && this.TargetColor.Equals(brush.TargetColor)
                && this.Threshold == brush.Threshold;
        }

        return false;
    }

    /// <inheritdoc/>
    public override int GetHashCode()
        => HashCode.Combine(this.Threshold, this.SourceColor, this.TargetColor);

    /// <summary>
    /// The recolor brush applicator.
    /// </summary>
    /// <typeparam name="TPixel">The pixel format.</typeparam>
    private sealed class RecolorBrushRenderer<TPixel> : BrushRenderer<TPixel>
        where TPixel : unmanaged, IPixel<TPixel>
    {
        private readonly Vector4 sourceColor;
        private readonly float threshold;
        private readonly TPixel targetColorPixel;

        /// <summary>
        /// Initializes a new instance of the <see cref="RecolorBrushRenderer{TPixel}" /> class.
        /// </summary>
        /// <param name="configuration">The configuration instance to use when performing operations.</param>
        /// <param name="options">The options</param>
        /// <param name="canvasWidth">The canvas width for the current render pass.</param>
        /// <param name="sourceColor">Color of the source.</param>
        /// <param name="targetColor">Color of the target.</param>
        /// <param name="threshold">The threshold .</param>
        public RecolorBrushRenderer(
            Configuration configuration,
            GraphicsOptions options,
            int canvasWidth,
            TPixel sourceColor,
            TPixel targetColor,
            float threshold)
            : base(configuration, options, canvasWidth)
        {
            this.sourceColor = sourceColor.ToScaledVector4();
            this.targetColorPixel = targetColor;

            // TODO: Review this. We can skip the conversion from/to Vector4.
            // Lets hack a min max extremes for a color space by letting the IPackedPixel clamp our values to something in the correct spaces :)
            TPixel maxColor = TPixel.FromVector4(new Vector4(float.MaxValue));
            TPixel minColor = TPixel.FromVector4(new Vector4(float.MinValue));
            this.threshold = Vector4.DistanceSquared(maxColor.ToVector4(), minColor.ToVector4()) * threshold;
        }

        /// <inheritdoc />
        public override void Apply(
            Span<TPixel> destinationRow,
            ReadOnlySpan<float> scanline,
            int x,
            int y,
            BrushWorkspace<TPixel> workspace)
        {
            Span<float> amounts = workspace.GetAmounts(scanline.Length);
            Span<TPixel> overlays = workspace.GetOverlays(scanline.Length);

            for (int i = 0; i < scanline.Length; i++)
            {
                amounts[i] = scanline[i] * this.Options.BlendPercentage;
                TPixel result = destinationRow[i];
                Vector4 background = result.ToVector4();
                float distance = Vector4.DistanceSquared(background, this.sourceColor);
                overlays[i] = distance <= this.threshold
                    ? this.Blender.Blend(result, this.targetColorPixel, (this.threshold - distance) / this.threshold)
                    : result;
            }

            this.Blender.Blend(
                this.Configuration,
                destinationRow,
                destinationRow,
                overlays,
                amounts);
        }
    }
}
