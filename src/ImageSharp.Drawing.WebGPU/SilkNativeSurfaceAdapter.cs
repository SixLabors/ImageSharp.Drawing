// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using Silk.NET.Core.Contexts;

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// Converts a <see cref="WebGPUSurfaceHost"/> descriptor into the native surface source used by the WebGPU surface factory.
/// </summary>
internal sealed class SilkNativeSurfaceAdapter : INativeWindowSource, INativeWindow
{
    private readonly WebGPUSurfaceHost host;

    public SilkNativeSurfaceAdapter(WebGPUSurfaceHost host) => this.host = host;

    public INativeWindow? Native => this;

    public NativeWindowFlags Kind => this.host.Kind switch
    {
        WebGPUSurfaceHostKind.Glfw => NativeWindowFlags.Glfw,
        WebGPUSurfaceHostKind.Sdl => NativeWindowFlags.Sdl,
        WebGPUSurfaceHostKind.Win32 => NativeWindowFlags.Win32,
        WebGPUSurfaceHostKind.X11 => NativeWindowFlags.X11,
        WebGPUSurfaceHostKind.Cocoa => NativeWindowFlags.Cocoa,
        WebGPUSurfaceHostKind.UIKit => NativeWindowFlags.UIKit,
        WebGPUSurfaceHostKind.Wayland => NativeWindowFlags.Wayland,
        WebGPUSurfaceHostKind.WinRT => NativeWindowFlags.WinRT,
        WebGPUSurfaceHostKind.Android => NativeWindowFlags.Android,
        WebGPUSurfaceHostKind.Vivante => NativeWindowFlags.Vivante,
        WebGPUSurfaceHostKind.EGL => NativeWindowFlags.EGL,
        _ => 0,
    };

    public nint? Glfw
        => this.host.Kind == WebGPUSurfaceHostKind.Glfw ? this.host.Handle0 : null;

    public nint? Sdl
        => this.host.Kind == WebGPUSurfaceHostKind.Sdl ? this.host.Handle0 : null;

    public (nint Hwnd, nint HDC, nint HInstance)? Win32
        => this.host.Kind == WebGPUSurfaceHostKind.Win32
            ? (this.host.Handle0, this.host.Handle1, this.host.Handle2)
            : null;

    public (nint Display, nuint Window)? X11
        => this.host.Kind == WebGPUSurfaceHostKind.X11 ? (this.host.Handle0, this.host.Number0) : null;

    public nint? Cocoa
        => this.host.Kind == WebGPUSurfaceHostKind.Cocoa ? this.host.Handle0 : null;

    public (nint Window, uint Framebuffer, uint Colorbuffer, uint ResolveFramebuffer)? UIKit
        => this.host.Kind == WebGPUSurfaceHostKind.UIKit
            ? (this.host.Handle0, this.host.Number1, this.host.Number2, this.host.Number3)
            : null;

    public (nint Display, nint Surface)? Wayland
        => this.host.Kind == WebGPUSurfaceHostKind.Wayland ? (this.host.Handle0, this.host.Handle1) : null;

    public nint? WinRT
        => this.host.Kind == WebGPUSurfaceHostKind.WinRT ? this.host.Handle0 : null;

    public (nint Window, nint Surface)? Android
        => this.host.Kind == WebGPUSurfaceHostKind.Android ? (this.host.Handle0, this.host.Handle1) : null;

    public (nint Display, nint Window)? Vivante
        => this.host.Kind == WebGPUSurfaceHostKind.Vivante ? (this.host.Handle0, this.host.Handle1) : null;

    public (nint? Display, nint? Surface)? EGL
        => this.host.Kind == WebGPUSurfaceHostKind.EGL
            ? (this.host.Handle0 == 0 ? null : this.host.Handle0, this.host.Handle1 == 0 ? null : this.host.Handle1)
            : null;

    public nint? DXHandle
        => this.host.Kind == WebGPUSurfaceHostKind.Win32 ? this.host.Handle0 : null;
}
