// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Runtime.CompilerServices;
using Silk.NET.WebGPU;
using SixLabors.ImageSharp.PixelFormats;

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// Pixel-format registration for composite session I/O.
/// </summary>
/// <remarks>
/// The map defined by <see cref="CreateCompositePixelHandlers"/> is intentionally explicit and only
/// includes one-to-one format mappings where the GPU texture format can round-trip the pixel payload
/// without channel swizzle or custom conversion logic.
/// Only formats that support <c>storage</c> texture binding (required by the compute compositor)
/// are included. Formats that lack storage support are omitted and fall back to the CPU backend.
/// </remarks>
internal sealed partial class WebGPUDrawingBackend
{
    /// <summary>
    /// Builds the static registration table that maps <see cref="IPixel{TSelf}"/> implementations to
    /// compatible WebGPU storage/sampling formats.
    /// </summary>
    /// <returns>The registration map used during flush dispatch.</returns>
    private static Dictionary<Type, CompositePixelRegistration> CreateCompositePixelHandlers() =>

        // Only formats with native or feature-gated storage binding support.
        // Non-storable formats (R8Unorm, RG8Unorm, RG8Snorm, R16Float, RG16Float,
        // RG16Sint, Rgb10A2Unorm, R16Uint, RG16Uint) are omitted — they cannot be
        // used as compute shader write targets and fall back to DefaultDrawingBackend.
        new()
        {
            [typeof(Byte4)] = CompositePixelRegistration.Create<Byte4>(TextureFormat.Rgba8Uint),
            [typeof(NormalizedByte4)] = CompositePixelRegistration.Create<NormalizedByte4>(TextureFormat.Rgba8Snorm),

            [typeof(HalfVector4)] = CompositePixelRegistration.Create<HalfVector4>(TextureFormat.Rgba16float),

            [typeof(Short4)] = CompositePixelRegistration.Create<Short4>(TextureFormat.Rgba16Sint),

            [typeof(Rgba32)] = CompositePixelRegistration.Create<Rgba32>(TextureFormat.Rgba8Unorm),
            [typeof(Bgra32)] = CompositePixelRegistration.Create<Bgra32>(TextureFormat.Bgra8Unorm, FeatureName.Bgra8UnormStorage),
            [typeof(RgbaVector)] = CompositePixelRegistration.Create<RgbaVector>(TextureFormat.Rgba32float),

            [typeof(Rgba64)] = CompositePixelRegistration.Create<Rgba64>(TextureFormat.Rgba16Uint)
        };

    /// <summary>
    /// Resolves the WebGPU texture format identifier for <typeparamref name="TPixel"/> when supported
    /// by the current device.
    /// </summary>
    /// <typeparam name="TPixel">The requested pixel type.</typeparam>
    /// <param name="formatId">Receives the mapped texture format identifier on success.</param>
    /// <returns>
    /// <see langword="true"/> when the pixel type is supported for GPU composition; otherwise <see langword="false"/>.
    /// </returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static bool TryGetCompositeTextureFormat<TPixel>(out WebGPUTextureFormatId formatId)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        if (!CompositePixelHandlers.TryGetValue(typeof(TPixel), out CompositePixelRegistration registration))
        {
            formatId = default;
            return false;
        }

        if (registration.RequiredFeature != FeatureName.Undefined
            && !WebGPURuntime.HasDeviceFeature(registration.RequiredFeature))
        {
            formatId = default;
            return false;
        }

        formatId = WebGPUTextureFormatMapper.FromSilk(registration.TextureFormat);
        return true;
    }

    /// <summary>
    /// Per-pixel registration payload consumed by GPU composition setup.
    /// </summary>
    private readonly struct CompositePixelRegistration
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="CompositePixelRegistration"/> struct.
        /// </summary>
        /// <param name="pixelType">The registered pixel CLR type.</param>
        /// <param name="textureFormat">The matching WebGPU texture format.</param>
        /// <param name="pixelSizeInBytes">The unmanaged pixel size in bytes.</param>
        /// <param name="requiredFeature">Optional device feature required for storage binding support.</param>
        public CompositePixelRegistration(
            Type pixelType,
            TextureFormat textureFormat,
            int pixelSizeInBytes,
            FeatureName requiredFeature)
        {
            this.PixelType = pixelType;
            this.TextureFormat = textureFormat;
            this.PixelSizeInBytes = pixelSizeInBytes;
            this.RequiredFeature = requiredFeature;
        }

        /// <summary>
        /// Gets the CLR pixel type registered for this mapping.
        /// </summary>
        public Type PixelType { get; }

        /// <summary>
        /// Gets the WebGPU texture format used for this pixel type.
        /// </summary>
        public TextureFormat TextureFormat { get; }

        /// <summary>
        /// Gets the unmanaged size of the pixel type in bytes.
        /// </summary>
        public int PixelSizeInBytes { get; }

        /// <summary>
        /// Gets the optional device feature required for storage binding support.
        /// <see cref="FeatureName.Undefined"/> means the format is natively storable.
        /// </summary>
        public FeatureName RequiredFeature { get; }

        /// <summary>
        /// Creates a registration record for <typeparamref name="TPixel"/> with native storage support.
        /// </summary>
        /// <param name="textureFormat">The matching WebGPU texture format.</param>
        /// <returns>The initialized registration.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static CompositePixelRegistration Create<TPixel>(TextureFormat textureFormat)
            where TPixel : unmanaged, IPixel<TPixel>
            => new(typeof(TPixel), textureFormat, Unsafe.SizeOf<TPixel>(), FeatureName.Undefined);

        /// <summary>
        /// Creates a registration record for <typeparamref name="TPixel"/> with a required device feature.
        /// </summary>
        /// <param name="textureFormat">The matching WebGPU texture format.</param>
        /// <param name="requiredFeature">The device feature required for storage binding.</param>
        /// <returns>The initialized registration.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static CompositePixelRegistration Create<TPixel>(TextureFormat textureFormat, FeatureName requiredFeature)
            where TPixel : unmanaged, IPixel<TPixel>
            => new(typeof(TPixel), textureFormat, Unsafe.SizeOf<TPixel>(), requiredFeature);
    }
}
