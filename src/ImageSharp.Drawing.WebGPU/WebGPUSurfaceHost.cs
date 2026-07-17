// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// Identifies which native platform surface a <see cref="WebGPUSurfaceHost"/> addresses.
/// </summary>
internal enum WebGPUSurfaceHostKind
{
    /// <summary>
    /// A GLFW-owned window (<c>GLFWwindow*</c> in <see cref="WebGPUSurfaceHost.Handle0"/>).
    /// </summary>
    Glfw,

    /// <summary>
    /// An SDL-owned window (<c>SDL_Window*</c> in <see cref="WebGPUSurfaceHost.Handle0"/>).
    /// </summary>
    Sdl,

    /// <summary>
    /// A Win32 window (<c>HWND</c> in <see cref="WebGPUSurfaceHost.Handle0"/> and <c>HINSTANCE</c> in
    /// <see cref="WebGPUSurfaceHost.Handle1"/>).
    /// </summary>
    Win32,

    /// <summary>
    /// An X11 window (<c>Display*</c> in <see cref="WebGPUSurfaceHost.Handle0"/>, window id in
    /// <see cref="WebGPUSurfaceHost.Number0"/>).
    /// </summary>
    X11,

    /// <summary>
    /// A Cocoa window (<c>NSWindow*</c> in <see cref="WebGPUSurfaceHost.Handle0"/>).
    /// </summary>
    Cocoa,

    /// <summary>
    /// A Core Animation metal layer (<c>CAMetalLayer*</c> in <see cref="WebGPUSurfaceHost.Handle0"/>).
    /// </summary>
    MetalLayer,

    /// <summary>
    /// A Wayland surface (<c>wl_display*</c> in <see cref="WebGPUSurfaceHost.Handle0"/>, <c>wl_surface*</c> in
    /// <see cref="WebGPUSurfaceHost.Handle1"/>).
    /// </summary>
    Wayland,

    /// <summary>
    /// A WinUI swap-chain panel (<c>ISwapChainPanelNative*</c> in <see cref="WebGPUSurfaceHost.Handle0"/>).
    /// </summary>
    SwapChainPanel,

    /// <summary>
    /// An Android native window (<c>ANativeWindow*</c> in <see cref="WebGPUSurfaceHost.Handle0"/>).
    /// </summary>
    Android,
}

/// <summary>
/// Describes the externally-owned native drawable that a <see cref="WebGPUExternalSurface"/> should attach to.
/// Use the factory method that matches the host toolkit or platform that owns the drawable surface.
/// </summary>
/// <remarks>
/// Construct via the platform-specific factory methods. The caller retains ownership of the underlying handles;
/// the external surface never releases them.
/// GLFW and SDL handles are translated to their underlying platform source during surface creation. The remaining
/// factories correspond directly to surface source descriptors accepted by the bundled wgpu-native API.
/// </remarks>
public readonly struct WebGPUSurfaceHost
{
    // Compact tagged payload for platform-specific native handles. Kind defines how these slots map
    // to the internal surface adapter, keeping backend windowing details out of the public API.
    private readonly nint handle0;
    private readonly nint handle1;
    private readonly nuint number0;

    /// <summary>
    /// Initializes a new instance of the <see cref="WebGPUSurfaceHost"/> struct. Only the slots meaningful
    /// for <paramref name="kind"/> are populated; see the public factory methods for the per-platform mapping.
    /// </summary>
    /// <param name="kind">The native platform surface kind.</param>
    /// <param name="handle0">The first pointer-sized handle slot.</param>
    /// <param name="handle1">The second pointer-sized handle slot.</param>
    /// <param name="number0">The pointer-sized numeric slot.</param>
    private WebGPUSurfaceHost(
        WebGPUSurfaceHostKind kind,
        nint handle0 = 0,
        nint handle1 = 0,
        nuint number0 = 0)
    {
        this.Kind = kind;
        this.handle0 = handle0;
        this.handle1 = handle1;
        this.number0 = number0;
    }

    /// <summary>
    /// Gets the native platform surface kind that defines how the handle and number slots are interpreted.
    /// </summary>
    internal WebGPUSurfaceHostKind Kind { get; }

    /// <summary>
    /// Gets the first pointer-sized handle slot; its meaning depends on <see cref="Kind"/>.
    /// </summary>
    internal nint Handle0 => this.handle0;

    /// <summary>
    /// Gets the second pointer-sized handle slot; its meaning depends on <see cref="Kind"/>.
    /// </summary>
    internal nint Handle1 => this.handle1;

    /// <summary>
    /// Gets the pointer-sized numeric slot; its meaning depends on <see cref="Kind"/>.
    /// </summary>
    internal nuint Number0 => this.number0;

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
    /// Creates a host descriptor for a Win32 window.
    /// </summary>
    /// <param name="hwnd">The Win32 window handle (<c>HWND</c>).</param>
    /// <param name="hinstance">The module instance handle (<c>HINSTANCE</c>) associated with the window.</param>
    /// <returns>A Win32 host descriptor.</returns>
    public static WebGPUSurfaceHost Win32(nint hwnd, nint hinstance)
        => new(WebGPUSurfaceHostKind.Win32, handle0: hwnd, handle1: hinstance);

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
    /// Creates a host descriptor for a Core Animation metal layer.
    /// </summary>
    /// <param name="metalLayer">The Core Animation metal layer pointer (<c>CAMetalLayer*</c>).</param>
    /// <returns>A metal-layer host descriptor.</returns>
    /// <remarks>The caller owns the layer and must keep it valid for the lifetime of the external surface.</remarks>
    public static WebGPUSurfaceHost MetalLayer(nint metalLayer)
        => new(WebGPUSurfaceHostKind.MetalLayer, handle0: metalLayer);

    /// <summary>
    /// Creates a host descriptor for a Wayland surface.
    /// </summary>
    /// <param name="display">The Wayland display pointer (<c>wl_display*</c>).</param>
    /// <param name="surface">The Wayland surface pointer (<c>wl_surface*</c>).</param>
    /// <returns>A Wayland host descriptor.</returns>
    public static WebGPUSurfaceHost Wayland(nint display, nint surface)
        => new(WebGPUSurfaceHostKind.Wayland, handle0: display, handle1: surface);

    /// <summary>
    /// Creates a host descriptor for a WinUI swap-chain panel.
    /// </summary>
    /// <param name="panelNative">The swap-chain panel's <c>ISwapChainPanelNative*</c> interface pointer.</param>
    /// <returns>A swap-chain-panel host descriptor.</returns>
    /// <remarks>The caller owns the interface pointer and must keep it valid for the lifetime of the external surface.</remarks>
    public static WebGPUSurfaceHost SwapChainPanel(nint panelNative)
        => new(WebGPUSurfaceHostKind.SwapChainPanel, handle0: panelNative);

    /// <summary>
    /// Creates a host descriptor for an Android native window.
    /// </summary>
    /// <param name="aNativeWindow">The Android native window pointer (<c>ANativeWindow*</c>).</param>
    /// <returns>An Android host descriptor.</returns>
    public static WebGPUSurfaceHost Android(nint aNativeWindow)
        => new(WebGPUSurfaceHostKind.Android, handle0: aNativeWindow);
}
