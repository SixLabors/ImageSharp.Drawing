// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Barcodes;

namespace SixLabors.ImageSharp.Drawing.Tests.Barcodes;

/// <summary>
/// Encoder tests for <see cref="RectangularMicroQrCodeSymbology"/>. Each expected grid is the module grid of
/// an independent reference implementation, taken through its raw encoding API with the error correction
/// level fixed and Kanji mode off, so the mode segments, the error correction, the module placement, the
/// function patterns and the format information all show up here. A grid row is the modules from the left
/// as hexadecimal digits, the leftmost module the most significant bit, padded with light modules to whole
/// digits.
/// </summary>
public class RectangularMicroQrCodeSymbologyTests
{
    /// <summary>
    /// Gets the cases of <see cref="MatchesReferenceGrid"/>: for every version at
    /// levels M and H, the longest start of <see cref="Pattern"/> the version holds.
    /// </summary>
    public static TheoryData<string, QrErrorCorrectionLevel, RectangularMicroQrCodeVersion, int, int, string> MatchesReferenceGridData { get; } = new()
    {
        {
            Pattern(7),
            QrErrorCorrectionLevel.M,
            RectangularMicroQrCodeVersion.R7x43,
            7,
            43,
            "feaaaeaaaae|" +
            "825a5b98d8a|" +
            "bab3af165fe|" +
            "ba6f5a9f622|" +
            "ba2b2e6a72a|" +
            "82e1dbeffa2|" +
            "feaaaeaaabe"
        },
        {
            Pattern(3),
            QrErrorCorrectionLevel.H,
            RectangularMicroQrCodeVersion.R7x43,
            7,
            43,
            "feaaaeaaaae|" +
            "82da6aa4c82|" +
            "bad17e6e53e|" +
            "baba11767a2|" +
            "ba73cfcd5aa|" +
            "82776b87ea2|" +
            "feaaaeaaabe"
        },
        {
            Pattern(16),
            QrErrorCorrectionLevel.M,
            RectangularMicroQrCodeVersion.R7x59,
            7,
            59,
            "feaabaaaabaaaae|" +
            "82beed8a9a91e4a|" +
            "ba9ebadcc3f43be|" +
            "bac240d8b48dd62|" +
            "ba6eff90b7b7faa|" +
            "82a96c9e2ee9322|" +
            "feaabaaaabaaabe"
        },
        {
            Pattern(8),
            QrErrorCorrectionLevel.H,
            RectangularMicroQrCodeVersion.R7x59,
            7,
            59,
            "feaabaaaabaaaae|" +
            "8239a8b03a91f42|" +
            "bafbbc1233f437e|" +
            "ba11659caecdce2|" +
            "ba20fd2c4fb7f2a|" +
            "8221ebd2ceb9c22|" +
            "feaabaaaabaaabe"
        },
        {
            Pattern(23),
            QrErrorCorrectionLevel.M,
            RectangularMicroQrCodeVersion.R7x77,
            7,
            77,
            "feaaaaeaaaaabaaaaab8|" +
            "829422a37f9169047828|" +
            "ba73a3fdb131be3d09f8|" +
            "bae7f306b8df03737488|" +
            "bacc98e44e96fbedfba8|" +
            "82ed1ba489a96cea4e88|" +
            "feaaaaeaaaaabaaaaaf8"
        },
        {
            Pattern(15),
            QrErrorCorrectionLevel.H,
            RectangularMicroQrCodeVersion.R7x77,
            7,
            77,
            "feaaaaeaaaaabaaaaab8|" +
            "821459b3260869047c08|" +
            "ba1ccafb28f83e3d0af8|" +
            "ba38ddb66cdce3637288|" +
            "ba8f26e7fc847bcdf9a8|" +
            "826c48af9ff268ea4a88|" +
            "feaaaaeaaaaabaaaaaf8"
        },
        {
            Pattern(33),
            QrErrorCorrectionLevel.M,
            RectangularMicroQrCodeVersion.R7x99,
            7,
            99,
            "feaaabaaaaaaeaaaaabaaaaae|" +
            "827f1a9df078b2af89ab9c3ca|" +
            "ba512ff3a9cef93821be47a3e|" +
            "ba4d08d5e4ffb576e561e4462|" +
            "ba8ee3e341b8f583a4bb0566a|" +
            "82a3dea3fb1cb21a48aeadb22|" +
            "feaaabaaaaaaeaaaaabaaaabe"
        },
        {
            Pattern(17),
            QrErrorCorrectionLevel.H,
            RectangularMicroQrCodeVersion.R7x99,
            7,
            99,
            "feaaabaaaaaaeaaaaabaaaaae|" +
            "82fe7ace9987a6f8f9ab9c2c2|" +
            "ba30c7f14ffde2a0f1be47afe|" +
            "ba932a1c7cab1b0b1561e45e2|" +
            "bacd2fd8c56ffcc484bb056ea|" +
            "8226c2ec36d6afaef8aeada22|" +
            "feaaabaaaaaaeaaaaabaaaabe"
        },
        {
            Pattern(51),
            QrErrorCorrectionLevel.M,
            RectangularMicroQrCodeVersion.R7x139,
            7,
            139,
            "feaaaabaaaaaabaaaaaabaaaaaabaaaaaae|" +
            "825cb92f5eda36e2e9016e5ae4fe90f198a|" +
            "ba7a133f80cfcfc6f10e78c98ef7c92a07e|" +
            "baa99b2bc976bc219b78acf51220b689fa2|" +
            "babf667f92a247eb1fe4fe00a8b39468c2a|" +
            "820bacaee838c6cb7e81ee7bcb6efdc0062|" +
            "feaaaabaaaaaabaaaaaabaaaaaabaaaaabe"
        },
        {
            Pattern(28),
            QrErrorCorrectionLevel.H,
            RectangularMicroQrCodeVersion.R7x139,
            7,
            139,
            "feaaaabaaaaaabaaaaaabaaaaaabaaaaaae|" +
            "82dc44adf76f6ea7fb056ec9a9cebfba082|" +
            "ba1e823e90be8be7cabefe95c5f799a24be|" +
            "ba7172454dd1d4b85e80ca6546745a2de22|" +
            "baf58d7ff7b3e3d8cc5d78737977bd37caa|" +
            "82838fae01c62ef8555bae7c45ba99c3162|" +
            "feaaaabaaaaaabaaaaaabaaaaaabaaaaabe"
        },
        {
            Pattern(16),
            QrErrorCorrectionLevel.M,
            RectangularMicroQrCodeVersion.R9x43,
            9,
            43,
            "feaaaeaaaae|" +
            "82b13a09082|" +
            "ba580e8758a|" +
            "ba063a3ce4c|" +
            "bae04eff63e|" +
            "82446072ae2|" +
            "fe749f4d2aa|" +
            "001a3b7d8e2|" +
            "eaaaaeaaabe"
        },
        {
            Pattern(8),
            QrErrorCorrectionLevel.H,
            RectangularMicroQrCodeVersion.R9x43,
            9,
            43,
            "feaaaeaaaae|" +
            "8230fa390b2|" +
            "ba3d8fb759a|" +
            "bad14158f44|" +
            "bab9c5ab6fe|" +
            "82d93a03b62|" +
            "fe6cbe3822a|" +
            "00b2ebec9e2|" +
            "eaaaaeaaabe"
        },
        {
            Pattern(25),
            QrErrorCorrectionLevel.M,
            RectangularMicroQrCodeVersion.R9x59,
            9,
            59,
            "feaabaaaabaaaae|" +
            "829568b406ae65a|" +
            "babcbe18d7b4352|" +
            "ba2d8a09488380c|" +
            "ba4a52914f001fe|" +
            "820961cf1699ca2|" +
            "fe42f97ed7a55ea|" +
            "006a6af0aa85e62|" +
            "eaaabaaaabaaabe"
        },
        {
            Pattern(16),
            QrErrorCorrectionLevel.H,
            RectangularMicroQrCodeVersion.R9x59,
            9,
            59,
            "feaabaaaabaaaae|" +
            "82166d9f9aae65a|" +
            "bad6bfebe3f4352|" +
            "baf002dcfc83904|" +
            "ba10df5c930013e|" +
            "829607514c99d22|" +
            "fe58f89103a556a|" +
            "00786e3d0e85f62|" +
            "eaaabaaaabaaabe"
        },
        {
            Pattern(38),
            QrErrorCorrectionLevel.M,
            RectangularMicroQrCodeVersion.R9x77,
            9,
            77,
            "feaaaaeaaaaabaaaaab8|" +
            "827b90ab8f49ad8a7be8|" +
            "ba9bc4f26763fde417c8|" +
            "ba83819275d3402f9f30|" +
            "ba0ab5c8a23e780e1ef8|" +
            "8248f709be21e9a2b788|" +
            "fefc02e73f63bbc9fda8|" +
            "0036c4b59bd7293b7388|" +
            "eaaaaaeaaaaabaaaaaf8"
        },
        {
            Pattern(20),
            QrErrorCorrectionLevel.H,
            RectangularMicroQrCodeVersion.R9x77,
            9,
            77,
            "feaaaaeaaaaabaaaaab8|" +
            "82f4f5b594d9af485ee8|" +
            "bafb1ff205703df13dc8|" +
            "ba544703d613c2b50310|" +
            "ba5745ed3b497a239df8|" +
            "82dbbc9a215ee98b1188|" +
            "fe55f5e23f01796bafa8|" +
            "005abdac5e6568d10788|" +
            "eaaaaaeaaaaabaaaaaf8"
        },
        {
            Pattern(48),
            QrErrorCorrectionLevel.M,
            RectangularMicroQrCodeVersion.R9x99,
            9,
            99,
            "feaaabaaaaaaeaaaaabaaaaae|" +
            "822b2ac5d9f5b301f2eb9f34a|" +
            "babfc7e58357f0d1e5fa26846|" +
            "baa86aa4f45ab410608108b68|" +
            "bae291d220a370ee18558ebfe|" +
            "827746adfaa19b1af12b3c1a2|" +
            "fe61ff89aa45fd15f77a912aa|" +
            "0067aa9df313a02046ad5b0a2|" +
            "eaaaabaaaaaaeaaaaabaaaabe"
        },
        {
            Pattern(25),
            QrErrorCorrectionLevel.H,
            RectangularMicroQrCodeVersion.R9x99,
            9,
            99,
            "feaaabaaaaaaeaaaaabaaaaae|" +
            "82a9cadcfedaaf99296fdfb8a|" +
            "badddbb1ba3eeed65dbdfa346|" +
            "ba765661daa0ae64aeab64460|" +
            "babf0766fd99d54dd2b13233e|" +
            "82fd3a8fb2bbb9c811ebbb822|" +
            "fe5fdbedb507fc2a613c19e2a|" +
            "004466d4da68ab5cd2aa22da2|" +
            "eaaaabaaaaaaeaaaaabaaaabe"
        },
        {
            Pattern(71),
            QrErrorCorrectionLevel.M,
            RectangularMicroQrCodeVersion.R9x139,
            9,
            139,
            "feaaaabaaaaaabaaaaaabaaaaaabaaaaaae|" +
            "82cd5b6ef7c2bee6f244ac620a0ab404cc2|" +
            "ba941438ac71a3b696d4fcbe1427dd9e11a|" +
            "ba061f4a3a92b465a72de1d65a608f336ac|" +
            "bab94c19039eb11971db5f14ea5938cbfbe|" +
            "82283ec2834b54da94a0a498f51adcde8e2|" +
            "fe410abde383b382dd007dd3a0d3868fc2a|" +
            "0063662c7c67129b8ffe68aa856ae9aa422|" +
            "eaaaaabaaaaaabaaaaaabaaaaaabaaaaabe"
        },
        {
            Pattern(39),
            QrErrorCorrectionLevel.H,
            RectangularMicroQrCodeVersion.R9x139,
            9,
            139,
            "feaaaabaaaaaabaaaaaabaaaaaabaaaaaae|" +
            "824d942fa85efa8f9e5d6ba1bae699d7742|" +
            "baf90ff8855f139643e83e9a3617c75d79a|" +
            "badcd98e5d61e0c028092c96996e8d007a4|" +
            "bae161df1799c369e33db341f7abb02557e|" +
            "82abf8ef6ab7f4cfaaac4c5ae20e164bf62|" +
            "fe75c3ff88ec27c81841bef6076be1836aa|" +
            "007dcf2d57c3aeaffc5aecce5802fed9b22|" +
            "eaaaaabaaaaaabaaaaaabaaaaaabaaaaabe"
        },
        {
            Pattern(8),
            QrErrorCorrectionLevel.M,
            RectangularMicroQrCodeVersion.R11x27,
            11,
            27,
            "feaaaae|" +
            "82e43fa|" +
            "ba78fb2|" +
            "ba2c6f4|" +
            "ba0f35a|" +
            "82632ec|" +
            "fe3ee7e|" +
            "00cfca2|" +
            "d77516a|" +
            "bf994a2|" +
            "eaaaabe"
        },
        {
            Pattern(6),
            QrErrorCorrectionLevel.H,
            RectangularMicroQrCodeVersion.R11x27,
            11,
            27,
            "feaaaae|" +
            "826b7fa|" +
            "ba183b6|" +
            "baf4ef8|" +
            "ba58f5a|" +
            "82fe7e4|" +
            "fe42abe|" +
            "0027522|" +
            "ebdd9ea|" +
            "be2c5a2|" +
            "eaaaabe"
        },
        {
            Pattern(22),
            QrErrorCorrectionLevel.M,
            RectangularMicroQrCodeVersion.R11x43,
            11,
            43,
            "feaaaeaaaae|" +
            "8201fa91802|" +
            "ba5d4fdd5c2|" +
            "ba86c24865c|" +
            "ba553fa6146|" +
            "823ef952128|" +
            "fef965d723e|" +
            "009b53005e2|" +
            "d2620e709ea|" +
            "8928bb99622|" +
            "eaaaaeaaabe"
        },
        {
            Pattern(16),
            QrErrorCorrectionLevel.H,
            RectangularMicroQrCodeVersion.R11x43,
            11,
            43,
            "feaaaeaaaae|" +
            "82853ad9802|" +
            "ba3e9ec55c2|" +
            "ba5a031c65c|" +
            "ba029cf6146|" +
            "82bf5862020|" +
            "fe5f77072fe|" +
            "0012e9b0462|" +
            "c9dbdff096a|" +
            "ad2caac9722|" +
            "eaaaaeaaabe"
        },
        {
            Pattern(38),
            QrErrorCorrectionLevel.M,
            RectangularMicroQrCodeVersion.R11x59,
            11,
            59,
            "feaabaaaabaaaae|" +
            "822229328ef8ff2|" +
            "ba703d8a3ba117e|" +
            "ba668867e092a04|" +
            "ba6579e2e59847e|" +
            "828442d18a71f68|" +
            "fe733a3c3fea87e|" +
            "00c60174e614022|" +
            "8d6f7eebcfe09aa|" +
            "9803ea51da83362|" +
            "eaaabaaaabaaabe"
        },
        {
            Pattern(18),
            QrErrorCorrectionLevel.H,
            RectangularMicroQrCodeVersion.R11x59,
            11,
            59,
            "feaabaaaabaaaae|" +
            "82a6af9a6ab3832|" +
            "ba12788513e5bfe|" +
            "babf0cee60f7004|" +
            "ba399112ff1101e|" +
            "820eeef87e79c60|" +
            "fe425b23afc38be|" +
            "00c60dac5a0eba2|" +
            "8afcfe26abfb12a|" +
            "aa066e95de99262|" +
            "eaaabaaaabaaabe"
        },
        {
            Pattern(49),
            QrErrorCorrectionLevel.M,
            RectangularMicroQrCodeVersion.R11x77,
            11,
            77,
            "feaaaaeaaaaabaaaaab8|" +
            "82ce1aae24706f5e3fc8|" +
            "ba5f77e03dfc3d2845f8|" +
            "bac20d25920fc854a810|" +
            "ba3ee06f683c37f611f8|" +
            "82dbed10a4b0c94c7aa0|" +
            "fe3919f812e2791aa0f8|" +
            "000b4185e0620ce50588|" +
            "e2d431eca0ecffb424a8|" +
            "a92449a09721eb14cf88|" +
            "eaaaaaeaaaaabaaaaaf8"
        },
        {
            Pattern(27),
            QrErrorCorrectionLevel.H,
            RectangularMicroQrCodeVersion.R11x77,
            11,
            77,
            "feaaaaeaaaaabaaaaab8|" +
            "824b8db514bc69dfe8c8|" +
            "ba309fe10043be17a338|" +
            "ba115eb67a2109bd4410|" +
            "ba7c75c395b216940438|" +
            "82588cbfc0c5a64e7680|" +
            "fe572bde58149160d3f8|" +
            "00065a21b48564988b88|" +
            "f6aca7e8f13bfd9be6a8|" +
            "b53766a5c59dad44cb88|" +
            "eaaaaaeaaaaabaaaaaf8"
        },
        {
            Pattern(64),
            QrErrorCorrectionLevel.M,
            RectangularMicroQrCodeVersion.R11x99,
            11,
            99,
            "feaaabaaaaaaeaaaaabaaaaae|" +
            "82ec22fa8a97a6afdc291eeaa|" +
            "bab8fbd06bcdea345eb889d16|" +
            "bae48a47680b35728d8e319e0|" +
            "ba9125e3ef99e51f6cfd0ed2a|" +
            "829910ccd4b8a20680e84d6ec|" +
            "fe0be1e7d017497b1137edffe|" +
            "009fb005c0a235b5e288d3522|" +
            "e03497d4dc28f5822ab9b8c6a|" +
            "a1a46ad2965bb61a5ca9cd962|" +
            "eaaaabaaaaaaeaaaaabaaaabe"
        },
        {
            Pattern(34),
            QrErrorCorrectionLevel.H,
            RectangularMicroQrCodeVersion.R11x99,
            11,
            99,
            "feaaabaaaaaaeaaaaabaaaaae|" +
            "8265e29fc3b5a279c82d44f9a|" +
            "badbf3be8153e8490fb863f06|" +
            "ba3b820aae3339bcadaef2ae0|" +
            "bacf15e40ae2e6e79cf90ea2a|" +
            "820e1c6f49251e1b50e24d3e4|" +
            "fe5065da6040ebee95b6e933e|" +
            "00c828439c49ba05a648f34a2|" +
            "e098c3e37594e7beaeb8a8cea|" +
            "a8b102c12407b75ca0a90f862|" +
            "eaaaabaaaaaaeaaaaabaaaabe"
        },
        {
            Pattern(94),
            QrErrorCorrectionLevel.M,
            RectangularMicroQrCodeVersion.R11x139,
            11,
            139,
            "feaaaabaaaaaabaaaaaabaaaaaabaaaaaae|" +
            "82058eec992a7a8ed9c22e2ae696d3fb4e2|" +
            "ba98c2fe537b3be65dd4f9c98e87a39c64a|" +
            "ba428c480743ae8236b98d471ad0a58d1bc|" +
            "bad367367537018694d53b33c07bba63376|" +
            "82dc6908d9c9ca29056dce582be4ab21928|" +
            "fe8d7057812825f5c45633c93cc5c5841be|" +
            "0008ee0a35db02d55a9d6839b1305f9e662|" +
            "e1847ebbc79b8bab13e2bab8ab23adb16ea|" +
            "affce16dd028a2fed2626f23cbb6cbd83e2|" +
            "eaaaaabaaaaaabaaaaaabaaaaaabaaaaabe"
        },
        {
            Pattern(46),
            QrErrorCorrectionLevel.H,
            RectangularMicroQrCodeVersion.R11x139,
            11,
            139,
            "feaaaabaaaaaabaaaaaabaaaaaabaaaaaae|" +
            "8284a0adc97032c62fc0a9b0bbea80e22d2|" +
            "bafccefd5b876bea3e73fb22bc73cb159fa|" +
            "ba9da4a44bf482d11fafe17716a4563b26c|" +
            "ba9c6bfb6d913316a2a511f887c1de6e8a6|" +
            "824c18c7f1f5e4bfaf0e41dcc3ea5ac5e20|" +
            "febb1996fcac07cc3a12ff1a5e3f76bd37e|" +
            "007769abc6ae52c40e5ee6ec9a4283613e2|" +
            "e1549abc59526fabced7bd04a017ea6166a|" +
            "aaa9dfaaf23732cdb92069eff3fea9c52e2|" +
            "eaaaaabaaaaaabaaaaaabaaaaaabaaaaabe"
        },
        {
            Pattern(16),
            QrErrorCorrectionLevel.M,
            RectangularMicroQrCodeVersion.R13x27,
            13,
            27,
            "feaaaae|" +
            "82104ca|" +
            "bac895e|" +
            "ba68810|" +
            "baf09d2|" +
            "823253c|" +
            "fed9646|" +
            "0093518|" +
            "e6e9b3e|" +
            "6cf8222|" +
            "ace00aa|" +
            "9393c22|" +
            "eaaaabe"
        },
        {
            Pattern(8),
            QrErrorCorrectionLevel.H,
            RectangularMicroQrCodeVersion.R13x27,
            13,
            27,
            "feaaaae|" +
            "82998ca|" +
            "baa655e|" +
            "bab9c10|" +
            "baa93de|" +
            "82b7b34|" +
            "fef0846|" +
            "0093410|" +
            "de5dbfe|" +
            "6d367a2|" +
            "b75762a|" +
            "8e28d22|" +
            "eaaaabe"
        },
        {
            Pattern(31),
            QrErrorCorrectionLevel.M,
            RectangularMicroQrCodeVersion.R13x43,
            13,
            43,
            "feaaaeaaaae|" +
            "82ffaa8ee6a|" +
            "bae0ef0e016|" +
            "bac36945f88|" +
            "bab2d6d6c1a|" +
            "826538fd1c0|" +
            "fe68c59ed6a|" +
            "00c10940edc|" +
            "dea01dd3b7e|" +
            "24e01bff562|" +
            "c523fe3f62a|" +
            "a079da986a2|" +
            "eaaaaeaaabe"
        },
        {
            Pattern(17),
            QrErrorCorrectionLevel.H,
            RectangularMicroQrCodeVersion.R13x43,
            13,
            43,
            "feaaaeaaaae|" +
            "8277cb62e6a|" +
            "ba813f12016|" +
            "ba1233d1f88|" +
            "bae2bc72c1a|" +
            "82ea001d1c0|" +
            "fe454666d6a|" +
            "00afbae4fd4|" +
            "cb356d43bbe|" +
            "3fe980334e2|" +
            "b5d70e9f6aa|" +
            "8d8ddad87a2|" +
            "eaaaaeaaabe"
        },
        {
            Pattern(43),
            QrErrorCorrectionLevel.M,
            RectangularMicroQrCodeVersion.R13x59,
            13,
            59,
            "feaabaaaabaaaae|" +
            "82db68baf28a4b2|" +
            "ba0138aa33efece|" +
            "bae04f3dead0150|" +
            "ba10314ecfa62c2|" +
            "82342959024cb18|" +
            "fe0ad79ef1de5b2|" +
            "004fefb4eace29c|" +
            "ef87b6438f2cabe|" +
            "704604df5080522|" +
            "8b183b1f63d516a|" +
            "94bcea087aee022|" +
            "eaaabaaaabaaabe"
        },
        {
            Pattern(23),
            QrErrorCorrectionLevel.H,
            RectangularMicroQrCodeVersion.R13x59,
            13,
            59,
            "feaabaaaabaaaae|" +
            "8259a918ce8a8da|" +
            "ba68bc7663d3eae|" +
            "ba310099b0eed08|" +
            "ba5a38cb97bcb02|" +
            "82a3a87a447ee38|" +
            "fe419213e3d76c2|" +
            "004f288df071394|" +
            "d32e140461b227e|" +
            "6f41210020c9ea2|" +
            "81a0bbcf8bc65ea|" +
            "96e46d178acc722|" +
            "eaaabaaaabaaabe"
        },
        {
            Pattern(61),
            QrErrorCorrectionLevel.M,
            RectangularMicroQrCodeVersion.R13x77,
            13,
            77,
            "feaaaaeaaaaabaaaaab8|" +
            "823915a595bbe9de7ee8|" +
            "ba2a55f4b9e178c6b918|" +
            "ba41c89c696e48dd6d60|" +
            "ba55855ccdb49ba61e68|" +
            "826ccca9823d6b3130a0|" +
            "fe102d797fdd5af9a008|" +
            "00bc3cb16245e9a8fd70|" +
            "ef0b8b59935c509a33f8|" +
            "4cf7ebad479b4b961988|" +
            "a3f7a7f9f1c57d4027a8|" +
            "984e4abbe1062cd4f288|" +
            "eaaaaaeaaaaabaaaaaf8"
        },
        {
            Pattern(34),
            QrErrorCorrectionLevel.H,
            RectangularMicroQrCodeVersion.R13x77,
            13,
            77,
            "feaaaaeaaaaabaaaaab8|" +
            "82b376bbb8cf2bde1ec8|" +
            "ba4d69e32de878c6d968|" +
            "ba98c19ef93f48496d40|" +
            "ba0f9fef010af92b1768|" +
            "82e80d0974c80b9739a0|" +
            "feb89eef81d05279ad08|" +
            "00db4133cb3189e8e950|" +
            "e85016e9e47b109a60f8|" +
            "474c7e0fc62cc79e8f88|" +
            "882591fd0095b854c5a8|" +
            "b3a24fbe0f7c6e68f688|" +
            "eaaaaaeaaaaabaaaaaf8"
        },
        {
            Pattern(83),
            QrErrorCorrectionLevel.M,
            RectangularMicroQrCodeVersion.R13x99,
            13,
            99,
            "feaaabaaaaaaeaaaaabaaaaae|" +
            "821f82a5a550b58a81e902a12|" +
            "ba0ea3860554e8330efa5bbfa|" +
            "baa43c1671ee137d9dae7b4d8|" +
            "ba7559195e8defacba15fe9ea|" +
            "82c9841c08a231046163b235c|" +
            "fe34b7f18c8f56c2f15071436|" +
            "00e0f2d223961c555668dbb18|" +
            "d42b65830224d70c37b2ec0be|" +
            "6941985376e889906084baba2|" +
            "817e8ff9d955fc3bcf39f2daa|" +
            "bc188a916f27af4d2f6c611e2|" +
            "eaaaabaaaaaaeaaaaabaaaabe"
        },
        {
            Pattern(39),
            QrErrorCorrectionLevel.H,
            RectangularMicroQrCodeVersion.R13x99,
            13,
            99,
            "feaaabaaaaaaeaaaaabaaaaae|" +
            "8290e2ee4085b364cf6aee5f2|" +
            "ba6f6bc9c3e5ec29cb3dfe4f6|" +
            "ba7172284cdeb8bc3aa833f70|" +
            "ba3d65e4bcc0d60f88f94bbea|" +
            "8248e84d98b624aa20a9c520c|" +
            "fe79477e535c6f74c63f6ccc6|" +
            "005aba890a2ea097568601410|" +
            "f91a2104dc604c05099b9307e|" +
            "513d86f7c8b98e1b1ae5a7e22|" +
            "8d2e53c3c6d8e679d53c59b2a|" +
            "ac5752fbe0a8a272ab2a136e2|" +
            "eaaaabaaaaaaeaaaaabaaaabe"
        },
        {
            Pattern(118),
            QrErrorCorrectionLevel.M,
            RectangularMicroQrCodeVersion.R13x139,
            13,
            139,
            "feaaaabaaaaaabaaaaaabaaaaaabaaaaaae|" +
            "82f62a2b2da5aaa44962a890d11aaa4f792|" +
            "ba23047ac389efe56538381e76739cc6e7a|" +
            "ba06c4c0e9c0d688ef1e24cc7ea68711d3c|" +
            "ba3e093e1c35691e5e229cb9fa932466c3a|" +
            "82979ca466ffc26e0c732b24f61a5d630b0|" +
            "fe9e267f73360f29c6b270a43011d49573a|" +
            "00d50ae81bb67cf79dc7c0e7197060bcadc|" +
            "f768cefe18e369d43af33c2165fb38e50fe|" +
            "4ca210e314431a9e934ee748858c06e62e2|" +
            "c6bab0ba54f917a30bbcf89fd5a3ff4c32a|" +
            "b8eeb9eb8e5cdacbb2946ea44b728e79162|" +
            "eaaaaabaaaaaabaaaaaabaaaaaabaaaaabe"
        },
        {
            Pattern(61),
            QrErrorCorrectionLevel.H,
            RectangularMicroQrCodeVersion.R13x139,
            13,
            139,
            "feaaaabaaaaaabaaaaaabaaaaaabaaaaaae|" +
            "827f77e9f12ef2f5a83a2cbcc7faa889faa|" +
            "ba460e39da70af8db015bd28922bef95336|" +
            "badd0ecfdb4b24b770ae0cb31a1a977111c|" +
            "ba7d6a37ba7c53a919f1327d7d2b99e21ba|" +
            "8210dfa747f0729f16c280acd62a303e3f0|" +
            "fee7d1d3935b7f6c3b0e18cb8ca5e6c721a|" +
            "00125ee38222fe44c079ab7e354867167d4|" +
            "ea9dddd87cd7850af4c177b6e505397f03e|" +
            "66f0faca49141499bb256444a61c7c39162|" +
            "965ef2f93ac8078ec8807a6f151b873cfaa|" +
            "9268ce6c494c22bb98ee6e87db6297f3062|" +
            "eaaaaabaaaaaabaaaaaabaaaaaabaaaaabe"
        },
        {
            Pattern(39),
            QrErrorCorrectionLevel.M,
            RectangularMicroQrCodeVersion.R15x43,
            15,
            43,
            "feaaaeaaaae|" +
            "82d1eae21f2|" +
            "bac8de1666e|" +
            "ba22e30c738|" +
            "ba85e649cda|" +
            "82cce2a6c34|" +
            "fe0f27d2496|" +
            "0046638afac|" +
            "c1b734a26c6|" +
            "5f85f2f4898|" +
            "9ea2c641d3e|" +
            "7696fb8aea2|" +
            "cb313f8766a|" +
            "82972af8be2|" +
            "eaaaaeaaabe"
        },
        {
            Pattern(18),
            QrErrorCorrectionLevel.H,
            RectangularMicroQrCodeVersion.R15x43,
            15,
            43,
            "feaaaeaaaae|" +
            "82557b6016a|" +
            "baac6f5276e|" +
            "baff1364c58|" +
            "badd45f3aaa|" +
            "824f2007904|" +
            "fe7cd48ceb6|" +
            "006ea1105cc|" +
            "fc124d76e86|" +
            "2ca0a2b4190|" +
            "9a5b8ff57fe|" +
            "12777bd6722|" +
            "9014aecfeea|" +
            "b6c17af34e2|" +
            "eaaaaeaaabe"
        },
        {
            Pattern(55),
            QrErrorCorrectionLevel.M,
            RectangularMicroQrCodeVersion.R15x59,
            15,
            59,
            "feaabaaaabaaaae|" +
            "82386d3216f2c1a|" +
            "baebfd79a7c6366|" +
            "ba892e07688d1d8|" +
            "bac2b0a627ec64a|" +
            "82894ef7d6966c4|" +
            "feccf479a182786|" +
            "0062a4af1cc54e4|" +
            "f7f1daf62f6e66e|" +
            "63dde3c10eb5958|" +
            "bfb9b35c339057e|" +
            "21fd6956f4d27e2|" +
            "bd33bee7aba26ea|" +
            "9f8aebc31ed4962|" +
            "eaaabaaaabaaabe"
        },
        {
            Pattern(30),
            QrErrorCorrectionLevel.H,
            RectangularMicroQrCodeVersion.R15x59,
            15,
            59,
            "feaabaaaabaaaae|" +
            "82b16c6402ac5aa|" +
            "ba8c3aa4379a122|" +
            "ba5c8bca004e794|" +
            "ba9e99a22100fbe|" +
            "8212087c5a488d4|" +
            "fea8b25da58c2b6|" +
            "00b9a48c74f2144|" +
            "c75f3ccb0b224ae|" +
            "613fcf71d221450|" +
            "a8fdb2ecb5ef5be|" +
            "1ecf0a44460cc62|" +
            "d574f8035f90a6a|" +
            "933f2f770e80462|" +
            "eaaabaaaabaaabe"
        },
        {
            Pattern(75),
            QrErrorCorrectionLevel.M,
            RectangularMicroQrCodeVersion.R15x77,
            15,
            77,
            "feaaaaeaaaaabaaaaab8|" +
            "826c47aa9f02ae9a3548|" +
            "bacd04f72bafb97bd588|" +
            "baaa608cb9ce8e02cc50|" +
            "ba22cec3eb8a94867cf8|" +
            "82b56d9fbed4e78c3f80|" +
            "fe03a47d65fd53ae4ac8|" +
            "00717cb8578b2873c150|" +
            "fef21dd7c21ada244a78|" +
            "583755b1be3cc91637e0|" +
            "ae30c17c7ba514cb74f8|" +
            "245ccf185bfead636688|" +
            "ec49fbff6a29fc96cca8|" +
            "ab6dd9b6b2546a3c1488|" +
            "eaaaaaeaaaaabaaaaaf8"
        },
        {
            Pattern(38),
            QrErrorCorrectionLevel.H,
            RectangularMicroQrCodeVersion.R15x77,
            15,
            77,
            "feaaaaeaaaaabaaaaab8|" +
            "82e380ae60d5a96ed3e8|" +
            "baaf8ee9cae33a55dfb8|" +
            "ba736430c6b9eef14130|" +
            "ba7a3257e1a2bea8b648|" +
            "8225531e8816ae02d060|" +
            "fe310d4bc1b67ec48088|" +
            "0021009b1b5f65f33d50|" +
            "e49d68c05dde12c97af8|" +
            "433987ae59df421a8bc0|" +
            "861b635845ef9a4387f8|" +
            "16f85f3c325142215088|" +
            "f1ab0be4b5b1ba0606a8|" +
            "acbf54be49cf2fb69888|" +
            "eaaaaaeaaaaabaaaaaf8"
        },
        {
            Pattern(98),
            QrErrorCorrectionLevel.M,
            RectangularMicroQrCodeVersion.R15x99,
            15,
            99,
            "feaaabaaaaaaeaaaaabaaaaae|" +
            "8288da94e91caa4af86fdf0aa|" +
            "bae503b3e149fa38a03f5d452|" +
            "ba0de279e6910071e7e191624|" +
            "ba750973ad135a0b7173cbace|" +
            "82f9c8b60230bb79884aec344|" +
            "fe7161b579d57c610e9fdff16|" +
            "008bf056a3e3b0aad78ca9cf0|" +
            "e08ca93796e8e4beb6d9a7d0a|" +
            "603ef06a6d19218e1463ef03c|" +
            "a51b03924445d978c41b9f17e|" +
            "2d79d290462d11e9efc1338e2|" +
            "d1c237edc653f0003c79bd7aa|" +
            "a0437addf674b77c8a6d1e9a2|" +
            "eaaaabaaaaaaeaaaaabaaaabe"
        },
        {
            Pattern(54),
            QrErrorCorrectionLevel.H,
            RectangularMicroQrCodeVersion.R15x99,
            15,
            99,
            "feaaabaaaaaaeaaaaabaaaaae|" +
            "820c56a9a031a8864929e43fa|" +
            "ba8b03e666cfe920cd3c68876|" +
            "badca25da764273cb54e75d20|" +
            "ba393758bb7e7a9cadd3a1302|" +
            "827504303d9ca5df1e090829c|" +
            "fec4814714e9f73fa3b5f5b06|" +
            "009c36af55ca86bd6609ca9e0|" +
            "e0b0b55d47f4f9c2ca718410a|" +
            "578cd472af493c212ac3cf734|" +
            "82c1219b7dee79ee27d2393be|" +
            "2f00c2d9e87faabdec4636962|" +
            "da6f2b8f352cfdfa87fe1ed2a|" +
            "a08b42e7d23aaf7638aaf68a2|" +
            "eaaaabaaaaaaeaaaaabaaaabe"
        },
        {
            Pattern(140),
            QrErrorCorrectionLevel.M,
            RectangularMicroQrCodeVersion.R15x139,
            15,
            139,
            "feaaaabaaaaaabaaaaaabaaaaaabaaaaaae|" +
            "82aa74af06f9a68acd612b9d6ade9ba38e2|" +
            "ba03b6fc905883f45299f96b3f57933d62a|" +
            "ba2a2d6c8aaa4e21e069644e556ec05042c|" +
            "bad44979bfcb774b32c93a31e6b1bee22a6|" +
            "82b0d8c70726508995260e1ef51e68be3e8|" +
            "fe1d3851409f5da28a1d37d40c9b7348486|" +
            "00134b6201c27e61330801427a26cf2672c|" +
            "e378ecba60da77f6d343f66976955a41d26|" +
            "60d262a6a128762e648b06d9a1fc1361278|" +
            "9cff3d3e7062594a85dd31c65ccd36ed6be|" +
            "5fea4cc796d774211216efcd5ca2651caa2|" +
            "8cf17dfd855943a7262a7f3eb7abbc220ea|" +
            "8ee12fab69078e8b2cf6ad99ce0add34122|" +
            "eaaaaabaaaaaabaaaaaabaaaaaabaaaaabe"
        },
        {
            Pattern(77),
            QrErrorCorrectionLevel.H,
            RectangularMicroQrCodeVersion.R15x139,
            15,
            139,
            "feaaaabaaaaaabaaaaaabaaaaaabaaaaaae|" +
            "8226c92d599032e582b5af08f5eada7eeea|" +
            "ba66c179751f5fa65b2b7b0959b7c636ede|" +
            "baf936082fdfb6fc0971c3670de65f39078|" +
            "ba9fd09754e8e1d596b09498132738e6dfa|" +
            "822d72eb739af4a4cc0f2320f2548878c00|" +
            "feb6a97e6006e32ce1c2db4566e3c61de36|" +
            "00202de5498bded1bca70623071a8f00ebc|" +
            "d96ab472b1ac3b9aa8b25189cf97f9fafa6|" +
            "69768ba36791b2197227a020e9de7ce9370|" +
            "85ca05364b76d3e578deb5bc16d9e700c7e|" +
            "403d26a6e6b7724f33912267ce2adc86b22|" +
            "c08e27780fbcabe3548b3f43eab7f04186a|" +
            "8b3801ec1b200acd81f1eecbf776990cc22|" +
            "eaaaaabaaaaaabaaaaaabaaaaaabaaaaabe"
        },
        {
            Pattern(44),
            QrErrorCorrectionLevel.M,
            RectangularMicroQrCodeVersion.R17x43,
            17,
            43,
            "feaaaeaaaae|" +
            "82496bf1c0a|" +
            "ba282eb8926|" +
            "ba86d187104|" +
            "ba9d8c252e2|" +
            "82ed91f6194|" +
            "fee4eef9176|" +
            "001efb6fe18|" +
            "fd2cfcb698a|" +
            "5bf792c31c0|" +
            "c406a65d12a|" +
            "6350f997dbc|" +
            "974d3ce68fe|" +
            "15d22ac1be2|" +
            "c9b9ce1c26a|" +
            "9e6bdbbe3a2|" +
            "eaaaaeaaabe"
        },
        {
            Pattern(24),
            QrErrorCorrectionLevel.H,
            RectangularMicroQrCodeVersion.R17x43,
            17,
            43,
            "feaaaeaaaae|" +
            "82cdcb7636a|" +
            "ba4daf64116|" +
            "ba51c133398|" +
            "bad7dec2a26|" +
            "82761ba5ee0|" +
            "fe726dd8c66|" +
            "00c0734c4c0|" +
            "e5f21f368da|" +
            "0500aa20180|" +
            "a83f34dc92a|" +
            "52fde9372b4|" +
            "bd420f17c3e|" +
            "180cea73062|" +
            "a525eeb4eea|" +
            "bf91ab974a2|" +
            "eaaaaeaaabe"
        },
        {
            Pattern(63),
            QrErrorCorrectionLevel.M,
            RectangularMicroQrCodeVersion.R17x59,
            17,
            59,
            "feaabaaaabaaaae|" +
            "8261ac41deba622|" +
            "ba03fcddc3d43c2|" +
            "ba6167352231490|" +
            "baa038b6a5a9eaa|" +
            "825800a5ee93324|" +
            "fea137b575cda22|" +
            "00dbce9dfe1d464|" +
            "d64170e21938c9e|" +
            "6bb7e02d8afee68|" +
            "d94c9f359db53b2|" +
            "0d4c09d878c8ffc|" +
            "eef8dbd20be3abe|" +
            "1aa0e271da10e22|" +
            "a3b738457b8182a|" +
            "8a416f157ee50e2|" +
            "eaaabaaaabaaabe"
        },
        {
            Pattern(32),
            QrErrorCorrectionLevel.H,
            RectangularMicroQrCodeVersion.R17x59,
            17,
            59,
            "feaabaaaabaaaae|" +
            "82ebeb00c6ee7e2|" +
            "ba6a7ba80b94782|" +
            "bab8083bd2fb080|" +
            "baf5305c53e2e8a|" +
            "82c12922ba32714|" +
            "fecaf02311dd21e|" +
            "009643fc7e0d5e4|" +
            "ee7a5aaca538c5e|" +
            "6aa664ca3e7eee8|" +
            "f3597ca015751b2|" +
            "131b26c862ca6f4|" +
            "f849d36b71e2e7e|" +
            "007da0e2f6327a2|" +
            "d24afe6943920aa|" +
            "aa002f331eb41e2|" +
            "eaaabaaaabaaabe"
        },
        {
            Pattern(87),
            QrErrorCorrectionLevel.M,
            RectangularMicroQrCodeVersion.R17x77,
            17,
            77,
            "feaaaaeaaaaabaaaaab8|" +
            "828f39a013e0ab0a9188|" +
            "ba2b80fd40d63ed70308|" +
            "bac9baa6254b80af4210|" +
            "baf192666c7774b8aab8|" +
            "820f868ffe38cb543ce0|" +
            "fef68ee271a4ddcbc878|" +
            "001f9825b103af335690|" +
            "f8e0114c13aaf75b3e78|" +
            "47025e2bc19b6acab1a0|" +
            "ad77716431c39fde4ac8|" +
            "5ec121261ba5255088f0|" +
            "e79ac94754f872caabf8|" +
            "75bda8b2ff20e27c7d88|" +
            "cc6252e0e50cfe20e2a8|" +
            "96cd45bd8307af710188|" +
            "eaaaaaeaaaaabaaaaaf8"
        },
        {
            Pattern(42),
            QrErrorCorrectionLevel.H,
            RectangularMicroQrCodeVersion.R17x77,
            17,
            77,
            "feaaaaeaaaaabaaaaab8|" +
            "820564a89436eb6d66a8|" +
            "ba41a1e798763a43a418|" +
            "ba116119e37dad175490|" +
            "baa73250df6df1160178|" +
            "829ace0b54e143162ea0|" +
            "fe04a75fd2429ff5d528|" +
            "007fff8dfd9a4659eb90|" +
            "b55b4bfe9c55f49c58b8|" +
            "275eadacabeccdc6f2e0|" +
            "abe89ff8d65af9c3a148|" +
            "6955800cd3c6cc395cd0|" +
            "945775d6af14930458f8|" +
            "4f1766a80d39e6db5b88|" +
            "f3b888f80c64ff8178a8|" +
            "bb8d2fa7bd6bad75ed88|" +
            "eaaaaaeaaaaabaaaaaf8"
        },
        {
            Pattern(110),
            QrErrorCorrectionLevel.M,
            RectangularMicroQrCodeVersion.R17x99,
            17,
            99,
            "feaaabaaaaaaeaaaaabaaaaae|" +
            "82a9faa44f34a2e45dedd0c9a|" +
            "bacd038899a7f9e5193d35efa|" +
            "bae6f24235ce1b05d12d522f4|" +
            "ba5ea3e77645725a2dbf0f95e|" +
            "824f3aff421f01cf23aa869f8|" +
            "fe59bff125e15c3742359597a|" +
            "00476eae90fd2cc5d38f69b50|" +
            "c4190953a7beeacd9cf70b256|" +
            "78db2ea5a76cb0963f21de7bc|" +
            "bd8a7382ed4455305f10e8f66|" +
            "6fa99cc314cd9e2d7f07b1678|" +
            "9a856d6533dada2e607d1fb3e|" +
            "720520143983a22c11c976d22|" +
            "bf3a27ebc04ff6f59bbb11dea|" +
            "a46a46d663dcbcc1552c512e2|" +
            "eaaaabaaaaaaeaaaaabaaaabe"
        },
        {
            Pattern(62),
            QrErrorCorrectionLevel.H,
            RectangularMicroQrCodeVersion.R17x99,
            17,
            99,
            "feaaabaaaaaaeaaaaabaaaaae|" +
            "822072aa8920afc8bea91e6a2|" +
            "baa143e8026dee5f97f899e26|" +
            "ba3474f2a26581b4d981a1020|" +
            "ba09ed9d857af8b93852ceac2|" +
            "82c61e54dac2bc283a699e8d4|" +
            "fe2b37476b515bbe471170ade|" +
            "00e540d09d800f655b2d955f0|" +
            "fe257729e216d4b07bdcd7256|" +
            "6fa33adef12a2f2370e9cecbc|" +
            "a8a6e30cda9079a49296785f6|" +
            "2c56b22daca7328df740b8970|" +
            "a17411235f28e2bae9122f1fe|" +
            "4b34a27f1ed11f5a3bab46ca2|" +
            "a6aa7785f04afa09fe3d7416a|" +
            "872bced7493eb6d347af59fe2|" +
            "eaaaabaaaaaaeaaaaabaaaabe"
        },
        {
            Pattern(168),
            QrErrorCorrectionLevel.M,
            RectangularMicroQrCodeVersion.R17x139,
            17,
            139,
            "feaaaabaaaaaabaaaaaabaaaaaabaaaaaae|" +
            "824e05698d6aaeaf98f6acb6995a88f1bba|" +
            "bae0687d1438f7d3dd807a0e7723cfd892e|" +
            "ba4067c4018db8e38a4769cb7c1e83c6304|" +
            "ba0896b83e625954e8a6fe0ddf9fb4a1d22|" +
            "821368e4a037488ab18307602b8e1dd2b58|" +
            "fedb6057fe02adf4831512c61b3fd14d19a|" +
            "002fcfefdc9b5c75ee146ee94a0c6639f94|" +
            "ff6e34b6e22cab2ebce31ab1da0d3eee9fa|" +
            "44f8b2a6216fe8c8a961ec9b0e1c70e74d0|" +
            "a2c20a5f7420ab84ec54bd7d9df7a3dd95a|" +
            "3911110bd9d15c539339ab07d974d6997bc|" +
            "eeec0717c9dcf12637bdd22b630f798937e|" +
            "417794a3dd072cc6e2c227784708e0aac62|" +
            "c9e81c790fa8b7b7955abddf040bc638d6a|" +
            "91d424687a5ec6f9c59c69ae7876946d062|" +
            "eaaaaabaaaaaabaaaaaabaaaaaabaaaaabe"
        },
        {
            Pattern(83),
            QrErrorCorrectionLevel.H,
            RectangularMicroQrCodeVersion.R17x139,
            17,
            139,
            "feaaaabaaaaaabaaaaaabaaaaaabaaaaaae|" +
            "82c5b7ed6cdfdae03af6ee80c096b07182a|" +
            "ba81de3b6232ebf88072f8fb1c3ba7fe302|" +
            "ba984606a33898a14619684a8d50c596b24|" +
            "ba53e79385bc79e903cb397b67071d47c2a|" +
            "828d6ce5299ddc8e0f1f620edb0458fb084|" +
            "fe20b69d22beb901a3f3fcbd91f5037ca36|" +
            "00675d8683f3769e37f12c4188c6bf5d00c|" +
            "c9dc3917fc6cfb074a3db45162cb18a303a|" +
            "462903213f87b60ae3250aa6e39671ef1c0|" +
            "be3f3edd4194a776912e53aec4b1e04e38a|" +
            "1a909aadd08440d344344f51acfc18558b4|" +
            "fa7acfd678dfc74847d5b310bf077cb51be|" +
            "4f721b622414407aff26c76bdbee6bdbde2|" +
            "830b11b8aefdc7aeaae4384bd40ff14c5ea|" +
            "aa3dde2f2b88a2e1fd686f20343eafc2362|" +
            "eaaaaabaaaaaabaaaaaabaaaaaabaaaaabe"
        },
    };

    /// <summary>
    /// Gets the cases of <see cref="ChoosesTheSmallestArea"/>: digits at levels M and
    /// H, with the version the reference implementation chooses.
    /// </summary>
    public static TheoryData<string, QrErrorCorrectionLevel, int, int, string> ChoosesTheSmallestAreaData { get; } = new()
    {
        {
            Digits(1),
            QrErrorCorrectionLevel.M,
            11,
            27,
            "feaaaae|" +
            "82ee78a|" +
            "ba786ba|" +
            "ba29030|" +
            "ba1fd46|" +
            "82730ec|" +
            "feb2a7e|" +
            "00c8ea2|" +
            "d19636a|" +
            "af5c4a2|" +
            "eaaaabe"
        },
        {
            Digits(12),
            QrErrorCorrectionLevel.M,
            11,
            27,
            "feaaaae|" +
            "82ea34a|" +
            "ba7ef32|" +
            "ba29e1c|" +
            "ba15196|" +
            "8269aec|" +
            "feaf47e|" +
            "0023ea2|" +
            "f0a156a|" +
            "bea9ea2|" +
            "eaaaabe"
        },
        {
            Digits(13),
            QrErrorCorrectionLevel.M,
            11,
            27,
            "feaaaae|" +
            "82e634a|" +
            "ba78f3a|" +
            "ba2ca1c|" +
            "ba16196|" +
            "827faec|" +
            "fe1447e|" +
            "008cea2|" +
            "f7c056a|" +
            "b31eea2|" +
            "eaaaabe"
        },
        {
            Digits(20),
            QrErrorCorrectionLevel.M,
            13,
            27,
            "feaaaae|" +
            "821ea0a|" +
            "bacdb02|" +
            "ba6b530|" +
            "bae1fea|" +
            "8238d28|" +
            "fe2ca1a|" +
            "006d518|" +
            "c07633e|" +
            "5ea3822|" +
            "a2656aa|" +
            "86a8622|" +
            "eaaaabe"
        },
        {
            Digits(30),
            QrErrorCorrectionLevel.M,
            11,
            43,
            "feaaaeaaaae|" +
            "8205abdafaa|" +
            "ba56be51b52|" +
            "ba84c809414|" +
            "ba5e46ef28a|" +
            "823f0198128|" +
            "fe80879e43e|" +
            "008623fb3e2|" +
            "c32b4f445ea|" +
            "94662acde22|" +
            "eaaaaeaaabe"
        },
        {
            Digits(45),
            QrErrorCorrectionLevel.M,
            9,
            59,
            "feaabaaaabaaaae|" +
            "8298e9611e8aa12|" +
            "bab4bc94a7a279e|" +
            "ba2728d76a3060c|" +
            "ba53b2ad4d8d7fe|" +
            "8217e058c61caa2|" +
            "fe7bfa8817f99ea|" +
            "00192f486e95a62|" +
            "eaaabaaaabaaabe"
        },
        {
            Digits(60),
            QrErrorCorrectionLevel.M,
            13,
            43,
            "feaaaeaaaae|" +
            "82f36bc27e2|" +
            "baea3e71dbe|" +
            "bac1997c88c|" +
            "babfdfb142e|" +
            "8273f3800fc|" +
            "fedeedc5ac6|" +
            "005d98074dc|" +
            "d79e7579d7e|" +
            "14d22940d62|" +
            "f070df0002a|" +
            "abafdb6d0a2|" +
            "eaaaaeaaabe"
        },
        {
            Digits(80),
            QrErrorCorrectionLevel.M,
            17,
            43,
            "feaaaeaaaae|" +
            "8248ca5d982|" +
            "ba210fb977a|" +
            "ba8890d0074|" +
            "ba99f6eee32|" +
            "82edc316fc0|" +
            "fe93df6748e|" +
            "0015bb7c18c|" +
            "e3a9bdf83f6|" +
            "32d77199984|" +
            "9cd02d86ec6|" +
            "20f740303bc|" +
            "cd391ee84fe|" +
            "7fa88a85de2|" +
            "dabedf4bc6a|" +
            "88d8fa5c9a2|" +
            "eaaaaeaaabe"
        },
        {
            Digits(100),
            QrErrorCorrectionLevel.M,
            11,
            77,
            "feaaaaeaaaaabaaaaab8|" +
            "82c6a4a8e54b6bb382c8|" +
            "ba5eb5e5dc8ffaa76118|" +
            "bacfadaf12af4aea5f20|" +
            "ba23dbd3c8503d0d2fc8|" +
            "82cf5397fe3e6c96b2a0|" +
            "fecb3b4afc1dfa9580f8|" +
            "00806421307101f33d88|" +
            "f75511f3c5ecbe49dca8|" +
            "879293a924dfef994f88|" +
            "eaaaaaeaaaaabaaaaaf8"
        },
        {
            Digits(150),
            QrErrorCorrectionLevel.M,
            15,
            77,
            "feaaaaeaaaaabaaaaab8|" +
            "8269ffa3ca44acb09f08|" +
            "bacbdfff30783f366828|" +
            "baadbc1a33ea6264e730|" +
            "ba238e41f0adf157f4d8|" +
            "82bb5a1be1a6c99871a0|" +
            "fedd54dee65f78ecd3f8|" +
            "0052c5b09ced68c6fac0|" +
            "fe51ed778e89be0cfe48|" +
            "5872108ca2448e350fe0|" +
            "b06c234148ca3e9c8cf8|" +
            "2e9fa407748d0b879688|" +
            "c93b1df7218a7b0c64a8|" +
            "b8ad75b1295a2be41c88|" +
            "eaaaaaeaaaaabaaaaaf8"
        },
        {
            Digits(200),
            QrErrorCorrectionLevel.M,
            15,
            99,
            "feaaabaaaaaaeaaaaabaaaaae|" +
            "8289aadfad7ab25fe16d74642|" +
            "baeb6bd51242fa4734fe6bf36|" +
            "ba08544dbaf0044976a4090b4|" +
            "ba6ceb37d83e5e5aec18ad842|" +
            "82f40a76330b9e3600e6930e8|" +
            "fe19ed8eb1effa2086f216a72|" +
            "00d42293ddbc07c85ee6ad01c|" +
            "f398695a5461514ceedd84316|" +
            "5f35fe47985c227fccc362e3c|" +
            "bedc81e8f96be8857191c0b7e|" +
            "3de0d48671e6995466ada00e2|" +
            "f0166b822760e104f23adfdaa|" +
            "a0fdbeb71a03a90533eb14ba2|" +
            "eaaaabaaaaaaeaaaaabaaaabe"
        },
        {
            Digits(250),
            QrErrorCorrectionLevel.M,
            13,
            139,
            "feaaaabaaaaaabaaaaaabaaaaaabaaaaaae|" +
            "82f481ec4408f6b82ae02e2cb4d2d066392|" +
            "ba2fb53950b3efbf1bf5b8d4ef0fafeeeae|" +
            "ba012cc1293d2a17a26c602976265741438|" +
            "ba271c3d08946146a47d758b7e931da9fee|" +
            "8282f8019321843db335c156eda8456ddcc|" +
            "fec24ff56f534d944dda9a6209c96e7ec86|" +
            "0009b32c9f6fe6c7feaba8e35fd0bbfeadc|" +
            "b5e6281d146cffdb110f719da80f6ac4afe|" +
            "5b214a228b5f300f4c46cf7a57a0617aee2|" +
            "894cd879c66e77920ddafc77a06bf4ab72a|" +
            "98112dedd9d9b6d3d1126fc1dac69a98f62|" +
            "eaaaaabaaaaaabaaaaaabaaaaaabaaaaabe"
        },
        {
            Digits(300),
            QrErrorCorrectionLevel.M,
            15,
            139,
            "feaaaabaaaaaabaaaaaabaaaaaabaaaaaae|" +
            "82ada5eebfd652cc329b6d09c9cae13401a|" +
            "ba0eb978771dbff045ff3fa7cd37c17a4b6|" +
            "ba2990c9b7aa9cdb13122aa4197c1a86f20|" +
            "bad3bab6274203d20f6b56b9834d98d3eb2|" +
            "82b205438a8e9854fa6dcd4d903a0b52058|" +
            "fed6c3d4e50157628b99d5a2961d6bff496|" +
            "003b66463d2724dfb9fac3f96716a00fe18|" +
            "ff07df1d0b9d6114edb33fea67c79a6a5aa|" +
            "768ddf47b6d97e3257b5448f4cf42b7ea78|" +
            "af4aa03037df2d845e0bf888d4db7e44abe|" +
            "5e2d1f82a56ff016497206a1a2c214ed0a2|" +
            "f7dc4479e94a2b9da97db9503e17afceeea|" +
            "970c756fec75cecbd417e852b5b28c48522|" +
            "eaaaaabaaaaaabaaaaaabaaaaaabaaaaabe"
        },
        {
            Digits(361),
            QrErrorCorrectionLevel.M,
            17,
            139,
            "feaaaabaaaaaabaaaaaabaaaaaabaaaaaae|" +
            "824b84eb8603f294b207eb747b82d47526a|" +
            "baedf63cdeef57834505fd34bb43f98cd1a|" +
            "ba4b336186e4ae8b19dc24a288f4a28ff98|" +
            "ba102a7fd44c6de2a2f232cc74adf24b7e6|" +
            "8204a26a30ca90342422a8aef7465970824|" +
            "fe73a3f596a0519c504dde114a530cf292e|" +
            "004acee8f108bcff670a6a41049e8aa77cc|" +
            "da9b3254d10f656bab7633ce85af83cd6ce|" +
            "510bbfa63a3f42419207e7fc39a0e765ee4|" +
            "af60349a7979bb3474059d707a537a8c516|" +
            "250179cf074f3c8e69dc24abc676328ffbc|" +
            "807ef9bcaaffcd91b2f212cb998fd64b77e|" +
            "5eb637ece83bc8bbe422a89d7a084d70862|" +
            "dc44d63ecfcdd38c5c4dfe3266d388b296a|" +
            "bfe47d68d9c51697ab496f7204de8aa7462|" +
            "eaaaaabaaaaaabaaaaaabaaaaaabaaaaabe"
        },
        {
            Digits(1),
            QrErrorCorrectionLevel.H,
            11,
            27,
            "feaaaae|" +
            "826c38a|" +
            "ba1baba|" +
            "bafb430|" +
            "ba53d46|" +
            "82f8de4|" +
            "fe4e2be|" +
            "0067322|" +
            "da4f3ea|" +
            "b4555a2|" +
            "eaaaabe"
        },
        {
            Digits(12),
            QrErrorCorrectionLevel.H,
            13,
            27,
            "feaaaae|" +
            "8299e0a|" +
            "baa7702|" +
            "bab1d30|" +
            "baabbea|" +
            "82b2d24|" +
            "fe5221a|" +
            "0076410|" +
            "e8aabfe|" +
            "63c7ba2|" +
            "b38b42a|" +
            "8726d22|" +
            "eaaaabe"
        },
        {
            Digits(13),
            QrErrorCorrectionLevel.H,
            13,
            27,
            "feaaaae|" +
            "8293a0a|" +
            "baae302|" +
            "babf934|" +
            "baab3ea|" +
            "82add24|" +
            "fe1f61a|" +
            "00d4010|" +
            "d6f27fe|" +
            "6df65a2|" +
            "844fc2a|" +
            "8179d22|" +
            "eaaaabe"
        },
        {
            Digits(20),
            QrErrorCorrectionLevel.H,
            11,
            43,
            "feaaaeaaaae|" +
            "8285eaf6fa2|" +
            "ba377eedb56|" +
            "ba51d2b9414|" +
            "ba169f1b28a|" +
            "82aab81c020|" +
            "fe2017b64fe|" +
            "00aa80cf262|" +
            "c991be7656a|" +
            "894c8ad0f22|" +
            "eaaaaeaaabe"
        },
        {
            Digits(30),
            QrErrorCorrectionLevel.H,
            15,
            43,
            "feaaaeaaaae|" +
            "825beabbeb2|" +
            "baa72ec1e5e|" +
            "bafa09b25d4|" +
            "bace3e6856a|" +
            "8249937785c|" +
            "fe179cbd5b2|" +
            "009579a982c|" +
            "c4e6bd0211a|" +
            "5bd4d25ff90|" +
            "db29952cbfe|" +
            "4cec2abc122|" +
            "a989ae930ea|" +
            "9e58eb094e2|" +
            "eaaaaeaaabe"
        },
        {
            Digits(45),
            QrErrorCorrectionLevel.H,
            17,
            43,
            "feaaaeaaaae|" +
            "82cbab69262|" +
            "ba402e74f9a|" +
            "ba55ca94e6c|" +
            "bacf8e4814e|" +
            "827239f169c|" +
            "fe5a6f9f69e|" +
            "001ad14eb28|" +
            "c10ecc0202a|" +
            "49374138fc8|" +
            "a70facc7776|" +
            "444d50c18b4|" +
            "8560660be3e|" +
            "4f156bdae62|" +
            "f2cd8f012ea|" +
            "915c7ad06a2|" +
            "eaaaaeaaabe"
        },
        {
            Digits(60),
            QrErrorCorrectionLevel.H,
            13,
            77,
            "feaaaaeaaaaabaaaaab8|" +
            "82ba38bc14942b467028|" +
            "ba4288fb37d3fcaf8c18|" +
            "ba97d6ae4bb3a9e77e00|" +
            "ba02ebcec1ae77e1fed8|" +
            "82e7a3ae322126331ec0|" +
            "fef150569cc71657e138|" +
            "0057fa37f7f3a0dd3150|" +
            "f8bb4afcf6c938f0d0f8|" +
            "74676c2099e9a6718788|" +
            "939da0facde1bc2b45a8|" +
            "b46ae3b6c4096df0f688|" +
            "eaaaaaeaaaaabaaaaaf8"
        },
        {
            Digits(80),
            QrErrorCorrectionLevel.H,
            13,
            99,
            "feaaabaaaaaaeaaaaabaaaaae|" +
            "8294e6c56baea4679eaec79ba|" +
            "ba653fb4a137f04b8dba035ee|" +
            "ba70d8ab6d579a27a529ac740|" +
            "ba3281bc038ff59ef87eca2b6|" +
            "82423065cae80d57304a06da4|" +
            "fef511a74ddbc95b9bd11ac6a|" +
            "00dfa4e279f823da38ecab410|" +
            "c8b23121ca2574d7d7939887e|" +
            "41947a4963b91f6ddf2536822|" +
            "afaf17d9fc76e02a1cba95d2a|" +
            "940ac2b9dc84a8a9fa68e54e2|" +
            "eaaaabaaaaaaeaaaaabaaaabe"
        },
        {
            Digits(100),
            QrErrorCorrectionLevel.H,
            15,
            99,
            "feaaabaaaaaaeaaaaabaaaaae|" +
            "820ef2fb472aa6c6b92e549fa|" +
            "ba8983ada880e4175c385f52e|" +
            "bad344fd1852a769fbaee4410|" +
            "ba353fcaa4fe581b85596828a|" +
            "827c6aafcf0688d3176fe71ac|" +
            "fee2fbc06c5d44b6345b17c66|" +
            "0042ba81b946223d1589eb648|" +
            "ef538165e214523f03fff8ad6|" +
            "681fa4cb7cc1bedb3b631bf34|" +
            "916611c19e80f751aeda32fbe|" +
            "079b021f74181ebf54c93df62|" +
            "e6fac388da3ae0f0c6baec32a|" +
            "a36a5ae65853a0f3e56f5eaa2|" +
            "eaaaabaaaaaaeaaaaabaaaabe"
        },
        {
            Digits(150),
            QrErrorCorrectionLevel.H,
            15,
            139,
            "feaaaabaaaaaabaaaaaabaaaaaabaaaaaae|" +
            "82298faa0ee17e99e72c2edfeca28218cea|" +
            "ba6152b83e9b738705e4fa03c8afa1fe32a|" +
            "baf6c668a3792810206423e122fa0c5e758|" +
            "ba82637e397f8936c1e91f7083619c0ff42|" +
            "8232206aa7beda0cf098c450d3f42e78af4|" +
            "fe0b7f164c642bee159cbaefb45f732417e|" +
            "009b4c056e14cef4927b604424007ba4ec4|" +
            "f1a9eb951ba2f122f8ac1cba6ec3890eeba|" +
            "7e06fd2f8954c4523acc496af35c9b40370|" +
            "fd616f18051c41540716f52e9a37495227e|" +
            "6a7b96aa9f5846f4e7f383cd3f92216e322|" +
            "b3e1d2bf0b98f7c71ae8b86b0d23d6ade6a|" +
            "92bf31a91631bea626af2d15c46ae457222|" +
            "eaaaaabaaaaaabaaaaaabaaaaaabaaaaabe"
        },
    };

    [Theory]
    [MemberData(nameof(MatchesReferenceGridData))]
    public void MatchesReferenceGrid(
        string text,
        QrErrorCorrectionLevel level,
        RectangularMicroQrCodeVersion version,
        int rows,
        int columns,
        string expected)
        => SymbolEncoder.Encode(new RectangularMicroQrCodeSymbology(level, version), text, new BarcodeOptions(), (ref symbol) =>
        {
            Assert.Equal((rows, columns), (symbol.MatrixRows, symbol.Columns));
            Assert.Equal(expected, SymbolEncoder.ModuleGrid(ref symbol));
        });

    /// <summary>
    /// Without a fixed version the symbol takes the version of the smallest area that holds the data.
    /// </summary>
    /// <param name="text">The text to encode.</param>
    /// <param name="level">The error correction level.</param>
    /// <param name="rows">The expected number of rows.</param>
    /// <param name="columns">The expected number of columns.</param>
    /// <param name="expected">The expected grid.</param>
    [Theory]
    [MemberData(nameof(ChoosesTheSmallestAreaData))]
    public void ChoosesTheSmallestArea(string text, QrErrorCorrectionLevel level, int rows, int columns, string expected)
        => SymbolEncoder.Encode(new RectangularMicroQrCodeSymbology(level), text, new BarcodeOptions(), (ref symbol) =>
        {
            Assert.Equal((rows, columns), (symbol.MatrixRows, symbol.Columns));
            Assert.Equal(expected, SymbolEncoder.ModuleGrid(ref symbol));
        });

    [Fact]
    public void QuietZoneIsTwoModules()
        => Assert.Equal(
            2F,
            SymbolEncoder.Read(new RectangularMicroQrCodeSymbology(), "1234", new BarcodeOptions(), static (ref symbol) => symbol.LeadingQuietZone));

    [Fact]
    public void RejectsLevelsLAndQ()
    {
        Assert.Throws<ArgumentException>(() => new RectangularMicroQrCodeSymbology(QrErrorCorrectionLevel.L));
        Assert.Throws<ArgumentException>(() => new RectangularMicroQrCodeSymbology(QrErrorCorrectionLevel.Q, RectangularMicroQrCodeVersion.R7x43));
    }

    /// <summary>
    /// R17x139 at level M, the largest version, holds 361 digits and no more.
    /// </summary>
    [Fact]
    public void RejectsDataLargerThanTheLargestVersion()
        => Assert.Throws<ArgumentException>(() => Encode(new RectangularMicroQrCodeSymbology(), Digits(362)));

    [Fact]
    public void RejectsDataLargerThanTheFixedVersion()
    {
        RectangularMicroQrCodeSymbology symbology = new(QrErrorCorrectionLevel.H, RectangularMicroQrCodeVersion.R7x43);
        Assert.Throws<ArgumentException>(() => Encode(symbology, Digits(6)));
    }

    [Fact]
    public void RejectsCharactersAboveTheByteRange()
        => Assert.Throws<ArgumentException>(() => Encode(new RectangularMicroQrCodeSymbology(), "Ā"));

    /// <summary>
    /// Encodes the text with the default options.
    /// </summary>
    /// <param name="symbology">The symbology to encode with.</param>
    /// <param name="text">The text to encode.</param>
    private static void Encode(BarcodeSymbology symbology, string text)
        => SymbolEncoder.Encode(symbology, text, new BarcodeOptions());

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
    /// Returns "HELLO 0123456789 rmqr " repeated to a length: runs of Alphanumeric, Numeric and Byte mode
    /// characters, so the data changes mode.
    /// </summary>
    /// <param name="length">The length.</param>
    /// <returns>The text.</returns>
    private static string Pattern(int length) => string.Create(length, 0, static (span, _) =>
    {
        const string pattern = "HELLO 0123456789 rmqr ";
        for (int i = 0; i < span.Length; i++)
        {
            span[i] = pattern[i % pattern.Length];
        }
    });
}
