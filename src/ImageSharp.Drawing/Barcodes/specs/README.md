# Barcode specification documents

These are the specifications the barcode symbologies in this folder are built to. They are kept in the
tree so a rule can be checked against its source rather than against memory.

| File | Document | Source | Status |
| ---- | -------- | ------ | ------ |
| `gs1-general-specifications-26.pdf` | GS1 General Specifications Standard, Release 26.0, ratified January 2026, 579 pages. Defines the GS1 identification keys, the symbol specifications for EAN/UPC, ITF-14 and GS1-128, and the rules for the human readable interpretation. | <https://www.gs1.org/docs/barcodes/GS1_General_Specifications.pdf> | Published free of charge by GS1 AISBL |
| `isbn-users-manual-7th.pdf` | ISBN Users' Manual, International edition, 7th edition. Defines the ISBN and how it is printed with an EAN-13 symbol. | <https://www.isbn-international.org/sites/default/files/ISBN%20Manual%202012%20-corr.pdf> | Published free of charge by the International ISBN Agency |
| `ismn-users-manual-2008.pdf` | ISMN Users' Manual, revised edition 2008. Defines the ISMN and how it is printed with an EAN-13 symbol. | <https://www.musicadanza.es/es/agencia-ismn/031_ISMN_manual_2008.pdf> | Published free of charge by the International ISMN Agency |
| `bic-bar-coding-for-books-2019.pdf` | Bar Coding for Books, A Guide for Publishers, Book Industry Communication, revision 09, 2019. Gives the overall printed size of the book symbol at each magnification, with and without the ISBN caption. | <https://bic.org.uk/wp-content/uploads/2022/11/2019.05.31-Bar-Coding-for-Books-rev-09.pdf> | Published free of charge by Book Industry Communication |

## Documents referenced but not held

| Document | What it settles | Where it was read |
| -------- | --------------- | ----------------- |
| ISO/IEC 16388:2007, Code 39 bar code symbology specification | The Code 39 character assignments of Table 1, the nine element character structure of section 4.1 c), the 10 X quiet zone of section 4.4 d), the wide to narrow ratio range of section 4.4 b), the inter-character gap of section 4.4 c), the bar height recommendation of section 4.4 e), the symbol width formula of the note to section 4.4, and the modulo 43 check character of Annex A.1.1 with the character values of Table A.1. | Clauses 1 to 4.5 from the iTeh standard preview at <https://cdn.standards.iteh.ai/samples/43897/358ed85e97e14c3e81d30f621f57ec34/ISO-IEC-16388-2007.pdf>. Annex A.1 and Table A.1 from a copy of BS ISO/IEC 16388:2007 the maintainer holds. Annex A.2, the full ASCII encodation for Code 39 Extended, has not been read from the standard. |
| IFA, `Technical Information regarding PZN Coding - PZN in Code 39`, V 2.3, 01.04.2025 | The PZN data structure: the minus sign identifier of ISO/IEC 15418, the `*` delimiters which plain text does not show, the printed line `PZN - 12345678` whose term and spaces are not encoded, the nominal module width of 0.25 mm, the nominal 1:2.5 ratio permitting 1:2 to 1:3, the 10 X quiet zone, and the nominal code height of 10 mm. | <https://www.ifaffm.de/mandanten/1/documents/04_ifa_coding_system/IFA_Info_Code_39_EN.pdf>, published free of charge by IFA GmbH |
| IFA, `Technical Information regarding PZN Coding - Check Digit Calculations of PZN, PPN and Basic UDI-DI`, 26 January 2024 | The PZN check digit: modulo 11 over the digits weighted one upward, the remainder as the check digit, and that a remainder of 10 is not issued. Carries the worked example 2758089 summing to 174 and checking as 9. | <https://www.ifaffm.de/mandanten/1/documents/04_ifa_coding_system/IFA-Info_Check_Digit_Calculations_PZN_PPN_UDI_EN.pdf>, published free of charge by IFA GmbH |
| Gazzetta Ufficiale della Repubblica Italiana, Serie generale n. 165, 18 July 2014, Allegato A, `Caratteristiche tecniche del bollino farmaceutico` | The Italian pharmaceutical code: the nine digit AIC code whose first digit is zero and last a check digit, the check digit rule of section 3, the base 32 alphabet of Table 1, and the Code 39 representation between asterisks with a 0.250 mm narrow module. | <https://www.medicoeleggi.com/archivio/406145-a1.pdf>, published in the official gazette of the Italian Republic |
| ANSI/AIM BC5-1995, Code 93 bar code symbology specification, also published as AIM ITS 93i | Code 93: the 47 symbol character patterns of Table 2, the full ASCII mapping of Table 3, and the symbol measurement of Section 2.6, which gives the length as `(9 * (C + 4) + 1) * X + 2 * Q` and a minimum height of 0.2 inches or 15 per cent of that length, whichever is greater. | The standard is sold rather than published, and no clause of it has been read here. Two reference implementations were compared instead. Both carry the same patterns element for element, and one cites Table 2, Table 3 and Section 2.6 by number for the rules above. |
| AIM USS-I 2/5, Uniform Symbology Specification Interleaved 2 of 5 | Interleaved 2 of 5 in full: the two wide of five element characters and the bar and space pairing of section 2.1 and 2.2.3, the leading zero of section 2.2.1, the start and stop patterns of section 2.3, the quiet zone of section 2.4 ("ten times the X dimension or 0.10 inch (2.54 mm), whichever is greater"), the wide element range of section 3.2 ("2.0X to 3.0X", narrowing to "2.2X to 3.0X" below 0.020 inches) and its minimum height ("0.25 inches (6.35 mm) or 15 percent of the bar code symbol length, whichever is greater"), the symbol length formula `L = (P (4N+6) + 6 + N) X + 2Q` of section 3.3, the optional check digit of section 2.5 with the alternate 1 and 3 weighting of Appendix C, and the human readable interpretation of Appendix D, which includes "all numeric characters in the code including leading zeroes" and shows no start or stop character. | <https://www.expresscorp.com/wp-content/uploads/2023/02/USS-I-2of-5.pdf>, a scan of the AIM specification hosted by a label vendor. ISO/IEC 16390:2007 succeeded it and is sold rather than published, and the secondary reference implementation cites its Section 4.4 for the same 10X quiet zone and 15 per cent height. Section 5.3 of the GS1 General Specifications, held here, gives the same encodation for ITF-14: Table 5-23, 5.3.2.1.1, 5.3.2.1.2 and 5.3.2.2, with the narrower ratio range of 2.25:1 to 3.0:1. |
| Deutsche Post AG, `Identcode und Leitcode für Postpakete`, MatNr 671-677 | The Leitcode structure of 5 + 3 + 3 + 2 digits and a check digit, the Identcode structure of 2 digits for the mail centre, a customer number of 1 to 5 digits, an item number and a check digit, the check digit rule "Anstelle der bei 2 aus 5 verwendeten Gewichte 3 und 1 werden die Stellen mit den Werten 4 und 9 gewichtet" with the worked examples 2134807501640 summing to 239 and checking as 1, and 56310243031 summing to 187 and checking as 3, the printed line rule "Die Klartextzeile enthält zwischen den einzelnen Stellenbereichen jeweils einen Punkt, die Prüfziffer wird durch ein Leerzeichen etwas abgesetzt", and the dimensions: narrow module 0.375 mm to 0.5 mm, ratio 1:2 to 1:3, height at least 25 mm, quiet zone at least 5 mm each side. | The document is not published for download, and the DHL label sheet of December 2022 refers to a "DPDHL Barcode-Spezifikation" that is not published either. The rules above are quoted by three vendor manuals that reproduce the Deutsche Post wording and cite the document by title and order number: <https://www.idealsoftware.com/manuals/progman/leitcodedeutschepostag.html>, <https://www.idealsoftware.com/manuals/progman/identcodedeutschepost.html>, <https://will-software.com/infos/hlp_barc/C_2_5_Post.htm> and the stethos Barcode-Generator manual. Both reference implementations agree with the worked examples. The Identcode printed line has no fixed rule, because the customer number length varies and one manual states "Da die Post AG keine Angabe macht, wo die Trennung zwischen Kundenkennung und Einlieferungsnummer erfolgen muss". The main reference implementation and <https://www.activebarcode.com/barcode/identcode/> both print the worked example as `56.310 243.031 3`, and this library follows them. DHL's current label page at <https://www.dhl.de/en/geschaeftskunden/paket/information/geschaeftskunden/abrechnung/leitcodierung.html> adds that the distance to the label edge must be at least 5 mm and to an adjacent barcode at least 7 mm left and right or 3 mm above and below. |
| Industrial, IATA, Matrix, COOP and Datalogic 2 of 5 | These five symbologies have no standard. Wikipedia gives the history of Industrial 2 of 5, "invented in 1971 by Identicon Corp. and Computer Identics Corp.", that the data is "encoded only in black bars and white spaces are just ignored", and that it "may include an optional check digit". The same page and the Matrix 2 of 5 page give the digit patterns, which are those of Interleaved 2 of 5 read as bars alone or as bars and spaces. The vendor pages agree that the check digit is optional for Industrial, Matrix and Datalogic. For IATA 2 of 5, Scandit gives "By default no checksum is verified" and the name "Computer Identics 2 of 5", while Accusoft and Neodynamic treat the check digit as part of the symbol. Both reference implementations make it optional in every variant, and this library does the same. COOP 2 of 5 appears in no document at all, and its patterns come from the main reference implementation alone. No document gives a wide element width, a quiet zone or a bar height, and the secondary reference implementation notes "No known standards. Following C25INTER, set to 10X" for the quiet zone, so this library draws all five with the values of Interleaved 2 of 5. | <https://en.wikipedia.org/wiki/Industrial_2_of_5>, <https://en.wikipedia.org/wiki/Matrix_2_of_5>, <https://barcodeguide.seagullscientific.com/Content/Symbologies/Standard_2_of_5.htm>, <https://barcodeguide.seagullscientific.com/Content/Symbologies/Matrix_2_of_5.htm>, <https://barcodeguide.seagullscientific.com/Content/Symbologies/Datalogic_2_of_5.htm>, <https://www.scandit.com/products/barcode-scanning/symbologies/iata-2-of-5/>, <https://www.accusoft.com/barcodes/airline-2-of-5-barcodes/>, <https://www.accusoft.com/barcodes/datalogic-2-of-5/>, <https://www.neodynamic.com/barcodes/Industrial-2-of-5-Barcode.aspx>, <https://www.neodynamic.com/barcodes/IATA-2-of-5-Barcode.aspx>, <https://www.neodynamic.com/barcodes/Data-Logic-2-of-5-Barcode.aspx>. The ZXing.Net decoder has no reader for any of the five, so their round trip test cannot be written. |

ISO sells its standards and they are not redistributable, so ISO/IEC 16388 is not held here. The
preview is free to read at the address above. The IFA and Gazzetta Ufficiale documents are free to
download at the addresses above.

Code 93 and Code 93 Extended have no free specification. ANSI/AIM BC5-1995, also published as
AIM ITS 93i, is sold rather than published, and no clause of it has been read. Their numbers will
have to come from implementations that agree with each other, and the code and the commit will say
so rather than cite a document.

The ISSN Manual is not held here: the ISSN International Centre publishes it, but no stable direct
download was reachable at the time of writing. ISO 2108 (ISBN), ISO 3297 (ISSN) and ISO 10957 (ISMN)
are sold by ISO and are not redistributable, so they are not held here either.

## Rules these documents settle

**The add-on caption.** GS1 General Specifications section on the EAN/UPC human readable
interpretation: "The human readable interpretation of the add-on symbol SHALL be above the symbol. The
digits SHALL be the same height as those of the main symbol. The upper edges of the digits are aligned
with the upper edges of the bars (dark bars) of the main symbol. The minimum space between the bottom
of the digits and the top of the bars (dark bars) SHALL be 0.5X." So the add-on digits anchor by their
top edge, not by a gap above the bars.

**The identifier caption.** ISBN Users' Manual: "When used in a bar code, the ISBN must be displayed
in human readable form immediately above the EAN-13 bar code symbol, preceded by 'ISBN'." It also
sets the size: "The ISBN should always be printed in type large enough to be easily legible (i.e.,
9-point or larger)." The ISMN Users' Manual gives the same floor, "9 point or larger", and requires
OCR-B for the human readable number.

**The caption width.** Bar Coding for Books gives the overall printed size of the whole symbol,
caption included, at each magnification. Version NR, the basic symbol with the ISBN above the code,
is 38 by 31 mm at 100 percent. The GS1 General Specifications gives the EAN-13 symbol at its nominal
X-dimension of 0.330 mm as 37.29 mm wide with a bar height of 22.85 mm. The caption therefore prints
inside the width of the symbol, and the strip above the bars is part of the 31 mm total.

Neither document gives a numeric gap between the caption and the bars.

**The ITF-14 bearer bar.** GS1 General Specifications section 5.3.2.4: "The bearer bar is mandatory
unless it is not technically feasible to apply it." For plate printing it "has a constant thickness of
4.83 millimetres (0.190 inch) and must completely surround the symbol, including its Quiet Zones and
butt directly against the top and bottom of the bars (dark bars) of the symbol." Without plates it
"SHALL be a minimum of twice the width of a narrow bar" at the top and bottom, and "it is not mandatory
to print the vertical sections of the bearer bar." Section 5.3.2.2 gives the target X of 1.016
millimetres, so the frame is 4.83 / 1.016 modules thick, and requires "a minimum space of 1.02
millimetre (0.040 inch) between the bottom line of the bearer bar and the top of the human readable
characters", which at that X is the one module clear space the renderer keeps below every symbol.
Footnote (****) of the symbol height table in section 5.12.3.2 gives the minimum bar height of 31.75
millimetres, "31.75 millimetres (1.250 inch)", which does "not include human readable interpretation
text or ITF-14 symbol bearer bars". Section 4.14 rule 2.a: "Spaces SHALL NOT be encoded in the barcode"
and "Spaces may be used in the HRI itself to ease manual data input", so the input may carry spaces,
which the printed line keeps and the symbol drops. Figure 5-32 prints the same number both as
`1 54 00141 28876 3` and as `15400141288763`.
