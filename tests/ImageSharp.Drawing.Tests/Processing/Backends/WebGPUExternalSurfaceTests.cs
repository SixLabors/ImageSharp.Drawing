// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Runtime.InteropServices;
using SixLabors.ImageSharp.Drawing.Processing.Backends;
using SixLabors.ImageSharp.Drawing.Tests.TestUtilities;
using SixLabors.ImageSharp.Drawing.Tests.TestUtilities.Attributes;

namespace SixLabors.ImageSharp.Drawing.Tests.Processing.Backends;

public static partial class WebGPUExternalSurfaceTests
{
    [WebGPUFact]
    public static void Win32HostCreatesNativePresentationSurface()
    {
        if (!TestEnvironment.IsWindows)
        {
            return;
        }

        nint module = GetModuleHandle(null);

        // A hidden window exercises wgpuInstanceCreateSurface and surface configuration without
        // opening UI or requiring interaction from the test runner.
        nint window = CreateWindowEx(0, "STATIC", null, 0, 0, 0, 16, 16, 0, 0, module, 0);
        Assert.NotEqual(nint.Zero, window);

        try
        {
            using WebGPUExternalSurface surface = new(WebGPUSurfaceHost.Win32(window, module), new Size(16, 16));

            // Hosts report a zero drawable size while minimized. Acquisition must pause without
            // touching the still-configured native surface until a later nonzero resize.
            surface.Resize(Size.Empty);
            Assert.False(surface.TryAcquireFrame(out _));
        }
        finally
        {
            DestroyWindow(window);
        }
    }

    [LibraryImport("kernel32", EntryPoint = "GetModuleHandleW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint GetModuleHandle(string? moduleName);

    [LibraryImport("user32", EntryPoint = "CreateWindowExW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint CreateWindowEx(uint extendedStyle, string className, string? windowName, uint style, int x, int y, int width, int height, nint parent, nint menu, nint instance, nint parameter);

    [LibraryImport("user32")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyWindow(nint window);
}
