# WebGPU External Surface Demo

`WebGPUExternalSurfaceDemo` shows how to render ImageSharp.Drawing content into a UI object owned by an application framework. The sample uses a WinForms `Control`, but the API shape is intended for any externally-owned native drawable host.

It exists to demonstrate:

- creating a `WebGPUExternalSurface` from a `WebGPUSurfaceHost`
- keeping the external surface synchronized with the host control's drawable framebuffer size
- acquiring `WebGPUSurfaceFrame` instances manually
- drawing with the normal `DrawingCanvas` API
- presenting by disposing the acquired frame

## Running

```bash
dotnet run --project samples/WebGPUExternalSurfaceDemo -c Debug
```

Requirements:

- .NET 8.0 SDK or later
- Windows, because this sample is a WinForms app
- a WebGPU-capable desktop backend such as D3D12 or Vulkan
- adapter support for the storage-capable BGRA format selected by the sample

When the sample starts you should see a WinForms window with five tabs:

- `Clock`: a continuously-rendered animated clock scene
- `Tiger`: an interactive SVG tiger viewer with pan and zoom
- `Apply`: a reactive external-surface readback scene using `DrawingCanvas.Apply(...)`; move the mouse to move the edge-detect and blur regions, and use the mouse wheel to resize them
- `Manual Text Flow`: prepared text is laid out one line at a time around a selectable path obstacle that follows mouse movement
- `Rich Text Editor`: a small editor surface that exercises text selection, caret movement, hit testing, font changes, and inline styling

## Why This Sample Matters

`WebGPUWindow` owns a top-level native window. `WebGPUExternalSurface` is different: it attaches WebGPU rendering to something the application already owns, such as a control, view, widget, or native surface.

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
this.surface = new WebGPUExternalSurface(
    WebGPUSurfaceHost.Win32(
        this.Handle,
        Marshal.GetHINSTANCE(typeof(WebGPURenderControl).Module)),
    initialFramebufferSize,
    new WebGPUExternalSurfaceOptions
    {
        Format = WebGPUTextureFormat.Bgra8Unorm
    });
```

`WebGPUSurfaceHost` is a small public descriptor for externally-owned native handles. The host keeps ownership of those handles; `WebGPUExternalSurface` only uses them to create and manage the WebGPU rendering surface.

### Resize

`WebGPUExternalSurface.Resize(...)` expects the drawable framebuffer size in pixels:

```csharp
this.framebufferSize = this.ClientSize;
this.surface.Resize(new ImageSharpSize(this.framebufferSize.Width, this.framebufferSize.Height));
```

The acquired frame exposes the same pixel coordinate space through `frame.Canvas.Bounds`.

### Frame Acquisition

`RenderOnce()` acquires a frame, invokes user drawing code, and disposes the frame:

```csharp
if (!this.surface.TryAcquireFrame(out WebGPUSurfaceFrame? frame))
{
    return;
}

using (frame)
{
    this.PaintFrame?.Invoke(frame.Canvas, delta);
}
```

Disposing the frame renders pending canvas work, presents the surface texture, and releases the per-frame WebGPU handles.

### Rendering Loop

`WebGPURenderControl` supports on-demand and continuous rendering. On-demand controls render from normal WinForms invalidation, which keeps static scenes idle until input, resize, or another event asks them to repaint. Continuous controls hook `Application.Idle` and render while the WinForms message queue is empty; the clock scene uses this mode because it animates without input.

Frame pacing is delegated to the present mode. With the default `WebGPUPresentMode.Fifo`, frame acquisition naturally waits for display presentation instead of using a separate software timer.

## Scene Code

[MainForm.cs](d:/GitHub/SixLabors/ImageSharp.Drawing/samples/WebGPUExternalSurfaceDemo/MainForm.cs) creates independent `WebGPURenderControl` instances, one per tab. Each control owns its own external surface.

The scenes are deliberately ordinary canvas code:

- [ClockScene.cs](d:/GitHub/SixLabors/ImageSharp.Drawing/samples/WebGPUExternalSurfaceDemo/Scenes/ClockScene.cs): animated vector clock
- [TigerViewerScene.cs](d:/GitHub/SixLabors/ImageSharp.Drawing/samples/WebGPUExternalSurfaceDemo/Scenes/TigerViewerScene.cs): pan and zoom SVG tiger viewer
- [ApplyReadbackScene.cs](d:/GitHub/SixLabors/ImageSharp.Drawing/samples/WebGPUExternalSurfaceDemo/Scenes/ApplyReadbackScene.cs): `Apply(...)` scene that reads the external surface back into CPU processing
- [ManualTextFlowScene.cs](d:/GitHub/SixLabors/ImageSharp.Drawing/samples/WebGPUExternalSurfaceDemo/Scenes/ManualTextFlowScene.cs): interactive manual text flow using prepared line layout enumeration and a selectable closed-path obstacle
- [RichTextEditorScene.cs](d:/GitHub/SixLabors/ImageSharp.Drawing/samples/WebGPUExternalSurfaceDemo/Scenes/RichTextEditorScene.cs): custom rich text editor built from Fonts hit testing, caret, selection, and run APIs

Each scene receives:

- `DrawingCanvas` for the acquired frame
- elapsed time since the previous frame

## Files

- [WebGPURenderControl.cs](d:/GitHub/SixLabors/ImageSharp.Drawing/samples/WebGPUExternalSurfaceDemo/Controls/WebGPURenderControl.cs): reusable WinForms external-surface control
- [MainForm.cs](d:/GitHub/SixLabors/ImageSharp.Drawing/samples/WebGPUExternalSurfaceDemo/MainForm.cs): tabs and scene wiring
- [ClockScene.cs](d:/GitHub/SixLabors/ImageSharp.Drawing/samples/WebGPUExternalSurfaceDemo/Scenes/ClockScene.cs): clock scene
- [TigerViewerScene.cs](d:/GitHub/SixLabors/ImageSharp.Drawing/samples/WebGPUExternalSurfaceDemo/Scenes/TigerViewerScene.cs): tiger viewer scene
- [ApplyReadbackScene.cs](d:/GitHub/SixLabors/ImageSharp.Drawing/samples/WebGPUExternalSurfaceDemo/Scenes/ApplyReadbackScene.cs): apply readback scene
- [ManualTextFlowScene.cs](d:/GitHub/SixLabors/ImageSharp.Drawing/samples/WebGPUExternalSurfaceDemo/Scenes/ManualTextFlowScene.cs): manual text-flow scene
- [RichTextEditorScene.cs](d:/GitHub/SixLabors/ImageSharp.Drawing/samples/WebGPUExternalSurfaceDemo/Scenes/RichTextEditorScene.cs): rich text editor scene
- [WebGPUExternalSurfaceDemo.csproj](d:/GitHub/SixLabors/ImageSharp.Drawing/samples/WebGPUExternalSurfaceDemo/WebGPUExternalSurfaceDemo.csproj): sample project file
