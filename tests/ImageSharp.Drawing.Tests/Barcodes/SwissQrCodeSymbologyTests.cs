// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Barcodes;

namespace SixLabors.ImageSharp.Drawing.Tests.Barcodes;

/// <summary>
/// Encoder tests for <see cref="SwissQrCodeSymbology"/> against the Swiss Implementation Guidelines for the QR-bill version 2.4. The valid data is the six
/// examples of Annex A, Tables 17 to 22, with the names as the payment parts print them. Each expected grid is the module grid of an independent reference
/// implementation at level M with Kanji mode off. That implementation scores the data masks with placeholders in the format information modules. Section
/// 6.8.2.1 of ISO/IEC 18004 evaluates "the complete symbol". Where that changes the mask, the expected grid is the reference grid with two changes: the data
/// modules use the mask that the complete symbol selects, and the format information is the format information of that mask. Each rejected input changes one
/// line of an example so that the line breaks one rule of the guidelines.
/// </summary>
public class SwissQrCodeSymbologyTests
{
    /// <summary>
    /// Annex A, Table 17: a QR-IBAN with a QR reference, an amount and an ultimate debtor.
    /// </summary>
    private static readonly string[] Example1 =
    [
        "SPC", "0200", "1", "CH6431961000004421557", "S", "Max Muster & Söhne", "Musterstrasse", "123", "8000",
        "Seldwyla", "CH", string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty,
        string.Empty, "50.00", "CHF", "S", "Simon Muster", "Musterstrasse", "1", "8000", "Seldwyla", "CH", "QRR",
        "000008207791225857421286694", "Payment of travel", "EPD", string.Empty, string.Empty, string.Empty,
    ];

    /// <summary>
    /// Annex A, Table 18: a QR reference with billing information and an alternative procedure.
    /// </summary>
    private static readonly string[] Example2 =
    [
        "SPC", "0200", "1", "CH4431999123000889012", "S", "Max Muster & Söhne", "Musterstrasse", "123", "8000",
        "Seldwyla", "CH", string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty,
        string.Empty, "1949.75", "CHF", "S", "Simon Muster", "Musterstrasse", "1", "8000", "Seldwyla", "CH", "QRR",
        "210000000003139471430009017", "Order from 15.10.2020", "EPD",
        "//S1/10/1234/11/201021/30/102673386/32/7.7/40/0:30", "eBill/B/simon.muster@example.com", string.Empty,
    ];

    /// <summary>
    /// Annex A, Table 19: an IBAN without a reference, an amount or an ultimate debtor.
    /// </summary>
    private static readonly string[] Example3 =
    [
        "SPC", "0200", "1", "CH5204835012345671000", "S", "Muster Stiftung", "P.O. box", string.Empty, "3001", "Bern",
        "CH", string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty,
        string.Empty, "CHF", string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty,
        string.Empty, "NON", string.Empty, string.Empty, "EPD", string.Empty, string.Empty, string.Empty,
    ];

    /// <summary>
    /// Annex A, Table 20: an IBAN with a Creditor Reference. Table 20 prints the reference RF720191230100405JSH0438, whose check digits fail the modulo 97-10
    /// check section 2.12.2 requires: the remainder is 49, not 1. The check digits of that reference body are 24.
    /// </summary>
    private static readonly string[] Example4 =
    [
        "SPC", "0200", "1", "CH5800791123000889012", "S", "Muster Krankenkasse", "Musterstrasse", "12", "8000",
        "Seldwyla", "CH", string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty,
        string.Empty, "211.00", "CHF", "S", "Sarah Beispiel", "Musterstrasse", "1", "8000", "Seldwyla", "CH", "SCOR",
        "RF240191230100405JSH0438", string.Empty, "EPD", string.Empty, string.Empty, string.Empty,
    ];

    /// <summary>
    /// Annex A, Table 21: a creditor in Liechtenstein.
    /// </summary>
    private static readonly string[] Example5 =
    [
        "SPC", "0200", "1", "CH5800791123000889012", "S", "Max Muster & Söhne", "Musterstrasse", "123", "9490",
        "Vaduz", "LI", string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty,
        string.Empty, "199.95", "CHF", "S", "Sarah Beispiel", "Musterstrasse", "1", "8000", "Seldwyla", "CH", "SCOR",
        "RF18539007547034", string.Empty, "EPD", string.Empty, string.Empty, string.Empty,
    ];

    /// <summary>
    /// Annex A, Table 22: an ultimate debtor in Germany.
    /// </summary>
    private static readonly string[] Example6 =
    [
        "SPC", "0200", "1", "CH5800791123000889012", "S", "Max Muster & Söhne", "Musterstrasse", "123", "8000",
        "Seldwyla", "CH", string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty,
        string.Empty, "199.95", "CHF", "S", "Sarah Beispiel", "Musterstrasse", "1", "78462", "Konstanz", "DE", "SCOR",
        "RF18539007547034", string.Empty, "EPD", string.Empty, string.Empty, string.Empty,
    ];

    /// <summary>
    /// Gets the cases of <see cref="MatchesReferenceGrid"/>: each example of Annex A, Example 1 also with CR LF, and Example 3 with a message of characters
    /// that take two and three bytes in UTF-8.
    /// </summary>
    public static TheoryData<string, string, int, string> MatchesReferenceGridData { get; } = new()
    {
        {
            "Example1",
            "\n",
            57,
            "fe1dacc130333f8|" +
            "8267b68d01f5208|" +
            "ba37b8d338732e8|" +
            "ba2707b715d52e8|" +
            "ba133c7eb2192e8|" +
            "82d2fc63a53a208|" +
            "feaaaaaaaaaabf8|" +
            "005763638895800|" +
            "96bf333f0750500|" +
            "88bed84a2da54c8|" +
            "5a34fe339b37e38|" +
            "04216604d3a91b8|" +
            "57974c8d2844e10|" +
            "bcb3aee847db1d0|" +
            "aa4ccb2b1ef20f0|" +
            "19efb6a4515d460|" +
            "166892f4ea4f700|" +
            "0c445723573c7e8|" +
            "3aa91643ae9b598|" +
            "3d5fac4b2182000|" +
            "0aa1269c4ad18a8|" +
            "94850f1b8e497c0|" +
            "5b572c529e6ccc8|" +
            "ac2df161c14a650|" +
            "3eff54e1206ec78|" +
            "5c12c7c4ebc7c68|" +
            "2fb2a9ffb98ef88|" +
            "98b48c22d405890|" +
            "7ab0feea4894a98|" +
            "08af7462478d880|" +
            "afb64c7ee9f2fa8|" +
            "7941e56cb4da570|" +
            "eb41e4dae3d08d0|" +
            "11555f075be93d0|" +
            "12984fdff56cfd8|" +
            "3c283c404a1df18|" +
            "37e7e8aec623868|" +
            "e927bb2370a4170|" +
            "3b9c53873af1080|" +
            "ddbcf16a0409910|" +
            "2349860e345a6e8|" +
            "f9d3f2678503c80|" +
            "db046b1ffbda228|" +
            "8c1b7e317a40698|" +
            "d7d8220e34f3cf0|" +
            "b8a82b00576f518|" +
            "a61a8ebb891c190|" +
            "f8691364268eb38|" +
            "03d8847e9cb9fa8|" +
            "00a5716208e28c0|" +
            "fe58486aafcaae0|" +
            "8288d663d4d18f8|" +
            "ba0588bf8c6ff98|" +
            "ba9a5fec66d4618|" +
            "ba4a701b8bf8da8|" +
            "824f36c2fdba8e0|" +
            "fe80ab9abde3850"
        },
        {
            "Example1",
            "\r\n",
            61,
            "fedb7962f9dc5bf8|" +
            "82efceede22dda08|" +
            "bac6a2e61b277ae8|" +
            "ba5389ae9916eae8|" +
            "baa9d2bfb5c9b2e8|" +
            "82271888a9e6e208|" +
            "feaaaaaaaaaaabf8|" +
            "002b5b68cc330800|" +
            "9fab907ff1b694b8|" +
            "0575a60a413324b8|" +
            "b6f145ef8b68b3c0|" +
            "3c466de970f7cf98|" +
            "62f510a351909fd8|" +
            "e515e1f6ce296128|" +
            "27226a0c6dd0e1a0|" +
            "c9ee57537bafabe0|" +
            "63298391b97d0108|" +
            "8df8448d29d97fa8|" +
            "0a0226f3dfdfd708|" +
            "001d2c270ea531c8|" +
            "86b749460db59fa8|" +
            "5523d7fdc9cd2978|" +
            "bbd408a5383cd6e8|" +
            "e06ec3a8397f6f40|" +
            "4308daaeaa1f2e50|" +
            "f59da6c796e8cb98|" +
            "e29e31dcb8a29d70|" +
            "c137aeaa7abb9608|" +
            "efe41ccfeb6d8ff8|" +
            "68e596c8b73b08b0|" +
            "3aac73baabda8a90|" +
            "a8b5a818de3cb888|" +
            "7ff2535f94a5bfa8|" +
            "c1599a9d6d4e2c00|" +
            "5f1ada3fbe315aa8|" +
            "911edaf73ca78200|" +
            "bb0f253c663697b0|" +
            "5cff4660e0b57398|" +
            "6fc9ecae3054dc68|" +
            "3de738852a26e768|" +
            "5acb3bda9f669fd8|" +
            "2807a00779c35068|" +
            "f3e3c57f9edab7c8|" +
            "4c9f1f5e7131d340|" +
            "636545ddce1b19b0|" +
            "c0b334a1d40aea38|" +
            "0b859fa6ca6dfec8|" +
            "4d989292608b0a50|" +
            "477930471f51b728|" +
            "c195eccfbd9b6918|" +
            "3e5e630e7fb5cb70|" +
            "e8182f9cf32aecc8|" +
            "f38eecafbbf5df98|" +
            "00a0aa588bc4d8c0|" +
            "feda07faad5a6ab8|" +
            "82b8fcd89a78e888|" +
            "babd157feb86bff0|" +
            "baa2c4e79e9527a8|" +
            "ba402aa6ff293938|" +
            "824dacd221b3ce38|" +
            "fec32aff13a659e8"
        },
        {
            "Example2",
            "\n",
            65,
            "fe763dfc4a57e53f8|" +
            "824a056741b0c8a08|" +
            "ba9b29a25a4c12ae8|" +
            "bab5a475c884c32e8|" +
            "bac58d83ede41cae8|" +
            "82b55c522d3096208|" +
            "feaaaaaaaaaaaabf8|" +
            "0099de0e2d61d6000|" +
            "be3b4877f24a3fbe0|" +
            "69bacedef12872fe0|" +
            "872ef593e480ac398|" +
            "c8e43f6ec93cdec90|" +
            "0fb18b004fa590208|" +
            "38703b826a8725ca8|" +
            "3a3b1a5ced8682500|" +
            "b990b2c244e0dfca8|" +
            "7aaf091bc3579c6e0|" +
            "80a8b8fc6cb976ce0|" +
            "a6ddabd3ec11d3c48|" +
            "0c3b02b1592e0a9d8|" +
            "bb8703f7b80c126c0|" +
            "50c40f15f2a4b0858|" +
            "525307ae4d04a05f8|" +
            "943a6e923b95f50d0|" +
            "ef7225121f511bc90|" +
            "a947625186c89e248|" +
            "fada373e5831838d0|" +
            "d97ec470389dcd488|" +
            "22a542e6cf74b3560|" +
            "21f722920fe4dae50|" +
            "1fc56f03e0183bfb0|" +
            "08a3a2d626b8918f0|" +
            "9a90fc3aade8cfaf0|" +
            "d8b55dfa2b13a08d0|" +
            "8fd2de83fe5644ff0|" +
            "c5ca24595124b27a0|" +
            "de0caa94d2ea32248|" +
            "b40b30c25521d34b0|" +
            "da168793c09784978|" +
            "e8f80c084d60d55f0|" +
            "1a7ea6a2aec2ba2c8|" +
            "c08a72e6e934ca5e0|" +
            "6f58e0e636435c898|" +
            "94e3f93a49a51f4c0|" +
            "3e8d60eb0a4411818|" +
            "41a7ab161ea7a2870|" +
            "732af0475a66a77c0|" +
            "2869894238f95eb78|" +
            "a6cb916a4902f1e80|" +
            "e90500f6b2bd4f680|" +
            "33b775864a77c74f8|" +
            "b863a1c6104c1eb58|" +
            "0a480bb219ec5ecc0|" +
            "e44ecf38d4c492fd0|" +
            "36d98c0a496497930|" +
            "90d7e175b7007d648|" +
            "6a8b9a73e85402fd0|" +
            "00a6a75e34dcaa8c0|" +
            "fe605aaeb8709aaa0|" +
            "82916e062c62b18c8|" +
            "baba8297ef7bb4fc0|" +
            "ba964fab4f2b350d0|" +
            "bab93edeae9e93330|" +
            "8210156ed624c7870|" +
            "fefb533245e94f3a0"
        },
        {
            "Example3",
            "\n",
            41,
            "fe8a20923f8|" +
            "8292d631208|" +
            "ba08b77c2e8|" +
            "ba942e1bae8|" +
            "ba10e1282e8|" +
            "8237cfc4208|" +
            "feaaaaaabf8|" +
            "00ea5e2c000|" +
            "b754aff5258|" +
            "d9266454010|" +
            "877aa5e69c8|" +
            "356795ab000|" +
            "9347a78a7d0|" +
            "384d4106d20|" +
            "2b14c60cb30|" +
            "d86878df550|" +
            "42fad1240f8|" +
            "6196d24b438|" +
            "4f3545c7618|" +
            "c9b09a705a8|" +
            "07fbba40e48|" +
            "199c22d2000|" +
            "036f9d4c990|" +
            "2593d2c3278|" +
            "faa5b003e88|" +
            "61f96fe9c20|" +
            "265edf45240|" +
            "7cdf62e2358|" +
            "2a06500e220|" +
            "0d189243fa8|" +
            "8b82e9dbcd8|" +
            "35aa8c61ae8|" +
            "431c1a4bf88|" +
            "00af2a918f0|" +
            "feb35e60ab8|" +
            "82b3b71c8c8|" +
            "ba392e1af80|" +
            "baa6e963920|" +
            "bab09555c68|" +
            "827d4792330|" +
            "feca71d6130"
        },
        {
            "Example4",
            "\n",
            53,
            "fea0bc9768a3f8|" +
            "82aa4e0465f208|" +
            "ba5412694dd2e8|" +
            "ba99b78a182ae8|" +
            "ba1899fb9762e8|" +
            "8214428d742208|" +
            "feaaaaaaaaabf8|" +
            "00807a8c647800|" +
            "b77bb9fab42258|" +
            "189748718faca0|" +
            "2b9ef27d9e2730|" +
            "112fa28b45f110|" +
            "9e6128c35a2250|" +
            "7c09f4c6f12b80|" +
            "abed8866fd5e18|" +
            "2003e89b1e2790|" +
            "8f30b1264a7968|" +
            "f51f17dbe6ae78|" +
            "26aad5a655a558|" +
            "64cba21d945260|" +
            "82c8090aa4f008|" +
            "1c2f6c10116100|" +
            "cb3a9a05430d80|" +
            "6d58b261cb9770|" +
            "bfd804f85b1fd8|" +
            "18e0ed8db768b0|" +
            "cae27faded4ac0|" +
            "48b0b48d7f38d0|" +
            "4f9bceff7e9fb8|" +
            "6d5fbe89040378|" +
            "62b8de13d523e0|" +
            "78fd9300f36d90|" +
            "6a7c93efafdb90|" +
            "eced644aaaf6e0|" +
            "5e4e5f56ca51e0|" +
            "8c91f645129cb0|" +
            "aebd03fa4016d8|" +
            "04971d6295eb10|" +
            "42553228b89ce8|" +
            "81e3761d6b48d0|" +
            "be018aa50c8868|" +
            "4c0dc2555bd1d8|" +
            "def6d243ef7e80|" +
            "61d5dbaca71cd0|" +
            "1304d7fc2d1f80|" +
            "00f5b189110880|" +
            "feb149acd78ad8|" +
            "82b7618e56b8c0|" +
            "ba1c46fbcd8f90|" +
            "ba95284c069a00|" +
            "bacde208b88f68|" +
            "820011d2682070|" +
            "feedd2a1d8ecf0"
        },
        {
            "Example5",
            "\n",
            53,
            "fe5cf35f8ea3f8|" +
            "82d513eda7f208|" +
            "ba26af4acbd2e8|" +
            "ba1db04194eae8|" +
            "baad07f8bea2e8|" +
            "824dba8d14a208|" +
            "feaaaaaaaaabf8|" +
            "00584c8b236000|" +
            "aa3232fa121890|" +
            "5d0694e513c520|" +
            "2fff40b236dcb8|" +
            "4496d515746868|" +
            "6f9e8a7ee1cb28|" +
            "1586b9d18e7b60|" +
            "7772e4932364f0|" +
            "8955e674425450|" +
            "ffc9de30f69de8|" +
            "cc9760e7957420|" +
            "2b560bbeb62638|" +
            "2c02980c543e38|" +
            "032fb3692fac60|" +
            "64e649770db5c0|" +
            "97f2adeb796a98|" +
            "78891515b25450|" +
            "df8655f8a07fa0|" +
            "0899318e7178c0|" +
            "5a88aaae7b2ab8|" +
            "f8f92f8b234880|" +
            "2fac39faf66fa0|" +
            "e8d7391595c360|" +
            "9f62ea3a36a4b8|" +
            "0588a51d5279c0|" +
            "6b57d9261be330|" +
            "e12fe991f88720|" +
            "4b6200dab2aa60|" +
            "4d806378435398|" +
            "5281bba3639be8|" +
            "5d1eb95316e4a0|" +
            "9f43244ab6a4b8|" +
            "4dd09ffa73b640|" +
            "eff0356b36f3e0|" +
            "646755492bde40|" +
            "defc98db0f7600|" +
            "60b025597005e0|" +
            "13042ffbbbaff8|" +
            "00bbd58a1c58c0|" +
            "fe7c9aaafe2ac8|" +
            "8223488bc778c0|" +
            "badc03fa1e0fb0|" +
            "ba5c79fd719570|" +
            "ba926d6a66a068|" +
            "8222aa753257f0|" +
            "fe90eedfb81f38"
        },
        {
            "Example6",
            "\n",
            53,
            "feba5e7adce3f8|" +
            "82dce6237e3208|" +
            "ba83930b04d2e8|" +
            "ba026fb06aeae8|" +
            "bada38fdce62e8|" +
            "820be48a792208|" +
            "feaaaaaaaaabf8|" +
            "0054088df81800|" +
            "9fde5ffa4514b8|" +
            "94c2129fec2ad8|" +
            "e38ecabec713a0|" +
            "d4c252c619ceb0|" +
            "3a7d15bb6c8ff8|" +
            "a522d4674d07d0|" +
            "672ae4e33470a8|" +
            "e86c448ad9a9a8|" +
            "26c383ad875a78|" +
            "c4a3565d5ce398|" +
            "c630ceea237278|" +
            "b81bc59a8b37c0|" +
            "8b7f5758ed1790|" +
            "f10306c8f28a18|" +
            "aeb24bff0a2840|" +
            "dd2053aed7e2b8|" +
            "7fbf91ffec0fb0|" +
            "38fdf48caa58f0|" +
            "9aeae6afa46ac8|" +
            "08c9708adcb8f8|" +
            "6f9f56fcffafb8|" +
            "4cc0bf85686fb8|" +
            "c68f1cffe3f3e8|" +
            "51a2a1a1ef1070|" +
            "6fdb721266ff40|" +
            "453a3631c16898|" +
            "0aedc6d41b7570|" +
            "0d3b25226ee518|" +
            "7a741deed6cea0|" +
            "319dcce54d8840|" +
            "9aa5e0baf1b818|" +
            "807478058da8e0|" +
            "37d66a374196f8|" +
            "e1ea4392402af8|" +
            "de87b1ee5da340|" +
            "604190cbbb6840|" +
            "124879f87cbfc8|" +
            "00e6f68be388b8|" +
            "feaebdaa86ead0|" +
            "82ee76880ac898|" +
            "babd97fb035fe0|" +
            "baec6dcdaaebc0|" +
            "ba419c9a71bd18|" +
            "8262eac9cba788|" +
            "fea2173551c820"
        },
        {
            "Example3WithMessage",
            "\n",
            49,
            "fe3f27bbb8bf8|" +
            "82d6c99113a08|" +
            "ba56a3a099ae8|" +
            "ba1c67394d2e8|" +
            "badd8feae82e8|" +
            "82654635a6208|" +
            "feaaaaaaaabf8|" +
            "0039363637000|" +
            "aa4acbee22890|" +
            "1174e6193d440|" +
            "cb5c6aeeaeae8|" +
            "15a3c54235560|" +
            "ae672c82ad830|" +
            "c8170bf8dfda0|" +
            "839cc1abf64d8|" +
            "bd030b7333028|" +
            "8a66825110a28|" +
            "091683dbbbdd8|" +
            "82ac3ab3332b0|" +
            "c505763d67c90|" +
            "e27b9a87a10a8|" +
            "30ba5c3439720|" +
            "7fd047e66afe8|" +
            "5893762236898|" +
            "cae2beb222ae8|" +
            "38a9f637de8c0|" +
            "df8a3beeaef80|" +
            "bc14ccd2f5560|" +
            "faa0bed2fdba0|" +
            "50f3149592740|" +
            "3be59e7a6e898|" +
            "0d85548445460|" +
            "8ab2322eefaf0|" +
            "9df27764445c8|" +
            "c2572a0eeea48|" +
            "7d123b4e35b50|" +
            "4662aa8ac50a0|" +
            "60b57dd2182e0|" +
            "46f1e2226a9d0|" +
            "7036f727371f0|" +
            "e357bbe5acfe8|" +
            "00dd8a3f5b8c0|" +
            "fe48caab36a98|" +
            "8212a223e58f8|" +
            "baec2be24cfa0|" +
            "ba3bec5cf74d0|" +
            "ba93276ae20e8|" +
            "820b2f5333f90|" +
            "fedced7110a78"
        },
    };

    /// <summary>
    /// Gets the cases of <see cref="RejectsALineOutsideItsDefinition"/>: an example, the number of a line of Table 8, and a value for that line that breaks its
    /// definition.
    /// </summary>
    public static TheoryData<string, int, string> RejectsALineOutsideItsDefinitionData { get; } = new()
    {
        // Lines 1 to 3, the header: fixed values.
        { nameof(Example1), 1, "SPD" },
        { nameof(Example1), 2, "0201" },
        { nameof(Example1), 3, "2" },

        // Line 4: a CH or LI IBAN of 21 characters, no spaces, with valid check digits.
        { nameof(Example1), 4, "CH6431961000004421558" },
        { nameof(Example1), 4, "ch6431961000004421557" },
        { nameof(Example1), 4, "CH64 31961000004421557" },
        { nameof(Example1), 4, "DE89370400440532013000" },

        // Lines 5 to 11, the creditor: a structured address.
        { nameof(Example1), 5, "K" },
        { nameof(Example1), 6, string.Empty },
        { nameof(Example1), 6, new string('A', 71) },
        { nameof(Example1), 7, new string('A', 71) },
        { nameof(Example1), 8, new string('1', 17) },
        { nameof(Example1), 9, string.Empty },
        { nameof(Example1), 9, new string('1', 17) },
        { nameof(Example1), 10, string.Empty },
        { nameof(Example1), 10, new string('A', 36) },
        { nameof(Example1), 11, string.Empty },
        { nameof(Example1), 11, "XX" },
        { nameof(Example1), 11, "ch" },

        // Lines 12 to 18, the ultimate creditor: must not be filled in.
        { nameof(Example1), 12, "S" },
        { nameof(Example1), 13, "Max Muster" },

        // Line 19, the amount: no leading zeros, a point and two decimals, at most 12 characters, and at least 0.01 outside a notification.
        { nameof(Example1), 19, "050.00" },
        { nameof(Example1), 19, "50.0" },
        { nameof(Example1), 19, "50,00" },
        { nameof(Example1), 19, ".50" },
        { nameof(Example1), 19, "1000000000.00" },
        { nameof(Example1), 19, "0.00" },

        // Line 20, the currency: CHF or EUR, and CHF with a QR reference.
        { nameof(Example1), 20, "USD" },
        { nameof(Example1), 20, "EUR" },

        // Lines 21 to 27, the ultimate debtor: when used, a structured address.
        { nameof(Example1), 21, "K" },
        { nameof(Example1), 22, string.Empty },
        { nameof(Example1), 25, string.Empty },
        { nameof(Example1), 26, string.Empty },
        { nameof(Example1), 27, string.Empty },
        { nameof(Example3), 22, "Simon Muster" },

        // Lines 28 and 29: QRR with a QR-IBAN and a QR reference, SCOR with an IBAN and a Creditor Reference, and NON with an IBAN and no reference.
        { nameof(Example1), 28, "XYZ" },
        { nameof(Example1), 28, "SCOR" },
        { nameof(Example1), 28, "NON" },
        { nameof(Example1), 29, "000008207791225857421286695" },
        { nameof(Example1), 29, "00000820779122585742128669" },
        { nameof(Example1), 29, "00000820779122585742128669A" },
        { nameof(Example1), 29, "000000000000000000000000000" },
        { nameof(Example3), 28, "QRR" },
        { nameof(Example3), 29, "RF18539007547034" },
        { nameof(Example4), 29, "RF720191230100405JSH0438" },
        { nameof(Example4), 29, "RF730191230100405JSH0438" },
        { nameof(Example4), 29, "RF18" },
        { nameof(Example4), 29, "RF1853900754703400000000000" },
        { nameof(Example4), 29, "XX18539007547034" },
        { nameof(Example4), 29, "RF18 5390 0754 7034" },

        // Section 4.1.3: no element is filled with blanks up to its maximum length.
        { nameof(Example1), 6, "Max Muster & Söhne".PadRight(70) },
        { nameof(Example1), 10, "Seldwyla".PadRight(35) },

        // Line 30, the unstructured message, at most 140 characters, and line 31, the trailer EPD.
        { nameof(Example1), 30, new string('A', 141) },
        { nameof(Example1), 31, "EPX" },

        // Line 32, the billing information: // and a name of two characters first, and at most 140 characters with the message. Lines 33 and 34, the
        // alternative procedures: at most 100 characters.
        { nameof(Example2), 32, "S1/10/1234" },
        { nameof(Example2), 32, "//S" },
        { nameof(Example2), 30, new string('A', 100) },
        { nameof(Example2), 33, new string('A', 101) },
    };

    /// <summary>
    /// The symbol of each example has the size and the module grid of the reference grid.
    /// </summary>
    /// <param name="example">The example of Annex A.</param>
    /// <param name="separator">The line separator.</param>
    /// <param name="size">The expected number of module rows and module columns.</param>
    /// <param name="expected">The expected module grid.</param>
    [Theory]
    [MemberData(nameof(MatchesReferenceGridData))]
    public void MatchesReferenceGrid(string example, string separator, int size, string expected)
        => SymbolEncoder.Encode(new SwissQrCodeSymbology(), Payload(example, separator), new BarcodeOptions(), (ref symbol) =>
        {
            Assert.Equal((size, size), (symbol.MatrixRows, symbol.Columns));
            Assert.Equal(expected, SymbolEncoder.ModuleGrid(ref symbol));
        });

    /// <summary>
    /// Section 6.4: the symbol is 46 x 46 mm without its quiet zone, whatever its version. At 254 dots per inch a millimetre is 10 pixels.
    /// </summary>
    /// <param name="example">The example of Annex A.</param>
    [Theory]
    [InlineData(nameof(Example2))]
    [InlineData(nameof(Example3))]
    public void Is46MillimetresWide(string example)
    {
        BarcodeOptions options = new() { Dpi = 254F, IncludeQuietZones = false };

        RectangleF bounds = BarcodeMeasurer.MeasureRenderableBounds(new SwissQrCodeSymbology(), options, PointF.Empty, Payload(example, "\n"));

        Assert.Equal(new SizeF(460F, 460F), bounds.Size);
    }

    /// <summary>
    /// Section 6.4.1: the symbol has the quiet zone of ISO 18004, four modules.
    /// </summary>
    [Fact]
    public void QuietZoneIsFourModules()
    {
        string text = Payload(nameof(Example3), "\n");

        Assert.Equal(4F, SymbolEncoder.Read(new SwissQrCodeSymbology(), text, new BarcodeOptions(), static (ref symbol) => symbol.LeadingQuietZone));
    }

    /// <summary>
    /// Section 6.4: "The measurements of the Swiss QR Code for printing must always be 46 x 46 mm (without surrounding quiet space) regardless of the Swiss QR
    /// Code version". The modules divide 46 mm, and the symbology rejects an X dimension from the caller.
    /// </summary>
    [Fact]
    public void IsAlwaysFortySixMillimetresWide()
    {
        string text = Payload(nameof(Example3), "\n");

        SymbolEncoder.Encode(new SwissQrCodeSymbology(), text, new BarcodeOptions(), static (ref symbol) =>
            Assert.Equal(46.0, symbol.XDimension * symbol.Columns, 1e-9));

        Assert.Throws<ArgumentException>(() => SymbolEncoder.Encode(new SwissQrCodeSymbology(), text, new BarcodeOptions { XDimension = 0.5 }));
    }

    /// <summary>
    /// Each line follows its definition in Table 8 and the rules of chapters 2 and 4. The symbology rejects a line that breaks its definition.
    /// </summary>
    /// <param name="example">The example of Annex A.</param>
    /// <param name="line">The number of the line in Table 8.</param>
    /// <param name="value">The value of the line.</param>
    [Theory]
    [MemberData(nameof(RejectsALineOutsideItsDefinitionData))]
    public void RejectsALineOutsideItsDefinition(string example, int line, string value)
        => Assert.Throws<ArgumentException>(() => Encode(With(Lines(example), line, value)));

    /// <summary>
    /// Section 4.1.4: the lines up to the trailer must be present, the billing information and the alternative procedures are the only further lines, and the
    /// lines are separated by CR LF or by LF, the same one throughout. The symbology rejects data that breaks one of these rules.
    /// </summary>
    [Fact]
    public void RejectsLinesOutsideTheStructure()
    {
        Assert.Throws<ArgumentException>(() => Encode(string.Join('\n', Example1[..30])));
        Assert.Throws<ArgumentException>(() => Encode(string.Join('\n', Example1) + "\nAV3"));
        Assert.Throws<ArgumentException>(() => Encode(string.Join('\r', Example1)));
        Assert.Throws<ArgumentException>(() => Encode(string.Join('\n', Example1[..20]) + "\r\n" + string.Join('\n', Example1[20..])));
    }

    /// <summary>
    /// Section 4.1.1 permits U+0020 to U+007E, U+00A0 to U+017F, U+0218 to U+021B and U+20AC, and section 6.2 permits at most 997 characters. The symbology
    /// rejects a tab, a Greek letter, an emoji and 998 characters.
    /// </summary>
    [Fact]
    public void RejectsCharactersOutsideSectionFourOneOneAndLongData()
    {
        Assert.Throws<ArgumentException>(() => Encode(With(Example1, 30, "Payment\tof travel")));
        Assert.Throws<ArgumentException>(() => Encode(With(Example1, 30, "Payment Ω")));
        Assert.Throws<ArgumentException>(() => Encode(With(Example1, 30, "Payment 😀")));
        Assert.Throws<ArgumentOutOfRangeException>(() => Encode(new string('A', 998)));
    }

    /// <summary>
    /// Data the guidelines permit beyond the examples: no billing information or alternative procedure lines, the amount 0.00 with a notification text of
    /// section 4.4, an amount below 1.00 and the largest amount, a Creditor Reference in small letters, which the banks read without regard to case, and a
    /// Creditor Reference in EUR. The symbology encodes each of them.
    /// </summary>
    [Fact]
    public void AcceptsDataTheGuidelinesPermit()
    {
        string[] notification = (string[])Example3.Clone();
        notification[18] = "0.00";
        notification[29] = "DO NOT USE FOR PAYMENT";
        Encode(string.Join('\n', notification));

        notification[29] = "NICHT ZUR ZAHLUNG VERWENDEN";
        Encode(string.Join('\n', notification));

        Encode(string.Join('\n', Example1[..31]));
        Encode(With(Example4, 19, "0.10"));
        Encode(With(Example4, 19, "999999999.99"));
        Encode(With(Example4, 29, "rf240191230100405jsh0438"));
        Encode(With(Example4, 20, "EUR"));
    }

    /// <summary>
    /// Annex B, Figure 22: the 26 digits 21000000000313947143000901 take the check digit 7, the QR reference of Example 2.
    /// </summary>
    [Fact]
    public void AcceptsTheCheckDigitOfFigureTwentyTwo()
    {
        Encode(With(Example2, 29, "210000000003139471430009017"));
        Assert.Throws<ArgumentException>(() => Encode(With(Example2, 29, "210000000003139471430009016")));
    }

    /// <summary>
    /// Returns the data of an example with its lines separated by the separator.
    /// </summary>
    /// <param name="example">The example of Annex A, or Example 3 with a message of multi-byte characters.</param>
    /// <param name="separator">The separator.</param>
    /// <returns>The data.</returns>
    private static string Payload(string example, string separator)
    {
        string[] lines = (string[])(example == "Example3WithMessage" ? Example3 : Lines(example)).Clone();
        if (example == "Example3WithMessage")
        {
            lines[29] = "Spende 100 € Ș ț ő ÿ";
        }

        return string.Join(separator, lines);
    }

    /// <summary>
    /// Returns the lines of an example.
    /// </summary>
    /// <param name="example">The name of the example.</param>
    /// <returns>The lines.</returns>
    private static string[] Lines(string example) => example switch
    {
        nameof(Example1) => Example1,
        nameof(Example2) => Example2,
        nameof(Example3) => Example3,
        nameof(Example4) => Example4,
        nameof(Example5) => Example5,
        _ => Example6,
    };

    /// <summary>
    /// Returns the data of an example with one line replaced, its lines separated by LF.
    /// </summary>
    /// <param name="lines">The lines of the example.</param>
    /// <param name="number">The number of the line in Table 8.</param>
    /// <param name="value">The value of the line.</param>
    /// <returns>The data.</returns>
    private static string With(string[] lines, int number, string value)
    {
        string[] copy = (string[])lines.Clone();
        copy[number - 1] = value;

        return string.Join('\n', copy);
    }

    /// <summary>
    /// Encodes the text with the default options.
    /// </summary>
    /// <param name="text">The text to encode.</param>
    private static void Encode(string text) => SymbolEncoder.Encode(new SwissQrCodeSymbology(), text, new BarcodeOptions());
}
