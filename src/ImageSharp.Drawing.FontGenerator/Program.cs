// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

// Generates the SixLabors OCR fonts from the OCR standards themselves: OCR-A from the dimensioned
// character drawings of FIPS PUB 32, OCR-B from the character sheet and dimension tables of ECMA-11.
// Each design's glyph skeletons are stroked with the library's own path stroker, the outlines are
// verified against the independently transcribed SpecChecks expectations, and <Name>.ttf,
// <Name>FontData.cs plus proof, grid and comparison sheets are written into the output directory.
// See specs/README.md for the source documents.
using System.Globalization;
using System.Numerics;
using System.Text;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.FontGenerator;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

if (args.Length == 0)
{
    Console.WriteLine("Usage: ImageSharp.Drawing.FontGenerator <output-directory> [--ref <font-path>] [--refb <font-path>] [<characters>...]");
    Console.WriteLine("  --ref   renders a side-by-side comparison sheet of the OCR-A design against the given font");
    Console.WriteLine("  --refb  renders the same sheet for the OCR-B design");
    Console.WriteLine("  Remaining arguments render full-size single glyph inspection images per design.");
    return 1;
}

string outputDirectory = args[0];
string? referencePath = null;
string? referenceBPath = null;
List<string> inspectTexts = [];
for (int argIndex = 1; argIndex < args.Length; argIndex++)
{
    if (args[argIndex] == "--ref" && argIndex + 1 < args.Length)
    {
        referencePath = args[++argIndex];
    }
    else if (args[argIndex] == "--refb" && argIndex + 1 < args.Length)
    {
        referenceBPath = args[++argIndex];
    }
    else
    {
        inspectTexts.Add(args[argIndex]);
    }
}

Directory.CreateDirectory(outputDirectory);

StrokeOptions roundStroke = new()
{
    LineJoin = LineJoin.Round,
    LineCap = LineCap.Round,
};

StrokeOptions squareStroke = new()
{
    LineJoin = LineJoin.Round,
    LineCap = LineCap.Square,
};

StrokeOptions buttStroke = new()
{
    LineJoin = LineJoin.Miter,
    LineCap = LineCap.Butt,
    MiterLimit = 1.5D,
};

StrokeOptions miterSquareStroke = new()
{
    LineJoin = LineJoin.Miter,
    LineCap = LineCap.Square,
    MiterLimit = 1.5D,
};

StrokeOptions miterStroke = new()
{
    LineJoin = LineJoin.Miter,
    LineCap = LineCap.Butt,
};

StrokeOptions miterRoundStroke = new()
{
    LineJoin = LineJoin.Miter,
    LineCap = LineCap.Round,
};

FontDesign[] designs = [OcrAGlyphs.Design, OcrBGlyphs.Design];
FontCollection collection = new();
Dictionary<string, FontFamily> families = [];
foreach (FontDesign design in designs)
{
    if (design.Skeletons.Count == 0)
    {
        Console.WriteLine($"{design.Name}: no glyphs yet, skipped");
        continue;
    }

    float emScale = design.EmScale;
    ushort advanceWidth = (ushort)MathF.Round(design.UnitsPerEm * emScale);

    // The sheet the skeletons are traced from fixes shape but not size, so every glyph is normalized
    // onto the height the standard gives its class. The scale is the glyph's own drawn bounds against
    // the bounds the standard calls for, and it applies to both axes, so the glyph lands on the line
    // and keeps every ratio it was drawn with. Each glyph carries its own scale, so a line of capitals,
    // digits and full height marks is one height throughout.
    float NormalizedScale(char character, float top, float bottom)
    {
        if (design.NormalizationExceptions.Contains(character, StringComparison.Ordinal))
        {
            return 1F;
        }

        // A letter carries its category's scale, so a letter that reaches past its line, an accented one
        // for example, keeps the mark in proportion with the body. A digit and a mark carry their own,
        // so a line of digits and full height marks is one height throughout. A mark that never reaches
        // the capital line, a hyphen or a comma, is not a full height glyph: normalizing it onto that
        // line would enlarge it out of all proportion, so it carries the scale of the capitals.
        switch (Classify(character))
        {
            case GlyphClass.Absolute:
                return 1F;
            case GlyphClass.Digit:
                return design.CapitalHeight / top;
            case GlyphClass.Ascender:
                return design.AscenderHeight / design.DrawnAscenderHeight;
            case GlyphClass.SmallLetter:
                return design.SmallLetterHeight / design.DrawnSmallLetterHeight;
            case GlyphClass.Descender:
                return design.DescenderDepth / design.DrawnDescenderDepth;
            default:
                float capitalScale = design.CapitalHeight / design.DrawnCapitalHeight;
                return char.IsLetter(character) || top < design.DrawnCapitalHeight
                    ? capitalScale
                    : design.CapitalHeight / top;
        }
    }

    // Ink projects a half stroke beyond the centerline box at every terminal, dots rise above it and
    // descenders drop below it, so the vertical metrics cover those extremes. Text layout grows the
    // line box for any glyph whose ink passes the ascender, which shifts that glyph off the shared
    // baseline, so the metrics are measured from the geometry rather than assumed from the box.
    float inkTop = 0;
    float inkBottom = 0;

    // The cap height is the ink height of a capital, which text layout uses to set a line of capitals
    // or digits against a reference line.
    float capHeight = design.CapitalHeight;

    float sideBearing = (design.UnitsPerEm - design.W) / 2;
    Dictionary<char, List<IReadOnlyList<Vector2>>> auditOutlines = [];
    Dictionary<char, List<IReadOnlyList<(short X, short Y)>>> quantizedGlyphs = [];
    Dictionary<char, float> glyphScales = [];
    List<string> specFailures = [];
    foreach ((char character, float[][] strokes) in design.Skeletons.OrderBy(entry => entry.Key))
    {
        List<IReadOnlyList<Vector2>> designContours = [];
        design.SquareStrokes.TryGetValue(character, out int[]? squareIndices);
        design.ButtStrokes.TryGetValue(character, out int[]? buttIndices);
        design.MiterStrokes.TryGetValue(character, out int[]? miterIndices);
        design.MiterRoundStrokes.TryGetValue(character, out int[]? miterRoundIndices);
        float characterWidth = design.StrokeWidths.TryGetValue(character, out float perCharacter)
            ? perCharacter
            : design.DefaultStrokeWidth;

        // A skeleton may record one outline as a run of single-segment strokes that share endpoints.
        // Under cut terminals every seam would project two square caps, so such runs merge into one
        // figure. Glyphs with per-stroke styling or width overrides keep their stroke list untouched
        // so the recorded stroke indices stay valid.
        float[][] strokeSet = strokes;
        if (design.CutTerminals
            && squareIndices is null
            && buttIndices is null
            && miterIndices is null
            && miterRoundIndices is null
            && !design.StrokeWidthOverrides.Keys.Any(key => key.Character == character))
        {
            List<float[]> chained = [];
            foreach (float[] stroke in strokes)
            {
                float[]? previous = chained.Count > 0 ? chained[^1] : null;
                if (previous is not null && previous[^2] == stroke[0] && previous[^1] == stroke[1])
                {
                    float[] run = new float[previous.Length + stroke.Length - 2];

                    previous.CopyTo(run, 0);
                    stroke.AsSpan(2).CopyTo(run.AsSpan(previous.Length));
                    chained[^1] = run;
                }
                else
                {
                    chained.Add(stroke);
                }
            }

            strokeSet = [.. chained];
        }

        // Union all strokes into one outline so the glyph carries no overlapping contours: coincident
        // edges from overlaid strokes otherwise double-cover antialiased pixels at render time.
        List<IPath> centerlines = [];
        List<float> strokeWidths = [];
        List<StrokeOptions> strokeStyles = [];
        List<bool> cutFlags = [];
        for (int strokeIndex = 0; strokeIndex < strokeSet.Length; strokeIndex++)
        {
            float[] tokens = strokeSet[strokeIndex];
            PathBuilder builder = new();
            builder.StartFigure();
            BuildStroke(builder, tokens);

            // A stroke that returns to its start point is a loop: close it so the seam takes a join
            // rather than two caps.
            if (tokens[0] == tokens[^2] && tokens[1] == tokens[^1])
            {
                builder.CloseFigure();
            }

            StrokeOptions options = design.CutTerminals ? buttStroke : roundStroke;
            bool cutEnds = design.CutTerminals;
            if (squareIndices is not null && Array.IndexOf(squareIndices, strokeIndex) >= 0)
            {
                options = squareStroke;
                cutEnds = false;
            }
            else if (buttIndices is not null && Array.IndexOf(buttIndices, strokeIndex) >= 0)
            {
                options = buttStroke;
                cutEnds = false;
            }
            else if (miterIndices is not null && Array.IndexOf(miterIndices, strokeIndex) >= 0)
            {
                options = miterStroke;
                cutEnds = false;
            }
            else if (miterRoundIndices is not null && Array.IndexOf(miterRoundIndices, strokeIndex) >= 0)
            {
                options = miterRoundStroke;
                cutEnds = false;
            }

            float effectiveWidth = design.StrokeWidthOverrides.TryGetValue((character, strokeIndex), out float overrideWidth)
                ? overrideWidth
                : characterWidth;

            // A sub-quantum width offset keeps stroke outlines from sharing exactly coincident
            // collinear edges, which the polygon clipper cannot union reliably; the deviation is far
            // below the design grid and vanishes in quantization. Width zero marks a pre-computed
            // filled ink polygon, carried through as drawn.
            if (effectiveWidth != 0)
            {
                effectiveWidth += strokeIndex * 0.02f;
            }

            centerlines.Add(builder.Build());
            strokeWidths.Add(effectiveWidth);
            strokeStyles.Add(options);
            cutFlags.Add(cutEnds);
        }

        // Butt-capped ink for every stroke, the probe geometry that tells a buried junction end from
        // a true terminal.
        IPath[] probeInk = new IPath[centerlines.Count];
        if (design.CutTerminals)
        {
            for (int strokeIndex = 0; strokeIndex < centerlines.Count; strokeIndex++)
            {
                probeInk[strokeIndex] = strokeWidths[strokeIndex] == 0
                    ? centerlines[strokeIndex]
                    : centerlines[strokeIndex].GenerateOutline(strokeWidths[strokeIndex], buttStroke);
            }
        }

        // Open-stroke endpoints, indexed by stroke, for junction detection at shared points.
        List<(int Stroke, Vector2 Point)> strokeEndpoints = [];
        for (int strokeIndex = 0; strokeIndex < centerlines.Count; strokeIndex++)
        {
            ISimplePath[] flat = [.. centerlines[strokeIndex].Flatten()];
            if (flat.Length == 1 && !flat[0].IsClosed && strokeWidths[strokeIndex] != 0)
            {
                ReadOnlySpan<PointF> pp = flat[0].Points.Span;

                strokeEndpoints.Add((strokeIndex, pp[0]));
                strokeEndpoints.Add((strokeIndex, pp[^1]));
            }
        }

        List<IPath> outlines = [];
        for (int strokeIndex = 0; strokeIndex < centerlines.Count; strokeIndex++)
        {
            float effectiveWidth = strokeWidths[strokeIndex];
            if (effectiveWidth == 0)
            {
                outlines.Add(centerlines[strokeIndex]);
                continue;
            }

            ISimplePath[] flattened = [.. centerlines[strokeIndex].Flatten()];
            if (!cutFlags[strokeIndex] || flattened.Length != 1 || flattened[0].IsClosed)
            {
                StrokeOptions closedOptions = cutFlags[strokeIndex] ? miterSquareStroke : strokeStyles[strokeIndex];

                outlines.Add(centerlines[strokeIndex].GenerateOutline(effectiveWidth, closedOptions));
                continue;
            }

            CutTerminalStroke(flattened[0].Points.ToArray(), centerlines[strokeIndex], effectiveWidth, strokeIndex, probeInk, strokeEndpoints, buttStroke, outlines);
        }

        // TrueType fills with the nonzero winding rule, so overlapping same-winding contours render
        // identically to their union; the polygon clipper drops pieces from many-input unions, so the
        // cut-terminal design writes its overlapping contours directly.
        bool allInk = strokeWidths.TrueForAll(width => width == 0);
        IPath merged = outlines.Count == 1
            ? outlines[0]
            : design.CutTerminals || allInk
                ? new ComplexPolygon([.. outlines])
                : outlines[0].Clip(BooleanOperation.Union, outlines.Skip(1));

        List<IReadOnlyList<(short X, short Y)>> contours = [];
        foreach (ISimplePath simple in merged.Flatten())
        {
            ReadOnlySpan<PointF> points = simple.Points.Span;
            Vector2[] dense = new Vector2[points.Length];
            for (int i = 0; i < points.Length; i++)
            {
                dense[i] = new Vector2(points[i].X, points[i].Y);
            }

            // The stroker flattens curves densely; most of those points are collinear within a
            // fraction of a design unit and would bloat the font. 0.6 units is 1.5 microns of print.
            designContours.Add(SimplifyContour(dense, 0.6f));
        }

        // Both axes take one scale, about the baseline and the centre of the character cell, so the
        // glyph lands on the height its category stands at and keeps the shape it was drawn with.
        float drawnTop = 0;
        float drawnBottom = 0;
        foreach (IReadOnlyList<Vector2> drawnContour in designContours)
        {
            foreach (Vector2 point in drawnContour)
            {
                drawnTop = MathF.Max(drawnTop, point.Y);
                drawnBottom = MathF.Min(drawnBottom, point.Y);
            }
        }

        float glyphScale = NormalizedScale(character, drawnTop, drawnBottom);
        glyphScales[character] = glyphScale;
        float cellCentre = design.W / 2;
        foreach (Vector2[] designPoints in designContours.Cast<Vector2[]>())
        {
            for (int i = 0; i < designPoints.Length; i++)
            {
                Vector2 point = designPoints[i];
                designPoints[i] = new Vector2(cellCentre + ((point.X - cellCentre) * glyphScale), point.Y * glyphScale);
                inkTop = MathF.Max(inkTop, designPoints[i].Y);
                inkBottom = MathF.Min(inkBottom, designPoints[i].Y);
            }

            List<(short X, short Y)> contour = new(designPoints.Length);
            foreach (Vector2 point in designPoints)
            {
                (short X, short Y) quantized = ((short)MathF.Round((point.X + sideBearing) * emScale), (short)MathF.Round(point.Y * emScale));
                if (contour.Count == 0 || contour[^1] != quantized)
                {
                    contour.Add(quantized);
                }
            }

            if (contour.Count > 2 && contour[0] == contour[^1])
            {
                contour.RemoveAt(contour.Count - 1);
            }

            if (contour.Count > 2)
            {
                contours.Add(contour);
            }
        }

        quantizedGlyphs[character] = contours;
        auditOutlines[character] = designContours;
        SpecChecks.Verify(character, designContours, design.Expectations, specFailures);
    }

    // Text layout centers the em box inside the declared line height, so a glyph cell reaches
    // ascender - (lineHeight - unitsPerEm) / 2 above the baseline. Ink above that line makes the
    // layout lower that one glyph to keep it inside the cell, which breaks the shared baseline.
    // Solving for a zero shift with a zero line gap gives the ascender below. This design needs it
    // because the em carries the 0.1 inch pitch while the ink stands taller than the pitch.
    float inkTopUnits = inkTop * emScale;
    short descender = (short)MathF.Round(inkBottom * emScale);
    short ascender = (short)MathF.Round(MathF.Max(inkTopUnits, (2 * inkTopUnits) - descender - design.UnitsPerEm));
    TrueTypeWriter writer = new(
        design.UnitsPerEm,
        ascender,
        descender,
        (short)MathF.Round(capHeight * emScale),
        design.FamilyName,
        "Copyright (c) Six Labors. Licensed under the Six Labors Split License.");

    writer.AddGlyph(' ', [], advanceWidth);
    foreach ((char glyphCharacter, List<IReadOnlyList<(short X, short Y)>> glyphContours) in quantizedGlyphs.OrderBy(entry => entry.Key))
    {
        writer.AddGlyph(glyphCharacter, glyphContours, advanceWidth);
    }

    if (specFailures.Count > 0)
    {
        Console.WriteLine($"{design.Name} SPEC CHECK FAILED: {specFailures.Count} deviations");
        foreach (string failure in specFailures)
        {
            Console.WriteLine($"  {failure}");
        }
    }
    else
    {
        Console.WriteLine($"{design.Name} SPEC CHECK PASSED");
    }

    // Normalizing the glyphs moves every extreme, so the expectations that record accepted ink are
    // rewritten from the normalized outlines. The probe points are not rewritten: each was chosen by
    // eye to sit inside or outside a particular sweep, so it takes the same scale as the outline it
    // probes and keeps testing the same feature.
    StringBuilder expectationRows = new();
    foreach (char expectedCharacter in auditOutlines.Keys.OrderBy(character => character))
    {
        float expectedMinX = float.MaxValue;
        float expectedMaxX = float.MinValue;
        float expectedMinY = float.MaxValue;
        float expectedMaxY = float.MinValue;
        foreach (IReadOnlyList<Vector2> contour in auditOutlines[expectedCharacter])
        {
            foreach (Vector2 point in contour)
            {
                expectedMinX = MathF.Min(expectedMinX, point.X);
                expectedMaxX = MathF.Max(expectedMaxX, point.X);
                expectedMinY = MathF.Min(expectedMinY, point.Y);
                expectedMaxY = MathF.Max(expectedMaxY, point.Y);
            }
        }

        float expectedScale = glyphScales[expectedCharacter];
        float expectedCentre = design.W / 2;
        (float[] Bounds, float[][] Inked, float[][] Blank) old = design.Expectations[expectedCharacter];
        string Probes(float[][] points) => $"[{string.Join(", ", points.Select(point => $"[{Number(expectedCentre + ((point[0] - expectedCentre) * expectedScale))}, {Number(point[1] * expectedScale)}]"))}]";
        expectationRows.AppendLine(
            CultureInfo.InvariantCulture,
            $"            [{Literal(expectedCharacter)}] = ([{Number(expectedMinX)}, {Number(expectedMaxX)}, {Number(expectedMinY)}, {Number(expectedMaxY)}], {Probes(old.Inked)}, {Probes(old.Blank)}),");
    }

    string expectationPath = System.IO.Path.Combine(outputDirectory, $"{design.Name}-expectations.txt");
    File.WriteAllText(expectationPath, expectationRows.ToString());
    Console.WriteLine(expectationPath);

    static string Number(float value)
    {
        float rounded = MathF.Round(value, 1);
        return rounded == MathF.Round(rounded)
            ? ((int)rounded).ToString(CultureInfo.InvariantCulture)
            : rounded.ToString("0.0#", CultureInfo.InvariantCulture) + "f";
    }

    static string Literal(char value) => value switch
    {
        '\'' => @"'\''",
        '\\' => @"'\\'",
        _ => $"'{value}'",
    };

    static GlyphClass Classify(char value) => value switch
    {
        // ECMA-11 section 10 and FIPS PUB 32 section 2.6 dimension these in millimetres rather than
        // drawing them, so they carry their own absolute size.
        '―' or '∣' or '█' or '|' => GlyphClass.Absolute,
        >= '0' and <= '9' => GlyphClass.Digit,
        'b' or 'd' or 'f' or 'h' or 'i' or 'k' or 'l' or 't' => GlyphClass.Ascender,
        'g' or 'j' or 'p' or 'q' or 'y' => GlyphClass.Descender,
        _ when char.IsLower(value) => GlyphClass.SmallLetter,
        _ => GlyphClass.Capital,
    };

    byte[] font = writer.Write();
    string fontPath = System.IO.Path.Combine(outputDirectory, $"{design.Name}.ttf");
    File.WriteAllBytes(fontPath, font);
    Console.WriteLine($"{fontPath}: {font.Length} bytes");

    // Emit the font bytes as C# data for embedding in the library: the emitted file replaces
    // src/ImageSharp.Drawing/Barcodes/Fonts/<Name>FontData.cs whenever the design changes.
    StringBuilder data = new();
    data.AppendLine("// Copyright (c) Six Labors.");
    data.AppendLine("// Licensed under the Six Labors Split License.");
    data.AppendLine();
    data.AppendLine("// <auto-generated>");
    data.AppendLine("// Generated by ImageSharp.Drawing.FontGenerator. Do not edit.");
    data.AppendLine("// </auto-generated>");
    data.AppendLine();
    data.AppendLine("namespace SixLabors.ImageSharp.Drawing.Barcodes;");
    data.AppendLine();
    data.AppendLine("/// <summary>");
    data.AppendLine(CultureInfo.InvariantCulture, $"/// The {design.FamilyName} TrueType font file.");
    data.AppendLine(CultureInfo.InvariantCulture, $"/// {design.DataSummary}");
    data.AppendLine("/// </summary>");
    data.AppendLine(CultureInfo.InvariantCulture, $"internal static class {design.Name}FontData");
    data.AppendLine("{");
    data.AppendLine("    /// <summary>");
    data.AppendLine("    /// Gets the font file bytes.");
    data.AppendLine("    /// </summary>");
    data.AppendLine("    public static ReadOnlySpan<byte> Bytes =>");
    data.AppendLine("    [");
    for (int byteIndex = 0; byteIndex < font.Length; byteIndex += 16)
    {
        int lineEnd = Math.Min(byteIndex + 16, font.Length);
        data.Append("        ");
        for (int lineByte = byteIndex; lineByte < lineEnd; lineByte++)
        {
            data.Append("0x");
            data.Append(font[lineByte].ToString("X2", CultureInfo.InvariantCulture));
            data.Append(',');
            if (lineByte + 1 < lineEnd)
            {
                data.Append(' ');
            }
        }

        data.AppendLine();
    }

    data.AppendLine("    ];");
    data.AppendLine("}");
    string dataPath = System.IO.Path.Combine(outputDirectory, $"{design.Name}FontData.cs");
    File.WriteAllText(dataPath, data.ToString());
    Console.WriteLine(dataPath);

    // Proof sheet: load the generated font back and render the full set with the library itself.
    using MemoryStream stream = new(font);
    FontFamily family = collection.Add(stream);
    families[design.Name] = family;
    Font proofFont = family.CreateFont(100);

    string[] lines =
    [
        .. design.Skeletons.Keys
            .OrderBy(character => character)
            .Select((character, index) => (character, index))
            .GroupBy(entry => entry.index / 17)
            .Select(group => new string([.. group.Select(entry => entry.character)])),
    ];

    using Image<Rgba32> proof = new(1800, 20 + (lines.Length * 160), Color.White.ToPixel<Rgba32>());
    proof.Mutate(context => context.Paint(canvas =>
    {
        for (int i = 0; i < lines.Length; i++)
        {
            canvas.DrawText(
                new RichTextOptions(proofFont)
                {
                    Origin = new PointF(20, 20 + (i * 160)),
                },
                lines[i],
                Brushes.Solid(Color.Black),
                null);
        }
    }));

    string proofPath = System.IO.Path.Combine(outputDirectory, $"{design.Name}-proof.png");
    proof.SaveAsPng(proofPath);
    Console.WriteLine(proofPath);

    // Large per-glyph grid for auditing each outline against its source drawing, every cell labelled
    // with the character and code point it renders.
    Font gridFont = family.CreateFont(200);
    Font labelFont = SystemFonts.CreateFont("Arial", 18);
    char[] auditCharacters = [.. design.Skeletons.Keys.OrderBy(character => character)];
    int columns = 10;
    int rows = (auditCharacters.Length + columns - 1) / columns;

    using Image<Rgba32> grid = new(columns * 240, rows * 360, Color.White.ToPixel<Rgba32>());
    grid.Mutate(context => context.Paint(canvas =>
    {
        for (int index = 0; index < auditCharacters.Length; index++)
        {
            int column = index % columns;
            int row = index / columns;
            canvas.DrawText(
                new RichTextOptions(gridFont)
                {
                    Origin = new PointF((column * 240) + 20, (row * 360) + 56),
                },
                auditCharacters[index].ToString(),
                Brushes.Solid(Color.Black),
                null);
            canvas.DrawText(
                new RichTextOptions(labelFont)
                {
                    Origin = new PointF((column * 240) + 20, (row * 360) + 8),
                },
                $"U+{(int)auditCharacters[index]:X4} {auditCharacters[index]}",
                Brushes.Solid(Color.Gray),
                null);
        }
    }));

    string gridPath = System.IO.Path.Combine(outputDirectory, $"{design.Name}-grid.png");
    grid.SaveAsPng(gridPath);
    Console.WriteLine(gridPath);
}

// Reference comparison sheets: each glyph from a reference font on the left, ours on the right,
// baseline aligned so metric differences between the faces do not read as glyph offsets.
(string Name, string? Path)[] referenceJobs = [("OcrA", referencePath), ("OcrB", referenceBPath)];
foreach ((string designName, string? jobPath) in referenceJobs)
{
    if (jobPath is null || !families.TryGetValue(designName, out FontFamily compareFamily))
    {
        continue;
    }

    FontDesign design = designs.First(entry => entry.Name == designName);
    FontCollection referenceCollection = new();
    Font referenceFont = referenceCollection.Add(jobPath).CreateFont(200);
    Font compareFont = compareFamily.CreateFont(200);
    char[] compareCharacters = [.. design.Skeletons.Keys.OrderBy(c => c)];
    int pairColumns = 5;
    int pairRows = (compareCharacters.Length + pairColumns - 1) / pairColumns;
    using Image<Rgba32> pairs = new(pairColumns * 520, pairRows * 320, Color.White.ToPixel<Rgba32>());
    pairs.Mutate(context => context.Paint(canvas =>
    {
        float referenceAscent = canvas.MeasureText(new RichTextOptions(referenceFont), "0").LineMetrics[0].Ascender;
        float compareAscent = canvas.MeasureText(new RichTextOptions(compareFont), "0").LineMetrics[0].Ascender;
        float baselineY = MathF.Max(referenceAscent, compareAscent) + 30;
        for (int index = 0; index < compareCharacters.Length; index++)
        {
            int column = index % pairColumns;
            int row = index / pairColumns;
            string text = compareCharacters[index].ToString();
            canvas.DrawText(
                new RichTextOptions(referenceFont)
                {
                    Origin = new PointF((column * 520) + 20, (row * 320) + baselineY - referenceAscent),
                },
                text,
                Brushes.Solid(Color.ParseHex("007700")),
                null);
            canvas.DrawText(
                new RichTextOptions(compareFont)
                {
                    Origin = new PointF((column * 520) + 250, (row * 320) + baselineY - compareAscent),
                },
                text,
                Brushes.Solid(Color.Black),
                null);
        }
    }));

    string referenceStem = System.IO.Path.GetFileNameWithoutExtension(jobPath).ToLowerInvariant();
    string pairsPath = System.IO.Path.Combine(outputDirectory, $"{designName}-vs-{referenceStem}.png");
    pairs.SaveAsPng(pairsPath);
    Console.WriteLine(pairsPath);
}

// Full-size single glyph renders for inspection, requested as extra arguments.
foreach ((string designName, FontFamily inspectFamily) in families)
{
    Font inspectFont = inspectFamily.CreateFont(700);
    foreach (string text in inspectTexts)
    {
        using Image<Rgba32> single = new(900, 1100, Color.White.ToPixel<Rgba32>());
        single.Mutate(context => context.Paint(canvas => canvas.DrawText(
            new RichTextOptions(inspectFont)
            {
                Origin = new PointF(100, 100),
            },
            text,
            Brushes.Solid(Color.Black),
            null)));

        string singlePath = System.IO.Path.Combine(outputDirectory, $"inspect-{designName}-{(int)text[0]}.png");
        single.SaveAsPng(singlePath);
        Console.WriteLine(singlePath);
    }
}

return 0;

// Adds one stroke's ink to the piece list with its terminals cut on axis-aligned planes, the way the
// drawings print them: feet sit exactly on the baseline and diagonal arms end on vertical cuts. The
// stroke takes butt caps, then every open end whose projection is not buried inside another stroke
// gains a filled patch running from its butt face out to the cut plane, half a stroke width from the
// endpoint. A buried end stays butt so nothing pokes through the partner stroke. probeInk carries
// butt-capped ink for every stroke of the glyph, the geometry a terminal is tested against, and
// strokeEndpoints every open endpoint paired with its stroke index.
static void CutTerminalStroke(PointF[] points, IPath centerline, float width, int strokeIndex, IPath[] probeInk, List<(int Stroke, Vector2 Point)> strokeEndpoints, StrokeOptions buttStroke, List<IPath> pieces)
{
    float half = width / 2;
    IPath body = centerline.GenerateOutline(width, buttStroke);

    pieces.Add(body);

    // Nonzero fill cancels where windings oppose, so every patch must turn the same way as the
    // stroke outline it overlaps.
    float bodyWinding = 0;
    foreach (ISimplePath simple in body.Flatten())
    {
        float area = SignedArea(simple.Points.Span);
        if (MathF.Abs(area) > MathF.Abs(bodyWinding))
        {
            bodyWinding = area;
        }
    }

    Vector2[] pts = [.. points.Select(p => (Vector2)p)];
    List<(bool Horizontal, float Plane, Vector2 P, Vector2 Tangent)> cutsWanted = [];
    for (int end = 0; end < 2; end++)
    {
        Vector2 p = end == 0 ? pts[0] : pts[^1];
        Vector2 inner = p;
        for (int i = 1; i < pts.Length; i++)
        {
            inner = end == 0 ? pts[i] : pts[^(i + 1)];
            if ((inner - p).LengthSquared() > 0.01F)
            {
                break;
            }
        }

        Vector2 tangent = Vector2.Normalize(p - inner);
        Vector2 probe = p + (tangent * (half / 2));
        bool buried = false;
        for (int other = 0; other < probeInk.Length; other++)
        {
            if (other != strokeIndex && probeInk[other].Contains(probe, IntersectionRule.NonZero, Vector2.One))
            {
                buried = true;
                break;
            }
        }

        if (buried)
        {
            // Two butt faces meeting at a shared junction point leave an unfilled wedge, so such
            // ends run a short way past the point. An end buried in a partner's body stays exactly
            // butt: any overrun there pokes into the partner's counter.
            bool meetsAnotherEnd = false;
            foreach ((int otherStroke, Vector2 otherPoint) in strokeEndpoints)
            {
                if (otherStroke != strokeIndex && (otherPoint - p).Length() < half)
                {
                    meetsAnotherEnd = true;
                    break;
                }
            }

            if (meetsAnotherEnd)
            {
                Vector2 tucked = p + (tangent * (half * 0.6F));
                if (end == 0)
                {
                    pts[0] = tucked;
                }
                else
                {
                    pts[^1] = tucked;
                }
            }

            continue;
        }

        // Two open ends meeting tip-to-tip form a point, as in the curly bracket middles: both run
        // out along their tangents and the union forms the point, with no cut plane.
        bool meetsEnd = false;
        foreach ((int otherStroke, Vector2 otherPoint) in strokeEndpoints)
        {
            if (otherStroke != strokeIndex && (otherPoint - p).Length() < width)
            {
                meetsEnd = true;
                break;
            }
        }

        if (meetsEnd)
        {
            Vector2 pointward = p + (tangent * (half * 0.9F));
            if (end == 0)
            {
                pts[0] = pointward;
            }
            else
            {
                pts[^1] = pointward;
            }

            continue;
        }

        // A curved terminal ends on a face perpendicular to the stroke, extended half a stroke
        // width; only straight-segment terminals take the axis-aligned cuts of the drawings.
        Vector2 back = p;
        float walked = 0;
        int step = 1;
        while (step < pts.Length && walked < width * 1.2F)
        {
            Vector2 next = end == 0 ? pts[step] : pts[^(step + 1)];
            walked += (next - back).Length();
            back = next;
            step++;
        }

        Vector2 chord = Vector2.Normalize(p - back);
        bool curvedEnd = Vector2.Dot(chord, tangent) < 0.985F;
        if (curvedEnd)
        {
            Vector2 extended = p + (tangent * (half * 0.9F));
            if (end == 0)
            {
                pts[0] = extended;
            }
            else
            {
                pts[^1] = extended;
            }

            continue;
        }

        // The drawings cut a straight terminal on the reference line its ink abuts: the baseline,
        // the cap line or the x-height line for steep strokes, a vertical side plane otherwise.
        bool meetsLine = MathF.Abs(tangent.Y) >= 0.55F
            && (p.Y - half <= 35 || p.Y + half >= 905 || MathF.Abs(p.Y + half - 741) <= 40);
        bool horizontalCut = meetsLine || MathF.Abs(tangent.X) < 0.5F * MathF.Abs(tangent.Y);
        float plane = horizontalCut
            ? p.Y + (half * MathF.Sign(tangent.Y))
            : p.X + (half * MathF.Sign(tangent.X));
        cutsWanted.Add((horizontalCut, plane, p, tangent));
    }

    // Both arms of a chevron cut on the same axis toward the same side end flush on a single line
    // in the drawings, so their planes unify to the outermost one.
    if (cutsWanted.Count == 2 && cutsWanted[0].Horizontal == cutsWanted[1].Horizontal
        && MathF.Abs(cutsWanted[0].Plane - cutsWanted[1].Plane) <= half)
    {
        float d0 = cutsWanted[0].Horizontal ? cutsWanted[0].Tangent.Y : cutsWanted[0].Tangent.X;
        float d1 = cutsWanted[1].Horizontal ? cutsWanted[1].Tangent.Y : cutsWanted[1].Tangent.X;
        if (MathF.Sign(d0) == MathF.Sign(d1))
        {
            float unified = d0 > 0
                ? MathF.Max(cutsWanted[0].Plane, cutsWanted[1].Plane)
                : MathF.Min(cutsWanted[0].Plane, cutsWanted[1].Plane);

            cutsWanted[0] = (cutsWanted[0].Horizontal, unified, cutsWanted[0].P, cutsWanted[0].Tangent);
            cutsWanted[1] = (cutsWanted[1].Horizontal, unified, cutsWanted[1].P, cutsWanted[1].Tangent);
        }
    }

    foreach ((bool horizontalCut, float plane, Vector2 p, Vector2 tangent) in cutsWanted)
    {
        // The patch roots half a stroke inside the body and runs a hair narrower than the stroke:
        // exactly coincident edges are the one geometry the polygon clipper cannot union reliably.
        Vector2 normal = new(-tangent.Y, tangent.X);
        Vector2 inside = p - (tangent * (half * 0.5F));
        Vector2 side = normal * (half - 0.02F);
        Vector2 c1 = inside + side;
        Vector2 c2 = inside - side;
        float t1 = horizontalCut ? (plane - c1.Y) / tangent.Y : (plane - c1.X) / tangent.X;
        float t2 = horizontalCut ? (plane - c2.Y) / tangent.Y : (plane - c2.X) / tangent.X;
        Vector2 c1Out = c1 + (tangent * Math.Clamp(t1, 0, half * 2.5F));
        Vector2 c2Out = c2 + (tangent * Math.Clamp(t2, 0, half * 2.5F));

        PointF[] quad = [(PointF)c1, (PointF)c1Out, (PointF)c2Out, (PointF)c2];
        if (SignedArea(quad) * bodyWinding < 0)
        {
            Array.Reverse(quad);
        }

        pieces.Add(new Polygon(new LinearLineSegment(quad)));
    }
}

// Returns the signed area of a closed point loop. The sign carries the winding direction, which
// nonzero fill depends on: a patch must turn the same way as the outline it overlaps.
static float SignedArea(ReadOnlySpan<PointF> points)
{
    float area = 0;
    for (int i = 0; i < points.Length; i++)
    {
        PointF a = points[i];
        PointF b = points[(i + 1) % points.Length];
        area += (a.X * b.Y) - (b.X * a.Y);
    }

    return area / 2;
}

// Converts one skeleton stroke into path segments. The token stream is a leading point, then line
// points, arcs (NaN, radius, end), corners (NaN, NaN, radius, vertex), splines
// (NaN, NaN, NaN, count, then count points) and cubic Bezier segments
// (NaN, NaN, NaN, NaN, control1, control2, end). Corners resolve against the neighbouring anchor
// points: a positive radius rounds the corner tangentially, a negative radius produces an arc through
// the vertex itself so a dimensioned pointed extreme keeps its position. A spline runs a
// tangent-continuous curve from the current point through every listed anchor, and cubic segments
// carry exact control points, the form the OCR-B outlines use.
static void BuildStroke(PathBuilder builder, float[] stroke)
{
    List<(int Kind, float Radius, Vector2 Point, Vector2[]? Anchors)> elements = [];
    int i = 0;
    while (i < stroke.Length)
    {
        if (float.IsNaN(stroke[i]))
        {
            if (float.IsNaN(stroke[i + 1]) && float.IsNaN(stroke[i + 2]) && float.IsNaN(stroke[i + 3]))
            {
                Vector2[] controls =
                [
                    new Vector2(stroke[i + 4], stroke[i + 5]),
                    new Vector2(stroke[i + 6], stroke[i + 7]),
                ];
                elements.Add((4, 0, new Vector2(stroke[i + 8], stroke[i + 9]), controls));
                i += 10;
            }
            else if (float.IsNaN(stroke[i + 1]) && float.IsNaN(stroke[i + 2]))
            {
                int count = (int)stroke[i + 3];
                Vector2[] anchors = new Vector2[count];
                for (int a = 0; a < count; a++)
                {
                    anchors[a] = new Vector2(stroke[i + 4 + (a * 2)], stroke[i + 5 + (a * 2)]);
                }

                elements.Add((3, 0, anchors[^1], anchors));
                i += 4 + (count * 2);
            }
            else if (float.IsNaN(stroke[i + 1]))
            {
                elements.Add((2, stroke[i + 2], new Vector2(stroke[i + 3], stroke[i + 4]), null));
                i += 5;
            }
            else
            {
                elements.Add((1, stroke[i + 1], new Vector2(stroke[i + 2], stroke[i + 3]), null));
                i += 4;
            }
        }
        else
        {
            elements.Add((0, 0, new Vector2(stroke[i], stroke[i + 1]), null));
            i += 2;
        }
    }

    bool closed = elements.Count > 2 && elements[0].Kind == 0 && elements[^1].Kind != 2 &&
        elements[0].Point == elements[^1].Point;

    Vector2 current = elements[0].Point;
    builder.MoveTo(current);
    for (int k = 1; k < elements.Count; k++)
    {
        (int kind, float radius, Vector2 point, Vector2[]? anchors) = elements[k];
        if (kind == 0)
        {
            builder.LineTo(point);
            current = point;
        }
        else if (kind == 1)
        {
            AddArc(builder, current, point, MathF.Abs(radius), radius > 0);
            current = point;
        }
        else if (kind == 3)
        {
            AddSpline(builder, current, anchors!);
            current = point;
        }
        else if (kind == 4)
        {
            AddCubic(builder, current, anchors![0], anchors[1], point);
            current = point;
        }
        else
        {
            Vector2 next = elements[k + 1].Point;
            Vector2 u1 = Vector2.Normalize(point - current);
            Vector2 u2 = Vector2.Normalize(next - point);
            float cross = (u1.X * u2.Y) - (u1.Y * u2.X);
            float dot = Vector2.Dot(u1, u2);
            float r = MathF.Abs(radius);

            float tangent = r * MathF.Abs(cross) / (1 + dot);
            Vector2 start = point - (u1 * tangent);
            Vector2 end = point + (u2 * tangent);

            builder.LineTo(start);
            AddArc(builder, start, end, r, cross > 0);
            current = end;
        }
    }

    if (closed)
    {
        builder.CloseFigure();
    }
}

// Samples a C2 cubic spline from the current point through the anchors, parameterized by chord
// length: a natural spline for open runs, which gives zero end curvature, and a periodic spline when
// the curve closes on its start. The anchor sets were authored for smooth interpolation, so a merely
// tangent-continuous curve through them reads as lumpy, and C2 continuity restores the drawn shape.
static void AddSpline(PathBuilder builder, Vector2 current, Vector2[] anchors)
{
    Vector2[] points = new Vector2[anchors.Length + 1];
    points[0] = current;
    anchors.CopyTo(points, 1);
    bool cyclic = points[0] == points[^1] && points.Length > 3;
    int knotCount = cyclic ? points.Length - 1 : points.Length;

    float[] chords = new float[points.Length - 1];
    for (int i = 0; i < chords.Length; i++)
    {
        chords[i] = MathF.Max(Vector2.Distance(points[i], points[i + 1]), 1e-3f);
    }

    // Solve for the second derivative M at every knot, per coordinate, with a dense Gaussian solve:
    // the systems are tiny. Row i couples M[i-1], M[i], M[i+1] with the standard cubic spline
    // continuity equation; open ends pin M to zero, cyclic rows wrap.
    Vector2[] m = new Vector2[knotCount];
    int unknowns = cyclic ? knotCount : knotCount - 2;
    if (unknowns > 0)
    {
        float[,] matrix = new float[unknowns, unknowns];
        Vector2[] rhs = new Vector2[unknowns];
        for (int row = 0; row < unknowns; row++)
        {
            int i = cyclic ? row : row + 1;
            float hPrev = chords[cyclic ? ((i - 1) + chords.Length) % chords.Length : i - 1];
            float hNext = chords[i % chords.Length];
            Vector2 prev = points[cyclic ? ((i - 1) + knotCount) % knotCount : i - 1];
            Vector2 here = points[i];
            Vector2 next = points[cyclic ? (i + 1) % knotCount : i + 1];

            void Add(int knot, float value)
            {
                int column = cyclic ? ((knot % knotCount) + knotCount) % knotCount : knot - 1;
                if (column >= 0 && column < unknowns)
                {
                    matrix[row, column] += value;
                }
            }

            Add(i - 1, hPrev);
            Add(i, 2 * (hPrev + hNext));
            Add(i + 1, hNext);
            rhs[row] = 6 * (((next - here) / hNext) - ((here - prev) / hPrev));
        }

        for (int pivot = 0; pivot < unknowns; pivot++)
        {
            int best = pivot;
            for (int row = pivot + 1; row < unknowns; row++)
            {
                if (MathF.Abs(matrix[row, pivot]) > MathF.Abs(matrix[best, pivot]))
                {
                    best = row;
                }
            }

            if (best != pivot)
            {
                for (int column = 0; column < unknowns; column++)
                {
                    (matrix[pivot, column], matrix[best, column]) = (matrix[best, column], matrix[pivot, column]);
                }

                (rhs[pivot], rhs[best]) = (rhs[best], rhs[pivot]);
            }

            float diagonal = matrix[pivot, pivot];
            for (int row = pivot + 1; row < unknowns; row++)
            {
                float factor = matrix[row, pivot] / diagonal;
                if (factor == 0)
                {
                    continue;
                }

                for (int column = pivot; column < unknowns; column++)
                {
                    matrix[row, column] -= factor * matrix[pivot, column];
                }

                rhs[row] -= factor * rhs[pivot];
            }
        }

        Vector2[] solution = new Vector2[unknowns];
        for (int row = unknowns - 1; row >= 0; row--)
        {
            Vector2 sum = rhs[row];
            for (int column = row + 1; column < unknowns; column++)
            {
                sum -= matrix[row, column] * solution[column];
            }

            solution[row] = sum / matrix[row, row];
        }

        for (int row = 0; row < unknowns; row++)
        {
            m[cyclic ? row : row + 1] = solution[row];
        }
    }

    for (int segment = 0; segment < points.Length - 1; segment++)
    {
        float h = chords[segment];
        Vector2 p1 = points[segment];
        Vector2 p2 = points[segment + 1];
        Vector2 m1 = m[segment % knotCount];
        Vector2 m2 = m[(segment + 1) % knotCount];
        int steps = Math.Max(4, (int)MathF.Ceiling(h / 8));
        for (int step = 1; step <= steps; step++)
        {
            float t = h * step / steps;
            float a = (h - t) / h;
            float b = t / h;
            Vector2 value = (a * p1) + (b * p2) +
                ((h * h / 6) * ((((a * a * a) - a) * m1) + (((b * b * b) - b) * m2)));
            builder.LineTo(value);
        }
    }
}

// Removes flattening points that deviate from a straight run by less than the tolerance, keeping the
// outline within a fraction of a design unit of the dense curve.
static Vector2[] SimplifyContour(Vector2[] points, float tolerance)
{
    if (points.Length < 4)
    {
        return points;
    }

    bool[] keep = new bool[points.Length];
    keep[0] = keep[^1] = true;
    Simplify(0, points.Length - 1);
    return [.. points.Where((_, index) => keep[index])];

    void Simplify(int first, int last)
    {
        if (last <= first + 1)
        {
            return;
        }

        Vector2 span = points[last] - points[first];
        float norm = span.Length();
        float worst = -1;
        int peak = first;
        for (int i = first + 1; i < last; i++)
        {
            Vector2 offset = points[i] - points[first];
            float distance = norm < 1e-6f
                ? offset.Length()
                : MathF.Abs((span.X * offset.Y) - (span.Y * offset.X)) / norm;
            if (distance > worst)
            {
                worst = distance;
                peak = i;
            }
        }

        if (worst > tolerance)
        {
            keep[peak] = true;
            Simplify(first, peak);
            Simplify(peak, last);
        }
    }
}

// Samples one cubic Bezier segment with exact control points.
static void AddCubic(PathBuilder builder, Vector2 from, Vector2 control1, Vector2 control2, Vector2 to)
{
    float chord = Vector2.Distance(from, to) +
        Vector2.Distance(from, control1) + Vector2.Distance(control2, to);
    int steps = Math.Max(4, (int)MathF.Ceiling(chord / 8));
    for (int step = 1; step <= steps; step++)
    {
        float t = (float)step / steps;
        float u = 1 - t;
        builder.LineTo(
            (u * u * u * from) + (3 * u * u * t * control1) + (3 * u * t * t * control2) + (t * t * t * to));
    }
}

// Samples a circular arc of the given radius between two points. The centre sits on whichever side of
// the chord the requested direction demands, so the arc keeps both endpoints exactly.
static void AddArc(PathBuilder builder, Vector2 from, Vector2 to, float radius, bool counterClockwise)
{
    Vector2 chord = to - from;
    float chordLength = chord.Length();
    Vector2 mid = (from + to) / 2;
    float height = MathF.Sqrt(MathF.Max(0, (radius * radius) - (chordLength * chordLength / 4)));
    Vector2 normal = Vector2.Normalize(new Vector2(-chord.Y, chord.X));
    Vector2 center = counterClockwise ? mid + (normal * height) : mid - (normal * height);

    float startAngle = MathF.Atan2(from.Y - center.Y, from.X - center.X);
    float endAngle = MathF.Atan2(to.Y - center.Y, to.X - center.X);
    float sweep = endAngle - startAngle;
    if (counterClockwise && sweep < 0)
    {
        sweep += 2 * MathF.PI;
    }
    else if (!counterClockwise && sweep > 0)
    {
        sweep -= 2 * MathF.PI;
    }

    int steps = Math.Max(4, (int)MathF.Ceiling(MathF.Abs(sweep) / (2 * MathF.Sqrt(0.2f / radius))));
    for (int step = 1; step <= steps; step++)
    {
        float angle = startAngle + (sweep * step / steps);
        builder.LineTo(center + (radius * new Vector2(MathF.Cos(angle), MathF.Sin(angle))));
    }
}
