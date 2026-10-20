// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Barcodes;

namespace SixLabors.ImageSharp.Drawing.Tests.Barcodes;

/// <summary>
/// Encoder tests for <see cref="QrCodeSymbology"/>, <see cref="MicroQrCodeSymbology"/>, <see cref="D3AqrSymbology"/> and the GS1, GS1 Digital Link and HIBC QR
/// Code symbologies. Each expected grid is the module grid of an independent reference implementation, taken through its raw encoding API with the error
/// correction level fixed and Kanji mode off, so the mode segments, the version, the error correction, the module placement, the data mask and the format and
/// version information all show up here. That implementation scores the data masks with placeholders in the format information modules, and section 6.8.2.1
/// evaluates "the complete symbol", so where that changes the mask the expected grid is the reference grid with its data modules masked by the pattern chosen
/// over the complete symbol, and the format information of that pattern. A grid row is the modules from the left as hexadecimal digits, the leftmost module
/// the most significant bit, padded with light modules to whole digits.
/// </summary>
public class QrCodeSymbologyTests
{
    /// <summary>
    /// Gets the cases of <see cref="QrCode_MatchesReferenceGrid"/>.
    /// </summary>
    public static TheoryData<string, bool, QrErrorCorrectionLevel, int, int, string> QrCode_MatchesReferenceGridData { get; } = new()
    {
        {
            "01234567",
            false,
            QrErrorCorrectionLevel.H,
            0,
            21,
            "fe93f8|" +
            "826208|" +
            "ba42e8|" +
            "ba9ae8|" +
            "ba12e8|" +
            "825208|" +
            "feabf8|" +
            "002800|" +
            "2ef448|" +
            "943510|" +
            "1f9b70|" +
            "81c390|" +
            "760108|" +
            "00de10|" +
            "fe2e88|" +
            "82f258|" +
            "ba96e8|" +
            "ba3570|" +
            "bac328|" +
            "8215c0|" +
            "fe3128"
        },
        {
            "AC-42",
            false,
            QrErrorCorrectionLevel.H,
            0,
            21,
            "fe1bf8|" +
            "82c208|" +
            "ba72e8|" +
            "ba5ae8|" +
            "ba2ae8|" +
            "82aa08|" +
            "feabf8|" +
            "00d800|" +
            "0f0b10|" +
            "65f050|" +
            "9b9e78|" +
            "b52900|" +
            "7275c0|" +
            "008f28|" +
            "feffe8|" +
            "82e2c8|" +
            "baea78|" +
            "ba4838|" +
            "ba1240|" +
            "822d48|" +
            "fe15b8"
        },
        {
            "QR Code Symbol",
            false,
            QrErrorCorrectionLevel.M,
            0,
            21,
            "fe9bf8|" +
            "829a08|" +
            "bacae8|" +
            "ba22e8|" +
            "ba9ae8|" +
            "821a08|" +
            "feabf8|" +
            "004800|" +
            "9fd4b8|" +
            "88e238|" +
            "530b58|" +
            "dc85f0|" +
            "7fe2d8|" +
            "0086a8|" +
            "fedaa0|" +
            "82a6e0|" +
            "ba9c70|" +
            "baf260|" +
            "ba4a98|" +
            "821bf8|" +
            "fe9600"
        },
        {
            "Hello, World!",
            false,
            QrErrorCorrectionLevel.L,
            0,
            21,
            "fe5bf8|" +
            "82d208|" +
            "bacae8|" +
            "ba52e8|" +
            "ba8ae8|" +
            "829a08|" +
            "feabf8|" +
            "00f800|" +
            "d363b0|" +
            "80b018|" +
            "7a4968|" +
            "645358|" +
            "3ee880|" +
            "00ff20|" +
            "fefef0|" +
            "825100|" +
            "ba3980|" +
            "ba8ff8|" +
            "ba78a8|" +
            "829400|" +
            "fec350"
        },
        {
            "Hello, World!",
            false,
            QrErrorCorrectionLevel.M,
            0,
            21,
            "fe93f8|" +
            "82c208|" +
            "ba52e8|" +
            "bad2e8|" +
            "ba72e8|" +
            "825a08|" +
            "feabf8|" +
            "00d800|" +
            "b75a58|" +
            "2dcc68|" +
            "ca7518|" +
            "a0f0d0|" +
            "b62b08|" +
            "00bca8|" +
            "fee280|" +
            "82ed70|" +
            "ba65f0|" +
            "ba8c70|" +
            "ba9b20|" +
            "823788|" +
            "febf20"
        },
        {
            "Hello, World!",
            false,
            QrErrorCorrectionLevel.Q,
            0,
            25,
            "fee73f8|" +
            "8237a08|" +
            "ba252e8|" +
            "ba432e8|" +
            "baa1ae8|" +
            "82f8208|" +
            "feaabf8|" +
            "005a800|" +
            "7f10988|" +
            "c8b7520|" +
            "d3d7bd8|" +
            "d945198|" +
            "b3236f8|" +
            "e83d920|" +
            "92965d8|" +
            "a48cb08|" +
            "9798fe0|" +
            "00dd8a8|" +
            "feffab8|" +
            "82978d8|" +
            "bae1ff0|" +
            "ba88728|" +
            "ba97148|" +
            "82c7e48|" +
            "fe3d278"
        },
        {
            "Hello, World!",
            false,
            QrErrorCorrectionLevel.H,
            0,
            25,
            "fe123f8|" +
            "82c4a08|" +
            "bac8ae8|" +
            "baa4ae8|" +
            "baceae8|" +
            "82d6208|" +
            "feaabf8|" +
            "003c000|" +
            "27c4df0|" +
            "c8bc768|" +
            "7a77668|" +
            "c0143d0|" +
            "1632b48|" +
            "4906b68|" +
            "e7b0868|" +
            "084b940|" +
            "f7d6fd0|" +
            "00928e0|" +
            "fed3a88|" +
            "82b0890|" +
            "ba1dfc0|" +
            "ba3d560|" +
            "baf9cf8|" +
            "822cc00|" +
            "fe53fc8"
        },
        {
            "https://sixlabors.com/products/imagesharp-drawing/",
            false,
            QrErrorCorrectionLevel.M,
            0,
            33,
            "feeef53f8|" +
            "828421a08|" +
            "ba38582e8|" +
            "bad39aae8|" +
            "ba2a0aae8|" +
            "82407e208|" +
            "feaaaabf8|" +
            "00e068000|" +
            "b72074258|" +
            "4cd89d368|" +
            "73c2c73d8|" +
            "e16fa8940|" +
            "7239335d8|" +
            "8c90e4d50|" +
            "b3a1842c0|" +
            "8c0ebe7e0|" +
            "72cd3f6e0|" +
            "80d76dad8|" +
            "d3e3cc9b0|" +
            "51269b098|" +
            "aaa635078|" +
            "b58325668|" +
            "120c85bb8|" +
            "44348aad8|" +
            "bfb7d3f80|" +
            "00bdd68c0|" +
            "fe8695a80|" +
            "82cd9d8f8|" +
            "ba5c5ffb0|" +
            "baaa63148|" +
            "baca48360|" +
            "8241f3b88|" +
            "fe8df64e0"
        },
        {
            "ABC123def456GHI789jkl0",
            false,
            QrErrorCorrectionLevel.M,
            0,
            25,
            "fe333f8|" +
            "82b6208|" +
            "ba74ae8|" +
            "ba312e8|" +
            "baef2e8|" +
            "8234208|" +
            "feaabf8|" +
            "0063800|" +
            "aa7f090|" +
            "18c98e8|" +
            "8bf9a88|" +
            "0c13dd0|" +
            "42b6df8|" +
            "3c36180|" +
            "96a27d0|" +
            "5491410|" +
            "ab08ff0|" +
            "00fd8c0|" +
            "fe5baa8|" +
            "820d888|" +
            "bab2fa8|" +
            "ba50250|" +
            "bac5668|" +
            "8210678|" +
            "fee8ee8"
        },
        {
            "123456789012ABCDEFGH123456789012abcdefgh",
            false,
            QrErrorCorrectionLevel.M,
            0,
            29,
            "feb993f8|" +
            "82834a08|" +
            "ba3f5ae8|" +
            "baf0e2e8|" +
            "ba546ae8|" +
            "822a1208|" +
            "feaaabf8|" +
            "00e67800|" +
            "b726ca58|" +
            "bc599ec0|" +
            "47534160|" +
            "a8af73d0|" +
            "3e08e790|" +
            "85246628|" +
            "2f0a5d28|" +
            "70652d88|" +
            "f7600d30|" +
            "48b42af0|" +
            "93a06018|" +
            "3c9195b8|" +
            "6feddff8|" +
            "00e708b0|" +
            "feec0aa0|" +
            "82e0b890|" +
            "ba337ff0|" +
            "ba9e6320|" +
            "bad6d328|" +
            "8223dbc0|" +
            "fe989b60"
        },
        {
            "abc1234567890123def",
            false,
            QrErrorCorrectionLevel.M,
            0,
            25,
            "fef83f8|" +
            "8230a08|" +
            "ba55ae8|" +
            "badbae8|" +
            "baaeae8|" +
            "829f208|" +
            "feaabf8|" +
            "00e2000|" +
            "8b977c8|" +
            "a5d8580|" +
            "cba7c00|" +
            "8c55c78|" +
            "7b7dcc8|" +
            "c046680|" +
            "17a0560|" +
            "21cb460|" +
            "c216fe8|" +
            "00898c0|" +
            "feffae0|" +
            "82758a8|" +
            "bacdfe8|" +
            "ba27228|" +
            "ba61c10|" +
            "820aab0|" +
            "fe96ff8"
        },
        {
            "ABCDEFGHIJ1234567890abcdef",
            false,
            QrErrorCorrectionLevel.Q,
            0,
            29,
            "feb9abf8|" +
            "8266f208|" +
            "ba005ae8|" +
            "ba6dbae8|" +
            "ba9e9ae8|" +
            "82812208|" +
            "feaaabf8|" +
            "00230800|" +
            "7f3e3988|" +
            "90ce84e0|" +
            "9b148e88|" +
            "3d6622d0|" +
            "d6fae4a8|" +
            "20add2d0|" +
            "fbdc4300|" +
            "3d6a77d0|" +
            "82e982c8|" +
            "91412d60|" +
            "8e09f290|" +
            "959b1938|" +
            "a3617f98|" +
            "00a3a898|" +
            "fefe3ab0|" +
            "828b18b0|" +
            "babb3fa8|" +
            "bab1d5f0|" +
            "baca7590|" +
            "82d0f5b8|" +
            "fe1220b0"
        },
        {
            "a1b2c3d4e5f6g7h8i9j0",
            false,
            QrErrorCorrectionLevel.L,
            0,
            25,
            "fe463f8|" +
            "82d9a08|" +
            "ba732e8|" +
            "bad62e8|" +
            "ba2cae8|" +
            "82a5208|" +
            "feaabf8|" +
            "0009000|" +
            "fbecd50|" +
            "49c2440|" +
            "e2d9778|" +
            "80f1758|" +
            "fe506a8|" +
            "e52c950|" +
            "9a47e68|" +
            "894c918|" +
            "8e2bfe8|" +
            "00a08c0|" +
            "fef8ab8|" +
            "82738c8|" +
            "ba91fb8|" +
            "bacebd8|" +
            "baa6428|" +
            "82ede08|" +
            "feaa178"
        },
        {
            "\u00e9\u00e8\u00ea\u00eb\u00e0\u00e2\u00e4\u00f6\u00fc\u00df",
            false,
            QrErrorCorrectionLevel.M,
            0,
            21,
            "fe93f8|" +
            "826a08|" +
            "bac2e8|" +
            "ba7ae8|" +
            "ba4ae8|" +
            "828208|" +
            "feabf8|" +
            "001000|" +
            "a35928|" +
            "5dc5a8|" +
            "9f2460|" +
            "6dae70|" +
            "4e40d0|" +
            "00b6a8|" +
            "fede40|" +
            "8256e8|" +
            "ba7bd0|" +
            "ba66a0|" +
            "bac018|" +
            "824860|" +
            "fec7c8"
        },
        {
            "\u00e0a\u00e1b\u0081@",
            false,
            QrErrorCorrectionLevel.M,
            0,
            21,
            "fe1bf8|" +
            "82fa08|" +
            "ba52e8|" +
            "ba1ae8|" +
            "bafae8|" +
            "827208|" +
            "feabf8|" +
            "004000|" +
            "aa2890|" +
            "451558|" +
            "c63778|" +
            "713db0|" +
            "97b708|" +
            "00e358|" +
            "fe68f0|" +
            "824318|" +
            "baca98|" +
            "ba5550|" +
            "bad788|" +
            "825c70|" +
            "fe96b8"
        },
        {
            "MiXeD cAsE 42 MiXeD $%*+-./:",
            false,
            QrErrorCorrectionLevel.M,
            0,
            25,
            "fe8fbf8|" +
            "825c208|" +
            "ba53ae8|" +
            "ba942e8|" +
            "baeeae8|" +
            "82f6a08|" +
            "feaabf8|" +
            "00c1800|" +
            "8bbd7c8|" +
            "482dab8|" +
            "62dd1e0|" +
            "3121020|" +
            "16d1220|" +
            "b55a8c0|" +
            "2a8bd20|" +
            "3067c20|" +
            "f7d4f90|" +
            "00948c8|" +
            "fecaae0|" +
            "824d8f8|" +
            "bab8fa8|" +
            "ba78b98|" +
            "ba2bb70|" +
            "82481b0|" +
            "fefe3b8"
        },
        {
            Digits(100),
            false,
            QrErrorCorrectionLevel.M,
            0,
            29,
            "fefd7bf8|" +
            "82d17208|" +
            "ba3c72e8|" +
            "baf282e8|" +
            "ba589ae8|" +
            "825f8a08|" +
            "feaaabf8|" +
            "00f39000|" +
            "b70d3a58|" +
            "083539d8|" +
            "dfd07508|" +
            "6515bba0|" +
            "6f0294a0|" +
            "cd51c9a8|" +
            "0a6a0418|" +
            "9850f540|" +
            "17a5fc30|" +
            "28b00fa8|" +
            "8f741028|" +
            "39326280|" +
            "6e73ffe8|" +
            "00dc38b0|" +
            "febb2aa0|" +
            "82cf58b8|" +
            "ba786fd8|" +
            "baf6c450|" +
            "ba9c6828|" +
            "825f6e40|" +
            "fede3bc0"
        },
        {
            Letters(100),
            false,
            QrErrorCorrectionLevel.Q,
            0,
            41,
            "fe594b8ebf8|" +
            "82e1f41ca08|" +
            "ba6666132e8|" +
            "bacd3101ae8|" +
            "ba84f7a1ae8|" +
            "823d22d9a08|" +
            "feaaaaaabf8|" +
            "00a6685c000|" +
            "5ef4fef06d0|" +
            "d841fd0dd48|" +
            "024e5e747b0|" +
            "15737c5f478|" +
            "77b85bcdca8|" +
            "84e3f8014c0|" +
            "36b1c9dfa48|" +
            "9116cb38d18|" +
            "43511ce2490|" +
            "4ddc98b6aa0|" +
            "b2f9e4f12d0|" +
            "94bdef4e2d0|" +
            "0bb26648a50|" +
            "ac0ec558518|" +
            "a38651b0890|" +
            "914e56a7f20|" +
            "7e57ce13118|" +
            "500af4aa0b0|" +
            "664b18ebcd0|" +
            "8ca9013c760|" +
            "83d0e627170|" +
            "c8b773e1558|" +
            "eb17956ad28|" +
            "dc89eaf06b8|" +
            "e6bb4cacfd8|" +
            "00f8ff438f0|" +
            "fe54fed7af0|" +
            "8283934a8d8|" +
            "ba925746fa0|" +
            "baa193b5858|" +
            "ba6c24a2858|" +
            "82d18426060|" +
            "fe7b8ab57f0"
        },
        {
            Lowercase(100),
            false,
            QrErrorCorrectionLevel.H,
            0,
            57,
            "fe2d47bd83a73f8|" +
            "826efe6c64c9208|" +
            "bae8dbbaa25f2e8|" +
            "bae5d11149bd2e8|" +
            "ba598b3e94a92e8|" +
            "823ff5a308ce208|" +
            "feaaaaaaaaaabf8|" +
            "00491f63e8b8800|" +
            "1b1844bf9eea060|" +
            "d071a4b114e3ff0|" +
            "7b124e3ae250d18|" +
            "207a62f38f7fc28|" +
            "27e3228a9eeb4d8|" +
            "04e2c354f3e5af0|" +
            "7b2509233654f80|" +
            "b018ae12ea676f0|" +
            "3ef65469263c688|" +
            "cc4fd0ebcc26c48|" +
            "9aaf8dfe1548008|" +
            "69ec98de86e3cf0|" +
            "42ffa8719ca2688|" +
            "355f39c26777fc0|" +
            "eb3c4069e5978d8|" +
            "2db81b699de9300|" +
            "0fad4e656bae2f8|" +
            "a9a15c317335a68|" +
            "dfb6a7fe47f9fb0|" +
            "18ccac63be858f8|" +
            "0aa864aa7b39ac8|" +
            "68dc1763a43f888|" +
            "7fe0313e48d5f88|" +
            "c877abc6ec810f0|" +
            "43a47b61e8a4b58|" +
            "10c90cc32f661f0|" +
            "eb5acff0601fe78|" +
            "b5955735290b230|" +
            "5664114ccaaae48|" +
            "651b9ba87ee5330|" +
            "a6650cb351815c0|" +
            "711c4a11ea3d7e0|" +
            "8a18980bde4f398|" +
            "e972b9fda9347d8|" +
            "a7090224f3deae8|" +
            "7403438778a5568|" +
            "6f5964e2aee4c10|" +
            "24f8ebec14679e0|" +
            "a6485499b28ef18|" +
            "f847329dbf2e6a8|" +
            "02d0a4bf4ccbf98|" +
            "008ef0a251658d0|" +
            "fef56eea8d11aa0|" +
            "824ee9e25a608f8|" +
            "ba9ca8bf403efd0|" +
            "ba947904922e460|" +
            "ba5825bc15048b8|" +
            "822d572aaa42338|" +
            "fe087fb92e03fe0"
        },
        {
            Digits(300) + Letters(120) + Lowercase(80),
            false,
            QrErrorCorrectionLevel.M,
            0,
            69,
            "fe13800c2185953bf8|" +
            "82491218f218f1c208|" +
            "baa41848a0c0c462e8|" +
            "babe9399529856cae8|" +
            "bab42a40fab58aeae8|" +
            "82f95b7a89b313e208|" +
            "feaaaaaaaaaaaaabf8|" +
            "00f2fc2d8ebe07b000|" +
            "be706870fd64d15be0|" +
            "d1546f9069b8c05a48|" +
            "3b658e20036954eae8|" +
            "7c697a4d7e06d5b7e8|" +
            "bed589b9b33a70e6a0|" +
            "4c14d77be2f601b1d0|" +
            "7317a854e9540b5190|" +
            "8c28d999e859bd7810|" +
            "a3fc9efb1411aa6d88|" +
            "0532a767f81cd94048|" +
            "efd0a99c0d21e83400|" +
            "9152d94ae1556c6258|" +
            "e29a48d060fef0fd30|" +
            "31bec49991df147ab8|" +
            "c2fd06bc66978bef20|" +
            "f41a28e865a9f6c228|" +
            "f6ff076acc18553240|" +
            "d4a150c3d9d00aea70|" +
            "626fc95538b821fd60|" +
            "288153bb1943689a38|" +
            "a276f8ea8d7c3405a0|" +
            "308328793c69fe4220|" +
            "32d5892d44dc153278|" +
            "c86e79e11dcac6ede8|" +
            "ef921594fdb8553fe0|" +
            "d8cd208a8b172948b0|" +
            "fab57b5ea9de550ac0|" +
            "58e64e6d8a4281c8b0|" +
            "8ff53ef5f8af431fe0|" +
            "e93533775ec247eda0|" +
            "be8c82b2a0bd540a28|" +
            "2c28f0b4815e112588|" +
            "1ac35bb6789ff7e718|" +
            "f5550405612781d2c8|" +
            "b718e679b89f417b80|" +
            "9d69b1092ec492b6b0|" +
            "97cf377a5c03a9d5f8|" +
            "ac562d2cccdf1d02c0|" +
            "d769163349b70fddf0|" +
            "ccb65b2d9790fc5cf0|" +
            "027e58f94351a89710|" +
            "29801c3b12e5b0d5c8|" +
            "eb9b1da4e24ac99600|" +
            "dcb744bfdee99408c8|" +
            "e242e0131b4d6bb6a8|" +
            "4d032824978edcae38|" +
            "0ea714cde94f4b91e8|" +
            "34340542a81ce2ca38|" +
            "a74465256520f73f70|" +
            "d8cd2bcbc469121bb8|" +
            "aff3fc0c7d492fb6f0|" +
            "819df520915bea79f8|" +
            "9ba33b7bfe545d7fe8|" +
            "008edbc889fdd2d8a8|" +
            "fe510d25ace78faac0|" +
            "82fc9e6b89b20be8b8|" +
            "ba832bb6f8c440fff0|" +
            "bae0c91b61318b6b30|" +
            "bac4525aa6865d6120|" +
            "82223e8f1243be9430|" +
            "fef4de23ca58354440"
        },
        {
            Lowercase(400),
            false,
            QrErrorCorrectionLevel.L,
            0,
            69,
            "fe24e78d44f8dfbbf8|" +
            "829cf419dbd3a04208|" +
            "ba7ca1691f893ee2e8|" +
            "ba84785fb213c10ae8|" +
            "ba1753a1fd71deaae8|" +
            "82f1f5528b92a8a208|" +
            "feaaaaaaaaaaaaabf8|" +
            "0022c83c8daa798800|" +
            "fb9cfcdefa53831550|" +
            "bc8c512154684f9d88|" +
            "3fd98a26ba033d3290|" +
            "49ee15471bfd7af1f8|" +
            "9f0c795ebc17e75410|" +
            "c53897a41470571ca8|" +
            "9a5b5a99fa8bf93790|" +
            "2c8e02d307995edbe0|" +
            "83dc40172831e35410|" +
            "9d01bbd4b5e9469d68|" +
            "4fbeeddbfbd2a02bf0|" +
            "50d6ff1f17af3fdef0|" +
            "0a8df85ef675a53000|" +
            "d024d3a144e0c71d08|" +
            "b3d4024f9e12a8bb30|" +
            "f9de2e630dec798ff0|" +
            "17337bdfb635c76608|" +
            "1c50d4a01571de9c08|" +
            "6b51b3b6da033d26d0|" +
            "e9b2f0a10bfd1af1e8|" +
            "9a99f95f3a53811210|" +
            "598217a41561ce1da8|" +
            "f6b1ad61ba8be127d0|" +
            "0db265371f993edbf8|" +
            "5f81b07efc77871f80|" +
            "98aa63848c705718e8|" +
            "0ab8f4c2abd2a03a90|" +
            "28df035989af3988e0|" +
            "bf9ff85ff811e35f80|" +
            "c40253a1adf9561a08|" +
            "12e39dbea60328a4f0|" +
            "e559f006bbec799260|" +
            "be167cde8071a12e88|" +
            "71355121b4e8c70c48|" +
            "722c222e430b392cb0|" +
            "c19317757b991af2f0|" +
            "83a7f85e1635c56190|" +
            "cd2417a5a4f8df8068|" +
            "6efc54181393e07530|" +
            "480001591f993ed6e8|" +
            "0227c01f9213c16180|" +
            "a88c3bc1ad61ce8670|" +
            "3ad7e952f392b0a448|" +
            "8d7f5c3dd9ae7997f0|" +
            "1e8ef85e8c57870b80|" +
            "30e053a1ac684f9e08|" +
            "e23a0aa64607392590|" +
            "188d9640dbfc7bf278|" +
            "b3817bde9c17e74790|" +
            "71f894a1b5f1561248|" +
            "af5bbbf8c30a793df0|" +
            "8046c2923f991ed2e0|" +
            "9bbff84ef871a30f90|" +
            "009013a58de94688e8|" +
            "fe8325e3ab92e06af0|" +
            "8278673f8fbf3fd8f0|" +
            "babbb07efe75a53f80|" +
            "ba96638104f8df1180|" +
            "ba96104ea602b0bc20|" +
            "82af206391ec798ce0|" +
            "feacf85f7a31c17f90"
        },
        {
            Digits(2000),
            false,
            QrErrorCorrectionLevel.M,
            0,
            109,
            "fe6ea8d921f7f907d865a68f4bf8|" +
            "82bd3efa77acf66c657a8042ea08|" +
            "ba6d405aeb1de2ea4c4677d8fae8|" +
            "ba5733dc86ee8cc00fcced67f2e8|" +
            "bab108ffa900efddcb6ffe28fae8|" +
            "82007558dbe6c8cd2198f8065208|" +
            "feaaaaaaaaaaaaaaaaaaaaaaabf8|" +
            "004f5a38f76058879108f0a60800|" +
            "aa2b42afba1affb5312fb129a890|" +
            "e4fe97e05433bb37b00a423b26f0|" +
            "2e02d58b2a1b21b018f1be79b880|" +
            "0d566b1231c17f05ff07332c1e58|" +
            "96111e5958fa41e803933576b7d0|" +
            "b4f30a434f13693f9c3b7001a4f8|" +
            "cb74453e3013199b03f7ada1b5a8|" +
            "dd1dce62a7994e223ba33f308c88|" +
            "4680886b58fe93f8a24911176ef8|" +
            "599df5a4729b61359f935f603ef0|" +
            "5f77fd8c712037c7f259e1920fe8|" +
            "f8677d9bccdbb81803e5be05a760|" +
            "b227f554bcf799b9a81a39ae7818|" +
            "245554a51eb3c3391dfbbb3ba4c0|" +
            "d64f1816593dff007f0379b24ea0|" +
            "ace47e45a66813ff8279241681d8|" +
            "aeb0f2e604529d939a87efa5bec0|" +
            "49d2ce0349638195b3ba1936b6d8|" +
            "4a68abd566819c0a678331310280|" +
            "b4cea5d19f56f7bbba5e31946158|" +
            "dfb197df94d35fa35fff87e90fc0|" +
            "a8d28558e8e0788f1908f9b788e8|" +
            "eae8fc1a81da5aebcb9a8a038ab8|" +
            "e88d2858ca51588b6b189dba2890|" +
            "ff83c48fa7576fa181efa779afc0|" +
            "315e03593770168d6f10ab3d0b50|" +
            "4ed2f585b48b90f618b4a3dff848|" +
            "e00e4874035b1e03f94296801f80|" +
            "4ff02da1e917310a03f7b529a1d0|" +
            "2838000a82f92ebbba2d26391f98|" +
            "0f85be41bdb0adf613a4291171e8|" +
            "98f5b875372186b2d1e695f79e48|" +
            "4e4dc45484134225bdb0f90398d8|" +
            "3cba2d56a2d3e896866fa61db9f0|" +
            "aa7d629e2a7a87b534a69b8a7308|" +
            "89e1554ff890d61405d6bb3bb360|" +
            "469a1715651c26e91304f9234a78|" +
            "65043c639596a0b795a5250f09c0|" +
            "a6f666eaa89986839266cfe3dac0|" +
            "84ed5c4d366c743a23b19912f3c0|" +
            "b2346565e5b28299a82631311a78|" +
            "551a0aca0bc390f95ba4390478d0|" +
            "dedd545d3bb104b9b66601ef1e40|" +
            "593d39b486bf7c2232b07f93f348|" +
            "afb06bcfabb02f8b839f821b0fe0|" +
            "28f06b48b561c89d32b89da238d8|" +
            "ba89c90a96b8aa93771a837f8aa0|" +
            "98d8c2d8e633b8b3b258ab3b58d8|" +
            "cff9dd3fd6922fa1a93fffbd3fc0|" +
            "85568b3da350efdbb3a808273ef8|" +
            "874ca13f1efe242e61a8b1299dd0|" +
            "69a8b9da9f3127bbbc03a01d7900|" +
            "7701f9a534b301031bff73531cd0|" +
            "6c99860957305d2b3b2b1b7b0248|" +
            "87fc3167457ee1e79bbc1b018fd8|" +
            "55c5fa8ea9b92e32c069805ffc08|" +
            "ded1b0b220e1ab576bcd3ca03440|" +
            "299ed323639a22001b6fb7b99660|" +
            "4344db23bf37f14b1319390161d8|" +
            "75f01e486fb3ad7391d6056f1ee0|" +
            "026e9b12b31dff196780da1b8ea8|" +
            "e4ebb9ad8ae008661bef73085e40|" +
            "8b265254ac14a099a829911765d8|" +
            "b9a153314507a9f95bba5f602dc0|" +
            "9ade8229c4b1191b6709b8b10768|" +
            "683d3f4121b7e7232b4db91d7048|" +
            "ebbc558804300a0ba5ee11771740|" +
            "60121d308f078d597091f79264c0|" +
            "4ff9187f93282ff3530f838b0fe0|" +
            "58d490c8843bb89b62189db3b898|" +
            "cac6ed6af5303a83890a93770ac0|" +
            "58da6148f3949899d7e8bb3b58e8|" +
            "8ff2905ffbe7afe6192fb25effc8|" +
            "61a95d5b7430bc1be04f87991900|" +
            "56221e78f0113da139ae6b530650|" +
            "94aadcba87b01a4b3b2993f39128|" +
            "42b7782037af68ff9b263901f1f8|" +
            "085bc1007611a22a4174047f9a48|" +
            "b643564082a39bb477abfe912fc0|" +
            "b987ccf14c1c297091a91b795368|" +
            "8a1b1e2b609ffe439b9c03927498|" +
            "c4f6cd681cdb3c728054ba23a160|" +
            "3b81e50c1cfbc3190778ff0335e8|" +
            "bc695b25508618c76923a3496e68|" +
            "132336d8af4db59839a85e7bc1c8|" +
            "e0063931153f3ff14a27110a67c0|" +
            "aad230dad8111cb9b672531301e0|" +
            "64155230cb75fea232affb004e00|" +
            "863b658e4b21918ba5e918f700f8|" +
            "4157474f241f9559709dff1b63c8|" +
            "7b1e021fcd530d7fbd3c80393168|" +
            "3ca56bd660bb2f88273af9c22580|" +
            "ea3adcbfaf343f92980f93fe8ff8|" +
            "00857848c5079881d778b3b3d8f8|" +
            "fe606aca888d2a9ef89a9bbd6ad0|" +
            "8257c6f8a49598f0b688c8611880|" +
            "baf23f1fd3591fb8382ff2da0fd0|" +
            "ba7c28488b8670d2b2a003f309a8|" +
            "bad6e6249ad8912779b493110678|" +
            "8221dce4a95f132b3d17195f67a0|" +
            "fed9131ac401ea3dff3ff618a608"
        },
        {
            Digits(7089),
            false,
            QrErrorCorrectionLevel.L,
            0,
            177,
            "feeb71d1669315cacacac9d51d1d4a42c7c559d9da3f8|" +
            "8280ba55a99c99554d4d418bb3b3b4d45250ba3a3aa08|" +
            "ba364ec919db588dc5c5c2bd9d9dc45c5ed9ddddda2e8|" +
            "ba30015b70919036f6f6e59010104acbce49830301ae8|" +
            "ba9a674f91bcbafb1b1b1fe9a9c9fd44434f8404042e8|" +
            "82cb67c88c33308ecedec89151108ded6cf8919192a08|" +
            "feaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaabf8|" +
            "00b4ca38ae72778f7f7778b7f7978f77f6e8df5f5e800|" +
            "e6ee668fc0ce4dfb73637fe44425fd3dbdafa62627798|" +
            "90b5a597c6141620909898e12141fb6beb740c8e8fc40|" +
            "3addf19f47272637978f9a32527248f9f8fbaeacada68|" +
            "682191a514e0e42991918c96d6b721181909cf49482f0|" +
            "af8f97f72b575330d0d8deecacec1212931aeceaebf48|" +
            "7c84c27c545256b3b3bba76747669d1d9c9def6f6f7f0|" +
            "973c320c544e4d55d5c5da62e2e2bc5ddd5627a7a42e0|" +
            "1caae8953246422b0b1b16ed2d4d59b0b1bef4f2f78a0|" +
            "db0fa6fffdc3c47c848c9e35d5f4d8d9d8dafb7f7af70|" +
            "f9e687ba05edec5810180afabafb0d141516aea8bded0|" +
            "66781839789093a8b0b8b48d6d4d938b8b9ec4c2cd6c8|" +
            "0da9603803dbddb951594caccceccd951592caccdf760|" +
            "e6fd684ce40c0edbe9f9fd50f0f1322babac4d4f44a40|" +
            "f0596a95468e8fb1d3c3d28a8acad9d8d8d5242a376e8|" +
            "aa18536676f6f669a7afac2d6d2cbafb7b6dd24e4a830|" +
            "fdbbf5f91cececc2949c9156567681c8c8da850f06778|" +
            "bbfebe3bb2eaea89011913a6264739a9a9bc262e277e8|" +
            "358b518e2ab2b2b0d8cacccd4d2c93939388ca4c5ece8|" +
            "7b3d53073646469b8d99934242429999998aa4b6a5250|" +
            "91247d835f2f2e9d91859cf2737338585854535f51608|" +
            "bfaed84f9be3e2f9dfdbdfbebfbefc14141fdfcdc3fa8|" +
            "b8f98568a4f4f58a3c3228ef6eee89090908ef6f678f0|" +
            "7a8ee32aca7a7aac22203a838302aa02424ab0aea7ae0|" +
            "b8f735b88ccccc8b393b28c4c4c58bb3b3b8ec74638a8|" +
            "dfdbd07fe6a6a7fa4c485fe2e2e2f971313f880a1ffe0|" +
            "0c2f24a01c040c25b9bdab048484e34b2b34d05850f90|" +
            "8a47c7d36f273673717170f776f677171743ff6f623b8|" +
            "39819018ce464f4951515dececeda515353b8e9e84ec8|" +
            "ae83e66834746c552b292156575769490979776776b60|" +
            "18effdc456f6f66351534faf2fae539bdbd5ea7262ca0|" +
            "cf92578c06263fc5292f2ac5c4c55af2b2a9cc4c5d360|" +
            "49b3a3fada6a6b01a3a5b826a6a7e25a3a3f2a22223e0|" +
            "cef2090e0ee6f7389090977f7efe532b0b53cfc7cf5f0|" +
            "e061e906dc2c2d7e484849e2e3638383a3a92fbfbea48|" +
            "bf16b6961ccccc4b131511eeeeef2151116ef6e6e7bf0|" +
            "a06f4f26f74f4f5ff5f1e3fd7dfd771f5f597df5f4b08|" +
            "03942a731cd4cd4737373dc5c4c55b9b9b80ea6262c60|" +
            "31bb17e799692849090d129697974e9e9ea3e8e8e83e0|" +
            "0afe8eb8e2425349f9fbed252425c7bf9fddcacacaed8|" +
            "68d1d9b8ee761ee8989898e8e968c189a9ba9494955a0|" +
            "4f7e73fc8d3d2d0d8d8d994acacb4111692aaeaeaeb48|" +
            "f4b1bfef9d4d4ccbbbbbb1f6f67771a9d9b576f6f6d38|" +
            "536118c814d4cd055d5d49acac2d85d5958bda5a5aae0|" +
            "e833707c946c3511b1b1aa52d4d5cb1b1b39ef6f6e370|" +
            "52aace7e8c4c4df9c9c9c7dd5d5d058dd595b7b7b70e0|" +
            "6d34e4838ef69fe901010d2dadac094111434acacbc68|" +
            "275ba656693929638b8b0854d4d568b0b0bccc4c4d148|" +
            "2c243b2595edfc2d15158bcecacf615949312cacade90|" +
            "efbc132fd8f0c1fe9e9f1f8d0d08fabab28fd4d4d4fa8|" +
            "28baa20890e8a98d3d3d38aea8ac8d9d9da88242428f0|" +
            "0ac2ba4ab74f4eaafbfafad2d2d0afafafaaa50504ad8|" +
            "58f33568868ece89c8c9c8e565658c9c8498f030318e8|" +
            "1fdaff0faeaecef991911fe46462fa92929fc24243fb0|" +
            "ed395a0acb6b6ab4adadb756d2d50939292ca4c4e4cc8|" +
            "0622e940d464445918990a2424255991918f4a4a6aec8|" +
            "3cb19ee3eab2f309d85944a72124bd959588d454548a8|" +
            "8aa150e1a67e3f1dbd3d24ebebe9514949565cdcbc750|" +
            "bc0dd3fe6f4f4e9b23a2a376f6f31898809b763677640|" +
            "77254cbea7a7c733c3c354be3c3c9c34342ecaca8bc00|" +
            "e5a261583ccced5bb3b3ad565255933b2b2e670746b30|" +
            "e7278d95c26a4afda4a4b1ac2c2d3b13130a20a080440|" +
            "dda6ab33a84041d2fbfa62c04445d2b2b2b0c54444a60|" +
            "f33794560272733fd75745636564ed75756e565736ee8|" +
            "94d479c85c64659535b4a6c6c6c2534b535e682848750|" +
            "f7f95a506f47468ab3131ef575752e96968ed756577b8|" +
            "051d75786eeeee5435151ceaeee99dbdbdbc668746720|" +
            "ca6d03c72b63630bb2b2ab545253db2b2b242425844e0|" +
            "59156d0196a6a79abbda5a6262658da5a5ac6262a2868|" +
            "f7b65248466e6fd9c9094667e7ed8ab2b2ad7cdc5c220|" +
            "a5c0a34e5a4243ada5e48f262632923a3a2e7a3a5a640|" +
            "ffd186d0ddcdcda8f1310a7efefd3b111108ee4e4ed20|" +
            "f073d7c8547474b65f7f4c5fd7c69ff5f5ed5e5f5eed0|" +
            "2295cb8db4cccd5b33336acc4c5515b9b9a1a74646360|" +
            "1946018ffb9392b9b0d08d796161abefeff10e0eaec48|" +
            "4e147bea14242577dfbf87c2424383f9fbfeacac4cc60|" +
            "60fb5fbab96161c9a9a995068e961298988e494889268|" +
            "cfa2803faa5253f8f8b89fb4a4bdfb13170febeaaafc8|" +
            "f8d5f608f4d4d48bdbfbc8ef676e8a9c9898ee6f6f8f0|" +
            "0ab6394ab2cb4bad95d59ad2cac2ad595d5aa52425ae0|" +
            "089c3cc8e544c48b3b3b78c545448bb7b1b8f676f78a8|" +
            "2ff6098fdcc445fcdcbc8fcdc5c4ffdbd9cffa7a7afe0|" +
            "a5825803766f6ed830301152c2d3cc161401ad2cad248|" +
            "739058d6439293a8b8b8a1cd5544338b8f9dc5c4c4248|" +
            "9db2badd1e5e5ec9313143ece4eceb151112cacaca768|" +
            "8bc1d95f708808d1e9c999908880cc2a28354c4d4cc50|" +
            "c940eee1960c8c6bd3b3f0cad2cbb3dbd9d0a42425388|" +
            "e3bdae159f7676afafefb5051d0d46faf8e1d05050120|" +
            "050ae746c4e868f49cdcf0960616cdcfcdd1830303970|" +
            "7320df0f0b6eef01191923ee7667212d2933a42425b58|" +
            "4118d05e7632364acacad3e5656c531316844e4e4e548|" +
            "2b94eb96e0c645e18989b1e26263f89a991626a6a6dd0|" +
            "984bff89f52e2a0d9595a26a3233695b59cd4545451c8|" +
            "7326050998e364f3dbdbd026dedee49392924bcbca1c8|" +
            "6c802b0a1cf074fa3a3a2d672f2fc00a0d09e36362c70|" +
            "b3c0fba87b7efa4c3434300be3e3a343c1d12eaeafa80|" +
            "9d5944092ccf4c2b2b2b29e5456522b337b57676765c8|" +
            "6e218bc94c27a7ca42425162e2e2f031b3390a0a0ab80|" +
            "acad97422d0603b7b7b7b3440444ebaaafb9545455090|" +
            "0a32efd610a324ed756d7f36763636d757d56363623f8|" +
            "95b256538a4642fb4b5b5d6c2c6ccdb4b523848484068|" +
            "3236ab2eb4707669313931177756a1e9e9f2f777769e0|" +
            "9147fe4d1ceeecf9514941ceeeaec3db5bd2727475320|" +
            "ca525a0a5c3636c32b3b348525457bb2b3ad4c4e4f1c0|" +
            "0d104ddbec6c6995b5a5b0460646425b5adba226278a0|" +
            "dfd5fb2f9ce2e6fc9c9c9fbe7e3ffaab2bafc7c5c5ff0|" +
            "58b2e8888c262288585058c262438ba2a338adafaf8e8|" +
            "6ab6cbfaa0d8dea901191acfefefa991109af6f6f6af0|" +
            "78c2dcf88345478dfdedf8ddfdbd8edfde48f9fdfa8c8|" +
            "cff12f8facc8cbfb2b333f84a4c5fb9a9b8fee6a6ffa0|" +
            "9442977d653938551d0d10b7f7b6167f7ee8eaeef1280|" +
            "17f2fda11442471bfbfbf244046407bfbfb2d6d4c2f38|" +
            "1167d7555c16163898908ce8e8c8390989940e8a84440|" +
            "b228b078ed2526059b8b96caeaeb09f1716b2aacb6ce8|" +
            "64a53416a34d4cdba1b1ba76d6969889899c70f4e3738|" +
            "4e4a05b924acac2d49514f2caced55159594464a5fce0|" +
            "b5fa485b88545439bbabb654d4b412bb3b3eef65678f0|" +
            "c75587d7524c4cdbd9d1de5c5c7c2dddddca27a7afb60|" +
            "0ccd53286cf6f63909191cad2d6c312121264edac4ec8|" +
            "63b5d88edd3938ff9b8d9655d5b588d8d8d0d24457468|" +
            "0d8b2fbfabeded410511194ecfcfa111111c3eb2aec70|" +
            "1736c113f4909084989e9f880888f282829ec8c6d5dc8|" +
            "a8e39c3d08c8c931333d36acadadcd9d9d994a4447c70|" +
            "2a78f1bd536f6e40fcfafd50d05017af8f9e8d070fcf8|" +
            "482e6065568e8e3dcbc9dae0e160b49cbcaf3020347c8|" +
            "4e42783bc4f6f73793959ce6e6e6b292f2f44a5e4a030|" +
            "05a95452d3636b30aca8ab56d656a931514a7cf4ef568|" +
            "a319f3bce4444c909a989fa6a626b98989956a6274048|" +
            "b165a6b1ccb2b231595956a2a3a23d9595bb5c444bca8|" +
            "3f6a1ef09c464e11bdbbaf6d6c6c0929491ea4b4a52d0|" +
            "6cdd87366f373edda3a1a6f3f37278c080c6362637540|" +
            "5e9e5b3ab7ffef2945414cbebe3fbc14743e6afaeb4a0|" +
            "35a81d8a96ecfda6b0b2b0d6d657732b4b36e76767550|" +
            "afdbe6afd67262fc22203faeae2efb53330fa0a8a1fe0|" +
            "f88fb028d868708b797968c544458ab292b8dd45458e0|" +
            "9ac7ea1af24a4baf57534ae2e3e3ad35550aa63636ae8|" +
            "f8c82978ec0c058cb0b4b8c1c1408b4b0b58d048498d0|" +
            "bf9dfd6f932f27fb13111ff2f273febe9e8fe77776fb8|" +
            "7d2bd6d51eeeff6d1513056dec6d159d9d8ac74746630|" +
            "4ab98ba971734242b2b4a9565757fb4b2b0e64e4e5d68|" +
            "2d259cdb54bef6ebdbdbd2e7666675bd9dbd626263ae0|" +
            "a72442bc946e272949494862e3e36ada8af15c5c5cb20|" +
            "b86e60ed1e0a436585858d2626262212224afafafbb40|" +
            "ca90d882d7adc47090908b7e7effc10911116f6f6fb30|" +
            "fd23a614c05465e65e5e435fdbdb25e58d853fbfbe2c8|" +
            "273aa0ac84ecdde3333334cc4c4ce1b1b1dae6e6e6df0|" +
            "c5a6579895bbf249d1d1db797d7d77f7d793cececebc0|" +
            "c3c23a6ff86c2427bfbf32c2444563f3f3ed4d4d4cee0|" +
            "493164fc3179610189889e0e8e8ac88890dbe8e8e9de0|" +
            "5bb2719c32627329b9b92d2eacad2717173acacacabd8|" +
            "186b920044d4f5429a9b1aeb6f68089888966f0f0eb78|" +
            "4b9641d78eeacbed1494994ecacaa949414d242425ce8|" +
            "4c2aee21f7650572bbba39494d4d53a3b3bbf6f6f6a38|" +
            "fa014ae8e8c4a4ad1d1d1b43c5c465d5d5d1fa5a5b0e0|" +
            "f086b5271b6f6f7191909cd4d6d36a0a1209ed2d4cb48|" +
            "96b8de4d4bf3f260d95945df5d5da5958d93c5c5c57c8|" +
            "bc5ef5b2f2debf20111101eaeee86911111c8acacade8|" +
            "eeed43dd172969d1292938868283682820352c4c4c9c8|" +
            "d4397355348c8c53b2b33dcececea9c1d1d8e46424288|" +
            "e695376daaf6b74f8f0f0b8f0f0c50f0e0e1705050120|" +
            "104bf66c60e8c8a5bdbcbd040603cbcbdbcda26202cf0|" +
            "5666e10fc96f6ef8f8f87ff67475ff372f3fe52525fd8|" +
            "00aefd08ac36378bca4b48e761658d1515088ecf4e8c8|" +
            "fe15e9aa804444a909091ae46264a890989aa74647ac0|" +
            "82a86628eb2b2b8db4b528aa2e2e8b535b58c4e4a58c8|" +
            "ba0ff7ffc06465fb5b3aafc6c6c4f890908fea4a4afc8|" +
            "ba0f2bb2d27273bb3b3b252723273c0c0c18236282c68|" +
            "ba8b5b9b12feff74b4b427ebefeacb434347efaeaf408|" +
            "82b948a6dd4f4f4baa2b2d65656d22b2b2b676f6f7a50|" +
            "fef5c06aa7a7a6a3034206eaeae4f5353526ea6b4ad98"
        },
        {
            "1234",
            false,
            QrErrorCorrectionLevel.M,
            10,
            57,
            "fe2ea6bc44433f8|" +
            "82c4a214ccc9208|" +
            "ba4940cbbbbb2e8|" +
            "ba19eb6911152e8|" +
            "baa9eb7fbbb92e8|" +
            "820fada31112208|" +
            "feaaaaaaaaaabf8|" +
            "004950e33333000|" +
            "aa6d23bf1110890|" +
            "cc79995dbbbabd0|" +
            "37a45df73332370|" +
            "a83e8f424444c58|" +
            "17e9d4e8eeefef0|" +
            "385ad4a24445438|" +
            "4ad8520aeeefe90|" +
            "b4d2d6b53333320|" +
            "67c9a7b71110928|" +
            "945d8d5dbbbabd0|" +
            "0ba5a5f73332370|" +
            "8d9bd3424444c58|" +
            "32194ae8eeefef0|" +
            "285ebaa24445420|" +
            "42805e0aeeefee8|" +
            "28a26eb53333340|" +
            "e3a19bb71110928|" +
            "588dbf5dbbbabd0|" +
            "8fb1fdbf3332ff0|" +
            "88ab9b2244448d8|" +
            "4aee82aaeeefaf0|" +
            "78e42ae244448a0|" +
            "6f99223eeeeff88|" +
            "3936a4a933334a0|" +
            "12ad5fdd11103a8|" +
            "fd867917bbbb150|" +
            "0748fb9d3332bf8|" +
            "8de29d544445950|" +
            "8fcb96aaeeefce8|" +
            "880222e84444ea0|" +
            "42d9a822eeef408|" +
            "101324a933334a0|" +
            "3eb813dd11103a8|" +
            "e59d5917bbbb150|" +
            "9731f79d3332bb0|" +
            "21e21b544445958|" +
            "4acb8caaeeefcb0|" +
            "8db276e84444ea0|" +
            "a6dbd022eeef408|" +
            "f9a72ca933334a0|" +
            "029103ff1110fa8|" +
            "00c62123bbba8d0|" +
            "fe2bc7ab3332af0|" +
            "82618b2244458d8|" +
            "bac86afeeeeeff0|" +
            "ba2890c24445430|" +
            "ba87d028eeef688|" +
            "824d2c833333c20|" +
            "fe970fab11116a8"
        },
        {
            "1234",
            false,
            QrErrorCorrectionLevel.L,
            40,
            177,
            "feac88f6095ff4920327b7560a98f97fca21859f5a3f8|" +
            "82a192ac7c02a1875ef2ea217fcfae0a9954d84a02a08|" +
            "badce82baea80b2dded868d11d6784a033fe72e0aa2e8|" +
            "bafa0cdb71df5c70a98d1d84c032517c67a305b5d9ae8|" +
            "ba288e7f8e58f2fa0327bfae6a98f97e4faf859f5c2e8|" +
            "82a31528fb85278f5ef2f8f93fcf8e0b18c8d84a02a08|" +
            "feaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaabf8|" +
            "005e6fb8a92f8c8dded868b17d068ca1b268f2e0a8800|" +
            "ce04f0bf8ba527ff5672ef9b5fadff82994ffa4a20178|" +
            "e8ac88f6095ef4d20327ae4e0af878ffce21839f5f758|" +
            "86d86d5b83fcde18a11d04468070507566a321b5fd5f8|" +
            "7958e82baea98b4ddec071511d67052033fe74e0a88a8|" +
            "ea83f2ac8e2123ef566aebfb3fcdae839854fe4a20220|" +
            "e8aa8e761158f292033fb72e6a98797e4fa185995f758|" +
            "86d8eddb8bfad878b10d1d86c03051f4e72327b3fd5f8|" +
            "795a6c2baeaf8d2dc6d868b17d0704a1b27e72e6a8ea8|" +
            "ea87f0ac9625278f4e72fa1b5fadaf829954fa4e20420|" +
            "e8ac0a760158f4d21b27ae4e0af878ffce21839f59158|" +
            "86d969db83fade18a11d04468070507566a321b5fb1f8|" +
            "795bee2bb6af8b4ddec071311d67052033fe74e0ae8a8|" +
            "ea87f2ac862523e9566aeb9b3fcdae839954fe4a24220|" +
            "e8ac0c761958f494033fb74e6a98797e4e2185994f738|" +
            "86d969db83fade7eb10d1dc6c03051f4e6a327b3e5598|" +
            "795bee2bbeaf8b29c6d868b11d0704a1b3fe72e6b0ec8|" +
            "ea87f2ac9625238f4872fa1b3fadaf839954fa4e38460|" +
            "e8ac0c760158f4d21d27ae4e6af878fe4e21838f59158|" +
            "86d969db83fade78a71d0446c0705074e6a321adfb1f8|" +
            "795bee2bb6af8b2ddac071311d070521b3fe74f8ae8a8|" +
            "ef87f2af862523f9566cff9b3fadff83995ffe5224fa0|" +
            "e8ac0c789958f58c0339a8ce6af988fe4e2893994f8b8|" +
            "8ad969ca83fadfaeb11b1ac6c070a814e6bab9b3e4a98|" +
            "78dbee38beaf8a89c6c478b11c078d41b3f8ece6b08c8|" +
            "ef87f2af962523ff486aff9b3e2cffe3995fe64e38fe0|" +
            "e8ac0c6c8158f50a1d3fabce6b78a8be4e34838958258|" +
            "875969d983fade28b71d19c6c1f1807486b621abfa0f8|" +
            "74bbee23b6af8afdc2c074301d07c521d3fb74feaeda8|" +
            "e287eaac062523d14e6ce69abface783f949fe5625920|" +
            "e4ac04649958e50c1b39abcfeaf930fe0e34939948438|" +
            "835931c183fac62eb11b19c740701814e6d639b3e2698|" +
            "7cdbce33beaf92f9c6c474311c065541b39b6ce6b69c8|" +
            "e2a7daac96253bd1486ae69b3e2c7fe39929e64e3d960|" +
            "e0cc4c641148f50c1d3fabce6b78a8be4e74838948438|" +
            "835919b98be2de2eb71d19c6c1f1807486b621abe26d8|" +
            "78abe63bb6b78af9c2c074301d07c521d3fb74feb6988|" +
            "228fe2cc163d23d1486ce69abface783f949fe563d940|" +
            "64ac04759138e50c1d39abcfeaf930fe0e34938948438|" +
            "835139b10b9ac62eb71b19c740701814e6d639abe2698|" +
            "7cd3de6a36cf92f9c2c474311c065541b39b6cfeb69c8|" +
            "82bfda9d16653bd1486ce69b3e2c7fe3992fe6563d960|" +
            "e0dc4c251148950c1d39abce6b78a8be4e72938948438|" +
            "835919b88be2be2eb71b19c6c1f1801486b039abe2698|" +
            "78a3e63a36b7eaf9c2c474361c074541d3ff6cfeb69c8|" +
            "028fe2cc163d63d1486ce69cbe2c67e3e941e6563d960|" +
            "64ac04779138e50c1d39abc9eb78b0be163c938948438|" +
            "835139b10b9ac62eb71b19c341f198149ece39abe2698|" +
            "7cd3de6c36cf92f9c2c474301a074541cb836cfeb69c8|" +
            "afbfda9f96653bf9486cef9ab82cfff3f93fe6563dfe0|" +
            "f8dc4c389148858c1d39b8cfed7888a60e78f389488b8|" +
            "aaf919ba8be2a6aeb71a1ac745f1a80c86cad9abe3a98|" +
            "78c3e638b6b7f389c2c5f8b61c008d59d3988cfeb68c8|" +
            "0fefe2cf963d7bf9486d6f9cbe2affe3e92fa6563dfe0|" +
            "60ac04631128e5d41d383ec9eb7e60be167693e948138|" +
            "8f7159b8eb82c776b61b144341f4c8149edcb9cbe2b98|" +
            "7113fe62b6d79229c34461b01a079d41cb91ec9eb76c8|" +
            "a73fc283b67d3a8149eceb9ab82d3ff3f93966163d660|" +
            "f4bc7c3c112885d41cb9becfed7860a60e69f38928138|" +
            "237939904b82a777b71a144745f0c80c86c359ab82b98|" +
            "7903867956d7f22842c5e1b61c019d59d3860cfed76c8|" +
            "8337a2cbd67d7a80c86d6b9cbe2b3fe3e924a6567d660|" +
            "edb43c73112885d59d383ec9eb7e60be167693e948138|" +
            "c2693988eb82a776b61b144341f4c8149edcb9cbe2b98|" +
            "fd8b867ab6d7f229c34461b61a079d41cb91ec9eb76c8|" +
            "aa37a28bb67d7a8129eceb9cb82d3ff3e93966163d660|" +
            "3cb43c75112885d47cb9bec9ed7860a6166ff38828128|" +
            "e2693989cb82a777d71a144345f0a80c9ec559aa02b80|" +
            "fc0b8678d6d7f22802c5e1b61a01fd59cb900cff576c0|" +
            "9a37a28b567d7a80c80d6b9cb82b5ff3e938a657fd678|" +
            "2c343c73112885d59d583ec9ed7e20a6166ef2e948138|" +
            "d2693989eb82a776b67a144345f4ce0c9ec4d84be2b98|" +
            "ec0b867ab6d7f229c305e1b61a119b59cb918d1eb76c8|" +
            "9b37a28b367d7a8129ed0b9cb83339f3e93927963d660|" +
            "2cb43c77112885d47cb85ec9ed6664a6166ef3e828128|" +
            "d269398fcb82a777d61a744345eca80a9ec4d9ca02b80|" +
            "ec0b867ad6d7f2280345a1b60a01fd5fcb918c9f566d0|" +
            "9fb7a30fd67d7af8c98d6f9ca02bfff5e93fa617fcff8|" +
            "28b43c78f128858d9cd828c9f57e88a21678f2e9288b8|" +
            "daed388a8b82a6afb67a0ac35df5ae0c98dad84b82a98|" +
            "e88b8778b6d7f2884305f8a61a118b59cd888d1ed78c8|" +
            "9fb0a29f967d7af8a9ed0f84b833f9f3ef3fa7967cfe0|" +
            "28363d6551298585fcb843d1ed665ca6127173e828e28|" +
            "d3ee3a1ecb8326afd61a715b45ed580a9ec159ca03e80|" +
            "e00c8065d6d672700345bc360a01cd5fcb8c8c9f563d0|" +
            "93b0a30d767cfb50a98d6e1ca02ae7f5e93e2617fd178|" +
            "29353a6156288585fcd823c9f57e38a2167172e829638|" +
            "d36d3b9ecc02a62fd67a11435df51e0c98c1584a03798|" +
            "e00b0565d157f2f00305fc261a11ab59cd8c8d1f57bc8|" +
            "93b0a48d73fd7ad0a98d0e04b83281f3ef3e2797fc860|" +
            "28363d61512f8405fcd843d1ed665ca6127172e828e28|" +
            "d3ee3a1ecb8527afd67a715b45ed5e0a9ec1584a03e80|" +
            "e00c0065d6d072700305bc560a11ab5fcb8c8d1f563d0|" +
            "93b0a30d7678fb50a98d087ca03281f5e83e2797fd178|" +
            "29353a6156288385fcd845a9f5665ca217f172e829628|" +
            "d3ed3b9ecc02a02fd67a77035ded5e0a9941584a03780|" +
            "e08b0565d157f4e00305b8266a11ab5fcc0c8d1f57a50|" +
            "9330a48d73fd7ec8a98d0e04c03281f4ef3e2797fc8f8|" +
            "29363d61512f841dfcd843d195665ca3927172e8287a8|" +
            "d3ee3a1ecb8527b7d67a715b1ded5e0b18c1584a03680|" +
            "e08c0065d6d072700305bc460a71ab5e4d8c8d1f57bd0|" +
            "9330a30d7678fb50a98d0864a05280f5ee3e2797fc978|" +
            "29353a6156298385fcd845b1f5065d2213f174e828628|" +
            "d3ef3b9ecc03202fd66a771b5dad5f8a99415e4a03780|" +
            "e08d0565d15674e0031db8266a11aadfcc0c8b1f57a50|" +
            "9fb0a48ff3fcfef8a9950f84c032f9f4ef3fa397fcff8|" +
            "28b23d78ce2f858dfcc048d195678ca39278f2ee288a8|" +
            "daec3f1ad40527afc67a7adb1dedae0b18cad84c03a80|" +
            "e88c0778c95073881b05a8c60a718b5e4d988d19568d0|" +
            "9fb0208ff3f8faf8b18d0fe4a052f8f5ee3fa793fcff8|" +
            "2132bd6d482f83cde4d84e31f507052213fe74e82e8a8|" +
            "d6ec390eda0521e7d66a741b5dada78a9956de4a04000|" +
            "e18c856ec75074b0031da9466a10dadfcc010b1f516d0|" +
            "9f3022827ff8fe0ea99503e4c03251f4ee2b2397f9c78|" +
            "2132bb6d562f83d3fcc05031956704a393fe72ee38948|" +
            "d6ec390ecc0521f9c67a621b1deda60b1956d84c1a1e0|" +
            "e18c856ec95074ac1b05b7466a70db5e4c010d194f7b0|" +
            "9f30228273f8fe18b78d1fe4c05250f4ee2b2793e5db8|" +
            "2132bb6d482f83cde2d848319507052393fe74f82e8a8|" +
            "d6ec390eda052187d06a721b1dada78b1956de5204000|" +
            "e18c856ec75074d0071daf466a70dade4c010b07517d0|" +
            "9f3022827ff8fe7ea99307e4c05250f4ee2b238ff9df8|" +
            "2132bb6d562f838bfcc651319507052393fe64ee388c8|" +
            "d6ec390ecc0521e1c66c639b1dada7eb1956c64c1a060|" +
            "e18c856ec95074b41b19b6c66b70dabe4c0113194f7b0|" +
            "9f30228273f8fe18b7951e64c1d25094ee2b3b93e5db8|" +
            "2132bb6d482f83cde2c048319487056393fe74fe2e8a8|" +
            "d6cc390eda052187c06a721b1c2da78b7956de5404000|" +
            "e1cc856ec75074d01f1daf476a70dade2c010b01511d0|" +
            "9f303a827ff8fe7eb19307e5405250f48e2b238bf9bf8|" +
            "2112bb6d562f938be4c6513015070523d3fe64ee3eec8|" +
            "d68c490ec80539e1c66c739a9dada7eb1936c64c1c460|" +
            "e1ac956ec9506cb41b19aec66b71dabe4c611319497b0|" +
            "9f880a8ff7f8e6feb7950fe4c1d3f894ee4fbb93e1fb8|" +
            "28aaa368ca3f828be2c058b194878d6393b8f4fe3e888|" +
            "dac4691aca1d20a9c06a6a9b1c2daf8b794ade541daa0|" +
            "e8d4fd28df48748c1f1da8c76a708ade2c088b0149890|" +
            "1fa83adfffe0fefeb7930fe54053f8f48e2fa38be1fb8|" +
            "ed1ad32a4a4f929be2c646301507d523d3fb64fe3edc8|" +
            "1e84517aca6539b1c06c679a9dadffeb1929c6541df60|" +
            "24ac955edf306d041f19aa466b7122be4c66930149030|" +
            "ae000af97fb8e72eb79318e4c1d22094ee56bb8be0638|" +
            "cd3aa3624a3fe2fbe2c647b19486556393bb64fe3ebc8|" +
            "fed4691aca1d41f1c06c669b1c2d7feb7949c6541d960|" +
            "e4dcfd2edf4815041f19abc16b70a2be2c06930149430|" +
            "1e283ac97fe0bf2eb793196341d3a0949e36bb8be0038|" +
            "fd1ad3224a4f92fbe2c646361487d563cbfb64fe3edc8|" +
            "1e84517aca6539f1c06c669e9c2dffeb6129c6541df60|" +
            "24ac9546df306d041f19abc76d7132be3466930149030|" +
            "96000ae17fb8e72eb793196547d238848e56bb8be0638|" +
            "f13aa3724a3ff2fbe2c6463012864d7bd3bb04fe3ebc8|" +
            "daf4691aca1d59f1c06d669a982d67f37929a6541d960|" +
            "c0fcfd2edf480d041f182bc16b76a2a62c66f30149430|" +
            "1e483ac9ffe0a72eb792996341d5a0949e56fb8be0638|" +
            "f11ad3220a5f92fbe2c7c63614815563cbbb649e3ebc8|" +
            "1ac4517a4a7d39f1c16c669e9c297feb6121c6341d960|" +
            "20cc95465f286d041e99abc76d70b2be346e936149430|" +
            "962052e0bfa0e72eb613196547d3b8848e4ebbcbe0638|" +
            "f17ab373aa5ff2fbe346463012874d7bd3a304fe5ebc8|" +
            "e6d4093aca7d59f0c06d669a982d67f37939a6547d960|" +
            "10dcbd2eff280d059f182bc16b76a2a62c66f30129430|" +
            "563812efdfa0a7ff37929fe341d5f8949e5ffb8ba0fb8|" +
            "00f2b3388a5ff28a62c7c8b614818d63cba8e49e3f8c8|" +
            "fe44091aca7d59a9c16c7a9e9c28afeb612ac6341dae0|" +
            "82c4bd18df280c8c1e99a8c16d718abe34689361498b0|" +
            "bab8128fbfa0a6fed6131fe347d2f8849e5fbbcbe0fb8|" +
            "ba72b3692a5ff22b834641361287a57bcbb884ff5fe58|" +
            "ba44090cca7d5800a06d699e982d2ff3613aa655fd460|" +
            "82c4bd12ff280df5df1836c16d767aa6346ff300a8130|" +
            "feb81281dfa0a77f37f2946347d448849e457b8a20bb8"
        },
        {
            "12345",
            true,
            QrErrorCorrectionLevel.L,
            0,
            11,
            "fea|" +
            "82c|" +
            "ba8|" +
            "ba0|" +
            "bae|" +
            "826|" +
            "fe8|" +
            "006|" +
            "ce6|" +
            "518|" +
            "f06"
        },
        {
            "0123456789012345",
            true,
            QrErrorCorrectionLevel.M,
            0,
            15,
            "feaa|" +
            "8216|" +
            "baee|" +
            "bae6|" +
            "baa4|" +
            "82f2|" +
            "fe66|" +
            "0082|" +
            "869a|" +
            "0a88|" +
            "9d76|" +
            "5010|" +
            "8fc6|" +
            "2ab2|" +
            "fcee"
        },
        {
            "ABC",
            true,
            QrErrorCorrectionLevel.L,
            0,
            13,
            "fea8|" +
            "8200|" +
            "baf8|" +
            "ba88|" +
            "bae8|" +
            "8268|" +
            "fed0|" +
            "0070|" +
            "d5f0|" +
            "2e78|" +
            "e1b8|" +
            "5718|" +
            "d150"
        },
        {
            "ABC123",
            true,
            QrErrorCorrectionLevel.M,
            0,
            15,
            "feaa|" +
            "82aa|" +
            "ba56|" +
            "ba7c|" +
            "baf6|" +
            "8224|" +
            "feba|" +
            "0092|" +
            "83ea|" +
            "3e0c|" +
            "d2f6|" +
            "3828|" +
            "c904|" +
            "1452|" +
            "b874"
        },
        {
            "abc",
            true,
            QrErrorCorrectionLevel.L,
            0,
            15,
            "feaa|" +
            "8216|" +
            "baee|" +
            "baae|" +
            "ba38|" +
            "8298|" +
            "fe1c|" +
            "0052|" +
            "fc60|" +
            "2672|" +
            "cbd8|" +
            "1c3c|" +
            "f388|" +
            "78ae|" +
            "911e"
        },
        {
            "Micro QR!",
            true,
            QrErrorCorrectionLevel.L,
            0,
            15,
            "feaa|" +
            "8216|" +
            "bafe|" +
            "bab6|" +
            "ba1e|" +
            "82b2|" +
            "fe4c|" +
            "0002|" +
            "fc50|" +
            "68b4|" +
            "cf1a|" +
            "4ef4|" +
            "b73a|" +
            "5a8c|" +
            "c3be"
        },
        {
            Digits(35),
            true,
            QrErrorCorrectionLevel.L,
            0,
            17,
            "feaa8|" +
            "829b0|" +
            "baa40|" +
            "ba148|" +
            "bad68|" +
            "828e0|" +
            "fe470|" +
            "00b60|" +
            "9d6f0|" +
            "4a298|" +
            "ea8f0|" +
            "18868|" +
            "d1630|" +
            "3f230|" +
            "c3f18|" +
            "4a938|" +
            "94570"
        },
        {
            Letters(13),
            true,
            QrErrorCorrectionLevel.Q,
            0,
            17,
            "feaa8|" +
            "823c8|" +
            "baf98|" +
            "ba458|" +
            "badc8|" +
            "82dd0|" +
            "fec80|" +
            "000f0|" +
            "bbba0|" +
            "12fc0|" +
            "ce7a8|" +
            "18d28|" +
            "b2588|" +
            "657c8|" +
            "9ecf0|" +
            "397f8|" +
            "a7740"
        },
        {
            "1",
            true,
            QrErrorCorrectionLevel.L,
            1,
            11,
            "fea|" +
            "828|" +
            "ba6|" +
            "ba8|" +
            "ba6|" +
            "820|" +
            "fe6|" +
            "00a|" +
            "c46|" +
            "5fa|" +
            "ff6"
        },
        {
            "1",
            true,
            QrErrorCorrectionLevel.M,
            2,
            13,
            "fea8|" +
            "8208|" +
            "ba28|" +
            "baf8|" +
            "ba50|" +
            "8248|" +
            "fef0|" +
            "0010|" +
            "e2a0|" +
            "2728|" +
            "dff8|" +
            "6d50|" +
            "9aa8"
        },
        {
            "1",
            true,
            QrErrorCorrectionLevel.L,
            3,
            15,
            "feaa|" +
            "8214|" +
            "baea|" +
            "baaa|" +
            "ba2a|" +
            "8298|" +
            "fe0e|" +
            "0056|" +
            "fc64|" +
            "7af0|" +
            "c6da|" +
            "1aba|" +
            "d40e|" +
            "13ae|" +
            "8f9c"
        },
        {
            "1",
            true,
            QrErrorCorrectionLevel.Q,
            4,
            17,
            "feaa8|" +
            "82860|" +
            "ba600|" +
            "bada0|" +
            "bad68|" +
            "82568|" +
            "fe058|" +
            "00580|" +
            "bee58|" +
            "2b6a8|" +
            "d6278|" +
            "69108|" +
            "ce830|" +
            "52980|" +
            "f2cf8|" +
            "4d3c8|" +
            "dcfb0"
        },
    };

    [Theory]
    [MemberData(nameof(QrCode_MatchesReferenceGridData))]
    public void QrCode_MatchesReferenceGrid(string text, bool micro, QrErrorCorrectionLevel level, int version, int size, string expected)
    {
        BarcodeSymbology symbology = micro
            ? version == 0 ? new MicroQrCodeSymbology(level) : new MicroQrCodeSymbology(level, version)
            : version == 0 ? new QrCodeSymbology(level) : new QrCodeSymbology(level, version);

        SymbolEncoder.Encode(symbology, text, new BarcodeOptions(), (ref symbol) =>
        {
            Assert.Equal((size, size), (symbol.MatrixRows, symbol.Columns));
            Assert.Equal(expected, SymbolEncoder.ModuleGrid(ref symbol));
        });
    }

    /// <summary>
    /// Section 5.3.7 of ISO/IEC 18004: the quiet zone is 4X wide around a QR Code symbol and 2X wide around a Micro QR Code symbol.
    /// </summary>
    [Fact]
    public void QuietZoneFollowsSectionFiveThreeSeven()
    {
        Assert.Equal(4F, SymbolEncoder.Read(new QrCodeSymbology(), "1234", new BarcodeOptions(), static (ref symbol) => symbol.LeadingQuietZone));
        Assert.Equal(2F, SymbolEncoder.Read(new MicroQrCodeSymbology(), "1234", new BarcodeOptions(), static (ref symbol) => symbol.LeadingQuietZone));
    }

    /// <summary>
    /// Table 7 of ISO/IEC 18004: a version 40-L symbol holds 7089 digits, the most any symbol holds.
    /// </summary>
    [Fact]
    public void QrCode_RejectsDataLargerThanTheLargestSymbol()
        => Assert.Throws<ArgumentOutOfRangeException>(() => Encode(new QrCodeSymbology(QrErrorCorrectionLevel.L), Digits(7090)));

    /// <summary>
    /// Table 7: a version 1-H symbol holds 17 digits, so 18 digits do not fit it.
    /// </summary>
    [Fact]
    public void QrCode_RejectsDataLargerThanTheFixedVersion()
        => Assert.Throws<ArgumentException>(() => Encode(new QrCodeSymbology(QrErrorCorrectionLevel.H, 1), Digits(18)));

    /// <summary>
    /// Section 6.3.4: Byte mode carries ISO/IEC 8859-1, so a character above U+00FF is rejected.
    /// </summary>
    [Fact]
    public void QrCode_RejectsCharactersAboveTheByteRange() => Assert.Throws<ArgumentException>(() => Encode(new QrCodeSymbology(), "Ā"));

    /// <summary>
    /// Sections 6.3.3 and 6.3.4: version M1 holds digits only, and M2 has no Byte mode.
    /// </summary>
    [Fact]
    public void MicroQrCode_RejectsModesTheVersionDoesNotHave()
    {
        Assert.Throws<ArgumentException>(() => Encode(new MicroQrCodeSymbology(QrErrorCorrectionLevel.L, 1), "A"));
        Assert.Throws<ArgumentException>(() => Encode(new MicroQrCodeSymbology(QrErrorCorrectionLevel.L, 2), "a"));
    }

    /// <summary>
    /// Section 6.5.1 and Table 13: Micro QR Code has no level H, M1 has error detection only and only M4 has level Q.
    /// </summary>
    [Fact]
    public void MicroQrCode_RejectsLevelsTheVersionDoesNotHave()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new MicroQrCodeSymbology(QrErrorCorrectionLevel.H));
        Assert.Throws<ArgumentException>(() => new MicroQrCodeSymbology(QrErrorCorrectionLevel.M, 1));
        Assert.Throws<ArgumentException>(() => new MicroQrCodeSymbology(QrErrorCorrectionLevel.Q, 3));
    }

    /// <summary>
    /// Gets the cases of <see cref="D3Aqr_MatchesReferenceGrid"/>: every host ratio, hosts small enough to widen the offset and large enough not to, and
    /// categories with each arm empty, full, and of odd and even parity.
    /// </summary>
    public static TheoryData<string, int, D3AqrHostRatio, int, string> D3Aqr_MatchesReferenceGridData { get; } = new()
    {
        {
            "HTTPS://ID.GS1.ORG/01/09521234543213/22/ABC%2D123?99=XYZ-987",
            2842,
            D3AqrHostRatio.Two,
            41,
            "00000000000|" +
            "00000000000|" +
            "3f97962fe00|" +
            "20b4cbe8200|" +
            "2e81f8cba00|" +
            "2e8dbeaba00|" +
            "2ea6ab4ba60|" +
            "209876c8260|" +
            "3faaaaafe18|" +
            "00115520018|" +
            "2a96aa82460|" +
            "020b3bbba60|" +
            "14b6a8f4218|" +
            "2433bf60418|" +
            "19c0eaec460|" +
            "3a0bef5c660|" +
            "1086ff36878|" +
            "3573331f078|" +
            "2ec366fca00|" +
            "250d6664200|" +
            "16bc910d818|" +
            "2b4b4674418|" +
            "1eda846ca00|" +
            "0b42d56c200|" +
            "22b0084ea00|" +
            "1b63452f200|" +
            "2cc7a2ff060|" +
            "00271f22660|" +
            "3f892cea400|" +
            "20929be3400|" +
            "2ebf2aff460|" +
            "2e926eb8c60|" +
            "2ea67775200|" +
            "208ebb69600|" +
            "3fbffcdd260|" +
            "00000000060|" +
            "00000000060|" +
            "003cc0333e0|" +
            "003cc0333e0|" +
            "00cccc00000|" +
            "00cccc00000"
        },
        {
            "HTTPS://ID.GS1.ORG/01/09521234543213/22/ABC%2D123?99=XYZ-987",
            2842,
            D3AqrHostRatio.TwoAndAHalf,
            175,
            "000000000000000000000000000000000000001ff800|" +
            "000000000000000000000000000000000000001ff800|" +
            "000000000000000000000000000000000000001ff800|" +
            "000000000000000000000000000000000000001ff800|" +
            "000000000000000000000000000000000000001ff800|" +
            "000000000000000000000000000000000000001ff800|" +
            "000000000000000000000000000000000000001ff800|" +
            "000000000000000000000000000000000000001ff800|" +
            "000000000000000000000000000000000000001ff800|" +
            "000000000000000000000000000000000000001ff800|" +
            "000000000000000000000000000000000000000007fe|" +
            "000000000000000000000000000000000000000007fe|" +
            "000000000000000000000000000000000000000007fe|" +
            "0007ffffff80787fff80787f800787ffffff800007fe|" +
            "0007ffffff80787fff80787f800787ffffff800007fe|" +
            "0007ffffff80787fff80787f800787ffffff800007fe|" +
            "0007ffffff80787fff80787f800787ffffff800007fe|" +
            "000780000787f87807f80787ffff87800007800007fe|" +
            "000780000787f87807f80787ffff87800007800007fe|" +
            "000780000787f87807f80787ffff87800007800007fe|" +
            "000780000787f87807f80787ffff87800007801ff800|" +
            "000787ff878000007fffff8007f80787ff87801ff800|" +
            "000787ff878000007fffff8007f80787ff87801ff800|" +
            "000787ff878000007fffff8007f80787ff87801ff800|" +
            "000787ff878000007fffff8007f80787ff87801ff800|" +
            "000787ff878007f87f87ffff87878787ff87801ff800|" +
            "000787ff878007f87f87ffff87878787ff87801ff800|" +
            "000787ff878007f87f87ffff87878787ff87801ff800|" +
            "000787ff878007f87f87ffff87878787ff87801ff800|" +
            "000787ff8787807f87878787f8780787ff87801ff800|" +
            "000787ff8787807f87878787f8780787ff87800007fe|" +
            "000787ff8787807f87878787f8780787ff87800007fe|" +
            "000787ff8787807f87878787f8780787ff87800007fe|" +
            "0007800007807f80007ff87f87f807800007800007fe|" +
            "0007800007807f80007ff87f87f807800007800007fe|" +
            "0007800007807f80007ff87f87f807800007800007fe|" +
            "0007800007807f80007ff87f87f807800007800007fe|" +
            "0007ffffff87878787878787878787ffffff800007fe|" +
            "0007ffffff87878787878787878787ffffff800007fe|" +
            "0007ffffff87878787878787878787ffffff800007fe|" +
            "0007ffffff87878787878787878787ffffff801ff800|" +
            "000000000000780078787878780780000000001ff800|" +
            "000000000000780078787878780780000000001ff800|" +
            "000000000000780078787878780780000000001ff800|" +
            "000000000000780078787878780780000000001ff800|" +
            "000787878780787f87878787878000078078001ff800|" +
            "000787878780787f87878787878000078078001ff800|" +
            "000787878780787f87878787878000078078001ff800|" +
            "000787878780787f87878787878000078078001ff800|" +
            "0000000780000787f807ff87ff87ff87ff87801ff800|" +
            "0000000780000787f807ff87ff87ff87ff87801ffffe|" +
            "0000000780000787f807ff87ff87ff87ff87801ffffe|" +
            "0000000780000787f807ff87ff87ff87ff87801ffffe|" +
            "000078780787f87f8787878007fff8780007801ffffe|" +
            "000078780787f87f8787878007fff8780007801ffffe|" +
            "000078780787f87f8787878007fff8780007801ffffe|" +
            "000078780787f87f8787878007fff8780007801ffffe|" +
            "000780780007f807ff87fffff87f80000078001ffffe|" +
            "000780780007f807ff87fffff87f80000078001ffffe|" +
            "000780780007f807ff87fffff87f80000078001ffffe|" +
            "000780780007f807ff87fffff87f8000007800000000|" +
            "00007f807ff8000007ff878787ff87f8007800000000|" +
            "00007f807ff8000007ff878787ff87f8007800000000|" +
            "00007f807ff8000007ff878787ff87f8007800000000|" +
            "00007f807ff8000007ff878787ff87f8007800000000|" +
            "0007ff8780000787ffff87fff8787ff8007f80000000|" +
            "0007ff8780000787ffff87fff8787ff8007f80000000|" +
            "0007ff8780000787ffff87fff8787ff8007f80000000|" +
            "0007ff8780000787ffff87fff8787ff8007f80000000|" +
            "000078000780007f87fffffff807f87f878000000000|" +
            "000078000780007f87fffffff807f87f8780000007fe|" +
            "000078000780007f87fffffff807f87f8780000007fe|" +
            "000078000780007f87fffffff807f87f8780000007fe|" +
            "0007f878787ff807f807f807f8007ffff800000007fe|" +
            "0007f878787ff807f807f807f8007ffff800000007fe|" +
            "0007f878787ff807f807f807f8007ffff800000007fe|" +
            "0007f878787ff807f807f807f8007ffff800000007fe|" +
            "000787ff87f80007f87f807f87fffff80787800007fe|" +
            "000787ff87f80007f87f807f87fffff80787800007fe|" +
            "000787ff87f80007f87f807f87fffff80787800007fe|" +
            "000787ff87f80007f87f807f87fffff8078780000000|" +
            "00078078780007f8787f807f807f8078000780000000|" +
            "00078078780007f8787f807f807f8078000780000000|" +
            "00078078780007f8787f807f807f8078000780000000|" +
            "00078078780007f8787f807f807f8078000780000000|" +
            "0000787f8787fff807807800780007f87f8000000000|" +
            "0000787f8787fff807807800780007f87f8000000000|" +
            "0000787f8787fff807807800780007f87f8000000000|" +
            "0000787f8787fff807807800780007f87f8000000000|" +
            "00078787f8780787f878007f807ff878007800000000|" +
            "00078787f8780787f878007f807ff878007800000000|" +
            "00078787f8780787f878007f807ff878007800000000|" +
            "00078787f8780787f878007f807ff878007800000000|" +
            "00007fff87f87f8787800078007f87f8078780000000|" +
            "00007fff87f87f8787800078007f87f8078780000000|" +
            "00007fff87f87f8787800078007f87f8078780000000|" +
            "00007fff87f87f8787800078007f87f8078780000000|" +
            "00000787f878000787f87878787f87f8000780000000|" +
            "00000787f878000787f87878787f87f8000780000000|" +
            "00000787f878000787f87878787f87f8000780000000|" +
            "00000787f878000787f87878787f87f80007801ff800|" +
            "000780078787f80000000780007807ff8787801ff800|" +
            "000780078787f80000000780007807ff8787801ff800|" +
            "000780078787f80000000780007807ff8787801ff800|" +
            "000780078787f80000000780007807ff8787801ff800|" +
            "00007f87f87f8007f8780078780787fff807801ff800|" +
            "00007f87f87f8007f8780078780787fff807801ff800|" +
            "00007f87f87f8007f8780078780787fff807801ff800|" +
            "00007f87f87f8007f8780078780787fff807801ff800|" +
            "000787f807f8007fff87800787fffffff800001ff800|" +
            "000787f807f8007fff87800787fffffff80000000000|" +
            "000787f807f8007fff87800787fffffff80000000000|" +
            "000787f807f8007fff87800787fffffff80000000000|" +
            "000000000007807ff8007ffff8078007807f80000000|" +
            "000000000007807ff8007ffff8078007807f80000000|" +
            "000000000007807ff8007ffff8078007807f80000000|" +
            "000000000007807ff8007ffff8078007807f80000000|" +
            "0007ffffff800780780787f807ff8787807800000000|" +
            "0007ffffff800780780787f807ff8787807800000000|" +
            "0007ffffff800780780787f807ff8787807800000000|" +
            "0007ffffff800780780787f807ff87878078001ff800|" +
            "000780000780780787807f87ffff8007f878001ff800|" +
            "000780000780780787807f87ffff8007f878001ff800|" +
            "000780000780780787807f87ffff8007f878001ff800|" +
            "000780000780780787807f87ffff8007f878001ff800|" +
            "000787ff8787fffff807878787fffffff878001ff800|" +
            "000787ff8787fffff807878787fffffff878001ff800|" +
            "000787ff8787fffff807878787fffffff878001ff800|" +
            "000787ff8787fffff807878787fffffff878001ff800|" +
            "000787ff87807807807f87ff8787ff8007f8001ff800|" +
            "000787ff87807807807f87ff8787ff8007f800000000|" +
            "000787ff87807807807f87ff8787ff8007f800000000|" +
            "000787ff87807807807f87ff8787ff8007f800000000|" +
            "000787ff8787807f807ff87ff87ff878780780000000|" +
            "000787ff8787807f807ff87ff87ff878780780000000|" +
            "000787ff8787807f807ff87ff87ff878780780000000|" +
            "000787ff8787807f807ff87ff87ff878780780000000|" +
            "00078000078007ff8787ff87f87f8780787f80000000|" +
            "00078000078007ff8787ff87f87f8780787f80000000|" +
            "00078000078007ff8787ff87f87f8780787f80000000|" +
            "00078000078007ff8787ff87f87f8780787f801ff800|" +
            "0007ffffff87fffffffffff807f87ff87807801ff800|" +
            "0007ffffff87fffffffffff807f87ff87807801ff800|" +
            "0007ffffff87fffffffffff807f87ff87807801ff800|" +
            "0007ffffff87fffffffffff807f87ff87807801ff800|" +
            "000000000000000000000000000000000000001ff800|" +
            "000000000000000000000000000000000000001ff800|" +
            "000000000000000000000000000000000000001ff800|" +
            "000000000000000000000000000000000000001ff800|" +
            "000000000000000000000000000000000000001ff800|" +
            "000000000000000000000000000000000000001ff800|" +
            "000000000000000000000000000000000000001ff800|" +
            "000000000000000000000000000000000000001ff800|" +
            "000000000000000000000000000000000000001ff800|" +
            "000000000000000000000000000000000000001ff800|" +
            "00000fffff003ff0000000000ffc00ffc00ffffff800|" +
            "00000fffff003ff0000000000ffc00ffc00ffffff800|" +
            "00000fffff003ff0000000000ffc00ffc00ffffff800|" +
            "00000fffff003ff0000000000ffc00ffc00ffffff800|" +
            "00000fffff003ff0000000000ffc00ffc00ffffff800|" +
            "00000fffff003ff0000000000ffc00ffc00ffffff800|" +
            "00000fffff003ff0000000000ffc00ffc00ffffff800|" +
            "00000fffff003ff0000000000ffc00ffc00ffffff800|" +
            "00000fffff003ff0000000000ffc00ffc00ffffff800|" +
            "00000fffff003ff0000000000ffc00ffc00ffffff800|" +
            "003ff003ff003ff003ff000000000000000000000000|" +
            "003ff003ff003ff003ff000000000000000000000000|" +
            "003ff003ff003ff003ff000000000000000000000000|" +
            "003ff003ff003ff003ff000000000000000000000000|" +
            "003ff003ff003ff003ff000000000000000000000000|" +
            "003ff003ff003ff003ff000000000000000000000000|" +
            "003ff003ff003ff003ff000000000000000000000000|" +
            "003ff003ff003ff003ff000000000000000000000000|" +
            "003ff003ff003ff003ff000000000000000000000000|" +
            "003ff003ff003ff003ff000000000000000000000000"
        },
        {
            "HTTPS://ID.GS1.ORG/01/09521234543213/22/ABC%2D123?99=XYZ-987",
            2842,
            D3AqrHostRatio.Three,
            105,
            "000000000000000000000007e00|" +
            "000000000000000000000007e00|" +
            "000000000000000000000007e00|" +
            "000000000000000000000007e00|" +
            "000000000000000000000007e00|" +
            "000000000000000000000007e00|" +
            "0000000000000000000000001f8|" +
            "0000000000000000000000001f8|" +
            "0000000000000000000000001f8|" +
            "0000000000000000000000001f8|" +
            "0000000000000000000000001f8|" +
            "0000000000000000000000001f8|" +
            "000000000000000000000007e00|" +
            "000000000000000000000007e00|" +
            "000000000000000000000007e00|" +
            "000000000000000000000007e00|" +
            "000000000000000000000007e00|" +
            "000000000000000000000007e00|" +
            "0000000000000000000000001f8|" +
            "0000000000000000000000001f8|" +
            "0000000000000000000000001f8|" +
            "000007ffe19fe19e067ffe001f8|" +
            "000007ffe19fe19e067ffe001f8|" +
            "0000060067987867fe6006001f8|" +
            "0000060067987867fe600607e00|" +
            "0000067e6001ffe07867e607e00|" +
            "0000067e6001ffe07867e607e00|" +
            "0000067e6079e7fe6667e607e00|" +
            "0000067e6079e7fe6667e607e00|" +
            "0000067e661e66679867e607e00|" +
            "0000067e661e66679867e607ff8|" +
            "0000060061e01f9e78600607ff8|" +
            "0000060061e01f9e78600607ff8|" +
            "000007ffe6666666667ffe07ff8|" +
            "000007ffe6666666667ffe07ff8|" +
            "000000000181999986000007ff8|" +
            "000000000181999986000000000|" +
            "00000666619e666660061800000|" +
            "00000666619e666660061800000|" +
            "00000006006787e7e7e7e600000|" +
            "00000006006787e7e7e7e600000|" +
            "00000198679e66607f980600000|" +
            "00000198679e66607f9806001f8|" +
            "000006180787e7ff9e0018001f8|" +
            "000006180787e7ff9e0018001f8|" +
            "000001e1f8007e667e7818001f8|" +
            "000001e1f8007e667e7818001f8|" +
            "000007e60067fe7f99f81e001f8|" +
            "000007e60067fe7f99f81e00000|" +
            "00000180601e7fff879e6000000|" +
            "00000180601e7fff879e6000000|" +
            "000007999f87878781ff8000000|" +
            "000007999f87878781ff8000000|" +
            "0000067e78079e1e7ff86600000|" +
            "0000067e78079e1e7ff86600000|" +
            "0000061980799e1e1e180600000|" +
            "0000061980799e1e1e180600000|" +
            "0000019e67f861818079e000000|" +
            "0000019e67f861818079e000000|" +
            "000006679867981e1f981800000|" +
            "000006679867981e1f981807e00|" +
            "000001fe79e660181e786607e00|" +
            "000001fe79e660181e786607e00|" +
            "00000067980679999e780607e00|" +
            "00000067980679999e780607e00|" +
            "0000060667800060187e6607e00|" +
            "0000060667800060187e6600000|" +
            "000001e79e079819867f8600000|" +
            "000001e79e079819867f8600000|" +
            "00000678781fe6067fff8000000|" +
            "00000678781fe6067fff8000000|" +
            "00000000061f81ff86061e00000|" +
            "00000000061f81ff86061e07e00|" +
            "000007ffe06186787e661807e00|" +
            "000007ffe06186787e661807e00|" +
            "00000600618661e7fe079807e00|" +
            "00000600618661e7fe079807e00|" +
            "0000067e67ff86667fff9807e00|" +
            "0000067e67ff86667fff9800000|" +
            "0000067e61861e7e67e07800000|" +
            "0000067e61861e7e67e07800000|" +
            "0000067e661e1f9f9f998600000|" +
            "0000067e661e1f9f9f998600000|" +
            "00000600607e67e79e619e00000|" +
            "00000600607e67e79e619e07e00|" +
            "000007ffe7fffff879f98607e00|" +
            "000007ffe7fffff879f98607e00|" +
            "000000000000000000000007e00|" +
            "000000000000000000000007e00|" +
            "000000000000000000000007e00|" +
            "000000000000000000000007e00|" +
            "000000000000000000000007e00|" +
            "000000000000000000000007e00|" +
            "000fff03f000000fc0fc0fffe00|" +
            "000fff03f000000fc0fc0fffe00|" +
            "000fff03f000000fc0fc0fffe00|" +
            "000fff03f000000fc0fc0fffe00|" +
            "000fff03f000000fc0fc0fffe00|" +
            "000fff03f000000fc0fc0fffe00|" +
            "03f03f03f03f000000000000000|" +
            "03f03f03f03f000000000000000|" +
            "03f03f03f03f000000000000000|" +
            "03f03f03f03f000000000000000|" +
            "03f03f03f03f000000000000000|" +
            "03f03f03f03f000000000000000"
        },
        {
            "1234",
            0,
            D3AqrHostRatio.Two,
            35,
            "000000000|" +
            "000000000|" +
            "000000006|" +
            "000000006|" +
            "000000000|" +
            "000000000|" +
            "000000006|" +
            "000000006|" +
            "00fe2bf80|" +
            "00823a080|" +
            "00baeae86|" +
            "00bad2e86|" +
            "00babae80|" +
            "00828a080|" +
            "00feabf86|" +
            "0000d8006|" +
            "00be2be00|" +
            "002da9780|" +
            "00a3b4a80|" +
            "00d8a1b80|" +
            "00af14b18|" +
            "0000fe418|" +
            "00fe6b000|" +
            "0082de500|" +
            "00ba89218|" +
            "00bae9218|" +
            "00baf4e00|" +
            "008201b00|" +
            "00feb4e18|" +
            "000000018|" +
            "000000018|" +
            "c0000ccf8|" +
            "c0000ccf8|" +
            "333300000|" +
            "333300000"
        },
        {
            "1234",
            16383,
            D3AqrHostRatio.Two,
            35,
            "000000018|" +
            "000000018|" +
            "00000001e|" +
            "00000001e|" +
            "000000018|" +
            "000000018|" +
            "00000001e|" +
            "00000001e|" +
            "00fe2bf98|" +
            "00823a098|" +
            "00baeae9e|" +
            "00bad2e9e|" +
            "00babae98|" +
            "00828a098|" +
            "00feabf9e|" +
            "0000d801e|" +
            "00be2be00|" +
            "002da9780|" +
            "00a3b4a80|" +
            "00d8a1b80|" +
            "00af14b18|" +
            "0000fe418|" +
            "00fe6b000|" +
            "0082de500|" +
            "00ba89218|" +
            "00bae9218|" +
            "00baf4e00|" +
            "008201b00|" +
            "00feb4e18|" +
            "000000018|" +
            "000000018|" +
            "3fff0ccf8|" +
            "3fff0ccf8|" +
            "333300000|" +
            "333300000"
        },
        {
            "1234",
            1,
            D3AqrHostRatio.Three,
            105,
            "000000000000000000000007e00|" +
            "000000000000000000000007e00|" +
            "000000000000000000000007e00|" +
            "000000000000000000000007e00|" +
            "000000000000000000000007e00|" +
            "000000000000000000000007e00|" +
            "000000000000000000000007ff8|" +
            "000000000000000000000007ff8|" +
            "000000000000000000000007ff8|" +
            "000000000000000000000007ff8|" +
            "000000000000000000000007ff8|" +
            "000000000000000000000007ff8|" +
            "000000000000000000000000000|" +
            "000000000000000000000000000|" +
            "000000000000000000000000000|" +
            "000000000000000000000000000|" +
            "000000000000000000000000000|" +
            "000000000000000000000000000|" +
            "0000000000000000000000001f8|" +
            "0000000000000000000000001f8|" +
            "0000000000000000000000001f8|" +
            "0000000000000000000000001f8|" +
            "0000000000000000000000001f8|" +
            "0000000000000000000000001f8|" +
            "000000000000000000000000000|" +
            "000000000000000000000000000|" +
            "000000000000000000000000000|" +
            "000000000000000000000000000|" +
            "000000000000000000000000000|" +
            "000000000000000000000000000|" +
            "0000000000000000000000001f8|" +
            "0000000000000000000000001f8|" +
            "0000000000000000000000001f8|" +
            "0000000000000000000000001f8|" +
            "0000000000000000000000001f8|" +
            "0000000000000000000000001f8|" +
            "000000000000000000000000000|" +
            "000000000000000000000000000|" +
            "000000000000000000000000000|" +
            "000000000000000000000000000|" +
            "000000000000000000000000000|" +
            "000000000000000000000000000|" +
            "0000000000000000000000001f8|" +
            "0000000000000000000000001f8|" +
            "0000000000000000000000001f8|" +
            "000000000007ffe0667ffe001f8|" +
            "000000000007ffe0667ffe001f8|" +
            "00000000000600607e6006001f8|" +
            "00000000000600607e600600000|" +
            "0000000000067e67e667e600000|" +
            "0000000000067e67e667e600000|" +
            "0000000000067e679867e600000|" +
            "0000000000067e679867e600000|" +
            "0000000000067e667e67e600000|" +
            "0000000000067e667e67e600000|" +
            "000000000006006606600600000|" +
            "000000000006006606600600000|" +
            "000000000007ffe6667ffe00000|" +
            "000000000007ffe6667ffe00000|" +
            "00000000000000079e000000000|" +
            "00000000000000079e000007e00|" +
            "0000000000067fe0667fe007e00|" +
            "0000000000067fe0667fe007e00|" +
            "000000000000679e6619fe07e00|" +
            "000000000000679e6619fe07e00|" +
            "000000000006607e79866607e00|" +
            "000000000006607e79866600000|" +
            "0000000000079e06601e7e00000|" +
            "0000000000079e06601e7e00000|" +
            "00000000000667f819867800000|" +
            "00000000000667f819867800000|" +
            "0000000000000007ffe18000000|" +
            "0000000000000007ffe18007e00|" +
            "000000000007ffe1e6780007e00|" +
            "000000000007ffe1e6780007e00|" +
            "00000000000600679fe19807e00|" +
            "00000000000600679fe19807e00|" +
            "0000000000067e6606186007e00|" +
            "0000000000067e6606186000000|" +
            "0000000000067e67e6186000000|" +
            "0000000000067e67e6186000000|" +
            "0000000000067e67f987e000000|" +
            "0000000000067e67f987e000000|" +
            "0000000000060060001e7800000|" +
            "0000000000060060001e7807e00|" +
            "000000000007ffe67987e007e00|" +
            "000000000007ffe67987e007e00|" +
            "000000000000000000000007e00|" +
            "000000000000000000000007e00|" +
            "000000000000000000000007e00|" +
            "000000000000000000000007e00|" +
            "000000000000000000000007e00|" +
            "000000000000000000000007e00|" +
            "fc0000000000000fc0fc0fffe00|" +
            "fc0000000000000fc0fc0fffe00|" +
            "fc0000000000000fc0fc0fffe00|" +
            "fc0000000000000fc0fc0fffe00|" +
            "fc0000000000000fc0fc0fffe00|" +
            "fc0000000000000fc0fc0fffe00|" +
            "03f03f03f03f000000000000000|" +
            "03f03f03f03f000000000000000|" +
            "03f03f03f03f000000000000000|" +
            "03f03f03f03f000000000000000|" +
            "03f03f03f03f000000000000000|" +
            "03f03f03f03f000000000000000"
        },
        {
            "1234",
            128,
            D3AqrHostRatio.TwoAndAHalf,
            175,
            "00000000000000000000000000000000000000000000|" +
            "00000000000000000000000000000000000000000000|" +
            "00000000000000000000000000000000000000000000|" +
            "00000000000000000000000000000000000000000000|" +
            "00000000000000000000000000000000000000000000|" +
            "00000000000000000000000000000000000000000000|" +
            "00000000000000000000000000000000000000000000|" +
            "00000000000000000000000000000000000000000000|" +
            "00000000000000000000000000000000000000000000|" +
            "00000000000000000000000000000000000000000000|" +
            "000000000000000000000000000000000000000007fe|" +
            "000000000000000000000000000000000000000007fe|" +
            "000000000000000000000000000000000000000007fe|" +
            "000000000000000000000000000000000000000007fe|" +
            "000000000000000000000000000000000000000007fe|" +
            "000000000000000000000000000000000000000007fe|" +
            "000000000000000000000000000000000000000007fe|" +
            "000000000000000000000000000000000000000007fe|" +
            "000000000000000000000000000000000000000007fe|" +
            "000000000000000000000000000000000000000007fe|" +
            "00000000000000000000000000000000000000000000|" +
            "00000000000000000000000000000000000000000000|" +
            "00000000000000000000000000000000000000000000|" +
            "00000000000000000000000000000000000000000000|" +
            "00000000000000000000000000000000000000000000|" +
            "00000000000000000000000000000000000000000000|" +
            "00000000000000000000000000000000000000000000|" +
            "00000000000000000000000000000000000000000000|" +
            "00000000000000000000000000000000000000000000|" +
            "00000000000000000000000000000000000000000000|" +
            "000000000000000000000000000000000000000007fe|" +
            "000000000000000000000000000000000000000007fe|" +
            "000000000000000000000000000000000000000007fe|" +
            "000000000000000000000000000000000000000007fe|" +
            "000000000000000000000000000000000000000007fe|" +
            "000000000000000000000000000000000000000007fe|" +
            "000000000000000000000000000000000000000007fe|" +
            "000000000000000000000000000000000000000007fe|" +
            "000000000000000000000000000000000000000007fe|" +
            "000000000000000000000000000000000000000007fe|" +
            "00000000000000000000000000000000000000000000|" +
            "00000000000000000000000000000000000000000000|" +
            "00000000000000000000000000000000000000000000|" +
            "00000000000000000000000000000000000000000000|" +
            "00000000000000000000000000000000000000000000|" +
            "00000000000000000000000000000000000000000000|" +
            "00000000000000000000000000000000000000000000|" +
            "00000000000000000000000000000000000000000000|" +
            "00000000000000000000000000000000000000000000|" +
            "00000000000000000000000000000000000000000000|" +
            "000000000000000000000000000000000000000007fe|" +
            "000000000000000000000000000000000000000007fe|" +
            "000000000000000000000000000000000000000007fe|" +
            "000000000000000000000000000000000000000007fe|" +
            "000000000000000000000000000000000000000007fe|" +
            "000000000000000000000000000000000000000007fe|" +
            "000000000000000000000000000000000000000007fe|" +
            "000000000000000000000000000000000000000007fe|" +
            "000000000000000000000000000000000000000007fe|" +
            "000000000000000000000000000000000000000007fe|" +
            "00000000000000000000000000000000000000000000|" +
            "0000000000000007ffffff80078787ffffff80000000|" +
            "0000000000000007ffffff80078787ffffff80000000|" +
            "0000000000000007ffffff80078787ffffff80000000|" +
            "0000000000000007ffffff80078787ffffff80000000|" +
            "00000000000000078000078007ff8780000780000000|" +
            "00000000000000078000078007ff8780000780000000|" +
            "00000000000000078000078007ff8780000780000000|" +
            "00000000000000078000078007ff8780000780000000|" +
            "000000000000000787ff8787ff878787ff8780000000|" +
            "000000000000000787ff8787ff878787ff87800007fe|" +
            "000000000000000787ff8787ff878787ff87800007fe|" +
            "000000000000000787ff8787ff878787ff87800007fe|" +
            "000000000000000787ff8787f8780787ff87800007fe|" +
            "000000000000000787ff8787f8780787ff87800007fe|" +
            "000000000000000787ff8787f8780787ff87800007fe|" +
            "000000000000000787ff8787f8780787ff87800007fe|" +
            "000000000000000787ff878787ff8787ff87800007fe|" +
            "000000000000000787ff878787ff8787ff87800007fe|" +
            "000000000000000787ff878787ff8787ff87800007fe|" +
            "000000000000000787ff878787ff8787ff8780000000|" +
            "00000000000000078000078780078780000780000000|" +
            "00000000000000078000078780078780000780000000|" +
            "00000000000000078000078780078780000780000000|" +
            "00000000000000078000078780078780000780000000|" +
            "0000000000000007ffffff87878787ffffff80000000|" +
            "0000000000000007ffffff87878787ffffff80000000|" +
            "0000000000000007ffffff87878787ffffff80000000|" +
            "0000000000000007ffffff87878787ffffff80000000|" +
            "000000000000000000000007f87f8000000000000000|" +
            "000000000000000000000007f87f8000000000000000|" +
            "000000000000000000000007f87f8000000000000000|" +
            "000000000000000000000007f87f8000000000000000|" +
            "000000000000000787ffff80078787ffff8000000000|" +
            "000000000000000787ffff80078787ffff8000000000|" +
            "000000000000000787ffff80078787ffff8000000000|" +
            "000000000000000787ffff80078787ffff8000000000|" +
            "00000000000000000787f87f878780787fff80000000|" +
            "00000000000000000787f87f878780787fff80000000|" +
            "00000000000000000787f87f878780787fff80000000|" +
            "00000000000000000787f87f878780787fff801ff800|" +
            "0000000000000007878007ff87f878078787801ff800|" +
            "0000000000000007878007ff87f878078787801ff800|" +
            "0000000000000007878007ff87f878078787801ff800|" +
            "0000000000000007878007ff87f878078787801ff800|" +
            "0000000000000007f87f80078780007f87ff801ff800|" +
            "0000000000000007f87f80078780007f87ff801ff800|" +
            "0000000000000007f87f80078780007f87ff801ff800|" +
            "0000000000000007f87f80078780007f87ff801ff800|" +
            "00000000000000078787fff80078780787f8001ff800|" +
            "00000000000000078787fff80078780787f800000000|" +
            "00000000000000078787fff80078780787f800000000|" +
            "00000000000000078787fff80078780787f800000000|" +
            "000000000000000000000007ffffff80780000000000|" +
            "000000000000000000000007ffffff80780000000000|" +
            "000000000000000000000007ffffff80780000000000|" +
            "000000000000000000000007ffffff80780000000000|" +
            "0000000000000007ffffff807f8787f8000000000000|" +
            "0000000000000007ffffff807f8787f8000000000000|" +
            "0000000000000007ffffff807f8787f8000000000000|" +
            "0000000000000007ffffff807f8787f80000001ff800|" +
            "000000000000000780000787f87fff807878001ff800|" +
            "000000000000000780000787f87fff807878001ff800|" +
            "000000000000000780000787f87fff807878001ff800|" +
            "000000000000000780000787f87fff807878001ff800|" +
            "000000000000000787ff8787800780780780001ff800|" +
            "000000000000000787ff8787800780780780001ff800|" +
            "000000000000000787ff8787800780780780001ff800|" +
            "000000000000000787ff8787800780780780001ff800|" +
            "000000000000000787ff8787ff8780780780001ff800|" +
            "000000000000000787ff8787ff878078078000000000|" +
            "000000000000000787ff8787ff878078078000000000|" +
            "000000000000000787ff8787ff878078078000000000|" +
            "000000000000000787ff8787fff87807ff8000000000|" +
            "000000000000000787ff8787fff87807ff8000000000|" +
            "000000000000000787ff8787fff87807ff8000000000|" +
            "000000000000000787ff8787fff87807ff8000000000|" +
            "0000000000000007800007800000007f87f800000000|" +
            "0000000000000007800007800000007f87f800000000|" +
            "0000000000000007800007800000007f87f800000000|" +
            "0000000000000007800007800000007f87f8001ff800|" +
            "0000000000000007ffffff8787f87807ff80001ff800|" +
            "0000000000000007ffffff8787f87807ff80001ff800|" +
            "0000000000000007ffffff8787f87807ff80001ff800|" +
            "0000000000000007ffffff8787f87807ff80001ff800|" +
            "000000000000000000000000000000000000001ff800|" +
            "000000000000000000000000000000000000001ff800|" +
            "000000000000000000000000000000000000001ff800|" +
            "000000000000000000000000000000000000001ff800|" +
            "000000000000000000000000000000000000001ff800|" +
            "000000000000000000000000000000000000001ff800|" +
            "000000000000000000000000000000000000001ff800|" +
            "000000000000000000000000000000000000001ff800|" +
            "000000000000000000000000000000000000001ff800|" +
            "000000000000000000000000000000000000001ff800|" +
            "003ff00000000000000000000ffc00ffc00ffffff800|" +
            "003ff00000000000000000000ffc00ffc00ffffff800|" +
            "003ff00000000000000000000ffc00ffc00ffffff800|" +
            "003ff00000000000000000000ffc00ffc00ffffff800|" +
            "003ff00000000000000000000ffc00ffc00ffffff800|" +
            "003ff00000000000000000000ffc00ffc00ffffff800|" +
            "003ff00000000000000000000ffc00ffc00ffffff800|" +
            "003ff00000000000000000000ffc00ffc00ffffff800|" +
            "003ff00000000000000000000ffc00ffc00ffffff800|" +
            "003ff00000000000000000000ffc00ffc00ffffff800|" +
            "003ff003ff003ff003ff000000000000000000000000|" +
            "003ff003ff003ff003ff000000000000000000000000|" +
            "003ff003ff003ff003ff000000000000000000000000|" +
            "003ff003ff003ff003ff000000000000000000000000|" +
            "003ff003ff003ff003ff000000000000000000000000|" +
            "003ff003ff003ff003ff000000000000000000000000|" +
            "003ff003ff003ff003ff000000000000000000000000|" +
            "003ff003ff003ff003ff000000000000000000000000|" +
            "003ff003ff003ff003ff000000000000000000000000|" +
            "003ff003ff003ff003ff000000000000000000000000"
        },
        {
            "https://sixlabors.com/products/imagesharp-drawing/",
            5461,
            D3AqrHostRatio.Two,
            41,
            "00000000000|" +
            "00000000000|" +
            "3fbbbd4fe00|" +
            "20a10868200|" +
            "2e8e160ba00|" +
            "2eb4e6aba00|" +
            "2e8a82aba00|" +
            "20901f88200|" +
            "3faaaaafe78|" +
            "00381a00078|" +
            "2dc81d09600|" +
            "1336274da00|" +
            "1cf0b1cf678|" +
            "385bea25078|" +
            "1c8e4cd7600|" +
            "23243935400|" +
            "2ce8610b078|" +
            "2303af9f878|" +
            "1cb34fdb800|" +
            "2035db6b600|" +
            "34f8f326c78|" +
            "1449a6c2678|" +
            "2aa98d41e00|" +
            "2d60c959a00|" +
            "0483216ee00|" +
            "110d22ab600|" +
            "2fedf4fe060|" +
            "002f75a3060|" +
            "3fa1a56a000|" +
            "20b36763e00|" +
            "2e9717fec60|" +
            "2eaa98c5260|" +
            "2eb2920d800|" +
            "20907cee200|" +
            "3fa37d93860|" +
            "00000000060|" +
            "00000000060|" +
            "003330333e0|" +
            "003330333e0|" +
            "00cccc00000|" +
            "00cccc00000"
        },
        {
            "https://sixlabors.com/products/imagesharp-drawing/",
            10922,
            D3AqrHostRatio.Three,
            105,
            "000000000000000000000007e00|" +
            "000000000000000000000007e00|" +
            "000000000000000000000007e00|" +
            "000000000000000000000007e00|" +
            "000000000000000000000007e00|" +
            "000000000000000000000007e00|" +
            "0000000000000000000000001f8|" +
            "0000000000000000000000001f8|" +
            "0000000000000000000000001f8|" +
            "0000000000000000000000001f8|" +
            "0000000000000000000000001f8|" +
            "0000000000000000000000001f8|" +
            "000000000000000000000007e00|" +
            "000000000000000000000007e00|" +
            "000000000000000000000007e00|" +
            "000000000000000000000007e00|" +
            "000000000000000000000007e00|" +
            "000000000000000000000007e00|" +
            "0000000000000000000000001f8|" +
            "0000000000000000000000001f8|" +
            "0000000000000000000000001f8|" +
            "000007ffe7e7e7f9987ffe001f8|" +
            "000007ffe7e7e7f9987ffe001f8|" +
            "00000600660180601e6006001f8|" +
            "00000600660180601e600607e00|" +
            "0000067e607e019e0067e607e00|" +
            "0000067e607e019e0067e607e00|" +
            "0000067e67987e1e6667e607e00|" +
            "0000067e67987e1e6667e607e00|" +
            "0000067e606660066667e607e00|" +
            "0000067e606660066667e6001f8|" +
            "00000600618001ffe06006001f8|" +
            "00000600618001ffe06006001f8|" +
            "000007ffe6666666667ffe001f8|" +
            "000007ffe6666666667ffe001f8|" +
            "0000000007e001e6000000001f8|" +
            "0000000007e001e600000007e00|" +
            "00000679f86001f980619e07e00|" +
            "00000679f86001f980619e07e00|" +
            "00000187879e061f9879e607e00|" +
            "00000187879e061f9879e607e00|" +
            "000001f87f806781f87f9e07e00|" +
            "000001f87f806781f87f9e001f8|" +
            "000007e019e7fe66061980001f8|" +
            "000007e019e7fe66061980001f8|" +
            "000001f8607e1878799f9e001f8|" +
            "000001f8607e1878799f9e001f8|" +
            "00000607861807e1879998001f8|" +
            "00000607861807e187999800000|" +
            "000006787e601e0180678000000|" +
            "000006787e601e0180678000000|" +
            "000006078007e67fe1ffe000000|" +
            "000006078007e67fe1ffe000000|" +
            "000001f86787987ff9e7e000000|" +
            "000001f86787987ff9e7e000000|" +
            "000006000799f9e79e679e00000|" +
            "000006000799f9e79e679e00000|" +
            "000007987fe07f87861e7800000|" +
            "000007987fe07f87861e7800000|" +
            "000001981861e61e78061e00000|" +
            "000001981861e61e78061e07e00|" +
            "000006666661e0799801fe07e00|" +
            "000006666661e0799801fe07e00|" +
            "000006799e00786199e1e607e00|" +
            "000006799e00786199e1e607e00|" +
            "00000018600786019e7e7e07e00|" +
            "00000018600786019e7e7e00000|" +
            "000001818079860666679e00000|" +
            "000001818079860666679e00000|" +
            "0000067ffe79ff987ffe0000000|" +
            "0000067ffe79ff987ffe0000000|" +
            "00000000067f9f99e6078000000|" +
            "00000000067f9f99e6078007e00|" +
            "000007ffe601e6199e660007e00|" +
            "000007ffe601e6199e660007e00|" +
            "0000060067879e1f9e07fe07e00|" +
            "0000060067879e1f9e07fe07e00|" +
            "0000067e619f819ffffe7807e00|" +
            "0000067e619f819ffffe7800000|" +
            "0000067e666661e078198600000|" +
            "0000067e666661e078198600000|" +
            "0000067e678661860079e000000|" +
            "0000067e678661860079e000000|" +
            "0000060061801ff87e7e0600000|" +
            "0000060061801ff87e7e0607e00|" +
            "000007ffe6079ff9e187e007e00|" +
            "000007ffe6079ff9e187e007e00|" +
            "000000000000000000000007e00|" +
            "000000000000000000000007e00|" +
            "000000000000000000000007e00|" +
            "000000000000000000000007e00|" +
            "000000000000000000000007e00|" +
            "000000000000000000000007e00|" +
            "fff03f03f03f000fc0fc0fffe00|" +
            "fff03f03f03f000fc0fc0fffe00|" +
            "fff03f03f03f000fc0fc0fffe00|" +
            "fff03f03f03f000fc0fc0fffe00|" +
            "fff03f03f03f000fc0fc0fffe00|" +
            "fff03f03f03f000fc0fc0fffe00|" +
            "03f03f03f03f000000000000000|" +
            "03f03f03f03f000000000000000|" +
            "03f03f03f03f000000000000000|" +
            "03f03f03f03f000000000000000|" +
            "03f03f03f03f000000000000000|" +
            "03f03f03f03f000000000000000"
        },
    };

    /// <summary>
    /// The host, the D3 component and the category bits and parity produce the grid of cells of the reference implementation.
    /// </summary>
    /// <param name="text">The text to encode.</param>
    /// <param name="category">The category.</param>
    /// <param name="hostRatio">The width of a D3 module in host modules.</param>
    /// <param name="size">The expected number of cells on a side.</param>
    /// <param name="expected">The expected grid.</param>
    [Theory]
    [MemberData(nameof(D3Aqr_MatchesReferenceGridData))]
    public void D3Aqr_MatchesReferenceGrid(string text, int category, D3AqrHostRatio hostRatio, int size, string expected)
        => SymbolEncoder.Encode(new D3AqrSymbology(category, QrErrorCorrectionLevel.M, hostRatio), text, new BarcodeOptions(), (ref symbol) =>
        {
            Assert.Equal((size, size), (symbol.MatrixRows, symbol.Columns));
            Assert.Equal(expected, SymbolEncoder.ModuleGrid(ref symbol));
        });

    /// <summary>
    /// The two arms carry 14 bits, so the category is 0 to 16383.
    /// </summary>
    [Fact]
    public void D3Aqr_RejectsCategoriesOutsideFourteenBits()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new D3AqrSymbology(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new D3AqrSymbology(16384));
    }

    /// <summary>
    /// Gets the cases of <see cref="QrCodeDataLayers_MatchReferenceGrid"/>.
    /// </summary>
    public static TheoryData<string, string, QrErrorCorrectionLevel, int, string> QrCodeDataLayers_MatchReferenceGridData { get; } = new()
    {
        {
            "gs1qrcode",
            "(01)09521234543213(17)120508(10)ABCD1234(410)9501101020917",
            QrErrorCorrectionLevel.M,
            29,
            "fe6cfbf8|" +
            "82b3aa08|" +
            "ba6eeae8|" +
            "ba2f6ae8|" +
            "ba824ae8|" +
            "82145208|" +
            "feaaabf8|" +
            "001ed800|" +
            "aa434890|" +
            "ec1b1510|" +
            "bfdc43c8|" +
            "41095778|" +
            "2fb0afd0|" +
            "19adac18|" +
            "af13acd0|" +
            "19beaf58|" +
            "ea033f60|" +
            "5dfb46f0|" +
            "b2cc0968|" +
            "7dc12610|" +
            "af08efa0|" +
            "00edf8d8|" +
            "fe3bdaf8|" +
            "8246e890|" +
            "baab3fa8|" +
            "ba570520|" +
            "bade5848|" +
            "824f1680|" +
            "fe8e8fb8"
        },
        {
            "gs1qrcode",
            "(01)10012345678902",
            QrErrorCorrectionLevel.M,
            21,
            "fe63f8|" +
            "82a208|" +
            "ba5ae8|" +
            "ba4ae8|" +
            "bafae8|" +
            "825208|" +
            "feabf8|" +
            "006000|" +
            "aa4890|" +
            "3455f0|" +
            "e792c8|" +
            "c5fdd0|" +
            "56f228|" +
            "00a2d0|" +
            "fe49b8|" +
            "824290|" +
            "ba8b28|" +
            "ba1420|" +
            "bab708|" +
            "821d60|" +
            "feb618"
        },
        {
            "gs1qrcode",
            "(01)09521234543213(10)ABC123(21)XYZ789",
            QrErrorCorrectionLevel.Q,
            25,
            "fecebf8|" +
            "8277208|" +
            "bae5ae8|" +
            "ba8cae8|" +
            "ba252e8|" +
            "82c5a08|" +
            "feaabf8|" +
            "00f1800|" +
            "57ccf68|" +
            "6817138|" +
            "071a5b0|" +
            "5d4eb48|" +
            "4f606a0|" +
            "09fd2b0|" +
            "9b91758|" +
            "447daf0|" +
            "d605fe0|" +
            "00a6880|" +
            "fef2ad8|" +
            "82888f0|" +
            "ba6aff8|" +
            "ba97160|" +
            "ba26ec8|" +
            "82af890|" +
            "fe212b8"
        },
        {
            "gs1qrcode",
            "(01)09521234543213(10)AB%CD(21)12%34",
            QrErrorCorrectionLevel.M,
            25,
            "feb9bf8|" +
            "82a2208|" +
            "ba44ae8|" +
            "ba8eae8|" +
            "ba2f2e8|" +
            "8201a08|" +
            "feaabf8|" +
            "00f8000|" +
            "b744a58|" +
            "84a3620|" +
            "97622a8|" +
            "70b73a8|" +
            "0ed9e40|" +
            "457aa50|" +
            "43f5040|" +
            "a5115e8|" +
            "268bff8|" +
            "00a08e0|" +
            "fee6ab8|" +
            "82f6890|" +
            "ba26fe0|" +
            "baa1e78|" +
            "baa23d0|" +
            "821e470|" +
            "fef1858"
        },
        {
            "gs1qrcode",
            "(01)09521234543213(21)abc-def/123(10)xyz",
            QrErrorCorrectionLevel.L,
            25,
            "fe2e3f8|" +
            "82a2208|" +
            "ba0d2e8|" +
            "baeaae8|" +
            "ba412e8|" +
            "82daa08|" +
            "feaabf8|" +
            "0075800|" +
            "fbdbd50|" +
            "982d320|" +
            "5e28e70|" +
            "190d640|" +
            "36fb940|" +
            "e095308|" +
            "af52e28|" +
            "a401e68|" +
            "8a8ffa0|" +
            "00d2888|" +
            "fed2ab8|" +
            "826d8c8|" +
            "ba8af88|" +
            "bab1178|" +
            "bad2b08|" +
            "82ed118|" +
            "feabe58"
        },
        {
            "gs1qrcode",
            "(01)09521234543213(17)120508(10)ABCD1234(410)9501101020917",
            QrErrorCorrectionLevel.H,
            33,
            "fe59703f8|" +
            "820e85a08|" +
            "ba38622e8|" +
            "ba1e892e8|" +
            "ba95cd2e8|" +
            "8276baa08|" +
            "feaaaabf8|" +
            "00f559000|" +
            "33ee34e80|" +
            "40e528120|" +
            "ff375e2e0|" +
            "b906f7908|" +
            "d37d57910|" +
            "39ba30b80|" +
            "d62a12040|" +
            "c9327f080|" +
            "7a5c52a28|" +
            "0475860b0|" +
            "222904028|" +
            "65c79ae58|" +
            "665a22598|" +
            "b50880468|" +
            "0f6dc6630|" +
            "7855c3ae0|" +
            "a36595fd0|" +
            "00fc138a8|" +
            "fea17baa0|" +
            "822f8e8f8|" +
            "ba0cd0ff0|" +
            "bae1ed640|" +
            "baa98a6c0|" +
            "824e372d8|" +
            "fe4a5d600"
        },
        {
            "gs1dlqrcode",
            "https://id.gs1.org/01/09521234543213",
            QrErrorCorrectionLevel.M,
            29,
            "fe6703f8|" +
            "824faa08|" +
            "baad3ae8|" +
            "bacb72e8|" +
            "baf1d2e8|" +
            "82a02a08|" +
            "feaaabf8|" +
            "00b2a000|" +
            "be508be0|" +
            "b91f5388|" +
            "fb5fc500|" +
            "b89503d0|" +
            "462b6c60|" +
            "38699388|" +
            "6b400c60|" +
            "b4729910|" +
            "6298b460|" +
            "b55716a8|" +
            "a29f8aa0|" +
            "90a55f90|" +
            "8f6b2fb8|" +
            "008988f8|" +
            "fe205ae0|" +
            "82b2f898|" +
            "ba900fa0|" +
            "bad72860|" +
            "ba8327f0|" +
            "827f3bd0|" +
            "fea45220"
        },
        {
            "gs1dlqrcode",
            "https://id.gs1.org/01/09521234543213/22/ABC%2D123?99=XYZ-987",
            QrErrorCorrectionLevel.M,
            33,
            "fe58b7bf8|" +
            "82792e208|" +
            "bafb21ae8|" +
            "ba9922ae8|" +
            "babfef2e8|" +
            "82b85da08|" +
            "feaaaabf8|" +
            "00c286000|" +
            "be38283e0|" +
            "c8f5d5360|" +
            "9aee460a8|" +
            "e89ce12e0|" +
            "76d38cf48|" +
            "1cbd297f8|" +
            "0a91bbef0|" +
            "15ab24720|" +
            "d7cbfbdc8|" +
            "857515360|" +
            "838cac9a8|" +
            "c8d3991e0|" +
            "aa233b6d8|" +
            "a515c3508|" +
            "8ed223b70|" +
            "b18ca6d88|" +
            "bb8092f90|" +
            "00b9568b8|" +
            "fe05cbab0|" +
            "82c3fa8f8|" +
            "baab80fc0|" +
            "baeb76c90|" +
            "baa9c9b60|" +
            "820f1d8a0|" +
            "fed5f2930"
        },
        {
            "gs1dlqrcode",
            "HTTPS://ID.GS1.ORG/01/09521234543213",
            QrErrorCorrectionLevel.L,
            25,
            "fe1dbf8|" +
            "8266208|" +
            "ba0eae8|" +
            "ba89ae8|" +
            "bac52e8|" +
            "821a208|" +
            "feaabf8|" +
            "0032800|" +
            "c7530c0|" +
            "f9cac60|" +
            "4b23798|" +
            "a44bc40|" +
            "67d94f0|" +
            "f480540|" +
            "b2fb4e0|" +
            "a4d5738|" +
            "be94fa8|" +
            "00ab8f0|" +
            "fe97ab8|" +
            "82c88f0|" +
            "ba69f80|" +
            "ba45628|" +
            "ba7cba8|" +
            "82b1d80|" +
            "feae698"
        },
        {
            "hibcqrcode",
            "A123BJC5D6E71",
            QrErrorCorrectionLevel.M,
            21,
            "fee3f8|" +
            "827a08|" +
            "ba02e8|" +
            "ba92e8|" +
            "baeae8|" +
            "82f208|" +
            "feabf8|" +
            "00b800|" +
            "8bd7c8|" +
            "7cdeb0|" +
            "82d7c0|" +
            "2da310|" +
            "8e2ff8|" +
            "00cac0|" +
            "fec878|" +
            "821fd8|" +
            "baf088|" +
            "ba4f78|" +
            "ba15c0|" +
            "821068|" +
            "feeef8"
        },
        {
            "hibcqrcode",
            "A999BJC5D6E71",
            QrErrorCorrectionLevel.H,
            25,
            "fe7abf8|" +
            "82b2a08|" +
            "bae6ae8|" +
            "baacae8|" +
            "bab3ae8|" +
            "82c5a08|" +
            "feaabf8|" +
            "0038800|" +
            "27d85f0|" +
            "d029ff8|" +
            "4ac27f8|" +
            "71a7358|" +
            "e62fdd8|" +
            "3cf6aa8|" +
            "d7bce78|" +
            "00f9810|" +
            "d61cfb8|" +
            "00b78b8|" +
            "fec9a80|" +
            "82dc898|" +
            "ba0bfa8|" +
            "ba410a0|" +
            "bad9ff8|" +
            "827a6e8|" +
            "fe36458"
        },
        {
            "hibcqrcode",
            "A123BJC5D6E71/$$52001510X3",
            QrErrorCorrectionLevel.M,
            25,
            "fed03f8|" +
            "82fa208|" +
            "ba36ae8|" +
            "bab9ae8|" +
            "ba602e8|" +
            "8218208|" +
            "feaabf8|" +
            "00bb800|" +
            "b732a58|" +
            "b0f1db8|" +
            "77424a0|" +
            "e54ce20|" +
            "4fe1468|" +
            "2996058|" +
            "4acaaa0|" +
            "912d878|" +
            "2bd0fd0|" +
            "00ad898|" +
            "fef8ab8|" +
            "82c8898|" +
            "ba7dff8|" +
            "baef6e8|" +
            "bafc690|" +
            "82203c8|" +
            "feca3e8"
        },
    };

    /// <summary>
    /// The GS1, GS1 Digital Link and HIBC data layers produce the grids of the reference implementation.
    /// </summary>
    /// <param name="symbology">The symbology name of the reference implementation.</param>
    /// <param name="text">The text to encode.</param>
    /// <param name="level">The error correction level.</param>
    /// <param name="size">The expected number of modules on each side.</param>
    /// <param name="expected">The expected grid.</param>
    [Theory]
    [MemberData(nameof(QrCodeDataLayers_MatchReferenceGridData))]
    public void QrCodeDataLayers_MatchReferenceGrid(string symbology, string text, QrErrorCorrectionLevel level, int size, string expected)
    {
        BarcodeSymbology created = symbology switch
        {
            "gs1qrcode" => new Gs1QrCodeSymbology(level),
            "gs1dlqrcode" => new Gs1DigitalLinkQrCodeSymbology(level),
            _ => new HibcQrCodeSymbology(level, false),
        };

        SymbolEncoder.Encode(created, text, new BarcodeOptions(), (ref symbol) =>
        {
            Assert.Equal((size, size), (symbol.MatrixRows, symbol.Columns));
            Assert.Equal(expected, SymbolEncoder.ModuleGrid(ref symbol));
        });
    }

    [Fact]
    public void HibcQrCode_RejectsAnIncorrectCheckCharacter() => Assert.Throws<ArgumentException>(() => Encode(new HibcQrCodeSymbology(true), "A123BJC5D6E70"));

    [Fact]
    public void Gs1QrCode_RejectsDataOutsideElementStringSyntax()
        => Assert.Throws<ArgumentException>(() => Encode(new Gs1QrCodeSymbology(), "0109521234543213"));

    /// <summary>
    /// Section 5.7.4.7 of the GS1 General Specifications gives GS1 QR Code the human readable interpretation rules of section 4.14, the rules GS1 DataMatrix
    /// follows: one element string on each line, with the Application Identifiers in parentheses, and no URL of Application Identifier (8200).
    /// </summary>
    [Fact]
    public void Gs1QrCode_PrintsOneElementStringOnEachLine()
    {
        BarcodeOptions options = new() { Font = BarcodeFonts.OcrB.CreateFont(12F) };

        Assert.Equal(
            "(01)03453120000011\n(17)191125\n(10)ABCD1234",
            Assert.Single(SymbolEncoder.Read(new Gs1QrCodeSymbology(), "(01)03453120000011(17)191125(10)ABCD1234", options, SymbolEncoder.Texts)));

        Assert.Equal(
            "(01)09520123456788",
            Assert.Single(SymbolEncoder.Read(new Gs1QrCodeSymbology(), "(01)09520123456788(8200)http://www.abc.net", options, SymbolEncoder.Texts)));
    }

    /// <summary>
    /// Rule 1c of section 4.14 of the GS1 General Specifications: the human readable interpretation follows the order of the encoded data, which for a GS1
    /// Digital Link URI is the primary key and its qualifiers in the path, then the data attributes in the query string. The URI itself does not print.
    /// </summary>
    [Fact]
    public void Gs1DigitalLinkQrCode_PrintsTheElementStringsOfTheUri()
    {
        BarcodeOptions options = new() { Font = BarcodeFonts.OcrB.CreateFont(12F) };
        string[] texts = SymbolEncoder.Read(
            new Gs1DigitalLinkQrCodeSymbology(),
            "https://id.gs1.org/01/09521234543213/22/ABC%2D123?99=XYZ-987",
            options,
            SymbolEncoder.Texts);

        Assert.Equal("(01)09521234543213\n(22)ABC-123\n(99)XYZ-987", Assert.Single(texts));
    }

    /// <summary>
    /// Section 3.0 of ANSI/HIBC 2.6 gives every symbology the same interpretation between asterisks, and the figure of section 4.3.3 breaks concatenated data
    /// after the "/" that joins it.
    /// </summary>
    [Fact]
    public void HibcQrCode_PrintsTheDataBetweenDelimiters()
    {
        BarcodeOptions options = new() { Font = BarcodeFonts.OcrB.CreateFont(12F) };

        Assert.Equal("*+A123BJC5D6E71G*", Assert.Single(SymbolEncoder.Read(new HibcQrCodeSymbology(), "A123BJC5D6E71", options, SymbolEncoder.Texts)));
        Assert.Equal(
            "*+A123BJC5D6E71/\n$$52ØØ151ØX3C*",
            Assert.Single(SymbolEncoder.Read(new HibcQrCodeSymbology(), "A123BJC5D6E71/$$52001510X3", options, SymbolEncoder.Texts)));
    }

    /// <summary>
    /// The human readable interpretation prints only when the caller sets a font.
    /// </summary>
    [Fact]
    public void Gs1AndHibcQrCode_PrintNoTextWithoutAFont()
    {
        BarcodeOptions options = new();

        Assert.Empty(SymbolEncoder.Read(new Gs1QrCodeSymbology(), "(01)03453120000011", options, SymbolEncoder.Texts));
        Assert.Empty(SymbolEncoder.Read(new Gs1DigitalLinkQrCodeSymbology(), "https://id.gs1.org/01/09521234543213", options, SymbolEncoder.Texts));
        Assert.Empty(SymbolEncoder.Read(new HibcQrCodeSymbology(), "A123BJC5D6E71", options, SymbolEncoder.Texts));
    }

    /// <summary>
    /// Encodes the text with the default options.
    /// </summary>
    /// <param name="symbology">The symbology to encode with.</param>
    /// <param name="text">The text to encode.</param>
    private static void Encode(BarcodeSymbology symbology, string text) => SymbolEncoder.Encode(symbology, text, new BarcodeOptions());

    /// <summary>
    /// Returns the digits 0 to 9 repeated to a length.
    /// </summary>
    /// <param name="length">The length.</param>
    /// <returns>The digits.</returns>
    private static string Digits(int length) => string.Create(length, 0, static (span, _) =>
    {
        for (int i = 0; i < span.Length; i++)
        {
            span[i] = (char)('0' + (i % 10));
        }
    });

    /// <summary>
    /// Returns the capital letters A to Z repeated to a length.
    /// </summary>
    /// <param name="length">The length.</param>
    /// <returns>The letters.</returns>
    private static string Letters(int length) => string.Create(length, 0, static (span, _) =>
    {
        for (int i = 0; i < span.Length; i++)
        {
            span[i] = (char)('A' + (i % 26));
        }
    });

    /// <summary>
    /// Returns the small letters a to z repeated to a length, characters only Byte mode holds.
    /// </summary>
    /// <param name="length">The length.</param>
    /// <returns>The letters.</returns>
    private static string Lowercase(int length) => string.Create(length, 0, static (span, _) =>
    {
        for (int i = 0; i < span.Length; i++)
        {
            span[i] = (char)('a' + (i % 26));
        }
    });
}
