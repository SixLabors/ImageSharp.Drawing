// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;
using SixLabors.ImageSharp.Drawing.Utilities;
using SixLabors.ImageSharp.Memory;
using SixLabors.ImageSharp.PixelFormats;

namespace SixLabors.ImageSharp.Drawing.Processing;

/// <summary>
/// Base class for Gradient brushes
/// </summary>
public abstract class GradientBrush : Brush
{
    /// <inheritdoc cref="Brush"/>
    /// <param name="repetitionMode">Defines how the colors are repeated beyond the interval [0..1]</param>
    /// <param name="colorStops">The gradient colors.</param>
    protected GradientBrush(GradientRepetitionMode repetitionMode, params ColorStop[] colorStops)
    {
        this.RepetitionMode = repetitionMode;
        this.ColorStops = colorStops;
    }

    /// <summary>
    /// Gets how the colors are repeated beyond the interval [0..1].
    /// </summary>
    protected GradientRepetitionMode RepetitionMode { get; }

    /// <summary>
    /// Gets the list of color stops for this gradient.
    /// </summary>
    protected ColorStop[] ColorStops { get; }

    /// <inheritdoc />
    public override bool Equals(Brush? other)
    {
        if (other is GradientBrush brush)
        {
            return this.RepetitionMode == brush.RepetitionMode
                && this.ColorStops?.SequenceEqual(brush.ColorStops) == true;
        }

        return false;
    }

    /// <inheritdoc/>
    public override int GetHashCode()
        => HashCode.Combine(this.RepetitionMode, this.ColorStops);

    /// <summary>
    /// Base class for gradient brush applicators
    /// </summary>
    /// <typeparam name="TPixel">The pixel format.</typeparam>
    internal abstract class GradientBrushApplicator<TPixel> : BrushApplicator<TPixel>
        where TPixel : unmanaged, IPixel<TPixel>
    {
        private static readonly TPixel Transparent = Color.Transparent.ToPixel<TPixel>();

        private readonly ColorStop[] colorStops;

        private readonly GradientRepetitionMode repetitionMode;

        private readonly MemoryAllocator allocator;

        private readonly int scanlineWidth;

        private readonly ThreadLocalBlenderBuffers<TPixel> blenderBuffers;

        private bool isDisposed;

        /// <summary>
        /// Initializes a new instance of the <see cref="GradientBrushApplicator{TPixel}"/> class.
        /// </summary>
        /// <param name="configuration">The configuration instance to use when performing operations.</param>
        /// <param name="options">The graphics options.</param>
        /// <param name="target">The target image.</param>
        /// <param name="colorStops">An array of color stops sorted by their position.</param>
        /// <param name="repetitionMode">Defines if and how the gradient should be repeated.</param>
        protected GradientBrushApplicator(
            Configuration configuration,
            GraphicsOptions options,
            ImageFrame<TPixel> target,
            ColorStop[] colorStops,
            GradientRepetitionMode repetitionMode)
            : base(configuration, options, target)
        {
            this.colorStops = colorStops;

            // Ensure the color-stop order is correct.
            InsertionSort(this.colorStops, (x, y) => x.Ratio.CompareTo(y.Ratio));
            this.repetitionMode = repetitionMode;
            this.scanlineWidth = target.Width;
            this.allocator = configuration.MemoryAllocator;
            this.blenderBuffers = new ThreadLocalBlenderBuffers<TPixel>(this.allocator, this.scanlineWidth);
        }

        internal TPixel this[int x, int y]
        {
            get
            {
                float positionOnCompleteGradient = this.PositionOnGradient(x + 0.5f, y + 0.5f);

                switch (this.repetitionMode)
                {
                    case GradientRepetitionMode.Repeat:
                        positionOnCompleteGradient %= 1;
                        break;
                    case GradientRepetitionMode.Reflect:
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

                (ColorStop from, ColorStop to) = this.GetGradientSegment(positionOnCompleteGradient);

                if (from.Color.Equals(to.Color))
                {
                    return from.Color.ToPixel<TPixel>();
                }

                float onLocalGradient = (positionOnCompleteGradient - from.Ratio) / (to.Ratio - from.Ratio);

                // TODO: This should use premultiplied vectors to avoid bad blends e.g. red -> brown <- green.
                return new Color(Vector4.Lerp((Vector4)from.Color, (Vector4)to.Color, onLocalGradient)).ToPixel<TPixel>();
            }
        }

        /// <inheritdoc />
        public override void Apply(Span<float> scanline, int x, int y)
        {
            Span<float> amounts = this.blenderBuffers.AmountSpan[..scanline.Length];
            Span<TPixel> overlays = this.blenderBuffers.OverlaySpan[..scanline.Length];
            float blendPercentage = this.Options.BlendPercentage;

            // TODO: Remove bounds checks.
            if (blendPercentage < 1)
            {
                for (int i = 0; i < scanline.Length; i++)
                {
                    amounts[i] = scanline[i] * blendPercentage;
                    overlays[i] = this[x + i, y];
                }
            }
            else
            {
                for (int i = 0; i < scanline.Length; i++)
                {
                    amounts[i] = scanline[i];
                    overlays[i] = this[x + i, y];
                }
            }

            Span<TPixel> destinationRow = this.Target.PixelBuffer.DangerousGetRowSpan(y).Slice(x, scanline.Length);
            this.Blender.Blend(this.Configuration, destinationRow, destinationRow, overlays, amounts);
        }

        /// <summary>
        /// Calculates the position on the gradient for a given point.
        /// This method is abstract as it's content depends on the shape of the gradient.
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

        /// <inheritdoc/>
        protected override void Dispose(bool disposing)
        {
            if (this.isDisposed)
            {
                return;
            }

            base.Dispose(disposing);

            if (disposing)
            {
                this.blenderBuffers.Dispose();
            }

            this.isDisposed = true;
        }

        private (ColorStop From, ColorStop To) GetGradientSegment(float positionOnCompleteGradient)
        {
            ColorStop localGradientFrom = this.colorStops[0];
            ColorStop localGradientTo = default;

            // TODO: ensure colorStops has at least 2 items (technically 1 would be okay, but that's no gradient)
            foreach (ColorStop colorStop in this.colorStops)
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
        /// Provides a stable sorting algorithm for the given array.
        /// <see cref="Array.Sort(Array, System.Collections.IComparer?)"/> is not stable.
        /// </summary>
        /// <typeparam name="T">The type of element to sort.</typeparam>
        /// <param name="collection">The array to sort.</param>
        /// <param name="comparison">The comparison delegate.</param>
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
    }
}
