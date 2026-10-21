// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Helpers;

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// Joins the linear component and the 2D component of a GS1 Composite symbol, ISO/IEC 24723: the separator pattern of section 11.4 between them, the 2D
/// component above the linear component at the horizontal position of clause 12, and the human readable interpretation of the 2D component.
/// </summary>
internal static class Gs1CompositeLayout
{
    /// <summary>
    /// The number of modules over the finder pattern of a GS1 DataBar symbol whose separator section 11.4 treats apart: elements 1 to 3 of the finder.
    /// </summary>
    private const int FinderSeparatorModules = 13;

    /// <summary>
    /// The height of each of the three segments of the separator pattern of an EAN/UPC Composite symbol in modules. Section 11.4: the 6X separator steps out
    /// after 2X and back after 4X.
    /// </summary>
    private const float EanUpcSeparatorSegment = 2F;

    /// <summary>
    /// The number of Code 128 symbol characters, from the start character to the stop character, that clause 12 g) needs to place a CC-A or CC-B over a
    /// GS1-128 symbol.
    /// </summary>
    private const int MinimumGs1128Characters = 9;

    /// <summary>
    /// Gets the 13 modules of the right finder pattern of value 3 of GS1 DataBar Omnidirectional, Truncated, Stacked and Stacked Omnidirectional, elements 3
    /// to 5 as they stand in the symbol: a bar of 9 modules, a space of 1 and a bar of 3.
    /// </summary>
    private static ReadOnlySpan<byte> FinderValueThree => [1, 1, 1, 1, 1, 1, 1, 1, 1, 0, 1, 1, 1];

    /// <summary>
    /// Validates the element strings of the two components together, as the element strings of one whole symbol. The pairs of section 4.13.1 of the GS1
    /// General Specifications apply to the whole symbol, so an element string of one component can exclude one of the other.
    /// </summary>
    /// <param name="linear">The element strings of the linear component.</param>
    /// <param name="component">The element strings of the 2D component.</param>
    /// <exception cref="ArgumentException">The text is not valid element string syntax, or an element string or a pair of them breaks a rule.</exception>
    public static void Validate(ReadOnlySpan<char> linear, ReadOnlySpan<char> component)
    {
        // The length checks run before the buffers take their size from the texts. GS1 DataBar Expanded carries the most data of the linear components.
        _ = Gs1Data.PreparedCapacity(linear, DataBarExpandedEncoder.MaximumDataCharacters);
        _ = Gs1Data.PreparedCapacity(component, Gs1CompositeComponentEncoder.MaximumPrepared);

        int length = linear.Length + component.Length;
        PooledBuffer<char> joined = new(length);
        InlineArray16<Gs1Element> elementStorage = default;
        int elementCapacity = (length / 5) + 1;
        ValueListBuilder<Gs1Element> elements = elementCapacity <= Gs1Data.InlineElementCount ? new(elementStorage) : new(elementCapacity);
        try
        {
            Span<char> text = joined.Span;
            linear.CopyTo(text);
            component.CopyTo(text[linear.Length..]);
            Gs1Data.Parse(text, ref elements);
            Gs1Data.ValidateSymbol(text, elements.AsSpan());
        }
        finally
        {
            joined.Dispose();
            elements.Dispose();
        }
    }

    /// <summary>
    /// Builds the 2D component at the X dimension of the linear component, section 11.1, and adds its human readable interpretation above it. Section 4.14 rule
    /// 1d of the GS1 General Specifications lets the interpretation split, the key attributes above the symbol and the identification key below it.
    /// </summary>
    /// <param name="text">The element strings of the 2D component.</param>
    /// <param name="component">The smallest 2D component the symbol takes.</param>
    /// <param name="columns">The data columns.</param>
    /// <param name="ccCColumns">The data columns of CC-C when a CC-B cannot hold the data, or 0 when the symbol cannot take CC-C.</param>
    /// <param name="xDimension">The X dimension of the linear component in millimetres.</param>
    /// <param name="options">The options that carry the font.</param>
    /// <param name="part">The part to write.</param>
    /// <returns>The 2D component the symbol took.</returns>
    public static Gs1CompositeComponent BuildComponent(
        ReadOnlySpan<char> text,
        Gs1CompositeComponent component,
        int columns,
        int ccCColumns,
        double xDimension,
        BarcodeOptions options,
        ref BarcodeSymbol part)
    {
        Gs1CompositeComponent used = Gs1CompositeComponentEncoder.Build(text, component, columns, ccCColumns, xDimension, ref part);
        if (options.Font is null)
        {
            return used;
        }

        // The interpretation on one line is never longer than the text.
        InlineArray128<char> readableStorage = default;
        PooledBuffer<char> rented = default;
        Span<char> readable = text.Length <= 128 ? ((Span<char>)readableStorage)[..text.Length] : (rented = new PooledBuffer<char>(text.Length)).Span;
        try
        {
            int written = Gs1Data.WriteReadable(text, false, readable);
            part.ReserveText(1, written);
            part.AddText(readable[..written], 0F, part.WidthInModules, BarcodeTextSide.AboveBars, -BarcodeTextPlacement.Clearance);

            // Section 4.14 rule 2.a.iii of the GS1 General Specifications: "The font size SHOULD be at least 2 mm (0.08 in) in height".
            part.SetTextSize(Gs1Data.MinimumTextSize, 0F);
        }
        finally
        {
            rented.Dispose();
        }

        return used;
    }

    /// <summary>
    /// Adds the 1X separator pattern of section 11.4 above the top row of a GS1 DataBar or GS1-128 linear component. The separator is the complement of the
    /// row below it, light over the given modules at each end. Over elements 1 to 3 of a GS1 DataBar finder pattern it is light over the bars and alternates
    /// dark and light over the spaces, starting dark after a bar.
    /// </summary>
    /// <param name="linear">The linear component.</param>
    /// <param name="lightStart">The number of light modules at the start of the separator.</param>
    /// <param name="lightEnd">The number of light modules at the end of the separator.</param>
    /// <param name="finderStarts">The first module of the 13 the separator treats apart over each finder pattern of the row.</param>
    /// <param name="shiftOverValueThree">Whether the single dark module over a right finder pattern of value 3 moves one module right, over the start of its
    /// three module bar, as section 11.4 requires of GS1 DataBar Omnidirectional, Truncated, Stacked and Stacked Omnidirectional.</param>
    public static void AddSeparator(ref BarcodeSymbol linear, int lightStart, int lightEnd, ReadOnlySpan<int> finderStarts, bool shiftOverValueThree)
    {
        BarcodeRow row = linear.Rows[0];
        ReadOnlySpan<int> runs = linear.Runs.Slice(row.RunStart, row.RunCount);
        int width = BarcodeSymbol.RunWidth(runs);

        // One rental holds the modules of the row, the modules of the separator and its runs, at most two more than its modules.
        PooledBuffer<int> buffer = new((width * 3) + 2);
        try
        {
            Span<int> below = buffer.Span[..width];
            Span<int> separator = buffer.Span.Slice(width, width);
            int position = 0;
            for (int i = 0; i < runs.Length; i++)
            {
                below.Slice(position, runs[i]).Fill((i & 1) == 0 ? 1 : 0);
                position += runs[i];
            }

            for (int i = 0; i < width; i++)
            {
                separator[i] = 1 - below[i];
            }

            separator[..lightStart].Clear();
            separator[(width - lightEnd)..].Clear();
            foreach (int start in finderStarts)
            {
                for (int i = start; i < start + FinderSeparatorModules; i++)
                {
                    separator[i] = below[i] == 1 ? 0 : below[i - 1] == 1 ? 1 : 1 - separator[i - 1];
                }

                if (shiftOverValueThree && Matches(below.Slice(start, FinderSeparatorModules), FinderValueThree))
                {
                    separator.Slice(start, FinderSeparatorModules).Clear();
                    separator[start + 10] = 1;
                }
            }

            Span<int> separatorRuns = buffer.Span[(width * 2)..];
            int separatorRunCount = ToRuns(separator, separatorRuns);
            linear.InsertRows(0, separatorRuns[..separatorRunCount], [separatorRunCount], 1F);
        }
        finally
        {
            buffer.Dispose();
        }
    }

    /// <summary>
    /// Adds the 1X separator pattern of section 11.4 above the top row of a GS1 DataBar Expanded or GS1 DataBar Expanded Stacked linear component. The
    /// separator is light over the four modules at each end and treats the finder patterns of the top row apart.
    /// </summary>
    /// <param name="linear">The linear component.</param>
    public static void AddExpandedSeparator(ref BarcodeSymbol linear)
    {
        BarcodeRow row = linear.Rows[0];
        InlineArray16<int> finderStorage = default;
        Span<int> finders = finderStorage;
        int finderCount = ExpandedFinderStarts(BarcodeSymbol.RunWidth(linear.Runs.Slice(row.RunStart, row.RunCount)), finders);
        AddSeparator(ref linear, 4, 4, finders[..finderCount], false);
    }

    /// <summary>
    /// Encodes the parts of an EAN/UPC Composite symbol. Section 7.2: the linear component carries no linkage flag, and the 2D component stands above the 6X
    /// separator pattern.
    /// </summary>
    /// <param name="linear">The symbology of the linear component.</param>
    /// <param name="columns">The data columns of the 2D component, Table 1.</param>
    /// <param name="parts">The parts to write.</param>
    /// <param name="options">The options that control layout choices.</param>
    /// <param name="first">The digits of the linear component.</param>
    /// <param name="second">The element strings of the 2D component.</param>
    /// <param name="gtin">The GTIN that the linear component carries, with its check digit: 8, 12 or 13 digits.</param>
    public static void EncodeEanUpc(
        BarcodeSymbology linear,
        int columns,
        Span<BarcodeSymbol> parts,
        BarcodeOptions options,
        ReadOnlySpan<char> first,
        ReadOnlySpan<char> second,
        ReadOnlySpan<char> gtin)
    {
        // The note of section 4.13 of the GS1 General Specifications: "GTINs encoded in an EAN/UPC and ITF-14 symbols are to be regarded as element strings
        // prefixed by an inferred AI (01)". So the symbol validates as the inferred (01) element string, the GTIN right-justified in 14 digits, with the
        // element strings of the 2D component.
        InlineArray32<char> inferredStorage = default;
        Span<char> inferred = ((Span<char>)inferredStorage)[..18];
        "(01)".CopyTo(inferred);
        inferred[4..(18 - gtin.Length)].Fill('0');
        gtin.CopyTo(inferred[(18 - gtin.Length)..]);
        Validate(inferred, second);

        linear.Encode(first, options, ref parts[0]);
        BuildComponent(second, Gs1CompositeComponent.CcA, columns, 0, parts[0].XDimension, options, ref parts[1]);

        // The outermost guard bars of the linear component are its first and its last module, and EanUpcOffset puts its first module under the module
        // linearStart of the 2D component.
        int linearWidth = (int)parts[0].WidthInModules;
        int linearStart = -EanUpcOffset(linearWidth, (int)parts[1].WidthInModules);
        AddEanUpcSeparator(ref parts[1], linearStart, linearStart + linearWidth - 1);
    }

    /// <summary>
    /// Returns the offset of the 2D component of an EAN/UPC Composite symbol from its linear component. Clause 12 a): the last space module before the stop
    /// bar of the 2D component stands over the last bar of the linear component.
    /// </summary>
    /// <param name="linearWidth">The width of the linear component in modules, from its first guard bar to its last.</param>
    /// <param name="componentWidth">The width of the 2D component in modules, which ends with its one module stop bar.</param>
    /// <returns>The offset of the 2D component from the first module of the linear component, in modules.</returns>
    public static int EanUpcOffset(int linearWidth, int componentWidth)
        => (linearWidth - 1) - (componentWidth - 2);

    /// <summary>
    /// Returns the module of a GS1-128 symbol above which clause 12 g) puts the left edge of a CC-A or CC-B: its right quiet zone stands over the last space
    /// module of the symbol character at position (n - 9) div 2, the positions numbered from the right with the stop character at 0, where n is the number of
    /// symbol characters.
    /// </summary>
    /// <param name="linearWidth">The width of the GS1-128 symbol in modules, validated by <see cref="ValidateGs1128Length"/>.</param>
    /// <param name="componentWidth">The width of the 2D component in modules.</param>
    /// <returns>The offset of the 2D component from the first module of the linear component, in modules.</returns>
    public static int Gs1128Offset(int linearWidth, int componentWidth)
    {
        // The last space module of a symbol character or the stop character is the last module of its first eleven.
        int characters = Gs1128Characters(linearWidth);
        int position = (characters - MinimumGs1128Characters) / 2;
        return ((characters - 1 - position) * Code128Encoder.ModulesPerCharacter) + (Code128Encoder.ModulesPerCharacter - 1) - componentWidth;
    }

    /// <summary>
    /// Validates that a GS1-128 symbol has the symbol characters that clause 12 g) needs to place a CC-A or CC-B over it.
    /// </summary>
    /// <param name="linearWidth">The width of the GS1-128 symbol in modules.</param>
    /// <exception cref="ArgumentException">The GS1-128 symbol has fewer than nine symbol characters.</exception>
    public static void ValidateGs1128Length(int linearWidth)
    {
        int characters = Gs1128Characters(linearWidth);
        if (characters < MinimumGs1128Characters)
        {
            throw new ArgumentException(
                $"A GS1-128 Composite symbol with a CC-A or CC-B has at least {MinimumGs1128Characters} symbol characters; the linear component has {characters}.");
        }
    }

    /// <summary>
    /// Returns where the 2D component stands: directly above the linear component, its first module over the given module of the linear component.
    /// </summary>
    /// <param name="parts">The linear component and the 2D component.</param>
    /// <param name="linearOrigin">The top left corner of the first module of the linear component, in pixels.</param>
    /// <param name="offset">The module of the linear component under the first module of the 2D component.</param>
    /// <param name="options">The sizing options.</param>
    /// <returns>The top left corner of the first module of the 2D component, in pixels.</returns>
    public static PointF PlaceAbove(ReadOnlySpan<BarcodeSymbol> parts, PointF linearOrigin, int offset, BarcodeOptions options)
    {
        int pixels = options.ModulePixels(parts[0].XDimension);
        return new PointF(linearOrigin.X + (offset * pixels), linearOrigin.Y - (parts[1].HeightInModules * pixels));
    }

    /// <summary>
    /// Returns the number of symbol characters of a GS1-128 symbol, from the start character to the stop character. The stop character is two modules wider
    /// than a symbol character.
    /// </summary>
    /// <param name="linearWidth">The width of the GS1-128 symbol in modules.</param>
    /// <returns>The number of symbol characters.</returns>
    private static int Gs1128Characters(int linearWidth)
        => (linearWidth - (Code128Encoder.StopModules - Code128Encoder.ModulesPerCharacter)) / Code128Encoder.ModulesPerCharacter;

    /// <summary>
    /// Adds the 6X separator pattern of an EAN/UPC Composite symbol, section 11.4, below the rows of the 2D component: a light space with two 1X bars that
    /// extend the outermost guard bars of the linear component. The bars step outward by 1X after 2X and back after 4X, three segments of 2X.
    /// </summary>
    /// <param name="component">The 2D component.</param>
    /// <param name="left">The module of the 2D component above the left outermost guard bar.</param>
    /// <param name="right">The module of the 2D component above the right outermost guard bar.</param>
    private static void AddEanUpcSeparator(ref BarcodeSymbol component, int left, int right)
    {
        int width = (int)component.WidthInModules;
        PooledBuffer<int> buffer = new((width * 2) + 2);
        try
        {
            Span<int> modules = buffer.Span[..width];
            Span<int> runs = buffer.Span[width..];

            // Each segment row has two bars, so it has at most six runs, and the three rows fit the run buffer together. One insert takes all three rows.
            InlineArray4<int> countStorage = default;
            Span<int> counts = ((Span<int>)countStorage)[..3];
            int written = 0;
            for (int segment = 0; segment < counts.Length; segment++)
            {
                // The middle segment stands one module further out on each side.
                int step = segment == 1 ? 1 : 0;
                modules.Clear();
                modules[left - step] = 1;
                modules[right + step] = 1;
                counts[segment] = ToRuns(modules, runs[written..]);
                written += counts[segment];
            }

            component.InsertRows(component.Rows.Length, runs[..written], counts, EanUpcSeparatorSegment);
        }
        finally
        {
            buffer.Dispose();
        }
    }

    /// <summary>
    /// Writes where the separator of a GS1 DataBar Expanded row treats its finder patterns apart. A row starts with a guard space and bar, and every pair of
    /// symbol characters holds a finder of 15 modules after its first character of 17, so the finders start at module 19 and every 49 modules after it. Section
    /// 11.4 treats elements 1 to 3 of the first finder of each two and elements 3 to 5 of the second, which start two modules into it.
    /// </summary>
    /// <param name="width">The width of the row in modules.</param>
    /// <param name="starts">The buffer the first modules are written to.</param>
    /// <returns>The number of finder patterns.</returns>
    private static int ExpandedFinderStarts(int width, Span<int> starts)
    {
        int count = 0;
        for (int start = 19, k = 0; start + FinderSeparatorModules <= width; start += 49, k++)
        {
            int first = (k & 1) == 0 ? start : start + 2;
            if (first + FinderSeparatorModules <= width)
            {
                starts[count++] = first;
            }
        }

        return count;
    }

    /// <summary>
    /// Returns whether two module sequences are equal.
    /// </summary>
    /// <param name="modules">The modules.</param>
    /// <param name="pattern">The pattern.</param>
    /// <returns><see langword="true"/> when they are equal.</returns>
    private static bool Matches(ReadOnlySpan<int> modules, ReadOnlySpan<byte> pattern)
    {
        for (int i = 0; i < pattern.Length; i++)
        {
            if (modules[i] != pattern[i])
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Converts a row of modules into runs that alternate bar and space from a bar. A row that starts or ends light takes a zero width bar at that end.
    /// </summary>
    /// <param name="modules">The modules, 1 for dark.</param>
    /// <param name="runs">The buffer the runs are written to, at least two longer than the modules.</param>
    /// <returns>The number of runs.</returns>
    private static int ToRuns(ReadOnlySpan<int> modules, Span<int> runs)
    {
        int count = 0;
        if (modules[0] == 0)
        {
            runs[count++] = 0;
        }

        int width = 1;
        for (int i = 1; i < modules.Length; i++)
        {
            if (modules[i] != modules[i - 1])
            {
                runs[count++] = width;
                width = 1;
            }
            else
            {
                width++;
            }
        }

        runs[count++] = width;
        if (modules[^1] == 0)
        {
            runs[count++] = 0;
        }

        return count;
    }
}
