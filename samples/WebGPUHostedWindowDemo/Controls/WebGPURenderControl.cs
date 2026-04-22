// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Diagnostics;
using System.Runtime.InteropServices;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Drawing.Processing.Backends;
using SixLabors.ImageSharp.PixelFormats;

namespace WebGPUHostedWindowDemo.Controls;

/// <summary>
/// A reusable WinForms control that embeds a <see cref="WebGPUHostedWindow{TPixel}"/> and drives a continuous
/// render loop via <see cref="Application.Idle"/>. Callers hook <see cref="PaintFrame"/> with their scene logic;
/// the control handles construction, resize, acquire/present, and teardown.
/// </summary>
public sealed partial class WebGPURenderControl : Control
{
    private const int WM_MOVING = 0x0216;
    private const int WM_EXITSIZEMOVE = 0x0232;

    private WebGPUHostedWindow<Bgra32>? window;
    private bool idleHooked;
    private long lastTicks;
    private System.Windows.Forms.Timer? titleBarMoveTimer;
    private TitleBarListener? titleBarListener;

    /// <summary>
    /// Initializes a new instance of the <see cref="WebGPURenderControl"/> class.
    /// </summary>
    public WebGPURenderControl()
    {
        this.SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.Opaque |
            ControlStyles.UserPaint,
            true);

        this.SetStyle(ControlStyles.OptimizedDoubleBuffer, false);
    }

    /// <summary>
    /// Raised each frame once the hosted window has acquired a drawable frame.
    /// </summary>
    public event Action<DrawingCanvas<Bgra32>, TimeSpan>? PaintFrame;

    /// <inheritdoc />
    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);

        int width = Math.Max(this.ClientSize.Width, 1);
        int height = Math.Max(this.ClientSize.Height, 1);

        this.window = new WebGPUHostedWindow<Bgra32>(
            WebGPUWindowHost.Win32(
                this.Handle,
                Marshal.GetHINSTANCE(typeof(WebGPURenderControl).Module)),
            width,
            height);

        this.lastTicks = Stopwatch.GetTimestamp();
        Application.Idle += this.OnApplicationIdle;
        this.idleHooked = true;

        // Application.Idle stops firing during a title-bar drag (modal move loop).
        // Attach a NativeWindow to the parent form's handle so we can see WM_MOVING
        // — which fires only during title-bar drags, never during border resizes —
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
        if (this.idleHooked)
        {
            Application.Idle -= this.OnApplicationIdle;
            this.idleHooked = false;
        }

        this.titleBarListener?.ReleaseHandle();
        this.titleBarListener = null;

        if (this.titleBarMoveTimer is not null)
        {
            this.titleBarMoveTimer.Stop();
            this.titleBarMoveTimer.Tick -= this.OnTitleBarMoveTick;
            this.titleBarMoveTimer.Dispose();
            this.titleBarMoveTimer = null;
        }

        this.window?.Dispose();
        this.window = null;
        base.OnHandleDestroyed(e);
    }

    /// <inheritdoc />
    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (this.window is not null && this.ClientSize.Width > 0 && this.ClientSize.Height > 0)
        {
            this.window.Resize(this.ClientSize.Width, this.ClientSize.Height);
        }
    }

    /// <inheritdoc />
    protected override void OnPaintBackground(PaintEventArgs pevent)
    {
        // WebGPU owns the surface; suppress the WinForms background paint.
    }

    /// <inheritdoc />
    protected override void OnPaint(PaintEventArgs e) => this.RenderOnce();

    // Application.Idle fires once when the WinForms message queue drains. Rendering only once
    // would leave the scene frozen between user input events, so we loop here — re-rendering as
    // long as the queue stays empty — and exit the moment a message arrives so WinForms can
    // process it. Frame pacing is delegated to the swap-chain present mode: with Fifo (v-sync)
    // TryAcquireFrame blocks until the display is ready, so this loop naturally runs at the
    // display's refresh rate without a software timer capping it.
    private void OnApplicationIdle(object? sender, EventArgs e)
    {
        while (IsApplicationIdle())
        {
            this.RenderOnce();
        }
    }

    private void OnTitleBarMoveTick(object? sender, EventArgs e) => this.RenderOnce();

    // Hooks the parent form's handle so the control can observe WM_MOVING / WM_EXITSIZEMOVE
    // without requiring the form to know this control exists.
    private sealed class TitleBarListener(WebGPURenderControl owner) : NativeWindow
    {
        protected override void WndProc(ref Message m)
        {
            switch (m.Msg)
            {
                case WM_MOVING:
                    owner.titleBarMoveTimer?.Start();
                    break;
                case WM_EXITSIZEMOVE:
                    owner.titleBarMoveTimer?.Stop();
                    break;
            }

            base.WndProc(ref m);
        }
    }

    private void RenderOnce()
    {
        if (this.window is null || this.ClientSize.Width <= 0 || this.ClientSize.Height <= 0)
        {
            return;
        }

        if (!this.window.TryAcquireFrame(out WebGPUWindowFrame<Bgra32>? frame))
        {
            return;
        }

        long now = Stopwatch.GetTimestamp();
        TimeSpan delta = TimeSpan.FromSeconds((now - this.lastTicks) / (double)Stopwatch.Frequency);
        this.lastTicks = now;

        using (frame)
        {
            this.PaintFrame?.Invoke(frame.Canvas, delta);
        }
    }

    // PeekMessage with wRemoveMsg = 0 (PM_NOREMOVE) asks the OS "is there a message waiting?" without
    // dequeuing it. Returning 0 means the queue is empty — the canonical "is the app idle right now"
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
