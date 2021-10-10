// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System.Runtime.InteropServices;
using Xunit;

namespace SixLabors.ImageSharp.Drawing.Tests.TestUtilities.Attributes
{
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
}
