# Font source documents

These are the specification documents the OCR-A and OCR-B glyphs in this generator are built from.
Both fonts are built clean-room from the standards themselves. No outline data from any other font is
read, embedded or shipped, and the glyphs are our own.

| File | Document | Source | Status |
| ---- | -------- | ------ | ------ |
| `fipspub32-1974.pdf` | FIPS PUB 32, *Character Set OCR-A* (NBS, 1974). Adopts ANSI X3.17-1974. Figures II-11 through II-96 are complete dimensioned centerline drawings of every character; Table II-3 gives the numeric dimension equivalents (W, H, T, r1-r4) per size. | nvlpubs.nist.gov | U.S. Government work, public domain |
| `fipspub32-1-1982.pdf` | FIPS PUB 32-1 announcement (NBS, 1982), superseding notice and printed character-set charts. | nvlpubs.nist.gov | U.S. Government work, public domain |
| `fips32-1-1975-ocrb.pdf` | FIPS PUB 32-1, *Character Set OCR-B* (NBS, 1975). Adopts ANSI X3.49-1975. Contains the reference drawing set for the OCR-B characters and the text describing them. | govinfo.gov | U.S. Government work, public domain |
| `ecma-11.pdf` | ECMA-11, *Alphanumeric Character Set OCR-B for Optical Recognition*, 3rd edition. Carries the printed 4:1 character sheet, the index table of all 121 reference numbers, and the dimension tables. | ecma-international.org | ECMA standards are freely available; text may be copied provided the source is acknowledged |

## The design grid

Both designs share one grid. One unit is 0.0001 inch, so the 10 characters-per-inch pitch of 0.1 inch
is 1000 units and becomes one em. The baseline is at zero and the cell centre is at x = 275, which is
half the nominal character width W of 550. The nominal centerline height H is 940 and the nominal
stroke width T is 140, matching Table II-3 of FIPS PUB 32 and the millimetre values of ECMA-11.

The generator scales the design onto the em by 0.72 before writing the font, which lands the cap ink
at the same size per em as other OCR faces render, so the 0.1 inch pitch prints at 10 points.

Text layout centres the em box inside the declared line height, so a glyph cell reaches
`ascender - (lineHeight - unitsPerEm) / 2` above the baseline. Ink above that line makes the layout
lower that one glyph to keep it inside its cell, which would break the shared baseline. The generator
therefore measures the real ink extremes and declares an ascender that the tallest ink fits inside.

## OCR-A

`OcrAGlyphs` records the stroke centerlines transcribed from the FIPS PUB 32 drawings. The drawings
dimension each character by its centerline as fractions of W, H and T, so ink projects half a stroke
past every dimension line: round terminals end on the line and overshoot by T/2, square terminals cut
off exactly on it. Labelled corner radii are outer ink radii, so the centerline radii recorded here
are those values less T/2. The generator strokes the centerlines with the library's own path stroker,
then cuts the open terminals flat on the axis-aligned planes the drawings show.

## OCR-B

`OcrBGlyphs` records filled ink outlines rather than centerlines, because ECMA-11 presents OCR-B as a
printed 4:1 character sheet rather than as dimensioned centerlines. Each outline is placed on its cell
of that sheet and squared to the dimensions the standard's tables give. The repertoire is the full
index table, references 1 to 120, less the four entries that draw nothing.

Four characters are exact primitives, because the standard dimensions them in millimetres instead of
drawing them. One millimetre is 393.7008 design units.

| Character | Reference | Source | Dimensions |
| --------- | --------- | ------ | ---------- |
| `U+007C` vertical line | 91 | Section 10 | height 3,20 mm, strokewidth 0,35 mm |
| `U+2223` pre-printed long vertical mark | 92 | Section 10 | minimum height 3,70 mm, strokewidth 0,35 mm, with the break the drawing shows |
| `U+2015` continuous underline | 116 | Section 8 and the character sheet | one pitch long so adjacent marks join, set below the baseline |
| `U+2588` character erase | 120 | Section 5.5 | width 1,9 mm, height 2,9 mm, 0,13 mm below the baseline |

A gap drawn in a printed specimen means the mark is cut short rather than that the ink has a hole,
except in the OCR-A long vertical mark of Figure II-24, where the break is real ink. Where a printed
specimen disagrees with the dimensions its own table gives, the table wins: the sheet draws these four
smaller than their stated sizes so they fit inside a character cell.

## Verification

`SpecChecks` runs on every build and is the regression gate. Each glyph asserts its exact ink bounding
box plus probe points that must be inside or outside the ink, which catches arc sweep and construction
errors a bounding box cannot see. The expectations were transcribed from the same documents as the
glyphs, so they cannot catch a misreading of the documents themselves.

Shape acceptance is visual. The generator writes `<Name>-proof.png`, a full set rendered by the
library from the built font, and `<Name>-grid.png`, one labelled cell per glyph for review against the
source drawings.
