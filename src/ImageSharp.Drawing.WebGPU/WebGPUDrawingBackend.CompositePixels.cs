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
public sealed partial class WebGPUDrawingBackend
{
    private static readonly Lazy<Dictionary<TextureFormat, TextureSampleType>> CompositeTextureSampleTypes =
        new(CreateCompositeTextureSampleTypes);

    private static readonly Lazy<Dictionary<TextureFormat, CompositeTextureShaderTraits>> CompositeTextureShaderTraitsMap =
        new(CreateCompositeTextureShaderTraits);

    /// <summary>
    /// Describes how one registered composite texture format encodes channel values in shader space.
    /// </summary>
    internal enum CompositeTextureEncodingKind
    {
        Float,
        Snorm,
        Uint8,
        Uint16,
        Sint16
    }

    /// <summary>
    /// Builds the static registration table that maps <see cref="IPixel{TSelf}"/> implementations to
    /// compatible WebGPU storage/sampling formats.
    /// </summary>
    /// <returns>The registration map used during flush dispatch.</returns>
    private static Dictionary<Type, CompositePixelRegistration> CreateCompositePixelHandlers() =>

        // Only formats with native or feature-gated storage binding support.
        new()
        {
            [typeof(NormalizedByte4)] = CompositePixelRegistration.Create<NormalizedByte4>(TextureFormat.Rgba8Snorm, TextureSampleType.Float),

            [typeof(HalfVector4)] = CompositePixelRegistration.Create<HalfVector4>(TextureFormat.Rgba16float, TextureSampleType.Float),

            [typeof(Rgba32)] = CompositePixelRegistration.Create<Rgba32>(TextureFormat.Rgba8Unorm, TextureSampleType.Float),
            [typeof(Bgra32)] = CompositePixelRegistration.Create<Bgra32>(TextureFormat.Bgra8Unorm, TextureSampleType.Float, FeatureName.Bgra8UnormStorage)
        };

    /// <summary>
    /// Builds the sampled-texture-type lookup keyed by the explicit composite format registrations.
    /// </summary>
    /// <returns>The lookup used by shader/bind-group setup.</returns>
    private static Dictionary<TextureFormat, TextureSampleType> CreateCompositeTextureSampleTypes()
    {
        Dictionary<TextureFormat, TextureSampleType> sampleTypes = [];
        foreach (CompositePixelRegistration registration in CompositePixelHandlers.Values)
        {
            sampleTypes[registration.TextureFormat] = registration.SampleType;
        }

        return sampleTypes;
    }

    /// <summary>
    /// Resolves the WebGPU texture format identifier for <typeparamref name="TPixel"/>.
    /// </summary>
    /// <typeparam name="TPixel">The requested pixel type.</typeparam>
    /// <param name="formatId">Receives the mapped texture format identifier on success.</param>
    /// <returns>
    /// <see langword="true"/> when the pixel type has a registered GPU format mapping; otherwise <see langword="false"/>.
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
    /// Resolves the WebGPU texture format identifier and any required device feature
    /// for <typeparamref name="TPixel"/>.
    /// </summary>
    /// <typeparam name="TPixel">The requested pixel type.</typeparam>
    /// <param name="formatId">Receives the mapped texture format identifier on success.</param>
    /// <param name="requiredFeature">
    /// Receives the device feature required for storage binding, or
    /// <see cref="FeatureName.Undefined"/> when no special feature is needed.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the pixel type has a registered GPU format mapping; otherwise <see langword="false"/>.
    /// </returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static bool TryGetCompositeTextureFormat<TPixel>(out WebGPUTextureFormatId formatId, out FeatureName requiredFeature)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        if (!CompositePixelHandlers.TryGetValue(typeof(TPixel), out CompositePixelRegistration registration))
        {
            formatId = default;
            requiredFeature = FeatureName.Undefined;
            return false;
        }

        formatId = WebGPUTextureFormatMapper.FromSilk(registration.TextureFormat);
        requiredFeature = registration.RequiredFeature;
        return true;
    }

    /// <summary>
    /// Resolves the unmanaged size in bytes of a registered composite pixel type.
    /// </summary>
    /// <typeparam name="TPixel">The requested pixel type.</typeparam>
    /// <param name="pixelSizeInBytes">Receives the unmanaged pixel size in bytes on success.</param>
    /// <returns>
    /// <see langword="true"/> when the pixel type has a registered GPU format mapping; otherwise <see langword="false"/>.
    /// </returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static bool TryGetCompositePixelSize<TPixel>(out int pixelSizeInBytes)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        if (!CompositePixelHandlers.TryGetValue(typeof(TPixel), out CompositePixelRegistration registration))
        {
            pixelSizeInBytes = 0;
            return false;
        }

        pixelSizeInBytes = registration.PixelSizeInBytes;
        return true;
    }

    /// <summary>
    /// Resolves the sampled texture type for a registered composite texture format.
    /// </summary>
    /// <param name="textureFormat">The WebGPU texture format.</param>
    /// <param name="sampleType">Receives the sampled texture type on success.</param>
    /// <returns>
    /// <see langword="true"/> when the format is one of the explicitly registered composite formats;
    /// otherwise <see langword="false"/>.
    /// </returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static bool TryGetCompositeTextureSampleType(TextureFormat textureFormat, out TextureSampleType sampleType)
        => CompositeTextureSampleTypes.Value.TryGetValue(textureFormat, out sampleType);

    /// <summary>
    /// Resolves the shader-side read/write traits for a registered composite texture format.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static bool TryGetCompositeTextureShaderTraits(TextureFormat textureFormat, out CompositeTextureShaderTraits traits)
        => CompositeTextureShaderTraitsMap.Value.TryGetValue(textureFormat, out traits);

    /// <summary>
    /// Builds the format-to-shader-traits lookup used when specializing composition shaders.
    /// </summary>
    private static Dictionary<TextureFormat, CompositeTextureShaderTraits> CreateCompositeTextureShaderTraits()
        => new()
        {
            [TextureFormat.Rgba8Snorm] = new CompositeTextureShaderTraits("rgba8snorm", "f32", TextureSampleType.Float, CompositeTextureEncodingKind.Snorm),
            [TextureFormat.Rgba16float] = new CompositeTextureShaderTraits("rgba16float", "f32", TextureSampleType.Float, CompositeTextureEncodingKind.Float),
            [TextureFormat.Rgba8Unorm] = new CompositeTextureShaderTraits("rgba8unorm", "f32", TextureSampleType.Float, CompositeTextureEncodingKind.Float),
            [TextureFormat.Bgra8Unorm] = new CompositeTextureShaderTraits("bgra8unorm", "f32", TextureSampleType.Float, CompositeTextureEncodingKind.Float)
        };

    /// <summary>
    /// Shader-facing traits derived from one registered composite texture format.
    /// </summary>
    internal readonly struct CompositeTextureShaderTraits(
        string outputFormat,
        string texelType,
        TextureSampleType sampleType,
        CompositeTextureEncodingKind encodingKind)
    {
        /// <summary>
        /// Gets the WGSL storage-texture format token used for writes.
        /// </summary>
        public string OutputFormat { get; } = outputFormat;

        /// <summary>
        /// Gets the WGSL sampled texel type used when reading the texture.
        /// </summary>
        public string TexelType { get; } = texelType;

        /// <summary>
        /// Gets the WebGPU sampled texture type required by bind-group validation.
        /// </summary>
        public TextureSampleType SampleType { get; } = sampleType;

        /// <summary>
        /// Gets the numeric encoding that the shader must apply when storing output texels.
        /// </summary>
        public CompositeTextureEncodingKind EncodingKind { get; } = encodingKind;
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
        /// <param name="sampleType">The sampled texture type for this format.</param>
        /// <param name="pixelSizeInBytes">The unmanaged pixel size in bytes.</param>
        /// <param name="requiredFeature">Optional device feature required for storage binding support.</param>
        public CompositePixelRegistration(
            Type pixelType,
            TextureFormat textureFormat,
            TextureSampleType sampleType,
            int pixelSizeInBytes,
            FeatureName requiredFeature)
        {
            this.PixelType = pixelType;
            this.TextureFormat = textureFormat;
            this.SampleType = sampleType;
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
        /// Gets the sampled texture type used when reading this format.
        /// </summary>
        public TextureSampleType SampleType { get; }

        /// <summary>
        /// Gets the optional device feature required for storage binding support.
        /// <see cref="FeatureName.Undefined"/> means the format is natively storable.
        /// </summary>
        public FeatureName RequiredFeature { get; }

        /// <summary>
        /// Creates a registration record for <typeparamref name="TPixel"/> with native storage support.
        /// </summary>
        /// <param name="textureFormat">The matching WebGPU texture format.</param>
        /// <param name="sampleType">The sampled texture type for this format.</param>
        /// <returns>The initialized registration.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static CompositePixelRegistration Create<TPixel>(TextureFormat textureFormat, TextureSampleType sampleType)
            where TPixel : unmanaged, IPixel<TPixel>
            => new(typeof(TPixel), textureFormat, sampleType, Unsafe.SizeOf<TPixel>(), FeatureName.Undefined);

        /// <summary>
        /// Creates a registration record for <typeparamref name="TPixel"/> with a required device feature.
        /// </summary>
        /// <param name="textureFormat">The matching WebGPU texture format.</param>
        /// <param name="sampleType">The sampled texture type for this format.</param>
        /// <param name="requiredFeature">The device feature required for storage binding.</param>
        /// <returns>The initialized registration.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static CompositePixelRegistration Create<TPixel>(TextureFormat textureFormat, TextureSampleType sampleType, FeatureName requiredFeature)
            where TPixel : unmanaged, IPixel<TPixel>
            => new(typeof(TPixel), textureFormat, sampleType, Unsafe.SizeOf<TPixel>(), requiredFeature);
    }
}
