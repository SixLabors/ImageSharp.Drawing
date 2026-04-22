// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using Silk.NET.Core.Contexts;

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// Bridges a <see cref="WebGPUWindowHost"/> descriptor to Silk.NET's <see cref="INativeWindowSource"/>
/// and <see cref="INativeWindow"/> so the existing <c>CreateWebGPUSurface</c> extension can build
/// a surface from externally-owned platform handles.
/// </summary>
internal sealed class SilkNativeWindowAdapter : INativeWindowSource, INativeWindow
{
    private readonly WebGPUWindowHost host;

    public SilkNativeWindowAdapter(WebGPUWindowHost host) => this.host = host;

    public INativeWindow? Native => this;

    public NativeWindowFlags Kind => this.host.Kind switch
    {
        WebGPUWindowHostKind.Glfw => NativeWindowFlags.Glfw,
        WebGPUWindowHostKind.Sdl => NativeWindowFlags.Sdl,
        WebGPUWindowHostKind.Win32 => NativeWindowFlags.Win32,
        WebGPUWindowHostKind.X11 => NativeWindowFlags.X11,
        WebGPUWindowHostKind.DirectFB => NativeWindowFlags.DirectFB,
        WebGPUWindowHostKind.Cocoa => NativeWindowFlags.Cocoa,
        WebGPUWindowHostKind.UIKit => NativeWindowFlags.UIKit,
        WebGPUWindowHostKind.Wayland => NativeWindowFlags.Wayland,
        WebGPUWindowHostKind.WinRT => NativeWindowFlags.WinRT,
        WebGPUWindowHostKind.Android => NativeWindowFlags.Android,
        WebGPUWindowHostKind.Vivante => NativeWindowFlags.Vivante,
        WebGPUWindowHostKind.OS2 => NativeWindowFlags.OS2,
        WebGPUWindowHostKind.Haiku => NativeWindowFlags.Haiku,
        WebGPUWindowHostKind.EGL => NativeWindowFlags.EGL,
        _ => 0,
    };

    public nint? Glfw
        => this.host.Kind == WebGPUWindowHostKind.Glfw ? this.host.Handle0 : null;

    public nint? Sdl
        => this.host.Kind == WebGPUWindowHostKind.Sdl ? this.host.Handle0 : null;

    public (nint Hwnd, nint HDC, nint HInstance)? Win32
        => this.host.Kind == WebGPUWindowHostKind.Win32
            ? (this.host.Handle0, this.host.Handle1, this.host.Handle2)
            : null;

    public (nint Display, nuint Window)? X11
        => this.host.Kind == WebGPUWindowHostKind.X11 ? (this.host.Handle0, this.host.Number0) : null;

    public nint? Cocoa
        => this.host.Kind == WebGPUWindowHostKind.Cocoa ? this.host.Handle0 : null;

    public (nint Window, uint Framebuffer, uint Colorbuffer, uint ResolveFramebuffer)? UIKit
        => this.host.Kind == WebGPUWindowHostKind.UIKit
            ? (this.host.Handle0, this.host.Number1, this.host.Number2, this.host.Number3)
            : null;

    public (nint Display, nint Surface)? Wayland
        => this.host.Kind == WebGPUWindowHostKind.Wayland ? (this.host.Handle0, this.host.Handle1) : null;

    public nint? WinRT
        => this.host.Kind == WebGPUWindowHostKind.WinRT ? this.host.Handle0 : null;

    public (nint Window, nint Surface)? Android
        => this.host.Kind == WebGPUWindowHostKind.Android ? (this.host.Handle0, this.host.Handle1) : null;

    public (nint Display, nint Window)? Vivante
        => this.host.Kind == WebGPUWindowHostKind.Vivante ? (this.host.Handle0, this.host.Handle1) : null;

    public (nint? Display, nint? Surface)? EGL
        => this.host.Kind == WebGPUWindowHostKind.EGL
            ? (this.host.Handle0 == 0 ? null : this.host.Handle0, this.host.Handle1 == 0 ? null : this.host.Handle1)
            : null;

    public nint? DXHandle
        => this.host.Kind == WebGPUWindowHostKind.Win32 ? this.host.Handle0 : null;
}
