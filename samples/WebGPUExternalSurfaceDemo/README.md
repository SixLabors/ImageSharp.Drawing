# WebGPU External Surface Demo

`WebGPUExternalSurfaceDemo` shows how to render ImageSharp.Drawing content into a UI object owned by an application framework. The sample uses a WinForms `Control`, but the API shape is intended for any externally-owned native drawable host.

It exists to demonstrate:

- creating a `WebGPUExternalSurface<Bgra32>` from a `WebGPUSurfaceHost`
- keeping the external surface synchronized with the host control's drawable framebuffer size
- acquiring `WebGPUSurfaceFrame<TPixel>` instances manually
- drawing with the normal `DrawingCanvas<TPixel>` API
- presenting by disposing the acquired frame

## Running

```bash
dotnet run --project samples/WebGPUExternalSurfaceDemo -c Debug
```

Requirements:

- .NET 8.0 SDK or later
- Windows, because this sample is a WinForms app
- a WebGPU-capable desktop backend such as D3D12 or Vulkan
- adapter support for the storage-capable BGRA format required by `Bgra32`

When the sample starts you should see a WinForms window with three tabs:

- `Clock`: a continuously-rendered animated clock scene
- `Tiger`: an interactive SVG tiger viewer with pan and zoom
- `Apply`: a reactive external-surface readback scene using `DrawingCanvas.Apply(...)`; move the mouse to move the edge-detect and blur regions, and use the mouse wheel to resize them

## Why This Sample Matters

`WebGPUWindow<TPixel>` owns a top-level native window. `WebGPUExternalSurface<TPixel>` is different: it attaches WebGPU rendering to something the application already owns, such as a control, view, widget, or native surface.

That makes it the integration path for UI frameworks. The host owns:

- the UI object and its lifecycle
- the platform handle
- layout and resize events
- input events

The external surface owns:

- the WebGPU surface created for that host
- swapchain configuration
- frame acquisition
- presentation
- per-frame texture and texture-view handles

## Code Tour

The reusable integration point is [WebGPURenderControl.cs](d:/GitHub/SixLabors/ImageSharp.Drawing/samples/WebGPUExternalSurfaceDemo/Controls/WebGPURenderControl.cs).

### Surface Creation

`WebGPURenderControl.OnHandleCreated(...)` creates the external surface from the WinForms control handle:

```csharp
this.surface = new WebGPUExternalSurface<Bgra32>(
    WebGPUSurfaceHost.Win32(
        this.Handle,
        Marshal.GetHINSTANCE(typeof(WebGPURenderControl).Module)),
    initialFramebufferSize);
```

`WebGPUSurfaceHost` is a small public descriptor for externally-owned native handles. The host keeps ownership of those handles; `WebGPUExternalSurface<TPixel>` only uses them to create and manage the WebGPU rendering surface.

### Resize

`WebGPUExternalSurface<TPixel>.Resize(...)` expects the drawable framebuffer size in pixels:

```csharp
this.framebufferSize = this.ClientSize;
this.surface.Resize(new ImageSharpSize(this.framebufferSize.Width, this.framebufferSize.Height));
```

The sample stores that size as `FramebufferSize` so scene code can draw in the same pixel coordinate space as the acquired frame.

### Frame Acquisition

`RenderOnce()` acquires a frame, invokes user drawing code, and disposes the frame:

```csharp
if (!this.surface.TryAcquireFrame(out WebGPUSurfaceFrame<Bgra32>? frame))
{
    return;
}

using (frame)
{
    this.PaintFrame?.Invoke(frame.Canvas, delta);
}
```

Disposing the frame flushes pending canvas work, presents the surface texture, and releases the per-frame WebGPU handles.

### Rendering Loop

The sample uses `Application.Idle` for continuous rendering. While the WinForms message queue is empty, the control renders frames. When input, resize, or other window messages arrive, the loop exits so WinForms can process them.

Frame pacing is delegated to the present mode. With the default `WebGPUPresentMode.Fifo`, frame acquisition naturally waits for display presentation instead of using a separate software timer.

## Scene Code

[MainForm.cs](d:/GitHub/SixLabors/ImageSharp.Drawing/samples/WebGPUExternalSurfaceDemo/MainForm.cs) creates independent `WebGPURenderControl` instances, one per tab. Each control owns its own external surface.

The scenes are deliberately ordinary canvas code:

- [ClockScene.cs](d:/GitHub/SixLabors/ImageSharp.Drawing/samples/WebGPUExternalSurfaceDemo/Scenes/ClockScene.cs): animated vector clock
- [TigerViewerScene.cs](d:/GitHub/SixLabors/ImageSharp.Drawing/samples/WebGPUExternalSurfaceDemo/Scenes/TigerViewerScene.cs): pan and zoom SVG tiger viewer
- [ApplyReadbackScene.cs](d:/GitHub/SixLabors/ImageSharp.Drawing/samples/WebGPUExternalSurfaceDemo/Scenes/ApplyReadbackScene.cs): `Apply(...)` scene that reads the external surface back into CPU processing

Each scene receives:

- `DrawingCanvas<Bgra32>` for the acquired frame
- the current framebuffer size
- elapsed time since the previous frame

## Files

- [WebGPURenderControl.cs](d:/GitHub/SixLabors/ImageSharp.Drawing/samples/WebGPUExternalSurfaceDemo/Controls/WebGPURenderControl.cs): reusable WinForms external-surface control
- [MainForm.cs](d:/GitHub/SixLabors/ImageSharp.Drawing/samples/WebGPUExternalSurfaceDemo/MainForm.cs): tabs and scene wiring
- [ClockScene.cs](d:/GitHub/SixLabors/ImageSharp.Drawing/samples/WebGPUExternalSurfaceDemo/Scenes/ClockScene.cs): clock scene
- [TigerViewerScene.cs](d:/GitHub/SixLabors/ImageSharp.Drawing/samples/WebGPUExternalSurfaceDemo/Scenes/TigerViewerScene.cs): tiger viewer scene
- [ApplyReadbackScene.cs](d:/GitHub/SixLabors/ImageSharp.Drawing/samples/WebGPUExternalSurfaceDemo/Scenes/ApplyReadbackScene.cs): apply readback scene
- [WebGPUExternalSurfaceDemo.csproj](d:/GitHub/SixLabors/ImageSharp.Drawing/samples/WebGPUExternalSurfaceDemo/WebGPUExternalSurfaceDemo.csproj): sample project file
