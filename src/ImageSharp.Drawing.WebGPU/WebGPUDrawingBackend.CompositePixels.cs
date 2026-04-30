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
/// <see cref="CompositeRegistrations"/> is intentionally explicit and only includes one-to-one format mappings
/// where the GPU texture format can round-trip the pixel payload without channel swizzle or custom conversion logic.
/// Only formats that support <c>storage</c> texture binding (required by the compute compositor) are included.
/// Formats that lack storage support are omitted and fall back to the CPU backend.
/// </remarks>
public sealed partial class WebGPUDrawingBackend
{
    private static readonly CompositePixelRegistration[] CompositeRegistrations =
    [
        CompositePixelRegistration.Create<NormalizedByte4>(TextureFormat.Rgba8Snorm, TextureSampleType.Float, new("rgba8snorm", CompositeTextureEncodingKind.Snorm)),
        CompositePixelRegistration.Create<HalfVector4>(TextureFormat.Rgba16float, TextureSampleType.Float, new("rgba16float", CompositeTextureEncodingKind.Float)),
        CompositePixelRegistration.Create<Rgba32>(TextureFormat.Rgba8Unorm, TextureSampleType.Float, new("rgba8unorm", CompositeTextureEncodingKind.Float)),
        CompositePixelRegistration.Create<Bgra32>(TextureFormat.Bgra8Unorm, TextureSampleType.Float, new("bgra8unorm", CompositeTextureEncodingKind.Float), FeatureName.Bgra8UnormStorage),
    ];

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

    private static bool TryFind(Type pixelType, out CompositePixelRegistration registration)
    {
        foreach (CompositePixelRegistration r in CompositeRegistrations)
        {
            if (r.PixelType == pixelType)
            {
                registration = r;
                return true;
            }
        }

        registration = default;
        return false;
    }

    private static bool TryFind(TextureFormat textureFormat, out CompositePixelRegistration registration)
    {
        foreach (CompositePixelRegistration r in CompositeRegistrations)
        {
            if (r.TextureFormat == textureFormat)
            {
                registration = r;
                return true;
            }
        }

        registration = default;
        return false;
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
        if (!TryFind(typeof(TPixel), out CompositePixelRegistration r))
        {
            formatId = default;
            return false;
        }

        formatId = WebGPUTextureFormatMapper.FromSilk(r.TextureFormat);
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
        if (!TryFind(typeof(TPixel), out CompositePixelRegistration r))
        {
            formatId = default;
            requiredFeature = FeatureName.Undefined;
            return false;
        }

        formatId = WebGPUTextureFormatMapper.FromSilk(r.TextureFormat);
        requiredFeature = r.RequiredFeature;
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
    {
        if (!TryFind(textureFormat, out CompositePixelRegistration r))
        {
            sampleType = default;
            return false;
        }

        sampleType = r.SampleType;
        return true;
    }

    /// <summary>
    /// Resolves the shader-side read/write traits for a registered composite texture format.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static bool TryGetCompositeTextureShaderTraits(TextureFormat textureFormat, out CompositeTextureShaderTraits traits)
    {
        if (!TryFind(textureFormat, out CompositePixelRegistration r))
        {
            traits = default;
            return false;
        }

        traits = r.ShaderTraits;
        return true;
    }

    /// <summary>
    /// Shader-facing traits derived from one registered composite texture format.
    /// </summary>
    internal readonly struct CompositeTextureShaderTraits(string outputFormat, CompositeTextureEncodingKind encodingKind)
    {
        /// <summary>
        /// Gets the WGSL storage-texture format token used for writes.
        /// </summary>
        public string OutputFormat { get; } = outputFormat;

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
        /// <param name="requiredFeature">Optional device feature required for storage binding support.</param>
        /// <param name="shaderTraits">Shader-facing read/write traits for this format.</param>
        public CompositePixelRegistration(
            Type pixelType,
            TextureFormat textureFormat,
            TextureSampleType sampleType,
            FeatureName requiredFeature,
            CompositeTextureShaderTraits shaderTraits)
        {
            this.PixelType = pixelType;
            this.TextureFormat = textureFormat;
            this.SampleType = sampleType;
            this.RequiredFeature = requiredFeature;
            this.ShaderTraits = shaderTraits;
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
        /// Gets the sampled texture type used when reading this format.
        /// </summary>
        public TextureSampleType SampleType { get; }

        /// <summary>
        /// Gets the optional device feature required for storage binding support.
        /// <see cref="FeatureName.Undefined"/> means the format is natively storable.
        /// </summary>
        public FeatureName RequiredFeature { get; }

        /// <summary>
        /// Gets the shader-facing read/write traits for this format.
        /// </summary>
        public CompositeTextureShaderTraits ShaderTraits { get; }

        /// <summary>
        /// Creates a registration record for <typeparamref name="TPixel"/> with a required device feature.
        /// </summary>
        /// <param name="textureFormat">The matching WebGPU texture format.</param>
        /// <param name="sampleType">The sampled texture type for this format.</param>
        /// <param name="shaderTraits">Shader-facing read/write traits for this format.</param>
        /// <param name="requiredFeature">The device feature required for storage binding.</param>
        /// <returns>The initialized registration.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static CompositePixelRegistration Create<TPixel>(TextureFormat textureFormat, TextureSampleType sampleType, CompositeTextureShaderTraits shaderTraits, FeatureName requiredFeature = FeatureName.Undefined)
            where TPixel : unmanaged, IPixel<TPixel>
            => new(typeof(TPixel), textureFormat, sampleType, requiredFeature, shaderTraits);
    }
}
