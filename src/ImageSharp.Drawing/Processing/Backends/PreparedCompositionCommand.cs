// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// One normalized composition command that applies a brush to the active coverage map.
/// </summary>
public readonly struct PreparedCompositionCommand
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PreparedCompositionCommand"/> struct.
    /// </summary>
    /// <param name="destinationRegion">The destination region in target-local coordinates.</param>
    /// <param name="sourceOffset">The source offset into the pre-rasterized coverage map.</param>
    /// <param name="brush">The brush used during composition.</param>
    /// <param name="brushBounds">Brush bounds used for applicator creation.</param>
    /// <param name="graphicsOptions">Graphics options used during composition.</param>
    public PreparedCompositionCommand(
        Rectangle destinationRegion,
        Point sourceOffset,
        Brush brush,
        Rectangle brushBounds,
        GraphicsOptions graphicsOptions)
    {
        this.DestinationRegion = destinationRegion;
        this.SourceOffset = sourceOffset;
        this.Brush = brush;
        this.BrushBounds = brushBounds;
        this.GraphicsOptions = graphicsOptions;
    }

    /// <summary>
    /// Gets the destination region in target-local coordinates.
    /// </summary>
    public Rectangle DestinationRegion { get; }

    /// <summary>
    /// Gets the source offset into the pre-rasterized coverage map.
    /// </summary>
    public Point SourceOffset { get; }

    /// <summary>
    /// Gets the brush used during composition.
    /// </summary>
    public Brush Brush { get; }

    /// <summary>
    /// Gets brush bounds used for applicator creation.
    /// </summary>
    public Rectangle BrushBounds { get; }

    /// <summary>
    /// Gets graphics options used during composition.
    /// </summary>
    public GraphicsOptions GraphicsOptions { get; }
}
