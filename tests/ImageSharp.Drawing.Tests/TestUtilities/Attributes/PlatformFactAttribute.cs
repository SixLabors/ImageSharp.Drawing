// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Runtime.InteropServices;

namespace SixLabors.ImageSharp.Drawing.Tests.TestUtilities.Attributes;

public class PlatformFactAttribute : FactAttribute
{
    public PlatformFactAttribute(OSPlatform platform)
    {
        if (!RuntimeInformation.IsOSPlatform(platform))
        {
            this.Skip = $"Platform specific test, runs only on '{platform}'";
        }
    }
}

public class WindowsFactAttribute : PlatformFactAttribute
{
    public WindowsFactAttribute()
        : base(OSPlatform.Windows)
    {
    }
}
