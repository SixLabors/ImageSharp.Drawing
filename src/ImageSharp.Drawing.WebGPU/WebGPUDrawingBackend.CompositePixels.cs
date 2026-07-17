// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Runtime.CompilerServices;
using SixLabors.ImageSharp.PixelFormats;

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// Pixel-format registration for composite session I/O.
/// </summary>
/// <remarks>
/// <see cref="CompositePixelRegistrations"/> is intentionally explicit and only includes one-to-one format mappings
/// where the GPU texture format can round-trip the pixel payload without channel swizzle or custom conversion logic.
/// Only formats that support <c>storage</c> texture binding (required by the compute compositor) are included.
/// Formats that lack storage support are omitted and fall back to the CPU backend.
/// </remarks>
public sealed partial class WebGPUDrawingBackend
{
    private static readonly CompositeTextureRegistration[] CompositeTextureRegistrations =
    [
        new(WebGPUTextureFormat.Rgba8Snorm, TextureFormat.RGBA8Snorm, new("rgba8snorm"), FeatureName.TextureFormatsTier1),
        new(WebGPUTextureFormat.Rgba16Float, TextureFormat.RGBA16Float, new("rgba16float"), default),
        new(WebGPUTextureFormat.Rgba8Unorm, TextureFormat.RGBA8Unorm, new("rgba8unorm"), default),

        // Bgra8Unorm is not storage-bindable in core WebGPU; it requires the optional
        // Bgra8UnormStorage device feature, checked at render and readback time.
        new(WebGPUTextureFormat.Bgra8Unorm, TextureFormat.BGRA8Unorm, new("bgra8unorm"), FeatureName.BGRA8UnormStorage),
    ];

    private static readonly CompositePixelRegistration[] CompositePixelRegistrations =
    [
        CompositePixelRegistration.Create<NormalizedByte4>(WebGPUTextureFormat.Rgba8Snorm, PixelAlphaRepresentation.Unassociated, WebGPUTargetNumericEncoding.SignedUnit),
        CompositePixelRegistration.Create<NormalizedByte4P>(WebGPUTextureFormat.Rgba8Snorm, PixelAlphaRepresentation.Associated, WebGPUTargetNumericEncoding.SignedUnit),
        CompositePixelRegistration.Create<RgbaHalf>(WebGPUTextureFormat.Rgba16Float, PixelAlphaRepresentation.Unassociated, WebGPUTargetNumericEncoding.Unit),
        CompositePixelRegistration.Create<RgbaHalfP>(WebGPUTextureFormat.Rgba16Float, PixelAlphaRepresentation.Associated, WebGPUTargetNumericEncoding.Unit),
        CompositePixelRegistration.Create<Rgba32>(WebGPUTextureFormat.Rgba8Unorm, PixelAlphaRepresentation.Unassociated, WebGPUTargetNumericEncoding.Unit),
        CompositePixelRegistration.Create<Rgba32P>(WebGPUTextureFormat.Rgba8Unorm, PixelAlphaRepresentation.Associated, WebGPUTargetNumericEncoding.Unit),
        CompositePixelRegistration.Create<Bgra32>(WebGPUTextureFormat.Bgra8Unorm, PixelAlphaRepresentation.Unassociated, WebGPUTargetNumericEncoding.Unit),
        CompositePixelRegistration.Create<Bgra32P>(WebGPUTextureFormat.Bgra8Unorm, PixelAlphaRepresentation.Associated, WebGPUTargetNumericEncoding.Unit),
    ];

    /// <summary>
    /// Resolves the WebGPU target descriptor and any required device feature
    /// for <typeparamref name="TPixel"/>.
    /// </summary>
    /// <typeparam name="TPixel">The requested pixel type.</typeparam>
    /// <param name="descriptor">Receives the mapped target descriptor on success.</param>
    /// <param name="requiredFeature">
    /// Receives the device feature required for storage binding, or
    /// the default value when no special feature is needed.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the pixel type has a registered GPU format mapping; otherwise <see langword="false"/>.
    /// </returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static bool TryGetCompositeTargetDescriptor<TPixel>(out WebGPUTargetDescriptor descriptor, out FeatureName requiredFeature)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        if (!TryFind(typeof(TPixel), out CompositePixelRegistration r))
        {
            descriptor = default;
            requiredFeature = default;
            return false;
        }

        descriptor = r.Descriptor;
        requiredFeature = FindTexture(r.Descriptor.Format).RequiredFeature;
        return true;
    }

    /// <summary>
    /// Creates the descriptor used by an ImageSharp-owned offscreen pixel buffer.
    /// </summary>
    /// <param name="format">The physical texture format.</param>
    /// <param name="alphaRepresentation">The alpha representation stored by the target.</param>
    /// <returns>The offscreen target descriptor.</returns>
    internal static WebGPUTargetDescriptor CreateOffscreenTargetDescriptor(WebGPUTextureFormat format, PixelAlphaRepresentation alphaRepresentation)
    {
        // NormalizedByte4 exposes unit values by remapping its signed native components.
        // Offscreen SNORM targets must retain that ImageSharp pixel contract.
        WebGPUTargetNumericEncoding numericEncoding = format == WebGPUTextureFormat.Rgba8Snorm
            ? WebGPUTargetNumericEncoding.SignedUnit
            : WebGPUTargetNumericEncoding.Unit;

        return new WebGPUTargetDescriptor(format, alphaRepresentation, numericEncoding);
    }

    /// <summary>
    /// Creates the descriptor used by an externally-owned or presentable native texture.
    /// </summary>
    /// <param name="format">The physical texture format.</param>
    /// <param name="alphaRepresentation">The alpha representation stored by the target.</param>
    /// <returns>The native target descriptor.</returns>
    internal static WebGPUTargetDescriptor CreateNativeTargetDescriptor(WebGPUTextureFormat format, PixelAlphaRepresentation alphaRepresentation)
    {
        // WebGPU float surface values are ordinary floating-point colors. SNORM remains signed
        // by definition of the native texture format and therefore still requires unit remapping.
        WebGPUTargetNumericEncoding numericEncoding = format == WebGPUTextureFormat.Rgba8Snorm
            ? WebGPUTargetNumericEncoding.SignedUnit
            : WebGPUTargetNumericEncoding.Unit;

        return new WebGPUTargetDescriptor(format, alphaRepresentation, numericEncoding);
    }

    /// <summary>
    /// Resolves native format information for one public WebGPU texture format.
    /// </summary>
    /// <param name="format">The public WebGPU texture format.</param>
    /// <param name="textureFormat">Receives the native texture format.</param>
    /// <param name="requiredFeature">Receives the device feature required for storage binding.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static void GetCompositeTextureFormatInfo(
        WebGPUTextureFormat format,
        out TextureFormat textureFormat,
        out FeatureName requiredFeature)
    {
        CompositeTextureRegistration registration = FindTexture(format);
        textureFormat = registration.TextureFormat;
        requiredFeature = registration.RequiredFeature;
    }

    /// <summary>
    /// Resolves the shader-side read/write traits for a registered composite texture format.
    /// </summary>
    /// <param name="textureFormat">The native texture format. Must be one of the registered formats.</param>
    /// <returns>The shader traits registered for the format.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static CompositeTextureShaderTraits GetCompositeTextureShaderTraits(TextureFormat textureFormat)
        => FindTexture(textureFormat).ShaderTraits;

    /// <summary>
    /// Finds the registration for a CLR pixel type.
    /// </summary>
    /// <param name="pixelType">The pixel CLR type to look up.</param>
    /// <param name="registration">Receives the matching registration on success.</param>
    /// <returns><see langword="true"/> when the pixel type is registered; otherwise <see langword="false"/>.</returns>
    private static bool TryFind(Type pixelType, out CompositePixelRegistration registration)
    {
        foreach (CompositePixelRegistration r in CompositePixelRegistrations)
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

    /// <summary>
    /// Finds the registration for a public texture format. Callers must pass a registered format.
    /// </summary>
    /// <param name="format">The public texture format to look up.</param>
    /// <returns>The matching texture registration.</returns>
    private static CompositeTextureRegistration FindTexture(WebGPUTextureFormat format)
        => Array.Find(CompositeTextureRegistrations, r => r.Format == format);

    /// <summary>
    /// Finds the registration for a native texture format. Callers must pass a registered format.
    /// </summary>
    /// <param name="textureFormat">The native texture format to look up.</param>
    /// <returns>The matching texture registration.</returns>
    private static CompositeTextureRegistration FindTexture(TextureFormat textureFormat)
        => Array.Find(CompositeTextureRegistrations, r => r.TextureFormat == textureFormat);

    /// <summary>
    /// Shader-facing traits derived from one registered composite texture format.
    /// </summary>
    /// <param name="outputFormat">The WGSL storage-texture format token used for writes.</param>
    internal readonly struct CompositeTextureShaderTraits(string outputFormat)
    {
        /// <summary>
        /// Gets the WGSL storage-texture format token used for writes.
        /// </summary>
        public string OutputFormat { get; } = outputFormat;
    }

    /// <summary>
    /// Physical texture registration consumed by GPU composition setup.
    /// </summary>
    private readonly struct CompositeTextureRegistration
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="CompositeTextureRegistration"/> struct.
        /// </summary>
        /// <param name="format">The public texture format.</param>
        /// <param name="textureFormat">The matching WebGPU texture format.</param>
        /// <param name="shaderTraits">Shader-facing read/write traits for this format.</param>
        /// <param name="requiredFeature">Optional device feature required for storage binding support.</param>
        public CompositeTextureRegistration(
            WebGPUTextureFormat format,
            TextureFormat textureFormat,
            CompositeTextureShaderTraits shaderTraits,
            FeatureName requiredFeature)
        {
            this.Format = format;
            this.TextureFormat = textureFormat;
            this.ShaderTraits = shaderTraits;
            this.RequiredFeature = requiredFeature;
        }

        /// <summary>
        /// Gets the public texture format.
        /// </summary>
        public WebGPUTextureFormat Format { get; }

        /// <summary>
        /// Gets the WebGPU texture format used for this pixel type.
        /// </summary>
        public TextureFormat TextureFormat { get; }

        /// <summary>
        /// Gets the shader-facing read/write traits for this format.
        /// </summary>
        public CompositeTextureShaderTraits ShaderTraits { get; }

        /// <summary>
        /// Gets the optional device feature required for storage binding support.
        /// </summary>
        public FeatureName RequiredFeature { get; }
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
        /// <param name="descriptor">The target descriptor matching the pixel type.</param>
        public CompositePixelRegistration(Type pixelType, WebGPUTargetDescriptor descriptor)
        {
            this.PixelType = pixelType;
            this.Descriptor = descriptor;
        }

        /// <summary>
        /// Gets the CLR pixel type registered for this mapping.
        /// </summary>
        public Type PixelType { get; }

        /// <summary>
        /// Gets the target descriptor registered for this mapping.
        /// </summary>
        public WebGPUTargetDescriptor Descriptor { get; }

        /// <summary>
        /// Creates a registration record for <typeparamref name="TPixel"/>.
        /// </summary>
        /// <typeparam name="TPixel">The pixel type to register.</typeparam>
        /// <param name="format">The matching WebGPU texture format.</param>
        /// <param name="alphaRepresentation">The alpha representation stored by the pixel type.</param>
        /// <param name="numericEncoding">The pixel type's mapping between native and unit channel values.</param>
        /// <returns>The initialized registration.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static CompositePixelRegistration Create<TPixel>(
            WebGPUTextureFormat format,
            PixelAlphaRepresentation alphaRepresentation,
            WebGPUTargetNumericEncoding numericEncoding)
            where TPixel : unmanaged, IPixel<TPixel>
            => new(typeof(TPixel), new WebGPUTargetDescriptor(format, alphaRepresentation, numericEncoding));
    }
}
