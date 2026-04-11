// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Formats;
using IOPath = System.IO.Path;

namespace SixLabors.ImageSharp.Drawing.Tests;

public static partial class TestEnvironment
{
    internal static Configuration Configuration => Configuration.Default;

    internal static IImageDecoder GetReferenceDecoder(string filePath)
    {
        IImageFormat format = GetImageFormat(filePath);
        return Configuration.ImageFormatsManager.GetDecoder(format);
    }

    internal static IImageEncoder GetReferenceEncoder(string filePath)
    {
        IImageFormat format = GetImageFormat(filePath);
        return Configuration.ImageFormatsManager.GetEncoder(format);
    }

    internal static IImageFormat GetImageFormat(string filePath)
    {
        string extension = IOPath.GetExtension(filePath);

        Configuration.ImageFormatsManager.TryFindFormatByFileExtension(extension, out IImageFormat format);

        return format;
    }
}
