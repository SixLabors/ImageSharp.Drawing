// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Buffers.Binary;

namespace SixLabors.ImageSharp.Drawing.FontGenerator;

/// <summary>
/// Writes a minimal TrueType font from flattened glyph contours. Outlines contain on-curve points only,
/// which the TrueType glyf format permits, so no curve conversion is required. The emitted tables are
/// head, hhea, maxp, OS/2, hmtx, cmap, loca, glyf, name and post.
/// </summary>
internal sealed class TrueTypeWriter
{
    private readonly List<GlyphRecord> glyphs = [];
    private readonly SortedDictionary<char, ushort> characterMap = [];

    /// <summary>
    /// Initializes a new instance of the <see cref="TrueTypeWriter"/> class. Glyph index 0 is the
    /// required empty .notdef glyph.
    /// </summary>
    /// <param name="unitsPerEm">The design units per em.</param>
    /// <param name="ascender">The typographic ascender in design units.</param>
    /// <param name="descender">The typographic descender in design units, negative below the baseline.</param>
    /// <param name="familyName">The font family name.</param>
    /// <param name="copyright">The copyright notice written to name ID 0.</param>
    public TrueTypeWriter(ushort unitsPerEm, short ascender, short descender, string familyName, string copyright)
    {
        this.UnitsPerEm = unitsPerEm;
        this.Ascender = ascender;
        this.Descender = descender;
        this.FamilyName = familyName;
        this.Copyright = copyright;
        this.glyphs.Add(new GlyphRecord([], unitsPerEm));
    }

    /// <summary>
    /// Gets the design units per em.
    /// </summary>
    public ushort UnitsPerEm { get; }

    /// <summary>
    /// Gets the typographic ascender in design units.
    /// </summary>
    public short Ascender { get; }

    /// <summary>
    /// Gets the typographic descender in design units.
    /// </summary>
    public short Descender { get; }

    /// <summary>
    /// Gets the font family name.
    /// </summary>
    public string FamilyName { get; }

    /// <summary>
    /// Gets the copyright notice.
    /// </summary>
    public string Copyright { get; }

    /// <summary>
    /// Adds a glyph mapped to a character. Contours are closed point lists in font units with the y axis
    /// pointing up; fill follows the non-zero winding rule.
    /// </summary>
    /// <param name="character">The character the glyph represents.</param>
    /// <param name="contours">The closed contours, each a list of on-curve points.</param>
    /// <param name="advanceWidth">The advance width in font units.</param>
    public void AddGlyph(char character, IReadOnlyList<IReadOnlyList<(short X, short Y)>> contours, ushort advanceWidth)
    {
        this.characterMap.Add(character, (ushort)this.glyphs.Count);
        this.glyphs.Add(new GlyphRecord(contours, advanceWidth));
    }

    /// <summary>
    /// Serializes the font.
    /// </summary>
    /// <returns>The font file bytes.</returns>
    public byte[] Write()
    {
        byte[] glyf = this.BuildGlyf(out uint[] locaOffsets, out short xMin, out short yMin, out short xMax, out short yMax);
        byte[] loca = BuildLoca(locaOffsets);
        byte[] head = this.BuildHead(xMin, yMin, xMax, yMax);
        byte[] hhea = this.BuildHhea(xMin, xMax);
        byte[] maxp = this.BuildMaxp();
        byte[] hmtx = this.BuildHmtx();
        byte[] cmap = this.BuildCmap();
        byte[] os2 = this.BuildOs2(xMin, yMin, xMax, yMax);
        byte[] name = this.BuildName();
        byte[] post = BuildPost();

        (string Tag, byte[] Data)[] tables =
        [
            ("OS/2", os2),
            ("cmap", cmap),
            ("glyf", glyf),
            ("head", head),
            ("hhea", hhea),
            ("hmtx", hmtx),
            ("loca", loca),
            ("maxp", maxp),
            ("name", name),
            ("post", post),
        ];

        ushort numTables = (ushort)tables.Length;
        ushort searchRange = 16;
        ushort entrySelector = 0;
        while (searchRange * 2 <= numTables * 16)
        {
            searchRange *= 2;
            entrySelector++;
        }

        int headerLength = 12 + (numTables * 16);
        int totalLength = headerLength;
        foreach ((_, byte[] data) in tables)
        {
            totalLength += (data.Length + 3) & ~3;
        }

        byte[] font = new byte[totalLength];
        BinaryPrimitives.WriteUInt32BigEndian(font, 0x00010000);
        BinaryPrimitives.WriteUInt16BigEndian(font.AsSpan(4), numTables);
        BinaryPrimitives.WriteUInt16BigEndian(font.AsSpan(6), searchRange);
        BinaryPrimitives.WriteUInt16BigEndian(font.AsSpan(8), entrySelector);
        BinaryPrimitives.WriteUInt16BigEndian(font.AsSpan(10), (ushort)((numTables * 16) - searchRange));

        int record = 12;
        int offset = headerLength;
        int headOffset = 0;
        foreach ((string tag, byte[] data) in tables)
        {
            if (tag == "head")
            {
                headOffset = offset;
            }

            for (int i = 0; i < 4; i++)
            {
                font[record + i] = (byte)tag[i];
            }

            data.CopyTo(font, offset);
            BinaryPrimitives.WriteUInt32BigEndian(font.AsSpan(record + 4), TableChecksum(font.AsSpan(offset, (data.Length + 3) & ~3)));
            BinaryPrimitives.WriteUInt32BigEndian(font.AsSpan(record + 8), (uint)offset);
            BinaryPrimitives.WriteUInt32BigEndian(font.AsSpan(record + 12), (uint)data.Length);
            record += 16;
            offset += (data.Length + 3) & ~3;
        }

        uint total = TableChecksum(font);
        BinaryPrimitives.WriteUInt32BigEndian(font.AsSpan(headOffset + 8), 0xB1B0AFBA - total);
        return font;
    }

    /// <summary>
    /// Sums the table as big-endian 32 bit words with zero padding, the per-table checksum the sfnt
    /// directory records.
    /// </summary>
    /// <param name="data">The table bytes.</param>
    /// <returns>The checksum.</returns>
    private static uint TableChecksum(ReadOnlySpan<byte> data)
    {
        uint sum = 0;
        for (int i = 0; i < data.Length; i += 4)
        {
            uint value = 0;
            for (int b = 0; b < 4; b++)
            {
                value <<= 8;
                if (i + b < data.Length)
                {
                    value |= data[i + b];
                }
            }

            sum += value;
        }

        return sum;
    }

    /// <summary>
    /// Builds the long-format loca table: one 32 bit glyf offset per glyph plus the trailing end offset.
    /// </summary>
    /// <param name="offsets">The glyf byte offsets.</param>
    /// <returns>The table bytes.</returns>
    private static byte[] BuildLoca(uint[] offsets)
    {
        byte[] loca = new byte[offsets.Length * 4];
        for (int i = 0; i < offsets.Length; i++)
        {
            BinaryPrimitives.WriteUInt32BigEndian(loca.AsSpan(i * 4), offsets[i]);
        }

        return loca;
    }

    /// <summary>
    /// Builds a version 3.0 post table, the variant that stores no glyph names.
    /// </summary>
    /// <returns>The table bytes.</returns>
    private static byte[] BuildPost()
    {
        byte[] post = new byte[32];
        BinaryPrimitives.WriteUInt32BigEndian(post, 0x00030000);
        return post;
    }

    /// <summary>
    /// Builds the glyf table from the glyph contours: per glyph a simple-glyph header, contour end
    /// indices, one on-curve flag per point and relative 16 bit coordinates, padded to 4 bytes.
    /// </summary>
    /// <param name="locaOffsets">The glyf byte offset of every glyph plus the trailing end offset.</param>
    /// <param name="xMin">The font-wide minimum x of the ink.</param>
    /// <param name="yMin">The font-wide minimum y of the ink.</param>
    /// <param name="xMax">The font-wide maximum x of the ink.</param>
    /// <param name="yMax">The font-wide maximum y of the ink.</param>
    /// <returns>The table bytes.</returns>
    private byte[] BuildGlyf(out uint[] locaOffsets, out short xMin, out short yMin, out short xMax, out short yMax)
    {
        using MemoryStream stream = new();
        Span<byte> coordinate = stackalloc byte[2];
        locaOffsets = new uint[this.glyphs.Count + 1];
        xMin = short.MaxValue;
        yMin = short.MaxValue;
        xMax = short.MinValue;
        yMax = short.MinValue;

        for (int g = 0; g < this.glyphs.Count; g++)
        {
            locaOffsets[g] = (uint)stream.Length;
            GlyphRecord glyph = this.glyphs[g];
            if (glyph.Contours.Count == 0)
            {
                continue;
            }

            glyph.GetBounds(out short gxMin, out short gyMin, out short gxMax, out short gyMax);
            xMin = Math.Min(xMin, gxMin);
            yMin = Math.Min(yMin, gyMin);
            xMax = Math.Max(xMax, gxMax);
            yMax = Math.Max(yMax, gyMax);

            int pointCount = glyph.Contours.Sum(contour => contour.Count);
            byte[] header = new byte[10 + (glyph.Contours.Count * 2) + 2];
            BinaryPrimitives.WriteInt16BigEndian(header, (short)glyph.Contours.Count);
            BinaryPrimitives.WriteInt16BigEndian(header.AsSpan(2), gxMin);
            BinaryPrimitives.WriteInt16BigEndian(header.AsSpan(4), gyMin);
            BinaryPrimitives.WriteInt16BigEndian(header.AsSpan(6), gxMax);
            BinaryPrimitives.WriteInt16BigEndian(header.AsSpan(8), gyMax);

            int end = -1;
            for (int c = 0; c < glyph.Contours.Count; c++)
            {
                end += glyph.Contours[c].Count;
                BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(10 + (c * 2)), (ushort)end);
            }

            BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(10 + (glyph.Contours.Count * 2)), 0);
            stream.Write(header);

            // All points are on-curve; write one flag byte per point followed by absolute-to-relative
            // deltas as int16 coordinates.
            for (int i = 0; i < pointCount; i++)
            {
                stream.WriteByte(0x01);
            }

            short previous = 0;
            foreach (IReadOnlyList<(short X, short Y)> contour in glyph.Contours)
            {
                foreach ((short x, _) in contour)
                {
                    BinaryPrimitives.WriteInt16BigEndian(coordinate, (short)(x - previous));
                    stream.Write(coordinate);
                    previous = x;
                }
            }

            previous = 0;
            foreach (IReadOnlyList<(short X, short Y)> contour in glyph.Contours)
            {
                foreach ((_, short y) in contour)
                {
                    BinaryPrimitives.WriteInt16BigEndian(coordinate, (short)(y - previous));
                    stream.Write(coordinate);
                    previous = y;
                }
            }

            while ((stream.Length & 3) != 0)
            {
                stream.WriteByte(0);
            }
        }

        locaOffsets[this.glyphs.Count] = (uint)stream.Length;
        if (xMin == short.MaxValue)
        {
            xMin = 0;
            yMin = 0;
            xMax = 0;
            yMax = 0;
        }

        return stream.ToArray();
    }

    /// <summary>
    /// Builds the head table: units per em, the font-wide ink bounds, a lowest recommended size of 8
    /// pixels per em and the long loca format flag.
    /// </summary>
    /// <param name="xMin">The font-wide minimum x of the ink.</param>
    /// <param name="yMin">The font-wide minimum y of the ink.</param>
    /// <param name="xMax">The font-wide maximum x of the ink.</param>
    /// <param name="yMax">The font-wide maximum y of the ink.</param>
    /// <returns>The table bytes.</returns>
    private byte[] BuildHead(short xMin, short yMin, short xMax, short yMax)
    {
        byte[] head = new byte[54];
        BinaryPrimitives.WriteUInt32BigEndian(head, 0x00010000);
        BinaryPrimitives.WriteUInt32BigEndian(head.AsSpan(4), 0x00010000);
        BinaryPrimitives.WriteUInt32BigEndian(head.AsSpan(12), 0x5F0F3CF5);
        BinaryPrimitives.WriteUInt16BigEndian(head.AsSpan(16), 0x0003);
        BinaryPrimitives.WriteUInt16BigEndian(head.AsSpan(18), this.UnitsPerEm);
        BinaryPrimitives.WriteInt16BigEndian(head.AsSpan(36), xMin);
        BinaryPrimitives.WriteInt16BigEndian(head.AsSpan(38), yMin);
        BinaryPrimitives.WriteInt16BigEndian(head.AsSpan(40), xMax);
        BinaryPrimitives.WriteInt16BigEndian(head.AsSpan(42), yMax);
        BinaryPrimitives.WriteUInt16BigEndian(head.AsSpan(46), 8);
        BinaryPrimitives.WriteInt16BigEndian(head.AsSpan(48), 2);
        BinaryPrimitives.WriteInt16BigEndian(head.AsSpan(50), 1);
        return head;
    }

    /// <summary>
    /// Builds the hhea table: the vertical metrics, a zero line gap, the maximum advance width and the
    /// horizontal metric count.
    /// </summary>
    /// <param name="xMin">The font-wide minimum x of the ink.</param>
    /// <param name="xMax">The font-wide maximum x of the ink.</param>
    /// <returns>The table bytes.</returns>
    private byte[] BuildHhea(short xMin, short xMax)
    {
        byte[] hhea = new byte[36];
        BinaryPrimitives.WriteUInt32BigEndian(hhea, 0x00010000);
        BinaryPrimitives.WriteInt16BigEndian(hhea.AsSpan(4), this.Ascender);
        BinaryPrimitives.WriteInt16BigEndian(hhea.AsSpan(6), this.Descender);
        BinaryPrimitives.WriteInt16BigEndian(hhea.AsSpan(8), 0);
        BinaryPrimitives.WriteUInt16BigEndian(hhea.AsSpan(10), this.glyphs.Max(glyph => glyph.AdvanceWidth));
        BinaryPrimitives.WriteInt16BigEndian(hhea.AsSpan(12), xMin);
        BinaryPrimitives.WriteInt16BigEndian(hhea.AsSpan(14), 0);
        BinaryPrimitives.WriteInt16BigEndian(hhea.AsSpan(16), xMax);
        BinaryPrimitives.WriteInt16BigEndian(hhea.AsSpan(18), 1);
        BinaryPrimitives.WriteUInt16BigEndian(hhea.AsSpan(34), (ushort)this.glyphs.Count);
        return hhea;
    }

    /// <summary>
    /// Builds the version 1.0 maxp table with the glyph count and the point and contour maxima.
    /// </summary>
    /// <returns>The table bytes.</returns>
    private byte[] BuildMaxp()
    {
        int maxPoints = 0;
        int maxContours = 0;
        foreach (GlyphRecord glyph in this.glyphs)
        {
            maxPoints = Math.Max(maxPoints, glyph.Contours.Sum(contour => contour.Count));
            maxContours = Math.Max(maxContours, glyph.Contours.Count);
        }

        byte[] maxp = new byte[32];
        BinaryPrimitives.WriteUInt32BigEndian(maxp, 0x00010000);
        BinaryPrimitives.WriteUInt16BigEndian(maxp.AsSpan(4), (ushort)this.glyphs.Count);
        BinaryPrimitives.WriteUInt16BigEndian(maxp.AsSpan(6), (ushort)maxPoints);
        BinaryPrimitives.WriteUInt16BigEndian(maxp.AsSpan(8), (ushort)maxContours);
        BinaryPrimitives.WriteUInt16BigEndian(maxp.AsSpan(14), 2);
        return maxp;
    }

    /// <summary>
    /// Builds the hmtx table: the advance width and left side bearing of every glyph.
    /// </summary>
    /// <returns>The table bytes.</returns>
    private byte[] BuildHmtx()
    {
        byte[] hmtx = new byte[this.glyphs.Count * 4];
        for (int i = 0; i < this.glyphs.Count; i++)
        {
            GlyphRecord glyph = this.glyphs[i];
            glyph.GetBounds(out short gxMin, out _, out _, out _);
            BinaryPrimitives.WriteUInt16BigEndian(hmtx.AsSpan(i * 4), glyph.AdvanceWidth);
            BinaryPrimitives.WriteInt16BigEndian(hmtx.AsSpan((i * 4) + 2), glyph.Contours.Count == 0 ? (short)0 : gxMin);
        }

        return hmtx;
    }

    /// <summary>
    /// Builds the cmap table with a single format 4 subtable for the Windows Unicode BMP platform.
    /// </summary>
    /// <returns>The table bytes.</returns>
    private byte[] BuildCmap()
    {
        // Build format 4 segments from runs of consecutive characters mapping to consecutive glyph ids.
        List<(ushort Start, ushort End, ushort GlyphStart)> segments = [];
        foreach (KeyValuePair<char, ushort> entry in this.characterMap)
        {
            if (segments.Count > 0)
            {
                (ushort start, ushort end, ushort glyphStart) = segments[^1];
                if (entry.Key == end + 1 && entry.Value == glyphStart + (end - start) + 1)
                {
                    segments[^1] = (start, entry.Key, glyphStart);
                    continue;
                }
            }

            segments.Add((entry.Key, entry.Key, entry.Value));
        }

        segments.Add((0xFFFF, 0xFFFF, 0));
        ushort segCount = (ushort)segments.Count;
        ushort segCountX2 = (ushort)(segCount * 2);
        ushort searchRange = 2;
        ushort entrySelector = 0;
        while (searchRange * 2 <= segCountX2)
        {
            searchRange *= 2;
            entrySelector++;
        }

        int subtableLength = 16 + (segCount * 8);
        byte[] cmap = new byte[12 + subtableLength];
        BinaryPrimitives.WriteUInt16BigEndian(cmap.AsSpan(2), 1);
        BinaryPrimitives.WriteUInt16BigEndian(cmap.AsSpan(4), 3);
        BinaryPrimitives.WriteUInt16BigEndian(cmap.AsSpan(6), 1);
        BinaryPrimitives.WriteUInt32BigEndian(cmap.AsSpan(8), 12);

        Span<byte> subtable = cmap.AsSpan(12);
        BinaryPrimitives.WriteUInt16BigEndian(subtable, 4);
        BinaryPrimitives.WriteUInt16BigEndian(subtable[2..], (ushort)subtableLength);
        BinaryPrimitives.WriteUInt16BigEndian(subtable[6..], segCountX2);
        BinaryPrimitives.WriteUInt16BigEndian(subtable[8..], searchRange);
        BinaryPrimitives.WriteUInt16BigEndian(subtable[10..], entrySelector);
        BinaryPrimitives.WriteUInt16BigEndian(subtable[12..], (ushort)(segCountX2 - searchRange));

        int endCodes = 14;
        int startCodes = endCodes + segCountX2 + 2;
        int idDeltas = startCodes + segCountX2;
        int idRanges = idDeltas + segCountX2;
        for (int i = 0; i < segCount; i++)
        {
            (ushort start, ushort end, ushort glyphStart) = segments[i];
            BinaryPrimitives.WriteUInt16BigEndian(subtable[(endCodes + (i * 2))..], end);
            BinaryPrimitives.WriteUInt16BigEndian(subtable[(startCodes + (i * 2))..], start);
            ushort delta = i == segCount - 1 ? (ushort)1 : (ushort)(glyphStart - start);
            BinaryPrimitives.WriteUInt16BigEndian(subtable[(idDeltas + (i * 2))..], delta);
            BinaryPrimitives.WriteUInt16BigEndian(subtable[(idRanges + (i * 2))..], 0);
        }

        return cmap;
    }

    /// <summary>
    /// Builds the version 4 OS/2 table: regular weight and width, the SixL vendor tag, the character
    /// range, the typographic and Windows vertical metrics and the space break character.
    /// </summary>
    /// <param name="xMin">The font-wide minimum x of the ink.</param>
    /// <param name="yMin">The font-wide minimum y of the ink.</param>
    /// <param name="xMax">The font-wide maximum x of the ink.</param>
    /// <param name="yMax">The font-wide maximum y of the ink.</param>
    /// <returns>The table bytes.</returns>
    private byte[] BuildOs2(short xMin, short yMin, short xMax, short yMax)
    {
        byte[] os2 = new byte[96];
        BinaryPrimitives.WriteUInt16BigEndian(os2, 4);
        BinaryPrimitives.WriteInt16BigEndian(os2.AsSpan(2), (short)this.UnitsPerEm);
        BinaryPrimitives.WriteUInt16BigEndian(os2.AsSpan(4), 400);
        BinaryPrimitives.WriteUInt16BigEndian(os2.AsSpan(6), 5);
        BinaryPrimitives.WriteInt16BigEndian(os2.AsSpan(10), (short)(this.UnitsPerEm / 5));
        BinaryPrimitives.WriteInt16BigEndian(os2.AsSpan(12), (short)(this.UnitsPerEm / 10));
        os2[58] = (byte)'S';
        os2[59] = (byte)'i';
        os2[60] = (byte)'x';
        os2[61] = (byte)'L';
        BinaryPrimitives.WriteUInt16BigEndian(os2.AsSpan(62), 0x0040);
        BinaryPrimitives.WriteUInt16BigEndian(os2.AsSpan(64), this.characterMap.Keys.Min());
        BinaryPrimitives.WriteUInt16BigEndian(os2.AsSpan(66), this.characterMap.Keys.Max());
        BinaryPrimitives.WriteInt16BigEndian(os2.AsSpan(68), this.Ascender);
        BinaryPrimitives.WriteInt16BigEndian(os2.AsSpan(70), this.Descender);
        BinaryPrimitives.WriteInt16BigEndian(os2.AsSpan(72), 0);
        BinaryPrimitives.WriteUInt16BigEndian(os2.AsSpan(74), (ushort)yMax);
        BinaryPrimitives.WriteUInt16BigEndian(os2.AsSpan(76), (ushort)Math.Max(0, -yMin));
        BinaryPrimitives.WriteInt16BigEndian(os2.AsSpan(88), yMax);
        BinaryPrimitives.WriteUInt16BigEndian(os2.AsSpan(92), ' ');
        return os2;
    }

    /// <summary>
    /// Builds the name table for the Windows Unicode platform: copyright, family, subfamily, unique and
    /// full names, the version string and the PostScript name.
    /// </summary>
    /// <returns>The table bytes.</returns>
    private byte[] BuildName()
    {
        (ushort Id, string Value)[] entries =
        [
            (0, this.Copyright),
            (1, this.FamilyName),
            (2, "Regular"),
            (3, this.FamilyName),
            (4, this.FamilyName),
            (5, "Version 1.000"),
            (6, this.FamilyName.Replace(" ", string.Empty)),
        ];

        int stringLength = entries.Sum(entry => entry.Value.Length * 2);
        byte[] name = new byte[6 + (entries.Length * 12) + stringLength];
        BinaryPrimitives.WriteUInt16BigEndian(name.AsSpan(2), (ushort)entries.Length);
        BinaryPrimitives.WriteUInt16BigEndian(name.AsSpan(4), (ushort)(6 + (entries.Length * 12)));

        int record = 6;
        int offset = 0;
        int storage = 6 + (entries.Length * 12);
        foreach ((ushort id, string value) in entries)
        {
            BinaryPrimitives.WriteUInt16BigEndian(name.AsSpan(record), 3);
            BinaryPrimitives.WriteUInt16BigEndian(name.AsSpan(record + 2), 1);
            BinaryPrimitives.WriteUInt16BigEndian(name.AsSpan(record + 4), 0x0409);
            BinaryPrimitives.WriteUInt16BigEndian(name.AsSpan(record + 6), id);
            BinaryPrimitives.WriteUInt16BigEndian(name.AsSpan(record + 8), (ushort)(value.Length * 2));
            BinaryPrimitives.WriteUInt16BigEndian(name.AsSpan(record + 10), (ushort)offset);
            foreach (char c in value)
            {
                BinaryPrimitives.WriteUInt16BigEndian(name.AsSpan(storage + offset), c);
                offset += 2;
            }

            record += 12;
        }

        return name;
    }

    /// <summary>
    /// One glyph's closed contours and advance width.
    /// </summary>
    private sealed class GlyphRecord
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="GlyphRecord"/> class.
        /// </summary>
        /// <param name="contours">The closed contours, each a list of on-curve points.</param>
        /// <param name="advanceWidth">The advance width in font units.</param>
        public GlyphRecord(IReadOnlyList<IReadOnlyList<(short X, short Y)>> contours, ushort advanceWidth)
        {
            this.Contours = contours;
            this.AdvanceWidth = advanceWidth;
        }

        /// <summary>
        /// Gets the closed contours, each a list of on-curve points.
        /// </summary>
        public IReadOnlyList<IReadOnlyList<(short X, short Y)>> Contours { get; }

        /// <summary>
        /// Gets the advance width in font units.
        /// </summary>
        public ushort AdvanceWidth { get; }

        /// <summary>
        /// Reports the bounding box of the contours, or a zero box for the empty glyph.
        /// </summary>
        /// <param name="xMin">The minimum x of the contours.</param>
        /// <param name="yMin">The minimum y of the contours.</param>
        /// <param name="xMax">The maximum x of the contours.</param>
        /// <param name="yMax">The maximum y of the contours.</param>
        public void GetBounds(out short xMin, out short yMin, out short xMax, out short yMax)
        {
            xMin = short.MaxValue;
            yMin = short.MaxValue;
            xMax = short.MinValue;
            yMax = short.MinValue;
            foreach (IReadOnlyList<(short X, short Y)> contour in this.Contours)
            {
                foreach ((short x, short y) in contour)
                {
                    xMin = Math.Min(xMin, x);
                    yMin = Math.Min(yMin, y);
                    xMax = Math.Max(xMax, x);
                    yMax = Math.Max(yMax, y);
                }
            }

            if (xMin == short.MaxValue)
            {
                xMin = 0;
                yMin = 0;
                xMax = 0;
                yMax = 0;
            }
        }
    }
}
