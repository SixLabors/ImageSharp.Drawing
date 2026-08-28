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
