// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Tests;

public interface ITestImageProvider
{
    PixelTypes PixelType { get; }

    ImagingTestCaseUtility Utility { get; }

    string SourceFileOrDescription { get; }

    Configuration Configuration { get; set; }
}
