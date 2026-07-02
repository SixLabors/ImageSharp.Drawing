// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Text;
using Silk.NET.WebGPU;

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// GPU stage that runs the fine rasterizer, the final pass of the pipeline: each workgroup
/// shades one 16x16 tile by interpreting the tile's command list (PTCL) written by coarse,
/// computing analytic coverage and evaluating brushes. Wraps <c>fine.wgsl</c>; only the output
/// storage-texture encoding is specialized per target format.
/// </summary>
internal static class FineAreaComputeShader
{
    /// <summary>
    /// The output texture declaration in fine.wgsl, replaced with the format-specific declaration.
    /// </summary>
    private const string OutputBindingMarker = "var output: texture_storage_2d<rgba8unorm, write>;";

    /// <summary>
    /// The output store statement in fine.wgsl, replaced with the format-specific encode-and-store statement.
    /// </summary>
    private const string OutputStoreMarker = "textureStore(output, vec2<i32>(coords), rgba_sep);";

    /// <summary>
    /// An anchor in fine.wgsl before which the format-specific encode_output function is inserted.
    /// </summary>
    private const string PremulAlphaMarker = "fn premul_alpha(rgba: vec4<f32>) -> vec4<f32> {";

    /// <summary>
    /// Specialized shader bytes per output texture format, guarded by its own monitor.
    /// </summary>
    private static readonly Dictionary<TextureFormat, byte[]> ShaderCache = [];

    /// <summary>
    /// Gets the WGSL entry point used by this shader.
    /// </summary>
    public static ReadOnlySpan<byte> EntryPoint => "main\0"u8;

    /// <summary>
    /// Gets or generates the fine-pass shader specialized for the requested output texture format.
    /// </summary>
    /// <param name="textureFormat">The output texture format to specialize the shader for.</param>
    /// <returns>The null-terminated UTF-8 WGSL source bytes for the specialized shader.</returns>
    public static byte[] GetCode(TextureFormat textureFormat)
    {
        ShaderTraits traits = GetTraits(textureFormat);

        lock (ShaderCache)
        {
            if (ShaderCache.TryGetValue(textureFormat, out byte[]? cachedCode))
            {
                return cachedCode;
            }

            string source = GeneratedWgslShaderSources.FineText;
            source = source.Replace(OutputBindingMarker, $"var output: texture_storage_2d<{traits.OutputFormat}, write>;", StringComparison.Ordinal);
            source = source.Replace(OutputStoreMarker, traits.StoreOutputStatement, StringComparison.Ordinal);
            source = source.Replace(PremulAlphaMarker, $"{traits.EncodeOutputFunction}\n\n{PremulAlphaMarker}", StringComparison.Ordinal);

            int byteCount = Encoding.UTF8.GetByteCount(source);
            byte[] code = new byte[byteCount + 1];
            _ = Encoding.UTF8.GetBytes(source, code);
            code[^1] = 0;
            ShaderCache[textureFormat] = code;
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
            EntryCount = 9,
            Entries = entries
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
    /// Resolves the WGSL specialization traits for the requested output texture format.
    /// </summary>
    /// <param name="textureFormat">The output texture format.</param>
    /// <returns>The traits describing the output declaration, encode function and store statement.</returns>
    private static ShaderTraits GetTraits(TextureFormat textureFormat)
    {
        WebGPUDrawingBackend.CompositeTextureShaderTraits compositeTraits = WebGPUDrawingBackend.GetCompositeTextureShaderTraits(textureFormat);

#pragma warning disable CS8524
        return compositeTraits.EncodingKind switch
        {
            WebGPUDrawingBackend.CompositeTextureEncodingKind.Float => CreateFloatTraits(compositeTraits.OutputFormat),
            WebGPUDrawingBackend.CompositeTextureEncodingKind.Snorm => CreateSnormTraits(compositeTraits.OutputFormat)
        };
#pragma warning restore CS8524
    }

    /// <summary>
    /// Creates traits for float-encoded output formats, where the shaded color is stored unchanged.
    /// </summary>
    /// <param name="outputFormat">The WGSL storage-texture format name.</param>
    /// <returns>The traits for the float encoding.</returns>
    private static ShaderTraits CreateFloatTraits(string outputFormat)
    {
        const string encodeOutput =
            """
            fn encode_output(color: vec4<f32>) -> vec4<f32> {
                return color;
            }
            """;

        return new ShaderTraits(
            outputFormat,
            encodeOutput,
            "textureStore(output, vec2<i32>(coords), encode_output(rgba_sep));");
    }

    /// <summary>
    /// Creates traits for snorm-encoded output formats, remapping the clamped [0, 1] color to [-1, 1].
    /// </summary>
    /// <param name="outputFormat">The WGSL storage-texture format name.</param>
    /// <returns>The traits for the snorm encoding.</returns>
    private static ShaderTraits CreateSnormTraits(string outputFormat)
    {
        const string encodeOutput =
            """
            fn encode_output(color: vec4<f32>) -> vec4<f32> {
                let clamped = clamp(color, vec4<f32>(0.0), vec4<f32>(1.0));
                return (clamped * 2.0) - vec4<f32>(1.0);
            }
            """;

        return new ShaderTraits(
            outputFormat,
            encodeOutput,
            "textureStore(output, vec2<i32>(coords), encode_output(rgba_sep));");
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
            Binding = binding,
            Visibility = ShaderStage.Compute,
            Buffer = new BufferBindingLayout
            {
                Type = type,
                HasDynamicOffset = false,
                MinBindingSize = minBindingSize
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
            Binding = binding,
            Visibility = ShaderStage.Compute,
            Buffer = new BufferBindingLayout
            {
                Type = BufferBindingType.Uniform,
                HasDynamicOffset = false,
                MinBindingSize = minBindingSize
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
            Binding = binding,
            Visibility = ShaderStage.Compute,
            StorageTexture = new StorageTextureBindingLayout
            {
                Access = StorageTextureAccess.WriteOnly,
                Format = outputTextureFormat,
                ViewDimension = TextureViewDimension.Dimension2D
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
            Binding = binding,
            Visibility = ShaderStage.Compute,
            Texture = new TextureBindingLayout
            {
                SampleType = TextureSampleType.Float,
                ViewDimension = TextureViewDimension.Dimension2D,
                Multisampled = false
            }
        };

    /// <summary>
    /// The WGSL text fragments substituted into fine.wgsl to specialize the output encoding.
    /// </summary>
    /// <param name="outputFormat">The WGSL storage-texture format name for the output binding.</param>
    /// <param name="encodeOutputFunction">The WGSL encode_output function inserted into the shader.</param>
    /// <param name="storeOutputStatement">The statement that encodes and stores the shaded color.</param>
    private readonly struct ShaderTraits(
        string outputFormat,
        string encodeOutputFunction,
        string storeOutputStatement)
    {
        /// <summary>
        /// Gets the WGSL storage-texture format name for the output binding.
        /// </summary>
        public string OutputFormat { get; } = outputFormat;

        /// <summary>
        /// Gets the WGSL encode_output function inserted into the shader.
        /// </summary>
        public string EncodeOutputFunction { get; } = encodeOutputFunction;

        /// <summary>
        /// Gets the statement that encodes and stores the shaded color.
        /// </summary>
        public string StoreOutputStatement { get; } = storeOutputStatement;
    }
}
