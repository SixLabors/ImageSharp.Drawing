// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Runtime.InteropServices;

namespace SixLabors.ImageSharp.Drawing.Renderers.Avalonia;

/// <summary>
/// The Win32 entry point needed to identify the module that owns Avalonia's windows.
/// </summary>
internal static partial class Kernel32
{
    /// <summary>
    /// Gets the handle of a loaded module. Pass <see langword="null"/> for the process executable,
    /// which is the module Avalonia registers its window classes against.
    /// </summary>
    /// <param name="moduleName">The module name, or <see langword="null"/> for the process executable.</param>
    /// <returns>The module handle (<c>HINSTANCE</c>).</returns>
    [LibraryImport("kernel32.dll", EntryPoint = "GetModuleHandleW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial nint GetModuleHandle(string? moduleName);
}
