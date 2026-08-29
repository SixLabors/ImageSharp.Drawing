// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.FontGenerator;

/// <summary>
/// One font design fed through the generator: the glyph skeletons, their stroke styling, the naming
/// and the acceptance expectations.
/// </summary>
internal sealed class FontDesign
{
    /// <summary>
    /// Gets the short name that prefixes every output file, for example OcrA for OcrA.ttf,
    /// OcrAFontData.cs and OcrA-proof.png.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Gets the font family name written to the name table.
    /// </summary>
    public required string FamilyName { get; init; }

    /// <summary>
    /// Gets the sentence naming this design's source standard, written into the generated data
    /// file's XML documentation.
    /// </summary>
    public required string DataSummary { get; init; }

    /// <summary>
    /// Gets the design units per em.
    /// </summary>
    public required ushort UnitsPerEm { get; init; }

    /// <summary>
    /// Gets the nominal centerline height H in design units.
    /// </summary>
    public required float H { get; init; }

    /// <summary>
    /// Gets the nominal centerline width W in design units.
    /// </summary>
    public required float W { get; init; }

    /// <summary>
    /// Gets the descender centerline depth in design units, negative below the baseline.
    /// </summary>
    public required float Descender { get; init; }

    /// <summary>
    /// Gets the default stroke width T in design units.
    /// </summary>
    public required float DefaultStrokeWidth { get; init; }

    /// <summary>
    /// Gets the ink height of a small letter above the baseline, in design units, as the standard
    /// dimensions the nominal printed image.
    /// </summary>
    public required float SmallLetterHeight { get; init; }

    /// <summary>
    /// Gets the ink height of a capital letter and of a digit above the baseline, in design units.
    /// </summary>
    public required float CapitalHeight { get; init; }

    /// <summary>
    /// Gets the ink height of an ascender above the baseline, in design units.
    /// </summary>
    public required float AscenderHeight { get; init; }

    /// <summary>
    /// Gets the ink depth of a descender below the baseline, in design units, as a positive distance.
    /// </summary>
    public required float DescenderDepth { get; init; }

    /// <summary>
    /// Gets the characters the standard dimensions on their own rather than on a nominal line. Those
    /// keep the size they are drawn at, because normalizing them onto a line they were never meant to
    /// touch would change a shape the standard states.
    /// </summary>
    public required string NormalizationExceptions { get; init; }

    /// <summary>
    /// Gets the factor that maps the design grid onto the em. The grid carries the 0.1 inch character
    /// pitch, which makes the ink far taller per em than any font convention, so the same point size
    /// would render far larger glyphs than an established digitization of the same standard. The factor
    /// lands the capital ink at the height those digitizations use, so a point size means the same
    /// thing across them.
    /// </summary>
    public required float EmScale { get; init; }

    /// <summary>
    /// Gets the ink height a small letter is drawn at, in design units, before normalization.
    /// </summary>
    public required float DrawnSmallLetterHeight { get; init; }

    /// <summary>
    /// Gets the ink height an ascender is drawn at, in design units, before normalization.
    /// </summary>
    public required float DrawnAscenderHeight { get; init; }

    /// <summary>
    /// Gets the ink depth a descender is drawn at, in design units, as a positive distance, before
    /// normalization.
    /// </summary>
    public required float DrawnDescenderDepth { get; init; }

    /// <summary>
    /// Gets the ink height a capital is drawn at, in design units, before normalization. A glyph whose
    /// drawn ink reaches this line is a full height glyph and is normalized onto
    /// <see cref="CapitalHeight"/>. One that does not reach it, such as a hyphen or a comma, keeps its
    /// place against the capitals instead.
    /// </summary>
    public required float DrawnCapitalHeight { get; init; }

    /// <summary>
    /// Gets a value indicating whether open stroke ends project half a stroke width along their
    /// tangent and cut flat, as the printed OCR-B terminals do, instead of ending in the round pen
    /// sweep.
    /// </summary>
    public required bool CutTerminals { get; init; }

    /// <summary>
    /// Gets the glyph skeletons keyed by character, in the stroke token format that
    /// <c>BuildStroke</c> in Program.cs documents.
    /// </summary>
    public required IReadOnlyDictionary<char, float[][]> Skeletons { get; init; }

    /// <summary>
    /// Gets, per character, the indices of strokes drawn with projecting square caps.
    /// </summary>
    public required IReadOnlyDictionary<char, int[]> SquareStrokes { get; init; }

    /// <summary>
    /// Gets, per character, the indices of strokes cut off exactly at their endpoints.
    /// </summary>
    public required IReadOnlyDictionary<char, int[]> ButtStrokes { get; init; }

    /// <summary>
    /// Gets, per character, the indices of strokes drawn with butt ends and sharp miter joins.
    /// </summary>
    public required IReadOnlyDictionary<char, int[]> MiterStrokes { get; init; }

    /// <summary>
    /// Gets, per character, the indices of strokes drawn with round caps and sharp miter joins.
    /// </summary>
    public required IReadOnlyDictionary<char, int[]> MiterRoundStrokes { get; init; }

    /// <summary>
    /// Gets per-character stroke width overrides for characters whose drawings use a thickness other
    /// than <see cref="DefaultStrokeWidth"/> throughout, such as the OCR-B small letters.
    /// </summary>
    public required IReadOnlyDictionary<char, float> StrokeWidths { get; init; }

    /// <summary>
    /// Gets per-stroke width overrides keyed by character and stroke index. The value zero marks a
    /// stroke that is already a filled ink polygon rather than a centerline to stroke.
    /// </summary>
    public required IReadOnlyDictionary<(char Character, int Stroke), float> StrokeWidthOverrides { get; init; }

    /// <summary>
    /// Gets the per-glyph acceptance expectations that <see cref="SpecChecks.Verify"/> enforces.
    /// </summary>
    public required IReadOnlyDictionary<char, (float[] Bounds, float[][] Inked, float[][] Blank)> Expectations { get; init; }
}
