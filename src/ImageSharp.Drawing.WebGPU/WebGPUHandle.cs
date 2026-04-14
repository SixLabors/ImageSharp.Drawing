// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Runtime.InteropServices;

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// Base <see cref="SafeHandle"/> wrapper for one native WebGPU handle.
/// </summary>
/// <remarks>
/// WebGPU calls in this assembly ultimately consume raw native pointers.
/// <see cref="AcquireReference"/> keeps the underlying handle alive while that raw pointer
/// is in active use so disposal and finalization cannot close it underneath the native call.
/// </remarks>
internal abstract class WebGPUHandle : SafeHandle
{
    /// <summary>
    /// Initializes a new instance of the <see cref="WebGPUHandle"/> class.
    /// </summary>
    /// <param name="handle">The non-zero native handle value.</param>
    /// <param name="ownsHandle">
    /// <see langword="true"/> when this wrapper owns the native handle and must release it;
    /// <see langword="false"/> when ownership stays with the caller.
    /// </param>
    protected WebGPUHandle(nint handle, bool ownsHandle)
        : base(IntPtr.Zero, ownsHandle)
    {
        if (handle == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(handle), "Handle must be non-zero.");
        }

        this.SetHandle(handle);
    }

    /// <inheritdoc />
    public override bool IsInvalid => this.handle == IntPtr.Zero;

    /// <summary>
    /// Acquires a scoped raw-handle reference.
    /// </summary>
    /// <returns>
    /// One disposable token that exposes the raw native handle and releases the matching
    /// <see cref="SafeHandle.DangerousAddRef(ref bool)"/> when disposed.
    /// </returns>
    /// <remarks>
    /// This method is the single place that pairs
    /// <see cref="SafeHandle.DangerousAddRef(ref bool)"/> with
    /// <see cref="SafeHandle.DangerousRelease"/> for WebGPU handles in this assembly.
    /// Callers should keep the returned token in the narrowest possible scope and dispose it
    /// as soon as the native call sequence that uses <see cref="HandleReference.Handle"/> ends.
    /// </remarks>
    internal HandleReference AcquireReference()
    {
        bool addRefSucceeded = false;
        this.DangerousAddRef(ref addRefSucceeded);

        try
        {
            return new HandleReference(this, this.DangerousGetHandle());
        }
        catch
        {
            if (addRefSucceeded)
            {
                this.DangerousRelease();
            }

            throw;
        }
    }

    /// <summary>
    /// Represents one acquired raw-handle reference.
    /// </summary>
    /// <remarks>
    /// This token exists so callers can use a normal <c>using</c> scope around raw WebGPU pointers
    /// without carrying a separate success flag beside the handle. Disposing it releases the
    /// reference acquired by <see cref="AcquireReference"/>.
    /// </remarks>
    internal sealed class HandleReference : IDisposable
    {
        private WebGPUHandle? owner;

        internal HandleReference(WebGPUHandle owner, nint handle)
        {
            this.owner = owner;
            this.Handle = handle;
        }

        /// <summary>
        /// Gets the raw native handle kept alive by this reference.
        /// </summary>
        public nint Handle { get; }

        /// <summary>
        /// Releases the acquired safe-handle reference.
        /// </summary>
        public void Dispose()
        {
            WebGPUHandle? current = this.owner;
            if (current is null)
            {
                return;
            }

            this.owner = null;
            current.DangerousRelease();
        }
    }
}
