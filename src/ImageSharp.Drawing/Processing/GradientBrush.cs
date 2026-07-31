// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;

namespace SixLabors.ImageSharp.Drawing.Processing;

/// <summary>
/// Base class for gradient brushes.
/// Derived brushes define a parameterization that maps each point to a scalar gradient
/// position; this base class maps that position to a color via the sorted color stops
/// and the <see cref="GradientRepetitionMode"/>.
/// </summary>
public abstract class GradientBrush : Brush
{
    /// <summary>
    /// Initializes a new instance of the <see cref="GradientBrush"/> class.
    /// </summary>
    /// <param name="repetitionMode">Defines how the colors are repeated beyond the interval [0..1].</param>
    /// <param name="colorStops">The gradient colors.</param>
    protected GradientBrush(GradientRepetitionMode repetitionMode, params ColorStop[] colorStops)
    {
        this.RepetitionMode = repetitionMode;

        InsertionSort(colorStops, (a, b) => a.Ratio.CompareTo(b.Ratio));
        this.ColorStopsArray = colorStops;
    }

    /// <summary>
    /// Gets how the colors are repeated beyond the interval [0..1].
    /// </summary>
    public GradientRepetitionMode RepetitionMode { get; }

    /// <summary>
    /// Gets the color stops for this gradient.
    /// </summary>
    public ReadOnlySpan<ColorStop> ColorStops => this.ColorStopsArray;

    /// <summary>
    /// Gets the color stops array for use by derived applicators.
    /// </summary>
    protected ColorStop[] ColorStopsArray { get; }

    /// <inheritdoc />
    public override bool Equals(Brush? other)
    {
        if (other is GradientBrush brush)
        {
            return this.RepetitionMode == brush.RepetitionMode
                && this.ColorStopsArray?.SequenceEqual(brush.ColorStopsArray) == true;
        }

        return false;
    }

    /// <inheritdoc/>
    public override int GetHashCode()
        => HashCode.Combine(this.RepetitionMode, this.ColorStopsArray);

    /// <summary>
    /// Sorts the collection in place using a stable insertion sort.
    /// <see cref="Array.Sort{T}(T[], Comparison{T})"/> is not stable and can reorder
    /// equal-ratio color stops, producing non-deterministic gradient results.
    /// </summary>
    /// <typeparam name="T">The element type of the collection.</typeparam>
    /// <param name="collection">The array to sort in place.</param>
    /// <param name="comparison">The comparison used to order the elements.</param>
    private static void InsertionSort<T>(T[] collection, Comparison<T> comparison)
    {
        int count = collection.Length;
        for (int j = 1; j < count; j++)
        {
            T key = collection[j];

            int i = j - 1;
            for (; i >= 0 && comparison(collection[i], key) > 0; i--)
            {
                collection[i + 1] = collection[i];
            }

            collection[i + 1] = key;
        }
    }

    /// <summary>
    /// Base class for gradient brush applicators.
    /// </summary>
    /// <typeparam name="TPixel">The pixel format.</typeparam>
    /// <typeparam name="TEncoder">The destination representation encoder.</typeparam>
    internal abstract class GradientBrushRenderer<TPixel, TEncoder> : BrushRenderer<TPixel>
        where TPixel : unmanaged, IPixel<TPixel>
        where TEncoder : struct, IGradientPixelEncoder<TPixel>
    {
        private static readonly TPixel Transparent = Color.Transparent.ToPixel<TPixel>();

        private readonly GradientColorStop[] colorStops;

        private readonly GradientRepetitionMode repetitionMode;

        /// <summary>
        /// Initializes a new instance of the <see cref="GradientBrushRenderer{TPixel, TEncoder}"/> class.
        /// </summary>
        /// <param name="configuration">The configuration instance to use when performing operations.</param>
        /// <param name="options">The graphics options.</param>
        /// <param name="canvasWidth">The canvas width for the current render pass.</param>
        /// <param name="colorStops">An array of color stops sorted by their position.</param>
        /// <param name="repetitionMode">Defines if and how the gradient should be repeated.</param>
        protected GradientBrushRenderer(
            Configuration configuration,
            GraphicsOptions options,
            int canvasWidth,
            ColorStop[] colorStops,
            GradientRepetitionMode repetitionMode)
            : base(configuration, options, canvasWidth)
        {
            this.colorStops = new GradientColorStop[colorStops.Length];

            // CSS Color 4 requires alpha to be premultiplied before color interpolation.
            // Cache associated stop vectors once so the result is independent of TPixel storage
            // and the per-pixel interpolation loop does not repeat the conversion.
            // https://www.w3.org/TR/css-color-4/#interpolation-alpha
            for (int i = 0; i < colorStops.Length; i++)
            {
                ColorStop stop = colorStops[i];
                this.colorStops[i] = new GradientColorStop(stop.Ratio, stop.Color.ToScaledVector4(PixelAlphaRepresentation.Associated));
            }

            this.repetitionMode = repetitionMode;
        }

        /// <summary>
        /// Gets the gradient color for the pixel at the given device coordinate.
        /// </summary>
        /// <param name="x">The x-coordinate of the pixel in device space.</param>
        /// <param name="y">The y-coordinate of the pixel in device space.</param>
        /// <returns>The blended gradient color converted to <typeparamref name="TPixel"/>.</returns>
        internal TPixel this[int x, int y]
        {
            get
            {
                // Evaluate at pixel centers so gradient sampling lines up with the
                // rasterized coverage positions.
                float fx = x + 0.5f;
                float fy = y + 0.5f;

                // NaN signals that the parameterization is undefined at this point
                // (e.g. outside the valid branch of a conic); such pixels stay transparent.
                float positionOnCompleteGradient = this.PositionOnGradient(fx, fy);
                if (float.IsNaN(positionOnCompleteGradient))
                {
                    return Transparent;
                }

                switch (this.repetitionMode)
                {
                    case GradientRepetitionMode.Repeat:
                        positionOnCompleteGradient %= 1;
                        break;
                    case GradientRepetitionMode.Reflect:
                        // Fold every second period back on itself so alternating
                        // repetitions run the stops in reverse order.
                        positionOnCompleteGradient %= 2;
                        if (positionOnCompleteGradient > 1)
                        {
                            positionOnCompleteGradient = 2 - positionOnCompleteGradient;
                        }

                        break;
                    case GradientRepetitionMode.DontFill:
                        if (positionOnCompleteGradient is > 1 or < 0)
                        {
                            return Transparent;
                        }

                        break;
                    case GradientRepetitionMode.None:
                    default:
                        // do nothing. The following could be done, but is not necessary:
                        // onLocalGradient = Math.Min(0, Math.Max(1, onLocalGradient));
                        break;
                }

                (GradientColorStop from, GradientColorStop to) = this.GetGradientSegment(positionOnCompleteGradient);

                if (from.Color == to.Color)
                {
                    return TEncoder.Encode(from.Color);
                }

                float onLocalGradient = (positionOnCompleteGradient - from.Ratio) / (to.Ratio - from.Ratio);

                return TEncoder.Encode(Vector4.Lerp(from.Color, to.Color, onLocalGradient));
            }
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

            // TODO: Remove bounds checks.
            for (int i = 0; i < scanline.Length; i++)
            {
                overlays[i] = this[x + i, y];
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

        /// <summary>
        /// Calculates the position on the gradient for a given point.
        /// This method is abstract as its content depends on the shape of the gradient.
        /// </summary>
        /// <param name="x">The x-coordinate of the point.</param>
        /// <param name="y">The y-coordinate of the point.</param>
        /// <returns>
        /// The position the given point has on the gradient.
        /// The position is not bound to the [0..1] interval.
        /// Values outside of that interval may be treated differently,
        /// e.g. for the <see cref="GradientRepetitionMode" /> enum.
        /// </returns>
        protected abstract float PositionOnGradient(float x, float y);

        /// <summary>
        /// Finds the pair of adjacent color stops bracketing the given gradient position.
        /// Positions before the first stop return the first stop twice; positions after the
        /// last stop return the last stop twice, which yields the stable edge colors used
        /// by <see cref="GradientRepetitionMode.None"/>.
        /// </summary>
        /// <param name="positionOnCompleteGradient">The position on the gradient, after repetition handling.</param>
        /// <returns>The bracketing stops; equal when the position lies outside the stop range.</returns>
        private (GradientColorStop From, GradientColorStop To) GetGradientSegment(float positionOnCompleteGradient)
        {
            // Stop counts are tiny, so a linear scan over the sorted stops beats a binary search.
            GradientColorStop localGradientFrom = this.colorStops[0];
            GradientColorStop localGradientTo = default;

            foreach (GradientColorStop colorStop in this.colorStops)
            {
                localGradientTo = colorStop;

                if (colorStop.Ratio > positionOnCompleteGradient)
                {
                    // we're done here, so break it!
                    break;
                }

                localGradientFrom = localGradientTo;
            }

            return (localGradientFrom, localGradientTo);
        }

        /// <summary>
        /// Stores one gradient stop in the associated representation used for interpolation.
        /// </summary>
        /// <param name="ratio">The stop position.</param>
        /// <param name="color">The associated stop color.</param>
        private readonly struct GradientColorStop(float ratio, Vector4 color)
        {
            /// <summary>
            /// Gets the stop position.
            /// </summary>
            public float Ratio { get; } = ratio;

            /// <summary>
            /// Gets the associated stop color.
            /// </summary>
            public Vector4 Color { get; } = color;
        }
    }
}
