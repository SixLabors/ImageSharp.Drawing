namespace SixLabors.ImageSharp.Drawing.Tests.TestUtilities.ImageComparison
{
    using System;

    public class ImagesSimilarityException : Exception
    {
        public ImagesSimilarityException(string message)
            : base(message)
        {
        }
    }
}