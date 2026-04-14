# Draw Shapes With ImageSharp

A sample application that demonstrates the core vector drawing capabilities of ImageSharp.Drawing. Each example renders shapes to an `Image<Rgba32>` using the `DrawingCanvas` API and saves the result as a PNG file in the `Output/` directory.

## What it demonstrates

- **Stars** — Filled and outlined star polygons with varying point counts, inner/outer radii, line join styles (miter, round, bevel), and dashed outlines with different line caps.
- **Clipping** — Rectangle-on-rectangle clipping using `IPath.Clip()`.
- **Path building** — Constructing complex shapes with `PathBuilder`, including multi-figure paths (a V overlaid with a rectangle) and an hourglass shape.
- **Curves** — Ellipses via `EllipsePolygon` and cubic Bezier arcs via `CubicBezierLineSegment`.
- **Text as paths** — Converting text to vector outlines using `TextBuilder.GeneratePaths()` with system fonts, including text laid out along a curved path.
- **Serialized glyph data** — Rendering OpenSans letter shapes ('a' and 'o') from serialized coordinate data as `ComplexPolygon` instances.
- **Canvas API** — `Fill` for solid backgrounds and shape rendering via `Paint`.

## Running

```bash
dotnet run --project samples/DrawShapesWithImageSharp -c Debug
```

Output images are written to the `Output/` directory, organized into subdirectories by category: `Stars/`, `Clipping/`, `Curves/`, `Text/`, `Drawing/`, `Letter/`, and `Issues/`.
