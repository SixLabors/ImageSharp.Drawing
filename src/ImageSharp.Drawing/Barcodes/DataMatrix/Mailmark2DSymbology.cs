// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// The Royal Mail 2D Mailmark barcode of the Mailmark barcode definition document: a Data Matrix ECC 200 symbol of 24 x 24, 32 x 32 or 16 x 48 modules. The
/// text is the 45 characters of the Royal Mail fields of Table 2, each of fixed length and padded with spaces, followed by optional customer content. The Royal
/// Mail fields are the UPU country ID "JGB ", the information type ID, the version ID, the class, the supply chain ID of 7 digits, the item ID of 8 digits, the
/// destination post code plus DPS of 9 characters, the service type, the return to sender post code of 7 characters and 6 reserved spaces. They encode in C40
/// encodation from the start of the data.
/// </summary>
public sealed class Mailmark2DSymbology : BarcodeSymbology
{
    /// <summary>
    /// The number of characters of the Royal Mail fields, which encode in C40 encodation.
    /// </summary>
    private const int RoyalMailLength = 45;

    /// <summary>
    /// The clear zone around the symbol in modules, section 2.4: "A clear zone of at least 4 times the module size should appear around 2D Mailmark barcodes."
    /// </summary>
    private const int ClearZone = 4;

    /// <summary>
    /// The X dimension in millimetres a symbol takes when the caller sets none: 0.5 mm, the module size section 2.4 recommends from the range of 0.5 mm to 0.7
    /// mm.
    /// </summary>
    private const double NominalXDimension = 0.5;

    /// <summary>
    /// Initializes a new instance of the <see cref="Mailmark2DSymbology"/> class.
    /// </summary>
    /// <param name="type">The barcode type, which sets the symbol size.</param>
    public Mailmark2DSymbology(Mailmark2DType type)
        => this.Type = type;

    /// <summary>
    /// Gets the barcode type.
    /// </summary>
    public Mailmark2DType Type { get; }

    /// <inheritdoc/>
    internal override void Encode(string text, BarcodeOptions options, ref BarcodeSymbol symbol)
    {
        Guard.NotNull(text, nameof(text));

        if (text.Length < RoyalMailLength)
        {
            throw new ArgumentException(
                $"A 2D Mailmark barcode starts with the {RoyalMailLength} characters of the Royal Mail fields, space padded; got {text.Length} characters.",
                nameof(text));
        }

        ReadOnlySpan<char> fields = text.AsSpan(0, RoyalMailLength);

        // Section 2.1: every Royal Mail field uses the C40 basic set, the capital letters, the digits and space.
        for (int i = 0; i < fields.Length; i++)
        {
            char c = fields[i];
            if (c is not (' ' or (>= '0' and <= '9') or (>= 'A' and <= 'Z')))
            {
                throw new ArgumentException(
                    $"The Royal Mail fields carry only capital letters, digits and spaces; got '{c}' at position {i + 1}.",
                    nameof(text));
            }
        }

        // Table 2 gives each field a fixed start position and length, so each check reads its slice: positions 1 to 4 the UPU country ID, 5 the information
        // type ID, 6 the version ID, 7 the class, 8 to 14 the supply chain ID and 15 to 22 the item ID. The information type ID and the class take values
        // listed in Table 2 and are never empty. The version ID is "Currently "1"".
        if (!fields.StartsWith("JGB "))
        {
            throw new ArgumentException("A 2D Mailmark barcode starts with the UPU country ID \"JGB \".", nameof(text));
        }

        if (fields[4] == ' ' || fields[6] == ' ')
        {
            throw new ArgumentException("The information type ID and the class of a 2D Mailmark barcode are not spaces.", nameof(text));
        }

        if (fields[5] != '1')
        {
            throw new ArgumentException($"The version ID of a 2D Mailmark barcode is \"1\"; got '{fields[5]}'.", nameof(text));
        }

        if (!IsDigits(fields.Slice(7, 7)) || !IsDigits(fields.Slice(14, 8)))
        {
            throw new ArgumentException("The supply chain ID is 7 digits and the item ID is 8 digits.", nameof(text));
        }

        // Appendix A, Table 14: the destination post code plus DPS, positions 23 to 31, is an outward code alone or with the inward code and the delivery
        // point, or spaces. The return to sender post code, positions 33 to 39, is the outward and inward codes, or spaces.
        ReadOnlySpan<char> destination = fields.Slice(22, 9);
        if (!IsSpaces(destination) && !IsPostcode(destination, true))
        {
            throw new ArgumentException($"The destination post code plus DPS is not a format of Appendix A; got \"{destination}\".", nameof(text));
        }

        // Table 2: the service type at position 32 is one of the listed values "0" to "6".
        if (fields[31] is < '0' or > '6')
        {
            throw new ArgumentException($"The service type of a 2D Mailmark barcode is \"0\" to \"6\"; got '{fields[31]}'.", nameof(text));
        }

        ReadOnlySpan<char> returnToSender = fields.Slice(32, 7);
        if (!IsSpaces(returnToSender) && !IsPostcode(returnToSender, false))
        {
            throw new ArgumentException($"The return to sender post code is not a format of Appendix A; got \"{returnToSender}\".", nameof(text));
        }

        // Table 2: positions 40 to 45 "Must be filled with <space> characters until further notice."
        if (!IsSpaces(fields.Slice(39, 6)))
        {
            throw new ArgumentException("The field reserved by Royal Mail is six spaces.", nameof(text));
        }

        // Section 2 fixes the symbol size of each type. The customer content after the Royal Mail fields takes any encodation, and a fixed size makes the
        // encoder reject content that does not fit.
        (int rows, int columns) = this.Type switch
        {
            Mailmark2DType.Type7 => (24, 24),
            Mailmark2DType.Type9 => (32, 32),
            _ => (16, 48),
        };

        DataMatrixEncoder.BuildSymbol(
            text,
            false,
            DataMatrixFormat.SquareOrRectangular,
            rows,
            columns,
            RoyalMailLength,
            ClearZone,
            options.XDimension ?? NominalXDimension,
            ref symbol);
    }

    /// <summary>
    /// Returns whether every character is a digit.
    /// </summary>
    /// <param name="field">The field.</param>
    /// <returns><see langword="true"/> when the field is all digits.</returns>
    private static bool IsDigits(ReadOnlySpan<char> field) => !field.ContainsAnyExceptInRange('0', '9');

    /// <summary>
    /// Returns whether every character is a space.
    /// </summary>
    /// <param name="field">The field.</param>
    /// <returns><see langword="true"/> when the field is all spaces.</returns>
    private static bool IsSpaces(ReadOnlySpan<char> field) => !field.ContainsAnyExcept(' ');

    /// <summary>
    /// Returns whether a post code field holds a format of Appendix A, Table 14, padded with spaces. The outward code is AN, AAN, ANN, AANN, ANA or AANA, the
    /// inward code NAA and the delivery point NA, where A is a capital letter and N a digit.
    /// </summary>
    /// <param name="field">The field.</param>
    /// <param name="destination"> Whether the field is the destination post code plus DPS, which may hold the outward code alone and holds the delivery point
    /// after the inward code, rather than the return to sender post code. </param>
    /// <returns><see langword="true"/> when the field holds a valid format.</returns>
    private static bool IsPostcode(ReadOnlySpan<char> field, bool destination)
    {
        ReadOnlySpan<char> code = field.TrimEnd(' ');
        if (destination && code.Length <= 4)
        {
            return IsOutward(code);
        }

        // The inward code and, in the destination field, the delivery point follow the outward code.
        int tail = destination ? 5 : 3;
        if (code.Length <= tail)
        {
            return false;
        }

        ReadOnlySpan<char> inward = code[^tail..];
        return IsOutward(code[..^tail])
            && char.IsAsciiDigit(inward[0])
            && char.IsAsciiLetterUpper(inward[1])
            && char.IsAsciiLetterUpper(inward[2])
            && (!destination || (char.IsAsciiDigit(inward[3]) && char.IsAsciiLetterUpper(inward[4])));
    }

    /// <summary>
    /// Returns whether the characters are an outward code of Appendix A, Table 14: AN, AAN, ANN, AANN, ANA or AANA.
    /// </summary>
    /// <param name="code">The characters.</param>
    /// <returns><see langword="true"/> when the characters are an outward code.</returns>
    private static bool IsOutward(ReadOnlySpan<char> code)
    {
        // Every outward code starts with a letter, has one or two letters before its first digit, and ends with that digit followed by one more digit or one
        // more letter at most.
        if (code.Length is < 2 or > 4 || !char.IsAsciiLetterUpper(code[0]))
        {
            return false;
        }

        int letters = char.IsAsciiLetterUpper(code[1]) ? 2 : 1;
        if (letters >= code.Length || !char.IsAsciiDigit(code[letters]))
        {
            return false;
        }

        int rest = code.Length - letters - 1;
        return rest == 0 || (rest == 1 && (char.IsAsciiDigit(code[^1]) || char.IsAsciiLetterUpper(code[^1])));
    }
}
