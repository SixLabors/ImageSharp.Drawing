// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Barcodes;

namespace SixLabors.ImageSharp.Drawing.Tests.Barcodes;

/// <summary>
/// Encoder tests for <see cref="HanXinSymbology"/>. Each expected grid is the module grid of an independent
/// reference implementation, bwip-js, one row of hexadecimal digits per line, four modules per digit and the last
/// digit padded with light modules. The vectors cover the four error correction levels, Numeric and Byte
/// segments, and versions with no alignment patterns up to six rows of them.
/// </summary>
public class HanXinSymbologyTests
{
    /// <summary>
    /// Gets the cases of <see cref="MatchesReferenceGrid"/>: the error correction level, the version (0 for the
    /// smallest that holds the data), the text, the side and the grid.
    /// </summary>
    public static TheoryData<HanXinErrorCorrectionLevel, int, string, int, string> MatchesReferenceGridData { get; } = new()
    {
        {
            HanXinErrorCorrectionLevel.L2,
            0,
            "Han Xin",
            23,
            "fe3cfe|" +
            "801002|" +
            "be7efa|" +
            "a0240a|" +
            "ae08ea|" +
            "ae7cea|" +
            "aeb2ea|" +
            "008200|" +
            "157eaa|" +
            "4f2484|" +
            "550344|" +
            "de98d6|" +
            "99ae4c|" +
            "7a93ca|" +
            "aafd50|" +
            "00ae00|" +
            "fed6ea|" +
            "027cea|" +
            "fa20ea|" +
            "0a300a|" +
            "ea9cfa|" +
            "ea2402|" +
            "ea10fe"
        },
        {
            HanXinErrorCorrectionLevel.L1,
            0,
            "1234567890",
            23,
            "fe2efe|" +
            "808002|" +
            "be7cfa|" +
            "a01c0a|" +
            "ae7aea|" +
            "ae7eea|" +
            "aebeea|" +
            "003000|" +
            "157d2a|" +
            "51849a|" +
            "2492b8|" +
            "f85ffe|" +
            "492490|" +
            "f29320|" +
            "a97d50|" +
            "002000|" +
            "fec2ea|" +
            "02fcea|" +
            "faa4ea|" +
            "0a0c0a|" +
            "ea5cfa|" +
            "ea2602|" +
            "ea90fe"
        },
        {
            HanXinErrorCorrectionLevel.L3,
            0,
            "12345",
            23,
            "feaefe|" +
            "808602|" +
            "bee0fa|" +
            "a01e0a|" +
            "ae70ea|" +
            "ae72ea|" +
            "aee2ea|" +
            "002600|" +
            "15efaa|" +
            "96c842|" +
            "2bb24a|" +
            "97fd1e|" +
            "4b0490|" +
            "769356|" +
            "abff50|" +
            "00ac00|" +
            "fe8eea|" +
            "02fcea|" +
            "fa20ea|" +
            "0ab40a|" +
            "ea5efa|" +
            "eaa602|" +
            "ea92fe"
        },
        {
            HanXinErrorCorrectionLevel.L4,
            0,
            "ABC1234567890DEF",
            27,
            "feb28fe|" +
            "8056802|" +
            "bef9afa|" +
            "a06d60a|" +
            "ae20aea|" +
            "ae920ea|" +
            "ae754ea|" +
            "00bd400|" +
            "17885aa|" +
            "3b3e17c|" +
            "cd5d88a|" +
            "d501040|" +
            "8aa8b46|" +
            "eb95556|" +
            "124e404|" +
            "b63e43c|" +
            "475182a|" +
            "30c22d4|" +
            "ab13bd0|" +
            "0038a00|" +
            "fe360ea|" +
            "022beea|" +
            "fa940ea|" +
            "0ac400a|" +
            "eaeaefa|" +
            "ea46002|" +
            "ea12afe"
        },
        {
            HanXinErrorCorrectionLevel.L2,
            0,
            "Bytes:  §®µ¼ÃÊÑØßæíôû¢©°·¾ÅÌÓÚáèïöý¤«²¹ÀÇÎÕÜãêñ",
            33,
            "fe3c9f3f8|" +
            "80e012808|" +
            "be137f3e8|" +
            "a086db028|" +
            "aef0a5ba8|" +
            "ae620aba8|" +
            "aec490ba8|" +
            "00ed27800|" +
            "1a3c356a8|" +
            "abc492f30|" +
            "89ae21c28|" +
            "9cff1dc00|" +
            "f3c8fbae0|" +
            "a4a44c168|" +
            "901295ff8|" +
            "e08b2bba0|" +
            "ffff951e8|" +
            "000095180|" +
            "95ceffa18|" +
            "e326cc340|" +
            "0b10853f8|" +
            "cc748d378|" +
            "98b8c2ee8|" +
            "0ba097f30|" +
            "ab66cd2c0|" +
            "00bce3800|" +
            "fecadfba8|" +
            "02a2cf3a8|" +
            "fadadfba8|" +
            "0a24bd828|" +
            "ea70923e8|" +
            "ead087808|" +
            "ea5eff3f8"
        },
        {
            HanXinErrorCorrectionLevel.L2,
            0,
            "Digits 0123456789012345678901234567 between bytes and 1234 short runs.",
            35,
            "feb2476fe|" +
            "80d50da02|" +
            "bebd128fa|" +
            "a056b5e0a|" +
            "aeaacf8ea|" +
            "ae9cd4cea|" +
            "ae392aeea|" +
            "00ea30c00|" +
            "1b6f003aa|" +
            "890c5d54c|" +
            "d849eb1ec|" +
            "acd4957de|" +
            "f21945dce|" +
            "ddc9556aa|" +
            "81ee9fca8|" +
            "80f8b6dee|" +
            "9071fcb90|" +
            "ffffe6aa2|" +
            "000076f40|" +
            "499d49112|" +
            "98a058354|" +
            "7553564ca|" +
            "28a57273a|" +
            "12ac5bd14|" +
            "a4994ac82|" +
            "87b56d514|" +
            "abf17fdb0|" +
            "00155c600|" +
            "fe9b468ea|" +
            "0257742ea|" +
            "fa0540aea|" +
            "0ae74080a|" +
            "ea696aafa|" +
            "eaca6da02|" +
            "eaca7fefe"
        },
        {
            HanXinErrorCorrectionLevel.L1,
            0,
            "The quick brown fox jumps over the lazy dog. The quick brown fox jumps over the lazy dog. The quick brown fox jumps over the lazy dog. The quick brown fox jumps over the lazy dog. ",
            49,
            "fe7f8a54b73f8|" +
            "808084c828008|" +
            "be20f8bdf6be8|" +
            "a05ed69dec028|" +
            "aefa92f90bba8|" +
            "ae48880a5c3a8|" +
            "ae98b2304cba8|" +
            "0002c42e17800|" +
            "2268d62b7e6a8|" +
            "d9a4f6263dee0|" +
            "8e7efeb4237c0|" +
            "9b96cab7a4e48|" +
            "d916fc25c9080|" +
            "3ec8be42c4ee0|" +
            "5bdc98bce47c8|" +
            "08e4fc680ac20|" +
            "90deffffb34a8|" +
            "1b900000b7a40|" +
            "8fe09d0eed5f8|" +
            "c947a8f4b2820|" +
            "df988c98e7238|" +
            "d12c2552d6958|" +
            "b2104fa496308|" +
            "be477b468d8c8|" +
            "b22a513ebf5a0|" +
            "fc08d024847e0|" +
            "a878bdf8c37e0|" +
            "accbc7f4d2f70|" +
            "cee76924ce930|" +
            "f7e5eaa2990d8|" +
            "ddc2d04eb52e8|" +
            "a123f922c12b0|" +
            "ffff959affff8|" +
            "0000881200008|" +
            "ca5ee37d91728|" +
            "32669c8f9ffe8|" +
            "6e90dced28168|" +
            "22d4f5c553f08|" +
            "bda2c6c642fe8|" +
            "136c848ff6948|" +
            "ab0eba4213220|" +
            "00c4d0f17b000|" +
            "fea2aec8c4ba8|" +
            "0210d0152a3a8|" +
            "fae6f8f91dba8|" +
            "0a2ccee7f4028|" +
            "eab2d331cf3e8|" +
            "ea3286f41c808|" +
            "ea54fffea43f8"
        },
        {
            HanXinErrorCorrectionLevel.L4,
            0,
            "012345678901234567890123456789012345678901234567890123456789012345678901234567890123456789012345678901234567890123456789012345678901234567890123456789012345678901234567890123456789012345678901234567890123456789012345678901234567890123456789012345678901234567890123456789012345678901234567890123456789",
            57,
            "fefff96dd5a93f8|" +
            "8080235fa032008|" +
            "bea6b2e58ddb3e8|" +
            "a0e4b77d1f39028|" +
            "aebab715c4393a8|" +
            "aeee2ffdbcbe3a8|" +
            "aefcbc9ee4fb3a8|" +
            "0093a66b90b5800|" +
            "26d621dcd3952a8|" +
            "cf2036518c309f0|" +
            "e454a253f9f79b0|" +
            "419a3f7e04db470|" +
            "9c69a2fbbe60fb8|" +
            "45eb21912f9f3c8|" +
            "e9b0b85b891f9e8|" +
            "8771afa03cadb20|" +
            "9a36ad4fbc33998|" +
            "65b52a1a1900100|" +
            "0762b8d24325320|" +
            "8d9a3ffffc8f128|" +
            "1b3b80000582fc0|" +
            "8fc8274a0646590|" +
            "c436f653d5db218|" +
            "9e98987eb6d4420|" +
            "b1400c31a78d568|" +
            "bc2ffe1da5ad638|" +
            "ab8ba94035d4fc0|" +
            "dc18991d1650508|" +
            "cf95e0cbe71d978|" +
            "f85af85e575d4a8|" +
            "bcf955c4475d808|" +
            "91800444a5881a0|" +
            "fffaefd255d0090|" +
            "8d1c9ab7b41ca90|" +
            "fa8e583677df1b0|" +
            "f6b40c3da6d5628|" +
            "bee13fd5b68dc48|" +
            "9d04fceb352eb08|" +
            "ffffe6bfa7ffff8|" +
            "000030f7e000008|" +
            "206db9ca5bade88|" +
            "a852b9fb4200968|" +
            "4610a2026897e88|" +
            "409aa332c7daac8|" +
            "c405ade8d64d5a8|" +
            "7e8939e4d326f28|" +
            "56f82c8aa651228|" +
            "7218b0144206308|" +
            "aa1b37070992b20|" +
            "00e6bb760a4f800|" +
            "fe063bb79131ba8|" +
            "023e29bc0121ba8|" +
            "fa423ffd4a87ba8|" +
            "0a2325925340828|" +
            "ea522a2e1c7dbe8|" +
            "ea10b2c760dc808|" +
            "ea63bffff524bf8"
        },
        {
            HanXinErrorCorrectionLevel.L2,
            0,
            "Han Xin version eleven holds two rows of alignment patterns. Han Xin version eleven holds two rows of alignment patterns. Han Xin version eleven holds two rows of alignment patterns. Han Xin version eleven holds two rows of alignment patterns. Han Xin version eleven holds two rows of alignment patterns. ",
            67,
            "fe197fff83934f0fe|" +
            "8040000084d713a02|" +
            "be56f772870b39efa|" +
            "a0230b72e6e6dee0a|" +
            "ae19b990a1f6654ea|" +
            "ae4cc0b09734b72ea|" +
            "ae6f6420a1014c6ea|" +
            "00b4b636e47fcc800|" +
            "2b08191cb975a20aa|" +
            "adc42406a90b739b8|" +
            "f13f8e448ba346632|" +
            "842eedc08d7aaa4b6|" +
            "ccacecc2ab93bb736|" +
            "e9c14e048323f5138|" +
            "cda5ce6ced390303a|" +
            "adda6e4ccb93a0030|" +
            "af4c2dce9100ca730|" +
            "ffffe642ffffc2eca|" +
            "0001e5980000702c8|" +
            "6d9d3c9bd90856954|" +
            "1939ad8c843263936|" +
            "c809000fa11678848|" +
            "0c2154139b9352e4a|" +
            "c215d1a1906b47776|" +
            "7249dcb298ec74b92|" +
            "dfb5696c6e704077c|" +
            "7441323ab6247565c|" +
            "726dc874181d56f76|" +
            "fde5ee4e8c2d7cf62|" +
            "5bd966eeb27679958|" +
            "cc0101bda2cb58ecc|" +
            "8bf16885a5b16fd56|" +
            "632140ca82065010a|" +
            "2e21125dc85b6729c|" +
            "9b2dffffb9c87fffe|" +
            "21740000800e00002|" +
            "d7268656bd9ac1892|" +
            "a5b99444a26972772|" +
            "eea76a6e9c08191ba|" +
            "991c9b4af26842122|" +
            "86074014a8c5bddca|" +
            "85bcb9d0e16e7393a|" +
            "8f666e648827cb59a|" +
            "98165b4496e6f6422|" +
            "93f10646d5b190012|" +
            "a7008190f26cb0212|" +
            "ee212016d45b9cdca|" +
            "9a1dcb08974686402|" +
            "a21776e4a625bdb1a|" +
            "d9a5c802c332776e2|" +
            "ef657e7088191d1a8|" +
            "ffff2636ffffd2e72|" +
            "00014dca000065198|" +
            "03a9df912cd2793a2|" +
            "813dee656f665b804|" +
            "7afd3210bbb7460c0|" +
            "596d1dec8f5942168|" +
            "b6694ead8c844b3ba|" +
            "aa4d79010b7a629a8|" +
            "006577216e7252200|" +
            "fef16e4d2e4d468ea|" +
            "02b5d1a195ba5a4ea|" +
            "fa5db724b7324ccea|" +
            "0a9d669727725240a|" +
            "eaf9d8dce043460fa|" +
            "ea85191c13695d002|" +
            "ea11fffe91807fcfe"
        },
        {
            HanXinErrorCorrectionLevel.L3,
            0,
            "Version twenty two has three alignment pattern rows across the symbol. Version twenty two has three alignment pattern rows across the symbol. Version twenty two has three alignment pattern rows across the symbol. Version twenty two has three alignment pattern rows across the symbol. Version twenty two has three alignment pattern rows across the symbol. Version twenty two has three alignment pattern rows across the symbol. Version twenty two has three alignment pattern rows across the symbol. Version twenty two has three alignment pattern rows across the symbol. Version twenty two has three alignment pattern rows across the symbol. Version twenty two has three alignment pattern rows across the symbol. Version twenty two has three alignment pattern rows across the symbol. Version twenty two has three alignment pattern rows across the symbol. ",
            123,
            "fefef126bfffefbb8bffffd326a08fe|" +
            "80823c72000023648000032d5016c02|" +
            "be6a49990df1382646237a280d968fa|" +
            "a0c32bb33123278aff814bf162b260a|" +
            "aecaac9413cdb6022950fba7b64c2ea|" +
            "ae4a32d88b1bb4546eb05b1921bd8ea|" +
            "aeba4d2d45f12b98da137a68ac62cea|" +
            "001bca2884ca2c76a78f7b703402000|" +
            "47d20ae5a362bb547978aa30f0a632a|" +
            "0f721799c547a6536c640287d742a88|" +
            "665a7e2828f02092492c6a438731d7a|" +
            "b66338d969e53f424659638a91df8a2|" +
            "cbea4881cc963297a5d9ab0f84ea766|" +
            "173a53784eb92e246e73aac63923dce|" +
            "aca2949efcf3b7b73f172200164348c|" +
            "ea0b296851e1a2ea8e92ea9ad292d44|" +
            "956a3080bf64b1dcba07128b376a81c|" +
            "362343e18ddeb7fbd665a2d8bc1c590|" +
            "8a8bffff9d6ebffff9ca83ffff96022|" +
            "08700000c39c80000af9780000e8658|" +
            "fffffcb2da23a9878ee3e7a6e499a50|" +
            "ed2c8f5495602fe3abe960faf4cafb6|" +
            "b877f1a4e64353ff2839e754fcf4bb2|" +
            "dbb3e4628de775f96a323463829d8a8|" +
            "86ebeff8c1e0c2b1e9413cf3a0c2950|" +
            "bf31acfa84afbc216da147815ce23a4|" +
            "c37b32129b9e9ccbe9b7a5a472c479a|" +
            "d53c8080e2d56e5f2e60ec16ccc2842|" +
            "815b9e52d02151e0c8a08f492489426|" +
            "f6e6b7a6a691e9088b7e1a56a6879bc|" +
            "ce6903f0c0940504ef558f2052e3b4c|" +
            "f78942fef4d964a4a9a5478de8ff2d4|" +
            "ac054a6690dedf4b8c6237d070f968c|" +
            "95fc581083c57bbd8ff8b15e3887fe6|" +
            "a512854cf23d4af38a7ac9d35ab27b2|" +
            "a2a3772698c90dfecb4a6e7a74db8ec|" +
            "fffe84deffffe7b36fffffd40effffe|" +
            "00034f3400002e202000020e0200002|" +
            "4bb20b1bbdbc2bdfcbfffbf0ee11b12|" +
            "0152682e73b520e153dda3f62bbffd2|" +
            "cad2eec6b97528125d481b3a0900082|" +
            "f8ba37c4264e2d968e6dd22465b2772|" +
            "fe329cec023fa2073c592a5e97647a2|" +
            "ad2a3e056de93ece9588231f03c5752|" +
            "7e129cd461a12127bf3c3a2acfc5c82|" +
            "046a4c12bebd2f96851e03d878e92ea|" +
            "29e3d7ec508c36e0d99023bd10dec72|" +
            "e7e27cf9bfffbd442e36239ea35fdea|" +
            "0bd24df69da8ad0380878b01d703012|" +
            "7683da55c7f4b3b3f52ac3d73d8ee9a|" +
            "4dca334cbcfbbc5c6b77ab271b2d39a|" +
            "d19bfc20eef532ac8f26e2649f63c32|" +
            "b4bb7a5d97a13c4ea765e217102bf0a|" +
            "038a09d8a8da3cb24e6a33b20c93df8|" +
            "b97bffffb3113ffffa7313ffffda992|" +
            "11700000c9d300000b17900000da140|" +
            "a33095e2c90df200aa6e7a766c90dfe|" +
            "c922e014cdfd24d92ad53cc914e1f56|" +
            "b90a59bcb05b33142bd95b9da687f94|" +
            "aa3631ca959ade9bcd77242236f127e|" +
            "dcba8b40b3040fc2ce6d241398d7f0c|" +
            "e5238080d57b0bfc2b5dbc92ac950a6|" +
            "ffa4a6e2a31469990b794e4e4688dfa|" +
            "d9553784d640c446ef15866512e2c56|" +
            "c9fe922c9448873448f52b1838eb274|" +
            "dd63437a8a8c239d8b7fffedb2e90de|" +
            "d32f7f50b2a46f8aefec99224280e22|" +
            "a1247964e03f44ceadacda1752f97e0|" +
            "e2b7b5e2b8a8d8c04b266b7a6e92928|" +
            "f4f8158ecec43140ce5c7d89d0f5794|" +
            "ac8f5592b34fe3b4cf70b2f49cfbab6|" +
            "e2477e2edf3c929b8afb3426648de0a|" +
            "ffffd226ffffee568ffffef9f2ffffe|" +
            "00022056000033a0a00003444600002|" +
            "c54394cb5273346432049bbf3c45972|" +
            "c8f2fd4f69ea2924cc3e7a74df4f13a|" +
            "0a3bda5cb3923ad2995b1379952356a|" +
            "7493a424a1cd38f9e9300a2636267ea|" +
            "1942d00bd4172a9da8d91ac0c78adc2|" +
            "c11a6f768411a657f4f813fecec4312|" +
            "37ba314dc8cd2c613bf172690d1c9c2|" +
            "6fa25de9992ba050c927028f6076c92|" +
            "96aaca207e84bd902a17e2a76557a5a|" +
            "5a328cb5509da2a334332339a8da74a|" +
            "24a2924e03b329e690eb62a87ec91b2|" +
            "3dfbd321ef03b3c5670bd32c981548a|" +
            "12cb3d39f0cdab4b489f7b6ef1f3daa|" +
            "94a2356e52d4bab416cd23bac856e62|" +
            "98e37a1e2b9bb81c8580b363499932a|" +
            "22130dcda9872c4d2ea6e313cf0dfd8|" +
            "8a8bffffabe5bffffc0a43ffff9d232|" +
            "3768000082f480000ad208000087d48|" +
            "c77abe9ea2929af18c4326640cd0178|" +
            "d2b92da8c634de05ca38691a48a4ea6|" +
            "bd4781bc97bd41e06f0ba1c13c986b6|" +
            "fce2e42cedc009196c91ad8a2a8c8b4|" +
            "e2c12eccf9e48145eae0479812b26e2|" +
            "dc7ad53c82563e1568fb786b4cb3696|" +
            "95d13a368be1bb7b8aa31135e2b9ade|" +
            "a1645f12d3f02c952b10c6909891f62|" +
            "990b723cb23d18812d3f8ec958ab0bc|" +
            "a43a050ee91df3f88af24a6baeff128|" +
            "d09e240ce0fc4925afafb95a4e81afa|" +
            "f84e929c9b0e73bcafba13dce8dd9f2|" +
            "877e7cd2fa4f8b912b95a9a464e4378|" +
            "b279cebcbce3dfd68f95324bccc63d2|" +
            "a1efec98bbc41ee2293ebf5838b53fe|" +
            "986af7b6e923e7e8ce2bc8d57e8a8d2|" +
            "ffff390cfffff741affffe8af0ffffe|" +
            "0003a7f200002be400000344dc00002|" +
            "5d4b0c97e5e12dc8929ac2fbf144762|" +
            "803a55797c94b9992a08aa612705f5a|" +
            "baabff24924d2afec7a6f27347eb43a|" +
            "9333e6237be2b3c08c8c7afcd662a32|" +
            "a9ea47d4c2c520979941a2f3c2b1fc4|" +
            "000a731391443d3f842da21d9f39c00|" +
            "fe5b433702433e79018d9ba76200eea|" +
            "0273bf64e6c52117114a33de1dc04ea|" +
            "facaa04d6a682c85c6c64b6e6eb92ea|" +
            "0aaa4c54629bbcf735bed327444860a|" +
            "ea533ca3dd87aeb8ebcdb303f62e0fa|" +
            "ea3a09640f53b24fe1fc73d86e23e02|" +
            "ea3bfffe91ffbfffebf943fffe94efe"
        },
        {
            HanXinErrorCorrectionLevel.L1,
            0,
            "Version thirty two has four. Version thirty two has four. Version thirty two has four. Version thirty two has four. Version thirty two has four. Version thirty two has four. Version thirty two has four. Version thirty two has four. Version thirty two has four. Version thirty two has four. Version thirty two has four. Version thirty two has four. Version thirty two has four. Version thirty two has four. Version thirty two has four. Version thirty two has four. Version thirty two has four. Version thirty two has four. Version thirty two has four. Version thirty two has four. Version thirty two has four. Version thirty two has four. Version thirty two has four. Version thirty two has four. Version thirty two has four. Version thirty two has four. Version thirty two has four. Version thirty two has four. Version thirty two has four. Version thirty two has four. Version thirty two has four. Version thirty two has four. Version thirty two has four. Version thirty two has four. Version thirty two has four. Version thirty two has four. Version thirty two has four. Version thirty two has four. Version thirty two has four. Version thirty two has four. Version thirty two has four. Version thirty two has four. Version thirty two has four. Version thirty two has four. Version thirty two has four. Version thirty two has four. Version thirty two has four. Version thirty two has four. Version thirty two has four. Version thirty two has four. ",
            127,
            "fefff41e0bffff4ae6bfffe57fcad0fe|" +
            "80003e03c00002b23c00003e65428e02|" +
            "be77b9e96919ab7f4236462a8d04c8fa|" +
            "a02aa1fb9759dbf45e09e626fe16280a|" +
            "ae6820f143eda3fc2156e43cbaced8ea|" +
            "ae41b3151921bb46c5efc53172dbd2ea|" +
            "ae9fa62e4af76b2f4ed2b9a57fd686ea|" +
            "00743c9aaef0139e8d28bf2257bb4a00|" +
            "4950a3573a37a31978d89aa61a365c2a|" +
            "2f05ad9eecced266951e6236cc9d3386|" +
            "4dfc34332b7e1a3c24c0bc3a8592405e|" +
            "59562a8b96de9265e3626a3868d5f6f8|" +
            "e2bfac0bc07a0b1e287db4bce7c9bf84|" +
            "7690b4ed2b9cebfd6857dc223aced33c|" +
            "3bf3b4b48cd6fba0a84b2daf48cbc6c4|" +
            "dcbaa56fa2f05b627ecced266951e620|" +
            "b425242aea69439332b7e1a3c24c0bc0|" +
            "22f7b2b2b4b99b7e7a5a463adfd08d90|" +
            "ba89bfffff897bffff8ab73fffffb972|" +
            "015b80000850a80000a5178000082ae0|" +
            "bd3f7e2a2b96de9196e3627fc00aad5a|" +
            "90c15be8aab2284dfcd06c99eecab4da|" +
            "a12be8ed4c1f5de020bd1763ff485e02|" +
            "af7e2a2e4ede91978c971cb7f428d918|" +
            "9ce92ecceee6698d6ed33d1a516a04ae|" +
            "b012f4ed2c38bc2620b80fdcbacd6fa2|" +
            "e77e7a5d8f038bdf8aa2e5b7a46978d8|" +
            "9078ef1d88fc3c57fc81780f412bc50e|" +
            "85da80a12b2741b266f809d4784ca0d0|" +
            "9e26a61a2d9799df9ea5a47fa4cda178|" +
            "dda42f808ae73a4bb295b99a3bae14ce|" +
            "bc13cc812c2c04bd3abf5fd37f0cce02|" +
            "a6a61a364f99df9f1aebd662f7e8a8b8|" +
            "eda12be8eee41f805095bf0f15ea605e|" +
            "ed42c8f14e5b2b2c28fec9d06c88be02|" +
            "c62a3247cc0189a986a365e677e9e968|" +
            "fffffc2dafffff27d4ffffe73d4ffffe|" +
            "00002005e0000274ce00003b52c00002|" +
            "4c343b2f7ced1a486f46c5bbf15172da|" +
            "cc9e2a264af76b2f4ed2b9a57fd69252|" +
            "2c64b01e82739a1f6d095f2ab721d4da|" +
            "4e8da462a32433e8d8bdf8aa2b26f77a|" +
            "79903f858097a2b7e1e2bc223d707ee2|" +
            "baceafa2f04f3337f0b3292351e62002|" +
            "d232bc6c4d4c3319703f1526486f46c2|" +
            "0b23a7988017f3c42f2741ba42f809d2|" +
            "8f09376675641aaef010a12d28bf64ea|" +
            "6b7f308de170dba465e3623a9868d97a|" +
            "8565abc5b42b73a09ce7a9bb4257d242|" +
            "109a35b907d77bb4cf46942c8128a922|" +
            "a7c53172dbd232bc6c4d4c2d1ad97312|" +
            "15ff205e03d04af143eda1bdf84dfc2a|" +
            "8097b2b7e1e2ba4c0bc14eb7182b7d12|" +
            "277e3e96919adbf44f3065ade91978d8|" +
            "b20fbfffff59dbfffffd17bffffd9702|" +
            "095a00000f80f00000ac8f00000f9880|" +
            "b7a465e30bd50dfd08b6462a3248de8c|" +
            "d42c8f4a4e742b2284ec9d06c98be026|" +
            "b99af83e8d6e41f5deed33d1a50a4834|" +
            "f425a466adfd08d918aa32437ace31a8|" +
            "d7780850ae945fb274dda42d8e4afb68|" +
            "ff0855b90f2eb3b4cebcf5a4980b3b50|" +
            "f7e2a2e5ade91978c4ff3b7f422d918a|" +
            "95ff5a15abd04d2a54e8bc13cc8dfc2c|" +
            "ec9995bf0d087cb4ceed4b10c1482142|" +
            "f7e7a5bd4ab8bdf8a8ae5b7a464f8d88|" +
            "dcbaed3a2a751e1260cced42c8e9e620|" +
            "e35b517eccaf7690beed2b9cec8b3f50|" +
            "a437a362edf8a8b96cfa465aff899ade|" +
            "c2f809d46ba4bb33b499a55a408cf468|" +
            "bcc812bdc84bd3b4b8e9e7f085483f72|" +
            "e1a365e66df9e978ceb62f7e2a2b96de|" +
            "ffffe4afafffff56e4fffff73eeffffe|" +
            "00002bb3a00002fc3c00003e12600002|" +
            "6f46b17bf15172b51bfcf4ad2335bfa2|" +
            "95bf351e127692254b10c1202142b22a|" +
            "09bfba0342f57a7a9b664ebd1da9621a|" +
            "68d93a86223242de8d8bdfa2a2e5b7fa|" +
            "43903495f476a20faef010be8d28adca|" +
            "415bbd04e73d4ada12be8eae41ed9632|" +
            "1b232546486f43b17bf151392f8efcf2|" +
            "204aab012f4ed3c3c57fda29d8fdcbaa|" +
            "9da625e09e640be166b681a3cc400bfa|" +
            "65e3389a9868c2118b423230de8d8bda|" +
            "478a31002feabab64e8364a5f013a8f2|" +
            "12602ce9f0abc3e02142b2317ec9d06a|" +
            "fe84385165bd22cbc6c4d4b0d1b2f33a|" +
            "4d06b66945fb22bdda42f834ae73bcba|" +
            "1476b20faef0139e8d28bf2257be01c2|" +
            "22a2b96de9197a3626a61a2dfbff8a88|" +
            "925dbffffc2b73ffffe8273ffffe287a|" +
            "001280000d63900000f12b80000b0128|" +
            "c236462a2c90de8c4a8a61a365e99df8|" +
            "cc812bddacbd861d78817f0855abf72e|" +
            "c15be8bc0bdc09b33ab99a54798cdb32|" +
            "c65e36492cdfd08d90e2a32437a8d8bc|" +
            "c8f2ef80aab2284dfcd06c9995aa7240|" +
            "94b39ce92d6856e668b9cf536ce8d2d2|" +
            "da466b7f488d918a8ca437a740491868|" +
            "80850ac8adfb2741b2c2eba7d36e84ae|" +
            "855b907d6b3b4cf50afa9d81780d0b22|" +
            "aa2e5b7a49978c7322b7f4236468a8c8|" +
            "f5a15b99adde894cf4c13cc812aac04a|" +
            "995bf0f06c234ce03eb10c15be8c2b22|" +
            "fa58e0ce2bdf8a8b96b7a465e3689a98|" +
            "a97e2bf0e9e12605e0d42c8f142c8400|" +
            "9d36cc9d2a3b52c430f80850bb6fdfd6|" +
            "fa362f7e2a8b96de90e5ce71dc2dfd08|" +
            "ffffe0274ffffe73a4fffffd684ffffe|" +
            "00003b50a00002856000003d4ce00002|" +
            "cefcbd2d2335bbe846c8102e9232f1b2|" +
            "907d2b3b4cf46b3cc86cdeb50b23c50a|" +
            "f6d0be10ab720a4f2e4adfba8f09302a|" +
            "8d8bb7e2a2e5ba6719e969266b7f4232|" +
            "7e1e3c24fc7442962182b7a28564509a|" +
            "7f0b3b2f489e6336cc9d33bb52c43052|" +
            "bff33becbccefb3d2d2335afe846c82a|" +
            "4f522be8ed4b135de02142ba511d3792|" +
            "a85bbd04e73d4ada12be8eae41e77d24|" +
            "00ed26c8c5464a1bd1b17bbc54519000|" +
            "feaaba517ec9d37690be022b9ce92aea|" +
            "02f5326a58860b010a15912bf64e80ea|" +
            "fa3139f8a8b96b7a465e3629a9868cea|" +
            "0a69befdef09336676a16428f310000a|" +
            "ea04b26e07f0b3b26656fc34784980fa|" +
            "eaf43b16a36d22d97833bf2f4b411802|" +
            "ea6ebfffea90abfffe89063fffe97afe"
        },
        {
            HanXinErrorCorrectionLevel.L2,
            1,
            "1",
            23,
            "fe3cfe|" +
            "801002|" +
            "be7efa|" +
            "a0240a|" +
            "ae10ea|" +
            "ae7cea|" +
            "aeb6ea|" +
            "00f200|" +
            "157eaa|" +
            "592492|" +
            "24a24a|" +
            "87fd06|" +
            "4bc490|" +
            "329348|" +
            "aafd50|" +
            "00ae00|" +
            "fe82ea|" +
            "027cea|" +
            "fa20ea|" +
            "0a040a|" +
            "eadcfa|" +
            "ea2402|" +
            "ea10fe"
        },
        {
            HanXinErrorCorrectionLevel.L3,
            5,
            "Version 5",
            31,
            "fe3a90fe|" +
            "80104a02|" +
            "be7d56fa|" +
            "a009480a|" +
            "aeb2c8ea|" +
            "ae7ffeea|" +
            "aebc90ea|" +
            "00450000|" +
            "19a9ff2a|" +
            "c9110d48|" +
            "d7b24926|" +
            "924c611a|" +
            "c9241d6e|" +
            "aad2492a|" +
            "f764bffe|" +
            "c89c4490|" +
            "ffff4922|" +
            "0003c4b0|" +
            "bc9a9248|" +
            "64434d24|" +
            "ffea076e|" +
            "212292d8|" +
            "a99a1330|" +
            "007bfc00|" +
            "fe2bb6ea|" +
            "02c204ea|" +
            "fa4afeea|" +
            "0a22940a|" +
            "eaabc4fa|" +
            "ea8a1002|" +
            "ea63fcfe"
        },
        {
            HanXinErrorCorrectionLevel.L4,
            12,
            "Twelve",
            45,
            "fefecaa4bbf8|" +
            "800395f15008|" +
            "beaaaaaaa3e8|" +
            "a05355555828|" +
            "aeaaaaaaaba8|" +
            "aed36554eba8|" +
            "ae13aaaaa3a8|" +
            "00d355555800|" +
            "20aaaaaaa4a8|" +
            "5553015405a8|" +
            "6aaaaaaaaaa8|" +
            "555355555548|" +
            "1aaa48b6aaa8|" +
            "555355555550|" +
            "2aaaaa862000|" +
            "95d3fffd5548|" +
            "2aa80006aaa0|" +
            "d55555555550|" +
            "a8722a55aaa8|" +
            "d55555555550|" +
            "aaaaaaa6a910|" +
            "e1ff55555550|" +
            "aaaaaaa6aaa8|" +
            "d5555674e980|" +
            "eaaaaaa6aaa8|" +
            "d55555555550|" +
            "a8ab5f56aaa8|" +
            "d55555555550|" +
            "aaaaaaa6aaa8|" +
            "990555555550|" +
            "fffeaaa7fff8|" +
            "000355500008|" +
            "aaaaaaaaaaa8|" +
            "74aa35555548|" +
            "aaaaaaaaaaa8|" +
            "555355557208|" +
            "a952aaaaa820|" +
            "00d355555800|" +
            "fe2aaaaaa3a8|" +
            "02d355555ba8|" +
            "faa27666aba8|" +
            "0ad355555028|" +
            "ea2aaaaaabe8|" +
            "ea5355515008|" +
            "eaabfff4abf8"
        },
    };

    [Theory]
    [MemberData(nameof(MatchesReferenceGridData))]
    public void MatchesReferenceGrid(HanXinErrorCorrectionLevel level, int version, string text, int side, string expected)
    {
        HanXinSymbology symbology = version == 0 ? new HanXinSymbology(level) : new HanXinSymbology(level, version);
        SymbolEncoder.Encode(symbology, text, new BarcodeOptions(), (ref symbol) =>
        {
            Assert.Equal((side, side), (symbol.MatrixRows, symbol.Columns));
            Assert.Equal(expected, SymbolEncoder.ModuleGrid(ref symbol));
        });
    }

    /// <summary>
    /// A fixed version holds a fixed number of data codewords, so data that needs more does not fit it.
    /// </summary>
    [Fact]
    public void RejectsDataBeyondTheVersion()
        => Assert.Throws<ArgumentException>(
            () => SymbolEncoder.Encode(new HanXinSymbology(HanXinErrorCorrectionLevel.L4, 1), "This text needs more than version 1 holds", new BarcodeOptions()));

    /// <summary>
    /// Han Xin Code carries the 256 byte values, U+0000 to U+00FF.
    /// </summary>
    [Fact]
    public void RejectsCharactersAboveLatin1()
        => Assert.Throws<ArgumentException>(() => SymbolEncoder.Encode(new HanXinSymbology(), "Ā", new BarcodeOptions()));
}
