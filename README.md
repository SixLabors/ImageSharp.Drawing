<h1 align="center">

<img src="https://raw.githubusercontent.com/SixLabors/Branding/master/icons/imagesharp.drawing/sixlabors.imagesharp.drawing.512.png" alt="SixLabors.ImageSharp.Drawing" width="256"/>
<br/>
SixLabors.ImageSharp.Drawing
</h1>


<div align="center">

[![Build Status](https://img.shields.io/github/workflow/status/SixLabors/ImageSharp.Drawing/Build/master)](https://github.com/SixLabors/ImageSharp.Drawing/actions)
[![License: Apache 2.0](https://img.shields.io/badge/license-Apache%202.0-blue.svg)](https://opensource.org/licenses/Apache-2.0)
[![Code coverage](https://codecov.io/gh/SixLabors/ImageSharp.Drawing/branch/master/graph/badge.svg)](https://codecov.io/gh/SixLabors/ImageSharp.Drawing)
[![Twitter](https://img.shields.io/twitter/url/http/shields.io.svg?style=flat&logo=twitter)](https://twitter.com/intent/tweet?hashtags=imagesharp,dotnet,oss&text=ImageSharp.+A+new+cross-platform+2D+graphics+API+in+C%23&url=https%3a%2f%2fgithub.com%2fSixLabors%2fImageSharp&via=sixlabors)

</div>

### **ImageSharp.Drawing** provides extensions to ImageSharp containing a cross-platform 2D polygon manipulation API and drawing operations.

Designed to democratize image processing, ImageSharp.Drawing brings you an incredibly powerful yet beautifully simple API.

Built against .NET Standard 1.3, ImageSharp.Drawing can be used in device, cloud, and embedded/IoT scenarios. 
  
Features Include:  
- Point in Polygon
- Line Intersections
- Complex Polygons
- Simple polygon clipping
- Regular Polygons (triangles, squares, pentagons etc, any number of sides)
- Ellipses (and therfore circles)
- Shape Builder api - for creating shapes declaratively 
- Polygons
   - With Linear line segments
   - With Beziear curve line segments
   - Mixture of both Linear & bezier segments
- Paths
   - With Linear line segments
   - With Bezier curve line segments
   - Mixture of both Linear & bezier segments
- Drawing
  - Brushes and various drawing algorithms, including drawing images
  - Various vector drawing methods for drawing paths, polygons etc.
  - Text drawing  

## License
  
- ImageSharp.Drawing is licensed under the [Apache License, Version 2.0](https://opensource.org/licenses/Apache-2.0)  
- An alternative Commercial License can be purchased for projects and applications requiring support.
Please visit https://sixlabors.com/pricing for details.

## Documentation

- [Detailed documentation](https://sixlabors.github.io/docs/) for the ImageSharp.Drawing API is available. This includes additional conceptual documentation to help you get started.
- Our [Samples Repository](https://github.com/SixLabors/Samples/tree/master/ImageSharp) is also available containing buildable code samples demonstrating common activities.

## Questions?

- Do you have questions? We are happy to help! Please [join our Discussions Forum](https://github.com/SixLabors/ImageSharp.Drawing/discussions/category_choices), or ask them on [stackoverflow](https://stackoverflow.com) using the `ImageSharp` tag. **Do not** open issues for questions!
- Please read our [Contribution Guide](https://github.com/SixLabors/ImageSharp.Drawing/blob/master/.github/CONTRIBUTING.md) before opening issues or pull requests!

## Code of Conduct  
This project has adopted the code of conduct defined by the [Contributor Covenant](https://contributor-covenant.org/) to clarify expected behavior in our community.
For more information, see the [.NET Foundation Code of Conduct](https://dotnetfoundation.org/code-of-conduct).

## Installation

Install stable releases via Nuget; development releases are available via MyGet.

| Package Name                   | Release (NuGet) | Nightly (MyGet) |
|--------------------------------|-----------------|-----------------|
| `SixLabors.ImageSharp.Drawing` | [![NuGet](https://img.shields.io/nuget/v/SixLabors.ImageSharp.Drawing.svg)](https://www.nuget.org/packages/SixLabors.ImageSharp.Drawing/) | [![MyGet](https://img.shields.io/myget/sixlabors/v/SixLabors.ImageSharp.Drawing.svg)](https://www.myget.org/feed/sixlabors/package/nuget/SixLabors.ImageSharp.Drawing) |

## Manual build

If you prefer, you can compile ImageSharp.Drawing yourself (please do and help!)

- Using [Visual Studio 2019](https://visualstudio.microsoft.com/vs/)
  - Make sure you have the latest version installed
  - Make sure you have [the .NET Core 3.1 SDK](https://www.microsoft.com/net/core#windows) installed

Alternatively, you can work from command line and/or with a lightweight editor on **both Linux/Unix and Windows**:

- [Visual Studio Code](https://code.visualstudio.com/) with [C# Extension](https://marketplace.visualstudio.com/items?itemName=ms-vscode.csharp)
- [.NET Core](https://www.microsoft.com/net/core#linuxubuntu)

- [Visual Studio Code](https://code.visualstudio.com/) with [C# Extension](https://marketplace.visualstudio.com/items?itemName=ms-vscode.csharp)
- [.NET Core](https://www.microsoft.com/net/core#linuxubuntu)

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

Please... Spread the word, contribute algorithms, submit performance improvements, unit tests, no input is too little. Make sure to read our [Contribution Guide](https://github.com/SixLabors/ImageSharp.Drawing/blob/master/.github/CONTRIBUTING.md) before opening a PR.

## The ImageSharp Team

- [Scott Williams](https://github.com/tocsoft)
- [James Jackson-South](https://github.com/jimbobsquarepants)
- [Dirk Lemstra](https://github.com/dlemstra)
- [Anton Firsov](https://github.com/antonfirsov)
- [Brian Popow](https://github.com/brianpopow)

## Sponsor Six Labors

Support the efforts of the development of the Six Labors projects. [[Become a sponsor :heart:](https://opencollective.com/sixlabors#sponsor)]

### Platinum Sponsors
Become a platinum sponsor with a monthly donation of $2000 (providing 32 hours of maintenance and development) and get 2 hours of dedicated support (remote support available through chat or screen-sharing) per month.

In addition you get your logo (large) on our README on GitHub and the home page (large) of sixlabors.com

<a href="https://opencollective.com/sixlabors/tiers/platinum-sponsors/0/website" target="_blank"><img src="https://opencollective.com/sixlabors/tiers/platinum-sponsors/0/avatar.svg?avatarHeight=192"></a>

### Gold Sponsors
Become a gold sponsor with a monthly donation of $1000 (providing 16 hours of maintenance and development) and get 1 hour of dedicated support (remote support available through chat or screen-sharing) per month.

In addition you get your logo (large) on our README on GitHub and the home page (medium) of sixlabors.com

<a href="https://opencollective.com/sixlabors/tiers/gold-sponsors/0/website" target="_blank"><img src="https://opencollective.com/sixlabors/tiers/gold-sponsors/0/avatar.svg?avatarHeight=156"></a><a href="https://opencollective.com/sixlabors/tiers/gold-sponsors/1/website" target="_blank"><img src="https://opencollective.com/sixlabors/tiers/gold-sponsors/1/avatar.svg?avatarHeight=156"></a><a href="https://opencollective.com/sixlabors/tiers/gold-sponsors/2/website" target="_blank"><img src="https://opencollective.com/sixlabors/tiers/gold-sponsors/2/avatar.svg?avatarHeight=156"></a><a href="https://opencollective.com/sixlabors/tiers/gold-sponsors/3/website" target="_blank"><img src="https://opencollective.com/sixlabors/tiers/gold-sponsors/3/avatar.svg?avatarHeight=156"></a><a href="https://opencollective.com/sixlabors/tiers/gold-sponsors/4/website" target="_blank"><img src="https://opencollective.com/sixlabors/tiers/gold-sponsors/4/avatar.svg?avatarHeight=156"></a><a href="https://opencollective.com/sixlabors/tiers/gold-sponsors/5/website" target="_blank"><img src="https://opencollective.com/sixlabors/tiers/gold-sponsors/5/avatar.svg?avatarHeight=156"></a><a href="https://opencollective.com/sixlabors/tiers/gold-sponsors/6/website" target="_blank"><img src="https://opencollective.com/sixlabors/tiers/gold-sponsors/6/avatar.svg?avatarHeight=156"></a><a href="https://opencollective.com/sixlabors/tiers/gold-sponsors/7/website" target="_blank"><img src="https://opencollective.com/sixlabors/tiers/gold-sponsors/7/avatar.svg?avatarHeight=156"></a><a href="https://opencollective.com/sixlabors/tiers/gold-sponsors/8/website" target="_blank"><img src="https://opencollective.com/sixlabors/tiers/gold-sponsors/8/avatar.svg?avatarHeight=156"></a><a href="https://opencollective.com/sixlabors/tiers/gold-sponsors/9/website" target="_blank"><img src="https://opencollective.com/sixlabors/tiers/gold-sponsors/9/avatar.svg?avatarHeight=156"></a><a href="https://opencollective.com/sixlabors/tiers/gold-sponsors/10/website" target="_blank"><img src="https://opencollective.com/sixlabors/tiers/gold-sponsors/10/avatar.svg?avatarHeight=156"></a>

### Silver Sponsors
Become a silver sponsor with a monthly donation of $500 (providing 8 hours of maintenance and development) and get your logo (medium) on our README on GitHub and the product pages of sixlabors.com

<a href="https://opencollective.com/sixlabors/tiers/silver-sponsors/0/website" target="_blank"><img src="https://opencollective.com/sixlabors/tiers/silver-sponsors/0/avatar.svg?avatarHeight=128"></a><a href="https://opencollective.com/sixlabors/tiers/silver-sponsors/1/website" target="_blank"><img src="https://opencollective.com/sixlabors/tiers/silver-sponsors/1/avatar.svg?avatarHeight=128"></a><a href="https://opencollective.com/sixlabors/tiers/silver-sponsors/2/website" target="_blank"><img src="https://opencollective.com/sixlabors/tiers/silver-sponsors/2/avatar.svg?avatarHeight=128"></a><a href="https://opencollective.com/sixlabors/tiers/silver-sponsors/3/website" target="_blank"><img src="https://opencollective.com/sixlabors/tiers/silver-sponsors/3/avatar.svg?avatarHeight=128"></a><a href="https://opencollective.com/sixlabors/tiers/silver-sponsors/4/website" target="_blank"><img src="https://opencollective.com/sixlabors/tiers/silver-sponsors/4/avatar.svg?avatarHeight=128"></a><a href="https://opencollective.com/sixlabors/tiers/silver-sponsors/5/website" target="_blank"><img src="https://opencollective.com/sixlabors/tiers/silver-sponsors/5/avatar.svg?avatarHeight=128"></a><a href="https://opencollective.com/sixlabors/tiers/silver-sponsors/6/website" target="_blank"><img src="https://opencollective.com/sixlabors/tiers/silver-sponsors/6/avatar.svg?avatarHeight=128"></a><a href="https://opencollective.com/sixlabors/tiers/silver-sponsors/7/website" target="_blank"><img src="https://opencollective.com/sixlabors/tiers/silver-sponsors/7/avatar.svg?avatarHeight=128"></a><a href="https://opencollective.com/sixlabors/tiers/silver-sponsors/8/website" target="_blank"><img src="https://opencollective.com/sixlabors/tiers/silver-sponsors/8/avatar.svg?avatarHeight=128"></a><a href="https://opencollective.com/sixlabors/tiers/silver-sponsors/9/website" target="_blank"><img src="https://opencollective.com/sixlabors/tiers/silver-sponsors/9/avatar.svg?avatarHeight=128"></a><a href="https://opencollective.com/sixlabors/tiers/silver-sponsors/10/website" target="_blank"><img src="https://opencollective.com/sixlabors/tiers/silver-sponsors/10/avatar.svg?avatarHeight=128"></a>

### Bronze Sponsors
Become a bronze sponsor with a monthly donation of $100 and get your logo (small) on our README on GitHub.

<a href="https://opencollective.com/sixlabors/tiers/bronze-sponsors/0/website" target="_blank"><img src="https://opencollective.com/sixlabors/tiers/bronze-sponsors/0/avatar.svg?avatarHeight=96"></a>
<a href="https://opencollective.com/sixlabors/tiers/bronze-sponsors/1/website" target="_blank"><img src="https://opencollective.com/sixlabors/tiers/bronze-sponsors/1/avatar.svg?avatarHeight=96"></a>
<a href="https://opencollective.com/sixlabors/tiers/bronze-sponsors/2/website" target="_blank"><img src="https://opencollective.com/sixlabors/tiers/bronze-sponsors/2/avatar.svg?avatarHeight=96"></a>
<a href="https://opencollective.com/sixlabors/tiers/bronze-sponsors/3/website" target="_blank"><img src="https://opencollective.com/sixlabors/tiers/bronze-sponsors/3/avatar.svg?avatarHeight=96"></a>
<a href="https://opencollective.com/sixlabors/tiers/bronze-sponsors/4/website" target="_blank"><img src="https://opencollective.com/sixlabors/tiers/bronze-sponsors/4/avatar.svg?avatarHeight=96"></a>
<a href="https://opencollective.com/sixlabors/tiers/bronze-sponsors/5/website" target="_blank"><img src="https://opencollective.com/sixlabors/tiers/bronze-sponsors/5/avatar.svg?avatarHeight=96"></a>
<a href="https://opencollective.com/sixlabors/tiers/bronze-sponsors/6/website" target="_blank"><img src="https://opencollective.com/sixlabors/tiers/bronze-sponsors/6/avatar.svg?avatarHeight=96"></a>
<a href="https://opencollective.com/sixlabors/tiers/bronze-sponsors/7/website" target="_blank"><img src="https://opencollective.com/sixlabors/tiers/bronze-sponsors/7/avatar.svg?avatarHeight=96"></a>
<a href="https://opencollective.com/sixlabors/tiers/bronze-sponsors/8/website" target="_blank"><img src="https://opencollective.com/sixlabors/tiers/bronze-sponsors/8/avatar.svg?avatarHeight=96"></a>
<a href="https://opencollective.com/sixlabors/tiers/bronze-sponsors/9/website" target="_blank"><img src="https://opencollective.com/sixlabors/tiers/bronze-sponsors/9/avatar.svg?avatarHeight=96"></a>
<a href="https://opencollective.com/sixlabors/tiers/bronze-sponsors/10/website" target="_blank"><img src="https://opencollective.com/sixlabors/tiers/bronze-sponsors/10/avatar.svg?avatarHeight=96"></a>
