// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Runtime.CompilerServices;
using Silk.NET.WebGPU;
using SixLabors.ImageSharp.PixelFormats;

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// Pixel-format registration for composite session I/O.
/// </summary>
internal sealed partial class WebGPUDrawingBackend
{
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

    private readonly struct CompositePixelRegistration
    {
        public CompositePixelRegistration(Type pixelType, TextureFormat textureFormat, int pixelSizeInBytes)
        {
            this.PixelType = pixelType;
            this.TextureFormat = textureFormat;
            this.PixelSizeInBytes = pixelSizeInBytes;
        }

        public Type PixelType { get; }

        public TextureFormat TextureFormat { get; }

        public int PixelSizeInBytes { get; }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static CompositePixelRegistration Create<TPixel>(TextureFormat textureFormat)
            where TPixel : unmanaged, IPixel<TPixel>
            => new(typeof(TPixel), textureFormat, Unsafe.SizeOf<TPixel>());
    }
}
