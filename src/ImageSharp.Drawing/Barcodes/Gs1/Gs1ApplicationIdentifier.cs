// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// One entry of the GS1 Barcode Syntax Dictionary: an Application Identifier, or a range of them, with the
/// format of its data, the rules that pair it with other Application Identifiers, and its role in a GS1
/// Digital Link URI.
/// </summary>
internal sealed class Gs1ApplicationIdentifier
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Gs1ApplicationIdentifier"/> class.
    /// </summary>
    /// <param name="first">The first Application Identifier of the range.</param>
    /// <param name="last">The last Application Identifier of the range, the same as <paramref name="first"/> for one.</param>
    /// <param name="predefinedLength">Whether the element string has a predefined length and takes no separator.</param>
    /// <param name="dataAttribute">Whether the Application Identifier may be a data attribute of a GS1 Digital Link URI.</param>
    /// <param name="components">The components of the data, in order.</param>
    /// <param name="requisites">The requisite rules, each a list of groups of Application Identifier patterns.</param>
    /// <param name="exclusions">The Application Identifier patterns that may not appear with this one.</param>
    /// <param name="keyQualifiers">The qualifier sequences of a GS1 Digital Link primary key, or <see langword="null"/> when it is not one.</param>
    public Gs1ApplicationIdentifier(
        string first,
        string last,
        bool predefinedLength,
        bool dataAttribute,
        Gs1Component[] components,
        string[][][] requisites,
        string[] exclusions,
        string[][]? keyQualifiers)
    {
        this.First = first;
        this.Last = last;
        this.PredefinedLength = predefinedLength;
        this.DataAttribute = dataAttribute;
        this.Components = components;
        this.Requisites = requisites;
        this.Exclusions = exclusions;
        this.KeyQualifiers = keyQualifiers;

        // An optional component adds nothing to the shortest data, and every component adds its maximum to
        // the longest.
        foreach (Gs1Component component in components)
        {
            this.MinimumLength += component.Optional ? 0 : component.Minimum;
            this.MaximumLength += component.Maximum;
        }
    }

    /// <summary>
    /// Gets the first Application Identifier of the range.
    /// </summary>
    public string First { get; }

    /// <summary>
    /// Gets the last Application Identifier of the range.
    /// </summary>
    public string Last { get; }

    /// <summary>
    /// Gets a value indicating whether the element string has a predefined length, so that it takes no
    /// separator before the element string that follows it.
    /// </summary>
    public bool PredefinedLength { get; }

    /// <summary>
    /// Gets a value indicating whether the Application Identifier may be a data attribute in the query
    /// string of a GS1 Digital Link URI.
    /// </summary>
    public bool DataAttribute { get; }

    /// <summary>
    /// Gets the components of the data, in order.
    /// </summary>
    public Gs1Component[] Components { get; }

    /// <summary>
    /// Gets the requisite rules. Each rule is a list of groups, and a rule holds when every pattern of at
    /// least one group matches another element string of the data.
    /// </summary>
    public string[][][] Requisites { get; }

    /// <summary>
    /// Gets the Application Identifier patterns that may not appear in the same data as this one.
    /// </summary>
    public string[] Exclusions { get; }

    /// <summary>
    /// Gets the qualifier sequences of a GS1 Digital Link primary key, or <see langword="null"/> when the
    /// Application Identifier is not a primary key. A key with no qualifiers has one empty sequence.
    /// </summary>
    public string[][]? KeyQualifiers { get; }

    /// <summary>
    /// Gets the length of the shortest data.
    /// </summary>
    public int MinimumLength { get; }

    /// <summary>
    /// Gets the length of the longest data.
    /// </summary>
    public int MaximumLength { get; }

    /// <summary>
    /// Returns whether the entry holds the given Application Identifier.
    /// </summary>
    /// <param name="identifier">The Application Identifier.</param>
    /// <returns><see langword="true"/> when the identifier is in the range of the entry.</returns>
    public bool Holds(ReadOnlySpan<char> identifier)
        => identifier.Length == this.First.Length
        && identifier.SequenceCompareTo(this.First) >= 0
        && identifier.SequenceCompareTo(this.Last) <= 0;
}
