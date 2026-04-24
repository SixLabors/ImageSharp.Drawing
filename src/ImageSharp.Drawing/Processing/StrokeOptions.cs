// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Processing;

/// <summary>
/// Provides configuration options for geometric stroke generation.
/// </summary>
public sealed class StrokeOptions : IEquatable<StrokeOptions?>
{
    /// <summary>
    /// Gets or sets the miter limit used to clamp outer miter joins.
    /// </summary>
    public double MiterLimit { get; set; } = 4D;

    /// <summary>
    /// Gets or sets the tessellation detail scale for round joins and round caps.
    /// Higher values produce more vertices (smoother curves, more work).
    /// Lower values produce fewer vertices.
    /// </summary>
    public double ArcDetailScale { get; set; } = 1D;

    /// <summary>
    /// Gets or sets the outer line join style used for stroking corners.
    /// </summary>
    public LineJoin LineJoin { get; set; } = LineJoin.Bevel;

    /// <summary>
    /// Gets or sets the line cap style used for open path ends.
    /// </summary>
    public LineCap LineCap { get; set; } = LineCap.Butt;

    /// <summary>
    /// Gets or sets a value indicating whether stroked contours should be normalized
    /// by resolving self-intersections and overlaps before returning.
    /// </summary>
    /// <remarks>
    /// Defaults to false for maximum throughput. When disabled, callers should rasterize
    /// with a non-zero winding fill rule.
    /// </remarks>
    public bool NormalizeOutput { get; set; }

    /// <inheritdoc/>
    public override bool Equals(object? obj) => this.Equals(obj as StrokeOptions);

    /// <inheritdoc/>
    public bool Equals(StrokeOptions? other)
        => other is not null &&
           this.MiterLimit == other.MiterLimit &&
           this.ArcDetailScale == other.ArcDetailScale &&
           this.LineJoin == other.LineJoin &&
           this.LineCap == other.LineCap &&
           this.NormalizeOutput == other.NormalizeOutput;

    /// <inheritdoc/>
    public override int GetHashCode()
        => HashCode.Combine(
            this.MiterLimit,
            this.ArcDetailScale,
            this.LineJoin,
            this.LineCap,
            this.NormalizeOutput);
}
