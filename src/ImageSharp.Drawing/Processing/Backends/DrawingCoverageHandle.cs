// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// Opaque handle to backend-prepared coverage data.
/// </summary>
internal readonly struct DrawingCoverageHandle : IEquatable<DrawingCoverageHandle>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DrawingCoverageHandle"/> struct.
    /// </summary>
    /// <param name="value">The backend-specific handle id.</param>
    public DrawingCoverageHandle(int value) => this.Value = value;

    /// <summary>
    /// Gets the raw handle id.
    /// </summary>
    public int Value { get; }

    /// <summary>
    /// Gets a value indicating whether this handle references prepared coverage.
    /// </summary>
    public bool IsValid => this.Value > 0;

    /// <summary>
    /// Equality operator.
    /// </summary>
    /// <param name="left">Left value.</param>
    /// <param name="right">Right value.</param>
    /// <returns><see langword="true"/> if equal.</returns>
    public static bool operator ==(DrawingCoverageHandle left, DrawingCoverageHandle right) => left.Equals(right);

    /// <summary>
    /// Inequality operator.
    /// </summary>
    /// <param name="left">Left value.</param>
    /// <param name="right">Right value.</param>
    /// <returns><see langword="true"/> if not equal.</returns>
    public static bool operator !=(DrawingCoverageHandle left, DrawingCoverageHandle right) => !(left == right);

    /// <inheritdoc />
    public bool Equals(DrawingCoverageHandle other) => this.Value == other.Value;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is DrawingCoverageHandle other && this.Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => this.Value;
}
