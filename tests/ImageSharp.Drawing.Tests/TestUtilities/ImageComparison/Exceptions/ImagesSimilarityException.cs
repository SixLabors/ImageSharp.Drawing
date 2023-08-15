// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Tests.TestUtilities.ImageComparison;

public class ImagesSimilarityException : Exception
{
    public ImagesSimilarityException(string message)
        : base(message)
    {
    }
}
