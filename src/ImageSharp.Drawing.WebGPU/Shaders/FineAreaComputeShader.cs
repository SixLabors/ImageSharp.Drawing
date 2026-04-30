// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Text;
using Silk.NET.WebGPU;

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// Final staged-scene fine pass driven from the source-of-truth WGSL stage.
/// Only the output storage texture encoding is specialized per target format.
/// </summary>
internal static class FineAreaComputeShader
{
    private const string OutputBindingMarker = "var output: texture_storage_2d<rgba8unorm, write>;";
    private const string OutputStoreMarker = "textureStore(output, vec2<i32>(coords), rgba_sep);";
    private const string PremulAlphaMarker = "fn premul_alpha(rgba: vec4<f32>) -> vec4<f32> {";

    private static readonly Dictionary<TextureFormat, byte[]> ShaderCache = [];

    /// <summary>
    /// Gets the WGSL entry point used by this shader.
    /// </summary>
    public static ReadOnlySpan<byte> EntryPoint => "main\0"u8;

    /// <summary>
    /// Gets or generates the fine-pass shader specialized for the requested output texture format.
    /// </summary>
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
    public static unsafe bool TryCreateBindGroupLayout(
        WebGPU api,
        Device* device,
        TextureFormat outputTextureFormat,
        out BindGroupLayout* layout,
        out string? error)
    {
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

    private static ShaderTraits GetTraits(TextureFormat textureFormat)
    {
        WebGPUDrawingBackend.CompositeTextureShaderTraits compositeTraits = WebGPUDrawingBackend.GetCompositeTextureShaderTraits(textureFormat);

#pragma warning disable CS8524
        return compositeTraits.EncodingKind switch
        {
            WebGPUDrawingBackend.CompositeTextureEncodingKind.Float => CreateFloatTraits(compositeTraits.OutputFormat),
            WebGPUDrawingBackend.CompositeTextureEncodingKind.Snorm => CreateSnormTraits(compositeTraits.OutputFormat),
            WebGPUDrawingBackend.CompositeTextureEncodingKind.Uint8 => CreateUintTraits(compositeTraits.OutputFormat, 255F),
            WebGPUDrawingBackend.CompositeTextureEncodingKind.Uint16 => CreateUintTraits(compositeTraits.OutputFormat, 65535F),
            WebGPUDrawingBackend.CompositeTextureEncodingKind.Sint16 => CreateSintTraits(compositeTraits.OutputFormat, -32768F, 32767F)
        };
#pragma warning restore CS8524
    }

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

    private static ShaderTraits CreateUintTraits(string outputFormat, float maxValue)
    {
        string maxVector = $"vec4<f32>({maxValue:F1}, {maxValue:F1}, {maxValue:F1}, {maxValue:F1})";
        const string encodeOutput =
            """
            const UINT_TEXEL_MAX: vec4<f32> = __UINT_TEXEL_MAX__;
            fn encode_output(color: vec4<f32>) -> vec4<u32> {
                let clamped = clamp(color, vec4<f32>(0.0), vec4<f32>(1.0));
                return vec4<u32>(round(clamped * UINT_TEXEL_MAX));
            }
            """;

        return new ShaderTraits(
            outputFormat,
            encodeOutput.Replace("__UINT_TEXEL_MAX__", maxVector, StringComparison.Ordinal),
            "textureStore(output, vec2<i32>(coords), encode_output(rgba_sep));");
    }

    private static ShaderTraits CreateSintTraits(string outputFormat, float minValue, float maxValue)
    {
        string minVector = $"vec4<f32>({minValue:F1}, {minValue:F1}, {minValue:F1}, {minValue:F1})";
        string maxVector = $"vec4<f32>({maxValue:F1}, {maxValue:F1}, {maxValue:F1}, {maxValue:F1})";
        string encodeOutput =
            $$"""
            const SINT_TEXEL_MIN: vec4<f32> = {{minVector}};
            const SINT_TEXEL_MAX: vec4<f32> = {{maxVector}};
            const SINT_TEXEL_RANGE: vec4<f32> = SINT_TEXEL_MAX - SINT_TEXEL_MIN;
            fn encode_output(color: vec4<f32>) -> vec4<i32> {
                let clamped = clamp(color, vec4<f32>(0.0), vec4<f32>(1.0));
                return vec4<i32>(round((clamped * SINT_TEXEL_RANGE) + SINT_TEXEL_MIN));
            }
            """;

        return new ShaderTraits(
            outputFormat,
            encodeOutput,
            "textureStore(output, vec2<i32>(coords), encode_output(rgba_sep));");
    }

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

    private readonly struct ShaderTraits(
        string outputFormat,
        string encodeOutputFunction,
        string storeOutputStatement)
    {
        public string OutputFormat { get; } = outputFormat;

        public string EncodeOutputFunction { get; } = encodeOutputFunction;

        public string StoreOutputStatement { get; } = storeOutputStatement;
    }
}
