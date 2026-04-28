// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// Identifies which native platform surface a <see cref="WebGPUSurfaceHost"/> addresses.
/// </summary>
internal enum WebGPUSurfaceHostKind
{
    Glfw,
    Sdl,
    Win32,
    X11,
    Cocoa,
    UIKit,
    Wayland,
    WinRT,
    Android,
    Vivante,
    EGL,
}

/// <summary>
/// Describes the externally-owned native drawable that a <see cref="WebGPUExternalSurface{TPixel}"/> should attach to.
/// Use the factory method that matches the host toolkit or platform that owns the drawable surface.
/// </summary>
/// <remarks>
/// Construct via the platform-specific factory methods. The caller retains ownership of the underlying handles;
/// the external surface never releases them.
/// </remarks>
public readonly struct WebGPUSurfaceHost
{
    // Compact tagged payload for platform-specific native handles. Kind defines how these slots map
    // to the internal surface adapter, keeping backend windowing details out of the public API.
    private readonly nint handle0;
    private readonly nint handle1;
    private readonly nint handle2;
    private readonly nuint number0;
    private readonly uint number1;
    private readonly uint number2;
    private readonly uint number3;

    private WebGPUSurfaceHost(
        WebGPUSurfaceHostKind kind,
        nint handle0 = 0,
        nint handle1 = 0,
        nint handle2 = 0,
        nuint number0 = 0,
        uint number1 = 0,
        uint number2 = 0,
        uint number3 = 0)
    {
        this.Kind = kind;
        this.handle0 = handle0;
        this.handle1 = handle1;
        this.handle2 = handle2;
        this.number0 = number0;
        this.number1 = number1;
        this.number2 = number2;
        this.number3 = number3;
    }

    internal WebGPUSurfaceHostKind Kind { get; }

    internal nint Handle0 => this.handle0;

    internal nint Handle1 => this.handle1;

    internal nint Handle2 => this.handle2;

    internal nuint Number0 => this.number0;

    internal uint Number1 => this.number1;

    internal uint Number2 => this.number2;

    internal uint Number3 => this.number3;

    /// <summary>
    /// Creates a host descriptor for a GLFW-owned window.
    /// </summary>
    /// <param name="glfwWindow">The GLFW window pointer (<c>GLFWwindow*</c>).</param>
    /// <returns>A GLFW host descriptor.</returns>
    public static WebGPUSurfaceHost Glfw(nint glfwWindow)
        => new(WebGPUSurfaceHostKind.Glfw, handle0: glfwWindow);

    /// <summary>
    /// Creates a host descriptor for an SDL-owned window.
    /// </summary>
    /// <param name="sdlWindow">The SDL window pointer (<c>SDL_Window*</c>).</param>
    /// <returns>An SDL host descriptor.</returns>
    public static WebGPUSurfaceHost Sdl(nint sdlWindow)
        => new(WebGPUSurfaceHostKind.Sdl, handle0: sdlWindow);

    /// <summary>
    /// Creates a host descriptor for a Win32 window. The device context handle is optional; when omitted,
    /// the surface layer derives one from the window as needed.
    /// </summary>
    /// <param name="hwnd">The Win32 window handle (<c>HWND</c>).</param>
    /// <param name="hinstance">The module instance handle (<c>HINSTANCE</c>) associated with the window.</param>
    /// <param name="hdc">The device context handle (<c>HDC</c>); pass <see cref="nint.Zero"/> if unavailable.</param>
    /// <returns>A Win32 host descriptor.</returns>
    public static WebGPUSurfaceHost Win32(nint hwnd, nint hinstance, nint hdc = 0)
        => new(WebGPUSurfaceHostKind.Win32, handle0: hwnd, handle1: hdc, handle2: hinstance);

    /// <summary>
    /// Creates a host descriptor for an X11 window.
    /// </summary>
    /// <param name="display">The X11 display pointer (<c>Display*</c>).</param>
    /// <param name="window">The X11 window identifier.</param>
    /// <returns>An X11 host descriptor.</returns>
    public static WebGPUSurfaceHost X11(nint display, nuint window)
        => new(WebGPUSurfaceHostKind.X11, handle0: display, number0: window);

    /// <summary>
    /// Creates a host descriptor for a Cocoa window.
    /// </summary>
    /// <param name="nsWindow">The Cocoa window pointer (<c>NSWindow*</c>).</param>
    /// <returns>A Cocoa host descriptor.</returns>
    public static WebGPUSurfaceHost Cocoa(nint nsWindow)
        => new(WebGPUSurfaceHostKind.Cocoa, handle0: nsWindow);

    /// <summary>
    /// Creates a host descriptor for a UIKit window with its framebuffer objects.
    /// </summary>
    /// <param name="uiWindow">The UIKit window pointer (<c>UIWindow*</c>).</param>
    /// <param name="framebuffer">The framebuffer object id.</param>
    /// <param name="colorbuffer">The color buffer object id.</param>
    /// <param name="resolveFramebuffer">The resolve framebuffer object id.</param>
    /// <returns>A UIKit host descriptor.</returns>
    public static WebGPUSurfaceHost UIKit(nint uiWindow, uint framebuffer, uint colorbuffer, uint resolveFramebuffer)
        => new(WebGPUSurfaceHostKind.UIKit, handle0: uiWindow, number1: framebuffer, number2: colorbuffer, number3: resolveFramebuffer);

    /// <summary>
    /// Creates a host descriptor for a Wayland surface.
    /// </summary>
    /// <param name="display">The Wayland display pointer (<c>wl_display*</c>).</param>
    /// <param name="surface">The Wayland surface pointer (<c>wl_surface*</c>).</param>
    /// <returns>A Wayland host descriptor.</returns>
    public static WebGPUSurfaceHost Wayland(nint display, nint surface)
        => new(WebGPUSurfaceHostKind.Wayland, handle0: display, handle1: surface);

    /// <summary>
    /// Creates a host descriptor for a WinRT window.
    /// </summary>
    /// <param name="inspectable">The WinRT window's <c>IInspectable*</c>.</param>
    /// <returns>A WinRT host descriptor.</returns>
    public static WebGPUSurfaceHost WinRT(nint inspectable)
        => new(WebGPUSurfaceHostKind.WinRT, handle0: inspectable);

    /// <summary>
    /// Creates a host descriptor for an Android native window.
    /// </summary>
    /// <param name="aNativeWindow">The Android native window pointer (<c>ANativeWindow*</c>).</param>
    /// <param name="eglSurface">The associated EGL surface (optional).</param>
    /// <returns>An Android host descriptor.</returns>
    public static WebGPUSurfaceHost Android(nint aNativeWindow, nint eglSurface = 0)
        => new(WebGPUSurfaceHostKind.Android, handle0: aNativeWindow, handle1: eglSurface);

    /// <summary>
    /// Creates a host descriptor for a Vivante-backed window.
    /// </summary>
    /// <param name="display">The Vivante EGL display type (<c>EGLNativeDisplayType</c>).</param>
    /// <param name="window">The Vivante EGL window type (<c>EGLNativeWindowType</c>).</param>
    /// <returns>A Vivante host descriptor.</returns>
    public static WebGPUSurfaceHost Vivante(nint display, nint window)
        => new(WebGPUSurfaceHostKind.Vivante, handle0: display, handle1: window);

    /// <summary>
    /// Creates a host descriptor for an EGL display and surface.
    /// </summary>
    /// <param name="eglDisplay">The EGL display handle (<see cref="nint.Zero"/> if unspecified).</param>
    /// <param name="eglSurface">The EGL surface handle (<see cref="nint.Zero"/> if unspecified).</param>
    /// <returns>An EGL host descriptor.</returns>
    public static WebGPUSurfaceHost EGL(nint eglDisplay, nint eglSurface)
        => new(WebGPUSurfaceHostKind.EGL, handle0: eglDisplay, handle1: eglSurface);
}
