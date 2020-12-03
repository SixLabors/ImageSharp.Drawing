// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;

namespace SixLabors.ImageSharp.Drawing.Tests.TestUtilities.ImageComparison
{
    public class ImagesSimilarityException : Exception
    {
        public ImagesSimilarityException(string message)
            : base(message)
        {
        }
    }
}
