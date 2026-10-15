// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Barcodes;

namespace SixLabors.ImageSharp.Drawing.Tests.Barcodes;

/// <summary>
/// Encoder tests for the Aztec Code symbologies. Each expected grid is the module grid of an independent
/// reference implementation, one row of hexadecimal digits per line, four modules per digit with the
/// leftmost module the most significant bit. That implementation never picks a compact symbol on its own, so
/// the reference for an automatic size is the first symbol in the order of section 11.5 of ISO/IEC 24778
/// that holds the data: compact 1 to 4 layers, then full-range 4 to 32 layers.
/// </summary>
public class AztecCodeSymbologyTests
{
    /// <summary>
    /// Gets the cases of <see cref="MatchesReferenceGrid"/>: the symbology, the text, the error correction
    /// percentage, the layers (0 for the smallest symbol), the side, and the grid.
    /// </summary>
    public static TheoryData<string, string, int, int, int, string> MatchesReferenceGridData { get; } = new()
    {
        {
            "Auto",
            "This is Aztec Code",
            23,
            0,
            19,
            "a394c|" +
            "9376a|" +
            "ddb3a|" +
            "f5dca|" +
            "3d420|" +
            "4ffe8|" +
            "34070|" +
            "b5f4c|" +
            "9517a|" +
            "95570|" +
            "8d160|" +
            "a5f6e|" +
            "2c076|" +
            "07fe8|" +
            "60b08|" +
            "ae870|" +
            "b683c|" +
            "061f0|" +
            "e3346"
        },
        {
            "Auto",
            "123",
            23,
            0,
            15,
            "3f1e|" +
            "d778|" +
            "f02a|" +
            "3ffa|" +
            "5010|" +
            "b7d6|" +
            "945c|" +
            "1552|" +
            "9458|" +
            "d7de|" +
            "3012|" +
            "9ff8|" +
            "0182|" +
            "5c14|" +
            "4a28"
        },
        {
            "Auto",
            "Hello, World! a. b: c\r\nd",
            23,
            0,
            19,
            "50408|" +
            "081c4|" +
            "efa66|" +
            "fd760|" +
            "3d5a8|" +
            "2ffe0|" +
            "d4078|" +
            "2df6a|" +
            "ed150|" +
            "75542|" +
            "4d148|" +
            "85f48|" +
            "c4062|" +
            "27fe4|" +
            "33684|" +
            "28f70|" +
            "114d8|" +
            "9a8b2|" +
            "2a162"
        },
        {
            "Auto",
            "012345678901234567890123456789012345678901234567890123456789012345678901234567890123456789012345678901234567890123456789",
            23,
            0,
            31,
            "fb108dd8|" +
            "e0afad04|" +
            "0c443764|" +
            "72bdb412|" +
            "30fee834|" +
            "4afbabac|" +
            "9acc96b4|" +
            "8eff32b6|" +
            "32c603d0|" +
            "9dfffe7c|" +
            "9d400760|" +
            "d3dff676|" +
            "26d016a6|" +
            "d0d7d658|" +
            "ccd4549e|" +
            "55555554|" +
            "155455a2|" +
            "65d7d53a|" +
            "24501708|" +
            "ca5ff74a|" +
            "67400482|" +
            "627fff0c|" +
            "74362148|" +
            "89d5a6b4|" +
            "7a1ea2a2|" +
            "316bf9f4|" +
            "47621aac|" +
            "af512d74|" +
            "888cec56|" +
            "16bda0b0|" +
            "76221bb4"
        },
        {
            "Auto",
            "Aztec Code is a matrix code invented by Andrew Longacre, Jr. and Robert Hussey in 1995. Aztec Code is a matrix code invented by Andrew Longacre, Jr. and Robert Hussey in 1995. Aztec Code is a matrix code invented by Andrew Longacre, Jr. and Robert Hussey in 1995. Aztec Code is a matrix code invented by Andrew Longacre, Jr. and Robert Hussey in 1995. ",
            23,
            0,
            57,
            "18db3ca999b8850|" +
            "43a185a0ac32858|" +
            "55dc1509c669570|" +
            "e2850a506560d40|" +
            "310a35f8104c418|" +
            "d032fab39610bf0|" +
            "47695f79beacfe8|" +
            "cb3022a18df0e00|" +
            "5149760d40e9fc0|" +
            "5820a2829431080|" +
            "14a90bbce81b480|" +
            "cd042c6743324f8|" +
            "aaaaaaaaaaaaaa8|" +
            "3b270f33d267c60|" +
            "765d18f86e29818|" +
            "34717196a110180|" +
            "30295f79a2b8530|" +
            "02d521010350728|" +
            "cd19f8bd150aa60|" +
            "ef053f746143448|" +
            "1249b07d5ce9900|" +
            "3976c6909c40240|" +
            "95781ffffb0fb00|" +
            "7e412e003554030|" +
            "116ca6ffbc8dc38|" +
            "22109e80bc34c98|" +
            "313e2abeb6486a0|" +
            "b604f6a2bd47ac0|" +
            "aaaaaaaaaaaaaa8|" +
            "ab827aa2ade7ca8|" +
            "640f9ebeb23c2b0|" +
            "88909a80a891038|" +
            "382ae6ffbbda260|" +
            "3515920034106e8|" +
            "8fae63fffdebd18|" +
            "86e590e583678f0|" +
            "a49e909eab3e280|" +
            "b335d1750946260|" +
            "a158f07c665ca40|" +
            "1e551d213ec0e78|" +
            "2c4c86cfa2fd968|" +
            "704670a4e2f74d0|" +
            "27ba96af07fe628|" +
            "637627b7ea75030|" +
            "aaaaaaaaaaaaaa8|" +
            "2d93198079567a8|" +
            "148ae16bbc7a990|" +
            "68424101f584810|" +
            "48d8869ed5fa028|" +
            "e8520dd05615258|" +
            "069a145846ce730|" +
            "09807953ece7c28|" +
            "916bbc7ec62d180|" +
            "a4327c009e84480|" +
            "b10a0059c17d108|" +
            "bd1069d55f54398|" +
            "a22a081fac48b10"
        },
        {
            "Auto",
            "abc £¦©¬¯²µ¸»¾ÁÄÇÊÍÐÓÖÙÜßâåèëîñô÷úý £¦©¬¯²µXYZ",
            23,
            0,
            27,
            "dbd9e8e|" +
            "9664576|" +
            "3fc5890|" +
            "2b3bf72|" +
            "b837534|" +
            "2ca77d2|" +
            "5305690|" +
            "aed179a|" +
            "7bfc226|" +
            "39ffff0|" +
            "fcc0438|" +
            "f6df432|" +
            "8e51764|" +
            "1d55656|" +
            "1dd1636|" +
            "395f6c4|" +
            "0a404e6|" +
            "297fff4|" +
            "743c11e|" +
            "39e852c|" +
            "d16a5fc|" +
            "45ffdb8|" +
            "126e9d6|" +
            "3fe66f4|" +
            "66b7bfe|" +
            "bbddfba|" +
            "1212342"
        },
        {
            "Auto",
            "MiXeD cAsE @ ~ | text 42",
            50,
            0,
            23,
            "46b32c|" +
            "cb23ea|" +
            "419e82|" +
            "c49302|" +
            "1b862a|" +
            "60a174|" +
            "5792f0|" +
            "73ffa0|" +
            "fb018a|" +
            "3b7d06|" +
            "ef4534|" +
            "2b5522|" +
            "a145be|" +
            "697da8|" +
            "8b0140|" +
            "fdffba|" +
            "90527a|" +
            "084fde|" +
            "548ad8|" +
            "4eab68|" +
            "32b868|" +
            "2819e4|" +
            "4ce946"
        },
        {
            "Full",
            "Aztec",
            23,
            1,
            19,
            "3d1f0|" +
            "1f62e|" +
            "7000a|" +
            "7fffa|" +
            "f0012|" +
            "17fd0|" +
            "f4050|" +
            "55f58|" +
            "d5152|" +
            "55554|" +
            "75156|" +
            "75f5a|" +
            "34058|" +
            "f7fd8|" +
            "1001e|" +
            "5fffc|" +
            "08a80|" +
            "85d94|" +
            "442d8"
        },
        {
            "Full",
            "This is Aztec Code",
            23,
            6,
            41,
            "aa48c9d7da0|" +
            "82fda464208|" +
            "df7b5c799e0|" +
            "e4b924ee910|" +
            "aaaaaaaaaa8|" +
            "2078521db08|" +
            "4cf47dd4288|" +
            "22d73302800|" +
            "ac255ae73b8|" +
            "a694b124968|" +
            "89d09c9c1d0|" +
            "83ac9793628|" +
            "892929f4090|" +
            "07565013150|" +
            "0d07fff55e8|" +
            "67220029e38|" +
            "6d8effae2c8|" +
            "761280b4308|" +
            "6c9ebeb5888|" +
            "47fea2a9b70|" +
            "aaaaaaaaaa8|" +
            "1406a2b9270|" +
            "3f36beb5d80|" +
            "333680b6a40|" +
            "e802ffa04a0|" +
            "e0520027160|" +
            "093bfff07b0|" +
            "036946c2058|" +
            "4e137f34cb8|" +
            "7147a7b9750|" +
            "bcfe0f459d0|" +
            "e1a3a347858|" +
            "8b58f86a4f8|" +
            "803d5611228|" +
            "9cd2cdecec8|" +
            "7262c68e160|" +
            "aaaaaaaaaa8|" +
            "979604f1620|" +
            "2de66a57fd8|" +
            "1486f5f1b10|" +
            "8e20bdb3bd8"
        },
        {
            "Full",
            "Aztec Code is a matrix code invented by Andrew Longacre, Jr. and Robert Hussey in 1995. Aztec Code is a matrix code invented by Andrew Longacre, Jr. and Robert Hussey in 1995. Aztec Code is a matrix code invented by Andrew Longacre, Jr. and Robert Hussey in 1995. Aztec Code is a matrix code invented by Andrew Longacre, Jr. and Robert Hussey in 1995. ",
            10,
            23,
            113,
            "18b50bd82a838ce2adc338b5f5b70|" +
            "5958850a1450ca61a9530d0235780|" +
            "68b5f5b72c917a8728808298afdc0|" +
            "dd02357810244572007cb04c11288|" +
            "3bc61e8564f748a9ed817186ddf10|" +
            "cb12d919be344305ac22f839b51e8|" +
            "428baec290d0bfa35689e2be788e8|" +
            "c04c1622f00e6c5bd70cea244e600|" +
            "aaaaaaaaaaaaaaaaaaaaaaaaaaaa8|" +
            "5f6be923e07857577c1bf43258120|" +
            "6ffccbcf5af3c3b2d9ac8ac682f20|" +
            "133ac6320934637b501b626bae770|" +
            "e3bd96ca2ae363b299bc6fb016de8|" +
            "241dd2603b76e65e30237c3f7c120|" +
            "509b9ce27fd804c95dc9c48e66850|" +
            "3f5e2a41e12d3c415e68dc14111e0|" +
            "1fd509b61095b19491fda5d481dc8|" +
            "13704d1254514f6006156d332e560|" +
            "caa688ef59cd8ea920cc9fb4cb8d8|" +
            "cc46f02b8d19ef0ef1534262482a8|" +
            "318570abaeb54bb2cd853386a3f88|" +
            "3f743f79a2567606247f4e5318308|" +
            "80ceea82f68553bd5ecd26971eaf0|" +
            "5c1a8017e2643954b5439538667e8|" +
            "aaaaaaaaaaaaaaaaaaaaaaaaaaaa8|" +
            "04631a246e72be371a158d0217170|" +
            "39f93d95ceaf8de8e1e92cc59b8f0|" +
            "114cae20933d0d12bd19bb35cd610|" +
            "a18b5cf753ba6480b9be7b98e99d0|" +
            "a010204de97e5c3545406410ef5c0|" +
            "6eaf559489a395c01cacfdb905a48|" +
            "9e7044154b4dbc2b42333e3f1d498|" +
            "138b93ac4cacaf9a15ade48ae8b88|" +
            "0708d050194a7d29183929037c4c0|" +
            "8bcdd1f5e2b62fc78ebd87f959fe0|" +
            "bc6f8a05923b35134f37a775d0728|" +
            "93d7cffd2cc2dd9e61d1739cc6a40|" +
            "a342c17aff44f4307917fe7e11388|" +
            "a4c86bd91fc53c9f7a8a5aa263d20|" +
            "0e25b5227f58f633915c402475120|" +
            "aaaaaaaaaaaaaaaaaaaaaaaaaaaa8|" +
            "0a150d289b3adc0e9e12670fe5580|" +
            "6bd98d888cea9f84a387abd927b50|" +
            "2165f0009033015f8820557dd4150|" +
            "6185b9f64ebe3f9cae919c9698d70|" +
            "162f5f16cb751f70c23b627a905b0|" +
            "30fcc79538d7cfd1cfd431a80df50|" +
            "76204a38e7111912b45cd04b19020|" +
            "70b02684bbee1ecf7ed280f54bd80|" +
            "eb3178749a11760938527207ee438|" +
            "059f6c8fbddb7fff51c5dce01c9e0|" +
            "1a3c0f3ac146a0021c36ba700d0d0|" +
            "87e876e8b48b2ffbfeb32f84efc10|" +
            "b9206122c715680b930d051f712a0|" +
            "a29248dcef99ebebdb985ad21eda0|" +
            "f324ff21e629ea2b8c0f194fbe628|" +
            "aaaaaaaaaaaaaaaaaaaaaaaaaaaa8|" +
            "207b3049d942ea2a7827b42b310e0|" +
            "f6acc9e78bcb2bea64df7db9ffa10|" +
            "525af4257026680a35677d410f0c0|" +
            "7d867ea8129b6ffb398838b459868|" +
            "7a7819779c1aa0035e220c44dd218|" +
            "92decbfb8bf2bfff23b89683f1f70|" +
            "4859a837dc4c8430f0726a3eb4280|" +
            "1f94cab56989d0c0d58e18e9dfb00|" +
            "3b4e7d2b062cc860c34aab43ea208|" +
            "a4fe41cdfdc071b6fce92effc4d20|" +
            "491a741f1a037a6159374f218d6e8|" +
            "10c7ffe361e953c76ee1adb3e0f90|" +
            "9325e824f7096b1d383ec776431c8|" +
            "31d0fc99cbebba83fb9b9df8998a8|" +
            "0719fd4b5b17443b453aaf70a1568|" +
            "aaaaaaaaaaaaaaaaaaaaaaaaaaaa8|" +
            "7864af75554eb404e22d533e006d8|" +
            "51cb02d6a0d149aba2b6ebf8eb808|" +
            "38307353d436bc24067f8253f9030|" +
            "d59adad1f0b96ecc658d70b2adfa8|" +
            "1d515b493b51580efa37367032298|" +
            "0af9e1c0088d26bd80cb64a63dc68|" +
            "7643904d1755ad083719d5711d160|" +
            "44dfa2a6befae7dbd7cab9e25f880|" +
            "516f124882544138561fbb7790058|" +
            "03d08f80df8dddc750b8578d90c18|" +
            "cc3f4678e4574f05c1660d34923e0|" +
            "cfc4fd83f4e508e5bc9686d7a1e88|" +
            "b12c2c6f9f4951651809914bcc008|" +
            "facedfc0c181e3fe1fdb1992d8a98|" +
            "857c2d7b3f25d676b358836b18018|" +
            "aaaaaaaaaaaaaaaaaaaaaaaaaaaa8|" +
            "4e56722dd466a93abc1ff75f474e8|" +
            "a2e98ab928f3b6df7287208a7eb10|" +
            "511d7b286f0a544b0a506b3036578|" +
            "c2982f940ef408c010d7b28611ed0|" +
            "c738ef5777636e1b45441922d3658|" +
            "4adaa289c9ea9cc4d3bc91ca5fb90|" +
            "641652228f3c272d9b078741ee368|" +
            "2bba76934dc08289a6d74be076bc0|" +
            "d831e22f6c6f742c5a54fe0d10620|" +
            "26e2248c2be52bf730a8cbb975a18|" +
            "4d0662330458b55c1674871af3218|" +
            "45cfd3ad12f889b818a19fbdc3f98|" +
            "2530c550b42d690ada63c16bc7698|" +
            "21c0d0f2b4b5eac6afb46bf27cde0|" +
            "a05e11202c06163eee09b41fbe110|" +
            "aaaaaaaaaaaaaaaaaaaaaaaaaaaa8|" +
            "2c23c22d5b7ad33e5532831b2c270|" +
            "ee898fd1e2b733ee43cda9c491a90|" +
            "c546601e557bb4071f5ee92dfc140|" +
            "41b859ef1fb1c2bb45af61e0d38a0|" +
            "2960bd08696d5f2c39436a4663410|" +
            "00a7a292088faca0b0e595c353838|" +
            "c3354631c0541b50ac1a8550c5608|" +
            "65cac3a9859428d08dccc59e6b8d0"
        },
        {
            "Compact",
            "Compact",
            23,
            0,
            15,
            "0cc2|" +
            "21ca|" +
            "b06e|" +
            "7ff8|" +
            "d012|" +
            "37de|" +
            "b45c|" +
            "155c|" +
            "1458|" +
            "d7dc|" +
            "b010|" +
            "9ffe|" +
            "0f62|" +
            "07e8|" +
            "a888"
        },
        {
            "Compact",
            "Compact",
            23,
            4,
            27,
            "2255212|" +
            "abc4094|" +
            "7c0c1ac|" +
            "db16d4e|" +
            "07f5718|" +
            "be9cea6|" +
            "3783f0c|" +
            "0a73846|" +
            "e2f1200|" +
            "abfffae|" +
            "bbc05e0|" +
            "385f79e|" +
            "ae514fc|" +
            "3fd54ae|" +
            "a9d1668|" +
            "175f6c6|" +
            "86c07ce|" +
            "4f7fe3c|" +
            "642212c|" +
            "4e5dad2|" +
            "2e5bdda|" +
            "6a59260|" +
            "8173e88|" +
            "aa43e10|" +
            "050aa8a|" +
            "82e8076|" +
            "a82efec"
        },
        {
            "Hibc",
            "A123BJC5D6E71",
            23,
            0,
            19,
            "01a6a|" +
            "18180|" +
            "45080|" +
            "b6f5a|" +
            "1d430|" +
            "3ffea|" +
            "3c056|" +
            "95f66|" +
            "e5160|" +
            "c554c|" +
            "2d14a|" +
            "5df72|" +
            "9c05c|" +
            "a7fe2|" +
            "3071e|" +
            "b9504|" +
            "d5b60|" +
            "9d6e6|" +
            "926f2"
        },
    };

    [Theory]
    [MemberData(nameof(MatchesReferenceGridData))]
    public void MatchesReferenceGrid(string kind, string text, int percentage, int layers, int side, string expected)
        => SymbolEncoder.Encode(Create(kind, percentage, layers), text, new BarcodeOptions(), (ref symbol) =>
        {
            Assert.Equal((side, side), (symbol.MatrixRows, symbol.Columns));
            Assert.Equal(expected, SymbolEncoder.ModuleGrid(ref symbol));
        });

    /// <summary>
    /// Annex A of ISO/IEC 24778: a rune is the 11 x 11 Core Symbol of a compact symbol whose mode message
    /// carries the value.
    /// </summary>
    /// <param name="text">The value.</param>
    /// <param name="expected">The reference grid.</param>
    [Theory]
    [InlineData("0", "eaa|ffe|404|df6|514|d56|514|df6|404|7fe|2a8")]
    [InlineData("25", "eca|ffe|406|5f6|514|d56|d16|df4|c06|7fe|240")]
    [InlineData("255", "d52|ffe|406|df6|d16|556|514|df6|c04|7fe|338")]
    public void RuneMatchesReferenceGrid(string text, string expected)
        => SymbolEncoder.Encode(new AztecRuneSymbology(), text, new BarcodeOptions(), (ref symbol) =>
        {
            Assert.Equal((11, 11), (symbol.MatrixRows, symbol.Columns));
            Assert.Equal(expected, SymbolEncoder.ModuleGrid(ref symbol));
        });

    /// <summary>
    /// A rune carries a value of 0 to 255 written in decimal digits.
    /// </summary>
    /// <param name="text">The text.</param>
    [Theory]
    [InlineData("")]
    [InlineData("256")]
    [InlineData("1000")]
    [InlineData("-1")]
    [InlineData("A")]
    public void RuneRejectsValuesOutsideTheRange(string text)
        => Assert.ThrowsAny<ArgumentException>(() => SymbolEncoder.Encode(new AztecRuneSymbology(), text, new BarcodeOptions()));

    /// <summary>
    /// Section 4.1 a) of ISO/IEC 24778: the default interpretation covers the 256 byte values, U+0000 to
    /// U+00FF.
    /// </summary>
    [Fact]
    public void RejectsCharactersAboveLatin1()
        => Assert.Throws<ArgumentException>(() => SymbolEncoder.Encode(new AztecCodeSymbology(), "Ā", new BarcodeOptions()));

    /// <summary>
    /// Section 4.1 c) of ISO/IEC 24778: "No quiet zones are required outside the bounds of the symbol."
    /// </summary>
    [Fact]
    public void HasNoQuietZone()
        => SymbolEncoder.Encode(new AztecCodeSymbology(), "This is Aztec Code", new BarcodeOptions(), static (ref symbol) =>
            Assert.Equal(0F, symbol.LeadingQuietZone));

    /// <summary>
    /// Table 1 of ISO/IEC 24778: a compact symbol holds at most 76 codewords, so data that needs more fills
    /// no compact symbol.
    /// </summary>
    [Fact]
    public void CompactRejectsDataBeyondFourLayers()
        => Assert.Throws<ArgumentException>(
            () => SymbolEncoder.Encode(new AztecCodeCompactSymbology(), new string('a', 200), new BarcodeOptions()));

    /// <summary>
    /// Section 7.2.3 of ISO/IEC 24778: the message length of a compact symbol has 6 bits, "from one up to 64
    /// codewords". Digits take a 5-bit D/L and 4 bits each, so 126 digits fill 509 bits, 64 codewords of 8
    /// bits, and 127 digits fill 513 bits, 65 codewords. At 5 percent the 4-layer compact symbol of 76
    /// codewords has room for the 65 codewords and their 7 check codewords, but the length cannot say 65.
    /// </summary>
    [Fact]
    public void CompactHoldsAtMost64DataCodewords()
    {
        AztecCodeCompactSymbology symbology = new(5);
        SymbolEncoder.Encode(symbology, new string('0', 126), new BarcodeOptions(), static (ref symbol) =>
            Assert.Equal(27, symbol.Columns));

        Assert.Throws<ArgumentException>(() => SymbolEncoder.Encode(symbology, new string('0', 127), new BarcodeOptions()));
    }

    private static BarcodeSymbology Create(string kind, int percentage, int layers)
        => kind switch
        {
            "Auto" => new AztecCodeSymbology(percentage),
            "Full" => new AztecCodeSymbology(percentage, layers),
            "Compact" => layers == 0 ? new AztecCodeCompactSymbology(percentage) : new AztecCodeCompactSymbology(percentage, layers),
            _ => new HibcAztecCodeSymbology(percentage, false),
        };
}
