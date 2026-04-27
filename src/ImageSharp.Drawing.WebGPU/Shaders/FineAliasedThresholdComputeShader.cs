// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Text;
using Silk.NET.WebGPU;

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// Final staged-scene fine pass for thresholded aliased rasterization.
/// The WGSL stays shared with the analytic fine pass, but coverage is quantized
/// immediately after path evaluation so brush evaluation and composition still
/// reuse the same scene encoding and blend logic.
/// </summary>
internal static class FineAliasedThresholdComputeShader
{
    private const string OutputBindingMarker = "var output: texture_storage_2d<rgba8unorm, write>;";
    private const string OutputStoreMarker = "textureStore(output, vec2<i32>(coords), rgba_sep);";
    private const string PremulAlphaMarker = "fn premul_alpha(rgba: vec4<f32>) -> vec4<f32> {";
    private const string WorkgroupSizeMarker = "// The X size should be 16 / PIXELS_PER_THREAD";
    private const string AnalyticFillCallMarker = "                fill_path(fill, local_xy, &area);";
    private const string ThresholdHelper =
        """
        fn apply_aliased_threshold(result: ptr<function, array<f32, PIXELS_PER_THREAD>>) {
            let threshold = config.fine_coverage_threshold;
            for (var i = 0u; i < PIXELS_PER_THREAD; i += 1u) {
                (*result)[i] = select(0.0, 1.0, (*result)[i] >= threshold);
            }
        }

        """;

    private static readonly object CacheSync = new();
    private static readonly Dictionary<TextureFormat, byte[]> ShaderCache = [];

    /// <summary>
    /// Gets the compute entry point exported by the aliased fine shader.
    /// </summary>
    public static ReadOnlySpan<byte> EntryPoint => "main\0"u8;

    /// <summary>
    /// Gets the texture-format-specialized WGSL for the aliased threshold fine pass.
    /// </summary>
    public static bool TryGetCode(TextureFormat textureFormat, out byte[] code, out string? error)
    {
        if (!TryGetTraits(textureFormat, out ShaderTraits traits))
        {
            code = [];
            error = $"Scene aliased-threshold fine shader does not support texture format '{textureFormat}'.";
            return false;
        }

        lock (CacheSync)
        {
            if (ShaderCache.TryGetValue(textureFormat, out byte[]? cachedCode))
            {
                code = cachedCode;
                error = null;
                return true;
            }

            string source = GeneratedWgslShaderSources.FineText;
            source = source.Replace(OutputBindingMarker, $"var output: texture_storage_2d<{traits.OutputFormat}, write>;", StringComparison.Ordinal);
            source = source.Replace(OutputStoreMarker, traits.StoreOutputStatement, StringComparison.Ordinal);
            source = source.Replace(PremulAlphaMarker, $"{traits.EncodeOutputFunction}\n\n{PremulAlphaMarker}", StringComparison.Ordinal);
            source = source.Replace(WorkgroupSizeMarker, $"{ThresholdHelper}{WorkgroupSizeMarker}", StringComparison.Ordinal);
            source = source.Replace(AnalyticFillCallMarker, $"{AnalyticFillCallMarker}\n                apply_aliased_threshold(&area);", StringComparison.Ordinal);

            int byteCount = Encoding.UTF8.GetByteCount(source);
            code = new byte[byteCount + 1];
            _ = Encoding.UTF8.GetBytes(source, code);
            code[^1] = 0;
            ShaderCache[textureFormat] = code;
        }

        error = null;
        return true;
    }

    /// <summary>
    /// Creates the bind-group layout consumed by the aliased threshold fine pass.
    /// </summary>
    public static unsafe bool TryCreateBindGroupLayout(
        WebGPU api,
        Device* device,
        TextureFormat outputTextureFormat,
        out BindGroupLayout* layout,
        out string? error)
        => FineAreaComputeShader.TryCreateBindGroupLayout(api, device, outputTextureFormat, out layout, out error);

    private static bool TryGetTraits(TextureFormat textureFormat, out ShaderTraits traits)
    {
        if (!WebGPUDrawingBackend.TryGetCompositeTextureShaderTraits(textureFormat, out WebGPUDrawingBackend.CompositeTextureShaderTraits compositeTraits))
        {
            traits = default;
            return false;
        }

        traits = compositeTraits.EncodingKind switch
        {
            WebGPUDrawingBackend.CompositeTextureEncodingKind.Float => CreateFloatTraits(compositeTraits.OutputFormat),
            WebGPUDrawingBackend.CompositeTextureEncodingKind.Snorm => CreateSnormTraits(compositeTraits.OutputFormat),
            WebGPUDrawingBackend.CompositeTextureEncodingKind.Uint8 => CreateUintTraits(compositeTraits.OutputFormat, 255F),
            WebGPUDrawingBackend.CompositeTextureEncodingKind.Uint16 => CreateUintTraits(compositeTraits.OutputFormat, 65535F),
            WebGPUDrawingBackend.CompositeTextureEncodingKind.Sint16 => CreateSintTraits(compositeTraits.OutputFormat, -32768F, 32767F),
            _ => default
        };

        return true;
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
