// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// The Code One symbology of the AIM USA Uniform Symbology Specification Code One: a matrix of square modules with a centre pattern of horizontal bars and
/// vertical risers. Versions A to H carry the characters U+0000 to U+00FF, versions S carry up to 18 digits, and versions T carry data in a symbol 16 rows
/// high. By default the symbol is the smallest of versions A to H that holds the data.
/// </summary>
public sealed class CodeOneSymbology : BarcodeSymbology
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CodeOneSymbology"/> class that takes the smallest of versions A to H that holds the data.
    /// </summary>
    public CodeOneSymbology()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="CodeOneSymbology"/> class with a fixed version.
    /// </summary>
    /// <param name="version">The version.</param>
    public CodeOneSymbology(CodeOneVersion version)
    {
        Guard.MustBeBetweenOrEqualTo((int)version, (int)CodeOneVersion.A, (int)CodeOneVersion.T48, nameof(version));
        this.Version = version;
    }

    /// <summary>
    /// Gets the version, or <see langword="null"/> when the symbol takes the smallest of versions A to H that holds the data.
    /// </summary>
    public CodeOneVersion? Version { get; }

    /// <inheritdoc/>
    internal override void Encode(ReadOnlySpan<char> text, BarcodeOptions options, ref BarcodeSymbol symbol)
    {
        CodeOneEncoder.BuildSymbol(text, this.Version, options.ResolveXDimension(DefaultMatrixXDimension), ref symbol);
    }
}
