// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// The position of one element string in a buffer, its Application Identifier and its data, with the
/// dictionary entry of the Application Identifier, which is found once when the element string is parsed.
/// </summary>
internal readonly struct Gs1Element
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Gs1Element"/> struct.
    /// </summary>
    /// <param name="identifierStart">The index of the first digit of the Application Identifier.</param>
    /// <param name="identifierLength">The number of digits of the Application Identifier.</param>
    /// <param name="dataStart">The index of the first character of the data.</param>
    /// <param name="dataLength">The number of characters of the data.</param>
    /// <param name="entryIndex">The index of the entry of the Application Identifier in <see cref="Gs1SyntaxDictionary.Entries"/>.</param>
    public Gs1Element(int identifierStart, int identifierLength, int dataStart, int dataLength, int entryIndex)
    {
        this.IdentifierStart = identifierStart;
        this.IdentifierLength = identifierLength;
        this.DataStart = dataStart;
        this.DataLength = dataLength;
        this.EntryIndex = entryIndex;
    }

    /// <summary>
    /// Gets the index of the first digit of the Application Identifier.
    /// </summary>
    public int IdentifierStart { get; }

    /// <summary>
    /// Gets the number of digits of the Application Identifier.
    /// </summary>
    public int IdentifierLength { get; }

    /// <summary>
    /// Gets the index of the first character of the data.
    /// </summary>
    public int DataStart { get; }

    /// <summary>
    /// Gets the number of characters of the data.
    /// </summary>
    public int DataLength { get; }

    /// <summary>
    /// Gets the index of the entry of the Application Identifier in <see cref="Gs1SyntaxDictionary.Entries"/>.
    /// </summary>
    public int EntryIndex { get; }

    /// <summary>
    /// Gets the dictionary entry of the Application Identifier.
    /// </summary>
    public Gs1ApplicationIdentifier Entry => Gs1SyntaxDictionary.Entries[this.EntryIndex];

    /// <summary>
    /// Returns the Application Identifier from the buffer.
    /// </summary>
    /// <param name="buffer">The buffer the element string is in.</param>
    /// <returns>The Application Identifier.</returns>
    public ReadOnlySpan<char> Identifier(ReadOnlySpan<char> buffer) => buffer.Slice(this.IdentifierStart, this.IdentifierLength);

    /// <summary>
    /// Returns the data from the buffer.
    /// </summary>
    /// <param name="buffer">The buffer the element string is in.</param>
    /// <returns>The data.</returns>
    public ReadOnlySpan<char> Data(ReadOnlySpan<char> buffer) => buffer.Slice(this.DataStart, this.DataLength);
}
