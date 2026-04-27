// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Processing;

/// <summary>
/// Provides an implementation of a pattern brush for painting patterns.
/// </summary>
/// <remarks>
/// The patterns that are used to create a custom pattern brush are made up of a repeating matrix of flags,
/// where each flag denotes whether to draw the foreground color or the background color.
/// so to create a new bool[,] with your flags
/// <para>
/// For example if you wanted to create a diagonal line that repeat every 4 pixels you would use a pattern like so
/// 1000
/// 0100
/// 0010
/// 0001
/// </para>
/// <para>
/// or you want a horizontal stripe which is 3 pixels apart you would use a pattern like
///  1
///  0
///  0
/// </para>
/// </remarks>
public sealed class PatternBrush : Brush
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PatternBrush"/> class.
    /// </summary>
    /// <param name="foreColor">Color of the fore.</param>
    /// <param name="backColor">Color of the back.</param>
    /// <param name="pattern">The pattern.</param>
    public PatternBrush(Color foreColor, Color backColor, bool[,] pattern)
        : this(foreColor, backColor, new DenseMatrix<bool>(pattern))
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PatternBrush"/> class.
    /// </summary>
    /// <param name="foreColor">Color of the fore.</param>
    /// <param name="backColor">Color of the back.</param>
    /// <param name="pattern">The pattern.</param>
    internal PatternBrush(Color foreColor, Color backColor, in DenseMatrix<bool> pattern)
    {
        this.Pattern = new DenseMatrix<Color>(pattern.Columns, pattern.Rows);
        for (int i = 0; i < pattern.Data.Length; i++)
        {
            if (pattern.Data[i])
            {
                this.Pattern.Data[i] = foreColor;
            }
            else
            {
                this.Pattern.Data[i] = backColor;
            }
        }
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PatternBrush"/> class.
    /// </summary>
    /// <param name="brush">The brush.</param>
    internal PatternBrush(PatternBrush brush) => this.Pattern = brush.Pattern;

    /// <summary>
    /// Gets the pattern color matrix.
    /// </summary>
    public DenseMatrix<Color> Pattern { get; }

    /// <inheritdoc />
    public override bool Equals(Brush? other)
    {
        if (other is PatternBrush sb)
        {
            return sb.Pattern.Equals(this.Pattern);
        }

        return false;
    }

    /// <inheritdoc/>
    public override int GetHashCode()
        => this.Pattern.GetHashCode();

    /// <inheritdoc />
    public override BrushRenderer<TPixel> CreateRenderer<TPixel>(
        Configuration configuration,
        GraphicsOptions options,
        int canvasWidth,
        RectangleF region)
        =>
        new PatternBrushRenderer<TPixel>(
            configuration,
            options,
            canvasWidth,
            this.Pattern.ToPixelMatrix<TPixel>());

    /// <summary>
    /// The pattern brush applicator.
    /// </summary>
    /// <typeparam name="TPixel">The pixel format.</typeparam>
    private sealed class PatternBrushRenderer<TPixel> : BrushRenderer<TPixel>
        where TPixel : unmanaged, IPixel<TPixel>
    {
        private readonly DenseMatrix<TPixel> pattern;

        /// <summary>
        /// Initializes a new instance of the <see cref="PatternBrushRenderer{TPixel}" /> class.
        /// </summary>
        /// <param name="configuration">The configuration instance to use when performing operations.</param>
        /// <param name="options">The graphics options.</param>
        /// <param name="canvasWidth">The canvas width for the current render pass.</param>
        /// <param name="pattern">The pattern.</param>
        public PatternBrushRenderer(
            Configuration configuration,
            GraphicsOptions options,
            int canvasWidth,
            in DenseMatrix<TPixel> pattern)
            : base(configuration, options, canvasWidth)
            => this.pattern = pattern;

        internal TPixel this[int x, int y]
        {
            get
            {
                x %= this.pattern.Columns;
                y %= this.pattern.Rows;

                // 2d array index at row/column
                return this.pattern[y, x];
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
            int patternY = y % this.pattern.Rows;
            Span<float> amounts = workspace.GetAmounts(scanline.Length);
            Span<TPixel> overlays = workspace.GetOverlays(scanline.Length);

            for (int i = 0; i < scanline.Length; i++)
            {
                amounts[i] = Math.Clamp(scanline[i] * this.Options.BlendPercentage, 0, 1F);

                int patternX = (x + i) % this.pattern.Columns;
                overlays[i] = this.pattern[patternY, patternX];
            }

            this.Blender.Blend(
                this.Configuration,
                destinationRow,
                destinationRow,
                overlays,
                amounts,
                workspace.GetBlendScratch(scanline.Length, 3));
        }
    }
}
