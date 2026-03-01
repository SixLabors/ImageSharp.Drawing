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
/// </remarks>
internal sealed partial class WebGPUDrawingBackend
{
    /// <summary>
    /// Builds the static registration table that maps <see cref="IPixel{TSelf}"/> implementations to
    /// compatible WebGPU storage/sampling formats.
    /// </summary>
    /// <returns>The registration map used during flush dispatch.</returns>
    private static Dictionary<Type, CompositePixelRegistration> CreateCompositePixelHandlers() =>

        // No-swizzle mappings only. Unsupported types are intentionally omitted from this map.
        new()
        {
            [typeof(A8)] = CompositePixelRegistration.Create<A8>(TextureFormat.R8Unorm),
            [typeof(L8)] = CompositePixelRegistration.Create<L8>(TextureFormat.R8Unorm),
            [typeof(La16)] = CompositePixelRegistration.Create<La16>(TextureFormat.RG8Unorm),

            [typeof(Byte4)] = CompositePixelRegistration.Create<Byte4>(TextureFormat.Rgba8Uint),
            [typeof(NormalizedByte2)] = CompositePixelRegistration.Create<NormalizedByte2>(TextureFormat.RG8Snorm),
            [typeof(NormalizedByte4)] = CompositePixelRegistration.Create<NormalizedByte4>(TextureFormat.Rgba8Snorm),

            [typeof(HalfSingle)] = CompositePixelRegistration.Create<HalfSingle>(TextureFormat.R16float),
            [typeof(HalfVector2)] = CompositePixelRegistration.Create<HalfVector2>(TextureFormat.RG16float),
            [typeof(HalfVector4)] = CompositePixelRegistration.Create<HalfVector4>(TextureFormat.Rgba16float),

            [typeof(Short2)] = CompositePixelRegistration.Create<Short2>(TextureFormat.RG16Sint),
            [typeof(Short4)] = CompositePixelRegistration.Create<Short4>(TextureFormat.Rgba16Sint),

            [typeof(Rgba1010102)] = CompositePixelRegistration.Create<Rgba1010102>(TextureFormat.Rgb10A2Unorm),
            [typeof(Rgba32)] = CompositePixelRegistration.Create<Rgba32>(TextureFormat.Rgba8Unorm),
            [typeof(Bgra32)] = CompositePixelRegistration.Create<Bgra32>(TextureFormat.Bgra8Unorm),
            [typeof(RgbaVector)] = CompositePixelRegistration.Create<RgbaVector>(TextureFormat.Rgba32float),

            [typeof(L16)] = CompositePixelRegistration.Create<L16>(TextureFormat.R16Uint),
            [typeof(La32)] = CompositePixelRegistration.Create<La32>(TextureFormat.RG16Uint),
            [typeof(Rg32)] = CompositePixelRegistration.Create<Rg32>(TextureFormat.RG16Uint),
            [typeof(Rgba64)] = CompositePixelRegistration.Create<Rgba64>(TextureFormat.Rgba16Uint)
        };

    /// <summary>
    /// Resolves the WebGPU texture format identifier for <typeparamref name="TPixel"/> when supported.
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
        public CompositePixelRegistration(Type pixelType, TextureFormat textureFormat, int pixelSizeInBytes)
        {
            this.PixelType = pixelType;
            this.TextureFormat = textureFormat;
            this.PixelSizeInBytes = pixelSizeInBytes;
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
        /// Creates a registration record for <typeparamref name="TPixel"/>.
        /// </summary>
        /// <param name="textureFormat">The matching WebGPU texture format.</param>
        /// <returns>The initialized registration.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static CompositePixelRegistration Create<TPixel>(TextureFormat textureFormat)
            where TPixel : unmanaged, IPixel<TPixel>
            => new(typeof(TPixel), textureFormat, Unsafe.SizeOf<TPixel>());
    }
}
