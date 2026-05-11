<h1 align="center">

<img src="https://raw.githubusercontent.com/SixLabors/Branding/main/icons/imagesharp.drawing/sixlabors.imagesharp.drawing.512.png" alt="SixLabors.ImageSharp.Drawing" width="256"/>
<br/>
SixLabors.ImageSharp.Drawing
</h1>


<div align="center">

[![Build Status](https://img.shields.io/github/actions/workflow/status/SixLabors/ImageSharp.Drawing/build-and-test.yml?branch=main)](https://github.com/SixLabors/ImageSharp.Drawing/actions)
[![Code coverage](https://codecov.io/gh/SixLabors/ImageSharp.Drawing/branch/main/graph/badge.svg)](https://codecov.io/gh/SixLabors/ImageSharp.Drawing)
[![License: Six Labors Split](https://img.shields.io/badge/license-Six%20Labors%20Split-%23e30183)](https://github.com/SixLabors/ImageSharp.Drawing/blob/main/LICENSE)

</div>

**ImageSharp.Drawing** is a cross-platform 2D drawing library built on top of [ImageSharp](https://github.com/SixLabors/ImageSharp). It adds a rich vector drawing model for composing raster images, rendering text, shaping paths, masking image-processing operations, and targeting CPU or WebGPU-backed drawing surfaces from the same `DrawingCanvas` API.

The core package targets .NET 8 and provides the default CPU backend. The optional `SixLabors.ImageSharp.Drawing.WebGPU` package adds GPU-backed rendering for native windows, external surfaces, and offscreen render targets.

## Capabilities

- Draw and fill paths, lines, arcs, ellipses, pies, rectangles, rounded rectangles, regular polygons, stars, and arbitrary `PathBuilder` geometry.
- Use solid, pattern, image, recolor, linear gradient, radial gradient, elliptic gradient, sweep gradient, and path gradient brushes.
- Stroke paths and polylines with configurable width, caps, joins, dash patterns, and stroke options.
- Render text with `SixLabors.Fonts`, including rich text runs, fallback fonts, bidirectional text, vertical layout, glyph paths, text measurement, wrapped text, and text-on-path scenarios.
- Compose with transforms, clipping, save/restore state, isolated layers, blend options, opacity, and region canvases.
- Use paths as masks for ImageSharp processors with `canvas.Apply(...)`, or fill paths with images via `ImageBrush`.
- Create retained drawing scenes and render them repeatedly to compatible targets.
- Render into `Image<TPixel>` memory with the CPU backend, or into WebGPU windows, external host surfaces, and offscreen render targets with the WebGPU backend.

## Quick Start

Draw into an `Image<TPixel>` with the CPU backend:

```csharp
image.Mutate(ctx => ctx.Paint(canvas =>
{
    // A fill without geometry paints the entire canvas.
    canvas.Fill(Brushes.Solid(Color.White));

    // Brushes can be reused across paths or used directly for full-canvas fills.
    canvas.Fill(new LinearGradientBrush(
        new PointF(0, 0),
        new PointF(400, 300),
        GradientRepetitionMode.None,
        new ColorStop(0F, Color.CornflowerBlue),
        new ColorStop(1F, Color.MediumSeaGreen)));

    // Built-in polygon types are regular IPath instances accepted by Fill and Draw.
    canvas.Fill(Brushes.Solid(Color.HotPink), new EllipsePolygon(200, 200, 100));
    canvas.Draw(Pens.Solid(Color.Navy, 3F), new RoundedRectanglePolygon(50, 50, 200, 100, 16));
}));
```

Draw into a native WebGPU window with the same canvas-facing API:

```csharp
using WebGPUWindow window = new(new WebGPUWindowOptions
{
    Title = "ImageSharp.Drawing",
    Size = new Size(800, 600),
    Format = WebGPUTextureFormat.Bgra8Unorm,
    PresentMode = WebGPUPresentMode.Fifo,
});

window.Run(frame =>
{
    DrawingCanvas canvas = frame.Canvas;

    // WebGPU frames expose the same DrawingCanvas API as CPU image processing.
    canvas.Fill(Brushes.Solid(Color.Black));
    canvas.Fill(Brushes.Solid(Color.CornflowerBlue), new EllipsePolygon(400, 300, 120));
});
```

## License

- ImageSharp.Drawing is licensed under the [Six Labors Split License, Version 1.0](https://github.com/SixLabors/ImageSharp.Drawing/blob/main/LICENSE)


## Support Six Labors

Support the efforts of the development of the Six Labors projects.
 - [Purchase a Commercial License :heart:](https://sixlabors.com/pricing/)
 - [Become a sponsor via GitHub Sponsors :heart:]( https://github.com/sponsors/SixLabors)
 - [Become a sponsor via Open Collective :heart:](https://opencollective.com/sixlabors)

## Documentation

- [Detailed documentation](https://sixlabors.github.io/docs/) for the ImageSharp.Drawing API is available. This includes additional conceptual documentation to help you get started.
- Our [Samples Repository](https://github.com/SixLabors/Samples/tree/main/ImageSharp) is also available containing buildable code samples demonstrating common activities.

## Questions?

- Do you have questions? We are happy to help! Please [join our Discussions Forum](https://github.com/SixLabors/ImageSharp.Drawing/discussions/category_choices), or ask them on [stackoverflow](https://stackoverflow.com) using the `ImageSharp` tag. **Do not** open issues for questions!
- Please read our [Contribution Guide](https://github.com/SixLabors/ImageSharp.Drawing/blob/main/.github/CONTRIBUTING.md) before opening issues or pull requests!

## Code of Conduct
This project has adopted the code of conduct defined by the [Contributor Covenant](https://contributor-covenant.org/) to clarify expected behavior in our community.
For more information, see the [.NET Foundation Code of Conduct](https://dotnetfoundation.org/code-of-conduct).

## Installation

Install stable releases via NuGet; development releases are available via MyGet.

| Package Name                          | Release (NuGet) | Nightly (MyGet) |
|---------------------------------------|-----------------|-----------------|
| `SixLabors.ImageSharp.Drawing`        | [![NuGet](https://img.shields.io/nuget/v/SixLabors.ImageSharp.Drawing.svg)](https://www.nuget.org/packages/SixLabors.ImageSharp.Drawing/) | [![feedz.io](https://img.shields.io/badge/endpoint.svg?url=https%3A%2F%2Ff.feedz.io%2Fsixlabors%2Fsixlabors%2Fshield%2FSixLabors.ImageSharp.Drawing%2Flatest)](https://f.feedz.io/sixlabors/sixlabors/nuget/index.json) |
| `SixLabors.ImageSharp.Drawing.WebGPU` | [![NuGet](https://img.shields.io/nuget/v/SixLabors.ImageSharp.Drawing.WebGPU.svg)](https://www.nuget.org/packages/SixLabors.ImageSharp.Drawing.WebGPU/) | [![feedz.io](https://img.shields.io/badge/endpoint.svg?url=https%3A%2F%2Ff.feedz.io%2Fsixlabors%2Fsixlabors%2Fshield%2FSixLabors.ImageSharp.Drawing.WebGPU%2Flatest)](https://f.feedz.io/sixlabors/sixlabors/nuget/index.json) |

## Manual build

If you prefer, you can compile ImageSharp.Drawing yourself (please do and help!)

- Using [Visual Studio 2022](https://visualstudio.microsoft.com/vs/)
  - Make sure you have the latest version installed
  - Make sure you have [the .NET 8 SDK](https://www.microsoft.com/net/core#windows) installed

Alternatively, you can work from command line and/or with a lightweight editor on **both Linux/Unix and Windows**:

- [Visual Studio Code](https://code.visualstudio.com/) with [C# Extension](https://marketplace.visualstudio.com/items?itemName=ms-vscode.csharp)
- [the .NET 8 SDK](https://www.microsoft.com/net/core#linuxubuntu)

To clone ImageSharp.Drawing locally, click the "Clone in [YOUR_OS]" button above or run the following git commands:

```bash
git clone https://github.com/SixLabors/ImageSharp.Drawing
```

If working with Windows please ensure that you have enabled log file paths in git (run as Administrator).

```bash
git config --system core.longpaths true
```

### Submodules

This repository contains [git submodules](https://blog.github.com/2016-02-01-working-with-submodules/). To add the submodules to the project, navigate to the repository root and type:

``` bash
git submodule update --init --recursive
```

## How can you help?

Please... Spread the word, contribute algorithms, submit performance improvements, unit tests, no input is too little. Make sure to read our [Contribution Guide](https://github.com/SixLabors/ImageSharp.Drawing/blob/main/.github/CONTRIBUTING.md) before opening a PR.

## The ImageSharp.Drawing Team

- [Scott Williams](https://github.com/tocsoft)
- [James Jackson-South](https://github.com/jimbobsquarepants)
- [Dirk Lemstra](https://github.com/dlemstra)
- [Anton Firsov](https://github.com/antonfirsov)
- [Brian Popow](https://github.com/brianpopow)

---

<div>
  <a href="https://www.jetbrains.com/?from=ImageSharp.Drawing" align="right"><img src="https://resources.jetbrains.com/storage/products/company/brand/logos/jb_beam.svg" alt="JetBrains" class="logo-footer" width="72" align="left"></a>
  <br/>

  Special thanks to [JetBrains](https://www.jetbrains.com/?from=ImageSharp) for supporting us with open-source licenses for their IDEs.
</div>
