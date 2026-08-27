// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

// Generates the SixLabors OCR-A font from the FIPS PUB 32 (1974) character drawings: strokes the
// OcrAGlyphs skeletons with the library's own path stroker, verifies the outlines against the
// independently transcribed SpecChecks expectations, and writes OcrA.ttf plus proof, grid and
// comparison sheets into the output directory. See specs/README.md for the source documents.
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
    Console.WriteLine("Usage: ImageSharp.Drawing.FontGenerator <output-directory> [--ref <font-path>] [<characters>...]");
    Console.WriteLine("  --ref  renders a side-by-side comparison sheet against the given font");
    Console.WriteLine("  Remaining arguments render full-size single glyph inspection images.");
    return 1;
}

string outputDirectory = args[0];
string? referencePath = null;
List<string> inspectTexts = [];
for (int argIndex = 1; argIndex < args.Length; argIndex++)
{
    if (args[argIndex] == "--ref" && argIndex + 1 < args.Length)
    {
        referencePath = args[++argIndex];
    }
    else
    {
        inspectTexts.Add(args[argIndex]);
    }
}

Directory.CreateDirectory(outputDirectory);

// The design grid maps the 0.1 inch character pitch to one em, which makes the cap ink 1.08 em: far
// larger than the 0.7 em cap convention that mainstream faces share, so the same point size renders
// far larger glyphs. This scale maps the design onto the em so the cap ink lands at 0.72 em and a
// point size matches other faces; the exact 0.1 inch pitch then prints at 10.8 points.
const float EmScale = 2F / 3F;
ushort advanceWidth = (ushort)MathF.Round(OcrAGlyphs.UnitsPerEm * EmScale);

// Ink projects a half stroke beyond the centerline box at every terminal, the i and j dots rise above it
// and the lower case descenders drop below it, so the vertical metrics cover those extremes.
short ascender = (short)MathF.Round((OcrAGlyphs.H + (OcrAGlyphs.T / 2)) * EmScale);
short descender = (short)MathF.Round((OcrAGlyphs.D - (OcrAGlyphs.T / 2)) * EmScale);
float sideBearing = (OcrAGlyphs.UnitsPerEm - OcrAGlyphs.W) / 2;
TrueTypeWriter writer = new(
    OcrAGlyphs.UnitsPerEm,
    ascender,
    descender,
    "SixLabors OCRA",
    "Copyright (c) Six Labors. Licensed under the Six Labors Split License.");

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
    LineJoin = LineJoin.Round,
    LineCap = LineCap.Butt,
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

writer.AddGlyph(' ', [], advanceWidth);
List<string> specFailures = [];
foreach ((char character, float[][] strokes) in OcrAGlyphs.Skeletons.OrderBy(entry => entry.Key))
{
    List<IReadOnlyList<Vector2>> designContours = [];
    OcrAGlyphs.SquareStrokes.TryGetValue(character, out int[]? squareIndices);
    OcrAGlyphs.ButtStrokes.TryGetValue(character, out int[]? buttIndices);
    OcrAGlyphs.MiterStrokes.TryGetValue(character, out int[]? miterIndices);
    OcrAGlyphs.MiterRoundStrokes.TryGetValue(character, out int[]? miterRoundIndices);

    // Union all strokes into one outline so the glyph carries no overlapping contours: coincident edges
    // from overlaid strokes otherwise double-cover antialiased pixels at render time.
    List<IPath> outlines = [];
    for (int strokeIndex = 0; strokeIndex < strokes.Length; strokeIndex++)
    {
        PathBuilder builder = new();
        builder.StartFigure();
        BuildStroke(builder, strokes[strokeIndex]);

        StrokeOptions options = roundStroke;
        if (squareIndices is not null && Array.IndexOf(squareIndices, strokeIndex) >= 0)
        {
            options = squareStroke;
        }
        else if (buttIndices is not null && Array.IndexOf(buttIndices, strokeIndex) >= 0)
        {
            options = buttStroke;
        }
        else if (miterIndices is not null && Array.IndexOf(miterIndices, strokeIndex) >= 0)
        {
            options = miterStroke;
        }
        else if (miterRoundIndices is not null && Array.IndexOf(miterRoundIndices, strokeIndex) >= 0)
        {
            options = miterRoundStroke;
        }

        float effectiveWidth = OcrAGlyphs.StrokeWidthOverrides.TryGetValue((character, strokeIndex), out float overrideWidth)
            ? overrideWidth
            : OcrAGlyphs.T;

        // A zero width marks a pre-computed filled ink polygon: the drawings clip some stroke ends on
        // planes a cap cannot express, so those contours carry their ink corners directly.
        if (effectiveWidth == 0)
        {
            outlines.Add(builder.Build());
            continue;
        }

        // A sub-quantum width offset keeps stroke outlines from sharing exactly coincident collinear
        // edges, which the polygon clipper cannot union reliably; the deviation is far below the design
        // grid and vanishes in quantization.
        effectiveWidth += strokeIndex * 0.02f;
        outlines.Add(builder.Build().GenerateOutline(effectiveWidth, options));
    }

    IPath merged = outlines.Count == 1
        ? outlines[0]
        : outlines[0].Clip(BooleanOperation.Union, outlines.Skip(1));

    List<IReadOnlyList<(short X, short Y)>> contours = [];
    foreach (ISimplePath simple in merged.Flatten())
    {
        ReadOnlySpan<PointF> points = simple.Points.Span;
        Vector2[] design = new Vector2[points.Length];
        for (int i = 0; i < points.Length; i++)
        {
            design[i] = new Vector2(points[i].X, points[i].Y);
        }

        designContours.Add(design);
        List<(short X, short Y)> contour = new(points.Length);
        foreach (PointF point in points)
        {
            (short X, short Y) quantized = ((short)MathF.Round((point.X + sideBearing) * EmScale), (short)MathF.Round(point.Y * EmScale));
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

    writer.AddGlyph(character, contours, advanceWidth);
    SpecChecks.Verify(character, designContours, specFailures);
}

if (specFailures.Count > 0)
{
    Console.WriteLine($"SPEC CHECK FAILED: {specFailures.Count} deviations");
    foreach (string failure in specFailures)
    {
        Console.WriteLine($"  {failure}");
    }
}
else
{
    Console.WriteLine("SPEC CHECK PASSED");
}

byte[] font = writer.Write();
string fontPath = System.IO.Path.Combine(outputDirectory, "OcrA.ttf");
File.WriteAllBytes(fontPath, font);
Console.WriteLine($"{fontPath}: {font.Length} bytes");

// Emit the font bytes as C# data for embedding in the library: the emitted file replaces
// src/ImageSharp.Drawing/Barcodes/OcrAFontData.cs whenever the design changes.
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
data.AppendLine("/// The SixLabors OCR-A TrueType font file, built clean-room from the dimensioned character");
data.AppendLine("/// drawings of FIPS PUB 32 (1974) by the ImageSharp.Drawing.FontGenerator tool.");
data.AppendLine("/// </summary>");
data.AppendLine("internal static class OcrAFontData");
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
string dataPath = System.IO.Path.Combine(outputDirectory, "OcrAFontData.cs");
File.WriteAllText(dataPath, data.ToString());
Console.WriteLine(dataPath);

// Proof sheet: load the generated font back and render the full set with the library itself.
FontCollection collection = new();
using MemoryStream stream = new(font);
FontFamily family = collection.Add(stream);
Font proofFont = family.CreateFont(100);

string[] lines =
[
    .. OcrAGlyphs.Skeletons.Keys
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

string proofPath = System.IO.Path.Combine(outputDirectory, "OcrA-proof.png");
proof.SaveAsPng(proofPath);
Console.WriteLine(proofPath);

// Large per-glyph grid for auditing each outline against its source drawing, every cell labelled
// with the character and code point it renders.
Font gridFont = family.CreateFont(200);
Font labelFont = SystemFonts.CreateFont("Arial", 24);
char[] auditCharacters = [.. OcrAGlyphs.Skeletons.Keys.OrderBy(character => character)];
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
                Origin = new PointF((column * 240) + 20, (row * 360) + 20),
            },
            auditCharacters[index].ToString(),
            Brushes.Solid(Color.Black),
            null);
        canvas.DrawText(
            new RichTextOptions(labelFont)
            {
                Origin = new PointF((column * 240) + 20, (row * 360) + 324),
            },
            $"U+{(int)auditCharacters[index]:X4} {auditCharacters[index]}",
            Brushes.Solid(Color.Gray),
            null);
    }
}));

string gridPath = System.IO.Path.Combine(outputDirectory, "OcrA-grid.png");
grid.SaveAsPng(gridPath);
Console.WriteLine(gridPath);

// Reference comparison sheet: each glyph from a reference font on the left, ours on the right.
if (referencePath is not null)
{
    FontCollection referenceCollection = new();
    Font referenceFont = referenceCollection.Add(referencePath).CreateFont(200);
    Font compareFont = family.CreateFont(200);
    char[] compareCharacters = [.. OcrAGlyphs.Skeletons.Keys.OrderBy(c => c)];
    int pairColumns = 5;
    int pairRows = (compareCharacters.Length + pairColumns - 1) / pairColumns;
    using Image<Rgba32> pairs = new(pairColumns * 520, pairRows * 320, Color.White.ToPixel<Rgba32>());
    pairs.Mutate(context => context.Paint(canvas =>
    {
        for (int index = 0; index < compareCharacters.Length; index++)
        {
            int column = index % pairColumns;
            int row = index / pairColumns;
            string text = compareCharacters[index].ToString();
            canvas.DrawText(
                new RichTextOptions(referenceFont)
                {
                    Origin = new PointF((column * 520) + 20, (row * 320) + 30),
                },
                text,
                Brushes.Solid(Color.ParseHex("007700")),
                null);
            canvas.DrawText(
                new RichTextOptions(compareFont)
                {
                    Origin = new PointF((column * 520) + 250, (row * 320) + 30),
                },
                text,
                Brushes.Solid(Color.Black),
                null);
        }
    }));

    string pairsPath = System.IO.Path.Combine(outputDirectory, "OcrA-vs-ref.png");
    pairs.SaveAsPng(pairsPath);
    Console.WriteLine(pairsPath);
}

// Full-size single glyph renders for inspection, requested as extra arguments.
Font inspectFont = family.CreateFont(700);
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

    string singlePath = System.IO.Path.Combine(outputDirectory, $"inspect-{(int)text[0]}.png");
    single.SaveAsPng(singlePath);
    Console.WriteLine(singlePath);
}

return 0;

// Converts one skeleton stroke into path segments. Token stream per stroke: a leading point, then line
// points, arcs (NaN, radius, end) and corners (NaN, NaN, radius, vertex). Corners are resolved against
// the neighbouring anchor points: a positive radius rounds the corner tangentially, a negative radius
// produces an arc through the vertex itself so a dimensioned pointed extreme keeps its position.
static void BuildStroke(PathBuilder builder, float[] stroke)
{
    List<(int Kind, float Radius, Vector2 Point)> elements = [];
    int i = 0;
    while (i < stroke.Length)
    {
        if (float.IsNaN(stroke[i]))
        {
            if (float.IsNaN(stroke[i + 1]))
            {
                elements.Add((2, stroke[i + 2], new Vector2(stroke[i + 3], stroke[i + 4])));
                i += 5;
            }
            else
            {
                elements.Add((1, stroke[i + 1], new Vector2(stroke[i + 2], stroke[i + 3])));
                i += 4;
            }
        }
        else
        {
            elements.Add((0, 0, new Vector2(stroke[i], stroke[i + 1])));
            i += 2;
        }
    }

    bool closed = elements.Count > 2 && elements[0].Kind == 0 && elements[^1].Kind != 2 &&
        elements[0].Point == elements[^1].Point;

    Vector2 current = elements[0].Point;
    builder.MoveTo(current);
    for (int k = 1; k < elements.Count; k++)
    {
        (int kind, float radius, Vector2 point) = elements[k];
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
