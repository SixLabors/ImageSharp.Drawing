// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Text;
using SixLabors.ImageSharp.PixelFormats;

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// GPU stage that runs the fine rasterizer, the final pass of the pipeline: each workgroup
/// shades one 16x16 tile by interpreting the tile's command list (PTCL) written by coarse,
/// computing analytic coverage and evaluating brushes. Wraps <c>fine.wgsl</c>; only the output
/// storage-texture encoding is specialized per target format and alpha representation.
/// </summary>
internal static class FineAreaComputeShader
{
    /// <summary>
    /// The output texture declaration in fine.wgsl, replaced with the format-specific declaration.
    /// </summary>
    private const string OutputBindingMarker = "var output: texture_storage_2d<rgba8unorm, write>;";

    /// <summary>
    /// The output store statement in fine.wgsl, replaced with the target-specific encode-and-store statement.
    /// </summary>
    private const string OutputStoreMarker = "textureStore(output, vec2<i32>(coords), rgba[i]);";

    /// <summary>
    /// An anchor in fine.wgsl before which the target conversion functions are inserted.
    /// </summary>
    private const string PremulAlphaMarker = "fn premul_alpha(rgba: vec4<f32>) -> vec4<f32> {";

    /// <summary>
    /// Specialized shader bytes per target texture format and alpha representation, guarded by its own monitor.
    /// </summary>
    private static readonly Dictionary<(TextureFormat TextureFormat, PixelAlphaRepresentation AlphaRepresentation, WebGPUTargetNumericEncoding NumericEncoding), byte[]> ShaderCache = [];

    /// <summary>
    /// Gets the WGSL entry point used by this shader.
    /// </summary>
    public static ReadOnlySpan<byte> EntryPoint => "main\0"u8;

    /// <summary>
    /// Gets or generates the fine-pass shader specialized for the requested target.
    /// </summary>
    /// <param name="textureFormat">The output texture format to specialize the shader for.</param>
    /// <param name="alphaRepresentation">The alpha representation stored by the target.</param>
    /// <param name="numericEncoding">The target's mapping between native channel values and ImageSharp unit values.</param>
    /// <returns>The null-terminated UTF-8 WGSL source bytes for the specialized shader.</returns>
    public static byte[] GetCode(
        TextureFormat textureFormat,
        PixelAlphaRepresentation alphaRepresentation,
        WebGPUTargetNumericEncoding numericEncoding)
    {
        (TextureFormat TextureFormat, PixelAlphaRepresentation AlphaRepresentation, WebGPUTargetNumericEncoding NumericEncoding) cacheKey =
            (textureFormat, alphaRepresentation, numericEncoding);
        ShaderTraits traits = GetTraits(textureFormat, alphaRepresentation, numericEncoding);

        lock (ShaderCache)
        {
            if (ShaderCache.TryGetValue(cacheKey, out byte[]? cachedCode))
            {
                return cachedCode;
            }

            string source = GeneratedWgslShaderSources.FineText;
            source = source.Replace(OutputBindingMarker, $"var output: texture_storage_2d<{traits.OutputFormat}, write>;", StringComparison.Ordinal);
            source = source.Replace(OutputStoreMarker, traits.StoreOutputStatement, StringComparison.Ordinal);
            source = source.Replace(PremulAlphaMarker, $"{traits.TargetConversionFunctions}\n\n{PremulAlphaMarker}", StringComparison.Ordinal);

            int byteCount = Encoding.UTF8.GetByteCount(source);
            byte[] code = new byte[byteCount + 1];
            _ = Encoding.UTF8.GetBytes(source, code);
            code[^1] = 0;
            ShaderCache[cacheKey] = code;
            return code;
        }
    }

    /// <summary>
    /// Creates the bind-group layout required by the fine area shader.
    /// </summary>
    /// <param name="api">The WebGPU API facade.</param>
    /// <param name="device">The device that owns the staged-scene pipelines.</param>
    /// <param name="outputTextureFormat">The storage-texture format of the output binding.</param>
    /// <param name="layout">Receives the created bind-group layout on success.</param>
    /// <param name="error">Receives the creation failure reason when layout creation fails.</param>
    /// <returns><see langword="true"/> when the bind-group layout was created successfully; otherwise, <see langword="false"/>.</returns>
    public static unsafe bool TryCreateBindGroupLayout(
        WebGPU api,
        Device* device,
        TextureFormat outputTextureFormat,
        out BindGroupLayout* layout,
        out string? error)
    {
        // Bindings match fine.wgsl:
        //   0 config uniform
        //   1 segments (read-only tile-relative segments from path_tiling)
        //   2 ptcl (read-only per-tile command lists from coarse)
        //   3 info (read-only per-draw brush info from draw_leaf)
        //   4 blend_spill (read-write scratch for clip stacks deeper than the in-shader split)
        //   5 output (write-only storage texture in the caller-specified format)
        //   6 gradients (sampled gradient ramp texture)
        //   7 image_atlas (sampled image atlas texture)
        //   8 backdrop_texture (sampled existing target contents)
        BindGroupLayoutEntry* entries = stackalloc BindGroupLayoutEntry[9];
        entries[0] = CreateUniformEntry(0, (nuint)sizeof(GpuSceneConfig));
        entries[1] = CreateStorageEntry(1, BufferBindingType.ReadOnlyStorage, 0);
        entries[2] = CreateStorageEntry(2, BufferBindingType.ReadOnlyStorage, 0);
        entries[3] = CreateStorageEntry(3, BufferBindingType.ReadOnlyStorage, 0);
        entries[4] = CreateStorageEntry(4, BufferBindingType.Storage, 0);
        entries[5] = CreateOutputTextureEntry(5, outputTextureFormat);
        entries[6] = CreateSampledTextureEntry(6);
        entries[7] = CreateSampledTextureEntry(7);
        entries[8] = CreateSampledTextureEntry(8);

        BindGroupLayoutDescriptor descriptor = new()
        {
            entryCount = 9,
            entries = entries
        };

        layout = api.DeviceCreateBindGroupLayout(device, in descriptor);
        if (layout is null)
        {
            error = "Failed to create the staged-scene fine bind-group layout.";
            return false;
        }

        error = null;
        return true;
    }

    /// <summary>
    /// Resolves the WGSL specialization traits for the requested target.
    /// </summary>
    /// <param name="textureFormat">The output texture format.</param>
    /// <param name="alphaRepresentation">The alpha representation stored by the target.</param>
    /// <param name="numericEncoding">The target's mapping between native channel values and ImageSharp unit values.</param>
    /// <returns>The traits describing the output declaration, encode function and store statement.</returns>
    private static ShaderTraits GetTraits(
        TextureFormat textureFormat,
        PixelAlphaRepresentation alphaRepresentation,
        WebGPUTargetNumericEncoding numericEncoding)
    {
        WebGPUDrawingBackend.CompositeTextureShaderTraits compositeTraits = WebGPUDrawingBackend.GetCompositeTextureShaderTraits(textureFormat);

#pragma warning disable CS8509, CS8524
        (string Decode, string Encode) numericBodies = numericEncoding switch
        {
            WebGPUTargetNumericEncoding.Unit => ("return color;", "return color;"),
            WebGPUTargetNumericEncoding.SignedUnit =>
                ("return (color + vec4<f32>(1.0)) * 0.5;", "return (color * 2.0) - vec4<f32>(1.0);")
        };

        // The CPU RecolorBrush observes a TPixel already written by earlier draws. A staged GPU
        // scene keeps those draws in f32 registers, so Recolor alone must reproduce the target's
        // physical storage conversion before comparing without reducing normal composition precision.
        string targetFormatRoundTripBody = textureFormat switch
        {
            TextureFormat.RGBA8Unorm or TextureFormat.BGRA8Unorm => "return unpack4x8unorm(pack4x8unorm(color));",
            TextureFormat.RGBA8Snorm => "return unpack4x8snorm(pack4x8snorm(color));",
            TextureFormat.RGBA16Float => "return vec4<f32>(unpack2x16float(pack2x16float(color.rg)), unpack2x16float(pack2x16float(color.ba)));"
        };

        // Fine shading uses associated colors internally. Recolor explicitly crosses the target
        // TPixel storage boundary, including associated-alpha rescaling when stored alpha quantizes.
        (string Decode, string Encode, string RecolorNativeToInternal, string RecolorStoreTarget) alphaBodies = alphaRepresentation switch
        {
            PixelAlphaRepresentation.Associated =>
                (
                    "return decode_numeric(color);",
                    "return encode_numeric(color);",
                    "return color;",
                    "if color.a <= 0.0 {\n        return vec4<f32>(0.0);\n    }\n\n    let alpha_sample = decode_numeric(round_trip_target_format(encode_numeric(vec4<f32>(0.0, 0.0, 0.0, color.a))));\n    let stored_alpha = alpha_sample.a;\n    let rgb = clamp(color.rgb * (stored_alpha / color.a), vec3<f32>(0.0), vec3<f32>(stored_alpha));\n    return decode_numeric(round_trip_target_format(encode_numeric(vec4<f32>(rgb, stored_alpha))));"),
            PixelAlphaRepresentation.Unassociated =>
                (
                    "return premul_alpha(decode_numeric(color));",
                    "let a_inv = select(0.0, 1.0 / color.a, color.a > 0.0);\n    return encode_numeric(vec4<f32>(color.rgb * a_inv, color.a));",
                    "return premul_alpha(color);",
                    "let a_inv = select(0.0, 1.0 / color.a, color.a > 0.0);\n    let native = vec4<f32>(color.rgb * a_inv, color.a);\n    return decode_numeric(round_trip_target_format(encode_numeric(native)));")
        };
#pragma warning restore CS8509, CS8524

        string targetConversionFunctions =
            $$"""
            fn decode_numeric(color: vec4<f32>) -> vec4<f32> {
                {{numericBodies.Decode}}
            }

            fn encode_numeric(color: vec4<f32>) -> vec4<f32> {
                {{numericBodies.Encode}}
            }

            fn round_trip_target_format(color: vec4<f32>) -> vec4<f32> {
                {{targetFormatRoundTripBody}}
            }

            fn decode_target(color: vec4<f32>) -> vec4<f32> {
                {{alphaBodies.Decode}}
            }

            fn encode_target(color: vec4<f32>) -> vec4<f32> {
                {{alphaBodies.Encode}}
            }

            fn recolor_native_to_internal(color: vec4<f32>) -> vec4<f32> {
                {{alphaBodies.RecolorNativeToInternal}}
            }

            fn recolor_store_target(color: vec4<f32>) -> vec4<f32> {
                {{alphaBodies.RecolorStoreTarget}}
            }

            fn decode_paint_color(color: vec4<f32>) -> vec4<f32> {
                return color;
            }

            fn pack_clip_color(color: vec4<f32>) -> vec2<u32> {
                return vec2<u32>(pack2x16float(color.rg), pack2x16float(color.ba));
            }

            fn unpack_clip_color(color: vec2<u32>) -> vec4<f32> {
                return vec4<f32>(unpack2x16float(color.x), unpack2x16float(color.y));
            }
            """;

        return new ShaderTraits(compositeTraits.OutputFormat, targetConversionFunctions, "textureStore(output, vec2<i32>(coords), encode_target(rgba[i]));");
    }

    /// <summary>
    /// Creates one compute-stage storage-buffer binding entry.
    /// </summary>
    /// <param name="binding">The WGSL binding index.</param>
    /// <param name="type">The storage-buffer access mode.</param>
    /// <param name="minBindingSize">The minimum buffer binding size in bytes, or 0 to skip validation.</param>
    /// <returns>The populated binding entry.</returns>
    private static BindGroupLayoutEntry CreateStorageEntry(uint binding, BufferBindingType type, nuint minBindingSize)
        => new()
        {
            binding = binding,
            visibility = (ulong)ShaderStage.Compute,
            buffer = new BufferBindingLayout
            {
                type = type,
                hasDynamicOffset = 0U,
                minBindingSize = minBindingSize
            }
        };

    /// <summary>
    /// Creates one compute-stage uniform-buffer binding entry.
    /// </summary>
    /// <param name="binding">The WGSL binding index.</param>
    /// <param name="minBindingSize">The minimum buffer binding size in bytes.</param>
    /// <returns>The populated binding entry.</returns>
    private static BindGroupLayoutEntry CreateUniformEntry(uint binding, nuint minBindingSize)
        => new()
        {
            binding = binding,
            visibility = (ulong)ShaderStage.Compute,
            buffer = new BufferBindingLayout
            {
                type = BufferBindingType.Uniform,
                hasDynamicOffset = 0U,
                minBindingSize = minBindingSize
            }
        };

    /// <summary>
    /// Creates the write-only storage-texture binding entry for the shaded output.
    /// </summary>
    /// <param name="binding">The WGSL binding index.</param>
    /// <param name="outputTextureFormat">The storage-texture format of the output binding.</param>
    /// <returns>The populated binding entry.</returns>
    private static BindGroupLayoutEntry CreateOutputTextureEntry(uint binding, TextureFormat outputTextureFormat)
        => new()
        {
            binding = binding,
            visibility = (ulong)ShaderStage.Compute,
            storageTexture = new StorageTextureBindingLayout
            {
                access = StorageTextureAccess.WriteOnly,
                format = outputTextureFormat,
                viewDimension = TextureViewDimension._2D
            }
        };

    /// <summary>
    /// Creates a 2D float sampled-texture binding entry (gradients, image atlas, backdrop).
    /// </summary>
    /// <param name="binding">The WGSL binding index.</param>
    /// <returns>The populated binding entry.</returns>
    private static BindGroupLayoutEntry CreateSampledTextureEntry(uint binding)
        => new()
        {
            binding = binding,
            visibility = (ulong)ShaderStage.Compute,
            texture = new TextureBindingLayout
            {
                sampleType = TextureSampleType.Float,
                viewDimension = TextureViewDimension._2D,
                multisampled = 0U,
            }
        };

    /// <summary>
    /// The WGSL text fragments substituted into fine.wgsl to specialize the output encoding.
    /// </summary>
    /// <param name="outputFormat">The WGSL storage-texture format name for the output binding.</param>
    /// <param name="targetConversionFunctions">The WGSL target conversion functions inserted into the shader.</param>
    /// <param name="storeOutputStatement">The statement that encodes and stores the shaded color.</param>
    private readonly struct ShaderTraits(
        string outputFormat,
        string targetConversionFunctions,
        string storeOutputStatement)
    {
        /// <summary>
        /// Gets the WGSL storage-texture format name for the output binding.
        /// </summary>
        public string OutputFormat { get; } = outputFormat;

        /// <summary>
        /// Gets the WGSL target conversion functions inserted into the shader.
        /// </summary>
        public string TargetConversionFunctions { get; } = targetConversionFunctions;

        /// <summary>
        /// Gets the statement that encodes and stores the shaded color.
        /// </summary>
        public string StoreOutputStatement { get; } = storeOutputStatement;
    }
}
