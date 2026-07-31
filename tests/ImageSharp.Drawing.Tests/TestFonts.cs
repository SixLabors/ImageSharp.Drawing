// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Tests;

public static class TestFonts
{
    public const string IcoMoonEvents = "icomoon-events.ttf";

    public const string Malgun = "malgun.ttf";

    public const string OpenSans = "OpenSans-Regular.ttf";

    // Inter Light is the weight Avalonia's ControlCatalog renders body text with (the font the sample
    // shows holes in). Inter draws several glyphs (e.g. 'A', 't') with overlapping contours, so it is a
    // good repro for glyph-fill winding/overlap holes. SIL OFL licensed, taken from Avalonia.Fonts.Inter.
    public const string InterLight = "Inter-Light.ttf";

    public const string SixLaborsSampleAB = "SixLaborsSampleAB.woff";

    public const string TwemojiMozilla = "TwemojiMozilla.ttf";

    public const string WendyOne = "WendyOne-Regular.ttf";

    public const string WhitneyBook = "whitney-book.ttf";

    public const string MeQuranVolyNewmet = "me_quran_volt_newmet.ttf";

    public const string NettoOffc = "NettoOffc.ttf";

    public const string NotoSansKRRegular = "NotoSansKR-Regular.otf";

    public const string NotoSerifKRRegular = "NotoSerifKR-Regular.otf";

    public static string NotoColorEmojiRegular => "NotoColorEmoji-Regular.ttf";
}
