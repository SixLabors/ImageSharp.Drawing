# Draw Shapes With ImageSharp

This sample generates four composed scenes using the `DrawingCanvas` API. Each scene renders to an `Image<Rgba32>` and writes a PNG file under the sample's artifacts output directory.

## Samples

### `01-poster-composition.png`
Poster-style landscape that exercises the building blocks:

- `LinearGradientBrush` and `RadialGradientBrush` for the sky, lake, and sun.
- `PathBuilder` + `CloseFigure` for the mountain ridges, lake, and shoreline.
- `canvas.Save(options, IPath)` with `BooleanOperation.Intersection` for the lake highlight clip.
- `canvas.Save` + `canvas.SaveLayer` to apply a Z rotation and then composite the title panel and text together with `GraphicsOptions.BlendPercentage`.
- `TextMeasurer.MeasureRenderableBounds` to size the title panel to the laid-out text.

### `02-transit-map.png`
Transit-map style scene focusing on stroke styling and labels:

- A reference grid drawn with `Pens.Solid` plus a translucent colour.
- Three route lines using `SolidPen` with `StrokeOptions { LineCap = Round, LineJoin = Round }` so corners and ends stay smooth across multi-segment polylines.
- `Pens.Dash` for a walking-transfer line.
- Station markers as filled-then-stroked `EllipsePolygon` instances.
- One-off labels with `RichTextOptions { Origin = ... }`.

### `03-typography.png`
Typography sheet covering the rich-text surface:

- A header that mixes a `headlineFont` and a `bodyFont` in one paragraph by way of `RichTextRun`.
- A "Rich runs" panel where each run targets a grapheme range computed dynamically with `string.GetGraphemeCount()` so the runs survive copy edits without re-numbering.
- A "Wrapping and measurement" panel that wraps text with `WrappingLength` and uses `TextMeasurer.MeasureRenderableBounds` (advance ∪ ink) to size a callout that never clips.
- A "Bidi and fallback" panel mixing Latin, Arabic, and Hebrew with `FallbackFontFamilies = [ArabicFontFamily]`.
- A "Vertical mixed layout" panel using `LayoutMode.VerticalMixedRightLeft` for traditional Korean vertical typography with sideways Latin and digit runs.
- Curve text drawn with the explicit `canvas.DrawText(options, text, bezier, brush, pen)` path overload and `WrappingLength = bezier.ComputeLength()`.

### `04-image-processing-mask.png`
Image-compositing scene demonstrating four ways a photograph (`tests/Images/Input/Jpg/baseline/Lake.jpg`, linked into this project via `Content Include`) interacts with an `IPath`:

- **Before / after wipe** — `canvas.Apply(rightHalfRect, ctx => ctx.OilPaint(15, 5))` scopes an `OilPaint` processor to the right half of the photograph.
- **Privacy redaction** — `canvas.Apply(ellipse, ctx => ctx.Pixelate(10))` pixelates an elliptical face-shaped region and leaves the rest untouched.
- **Image as a brush** — `new ImageBrush<Rgba32>(source, source.Bounds, brushOffset)` wraps the photograph as a `Brush` so a `Star` path can be filled with it as a texture; the brush offset aligns the mountain in the photograph with the star's centre.
- **Photo in text** — `TextBuilder.GeneratePaths("MASK", ...)` produces one `IPath` per glyph; `canvas.Save(intersectionOptions, glyphPaths)` uses them as a compound clip so `DrawImage` only renders inside the letterforms.

## Running

```bash
dotnet run --project samples/DrawShapesWithImageSharp -c Release
```

Output PNGs are written to `artifacts/bin/samples/DrawShapesWithImageSharp/<configuration>/<target-framework>/Output/`.

## Fonts and image assets

The csproj links four fonts and one JPEG from the test fixtures:

| Asset | Source | Used by |
| --- | --- | --- |
| `OpenSans-Regular.ttf` | `tests/ImageSharp.Drawing.Tests/TestFonts/` | text bodies, transit labels |
| `WendyOne-Regular.ttf` | `tests/ImageSharp.Drawing.Tests/TestFonts/` | display headings, glyph mask |
| `me_quran_volt_newmet.ttf` | `tests/ImageSharp.Drawing.Tests/TestFonts/` | Arabic fallback in the bidi panel |
| `NotoSansKR-Regular.otf` | `tests/ImageSharp.Drawing.Tests/TestFonts/` | Korean vertical-layout panel |
| `Lake.jpg` | `tests/Images/Input/Jpg/baseline/` | masking sample source photograph |
