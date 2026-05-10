// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Diagnostics;
using System.Runtime.InteropServices;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Drawing.Processing.Backends;
using ImageSharpSize = SixLabors.ImageSharp.Size;

namespace WebGPUExternalSurfaceDemo.Controls;

/// <summary>
/// A reusable WinForms control that embeds a <see cref="WebGPUExternalSurface"/>. Callers hook <see cref="PaintFrame"/> with their scene logic;
/// the control handles construction, resize, acquire/present, and teardown.
/// </summary>
public sealed partial class WebGPURenderControl : Control
{
    private const int WM_MOVING = 0x0216;
    private const int WM_EXITSIZEMOVE = 0x0232;

    private WebGPUExternalSurface? surface;
    private Size framebufferSize;
    private bool idleHooked;
    private long lastTicks;
    private WebGPURenderMode renderMode;
    private System.Windows.Forms.Timer? titleBarMoveTimer;
    private TitleBarListener? titleBarListener;

    /// <summary>
    /// Initializes a new instance of the <see cref="WebGPURenderControl"/> class.
    /// </summary>
    public WebGPURenderControl()
    {
        // WebGPU presents directly to the native surface. Normal WinForms buffering and background
        // painting would add flicker or unnecessary work, so the control opts into direct user painting.
        this.SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.Opaque |
            ControlStyles.UserPaint,
            true);

        this.SetStyle(ControlStyles.OptimizedDoubleBuffer, false);
    }

    /// <summary>
    /// Raised each frame once the external surface has acquired a drawable frame.
    /// </summary>
    public event Action<DrawingCanvas, TimeSpan>? PaintFrame;

    /// <summary>
    /// Gets or sets how this control schedules rendering.
    /// </summary>
    public WebGPURenderMode RenderMode
    {
        get => this.renderMode;
        set
        {
            if (this.renderMode == value)
            {
                return;
            }

            // Changing mode changes who is responsible for the next frame: WinForms paint
            // messages for on-demand scenes, or Application.Idle for animated scenes.
            this.renderMode = value;
            this.UpdateIdleHook();
            this.Invalidate();
        }
    }

    /// <inheritdoc />
    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);

        // An external surface can only be created once the native HWND exists. The surface borrows the HWND;
        // WinForms still owns the control, handle lifetime, and layout.
        // WinForms ClientSize is the HWND client rectangle size; pass it through as the drawable framebuffer size.
        this.framebufferSize = this.ClientSize;
        ImageSharpSize initialFramebufferSize = new(
            Math.Max(this.framebufferSize.Width, 1),
            Math.Max(this.framebufferSize.Height, 1));

        // The module handle is required by the Win32 surface descriptor. It identifies the process module
        // that owns the window class backing this control.
        this.surface = new WebGPUExternalSurface(
            WebGPUSurfaceHost.Win32(
                this.Handle,
                Marshal.GetHINSTANCE(typeof(WebGPURenderControl).Module)),
            initialFramebufferSize,
            new WebGPUExternalSurfaceOptions
            {
                Format = WebGPUTextureFormat.Bgra8Unorm
            });

        this.lastTicks = Stopwatch.GetTimestamp();
        this.UpdateIdleHook();

        // Application.Idle stops firing during a title-bar drag (modal move loop).
        // Attach a NativeWindow to the parent form's handle so we can see WM_MOVING
        // which fires only during title-bar drags, never during border resizes,
        // and drive a short Timer during those. Border resize is left untouched.
        Form? parentForm = this.FindForm();
        if (parentForm is not null && parentForm.IsHandleCreated)
        {
            this.titleBarMoveTimer = new System.Windows.Forms.Timer { Interval = 16 };
            this.titleBarMoveTimer.Tick += this.OnTitleBarMoveTick;
            this.titleBarListener = new TitleBarListener(this);
            this.titleBarListener.AssignHandle(parentForm.Handle);
        }
    }

    /// <inheritdoc />
    protected override void OnHandleDestroyed(EventArgs e)
    {
        this.UnhookApplicationIdle();

        this.titleBarListener?.ReleaseHandle();
        this.titleBarListener = null;

        if (this.titleBarMoveTimer is not null)
        {
            this.titleBarMoveTimer.Stop();
            this.titleBarMoveTimer.Tick -= this.OnTitleBarMoveTick;
            this.titleBarMoveTimer.Dispose();
            this.titleBarMoveTimer = null;
        }

        this.surface?.Dispose();
        this.surface = null;
        this.framebufferSize = Size.Empty;
        base.OnHandleDestroyed(e);
    }

    /// <inheritdoc />
    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (this.surface is not null)
        {
            // WinForms ClientSize is the HWND client rectangle size; pass it through as the drawable framebuffer size.
            this.framebufferSize = this.ClientSize;
            this.surface.Resize(new ImageSharpSize(this.framebufferSize.Width, this.framebufferSize.Height));
            this.Invalidate();
        }
    }

    /// <inheritdoc />
    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);

        // Application.Idle is process-wide. Keep it hooked only while this particular
        // surface can be seen, otherwise an inactive tab can keep rendering in the background.
        this.UpdateIdleHook();

        if (this.Visible)
        {
            this.Invalidate();
        }
    }

    /// <inheritdoc />
    protected override void OnParentVisibleChanged(EventArgs e)
    {
        base.OnParentVisibleChanged(e);

        // TabControl usually changes the tab page visibility, not the child control's own
        // Visible property. Re-evaluate here so hidden tab pages stop continuous rendering too.
        this.UpdateIdleHook();

        if (this.Visible)
        {
            this.Invalidate();
        }
    }

    /// <inheritdoc />
    protected override void OnPaintBackground(PaintEventArgs pevent)
    {
        // WebGPU owns the surface; suppress the WinForms background paint.
    }

    /// <inheritdoc />
    protected override void OnPaint(PaintEventArgs e) => _ = this.RenderOnce();

    // Continuous scenes opt into Application.Idle. Rendering only once would leave animation
    // frozen between user input events, so continuous mode loops while the queue stays empty
    // and while the control remains visible. On-demand scenes skip this hook and render from
    // normal WinForms invalidation instead.
    private void OnApplicationIdle(object? sender, EventArgs e)
    {
        while (this.renderMode == WebGPURenderMode.Continuous && this.Visible && IsApplicationIdle())
        {
            if (!this.RenderOnce())
            {
                break;
            }
        }
    }

    private void OnTitleBarMoveTick(object? sender, EventArgs e)
    {
        // WinForms enters a modal move loop for title-bar dragging, so Application.Idle is
        // paused. The temporary timer keeps animated visible scenes alive during that loop.
        if (this.renderMode == WebGPURenderMode.Continuous && this.Visible)
        {
            _ = this.RenderOnce();
        }
    }

    private void UpdateIdleHook()
    {
        // Application.Idle is a global app event, so the control must actively subscribe and
        // unsubscribe as its scheduling mode and visibility change. Leaving hidden controls
        // hooked is enough to burn a steady core percentage while another tab is selected.
        if (!this.IsHandleCreated)
        {
            return;
        }

        if (this.renderMode == WebGPURenderMode.Continuous && this.Visible)
        {
            this.HookApplicationIdle();
            return;
        }

        this.UnhookApplicationIdle();
    }

    private void HookApplicationIdle()
    {
        if (this.idleHooked)
        {
            return;
        }

        Application.Idle += this.OnApplicationIdle;
        this.idleHooked = true;
    }

    private void UnhookApplicationIdle()
    {
        if (!this.idleHooked)
        {
            return;
        }

        Application.Idle -= this.OnApplicationIdle;
        this.idleHooked = false;
    }

    // Hooks the parent form's handle so the control can observe WM_MOVING / WM_EXITSIZEMOVE
    // without requiring the form to know this control exists. The listener only starts the
    // timer for visible continuous scenes; hidden tabs should not render during a window drag.
    private sealed class TitleBarListener(WebGPURenderControl owner) : NativeWindow
    {
        protected override void WndProc(ref Message m)
        {
            switch (m.Msg)
            {
                case WM_MOVING:
                    if (owner.renderMode == WebGPURenderMode.Continuous && owner.Visible)
                    {
                        owner.titleBarMoveTimer?.Start();
                    }

                    break;
                case WM_EXITSIZEMOVE:
                    owner.titleBarMoveTimer?.Stop();
                    break;
            }

            base.WndProc(ref m);
        }
    }

    private bool RenderOnce()
    {
        if (!this.Visible || this.surface is null || this.framebufferSize.Width <= 0 || this.framebufferSize.Height <= 0)
        {
            return false;
        }

        // Frame acquisition can fail transiently while the surface is unavailable, for example during resize
        // or device recovery. Returning false lets continuous mode leave the idle loop instead of busy-spinning
        // on a surface that cannot currently produce a drawable frame.
        if (!this.surface.TryAcquireFrame(out WebGPUSurfaceFrame? frame))
        {
            return false;
        }

        long now = Stopwatch.GetTimestamp();
        TimeSpan delta = TimeSpan.FromSeconds((now - this.lastTicks) / (double)Stopwatch.Frequency);
        this.lastTicks = now;

        using (frame)
        {
            // The canvas records drawing work against the acquired surface texture. Disposing the frame
            // renders that work, presents the texture, and releases the per-frame native handles.
            this.PaintFrame?.Invoke(frame.Canvas, delta);
        }

        return true;
    }

    // PeekMessage with wRemoveMsg = 0 (PM_NOREMOVE) asks the OS "is there a message waiting?" without
    // dequeuing it. Returning 0 means the queue is empty, the canonical "is the app idle right now"
    // check used by the WinForms continuous-render idiom. Cheaper than any managed alternative and the
    // only way to tell whether WinForms is about to break out of Application.Idle on the next pump.
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeMessage
    {
        public nint Handle;
        public uint Message;
        public nint WParam;
        public nint LParam;
        public uint Time;
        public Point Location;
    }

    [LibraryImport("user32.dll", EntryPoint = "PeekMessageW")]
    private static partial int PeekMessage(out NativeMessage msg, nint hWnd, uint messageFilterMin, uint messageFilterMax, uint flags);

    private static bool IsApplicationIdle() => PeekMessage(out _, 0, 0, 0, 0) == 0;
}
