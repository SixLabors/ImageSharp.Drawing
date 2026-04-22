// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// Identifies which native windowing platform a <see cref="WebGPUWindowHost"/> addresses.
/// </summary>
internal enum WebGPUWindowHostKind
{
    Glfw,
    Sdl,
    Win32,
    X11,
    DirectFB,
    Cocoa,
    UIKit,
    Wayland,
    WinRT,
    Android,
    Vivante,
    OS2,
    Haiku,
    EGL,
}

/// <summary>
/// Describes the externally-owned native window that a <see cref="WebGPUHostedWindow{TPixel}"/> should attach to.
/// Supports every major desktop, mobile, and embedded windowing system.
/// </summary>
/// <remarks>
/// Construct via the platform-specific factory methods. The caller retains ownership of the underlying handles;
/// the hosted window never releases them.
/// </remarks>
public readonly struct WebGPUWindowHost
{
    private readonly nint handle0;
    private readonly nint handle1;
    private readonly nint handle2;
    private readonly nuint number0;
    private readonly uint number1;
    private readonly uint number2;
    private readonly uint number3;

    private WebGPUWindowHost(
        WebGPUWindowHostKind kind,
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

    internal WebGPUWindowHostKind Kind { get; }

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
    public static WebGPUWindowHost Glfw(nint glfwWindow)
        => new(WebGPUWindowHostKind.Glfw, handle0: glfwWindow);

    /// <summary>
    /// Creates a host descriptor for an SDL-owned window.
    /// </summary>
    /// <param name="sdlWindow">The SDL window pointer (<c>SDL_Window*</c>).</param>
    /// <returns>An SDL host descriptor.</returns>
    public static WebGPUWindowHost Sdl(nint sdlWindow)
        => new(WebGPUWindowHostKind.Sdl, handle0: sdlWindow);

    /// <summary>
    /// Creates a host descriptor for a Win32 window. The device context handle is optional; when omitted,
    /// the surface layer derives one from the window as needed.
    /// </summary>
    /// <param name="hwnd">The Win32 window handle (<c>HWND</c>).</param>
    /// <param name="hinstance">The module instance handle (<c>HINSTANCE</c>) associated with the window.</param>
    /// <param name="hdc">The device context handle (<c>HDC</c>); pass <see cref="nint.Zero"/> if unavailable.</param>
    /// <returns>A Win32 host descriptor.</returns>
    public static WebGPUWindowHost Win32(nint hwnd, nint hinstance, nint hdc = 0)
        => new(WebGPUWindowHostKind.Win32, handle0: hwnd, handle1: hdc, handle2: hinstance);

    /// <summary>
    /// Creates a host descriptor for an X11 window.
    /// </summary>
    /// <param name="display">The X11 display pointer (<c>Display*</c>).</param>
    /// <param name="window">The X11 window identifier.</param>
    /// <returns>An X11 host descriptor.</returns>
    public static WebGPUWindowHost X11(nint display, nuint window)
        => new(WebGPUWindowHostKind.X11, handle0: display, number0: window);

    /// <summary>
    /// Creates a host descriptor for a DirectFB-backed window. DirectFB has no native handle payload;
    /// the descriptor only tags the platform.
    /// </summary>
    /// <returns>A DirectFB host descriptor.</returns>
    public static WebGPUWindowHost DirectFB()
        => new(WebGPUWindowHostKind.DirectFB);

    /// <summary>
    /// Creates a host descriptor for a Cocoa window.
    /// </summary>
    /// <param name="nsWindow">The Cocoa window pointer (<c>NSWindow*</c>).</param>
    /// <returns>A Cocoa host descriptor.</returns>
    public static WebGPUWindowHost Cocoa(nint nsWindow)
        => new(WebGPUWindowHostKind.Cocoa, handle0: nsWindow);

    /// <summary>
    /// Creates a host descriptor for a UIKit window with its OpenGL framebuffer objects.
    /// </summary>
    /// <param name="uiWindow">The UIKit window pointer (<c>UIWindow*</c>).</param>
    /// <param name="framebuffer">The OpenGL framebuffer object id.</param>
    /// <param name="colorbuffer">The OpenGL renderbuffer object id.</param>
    /// <param name="resolveFramebuffer">The resolve color renderbuffer object id.</param>
    /// <returns>A UIKit host descriptor.</returns>
    public static WebGPUWindowHost UIKit(nint uiWindow, uint framebuffer, uint colorbuffer, uint resolveFramebuffer)
        => new(WebGPUWindowHostKind.UIKit, handle0: uiWindow, number1: framebuffer, number2: colorbuffer, number3: resolveFramebuffer);

    /// <summary>
    /// Creates a host descriptor for a Wayland surface.
    /// </summary>
    /// <param name="display">The Wayland display pointer (<c>wl_display*</c>).</param>
    /// <param name="surface">The Wayland surface pointer (<c>wl_surface*</c>).</param>
    /// <returns>A Wayland host descriptor.</returns>
    public static WebGPUWindowHost Wayland(nint display, nint surface)
        => new(WebGPUWindowHostKind.Wayland, handle0: display, handle1: surface);

    /// <summary>
    /// Creates a host descriptor for a WinRT window.
    /// </summary>
    /// <param name="inspectable">The WinRT window's <c>IInspectable*</c>.</param>
    /// <returns>A WinRT host descriptor.</returns>
    public static WebGPUWindowHost WinRT(nint inspectable)
        => new(WebGPUWindowHostKind.WinRT, handle0: inspectable);

    /// <summary>
    /// Creates a host descriptor for an Android native window.
    /// </summary>
    /// <param name="aNativeWindow">The Android native window pointer (<c>ANativeWindow*</c>).</param>
    /// <param name="eglSurface">The associated EGL surface (optional).</param>
    /// <returns>An Android host descriptor.</returns>
    public static WebGPUWindowHost Android(nint aNativeWindow, nint eglSurface = 0)
        => new(WebGPUWindowHostKind.Android, handle0: aNativeWindow, handle1: eglSurface);

    /// <summary>
    /// Creates a host descriptor for a Vivante-backed window.
    /// </summary>
    /// <param name="display">The Vivante EGL display type (<c>EGLNativeDisplayType</c>).</param>
    /// <param name="window">The Vivante EGL window type (<c>EGLNativeWindowType</c>).</param>
    /// <returns>A Vivante host descriptor.</returns>
    public static WebGPUWindowHost Vivante(nint display, nint window)
        => new(WebGPUWindowHostKind.Vivante, handle0: display, handle1: window);

    /// <summary>
    /// Creates a host descriptor for an OS/2 window. OS/2 has no native handle payload; the descriptor
    /// only tags the platform.
    /// </summary>
    /// <returns>An OS/2 host descriptor.</returns>
    public static WebGPUWindowHost OS2()
        => new(WebGPUWindowHostKind.OS2);

    /// <summary>
    /// Creates a host descriptor for a Haiku window. Haiku has no native handle payload; the descriptor
    /// only tags the platform.
    /// </summary>
    /// <returns>A Haiku host descriptor.</returns>
    public static WebGPUWindowHost Haiku()
        => new(WebGPUWindowHostKind.Haiku);

    /// <summary>
    /// Creates a host descriptor for an EGL display and surface.
    /// </summary>
    /// <param name="eglDisplay">The EGL display handle (<see cref="nint.Zero"/> if unspecified).</param>
    /// <param name="eglSurface">The EGL surface handle (<see cref="nint.Zero"/> if unspecified).</param>
    /// <returns>An EGL host descriptor.</returns>
    public static WebGPUWindowHost EGL(nint eglDisplay, nint eglSurface)
        => new(WebGPUWindowHostKind.EGL, handle0: eglDisplay, handle1: eglSurface);
}
