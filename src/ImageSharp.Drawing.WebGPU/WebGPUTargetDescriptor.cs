// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.PixelFormats;

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// Identifies the physical storage, numeric encoding, and alpha representation of a WebGPU target.
/// </summary>
internal readonly struct WebGPUTargetDescriptor : IEquatable<WebGPUTargetDescriptor>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="WebGPUTargetDescriptor"/> struct.
    /// </summary>
    /// <param name="format">The physical texture format.</param>
    /// <param name="alphaRepresentation">The alpha representation stored by the texture.</param>
    /// <param name="numericEncoding">The mapping between native channel values and ImageSharp unit values.</param>
    public WebGPUTargetDescriptor(
        WebGPUTextureFormat format,
        PixelAlphaRepresentation alphaRepresentation,
        WebGPUTargetNumericEncoding numericEncoding)
    {
        this.Format = format;
        this.AlphaRepresentation = alphaRepresentation == PixelAlphaRepresentation.Associated
            ? PixelAlphaRepresentation.Associated
            : PixelAlphaRepresentation.Unassociated;
        this.NumericEncoding = numericEncoding;
    }

    /// <summary>
    /// Gets the physical texture format.
    /// </summary>
    public WebGPUTextureFormat Format { get; }

    /// <summary>
    /// Gets the alpha representation stored by the texture.
    /// </summary>
    public PixelAlphaRepresentation AlphaRepresentation { get; }

    /// <summary>
    /// Gets the mapping between native channel values and ImageSharp unit values.
    /// </summary>
    public WebGPUTargetNumericEncoding NumericEncoding { get; }

    /// <summary>
    /// Compares two descriptors for equality.
    /// </summary>
    /// <param name="left">The left descriptor.</param>
    /// <param name="right">The right descriptor.</param>
    /// <returns><see langword="true"/> when both descriptors identify the same target type.</returns>
    public static bool operator ==(WebGPUTargetDescriptor left, WebGPUTargetDescriptor right) => left.Equals(right);

    /// <summary>
    /// Compares two descriptors for inequality.
    /// </summary>
    /// <param name="left">The left descriptor.</param>
    /// <param name="right">The right descriptor.</param>
    /// <returns><see langword="true"/> when the descriptors identify different target types.</returns>
    public static bool operator !=(WebGPUTargetDescriptor left, WebGPUTargetDescriptor right) => !left.Equals(right);

    /// <inheritdoc/>
    public bool Equals(WebGPUTargetDescriptor other)
        => this.Format == other.Format &&
            this.AlphaRepresentation == other.AlphaRepresentation &&
            this.NumericEncoding == other.NumericEncoding;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is WebGPUTargetDescriptor other && this.Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(this.Format, this.AlphaRepresentation, this.NumericEncoding);
}
