// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using Silk.NET.Core.Contexts;
using Silk.NET.GLFW;
using Silk.NET.SDL;
using SixLabors.ImageSharp.Drawing.Processing.Backends.Native;

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// Creates WebGPU surfaces from native window handles.
/// </summary>
internal static unsafe class WebGPUSurfaceFactory
{
    /// <summary>
    /// Creates a WebGPU surface for a native window source.
    /// </summary>
    /// <param name="api">The WebGPU API facade.</param>
    /// <param name="instance">The instance that owns the surface.</param>
    /// <param name="source">The native window source.</param>
    /// <returns>The created surface, or <see langword="null"/> on failure.</returns>
    public static WGPUSurfaceImpl* Create(WebGPU api, WGPUInstanceImpl* instance, INativeWindowSource source)
    {
        // Every backend entry point supplies an initialized window source. The Silk contract makes
        // Native nullable only for source implementations that have not created their window yet.
        return Create(api, instance, source.Native!, true);
    }

    /// <summary>
    /// Creates a WebGPU surface for an externally-owned host.
    /// </summary>
    /// <param name="api">The WebGPU API facade.</param>
    /// <param name="instance">The instance that owns the surface.</param>
    /// <param name="host">The external native host.</param>
    /// <returns>The created surface, or <see langword="null"/> on failure.</returns>
    public static WGPUSurfaceImpl* Create(WebGPU api, WGPUInstanceImpl* instance, WebGPUSurfaceHost host)
    {
        // External hosts model the descriptors accepted by wgpu-native directly. GLFW and SDL are
        // the only toolkit-level entries and are translated before reaching the native C API.
        return host.Kind switch
        {
            WebGPUSurfaceHostKind.Glfw => Create(api, instance, new GlfwNativeWindow(GlfwProvider.GLFW.Value, (WindowHandle*)host.Handle0), false),
            WebGPUSurfaceHostKind.Sdl => Create(api, instance, new SdlNativeWindow(SdlProvider.SDL.Value, (Silk.NET.SDL.Window*)host.Handle0), false),
            WebGPUSurfaceHostKind.Win32 => CreateWindowsSurface(api, instance, host.Handle0, host.Handle1),
            WebGPUSurfaceHostKind.X11 => CreateXlibSurface(api, instance, host.Handle0, host.Number0),
            WebGPUSurfaceHostKind.Cocoa => CreateCocoaSurface(api, instance, host.Handle0),
            WebGPUSurfaceHostKind.MetalLayer => CreateMetalLayerSurface(api, instance, host.Handle0),
            WebGPUSurfaceHostKind.Wayland => CreateWaylandSurface(api, instance, host.Handle0, host.Handle1),
            WebGPUSurfaceHostKind.SwapChainPanel => CreateSwapChainPanelSurface(api, instance, host.Handle0),
            WebGPUSurfaceHostKind.Android => CreateAndroidSurface(api, instance, host.Handle0),
            _ => null,
        };
    }

    /// <summary>
    /// Resolves a Silk native window to one of the platform handles represented by the WebGPU C surface descriptors.
    /// </summary>
    /// <param name="api">The WebGPU API facade.</param>
    /// <param name="instance">The instance that owns the surface.</param>
    /// <param name="native">The native window information exposed by Silk.</param>
    /// <param name="translateToolkitHandle">
    /// Whether a GLFW or SDL toolkit handle may be translated to its underlying platform handles. Recursive calls disable
    /// translation so a toolkit that cannot expose a supported platform source terminates without recursing indefinitely.
    /// </param>
    /// <returns>The created surface, or <see langword="null"/> when Silk exposes no compatible platform source.</returns>
    private static WGPUSurfaceImpl* Create(WebGPU api, WGPUInstanceImpl* instance, INativeWindow native, bool translateToolkitHandle)
    {
        if (native.X11 is { } x11)
        {
            return CreateXlibSurface(api, instance, x11.Display, x11.Window);
        }

        if (native.Cocoa is { } cocoa)
        {
            return CreateCocoaSurface(api, instance, cocoa);
        }

        if (native.Wayland is { } wayland)
        {
            return CreateWaylandSurface(api, instance, wayland.Display, wayland.Surface);
        }

        if (native.Win32 is { } win32)
        {
            return CreateWindowsSurface(api, instance, win32.Hwnd, win32.HInstance);
        }

        if (native.Android is { } android)
        {
            return CreateAndroidSurface(api, instance, android.Window);
        }

        if (translateToolkitHandle && native.Glfw is { } glfw)
        {
            // GLFWwindow* is a toolkit handle rather than a WebGPU surface source. Resolve the
            // platform window once, then create the surface from the resulting native handles.
            GlfwNativeWindow platformWindow = new(GlfwProvider.GLFW.Value, (WindowHandle*)glfw);
            return Create(api, instance, platformWindow, false);
        }

        if (translateToolkitHandle && native.Sdl is { } sdl)
        {
            // SDL_Window* follows the same rule. SDL_GetWindowWMInfo exposes the underlying
            // platform handles required by the WebGPU C surface descriptor.
            SdlNativeWindow platformWindow = new(SdlProvider.SDL.Value, (Window*)sdl);
            return Create(api, instance, platformWindow, false);
        }

        return null;
    }

    /// <summary>
    /// Creates a surface from an Xlib display and window identifier.
    /// </summary>
    /// <param name="api">The WebGPU API facade.</param>
    /// <param name="instance">The instance that owns the surface.</param>
    /// <param name="display">The Xlib <c>Display*</c>.</param>
    /// <param name="window">The Xlib window identifier.</param>
    /// <returns>The created surface.</returns>
    private static WGPUSurfaceImpl* CreateXlibSurface(WebGPU api, WGPUInstanceImpl* instance, nint display, nuint window)
    {
        WGPUSurfaceSourceXlibWindow source = new()
        {
            chain = new WGPUChainedStruct { sType = WGPUSType.SurfaceSourceXlibWindow },
            display = (void*)display,
            window = window
        };

        return CreateSurface(api, instance, (WGPUChainedStruct*)&source);
    }

    /// <summary>
    /// Creates and attaches the metal layer required to present through an AppKit window.
    /// </summary>
    /// <param name="api">The WebGPU API facade.</param>
    /// <param name="instance">The instance that owns the surface.</param>
    /// <param name="nsWindow">The AppKit <c>NSWindow*</c>.</param>
    /// <returns>The created surface.</returns>
    private static WGPUSurfaceImpl* CreateCocoaSurface(WebGPU api, WGPUInstanceImpl* instance, nint nsWindow)
    {
        nint metalLayer = MacOSMetalLayerFactory.Create(nsWindow);

        try
        {
            return CreateMetalLayerSurface(api, instance, metalLayer);
        }
        finally
        {
            // The NSView retains its layer property, so release the ownership acquired by alloc/init.
            MacOSMetalLayerFactory.Release(metalLayer);
        }
    }

    /// <summary>
    /// Creates a surface from an existing Core Animation metal layer.
    /// </summary>
    /// <param name="api">The WebGPU API facade.</param>
    /// <param name="instance">The instance that owns the surface.</param>
    /// <param name="metalLayer">The <c>CAMetalLayer*</c> used for presentation.</param>
    /// <returns>The created surface.</returns>
    private static WGPUSurfaceImpl* CreateMetalLayerSurface(WebGPU api, WGPUInstanceImpl* instance, nint metalLayer)
    {
        WGPUSurfaceSourceMetalLayer source = new()
        {
            chain = new WGPUChainedStruct { sType = WGPUSType.SurfaceSourceMetalLayer },
            layer = (void*)metalLayer
        };

        return CreateSurface(api, instance, (WGPUChainedStruct*)&source);
    }

    /// <summary>
    /// Creates a surface from a Wayland display and surface pair.
    /// </summary>
    /// <param name="api">The WebGPU API facade.</param>
    /// <param name="instance">The instance that owns the surface.</param>
    /// <param name="display">The Wayland <c>wl_display*</c>.</param>
    /// <param name="surface">The Wayland <c>wl_surface*</c>.</param>
    /// <returns>The created surface.</returns>
    private static WGPUSurfaceImpl* CreateWaylandSurface(WebGPU api, WGPUInstanceImpl* instance, nint display, nint surface)
    {
        WGPUSurfaceSourceWaylandSurface source = new()
        {
            chain = new WGPUChainedStruct { sType = WGPUSType.SurfaceSourceWaylandSurface },
            display = (void*)display,
            surface = (void*)surface
        };

        return CreateSurface(api, instance, (WGPUChainedStruct*)&source);
    }

    /// <summary>
    /// Creates a surface from a Win32 window and its module instance.
    /// </summary>
    /// <param name="api">The WebGPU API facade.</param>
    /// <param name="instance">The instance that owns the surface.</param>
    /// <param name="hwnd">The Win32 <c>HWND</c>.</param>
    /// <param name="hinstance">The Win32 <c>HINSTANCE</c>.</param>
    /// <returns>The created surface.</returns>
    private static WGPUSurfaceImpl* CreateWindowsSurface(WebGPU api, WGPUInstanceImpl* instance, nint hwnd, nint hinstance)
    {
        WGPUSurfaceSourceWindowsHWND source = new()
        {
            chain = new WGPUChainedStruct { sType = WGPUSType.SurfaceSourceWindowsHWND },
            hwnd = (void*)hwnd,
            hinstance = (void*)hinstance
        };

        return CreateSurface(api, instance, (WGPUChainedStruct*)&source);
    }

    /// <summary>
    /// Creates a surface from an Android native window.
    /// </summary>
    /// <param name="api">The WebGPU API facade.</param>
    /// <param name="instance">The instance that owns the surface.</param>
    /// <param name="window">The Android <c>ANativeWindow*</c>.</param>
    /// <returns>The created surface.</returns>
    private static WGPUSurfaceImpl* CreateAndroidSurface(WebGPU api, WGPUInstanceImpl* instance, nint window)
    {
        WGPUSurfaceSourceAndroidNativeWindow source = new()
        {
            chain = new WGPUChainedStruct { sType = WGPUSType.SurfaceSourceAndroidNativeWindow },
            window = (void*)window
        };

        return CreateSurface(api, instance, (WGPUChainedStruct*)&source);
    }

    /// <summary>
    /// Creates a surface from the native interface of a WinUI swap-chain panel.
    /// </summary>
    /// <param name="api">The WebGPU API facade.</param>
    /// <param name="instance">The instance that owns the surface.</param>
    /// <param name="panelNative">The panel's <c>ISwapChainPanelNative*</c>.</param>
    /// <returns>The created surface.</returns>
    private static WGPUSurfaceImpl* CreateSwapChainPanelSurface(WebGPU api, WGPUInstanceImpl* instance, nint panelNative)
    {
        WGPUSurfaceSourceSwapChainPanel source = new()
        {
            chain = new WGPUChainedStruct { sType = (WGPUSType)WGPUNativeSType.WGPUSType_SurfaceSourceSwapChainPanel },
            panelNative = (void*)panelNative
        };

        return CreateSurface(api, instance, (WGPUChainedStruct*)&source);
    }

    /// <summary>
    /// Passes a platform-specific chained source descriptor to wgpu-native.
    /// </summary>
    /// <param name="api">The WebGPU API facade.</param>
    /// <param name="instance">The instance that owns the surface.</param>
    /// <param name="source">The platform source descriptor at the head of the chain.</param>
    /// <returns>The created surface.</returns>
    private static WGPUSurfaceImpl* CreateSurface(WebGPU api, WGPUInstanceImpl* instance, WGPUChainedStruct* source)
    {
        // Every source descriptor is stack allocated by its owning helper. wgpuInstanceCreateSurface
        // consumes the chain synchronously, so no pointer to that storage escapes this call.
        WGPUSurfaceDescriptor descriptor = new() { nextInChain = source };
        return api.InstanceCreateSurface(instance, in descriptor);
    }
}
