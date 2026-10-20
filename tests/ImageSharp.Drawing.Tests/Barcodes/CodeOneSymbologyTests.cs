// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Barcodes;

namespace SixLabors.ImageSharp.Drawing.Tests.Barcodes;

/// <summary>
/// Encoder tests for <see cref="CodeOneSymbology"/>. Each expected grid is the module grid of an independent reference implementation, one row of hexadecimal
/// digits per line, four modules per digit and the last digit padded with light modules. The grids come from bwip-js, except version G, which comes from Zint:
/// bwip-js adds the 2/3 steps of the mode look-ahead in floating point, and on that text one sum rounds up where exact arithmetic does not, so it latches into
/// Text mode later. Zint counts the steps exactly, and the two agree on the other cases of versions A to H.
/// </summary>
public class CodeOneSymbologyTests
{
    /// <summary>
    /// Gets the cases of <see cref="MatchesReferenceGrid"/>: the version (empty for the smallest of A to H), the text, the rows, the columns and the grid. The
    /// cases cover the ASCII, C40, Text, X12, Decimal and Byte modes, the multi-block versions F and G, and the T and S versions.
    /// </summary>
    public static TheoryData<string, string, int, int, string> MatchesReferenceGridData { get; } = new()
    {
        {
            string.Empty,
            "Code One",
            16,
            18,
            "4dd98|" +
            "48158|" +
            "29598|" +
            "183d8|" +
            "8a0e4|" +
            "08080|" +
            "ffffc|" +
            "00000|" +
            "7fff8|" +
            "40008|" +
            "7fff8|" +
            "11eb4|" +
            "b47ac|" +
            "50eb0|" +
            "86790|" +
            "838e0"
        },
        {
            string.Empty,
            "CODE ONE IN C40 MODE WITH CAPITALS AND 123",
            28,
            32,
            "ed9dc6b9|" +
            "6a0856e4|" +
            "8ad907a6|" +
            "db155b6d|" +
            "1ba63161|" +
            "4908e72f|" +
            "da3229a2|" +
            "0aa1ef20|" +
            "3be22228|" +
            "8bc44461|" +
            "08000020|" +
            "ffffffff|" +
            "00000020|" +
            "ffffffff|" +
            "00000000|" +
            "7ffffffe|" +
            "40000002|" +
            "7ffffffe|" +
            "8a222228|" +
            "18444461|" +
            "8a04952f|" +
            "184d9425|" +
            "cbd64024|" +
            "3a54632b|" +
            "2b71c96a|" +
            "6a7382a2|" +
            "e9bddae5|" +
            "3d3fe7bf"
        },
        {
            string.Empty,
            "code one in text mode with lower case letters",
            28,
            32,
            "ed9dc6b9|" +
            "fa0856e4|" +
            "8af4f36b|" +
            "db05b8e1|" +
            "b85f62ac|" +
            "ba1c7025|" +
            "79595c67|" +
            "5b1844a5|" +
            "c9dfe228|" +
            "a967c461|" +
            "08000020|" +
            "ffffffff|" +
            "00000020|" +
            "ffffffff|" +
            "00000000|" +
            "7ffffffe|" +
            "40000002|" +
            "7ffffffe|" +
            "8a222228|" +
            "18444461|" +
            "8a21dead|" +
            "187c86a3|" +
            "48b445a7|" +
            "39b39825|" +
            "786ae461|" +
            "4b65ede5|" +
            "6a7589ab|" +
            "dd8102b3"
        },
        {
            string.Empty,
            "ABC*DEF>GHI\rJKL*MNO>PQR\rSTU*VWX>YZ0",
            28,
            32,
            "ed783df2|" +
            "ea662de5|" +
            "8ae641aa|" +
            "684eab2f|" +
            "6b17102d|" +
            "e8276eed|" +
            "eafd4e28|" +
            "79fec461|" +
            "8a222228|" +
            "18444461|" +
            "08000020|" +
            "ffffffff|" +
            "00000020|" +
            "ffffffff|" +
            "00000000|" +
            "7ffffffe|" +
            "40000002|" +
            "7ffffffe|" +
            "8a222228|" +
            "18444461|" +
            "8a08a2ec|" +
            "187cb323|" +
            "db0492ab|" +
            "09eaceee|" +
            "38eb55e8|" +
            "3b4ccbea|" +
            "49d0ce6d|" +
            "fe4de076"
        },
        {
            string.Empty,
            "12345678901234567890123456789012345678901234567890",
            28,
            32,
            "fff32376|" +
            "1865416a|" +
            "aba2f9e1|" +
            "787a0eef|" +
            "1a5416a7|" +
            "cb235aaf|" +
            "e8e22228|" +
            "98444461|" +
            "8a222228|" +
            "18444461|" +
            "08000020|" +
            "ffffffff|" +
            "00000020|" +
            "ffffffff|" +
            "00000000|" +
            "7ffffffe|" +
            "40000002|" +
            "7ffffffe|" +
            "8a222228|" +
            "18444461|" +
            "8a334f20|" +
            "1868bba2|" +
            "0ad801e6|" +
            "3ad4eded|" +
            "3b137ea2|" +
            "49e54fab|" +
            "2bb31ce7|" +
            "1fa75439"
        },
        {
            string.Empty,
            "Bytes:  £¦©¬¯²µ¸»¾ÁÄÇÊÍÐÓÖÙÜßâåèëîñô÷",
            28,
            32,
            "4ddd9fb2|" +
            "3a9591e0|" +
            "38aaaaaa|" +
            "a800da6c|" +
            "aaeeeeec|" +
            "f8962fa1|" +
            "cb33376d|" +
            "49eb40e6|" +
            "db77bbae|" +
            "9b3c962b|" +
            "08000020|" +
            "ffffffff|" +
            "00000020|" +
            "ffffffff|" +
            "00000000|" +
            "7ffffffe|" +
            "40000002|" +
            "7ffffffe|" +
            "ebffe228|" +
            "e851c461|" +
            "8a31b2af|" +
            "185ec96f|" +
            "994ac763|" +
            "7b6d5d69|" +
            "2a48f523|" +
            "7916f6af|" +
            "7b0cd7af|" +
            "6d0376b3"
        },
        {
            string.Empty,
            "Mixed 12 digits 345678 and Text, CAPS, symbols !@#$%^&*() and more.",
            40,
            42,
            "4fa15eb20e0|" +
            "ebfeeae6ab0|" +
            "bb508844180|" +
            "78d36a055b0|" +
            "6adff8711bc|" +
            "aafdba434bc|" +
            "4954ba1eabc|" +
            "28537b77494|" +
            "a93c9822294|" +
            "193c89456bc|" +
            "2888ba106b4|" +
            "7ae6bb5ca8c|" +
            "bfbcac888e0|" +
            "3affc911184|" +
            "8a2228888a0|" +
            "08000800080|" +
            "ffffffffffc|" +
            "00000000000|" +
            "7fffffffff8|" +
            "40000000008|" +
            "7fffffffff8|" +
            "40000000008|" +
            "7fffffffff8|" +
            "40000000008|" +
            "7fffffffff8|" +
            "18444911184|" +
            "8a2228888a0|" +
            "1c444d111c4|" +
            "8a2228888a0|" +
            "18444911184|" +
            "89be5a8fd9c|" +
            "1b8e793b6b8|" +
            "5b23fa149b0|" +
            "0910d9f5b94|" +
            "69824925d80|" +
            "6a8fb832fb0|" +
            "0b56ba5fcb8|" +
            "bbb5ba53bac|" +
            "0b42788b594|" +
            "ef15df74fd4"
        },
        {
            string.Empty,
            "The quick brown fox jumps over the lazy dog. The quick brown fox jumps over the lazy dog. The quick brown fox jumps over the lazy dog. " +
            "The quick brown fox jumps over the lazy dog. The quick brown fox jumps over the lazy dog. The quick brown fox jumps over the lazy dog. " +
            "The quick brown fox jumps over the lazy dog. The quick brown fox jumps over the lazy dog. The quick brown fox jumps over the lazy dog. ",
            70,
            76,
            "5fa073b6c6becdd05fa|" +
            "5bd901e74255b2d9ae7|" +
            "2851d7691bfb9ac102b|" +
            "99d1706b6794b9633e6|" +
            "7874262c4a4bb24ad2b|" +
            "19e05a2659c4b16e924|" +
            "8b160669e0b296e1469|" +
            "b8a993a8ea2286c2de3|" +
            "8844eee1deddab276e1|" +
            "ca5613255708ba17427|" +
            "f9c56c687312a0739ac|" +
            "e84b7d65d6359901de4|" +
            "6ef8dd317a21d1d7671|" +
            "29572da6b79791706e6|" +
            "bbedac240b71b426324|" +
            "7a53962cf617a05a1a5|" +
            "a92f24ab4b8c960666e|" +
            "9b1316ea44b2a993a2e|" +
            "0ac96e25198184eec6d|" +
            "a8886c2b73c99613165|" +
            "eb76b26db1f7856c627|" +
            "7823a16d07e18b7d56d|" +
            "384a072e6c6bb8dd067|" +
            "68d590277425972d9ab|" +
            "ac851d7d91bfedac130|" +
            "7a5d1721b679939632f|" +
            "b9c74268c4a4af24ae4|" +
            "685e05a8659c9316ea4|" +
            "ba3160699e0b896e161|" +
            "4aca992e8ea2886c2e7|" +
            "0800002000008000020|" +
            "fffffffffffffffffff|" +
            "0000002000000000020|" +
            "fffffffffffffffffff|" +
            "0000002000000000020|" +
            "fffffffffffffffffff|" +
            "0000000000000000000|" +
            "7fffffffffffffffffe|" +
            "4000000000000000002|" +
            "7fffffffffffffffffe|" +
            "9a044eeb1dedb6b276b|" +
            "3b25612c5570a3a1760|" +
            "1bdc56e1873f8a22228|" +
            "7b84b7e55d6f8444461|" +
            "8a2222288888a222228|" +
            "1c4444711111c444471|" +
            "8a2222288888a222228|" +
            "1844446111118444461|" +
            "8a2222288888a222228|" +
            "1844446111118444461|" +
            "8a2222288888a222228|" +
            "1844446111118444461|" +
            "8a2222288888a223524|" +
            "1844446111118455ea4|" +
            "bac2cbe47259a429f2c|" +
            "c8577fae4dcc8fa32e0|" +
            "78793a2c942388224e9|" +
            "9f812a72c108ddd63ff|" +
            "f96973aaf92ca46226f|" +
            "3924d92eb361a0179a0|" +
            "699eca6c85d3967b324|" +
            "3998bd21c776a35f6e0|" +
            "2ad5f26e0d69ac9f0e8|" +
            "39ba51a5d0fb8308767|" +
            "384fed2eaa9caa021ed|" +
            "c9912f226cc79b7316e|" +
            "eb08f06ebf1cb0d84e4|" +
            "9a3d0f68f878900b026|" +
            "68c6dde22e049f832af|" +
            "4c3507bd1c8df0184fe"
        },
        {
            string.Empty,
            "0123456789012345678901234567890123456789012345678901234567890123456789012345678901234567890123456789" +
            "0123456789012345678901234567890123456789012345678901234567890123456789012345678901234567890123456789" +
            "0123456789012345678901234567890123456789012345678901234567890123456789012345678901234567890123456789" +
            "0123456789012345678901234567890123456789012345678901234567890123456789012345678901234567890123456789" +
            "0123456789012345678901234567890123456789012345678901234567890123456789012345678901234567890123456789" +
            "0123456789012345678901234567890123456789012345678901234567890123456789012345678901234567890123456789" +
            "0123456789012345678901234567890123456789012345678901234567890123456789012345678901234567890123456789",
            70,
            76,
            "fcd6bdba3dccc8416b9|" +
            "096a60ee8e779d8d6af|" +
            "88fa39e77635abda8ed|" +
            "6a8f7322105aa60fa2e|" +
            "cb08416a983ea39dde6|" +
            "79dd8d6af6a3b730860|" +
            "396bdaa3dcc28416a68|" +
            "5aa60fa8e77798d6be6|" +
            "3ba39de7635abda8f6c|" +
            "a8f730a105a9a0fa3a7|" +
            "c88416a983e8b9ddda3|" +
            "79d8d6af6a3db308425|" +
            "5ebda8fdcc21c16a633|" +
            "aa60fa2e77768d6bdaa|" +
            "ea39dde635af9a8f72c|" +
            "3b7308605a988fa39e7|" +
            "28416a683e8e9ddd8e5|" +
            "798d6be6a3dcb08416a|" +
            "abda8f6cc21096a60ee|" +
            "9a0fa3a7776396bdaa3|" +
            "8b9ddda35af6a8f7322|" +
            "db308425a983ba39de7|" +
            "1816a623e8e79dd8d6a|" +
            "68d6bdaa3dcc88416a9|" +
            "fda8f73c2105ea60fb8|" +
            "88fa39e77635abda8ed|" +
            "e9ddd8e5af6a8f730a1|" +
            "cb08416a983ea39dde6|" +
            "096a60ee8e779d8d6af|" +
            "396bdaa3dcc28416a68|" +
            "0800002000008000020|" +
            "fffffffffffffffffff|" +
            "0000002000000000020|" +
            "fffffffffffffffffff|" +
            "0000002000000000020|" +
            "fffffffffffffffffff|" +
            "0000000000000000000|" +
            "7fffffffffffffffffe|" +
            "4000000000000000002|" +
            "7fffffffffffffffffe|" +
            "6a8f7322105aa60fa2e|" +
            "3ba39de7635abda8f6c|" +
            "79dd8d6af6a3b730860|" +
            "c88416a983e8b9ddda3|" +
            "5aa7e2288888a222228|" +
            "5ebe84711111c444471|" +
            "8a2222288888a222228|" +
            "1844446111118444461|" +
            "8a2222288888a222228|" +
            "1844446111118444461|" +
            "8a2222288888a222228|" +
            "1844446111118444461|" +
            "8a2222288888a23cba2|" +
            "18444461111184781e0|" +
            "abc33826356d9a4e4ee|" +
            "3bde442075ffb8e1c2e|" +
            "8813442b3edfbce802a|" +
            "bc5d90360ff3c37a97b|" +
            "fa1f2b604181bf38ce1|" +
            "1acbe2eecb00825ab22|" +
            "9a11adadb15c832f0ea|" +
            "2b0f3fa7c6f78984f2d|" +
            "4875c2e641e9b7101ab|" +
            "79a13aaebab3ab9626f|" +
            "db5f18e54780a60ce20|" +
            "09a2faa163ae9a83862|" +
            "5bc778ebbfc8a9084a0|" +
            "ab0e152cc5e59e8ade8|" +
            "59e2b1646083b403f2e|" +
            "7c73a73dd6cdd9b1c73"
        },
        {
            string.Empty,
            "Code One version G holds 732 data codewords in four blocks. Code One version G holds 732 data codewords in four blocks. " +
            "Code One version G holds 732 data codewords in four blocks. Code One version G holds 732 data codewords in four blocks. " +
            "Code One version G holds 732 data codewords in four blocks. Code One version G holds 732 data codewords in four blocks. " +
            "Code One version G holds 732 data codewords in four blocks. Code One version G holds 732 data codewords in four blocks. " +
            "Code One version G holds 732 data codewords in four blocks. Code One version G holds 732 data codewords in four blocks. " +
            "Code One version G holds 732 data codewords in four blocks. Code One version G holds 732 data codewords in four blocks. " +
            "Code One version G holds 732 data codewords in four blocks. Code One version G holds 732 data codewords in four blocks. ",
            104,
            98,
            "4faec4ba9dbce28c96bfc83ac|" +
            "4ec6cc334d0bb66f3c2a84e10|" +
            "6a542ef68648add314ab9066c|" +
            "c2a246d50f4db2cca401b226c|" +
            "12dfd8114cdb8c262d644d25c|" +
            "7282caf0b673b371ecf45f65c|" +
            "d6d9dcf48141b755b46e04204|" +
            "06209b5496c5967055718ce54|" +
            "6243dc5778f1844a44182066c|" +
            "d63907159dc3a197e5406ae1c|" +
            "1658efb48a4b9c4ee5c311aec|" +
            "5e754eb0f9c4814865863426c|" +
            "13aa76fc8a32d2ff262b69740|" +
            "322d3433d9bcb8aa14c4c2a24|" +
            "bafa1930b74c92ae445b136fc|" +
            "1ad43d35cb32a006cc1b7222c|" +
            "620533733098ad913517d769c|" +
            "2ae2d9d3cdc7abd17d5704a08|" +
            "72f20511dd56b1b814016123c|" +
            "6e525b1559c195c634d5d4e90|" +
            "725de3d111298060845b1568c|" +
            "1e167713865fa501acc75de54|" +
            "beb22933713ba70c45bb12aa4|" +
            "3aa3e7140521a618d41b30ad0|" +
            "dbf228da4bfce0ada750bbfa0|" +
            "d22f66f4e2a893130c891b640|" +
            "6622dd344ab9816c4d7f60250|" +
            "f6372cd2801b886dcc0b2ba2c|" +
            "ce6cc270b644b45f5d6773e20|" +
            "66cf3717af45bd5c14826d624|" +
            "16077556c6e09005850f716dc|" +
            "6e1567115718b35754e41c664|" +
            "8ec444b10182816c5563bee20|" +
            "de0e19779406ab1d75d53aa3c|" +
            "a62dc4f39c3186ec4ca9dbe20|" +
            "9e1014919863906cc4b4d0af4|" +
            "a3c92ffc82b6e542efe864b2c|" +
            "9af38ab04c4c8a246d50f4e70|" +
            "76312af105b18dfd8414cdacc|" +
            "b2ca007321b7882cad8b672f0|" +
            "0a62d914d17d9d9dcdc814274|" +
            "de1ebd15f5709209b5496c654|" +
            "d65b1b904016843dc5778f244|" +
            "9e055c70cd5d93907459dc2e0|" +
            "12a4061205b1958efcc8a4adc|" +
            "667e5016ac75b754ec8f9c600|" +
            "0200001000008000040000200|" +
            "ffffffffffffffffffffffffc|" +
            "0000001000000000040000000|" +
            "ffffffffffffffffffffffffc|" +
            "0000000000000000000000000|" +
            "7fffffffffffffffffffffff8|" +
            "4000000000000000000000008|" +
            "7fffffffffffffffffffffff8|" +
            "4000000000000000000000008|" +
            "7fffffffffffffffffffffff8|" +
            "4000000000000000000000008|" +
            "7fffffffffffffffffffffff8|" +
            "12ee70d11bb18aa76dc8a3290|" +
            "5286619341b382d344bd9be38|" +
            "bef20ad6950bafa1948b74e10|" +
            "2aa131342891ad43d4dcb32a0|" +
            "aae416d137f6805335b309a2c|" +
            "026c86d720b2ae2d9d3cdc6e8|" +
            "661345f576778f20541dd56b0|" +
            "f617d5d04826b525b4559c254|" +
            "6e813e308888a222248888a20|" +
            "73633c591111c444471111344|" +
            "8a2222308888a222248888a20|" +
            "1244445111118444451111244|" +
            "8a2222308888a222248888a20|" +
            "1244445111118444451111244|" +
            "8a2222308888a222248888a20|" +
            "1244445111118444451111244|" +
            "8a2222308888a222248888a20|" +
            "1244445111118444451111244|" +
            "8a2222308888a222248888a20|" +
            "1244445111118444451111244|" +
            "8a2222322da0ac071c0e11a6c|" +
            "1344447f535ed530678393324|" +
            "8a9af1766539842b5ce5b9654|" +
            "3e7ee8718de78817ec1031aa0|" +
            "62acfa9109fd888da5824de44|" +
            "ae642c324aedb2c09417e3ab8|" +
            "3e63caf7182ba8f32dc9976c0|" +
            "2aa3acf6dbd9a4ab35aeeb26c|" +
            "d67b9012184f90d8751bce270|" +
            "6ed7095521e8b68d05613d210|" +
            "5e4466358dfe95a225a7d522c|" +
            "9e30b9d1d398a602e5d9c6afc|" +
            "861bcf92f583aca4140d2faa8|" +
            "3f89171f1b39c95dc7d35f748|" +
            "264960f79acfb93e8d6133e68|" +
            "ee18c49026588f6b3d8f40ae4|" +
            "fe6aef51900f940a1c3609e48|" +
            "720fe8108d8ebab22de9322c0|" +
            "22f06f30aacdb5237491caa98|" +
            "1e4d59f74d8ca30ae42eaeea8|" +
            "ded22473fce384db5d6ad3244|" +
            "b6eed6d74af5b091652daae0c|" +
            "9a691fb20b19a6cf3d071e6d4|" +
            "f646da35d94cb5ce6cb851a48|" +
            "6260fcd452b08a1cb4c77c6cc|" +
            "e706bd7d5a11dcdc5e1c1c78c"
        },
        {
            "A",
            "A1",
            16,
            18,
            "4ce20|" +
            "28844|" +
            "8a220|" +
            "18444|" +
            "8a110|" +
            "08080|" +
            "ffffc|" +
            "00000|" +
            "7fff8|" +
            "40008|" +
            "7fff8|" +
            "11294|" +
            "5e38c|" +
            "fb0ac|" +
            "50980|" +
            "a74c4"
        },
        {
            "B",
            "Version B",
            22,
            22,
            "5f9de0|" +
            "7bd674|" +
            "f847e0|" +
            "c8cfc4|" +
            "8a2220|" +
            "184444|" +
            "8a2218|" +
            "080080|" +
            "fffffc|" +
            "000000|" +
            "7ffff8|" +
            "400008|" +
            "7ffff8|" +
            "400008|" +
            "7ffff8|" +
            "11118c|" +
            "2e67b0|" +
            "2c00b8|" +
            "0c0680|" +
            "4ad490|" +
            "6b8988|" +
            "bea8f4"
        },
        {
            "T-16",
            "T16",
            16,
            17,
            "59c40|" +
            "52088|" +
            "88440|" +
            "11088|" +
            "88328|" +
            "11780|" +
            "ca568|" +
            "4b390|" +
            "02600|" +
            "df6e8|" +
            "00800|" +
            "ffff8|" +
            "80008|" +
            "bffe8|" +
            "80008|" +
            "bffe8"
        },
        {
            "T-32",
            "T-32 data",
            16,
            33,
            "52a2b33b0|" +
            "5e2129290|" +
            "888844440|" +
            "111108888|" +
            "888844440|" +
            "111108888|" +
            "151039cf8|" +
            "714b4ac28|" +
            "d19d7d020|" +
            "b5f870fc8|" +
            "000080000|" +
            "ffffffff8|" +
            "800080008|" +
            "bffffffe8|" +
            "800000008|" +
            "bffffffe8"
        },
        {
            "T-48",
            "T-48 holds more",
            16,
            49,
            "52be12c80f7b8|" +
            "5e2f657cb6f98|" +
            "6888884444440|" +
            "6111110888888|" +
            "8888884444440|" +
            "1111110888888|" +
            "888b8b6586520|" +
            "11d29b5e57f78|" +
            "875f081683330|" +
            "c08d6660c9230|" +
            "0000008000000|" +
            "ffffffffffff8|" +
            "8000008000008|" +
            "bffffffffffe8|" +
            "8000008000008|" +
            "bffffffffffe8"
        },
        {
            "S-10",
            "123456",
            8,
            11,
            "1e0|" +
            "c22|" +
            "a38|" +
            "c30|" +
            "000|" +
            "ffe|" +
            "802|" +
            "bfa"
        },
        {
            "S-20",
            "123456789012",
            8,
            21,
            "177090|" +
            "d65628|" +
            "829d60|" +
            "5a9440|" +
            "002000|" +
            "fffff8|" +
            "800008|" +
            "bfffe8"
        },
        {
            "S-30",
            "123456789012345678",
            8,
            31,
            "0dd380f2|" +
            "e8dcdb2e|" +
            "f894e194|" +
            "d8125f1c|" +
            "00010000|" +
            "fffffffe|" +
            "80010002|" +
            "bffffffa"
        },
    };

    /// <summary>
    /// The size and the module grid of each symbol match the reference implementation.
    /// </summary>
    /// <param name="version">The version name, or empty for the smallest of versions A to H that holds the data.</param>
    /// <param name="text">The text to encode.</param>
    /// <param name="rows">The number of module rows.</param>
    /// <param name="columns">The number of module columns.</param>
    /// <param name="expected">The reference module grid.</param>
    [Theory]
    [MemberData(nameof(MatchesReferenceGridData))]
    public void MatchesReferenceGrid(string version, string text, int rows, int columns, string expected)
    {
        CodeOneSymbology symbology = version.Length == 0
            ? new CodeOneSymbology()
            : new CodeOneSymbology(Enum.Parse<CodeOneVersion>(version.Replace("-", string.Empty)));

        SymbolEncoder.Encode(symbology, text, new BarcodeOptions(), (ref symbol) =>
        {
            Assert.Equal((rows, columns), (symbol.MatrixRows, symbol.Columns));
            Assert.Equal(expected, SymbolEncoder.ModuleGrid(ref symbol));
        });
    }

    /// <summary>
    /// The S versions carry digits only, and each holds a fixed number of them: 6, 12 and 18.
    /// </summary>
    /// <param name="version">The version.</param>
    /// <param name="text">The text.</param>
    [Theory]
    [InlineData(CodeOneVersion.S10, "12345A")]
    [InlineData(CodeOneVersion.S10, "1234567")]
    [InlineData(CodeOneVersion.S20, "1234567890123")]
    [InlineData(CodeOneVersion.S30, "1234567890123456789")]
    public void NumericVersionRejectsTextItCannotHold(CodeOneVersion version, string text)
        => Assert.Throws<ArgumentException>(() => SymbolEncoder.Encode(new CodeOneSymbology(version), text, new BarcodeOptions()));

    /// <summary>
    /// Data that fills the 1480 data codewords of version H fits it. Two ASCII codewords and a Decimal run of 3546 digits, 4 + 1182 x 10 bits, fill the version
    /// exactly. Zint encodes the same text as version H.
    /// </summary>
    [Fact]
    public void FillsVersionHExactly()
    {
        string text = "AB" + new string('1', 3546);

        SymbolEncoder.Encode(new CodeOneSymbology(), text, new BarcodeOptions(), (ref symbol) =>
            Assert.Equal((148, 134), (symbol.MatrixRows, symbol.Columns)));
    }

    /// <summary>
    /// A fixed version holds a fixed number of data codewords, so data that needs more does not fit it.
    /// </summary>
    [Fact]
    public void RejectsDataBeyondTheVersion()
        => Assert.Throws<ArgumentException>(
            () => SymbolEncoder.Encode(new CodeOneSymbology(CodeOneVersion.A), "This text needs more than ten codewords", new BarcodeOptions()));

    /// <summary>
    /// Versions A to H carry the 256 byte values, U+0000 to U+00FF.
    /// </summary>
    [Fact]
    public void RejectsCharactersAboveLatin1()
        => Assert.Throws<ArgumentException>(() => SymbolEncoder.Encode(new CodeOneSymbology(), "Ā", new BarcodeOptions()));
}
