// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Runtime.InteropServices;
using FilePath = System.IO.Path;

namespace SixLabors.ImageSharp.Drawing.Processing.Backends.Native;

/// <summary>
/// Locates this library's own loaded module on disk.
/// </summary>
/// <remarks>
/// <para>
/// Under Native AOT this code is compiled into a shared library or executable image, and when
/// that image is a plugin loaded by a foreign host, <see cref="AppContext.BaseDirectory"/>
/// points at the host process rather than the image. Anchoring native-asset probing to the
/// image itself keeps resolution correct there. Under JIT hosting managed code does not live
/// inside a loadable image, so resolution reports failure and callers fall back to standard
/// probing.
/// </para>
/// <para>
/// The runtime provides no managed API for recovering a loaded image's path; the mechanism
/// here follows <see href="https://github.com/dotnet/runtime/issues/112290"/> and the
/// address-anchored approach demonstrated in
/// <see href="https://github.com/dotnet/runtime/discussions/122457"/>.
/// </para>
/// </remarks>
internal static unsafe partial class NativeModuleLocator
{
    /// <summary>
    /// <c>GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT</c> from <c>libloaderapi.h</c>. The
    /// returned handle does not take a reference on the module, so it must not be freed.
    /// </summary>
    private const uint GetModuleHandleExFlagUnchangedRefCount = 0x00000002;

    /// <summary>
    /// <c>GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS</c> from <c>libloaderapi.h</c>. The address
    /// argument is any address inside the target module rather than a module name.
    /// </summary>
    private const uint GetModuleHandleExFlagFromAddress = 0x00000004;

    /// <summary>
    /// Attempts to resolve the directory of the image containing this library's code.
    /// </summary>
    /// <param name="directory">Receives the image directory.</param>
    /// <returns><see langword="true"/> when the image path could be resolved.</returns>
    public static bool TryGetModuleDirectory(out string directory)
    {
        directory = string.Empty;
        delegate* managed<void> anchor = &Anchor;

        string modulePath;
        if (OperatingSystem.IsWindows())
        {
            if (GetModuleHandleExW(GetModuleHandleExFlagFromAddress | GetModuleHandleExFlagUnchangedRefCount, (nint)anchor, out nint module) == 0)
            {
                return false;
            }

            modulePath = GetModuleFilePath(module);
        }
        else
        {
            // dladdr reports the image containing an address. Resolving it through the process
            // exports avoids binding to a platform-specific libdl name.
            if (!NativeLibrary.TryGetExport(NativeLibrary.GetMainProgramHandle(), "dladdr", out nint export))
            {
                return false;
            }

            delegate* unmanaged<void*, DlInfo*, int> dladdr = (delegate* unmanaged<void*, DlInfo*, int>)export;
            DlInfo info;
            if (dladdr(anchor, &info) == 0 || info.FileName is null)
            {
                return false;
            }

            modulePath = Marshal.PtrToStringUTF8((nint)info.FileName) ?? string.Empty;
        }

        if (modulePath.Length == 0)
        {
            return false;
        }

        string? parent = FilePath.GetDirectoryName(modulePath);
        if (parent is null || parent.Length == 0)
        {
            return false;
        }

        directory = parent;
        return true;
    }

    /// <summary>
    /// Reads the file path of a loaded native module.
    /// </summary>
    /// <param name="module">The loaded module handle.</param>
    /// <returns>The module's file path, or an empty string when it cannot be read.</returns>
    public static string GetModuleFilePath(nint module)
    {
        Span<char> buffer = stackalloc char[260];
        fixed (char* bufferPointer = buffer)
        {
            uint length = GetModuleFileNameW(module, bufferPointer, (uint)buffer.Length);
            if (length > 0 && length < buffer.Length)
            {
                return new string(buffer[..(int)length]);
            }
        }

        // Cold fallback for paths beyond MAX_PATH up to the documented Windows maximum.
        char[] largeBuffer = new char[short.MaxValue];
        fixed (char* largePointer = largeBuffer)
        {
            uint length = GetModuleFileNameW(module, largePointer, (uint)largeBuffer.Length);
            return length > 0 && length < largeBuffer.Length ? new string(largeBuffer, 0, (int)length) : string.Empty;
        }
    }

    /// <summary>
    /// Provides an address that lives inside this library's compiled image.
    /// </summary>
    private static void Anchor()
    {
    }

    /// <summary>
    /// Reads the file path of a loaded module into the supplied buffer.
    /// </summary>
    /// <param name="module">The loaded module handle.</param>
    /// <param name="filename">The buffer receiving the path.</param>
    /// <param name="size">The buffer length in characters.</param>
    /// <returns>The number of characters written, or zero on failure.</returns>
    [LibraryImport("kernel32", EntryPoint = "GetModuleFileNameW", SetLastError = true)]
    private static partial uint GetModuleFileNameW(nint module, char* filename, uint size);

    /// <summary>
    /// Retrieves a module handle for the image containing the supplied address.
    /// </summary>
    /// <param name="flags">The handle retrieval flags.</param>
    /// <param name="address">An address inside the target image.</param>
    /// <param name="module">Receives the module handle.</param>
    /// <returns>A non-zero value on success.</returns>
    [LibraryImport("kernel32", EntryPoint = "GetModuleHandleExW", SetLastError = true)]
    private static partial int GetModuleHandleExW(uint flags, nint address, out nint module);

    /// <summary>
    /// Image information reported by <c>dladdr</c>.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct DlInfo
    {
        /// <summary>
        /// The path of the image containing the queried address.
        /// </summary>
        public byte* FileName;

        /// <summary>
        /// The base address of the image.
        /// </summary>
        public void* BaseAddress;

        /// <summary>
        /// The name of the nearest symbol.
        /// </summary>
        public byte* SymbolName;

        /// <summary>
        /// The address of the nearest symbol.
        /// </summary>
        public void* SymbolAddress;
    }
}
