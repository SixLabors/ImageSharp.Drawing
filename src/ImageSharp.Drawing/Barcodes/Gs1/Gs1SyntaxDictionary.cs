// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// The GS1 Application Identifiers and the rules of their data, from the GS1 Barcode Syntax Dictionary, release 2026-01-27, which GS1 maintains in step with
/// release 26 of the GS1 General Specifications.
/// </summary>
internal static class Gs1SyntaxDictionary
{
    /// <summary>
    /// The entries, one for each Application Identifier or range of them, in the lexical order of their first Application Identifier, each with its title from
    /// the dictionary. An entry gives the first and last Application Identifier, whether the element string has a predefined length, whether it can be a GS1
    /// Digital Link data attribute, the components of its data, its excluded Application Identifiers, and its GS1 Digital Link qualifier sequences when it is a
    /// primary key. The requisite rules of the dictionary are left out: they hold over the combined data of all carriers that mark an item, which one symbol
    /// does not hold.
    /// </summary>
    private static readonly Gs1ApplicationIdentifier[] Table =
    [

        // SSCC
        new("00", "00", true, true, [new('N', 18, 18, false, [Gs1Linter.Csum, Gs1Linter.GcpPos2])], [], [[]]),

        // GTIN
        new("01", "01", true, true, [new('N', 14, 14, false, [Gs1Linter.Csum, Gs1Linter.GcpPos2])], ["255", "37"], [["22", "10", "21"], ["235"]]),

        // CONTENT
        new("02", "02", true, true, [new('N', 14, 14, false, [Gs1Linter.Csum, Gs1Linter.GcpPos2])], ["01", "03"], null),

        // MTO GTIN
        new("03", "03", true, false, [new('N', 14, 14, false, [Gs1Linter.Csum, Gs1Linter.GcpPos2])], ["01", "02", "37"], null),

        // BATCH/LOT
        new("10", "10", false, true, [new('X', 1, 20, false, [])], [], null),

        // PROD DATE
        new("11", "11", true, true, [new('N', 6, 6, false, [Gs1Linter.Yymmd0])], [], null),

        // DUE DATE
        new("12", "12", true, true, [new('N', 6, 6, false, [Gs1Linter.Yymmd0])], [], null),

        // PACK DATE
        new("13", "13", true, true, [new('N', 6, 6, false, [Gs1Linter.Yymmd0])], [], null),

        // BEST BEFORE or BEST BY
        new("15", "15", true, true, [new('N', 6, 6, false, [Gs1Linter.Yymmd0])], [], null),

        // SELL BY
        new("16", "16", true, true, [new('N', 6, 6, false, [Gs1Linter.Yymmd0])], [], null),

        // USE BY or EXPIRY
        new("17", "17", true, true, [new('N', 6, 6, false, [Gs1Linter.Yymmd0])], [], null),

        // VARIANT
        new("20", "20", true, true, [new('N', 2, 2, false, [])], [], null),

        // SERIAL
        new("21", "21", false, false, [new('X', 1, 20, false, [])], ["235"], null),

        // CPV
        new("22", "22", false, false, [new('X', 1, 20, false, [])], [], null),

        // TPX
        new("235", "235", false, false, [new('X', 1, 28, false, [])], [], null),

        // ADDITIONAL ID
        new("240", "240", false, true, [new('X', 1, 30, false, [])], [], null),

        // CUST. PART No.
        new("241", "241", false, true, [new('X', 1, 30, false, [])], [], null),

        // MTO VARIANT
        new("242", "242", false, true, [new('N', 1, 6, false, [])], [], null),

        // PCN
        new("243", "243", false, true, [new('X', 1, 20, false, [])], [], null),

        // SECONDARY SERIAL
        new("250", "250", false, true, [new('X', 1, 30, false, [])], [], null),

        // REF. TO SOURCE
        new("251", "251", false, true, [new('X', 1, 30, false, [])], [], null),

        // GDTI
        new("253", "253", false, true, [new('N', 13, 13, false, [Gs1Linter.Csum, Gs1Linter.GcpPos1]), new('X', 1, 17, true, [])], [], [[]]),

        // GLN EXTENSION COMPONENT
        new("254", "254", false, false, [new('X', 1, 20, false, [])], [], null),

        // GCN
        new(
            "255",
            "255",
            false,
            true,
            [new('N', 13, 13, false, [Gs1Linter.Csum, Gs1Linter.GcpPos1]), new('N', 1, 12, true, [])],
            ["01", "02", "415", "8006", "8020", "8026"],
            [[]]),

        // VAR. COUNT
        new("30", "30", false, true, [new('N', 1, 8, false, [])], [], null),

        // NET WEIGHT (kg)
        new("3100", "3105", true, true, [new('N', 6, 6, false, [])], ["310n"], null),

        // LENGTH (m)
        new("3110", "3115", true, true, [new('N', 6, 6, false, [])], ["311n"], null),

        // WIDTH (m)
        new("3120", "3125", true, true, [new('N', 6, 6, false, [])], ["312n"], null),

        // HEIGHT (m)
        new("3130", "3135", true, true, [new('N', 6, 6, false, [])], ["313n"], null),

        // AREA (m²)
        new("3140", "3145", true, true, [new('N', 6, 6, false, [])], ["314n"], null),

        // NET VOLUME (l)
        new("3150", "3155", true, true, [new('N', 6, 6, false, [])], ["315n"], null),

        // NET VOLUME (m³)
        new("3160", "3165", true, true, [new('N', 6, 6, false, [])], ["316n"], null),

        // NET WEIGHT (lb)
        new("3200", "3205", true, true, [new('N', 6, 6, false, [])], ["320n"], null),

        // LENGTH (in)
        new("3210", "3215", true, true, [new('N', 6, 6, false, [])], ["321n"], null),

        // LENGTH (ft)
        new("3220", "3225", true, true, [new('N', 6, 6, false, [])], ["322n"], null),

        // LENGTH (yd)
        new("3230", "3235", true, true, [new('N', 6, 6, false, [])], ["323n"], null),

        // WIDTH (in)
        new("3240", "3245", true, true, [new('N', 6, 6, false, [])], ["324n"], null),

        // WIDTH (ft)
        new("3250", "3255", true, true, [new('N', 6, 6, false, [])], ["325n"], null),

        // WIDTH (yd)
        new("3260", "3265", true, true, [new('N', 6, 6, false, [])], ["326n"], null),

        // HEIGHT (in)
        new("3270", "3275", true, true, [new('N', 6, 6, false, [])], ["327n"], null),

        // HEIGHT (ft)
        new("3280", "3285", true, true, [new('N', 6, 6, false, [])], ["328n"], null),

        // HEIGHT (yd)
        new("3290", "3295", true, true, [new('N', 6, 6, false, [])], ["329n"], null),

        // GROSS WEIGHT (kg)
        new("3300", "3305", true, true, [new('N', 6, 6, false, [])], ["330n"], null),

        // LENGTH (m), log
        new("3310", "3315", true, true, [new('N', 6, 6, false, [])], ["331n"], null),

        // WIDTH (m), log
        new("3320", "3325", true, true, [new('N', 6, 6, false, [])], ["332n"], null),

        // HEIGHT (m), log
        new("3330", "3335", true, true, [new('N', 6, 6, false, [])], ["333n"], null),

        // AREA (m²), log
        new("3340", "3345", true, true, [new('N', 6, 6, false, [])], ["334n"], null),

        // VOLUME (l), log
        new("3350", "3355", true, true, [new('N', 6, 6, false, [])], ["335n"], null),

        // VOLUME (m³), log
        new("3360", "3365", true, true, [new('N', 6, 6, false, [])], ["336n"], null),

        // KG PER m²
        new("3370", "3375", true, true, [new('N', 6, 6, false, [])], ["337n"], null),

        // GROSS WEIGHT (lb)
        new("3400", "3405", true, true, [new('N', 6, 6, false, [])], ["340n"], null),

        // LENGTH (in), log
        new("3410", "3415", true, true, [new('N', 6, 6, false, [])], ["341n"], null),

        // LENGTH (ft), log
        new("3420", "3425", true, true, [new('N', 6, 6, false, [])], ["342n"], null),

        // LENGTH (yd), log
        new("3430", "3435", true, true, [new('N', 6, 6, false, [])], ["343n"], null),

        // WIDTH (in), log
        new("3440", "3445", true, true, [new('N', 6, 6, false, [])], ["344n"], null),

        // WIDTH (ft), log
        new("3450", "3455", true, true, [new('N', 6, 6, false, [])], ["345n"], null),

        // WIDTH (yd), log
        new("3460", "3465", true, true, [new('N', 6, 6, false, [])], ["346n"], null),

        // HEIGHT (in), log
        new("3470", "3475", true, true, [new('N', 6, 6, false, [])], ["347n"], null),

        // HEIGHT (ft), log
        new("3480", "3485", true, true, [new('N', 6, 6, false, [])], ["348n"], null),

        // HEIGHT (yd), log
        new("3490", "3495", true, true, [new('N', 6, 6, false, [])], ["349n"], null),

        // AREA (in²)
        new("3500", "3505", true, true, [new('N', 6, 6, false, [])], ["350n"], null),

        // AREA (ft²)
        new("3510", "3515", true, true, [new('N', 6, 6, false, [])], ["351n"], null),

        // AREA (yd²)
        new("3520", "3525", true, true, [new('N', 6, 6, false, [])], ["352n"], null),

        // AREA (in²), log
        new("3530", "3535", true, true, [new('N', 6, 6, false, [])], ["353n"], null),

        // AREA (ft²), log
        new("3540", "3545", true, true, [new('N', 6, 6, false, [])], ["354n"], null),

        // AREA (yd²), log
        new("3550", "3555", true, true, [new('N', 6, 6, false, [])], ["355n"], null),

        // NET WEIGHT (tr oz)
        new("3560", "3565", true, true, [new('N', 6, 6, false, [])], ["356n"], null),

        // NET VOLUME (oz)
        new("3570", "3575", true, true, [new('N', 6, 6, false, [])], ["357n"], null),

        // NET VOLUME (qt (US))
        new("3600", "3605", true, true, [new('N', 6, 6, false, [])], ["360n"], null),

        // NET VOLUME (gal.)
        new("3610", "3615", true, true, [new('N', 6, 6, false, [])], ["361n"], null),

        // VOLUME (qt (US)), log
        new("3620", "3625", true, true, [new('N', 6, 6, false, [])], ["362n"], null),

        // VOLUME (gal (US)), log
        new("3630", "3635", true, true, [new('N', 6, 6, false, [])], ["363n"], null),

        // NET VOLUME (in³)
        new("3640", "3645", true, true, [new('N', 6, 6, false, [])], ["364n"], null),

        // NET VOLUME (ft³)
        new("3650", "3655", true, true, [new('N', 6, 6, false, [])], ["365n"], null),

        // NET VOLUME (yd³)
        new("3660", "3665", true, true, [new('N', 6, 6, false, [])], ["366n"], null),

        // VOLUME (in³), log
        new("3670", "3675", true, true, [new('N', 6, 6, false, [])], ["367n"], null),

        // VOLUME (ft³), log
        new("3680", "3685", true, true, [new('N', 6, 6, false, [])], ["368n"], null),

        // VOLUME (yd³), log
        new("3690", "3695", true, true, [new('N', 6, 6, false, [])], ["369n"], null),

        // COUNT
        new("37", "37", false, true, [new('N', 1, 8, false, [])], [], null),

        // AMOUNT
        new("3900", "3909", false, true, [new('N', 1, 15, false, [])], ["390n", "391n", "394n", "8111"], null),

        // AMOUNT
        new("3910", "3919", false, true, [new('N', 3, 3, false, [Gs1Linter.Iso4217]), new('N', 1, 15, false, [])], ["391n"], null),

        // PRICE
        new("3920", "3929", false, true, [new('N', 1, 15, false, [])], ["392n", "393n"], null),

        // PRICE
        new("3930", "3939", false, true, [new('N', 3, 3, false, [Gs1Linter.Iso4217]), new('N', 1, 15, false, [])], ["393n"], null),

        // PRCNT OFF
        new("3940", "3943", false, true, [new('N', 4, 4, false, [])], ["394n", "8111"], null),

        // PRICE/UoM
        new("3950", "3955", false, true, [new('N', 6, 6, false, [])], ["392n", "393n", "395n", "8005"], null),

        // ORDER NUMBER
        new("400", "400", false, true, [new('X', 1, 30, false, [])], [], null),

        // GINC
        new("401", "401", false, true, [new('X', 1, 30, false, [Gs1Linter.GcpPos1])], [], [[]]),

        // GSIN
        new("402", "402", false, true, [new('N', 17, 17, false, [Gs1Linter.Csum, Gs1Linter.GcpPos1])], [], [[]]),

        // ROUTE
        new("403", "403", false, true, [new('X', 1, 30, false, [])], [], null),

        // SHIP TO LOC
        new("410", "410", true, true, [new('N', 13, 13, false, [Gs1Linter.Csum, Gs1Linter.GcpPos1])], [], null),

        // BILL TO
        new("411", "411", true, true, [new('N', 13, 13, false, [Gs1Linter.Csum, Gs1Linter.GcpPos1])], [], null),

        // PURCHASE FROM
        new("412", "412", true, true, [new('N', 13, 13, false, [Gs1Linter.Csum, Gs1Linter.GcpPos1])], [], null),

        // SHIP FOR LOC
        new("413", "413", true, true, [new('N', 13, 13, false, [Gs1Linter.Csum, Gs1Linter.GcpPos1])], [], null),

        // LOC No.
        new("414", "414", true, true, [new('N', 13, 13, false, [Gs1Linter.Csum, Gs1Linter.GcpPos1])], [], [["254"], ["7040"]]),

        // PAY TO
        new("415", "415", true, true, [new('N', 13, 13, false, [Gs1Linter.Csum, Gs1Linter.GcpPos1])], [], [["8020"]]),

        // PROD/SERV LOC
        new("416", "416", true, true, [new('N', 13, 13, false, [Gs1Linter.Csum, Gs1Linter.GcpPos1])], [], null),

        // PARTY
        new("417", "417", true, true, [new('N', 13, 13, false, [Gs1Linter.Csum, Gs1Linter.GcpPos1])], [], [["7040"]]),

        // SHIP TO POST
        new("420", "420", false, true, [new('X', 1, 20, false, [])], ["421"], null),

        // SHIP TO POST
        new("421", "421", false, true, [new('N', 3, 3, false, [Gs1Linter.Iso3166]), new('X', 1, 9, false, [])], ["4307"], null),

        // ORIGIN
        new("422", "422", false, true, [new('N', 3, 3, false, [Gs1Linter.Iso3166])], ["426"], null),

        // COUNTRY - INITIAL PROCESS
        new(
            "423",
            "423",
            false,
            true,
            [
                new('N', 3, 3, false, [Gs1Linter.Iso3166]),
                new('N', 3, 3, true, [Gs1Linter.Iso3166]),
                new('N', 3, 3, true, [Gs1Linter.Iso3166]),
                new('N', 3, 3, true, [Gs1Linter.Iso3166]),
                new('N', 3, 3, true, [Gs1Linter.Iso3166])
            ],
            ["426"],
            null),

        // COUNTRY - PROCESS
        new("424", "424", false, true, [new('N', 3, 3, false, [Gs1Linter.Iso3166])], ["426"], null),

        // COUNTRY - DISASSEMBLY
        new(
            "425",
            "425",
            false,
            true,
            [
                new('N', 3, 3, false, [Gs1Linter.Iso3166]),
                new('N', 3, 3, true, [Gs1Linter.Iso3166]),
                new('N', 3, 3, true, [Gs1Linter.Iso3166]),
                new('N', 3, 3, true, [Gs1Linter.Iso3166]),
                new('N', 3, 3, true, [Gs1Linter.Iso3166])
            ],
            ["426"],
            null),

        // COUNTRY - FULL PROCESS
        new("426", "426", false, true, [new('N', 3, 3, false, [Gs1Linter.Iso3166])], [], null),

        // ORIGIN SUBDIVISION
        new("427", "427", false, true, [new('X', 1, 3, false, [])], [], null),

        // SHIP TO COMP
        new("4300", "4300", false, true, [new('X', 1, 35, false, [Gs1Linter.PercentEncoded])], [], null),

        // SHIP TO NAME
        new("4301", "4301", false, true, [new('X', 1, 35, false, [Gs1Linter.PercentEncoded])], [], null),

        // SHIP TO ADD1
        new("4302", "4302", false, true, [new('X', 1, 70, false, [Gs1Linter.PercentEncoded])], [], null),

        // SHIP TO ADD2
        new("4303", "4303", false, true, [new('X', 1, 70, false, [Gs1Linter.PercentEncoded])], [], null),

        // SHIP TO SUB
        new("4304", "4304", false, true, [new('X', 1, 70, false, [Gs1Linter.PercentEncoded])], [], null),

        // SHIP TO LOC
        new("4305", "4305", false, true, [new('X', 1, 70, false, [Gs1Linter.PercentEncoded])], [], null),

        // SHIP TO REG
        new("4306", "4306", false, true, [new('X', 1, 70, false, [Gs1Linter.PercentEncoded])], [], null),

        // SHIP TO COUNTRY
        new("4307", "4307", false, true, [new('X', 2, 2, false, [Gs1Linter.Iso3166Alpha2])], [], null),

        // SHIP TO PHONE
        new("4308", "4308", false, true, [new('X', 1, 30, false, [])], [], null),

        // SHIP TO GEO
        new("4309", "4309", false, true, [new('N', 10, 10, false, [Gs1Linter.Latitude]), new('N', 10, 10, false, [Gs1Linter.Longitude])], [], null),

        // RTN TO COMP
        new("4310", "4310", false, true, [new('X', 1, 35, false, [Gs1Linter.PercentEncoded])], [], null),

        // RTN TO NAME
        new("4311", "4311", false, true, [new('X', 1, 35, false, [Gs1Linter.PercentEncoded])], [], null),

        // RTN TO ADD1
        new("4312", "4312", false, true, [new('X', 1, 70, false, [Gs1Linter.PercentEncoded])], [], null),

        // RTN TO ADD2
        new("4313", "4313", false, true, [new('X', 1, 70, false, [Gs1Linter.PercentEncoded])], [], null),

        // RTN TO SUB
        new("4314", "4314", false, true, [new('X', 1, 70, false, [Gs1Linter.PercentEncoded])], [], null),

        // RTN TO LOC
        new("4315", "4315", false, true, [new('X', 1, 70, false, [Gs1Linter.PercentEncoded])], [], null),

        // RTN TO REG
        new("4316", "4316", false, true, [new('X', 1, 70, false, [Gs1Linter.PercentEncoded])], [], null),

        // RTN TO COUNTRY
        new("4317", "4317", false, true, [new('X', 2, 2, false, [Gs1Linter.Iso3166Alpha2])], [], null),

        // RTN TO POST
        new("4318", "4318", false, true, [new('X', 1, 20, false, [])], [], null),

        // RTN TO PHONE
        new("4319", "4319", false, true, [new('X', 1, 30, false, [])], [], null),

        // SRV DESCRIPTION
        new("4320", "4320", false, true, [new('X', 1, 35, false, [Gs1Linter.PercentEncoded])], [], null),

        // DANGEROUS GOODS
        new("4321", "4321", false, true, [new('N', 1, 1, false, [Gs1Linter.YesNo])], [], null),

        // AUTH TO LEAVE
        new("4322", "4322", false, true, [new('N', 1, 1, false, [Gs1Linter.YesNo])], [], null),

        // SIG REQUIRED
        new("4323", "4323", false, true, [new('N', 1, 1, false, [Gs1Linter.YesNo])], [], null),

        // NOT BEF DEL DT
        new("4324", "4324", false, true, [new('N', 6, 6, false, [Gs1Linter.Yymmd0]), new('N', 4, 4, false, [Gs1Linter.Hhmi])], [], null),

        // NOT AFT DEL DT
        new("4325", "4325", false, true, [new('N', 6, 6, false, [Gs1Linter.Yymmd0]), new('N', 4, 4, false, [Gs1Linter.Hhmi])], [], null),

        // REL DATE
        new("4326", "4326", false, true, [new('N', 6, 6, false, [Gs1Linter.Yymmdd])], [], null),

        // MAX TEMP F.
        new("4330", "4330", false, true, [new('N', 6, 6, false, []), new('X', 1, 1, true, [Gs1Linter.Hyphen])], ["4331"], null),

        // MAX TEMP C.
        new("4331", "4331", false, true, [new('N', 6, 6, false, []), new('X', 1, 1, true, [Gs1Linter.Hyphen])], ["4330"], null),

        // MIN TEMP F.
        new("4332", "4332", false, true, [new('N', 6, 6, false, []), new('X', 1, 1, true, [Gs1Linter.Hyphen])], ["4333"], null),

        // MIN TEMP C.
        new("4333", "4333", false, true, [new('N', 6, 6, false, []), new('X', 1, 1, true, [Gs1Linter.Hyphen])], ["4332"], null),

        // NSN
        new("7001", "7001", false, true, [new('N', 13, 13, false, [])], [], null),

        // MEAT CUT
        new("7002", "7002", false, true, [new('X', 1, 30, false, [])], [], null),

        // EXPIRY TIME
        new("7003", "7003", false, true, [new('N', 6, 6, false, [Gs1Linter.Yymmdd]), new('N', 4, 4, false, [Gs1Linter.Hhmi])], [], null),

        // ACTIVE POTENCY
        new("7004", "7004", false, true, [new('N', 1, 4, false, [])], [], null),

        // CATCH AREA
        new("7005", "7005", false, true, [new('X', 1, 12, false, [])], [], null),

        // FIRST FREEZE DATE
        new("7006", "7006", false, true, [new('N', 6, 6, false, [Gs1Linter.Yymmdd])], [], null),

        // HARVEST DATE
        new("7007", "7007", false, true, [new('N', 6, 6, false, [Gs1Linter.Yymmdd]), new('N', 6, 6, true, [Gs1Linter.Yymmdd])], [], null),

        // AQUATIC SPECIES
        new("7008", "7008", false, true, [new('X', 1, 3, false, [])], [], null),

        // FISHING GEAR TYPE
        new("7009", "7009", false, true, [new('X', 1, 10, false, [])], [], null),

        // PROD METHOD
        new("7010", "7010", false, true, [new('X', 1, 2, false, [])], [], null),

        // TEST BY DATE
        new("7011", "7011", false, true, [new('N', 6, 6, false, [Gs1Linter.Yymmdd]), new('N', 4, 4, true, [Gs1Linter.Hhmi])], [], null),

        // REFURB LOT
        new("7020", "7020", false, true, [new('X', 1, 20, false, [])], [], null),

        // FUNC STAT
        new("7021", "7021", false, true, [new('X', 1, 20, false, [])], [], null),

        // REV STAT
        new("7022", "7022", false, true, [new('X', 1, 20, false, [])], [], null),

        // GIAI - ASSEMBLY
        new("7023", "7023", false, true, [new('X', 1, 30, false, [Gs1Linter.GcpPos1])], [], null),

        // PROCESSOR # 0
        new("7030", "7030", false, true, [new('N', 3, 3, false, [Gs1Linter.Iso3166999]), new('X', 1, 27, false, [])], [], null),

        // PROCESSOR # 1
        new("7031", "7031", false, true, [new('N', 3, 3, false, [Gs1Linter.Iso3166999]), new('X', 1, 27, false, [])], [], null),

        // PROCESSOR # 2
        new("7032", "7032", false, true, [new('N', 3, 3, false, [Gs1Linter.Iso3166999]), new('X', 1, 27, false, [])], [], null),

        // PROCESSOR # 3
        new("7033", "7033", false, true, [new('N', 3, 3, false, [Gs1Linter.Iso3166999]), new('X', 1, 27, false, [])], [], null),

        // PROCESSOR # 4
        new("7034", "7034", false, true, [new('N', 3, 3, false, [Gs1Linter.Iso3166999]), new('X', 1, 27, false, [])], [], null),

        // PROCESSOR # 5
        new("7035", "7035", false, true, [new('N', 3, 3, false, [Gs1Linter.Iso3166999]), new('X', 1, 27, false, [])], [], null),

        // PROCESSOR # 6
        new("7036", "7036", false, true, [new('N', 3, 3, false, [Gs1Linter.Iso3166999]), new('X', 1, 27, false, [])], [], null),

        // PROCESSOR # 7
        new("7037", "7037", false, true, [new('N', 3, 3, false, [Gs1Linter.Iso3166999]), new('X', 1, 27, false, [])], [], null),

        // PROCESSOR # 8
        new("7038", "7038", false, true, [new('N', 3, 3, false, [Gs1Linter.Iso3166999]), new('X', 1, 27, false, [])], [], null),

        // PROCESSOR # 9
        new("7039", "7039", false, true, [new('N', 3, 3, false, [Gs1Linter.Iso3166999]), new('X', 1, 27, false, [])], [], null),

        // UIC+EXT
        new(
            "7040",
            "7040",
            false,
            false,
            [new('N', 1, 1, false, []), new('X', 1, 1, false, []), new('X', 1, 1, false, []), new('X', 1, 1, false, [Gs1Linter.ImporterIndex])],
            [],
            null),

        // UFRGT UNIT TYPE
        new("7041", "7041", false, false, [new('X', 1, 4, false, [Gs1Linter.PackageType])], [], null),

        // NHRN PZN
        new("710", "710", false, true, [new('X', 1, 20, false, [])], [], null),

        // NHRN CIP
        new("711", "711", false, true, [new('X', 1, 20, false, [])], [], null),

        // NHRN CN
        new("712", "712", false, true, [new('X', 1, 20, false, [])], [], null),

        // NHRN DRN
        new("713", "713", false, true, [new('X', 1, 20, false, [])], [], null),

        // NHRN AIM
        new("714", "714", false, true, [new('X', 1, 20, false, [])], [], null),

        // NHRN NDC
        new("715", "715", false, true, [new('X', 1, 20, false, [])], [], null),

        // NHRN AIC
        new("716", "716", false, true, [new('X', 1, 20, false, [])], [], null),

        // NHRN SRN
        new("717", "717", false, true, [new('X', 1, 20, false, [])], [], null),

        // CERT # 1
        new("7230", "7230", false, true, [new('X', 2, 2, false, []), new('X', 1, 28, false, [])], [], null),

        // CERT # 2
        new("7231", "7231", false, true, [new('X', 2, 2, false, []), new('X', 1, 28, false, [])], [], null),

        // CERT # 3
        new("7232", "7232", false, true, [new('X', 2, 2, false, []), new('X', 1, 28, false, [])], [], null),

        // CERT # 4
        new("7233", "7233", false, true, [new('X', 2, 2, false, []), new('X', 1, 28, false, [])], [], null),

        // CERT # 5
        new("7234", "7234", false, true, [new('X', 2, 2, false, []), new('X', 1, 28, false, [])], [], null),

        // CERT # 6
        new("7235", "7235", false, true, [new('X', 2, 2, false, []), new('X', 1, 28, false, [])], [], null),

        // CERT # 7
        new("7236", "7236", false, true, [new('X', 2, 2, false, []), new('X', 1, 28, false, [])], [], null),

        // CERT # 8
        new("7237", "7237", false, true, [new('X', 2, 2, false, []), new('X', 1, 28, false, [])], [], null),

        // CERT # 9
        new("7238", "7238", false, true, [new('X', 2, 2, false, []), new('X', 1, 28, false, [])], [], null),

        // CERT # 10
        new("7239", "7239", false, true, [new('X', 2, 2, false, []), new('X', 1, 28, false, [])], [], null),

        // PROTOCOL
        new("7240", "7240", false, true, [new('X', 1, 20, false, [])], [], null),

        // AIDC MEDIA TYPE
        new("7241", "7241", false, true, [new('N', 2, 2, false, [Gs1Linter.MediaType])], [], null),

        // VCN
        new("7242", "7242", false, true, [new('X', 1, 25, false, [])], [], null),

        // DOB
        new("7250", "7250", false, true, [new('N', 8, 8, false, [Gs1Linter.Yyyymmdd])], ["7251"], null),

        // DOB TIME
        new("7251", "7251", false, true, [new('N', 8, 8, false, [Gs1Linter.Yyyymmdd]), new('N', 4, 4, false, [Gs1Linter.Hhmi])], ["7250"], null),

        // BIO SEX
        new("7252", "7252", false, true, [new('N', 1, 1, false, [Gs1Linter.Iso5218])], [], null),

        // FAMILY NAME
        new("7253", "7253", false, true, [new('X', 1, 40, false, [Gs1Linter.PercentEncoded])], ["7256", "7259"], null),

        // GIVEN NAME
        new("7254", "7254", false, true, [new('X', 1, 40, false, [Gs1Linter.PercentEncoded])], ["7256", "7259"], null),

        // SUFFIX
        new("7255", "7255", false, true, [new('X', 1, 10, false, [])], ["7256", "7259"], null),

        // FULL NAME
        new("7256", "7256", false, true, [new('X', 1, 90, false, [Gs1Linter.PercentEncoded])], [], null),

        // PERSON ADDR
        new("7257", "7257", false, true, [new('X', 1, 70, false, [Gs1Linter.PercentEncoded])], [], null),

        // BIRTH SEQUENCE
        new("7258", "7258", false, true, [new('X', 3, 3, false, [Gs1Linter.PositionInSequence])], [], null),

        // BABY
        new("7259", "7259", false, true, [new('X', 1, 40, false, [Gs1Linter.PercentEncoded])], ["7256"], null),

        // DIMENSIONS
        new(
            "8001",
            "8001",
            false,
            true,
            [
                new('N', 4, 4, false, [Gs1Linter.NonZero]),
                new('N', 5, 5, false, [Gs1Linter.NonZero]),
                new('N', 3, 3, false, [Gs1Linter.NonZero]),
                new('N', 1, 1, false, [Gs1Linter.Winding]),
                new('N', 1, 1, false, [])
            ],
            [],
            null),

        // CMT No.
        new("8002", "8002", false, true, [new('X', 1, 20, false, [])], [], null),

        // GRAI
        new(
            "8003",
            "8003",
            false,
            true,
            [new('N', 1, 1, false, [Gs1Linter.Zero]), new('N', 13, 13, false, [Gs1Linter.Csum, Gs1Linter.GcpPos1]), new('X', 1, 16, true, [])],
            [],
            [[]]),

        // GIAI
        new("8004", "8004", false, true, [new('X', 1, 30, false, [Gs1Linter.GcpPos1])], [], [["7040"]]),

        // PRICE PER UNIT
        new("8005", "8005", false, true, [new('N', 6, 6, false, [])], [], null),

        // ITIP
        new(
            "8006",
            "8006",
            false,
            true,
            [new('N', 14, 14, false, [Gs1Linter.Csum, Gs1Linter.GcpPos2]), new('N', 4, 4, false, [Gs1Linter.PieceOfTotal])],
            ["01", "37"],
            [["22", "10", "21"]]),

        // IBAN
        new("8007", "8007", false, true, [new('X', 1, 34, false, [Gs1Linter.Iban])], [], null),

        // PROD TIME
        new(
            "8008",
            "8008",
            false,
            true,
            [
                new('N', 6, 6, false, [Gs1Linter.Yymmdd]),
                new('N', 2, 2, false, [Gs1Linter.Hh]),
                new('N', 2, 2, true, [Gs1Linter.Mi]),
                new('N', 2, 2, true, [Gs1Linter.Ss])
            ],
            [],
            null),

        // OPTSEN
        new("8009", "8009", false, true, [new('X', 1, 50, false, [])], [], null),

        // CPID
        new("8010", "8010", false, true, [new('Y', 1, 30, false, [Gs1Linter.GcpPos1])], [], [["8011"]]),

        // CPID SERIAL
        new("8011", "8011", false, false, [new('N', 1, 12, false, [Gs1Linter.NoZeroPrefix])], [], null),

        // VERSION
        new("8012", "8012", false, true, [new('X', 1, 20, false, [])], [], null),

        // GMN
        new("8013", "8013", false, true, [new('X', 1, 25, false, [Gs1Linter.CsumAlpha, Gs1Linter.GcpPos1])], [], [[]]),

        // MUDI
        new("8014", "8014", false, false, [new('X', 1, 25, false, [Gs1Linter.CsumAlpha, Gs1Linter.GcpPos1, Gs1Linter.HasNonDigit])], [], null),

        // GSRN - PROVIDER
        new("8017", "8017", false, true, [new('N', 18, 18, false, [Gs1Linter.Csum, Gs1Linter.GcpPos1])], ["8018"], [["8019"]]),

        // GSRN - RECIPIENT
        new("8018", "8018", false, true, [new('N', 18, 18, false, [Gs1Linter.Csum, Gs1Linter.GcpPos1])], ["8017"], [["8019"]]),

        // SRIN
        new("8019", "8019", false, false, [new('N', 1, 10, false, [])], [], null),

        // REF No.
        new("8020", "8020", false, false, [new('X', 1, 25, false, [])], [], null),

        // ITIP CONTENT
        new(
            "8026",
            "8026",
            false,
            true,
            [new('N', 14, 14, false, [Gs1Linter.Csum, Gs1Linter.GcpPos2]), new('N', 4, 4, false, [Gs1Linter.PieceOfTotal])],
            ["02", "8006"],
            null),

        // DIGSIG
        new("8030", "8030", false, true, [new('Z', 1, 90, false, [])], [], null),

        // IMEI
        new("8040", "8040", false, false, [new('N', 15, 15, false, [])], [], null),

        // IMEI2
        new("8041", "8041", false, false, [new('N', 15, 15, false, [])], [], null),

        // ESIM
        new("8042", "8042", false, false, [new('N', 32, 32, false, [])], [], null),

        // PSIM
        new("8043", "8043", false, false, [new('N', 18, 18, false, []), new('N', 1, 2, true, [])], [], null),

        new("8110", "8110", false, true, [new('X', 1, 70, false, [Gs1Linter.CouponCode])], [], null),

        // POINTS
        new("8111", "8111", false, true, [new('N', 4, 4, false, [])], [], null),

        new("8112", "8112", false, true, [new('X', 1, 70, false, [Gs1Linter.CouponPositiveOffer])], [], null),

        // PRODUCT URL
        new("8200", "8200", false, false, [new('X', 1, 70, false, [])], [], null),

        // INTERNAL
        new("90", "90", false, true, [new('X', 1, 30, false, [])], [], null),

        // INTERNAL
        new("91", "99", false, true, [new('X', 1, 90, false, [])], [], null),
    ];

    /// <summary>
    /// Gets the entries in the lexical order of their first Application Identifier.
    /// </summary>
    public static ReadOnlySpan<Gs1ApplicationIdentifier> Entries => Table;

    /// <summary>
    /// Returns the entry that holds the given Application Identifier.
    /// </summary>
    /// <param name="identifier">The Application Identifier, two to four digits.</param>
    /// <returns>The entry, or <see langword="null"/> when the identifier is not an allocated Application Identifier.</returns>
    public static Gs1ApplicationIdentifier? Find(ReadOnlySpan<char> identifier)
    {
        int index = IndexOf(identifier);
        return index >= 0 ? Table[index] : null;
    }

    /// <summary>
    /// Returns the index in <see cref="Entries"/> of the entry that holds the given Application Identifier.
    /// </summary>
    /// <param name="identifier">The Application Identifier, two to four digits.</param>
    /// <returns>The index, or -1 when the identifier is not an allocated Application Identifier.</returns>
    public static int IndexOf(ReadOnlySpan<char> identifier)
    {
        // The entries are in lexical order, and no Application Identifier is a prefix of another, so the only entry that can hold the identifier is the last
        // one whose first identifier is not after it.
        int low = 0;
        int high = Table.Length - 1;
        int candidate = -1;
        while (low <= high)
        {
            int middle = low + ((high - low) / 2);
            if (Table[middle].First.AsSpan().SequenceCompareTo(identifier) <= 0)
            {
                candidate = middle;
                low = middle + 1;
            }
            else
            {
                high = middle - 1;
            }
        }

        return candidate >= 0 && Table[candidate].Holds(identifier) ? candidate : -1;
    }
}
