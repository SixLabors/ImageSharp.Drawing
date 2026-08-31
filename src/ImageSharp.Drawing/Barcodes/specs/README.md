# Barcode specification documents

These are the specifications the barcode symbologies in this folder are built to. They are kept in the
tree so a rule can be checked against its source rather than against memory.

| File | Document | Source | Status |
| ---- | -------- | ------ | ------ |
| `gs1-general-specifications-26.pdf` | GS1 General Specifications Standard, Release 26.0, ratified January 2026, 579 pages. Defines the GS1 identification keys, the symbol specifications for EAN/UPC and the rules for the human readable interpretation. | <https://www.gs1.org/docs/barcodes/GS1_General_Specifications.pdf> | Published free of charge by GS1 AISBL |
| `isbn-users-manual-7th.pdf` | ISBN Users' Manual, International edition, 7th edition. Defines the ISBN and how it is printed with an EAN-13 symbol. | <https://www.isbn-international.org/sites/default/files/ISBN%20Manual%202012%20-corr.pdf> | Published free of charge by the International ISBN Agency |
| `ismn-users-manual-2008.pdf` | ISMN Users' Manual, revised edition 2008. Defines the ISMN and how it is printed with an EAN-13 symbol. | <https://www.musicadanza.es/es/agencia-ismn/031_ISMN_manual_2008.pdf> | Published free of charge by the International ISMN Agency |
| `bic-bar-coding-for-books-2019.pdf` | Bar Coding for Books, A Guide for Publishers, Book Industry Communication, revision 09, 2019. Gives the overall printed size of the book symbol at each magnification, with and without the ISBN caption. | <https://bic.org.uk/wp-content/uploads/2022/11/2019.05.31-Bar-Coding-for-Books-rev-09.pdf> | Published free of charge by Book Industry Communication |

## Documents referenced but not held

| Document | What it settles | Where it was read |
| -------- | --------------- | ----------------- |
| ISO/IEC 16388:2007, Code 39 bar code symbology specification | The Code 39 character assignments of Table 1, the nine element character structure of section 4.1 c), the 10 X quiet zone of section 4.4 d), the wide to narrow ratio range of section 4.4 b), the inter-character gap of section 4.4 c) and the bar height recommendation of section 4.4 e). | The iTeh standard preview at <https://cdn.standards.iteh.ai/samples/43897/358ed85e97e14c3e81d30f621f57ec34/ISO-IEC-16388-2007.pdf>, which carries clauses 1 to 4.5 in full. Annex A, which defines the check character calculation, is not in the preview, so that calculation follows the BWIPP reference implementation instead. |

ISO sells its standards and they are not redistributable, so the file is not held here. The preview
is free to read at the address above.

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
