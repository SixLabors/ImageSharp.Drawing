using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
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
        public WindowsFactAttribute() : base(OSPlatform.Windows)
        {
        }
    }
}
