// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Helpers;

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// Shared encodation for the Code 128 symbology family. Section 5.4 of the GS1 General Specifications describes the symbology and names ISO/IEC 15417 as its
/// full definition. Every symbol character is six elements wide, three bars and three spaces of one to four modules, and carries eleven modules. The stop
/// character is the one exception: seven elements over thirteen modules.
/// <para>
/// A GS1 symbol starts with Start C and a Function 1 character, step 1 of section 5.4.7.6 of the GS1 General Specifications. Other text starts in code set C
/// before four or more digits or before two digits that are the whole text, and otherwise in the one of code sets A and B that carries the first character only
/// one of them carries. A run of four or more digits changes to code set C, after its first digit when the run is odd, as step 3 does. A character that only
/// the other of code sets A and B carries takes a Shift when the next such character is in the current code set, and a code set change otherwise.
/// </para>
/// <para>
/// Clause 4.3.4.2 d) of ISO/IEC 15417 encodes the byte values 128 to 255, ISO/IEC 8859-1 by default, with the FNC4 character. A single FNC4 adds 128 to the
/// next data character, and two FNC4 characters start or end extended mode, which adds 128 to every data character between them. Note 3 of Annex E chooses
/// code set A or B for such a character by its value less 128, and takes extended mode for a run of five or more of them, or three or more at the end of the
/// data. A shorter run takes a single FNC4 before each character.
/// </para>
/// </summary>
internal static class Code128Encoder
{
    /// <summary>
    /// The quiet zone in modules on each side. Section 5.4.4.2 requires ten times the X-dimension.
    /// </summary>
    public const int QuietZone = 10;

    /// <summary>
    /// The symbol character value of the Function 1 character. Section 5.4.2 places it directly after the start character to mark a GS1-128 symbol, and section
    /// 5.4.7.4 transmits it "in the third or subsequent character position" as the control character GS, the separator of section 7.8.6.2.
    /// </summary>
    public const int FunctionOne = 102;

    /// <summary>
    /// The largest number of characters a Code 128 symbol carries.
    /// </summary>
    public const int MaximumLength = 500;

    /// <summary>
    /// The nominal X dimension in millimetres of a GS1 symbol: the target of 0.495 mm of Table 5-51 of the GS1 General Specifications, and of most other GS1
    /// symbol specification tables that list GS1-128. A Code 128 symbol that is not a GS1 symbol takes <see cref="BarcodeSymbology.DefaultXDimension"/>,
    /// because section 4.4.1 of ISO/IEC 15417 gives no value: "The width of a module should be defined by the application specification".
    /// </summary>
    public const double NominalXDimension = 0.495;

    /// <summary>
    /// The symbol character value of Shift, which changes code set A to B, or B to A, for one character.
    /// </summary>
    private const int Shift = 98;

    /// <summary>
    /// The symbol character value of Code C, which changes code set A or B to code set C.
    /// </summary>
    private const int LatchC = 99;

    /// <summary>
    /// The symbol character value of Code B, which changes code set A or C to code set B.
    /// </summary>
    private const int LatchB = 100;

    /// <summary>
    /// The symbol character value of Code A, which changes code set B or C to code set A.
    /// </summary>
    private const int LatchA = 101;

    /// <summary>
    /// The symbol character value of FNC4 in code set A, Table 1 of ISO/IEC 15417.
    /// </summary>
    private const int FunctionFourInA = 101;

    /// <summary>
    /// The symbol character value of FNC4 in code set B, Table 1 of ISO/IEC 15417. Code set C has no FNC4.
    /// </summary>
    private const int FunctionFourInB = 100;

    /// <summary>
    /// The shortest run of characters above 127 that takes extended mode, note 3 of Annex E of ISO/IEC 15417. The two FNC4 characters before the run and the
    /// two after it cost four symbol characters, and a single FNC4 before each character of the run costs one each.
    /// </summary>
    private const int ShortestExtendedRun = 5;

    /// <summary>
    /// The shortest run of characters above 127 that takes extended mode when the run ends the data, note 3 of Annex E of ISO/IEC 15417. The data ends in
    /// extended mode, so only the two FNC4 characters before the run are needed.
    /// </summary>
    private const int ShortestFinalExtendedRun = 3;

    /// <summary>
    /// The symbol character value of Start A.
    /// </summary>
    private const int StartA = 103;

    /// <summary>
    /// The symbol character value of Start B.
    /// </summary>
    private const int StartB = 104;

    /// <summary>
    /// The symbol character value of Start C.
    /// </summary>
    private const int StartC = 105;

    /// <summary>
    /// The symbol character value of the stop character, the one character of seven elements.
    /// </summary>
    private const int Stop = 106;

    /// <summary>
    /// The modulus of the symbol check character, step 5 of section 5.4.7.5.1.
    /// </summary>
    private const int CheckModulus = 103;

    /// <summary>
    /// The element count of a symbol character, section 5.4.1: three bars and three spaces.
    /// </summary>
    private const int ElementsPerCharacter = 6;

    /// <summary>
    /// The element count of the stop character, section 5.4.1: four bars and three spaces.
    /// </summary>
    private const int StopElements = 7;

    /// <summary>
    /// The width of a symbol character in modules, section 5.4.7.2.
    /// </summary>
    private const int ModulesPerCharacter = 11;

    /// <summary>
    /// The width of the stop character in modules, section 5.4.7.2.
    /// </summary>
    private const int StopModules = 13;

    /// <summary>
    /// The character count the inline buffers are sized for: 128 values, two for each character of ASCII text or three for each character of text that has a
    /// character above 127, and 256 look-ahead bytes, four for each character. The values also hold five characters that are not text, so ASCII text of at
    /// most 61 characters encodes inline. Section 5.4.4.3 caps a GS1-128 symbol at 48 data characters of ASCII, so every conforming GS1-128 symbol encodes
    /// inline.
    /// </summary>
    private const int InlineLength = 64;

    /// <summary>
    /// The nominal bar height in modules: the minimum symbol height of 31.75 mm at the X dimension of 0.495 mm, Table 5-51 of the GS1 General Specifications.
    /// </summary>
    private const float NominalBarHeight = (float)(31.75 / NominalXDimension);

    /// <summary>
    /// The bar height of a GS1 symbol in millimetres. Footnote (****) of Table 5-47 of the GS1 General Specifications: "For GS1-128 and ITF-14 symbols the
    /// minimum symbol height for General distribution scanning is always 31.75 millimetres (1.250 inch)."
    /// </summary>
    private const double Gs1BarHeight = 31.75;

    /// <summary>
    /// Gets the element widths of every symbol character, packed four bits per element, the first element in the highest bits. Table 5-25 of the GS1 General
    /// Specifications lists them: six elements each, three bars and three spaces of one to four modules, except the stop character, <see cref="Stop"/>, which
    /// takes seven.
    /// </summary>
    private static ReadOnlySpan<uint> Patterns =>
    [
        0x212222, 0x222122, 0x222221, 0x121223, 0x121322, 0x131222, 0x122213, 0x122312,
        0x132212, 0x221213, 0x221312, 0x231212, 0x112232, 0x122132, 0x122231, 0x113222,
        0x123122, 0x123221, 0x223211, 0x221132, 0x221231, 0x213212, 0x223112, 0x312131,
        0x311222, 0x321122, 0x321221, 0x312212, 0x322112, 0x322211, 0x212123, 0x212321,
        0x232121, 0x111323, 0x131123, 0x131321, 0x112313, 0x132113, 0x132311, 0x211313,
        0x231113, 0x231311, 0x112133, 0x112331, 0x132131, 0x113123, 0x113321, 0x133121,
        0x313121, 0x211331, 0x231131, 0x213113, 0x213311, 0x213131, 0x311123, 0x311321,
        0x331121, 0x312113, 0x312311, 0x332111, 0x314111, 0x221411, 0x431111, 0x111224,
        0x111422, 0x121124, 0x121421, 0x141122, 0x141221, 0x112214, 0x112412, 0x122114,
        0x122411, 0x142112, 0x142211, 0x241211, 0x221114, 0x413111, 0x241112, 0x134111,
        0x111242, 0x121142, 0x121241, 0x114212, 0x124112, 0x124211, 0x411212, 0x421112,
        0x421211, 0x212141, 0x214121, 0x412121, 0x111143, 0x111341, 0x131141, 0x114113,
        0x114311, 0x411113, 0x411311, 0x113141, 0x114131, 0x311141, 0x411131, 0x211412,
        0x211214, 0x211232, 0x2331112,
    ];

    /// <summary>
    /// Encodes text and builds the symbol. The runs carry the start character, the code set switches, the check character of section 5.4.3.6 and the stop
    /// character, whose seven elements end the list on a bar. Every symbology in the family shares this step, so only the encodation differs between them. Code
    /// 128 carries no guard bars, so every bar runs the full height. The human readable interpretation prints below the symbol, adjacent to it as rule 1 of
    /// section 4.14 asks.
    /// </summary>
    /// <param name="text">The text to encode. Every character must be U+0000 to U+00FF.</param>
    /// <param name="gs1">Whether this is a GS1 symbol. Such a symbol carries a Function 1 character after its start character, and encodes every
    /// <see cref="Gs1Data.Separator"/> in the text as another one.</param>
    /// <param name="readablePrefix">The characters of the human readable interpretation before its body.</param>
    /// <param name="readableBody">The body of the human readable interpretation.</param>
    /// <param name="readableSuffix">The characters of the human readable interpretation after its body.</param>
    /// <param name="options">The options that control layout choices.</param>
    /// <param name="symbol">The symbol to write.</param>
    /// <exception cref="ArgumentException">The text carries a character the symbology cannot encode.</exception>
    public static void BuildSymbol(
        ReadOnlySpan<char> text,
        bool gs1,
        ReadOnlySpan<char> readablePrefix,
        ReadOnlySpan<char> readableBody,
        ReadOnlySpan<char> readableSuffix,
        BarcodeOptions options,
        ref BarcodeSymbol symbol)
        => BuildSymbol(text, gs1, readablePrefix, readableBody, readableSuffix, 0, double.PositiveInfinity, Code128Linkage.None, options, ref symbol);

    /// <summary>
    /// Encodes text and builds the symbol at an X dimension clamped to the range of the application standard. The steps are those of
    /// <see cref="BuildSymbol(ReadOnlySpan{char}, bool, ReadOnlySpan{char}, ReadOnlySpan{char}, ReadOnlySpan{char}, BarcodeOptions, ref BarcodeSymbol)"/>.
    /// </summary>
    /// <param name="text">The text to encode. Every character must be U+0000 to U+00FF.</param>
    /// <param name="gs1">Whether this is a GS1 symbol. Such a symbol carries a Function 1 character after its start character, and encodes every
    /// <see cref="Gs1Data.Separator"/> in the text as another one.</param>
    /// <param name="readablePrefix">The characters of the human readable interpretation before its body.</param>
    /// <param name="readableBody">The body of the human readable interpretation.</param>
    /// <param name="readableSuffix">The characters of the human readable interpretation after its body.</param>
    /// <param name="minimumXDimension">The smallest X dimension the application standard permits, in millimetres.</param>
    /// <param name="maximumXDimension">The largest X dimension the application standard permits, in millimetres.</param>
    /// <param name="linkage">The linkage flag of a GS1-128 symbol to which a 2D component of a GS1 Composite symbol is linked.</param>
    /// <param name="options">The options that control layout choices.</param>
    /// <param name="symbol">The symbol to write.</param>
    /// <exception cref="ArgumentException">The text carries a character the symbology cannot encode.</exception>
    public static void BuildSymbol(
        ReadOnlySpan<char> text,
        bool gs1,
        ReadOnlySpan<char> readablePrefix,
        ReadOnlySpan<char> readableBody,
        ReadOnlySpan<char> readableSuffix,
        double minimumXDimension,
        double maximumXDimension,
        Code128Linkage linkage,
        BarcodeOptions options,
        ref BarcodeSymbol symbol)
    {
        // The bars of a GS1 symbol are 31.75 millimetres at any X dimension. The module draws at a whole number of pixels.
        double xDimension = options.ResolveXDimension(minimumXDimension, gs1 ? NominalXDimension : BarcodeSymbology.DefaultXDimension, maximumXDimension);
        Code128Layout layout = new()
        {
            XDimension = xDimension,
            NominalBarHeight = gs1 ? (float)(Gs1BarHeight / xDimension) : NominalBarHeight,
            TextClearance = BarcodeTextPlacement.Clearance,
            HorizontalQuietZone = QuietZone,
        };

        BuildSymbol(text, gs1, readablePrefix, readableBody, readableSuffix, [], linkage, in layout, options, ref symbol);

        // Section 4.14 rule 2.a.iii of the GS1 General Specifications: "The font size SHOULD be at least 2 mm (0.08 in) in height".
        if (gs1)
        {
            symbol.SetTextSize(Gs1Data.MinimumTextSize, 0F);
        }
    }

    /// <summary>
    /// Encodes text and builds the symbol at the dimensions of an application standard, which can print a second line above the bars. The ink of the line
    /// above the bars stays within the symbol and the quiet zones of <see cref="QuietZone"/>.
    /// </summary>
    /// <param name="text">The text to encode. Every character must be U+0000 to U+00FF.</param>
    /// <param name="gs1">Whether this is a GS1 symbol. Such a symbol carries a Function 1 character after its start character, and encodes every
    /// <see cref="Gs1Data.Separator"/> in the text as another one.</param>
    /// <param name="readablePrefix">The characters of the human readable interpretation before its body.</param>
    /// <param name="readableBody">The body of the human readable interpretation.</param>
    /// <param name="readableSuffix">The characters of the human readable interpretation after its body.</param>
    /// <param name="above">The line above the bars, or empty for none.</param>
    /// <param name="linkage">The linkage flag of a GS1-128 symbol to which a 2D component of a GS1 Composite symbol is linked.</param>
    /// <param name="layout">The dimensions of the application standard.</param>
    /// <param name="options">The options that control layout choices.</param>
    /// <param name="symbol">The symbol to write.</param>
    /// <exception cref="ArgumentException">The text carries a character the symbology cannot encode.</exception>
    public static void BuildSymbol(
        ReadOnlySpan<char> text,
        bool gs1,
        ReadOnlySpan<char> readablePrefix,
        ReadOnlySpan<char> readableBody,
        ReadOnlySpan<char> readableSuffix,
        ReadOnlySpan<char> above,
        Code128Linkage linkage,
        in Code128Layout layout,
        BarcodeOptions options,
        ref BarcodeSymbol symbol)
    {
        double xDimension = layout.XDimension;
        Guard.MustBeLessThanOrEqualTo(text.Length, MaximumLength, nameof(text));

        // The code sets carry ASCII 0 to 127 between them, and clause 4.3.4.2 d) of ISO/IEC 15417 adds the byte values 128 to 255 with FNC4, so anything above
        // U+00FF is rejected. The message decodes the code point at the first such unit, so a surrogate pair reports as the one character it is.
        int invalid = text.IndexOfAnyExceptInRange('\0', '\u00FF');
        if (invalid >= 0)
        {
            throw new ArgumentException(
                $"Code 128 encodes U+0000 to U+00FF; {text.CodePointAt(invalid).ToDisplayString()} is outside that range.",
                nameof(text));
        }

        // A code set change or a shift costs one extra symbol character, so two per ASCII character bounds the run. A character above 127 can also take a
        // single FNC4, so three per character bounds text that has one. Extended mode costs at most four FNC4 characters for a run of at least three, which
        // the third value per character covers. The start, the optional Function 1, the optional linkage flag, the check and the stop add five more. The text
        // is at most MaximumLength characters, so the buffers are bounded: short text works inline, and longer text rents each buffer once.
        int capacity = (text.Length * (text.ContainsAnyExceptInRange('\0', '\u007F') ? 3 : 2)) + 5;
        int lookahead = text.Length + 1;
        InlineArray128<int> valueStorage = default;
        InlineArray256<byte> lookaheadStorage = default;
        PooledBuffer<int> rentedValues = default;
        PooledBuffer<byte> rentedLookahead = default;
        Span<int> values = capacity <= InlineLength * 2
            ? valueStorage
            : (rentedValues = new PooledBuffer<int>(capacity)).Span;

        Span<byte> lookaheadTables = lookahead <= InlineLength
            ? lookaheadStorage
            : (rentedLookahead = new PooledBuffer<byte>(lookahead * 4)).Span;

        try
        {
            int count = new Encodation(text, gs1, linkage, values, lookaheadTables[..(lookahead * 4)]).Run();

            // The stop character is always the last value, so every other value takes the six elements and eleven modules of an ordinary character.
            Span<int> runs = symbol.BeginLinear(((count - 1) * ElementsPerCharacter) + StopElements, xDimension, out Span<float> heights, out _);
            WriteRuns(values[..count], runs);
            int widthInModules = ((count - 1) * ModulesPerCharacter) + StopModules;

            float barHeight = options.ResolveBarHeight(xDimension, MathF.Max(layout.NominalBarHeight, widthInModules * layout.MinimumHeightFraction));
            heights.Fill(barHeight);

            int readableLength = readablePrefix.Length + readableBody.Length + readableSuffix.Length;
            if (options.Font is not null && readableLength + above.Length > 0)
            {
                symbol.ReserveText(above.IsEmpty ? 1 : 2, readableLength + above.Length);
                float textEdge = barHeight + layout.TextClearance;
                symbol.AddText(readablePrefix, readableBody, readableSuffix, 0F, widthInModules, BarcodeTextSide.BelowBars, textEdge);

                // The line above the bars spans the symbol and the quiet zones of QuietZone, and its ink stays within them.
                if (!above.IsEmpty)
                {
                    symbol.AddTextWithinSpan(above, -QuietZone, widthInModules + QuietZone, BarcodeTextSide.AboveBars, -layout.AboveClearance);
                }
            }

            float horizontal = layout.HorizontalQuietZone;
            symbol.EndLinear(horizontal, horizontal, layout.VerticalQuietZone, layout.VerticalQuietZone, 0F, 1F, false);
        }
        finally
        {
            rentedValues.Dispose();
            rentedLookahead.Dispose();
        }
    }

    /// <summary>
    /// Increments a saturating distance, so a run longer than a byte still compares as further away.
    /// </summary>
    /// <param name="value">The distance to increment.</param>
    /// <returns>The incremented distance, held at the maximum.</returns>
    private static byte SaturatingIncrement(byte value) => value == byte.MaxValue ? value : (byte)(value + 1);

    /// <summary>
    /// Adds to a saturating count, so a run longer than a byte still compares as long enough.
    /// </summary>
    /// <param name="value">The count to add to.</param>
    /// <param name="amount">The amount to add.</param>
    /// <returns>The new count.</returns>
    private static byte SaturatingAdd(byte value, int amount) => value + amount >= byte.MaxValue ? byte.MaxValue : (byte)(value + amount);

    /// <summary>
    /// Writes the run lengths of symbol character values, starting with a bar.
    /// </summary>
    /// <param name="values">The symbol character values, including the start, check and stop characters.</param>
    /// <param name="runs">The alternating bar and space run widths in modules to write.</param>
    private static void WriteRuns(ReadOnlySpan<int> values, Span<int> runs)
    {
        ReadOnlySpan<uint> patterns = Patterns;
        int index = 0;
        foreach (int value in values)
        {
            uint pattern = patterns[value];
            int elements = value == Stop ? StopElements : ElementsPerCharacter;
            for (int shift = (elements - 1) * 4; shift >= 0; shift -= 4)
            {
                runs[index++] = (int)((pattern >> shift) & 0xF);
            }
        }
    }

    /// <summary>
    /// Computes the symbol check character. Section 5.4.7.5.1 weights the start character by one and each following character by its position, sums the
    /// products and takes the remainder modulo 103.
    /// </summary>
    /// <param name="values">The symbol character values, starting with the start character.</param>
    /// <returns>The symbol check character value.</returns>
    private static int ComputeCheckCharacter(ReadOnlySpan<int> values)
    {
        long sum = values[0];
        for (int i = 1; i < values.Length; i++)
        {
            sum += (long)values[i] * i;
        }

        return (int)(sum % CheckModulus);
    }

    /// <summary>
    /// Determines whether only code set A carries the character. Section 5.4.3.3.1 gives code set A the control characters, which code set B replaces with the
    /// lower case letters. Note 3 of Annex E of ISO/IEC 15417 classifies a character above 127 by its value less 128, the character that FNC4 shifts.
    /// </summary>
    /// <param name="character">The character to classify.</param>
    /// <returns><see langword="true"/> when only code set A carries it.</returns>
    private static bool OnlyInSetA(char character) => (character & 0x7F) < 32;

    /// <summary>
    /// Determines whether only code set B carries the character. Section 5.4.3.3.2 gives code set B the lower case letters and the characters above them, which
    /// code set A replaces with control codes. Note 3 of Annex E of ISO/IEC 15417 classifies a character above 127 by its value less 128, the character that
    /// FNC4 shifts.
    /// </summary>
    /// <param name="character">The character to classify.</param>
    /// <returns><see langword="true"/> when only code set B carries it.</returns>
    private static bool OnlyInSetB(char character) => (character & 0x7F) >= 96;

    /// <summary>
    /// Returns the symbol character value of a character in the given code set. Code set A runs from the space at value zero through to the control characters,
    /// and code set B from the space through to the delete character. A character above 127 takes the value of the character 128 below it, which FNC4 shifts
    /// back up, as Annex F of ISO/IEC 15417 gives: C = (D - 128).
    /// </summary>
    /// <param name="set">The code set to encode in.</param>
    /// <param name="character">The character to encode.</param>
    /// <returns>The symbol character value.</returns>
    private static int ValueIn(Code128CodeSet set, char character)
    {
        int value = character & 0x7F;
        if (set == Code128CodeSet.A && value < 32)
        {
            return value + 64;
        }

        return value - 32;
    }

    /// <summary>
    /// Returns the symbol character value of FNC4 in the given code set, Table 1 of ISO/IEC 15417.
    /// </summary>
    /// <param name="set">The code set, A or B. Code set C has no FNC4.</param>
    /// <returns>The symbol character value.</returns>
    private static int FunctionFourIn(Code128CodeSet set) => set == Code128CodeSet.A ? FunctionFourInA : FunctionFourInB;

    /// <summary>
    /// The state of one encodation: the text, the look-ahead the code set choices read, and the symbol character values written so far.
    /// </summary>
    private ref struct Encodation
    {
        /// <summary>
        /// The text to encode.
        /// </summary>
        private readonly ReadOnlySpan<char> text;

        /// <summary>
        /// Whether this is a GS1 symbol, which encodes every <see cref="Gs1Data.Separator"/> as a Function 1 character.
        /// </summary>
        private readonly bool gs1;

        /// <summary>
        /// The linkage flag the symbol carries before its check character.
        /// </summary>
        private readonly Code128Linkage linkage;

        /// <summary>
        /// The buffer the symbol character values are written to.
        /// </summary>
        private readonly Span<int> values;

        /// <summary>
        /// The distance from each position to the next character only code set A carries, one entry per position and one past the end.
        /// </summary>
        private readonly Span<byte> nextOnlyInA;

        /// <summary>
        /// The distance from each position to the next character only code set B carries, one entry per position and one past the end.
        /// </summary>
        private readonly Span<byte> nextOnlyInB;

        /// <summary>
        /// The digits a code set C run carries from each position, one entry per position and one past the end.
        /// </summary>
        private readonly Span<byte> digitsFromEven;

        /// <summary>
        /// The length of the run of characters above 127 from each position, one entry per position and one past the end.
        /// </summary>
        private readonly Span<byte> extendedRun;

        /// <summary>
        /// The number of symbol character values written.
        /// </summary>
        private int written;

        /// <summary>
        /// Whether two FNC4 characters have started extended mode, which adds 128 to every data character until two more end it.
        /// </summary>
        private bool extendedMode;

        /// <summary>
        /// Initializes a new instance of the <see cref="Encodation"/> struct and measures the look-ahead.
        /// </summary>
        /// <param name="text">The text to encode.</param>
        /// <param name="gs1">Whether this is a GS1 symbol.</param>
        /// <param name="linkage">The linkage flag the symbol carries before its check character.</param>
        /// <param name="values">The buffer the symbol character values are written to, of at least twice the text length plus five, or three times the text
        /// length plus five when the text has a character above 127.</param>
        /// <param name="lookahead">Scratch for the four look-ahead tables, each one entry longer than the text.</param>
        public Encodation(ReadOnlySpan<char> text, bool gs1, Code128Linkage linkage, Span<int> values, Span<byte> lookahead)
        {
            int stride = text.Length + 1;
            this.text = text;
            this.gs1 = gs1;
            this.linkage = linkage;
            this.values = values;
            this.nextOnlyInA = lookahead[..stride];
            this.nextOnlyInB = lookahead.Slice(stride, stride);
            this.digitsFromEven = lookahead.Slice(stride * 2, stride);
            this.extendedRun = lookahead.Slice(stride * 3, stride);
            this.written = 0;
            this.extendedMode = false;
            this.MeasureLookahead();
        }

        /// <summary>
        /// Encodes the text, then writes the check character and the stop character.
        /// </summary>
        /// <returns>The number of symbol character values written.</returns>
        public int Run()
        {
            ReadOnlySpan<char> text = this.text;
            Code128CodeSet set = this.ChooseStartSet();
            if (this.gs1)
            {
                this.values[this.written++] = FunctionOne;
            }

            int position = 0;
            while (position < text.Length)
            {
                if (this.gs1 && text[position] == Gs1Data.Separator)
                {
                    this.values[this.written++] = FunctionOne;
                    position++;
                    continue;
                }

                // Extended mode covers exactly one run of characters above 127, so two FNC4 characters end it before the next character of 127 or below. That
                // happens before any change to code set C, which has no FNC4.
                if (this.extendedMode && text[position] <= '\u007F')
                {
                    this.values[this.written++] = FunctionFourIn(set);
                    this.values[this.written++] = FunctionFourIn(set);
                    this.extendedMode = false;
                    continue;
                }

                int digits = this.digitsFromEven[position];
                if (set != Code128CodeSet.C && digits >= 4)
                {
                    if ((digits & 1) == 0)
                    {
                        this.values[this.written++] = LatchC;
                        set = Code128CodeSet.C;
                        continue;
                    }

                    // An odd run starts one character late, so the current set takes the first digit and code set C takes the even remainder.
                    this.values[this.written++] = ValueIn(set, text[position++]);
                    if (this.digitsFromEven[position] >= 4)
                    {
                        this.values[this.written++] = LatchC;
                        set = Code128CodeSet.C;
                        continue;
                    }
                }

                char character = text[position];
                bool extended = character > '\u007F';

                // Note 3 of Annex E of ISO/IEC 15417: a long run of characters above 127 takes extended mode. Code set C has no FNC4, so the run waits for the
                // change out of code set C below. A saturated run length is long enough whether or not it ends the data.
                if (extended && !this.extendedMode && set != Code128CodeSet.C)
                {
                    int run = this.extendedRun[position];
                    if (run >= (position + run == text.Length ? ShortestFinalExtendedRun : ShortestExtendedRun))
                    {
                        this.values[this.written++] = FunctionFourIn(set);
                        this.values[this.written++] = FunctionFourIn(set);
                        this.extendedMode = true;
                        continue;
                    }
                }

                // Outside extended mode a single FNC4 adds 128 to the one data character after it. Clause 4.3.4.2 d) lets a Shift follow the FNC4, so the
                // FNC4 takes its value from the code set in force before the Shift.
                bool functionFour = extended && !this.extendedMode;
                if (set == Code128CodeSet.B && OnlyInSetA(character))
                {
                    // A shift serves one character when the next character only one of the two sets carries belongs to the current set.
                    if (position < text.Length - 1 && this.nextOnlyInB[position + 1] < this.nextOnlyInA[position + 1])
                    {
                        if (functionFour)
                        {
                            this.values[this.written++] = FunctionFourInB;
                        }

                        this.values[this.written++] = Shift;
                        this.values[this.written++] = ValueIn(Code128CodeSet.A, character);
                        position++;
                        continue;
                    }

                    this.values[this.written++] = LatchA;
                    set = Code128CodeSet.A;
                    continue;
                }

                if (set == Code128CodeSet.A && OnlyInSetB(character))
                {
                    if (position < text.Length - 1 && this.nextOnlyInA[position + 1] < this.nextOnlyInB[position + 1])
                    {
                        if (functionFour)
                        {
                            this.values[this.written++] = FunctionFourInA;
                        }

                        this.values[this.written++] = Shift;
                        this.values[this.written++] = ValueIn(Code128CodeSet.B, character);
                        position++;
                        continue;
                    }

                    this.values[this.written++] = LatchB;
                    set = Code128CodeSet.B;
                    continue;
                }

                if (set == Code128CodeSet.C && digits < 2)
                {
                    bool intoA = this.nextOnlyInA[position] < this.nextOnlyInB[position];
                    this.values[this.written++] = intoA ? LatchA : LatchB;
                    set = intoA ? Code128CodeSet.A : Code128CodeSet.B;
                    continue;
                }

                if (set == Code128CodeSet.C)
                {
                    this.values[this.written++] = ((text[position] - '0') * 10) + (text[position + 1] - '0');
                    position += 2;
                    continue;
                }

                if (functionFour)
                {
                    this.values[this.written++] = FunctionFourIn(set);
                }

                this.values[this.written++] = ValueIn(set, character);
                position++;
            }

            // Section 7.4 of ISO/IEC 24723: a linked symbol ends its data with an extra code set character, which Table 8 chooses from the active code set.
            if (this.linkage != Code128Linkage.None)
            {
                bool ccC = this.linkage == Code128Linkage.CcC;
                this.values[this.written++] = set switch
                {
                    Code128CodeSet.A => ccC ? LatchC : LatchB,
                    Code128CodeSet.B => ccC ? LatchA : LatchC,
                    _ => ccC ? LatchB : LatchA,
                };
            }

            this.values[this.written] = ComputeCheckCharacter(this.values[..this.written]);
            this.written++;
            this.values[this.written++] = Stop;
            return this.written;
        }

        /// <summary>
        /// Fills the look-ahead tables in one backward walk. The distance to the next character that only one of code sets A and B can carry decides which set
        /// to start in and which to latch back to. The walk runs backwards, so the distances have to be held; everything else the encoder needs is one step
        /// ahead of the write. Both distances saturate at the byte maximum, which is further than any comparison can reach.
        /// </summary>
        private readonly void MeasureLookahead()
        {
            ReadOnlySpan<char> text = this.text;
            Span<byte> nextOnlyInA = this.nextOnlyInA;
            Span<byte> nextOnlyInB = this.nextOnlyInB;
            Span<byte> digitsFromEven = this.digitsFromEven;
            Span<byte> extendedRun = this.extendedRun;
            nextOnlyInA[text.Length] = byte.MaxValue;
            nextOnlyInB[text.Length] = byte.MaxValue;
            digitsFromEven[text.Length] = 0;
            extendedRun[text.Length] = 0;

            // The same digit count one character out of step is read only at the position after, so the walk carries it.
            byte digitsFromOdd = 0;
            for (int i = text.Length - 1; i >= 0; i--)
            {
                // A separator encodes as a Function 1 character, which every code set carries, so it never forces a set of its own.
                bool separator = this.gs1 && text[i] == Gs1Data.Separator;
                nextOnlyInA[i] = !separator && OnlyInSetA(text[i]) ? (byte)0 : SaturatingIncrement(nextOnlyInA[i + 1]);
                nextOnlyInB[i] = !separator && OnlyInSetB(text[i]) ? (byte)0 : SaturatingIncrement(nextOnlyInB[i + 1]);
                extendedRun[i] = text[i] > '\u007F' ? SaturatingIncrement(extendedRun[i + 1]) : (byte)0;

                // The digits a code set C run carries from here, counted on the two parities code set C packs against. A separator carries two, because
                // code set C encodes the Function 1 in the one symbol character that a digit pair takes, so a run of digits continues across it.
                if (separator)
                {
                    digitsFromEven[i] = SaturatingAdd(digitsFromEven[i + 1], 2);
                    digitsFromOdd = 0;
                }
                else if (char.IsAsciiDigit(text[i]))
                {
                    digitsFromEven[i] = SaturatingAdd(digitsFromOdd, 1);
                    digitsFromOdd = SaturatingAdd(digitsFromEven[i + 1], 1);
                }
                else
                {
                    digitsFromEven[i] = 0;
                    digitsFromOdd = 0;
                }
            }
        }

        /// <summary>
        /// Chooses the code set the symbol starts in and writes its start character.
        /// </summary>
        /// <returns>The code set the symbol starts in.</returns>
        private Code128CodeSet ChooseStartSet()
        {
            // Step 1 of section 5.4.7.6 begins a GS1 symbol with start character C and a Function 1 character. An Application Identifier opens every such
            // symbol with two or more digits, so the pair code set C packs pays for the switch out of it, and the symbol is never longer for it.
            if (this.gs1)
            {
                this.values[this.written++] = StartC;
                return Code128CodeSet.C;
            }

            if (this.text.Length == 0)
            {
                this.values[this.written++] = StartB;
                return Code128CodeSet.B;
            }

            int digits = this.digitsFromEven[0];
            if ((this.text.Length == 2 && digits == 2) || digits >= 4)
            {
                this.values[this.written++] = StartC;
                return Code128CodeSet.C;
            }

            if (this.nextOnlyInA[0] < this.nextOnlyInB[0])
            {
                this.values[this.written++] = StartA;
                return Code128CodeSet.A;
            }

            this.values[this.written++] = StartB;
            return Code128CodeSet.B;
        }
    }
}
