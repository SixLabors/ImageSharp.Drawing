// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Barcodes;

namespace SixLabors.ImageSharp.Drawing.Tests.Barcodes;

/// <summary>
/// The table of <see cref="Gs1SyntaxDictionary"/> against the GS1 Barcode Syntax Dictionary release it comes from, gs1-syntax-dictionary.txt, release
/// 2026-01-27, as GS1 publishes it. The test parses the release file in the syntax its header defines and compares every entry, so a change of release is a
/// checked diff.
/// </summary>
public class Gs1SyntaxDictionaryTests
{
    /// <summary>
    /// The linters by the names the dictionary gives them.
    /// </summary>
    private static readonly Dictionary<string, Gs1Linter> LinterNames = new(StringComparer.Ordinal)
    {
        ["csum"] = Gs1Linter.Csum,
        ["csumalpha"] = Gs1Linter.CsumAlpha,
        ["gcppos1"] = Gs1Linter.GcpPos1,
        ["gcppos2"] = Gs1Linter.GcpPos2,
        ["yymmd0"] = Gs1Linter.Yymmd0,
        ["yymmdd"] = Gs1Linter.Yymmdd,
        ["yyyymmdd"] = Gs1Linter.Yyyymmdd,
        ["hh"] = Gs1Linter.Hh,
        ["mi"] = Gs1Linter.Mi,
        ["ss"] = Gs1Linter.Ss,
        ["hhmi"] = Gs1Linter.Hhmi,
        ["iso4217"] = Gs1Linter.Iso4217,
        ["iso3166"] = Gs1Linter.Iso3166,
        ["iso3166999"] = Gs1Linter.Iso3166999,
        ["iso3166alpha2"] = Gs1Linter.Iso3166Alpha2,
        ["iso5218"] = Gs1Linter.Iso5218,
        ["pcenc"] = Gs1Linter.PercentEncoded,
        ["yesno"] = Gs1Linter.YesNo,
        ["hyphen"] = Gs1Linter.Hyphen,
        ["latitude"] = Gs1Linter.Latitude,
        ["longitude"] = Gs1Linter.Longitude,
        ["importeridx"] = Gs1Linter.ImporterIndex,
        ["packagetype"] = Gs1Linter.PackageType,
        ["mediatype"] = Gs1Linter.MediaType,
        ["posinseqslash"] = Gs1Linter.PositionInSequence,
        ["nonzero"] = Gs1Linter.NonZero,
        ["winding"] = Gs1Linter.Winding,
        ["zero"] = Gs1Linter.Zero,
        ["pieceoftotal"] = Gs1Linter.PieceOfTotal,
        ["iban"] = Gs1Linter.Iban,
        ["nozeroprefix"] = Gs1Linter.NoZeroPrefix,
        ["hasnondigit"] = Gs1Linter.HasNonDigit,
        ["couponcode"] = Gs1Linter.CouponCode,
        ["couponposoffer"] = Gs1Linter.CouponPositiveOffer,
    };

    /// <summary>
    /// The table holds one entry for every entry line of release 2026-01-27, in the same order and with the same contents.
    /// </summary>
    [Fact]
    public void TableMatchesTheDictionaryRelease()
    {
        string path = System.IO.Path.Combine(AppContext.BaseDirectory, "Barcodes", "gs1-syntax-dictionary.txt");
        string[] lines = File.ReadAllLines(path);

        Assert.Contains("# Release: 2026-01-27", lines);

        ReadOnlySpan<Gs1ApplicationIdentifier> table = Gs1SyntaxDictionary.Entries;
        int index = 0;

        foreach (string line in lines)
        {
            // Entries are the lines that start with an Application Identifier; the rest are comments.
            if (line.Length == 0 || !char.IsAsciiDigit(line[0]))
            {
                continue;
            }

            Assert.True(index < table.Length, $"The table has no entry for: {line}");
            AssertEntry(line, table[index]);
            index++;
        }

        Assert.Equal(table.Length, index);
    }

    /// <summary>
    /// Compares one line of the dictionary, in the syntax "AIs [Flags] Specification [Attributes...] [# Title]", with its table entry.
    /// </summary>
    /// <param name="line">The dictionary line.</param>
    /// <param name="entry">The table entry.</param>
    private static void AssertEntry(string line, Gs1ApplicationIdentifier entry)
    {
        int title = line.IndexOf('#');
        string[] tokens = (title < 0 ? line : line[..title]).Split(' ', StringSplitOptions.RemoveEmptyEntries);

        string[] range = tokens[0].Split('-');
        Assert.Equal(range[0], entry.First);
        Assert.Equal(range[^1], entry.Last);

        int next = 1;
        bool predefinedLength = false;
        bool dataAttribute = false;
        if (tokens[1][0] is '*' or '?')
        {
            predefinedLength = tokens[1].Contains('*');
            dataAttribute = tokens[1].Contains('?');
            next = 2;
        }

        Assert.Equal(predefinedLength, entry.PredefinedLength);
        Assert.Equal(dataAttribute, entry.DataAttribute);

        List<string> components = [];
        List<string> exclusions = [];
        string keyQualifiers = null;
        for (int t = next; t < tokens.Length; t++)
        {
            string token = tokens[t];
            if (token[0] is 'N' or 'X' or 'Y' or 'Z' or '[')
            {
                components.Add(Describe(ParseComponent(token)));
            }
            else if (token.StartsWith("req=", StringComparison.Ordinal))
            {
                // The requisite rules hold over all carriers of an item, so the table leaves them out.
                continue;
            }
            else if (token.StartsWith("ex=", StringComparison.Ordinal))
            {
                exclusions.AddRange(token[3..].Split(','));
            }
            else if (token == "dlpkey")
            {
                keyQualifiers = string.Empty;
            }
            else
            {
                Assert.StartsWith("dlpkey=", token, StringComparison.Ordinal);
                keyQualifiers = token[7..];
            }
        }

        // Each part compares in the dictionary's own notation, so a difference reads as the line does.
        Assert.Equal(components, entry.Components.Select(Describe));
        Assert.Equal(exclusions, entry.Exclusions);
        Assert.Equal(keyQualifiers, entry.KeyQualifiers is null ? null : string.Join('|', entry.KeyQualifiers.Select(sequence => string.Join(',', sequence))));
    }

    /// <summary>
    /// Parses one component, such as "N14,csum,gcppos2", "X..20" or "[N3],iso3166".
    /// </summary>
    /// <param name="token">The component.</param>
    /// <returns>The component.</returns>
    private static Gs1Component ParseComponent(string token)
    {
        bool optional = token[0] == '[';
        int end = optional ? token.IndexOf(']') : token.IndexOf(',');
        if (end < 0)
        {
            end = token.Length;
        }

        string specification = optional ? token[1..end] : token[..end];
        int minimum = 1;
        int maximum;
        if (specification[1..].StartsWith("..", StringComparison.Ordinal))
        {
            maximum = int.Parse(specification[3..], System.Globalization.CultureInfo.InvariantCulture);
        }
        else
        {
            minimum = int.Parse(specification[1..], System.Globalization.CultureInfo.InvariantCulture);
            maximum = minimum;
        }

        // The linters follow the specification, after the closing bracket of an optional component.
        int lintersStart = end + (optional ? 1 : 0);
        Gs1Linter[] linters = lintersStart < token.Length
            ? [.. token[(lintersStart + 1)..].Split(',').Select(name => LinterNames[name])]
            : [];

        // The dictionary names the character sets N, X, Y and Z.
        Gs1CharacterSet characterSet = specification[0] switch
        {
            'N' => Gs1CharacterSet.Digits,
            'X' => Gs1CharacterSet.Set82,
            'Y' => Gs1CharacterSet.Set39,
            'Z' => Gs1CharacterSet.Set64,
            _ => throw new FormatException($"Unknown character set '{specification[0]}'."),
        };

        return new Gs1Component(characterSet, minimum, maximum, optional, linters);
    }

    /// <summary>
    /// Describes a component as text, so two components compare by value.
    /// </summary>
    /// <param name="component">The component.</param>
    /// <returns>The description.</returns>
    private static string Describe(Gs1Component component)
        => $"{component.CharacterSet} {component.Minimum}..{component.Maximum} {component.Optional} {string.Join(',', component.Linters)}";
}
