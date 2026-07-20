// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Reflection;
using System.Runtime.InteropServices;

namespace SixLabors.ImageSharp.Drawing.Processing.Backends.Native;

/// <summary>
/// Selects the native library used by the generated WebGPU bindings.
/// </summary>
internal static partial class WebGPUNative
{
    private const string LibraryName = "wgpu_native";
    private static readonly nint LibraryHandle;

    static WebGPUNative()
    {
        string fileName = OperatingSystem.IsWindows()
            ? "wgpu_native.dll"
            : OperatingSystem.IsMacOS()
                ? "libwgpu_native.dylib"
                : "libwgpu_native.so";

        string libraryPath = System.IO.Path.Combine(AppContext.BaseDirectory, fileName);
        if (File.Exists(libraryPath))
        {
            // Load the binary shipped with this package before .NET searches RID-native directories.
            // This prevents an older same-named dependency from satisfying our generated v29 ABI.
            LibraryHandle = NativeLibrary.Load(libraryPath);
        }

        NativeLibrary.SetDllImportResolver(typeof(WebGPUNative).Assembly, ResolveLibrary);
    }

    private static nint ResolveLibrary(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
        => libraryName == LibraryName ? LibraryHandle : nint.Zero;
}
