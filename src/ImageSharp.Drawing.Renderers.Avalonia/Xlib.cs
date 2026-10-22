// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Runtime.InteropServices;

namespace SixLabors.ImageSharp.Drawing.Renderers.Avalonia;

/// <summary>
/// The Xlib entry points needed to open a connection to the X server that Avalonia's window lives on.
/// Avalonia keeps its own <c>Display*</c> internal, and window ids are valid across connections, so the
/// renderer opens a second connection and pairs it with the window id Avalonia exposes.
/// </summary>
internal static partial class Xlib
{
    /// <summary>
    /// Opens a connection to the X server. Pass <see langword="null"/> to use the <c>DISPLAY</c> environment variable.
    /// </summary>
    /// <param name="displayName">The display name, or <see langword="null"/> for the default display.</param>
    /// <returns>The <c>Display*</c>, or <see cref="nint.Zero"/> when no connection could be made.</returns>
    [LibraryImport("libX11.so.6", StringMarshalling = StringMarshalling.Utf8)]
    public static partial nint XOpenDisplay(string? displayName);

    /// <summary>
    /// Closes a connection opened by <see cref="XOpenDisplay"/>.
    /// </summary>
    /// <param name="display">The <c>Display*</c> to close.</param>
    /// <returns>Zero on success.</returns>
    [LibraryImport("libX11.so.6")]
    public static partial int XCloseDisplay(nint display);
}
