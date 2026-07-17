// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Runtime.InteropServices;

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// Attaches a Core Animation metal layer to a Cocoa window for WebGPU presentation.
/// </summary>
internal static partial class MacOSMetalLayerFactory
{
    /// <summary>
    /// The Objective-C runtime library used to construct and attach the Core Animation layer.
    /// </summary>
    private const string ObjectiveCLibrary = "/usr/lib/libobjc.A.dylib";

    /// <summary>
    /// Creates and attaches a metal layer to a Cocoa window.
    /// </summary>
    /// <param name="nsWindow">The Cocoa <c>NSWindow*</c>.</param>
    /// <returns>The attached <c>CAMetalLayer*</c>.</returns>
    /// <remarks>
    /// The returned layer carries the ownership acquired by <c>alloc/init</c>. Call <see cref="Release"/> after
    /// wgpu-native has created its surface; the AppKit content view retains the attached layer independently.
    /// </remarks>
    public static nint Create(nint nsWindow)
    {
        nint metalLayerClass = GetClass("CAMetalLayer");
        nint metalLayer = SendPointer(SendPointer(metalLayerClass, RegisterSelector("alloc")), RegisterSelector("init"));
        nint contentView = SendPointer(nsWindow, RegisterSelector("contentView"));

        // WebGPU presents through a CAMetalLayer. AppKit creates a default backing layer only
        // after wantsLayer is enabled, so install the explicitly-created metal layer afterwards.
        SendByte(contentView, RegisterSelector("setWantsLayer:"), 1);
        SendPointer(contentView, RegisterSelector("setLayer:"), metalLayer);
        return metalLayer;
    }

    /// <summary>
    /// Releases the factory's ownership of a metal layer after its AppKit view has retained it.
    /// </summary>
    /// <param name="metalLayer">The <c>CAMetalLayer*</c> to release.</param>
    public static void Release(nint metalLayer)
        => SendVoid(metalLayer, RegisterSelector("release"));

    /// <summary>
    /// Resolves an Objective-C class by name.
    /// </summary>
    /// <param name="name">The Objective-C class name.</param>
    /// <returns>The Objective-C class object.</returns>
    [LibraryImport(ObjectiveCLibrary, EntryPoint = "objc_getClass", StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint GetClass(string name);

    /// <summary>
    /// Registers an Objective-C selector by name.
    /// </summary>
    /// <param name="name">The selector name.</param>
    /// <returns>The registered selector.</returns>
    [LibraryImport(ObjectiveCLibrary, EntryPoint = "sel_registerName", StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint RegisterSelector(string name);

    /// <summary>
    /// Sends a parameterless Objective-C message that returns an object pointer.
    /// </summary>
    /// <param name="receiver">The receiving object or class.</param>
    /// <param name="selector">The selector to invoke.</param>
    /// <returns>The returned object pointer.</returns>
    [LibraryImport(ObjectiveCLibrary, EntryPoint = "objc_msgSend")]
    private static partial nint SendPointer(nint receiver, nint selector);

    /// <summary>
    /// Sends an Objective-C message with one object-pointer argument and no return value.
    /// </summary>
    /// <param name="receiver">The receiving object.</param>
    /// <param name="selector">The selector to invoke.</param>
    /// <param name="value">The object pointer passed to the selector.</param>
    [LibraryImport(ObjectiveCLibrary, EntryPoint = "objc_msgSend")]
    private static partial void SendPointer(nint receiver, nint selector, nint value);

    /// <summary>
    /// Sends an Objective-C message with one Boolean-sized argument and no return value.
    /// </summary>
    /// <param name="receiver">The receiving object.</param>
    /// <param name="selector">The selector to invoke.</param>
    /// <param name="value">The Objective-C <c>BOOL</c> value passed to the selector.</param>
    [LibraryImport(ObjectiveCLibrary, EntryPoint = "objc_msgSend")]
    private static partial void SendByte(nint receiver, nint selector, byte value);

    /// <summary>
    /// Sends a parameterless Objective-C message with no return value.
    /// </summary>
    /// <param name="receiver">The receiving object.</param>
    /// <param name="selector">The selector to invoke.</param>
    [LibraryImport(ObjectiveCLibrary, EntryPoint = "objc_msgSend")]
    private static partial void SendVoid(nint receiver, nint selector);
}
