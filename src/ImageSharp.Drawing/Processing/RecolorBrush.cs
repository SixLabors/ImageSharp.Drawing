// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;

namespace SixLabors.ImageSharp.Drawing.Processing;

/// <summary>
/// Provides an implementation of a brush that can recolor an image.
/// Pixels within <see cref="Threshold"/> of <see cref="SourceColor"/> are blended
/// towards <see cref="TargetColor"/>, with the blend strength falling off as the
/// color distance approaches the threshold.
/// </summary>
public sealed class RecolorBrush : Brush
{
    /// <summary>
    /// Initializes a new instance of the <see cref="RecolorBrush" /> class.
    /// </summary>
    /// <param name="sourceColor">The color to match against existing pixels.</param>
    /// <param name="targetColor">The color to recolor matched pixels with.</param>
    /// <param name="threshold">The color-matching threshold as a value between 0 and 1.</param>
    public RecolorBrush(Color sourceColor, Color targetColor, float threshold)
    {
        this.SourceColor = sourceColor;
        this.Threshold = threshold;
        this.TargetColor = targetColor;
    }

    /// <summary>
    /// Gets the color-matching threshold as a value between 0 and 1.
    /// </summary>
    public float Threshold { get; }

    /// <summary>
    /// Gets the color to match against existing pixels.
    /// </summary>
    public Color SourceColor { get; }

    /// <summary>
    /// Gets the color to recolor matched pixels with.
    /// </summary>
    public Color TargetColor { get; }

    /// <inheritdoc />
    public override BrushRenderer<TPixel> CreateRenderer<TPixel>(
        Configuration configuration,
        GraphicsOptions options,
        int canvasWidth,
        RectangleF region)
    {
        Vector4 sourceColor = this.SourceColor.ToScaledVector4(TPixel.GetPixelTypeInfo().AlphaRepresentation);
        TPixel targetColor = this.TargetColor.ToPixel<TPixel>();

        return new RecolorBrushRenderer<TPixel>(
            configuration,
            options,
            canvasWidth,
            sourceColor,
            targetColor,
            this.Threshold);
    }

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
        /// <param name="options">The graphics options.</param>
        /// <param name="canvasWidth">The canvas width for the current render pass.</param>
        /// <param name="sourceColor">The color to match, expressed in the destination pixel format's native alpha representation.</param>
        /// <param name="targetColor">The color to recolor matched pixels with.</param>
        /// <param name="threshold">The color-matching threshold as a value between 0 and 1.</param>
        public RecolorBrushRenderer(
            Configuration configuration,
            GraphicsOptions options,
            int canvasWidth,
            Vector4 sourceColor,
            TPixel targetColor,
            float threshold)
            : base(configuration, options, canvasWidth)
        {
            this.sourceColor = sourceColor;
            this.targetColorPixel = targetColor;

            // Matching happens in squared-distance space so no per-pixel sqrt is needed.
            this.threshold = threshold * Vector4.DistanceSquared(Vector4.Zero, Vector4.One);
        }

        /// <inheritdoc />
        public override void Apply(
            Span<TPixel> destinationRow,
            ReadOnlySpan<float> scanline,
            int x,
            int y,
            BrushWorkspace<TPixel> workspace)
        {
            Span<TPixel> overlays = workspace.GetOverlays(scanline.Length);

            for (int i = 0; i < scanline.Length; i++)
            {
                // The brush reads the already-composed destination pixel: recoloring is a
                // function of what is currently on the canvas, not of a source texture.
                TPixel result = destinationRow[i];

                // Keep both operands in TPixel's native representation. The constrained call is
                // statically specialized and adds no representation branch to the pixel loop.
                Vector4 background = result.ToScaledVector4();
                float distance = Vector4.DistanceSquared(background, this.sourceColor);

                // Blend strength falls off linearly with squared distance: an exact match
                // is fully recolored while a match at the threshold is left untouched.
                overlays[i] = distance <= this.threshold
                    ? this.Blender.Blend(result, this.targetColorPixel, (this.threshold - distance) / this.threshold)
                    : result;
            }

            this.Blender.BlendWithCoverage<TPixel>(
                this.Configuration,
                destinationRow,
                destinationRow,
                overlays,
                this.Options.BlendPercentage,
                scanline,
                workspace.GetBlendScratch(scanline.Length, 3));
        }
    }
}
