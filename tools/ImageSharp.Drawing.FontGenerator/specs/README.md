# Font source documents

These are the primary specification documents the OCR-A and OCR-B glyph outlines in this generator are
built from. Both fonts are constructed clean-room: every glyph is transcribed from the dimensioned
character drawings in these documents, never from an existing font file.

| File | Document | Source | Status |
| ---- | -------- | ------ | ------ |
| `fipspub32-1974.pdf` | FIPS PUB 32, *Character Set OCR-A* (NBS, 1974). Adopts ANSI X3.17-1974. Figures II-11 through II-96 are complete dimensioned centerline drawings of every character; Table II-3 gives the numeric dimension equivalents (W, H, T, r1-r4) per size. | nvlpubs.nist.gov | U.S. Government work, public domain |
| `fipspub32-1-1982.pdf` | FIPS PUB 32-1 announcement (NBS, 1982), superseding notice and printed character-set charts. | nvlpubs.nist.gov | U.S. Government work, public domain |
| `fips32-1-1975-ocrb.pdf` | FIPS PUB 32-1, *Character Set OCR-B* (NBS, 1975). Adopts ANSI X3.49-1975. Contains the complete reference drawing set (gridded RDN drawings) for the OCR-B characters. | govinfo.gov | U.S. Government work, public domain |
| `ecma-11.pdf` | ECMA-11, *Alphanumeric Character Set OCR-B for Optical Recognition*, 3rd edition. Supplies the dimensional tables and specification text that pair with the FIPS 32-1 drawings. | ecma-international.org | ECMA standards are freely available; text may be copied provided the source is acknowledged |

## How the drawings are used

The FIPS drawings dimension each character by its stroke centerline as fractions of the nominal
centerline width `W`, centerline height `H`, and stroke width `T` (OCR-A Size I: `W` = 0.055 in,
`H` = 0.094 in, `T` = 0.014 in). Round terminals end on the dimension line and the ink overshoots by
`T/2`; square terminals cut off exactly on it. `OcrAGlyphs` records those centerlines in design units of
0.0001 in (so `W` = 550, `H` = 940, `T` = 140 with 1000 units per em at the 10 characters-per-inch
pitch), and the generator strokes them with the library's own path stroker before writing the TrueType
tables.
