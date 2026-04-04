// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Text;
using Silk.NET.WebGPU;

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// GPU compute shader that composites a source layer texture onto a destination texture
/// using configurable blend mode, alpha composition mode, and opacity.
/// </summary>
internal static class ComposeLayerComputeShader
{
    private static readonly object CacheSync = new();
    private static readonly Dictionary<TextureFormat, byte[]> ShaderCache = [];

    private static readonly string ShaderTemplate =
        """
        struct LayerConfig {
            source_width: u32,
            source_height: u32,
            dest_offset_x: i32,
            dest_offset_y: i32,
            color_blend_mode: u32,
            alpha_composition_mode: u32,
            blend_percentage: u32,
            _padding: u32,
        };

        @group(0) @binding(0) var source_texture: texture_2d<__TEXEL_TYPE__>;
        @group(0) @binding(1) var backdrop_texture: texture_2d<__TEXEL_TYPE__>;
        @group(0) @binding(2) var output_texture: texture_storage_2d<__OUTPUT_FORMAT__, write>;
        @group(0) @binding(3) var<uniform> config: LayerConfig;

        __DECODE_TEXEL_FUNCTION__

        __ENCODE_OUTPUT_FUNCTION__

        __BLEND_AND_COMPOSE__

        @compute @workgroup_size(16, 16, 1)
        fn main(@builtin(global_invocation_id) gid: vec3<u32>) {
            // Output coordinates are in local output-texture space.
            let out_x = i32(gid.x);
            let out_y = i32(gid.y);

            // Destination coordinates map into the full backdrop texture.
            let dest_x = out_x + config.dest_offset_x;
            let dest_y = out_y + config.dest_offset_y;

            let dest_dims = textureDimensions(backdrop_texture);
            if (dest_x < 0 || dest_y < 0 || u32(dest_x) >= dest_dims.x || u32(dest_y) >= dest_dims.y) {
                return;
            }

            let src_x = out_x;
            let src_y = out_y;
            if (u32(src_x) >= config.source_width || u32(src_y) >= config.source_height) {
                // Outside layer bounds — pass through the backdrop.
                let backdrop_raw = decode_texel(__LOAD_BACKDROP__);
                let backdrop = vec4<f32>(backdrop_raw.rgb * backdrop_raw.a, backdrop_raw.a);
                let alpha = backdrop.a;
                let rgb = unpremultiply(backdrop.rgb, alpha);
                __STORE_OUTPUT__
                return;
            }

            let backdrop_raw = decode_texel(__LOAD_BACKDROP__);
            let backdrop = vec4<f32>(backdrop_raw.rgb * backdrop_raw.a, backdrop_raw.a);
            let source_raw = decode_texel(__LOAD_SOURCE__);

            // Apply layer opacity.
            let opacity = bitcast<f32>(config.blend_percentage);
            let source = vec4<f32>(source_raw.rgb, source_raw.a * opacity);

            let result = compose_pixel(backdrop, source, config.color_blend_mode, config.alpha_composition_mode);
            let alpha = result.a;
            let rgb = unpremultiply(result.rgb, alpha);
            __STORE_OUTPUT__
        }
        """;

    public static ReadOnlySpan<byte> EntryPoint => "main\0"u8;

    /// <summary>
    /// Gets the null-terminated WGSL source for the layer composite shader variant.
    /// </summary>
    public static bool TryGetCode(TextureFormat textureFormat, out byte[] code, out string? error)
    {
        if (!WebGPUDrawingBackend.TryGetCompositeTextureShaderTraits(textureFormat, out _))
        {
            code = [];
            error = $"Layer composite shader does not support texture format '{textureFormat}'.";
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

            LayerShaderTraits traits = GetTraits(textureFormat);
            string source = ShaderTemplate
                .Replace("__TEXEL_TYPE__", traits.TexelType, StringComparison.Ordinal)
                .Replace("__OUTPUT_FORMAT__", traits.OutputFormat, StringComparison.Ordinal)
                .Replace("__DECODE_TEXEL_FUNCTION__", traits.DecodeTexelFunction, StringComparison.Ordinal)
                .Replace("__ENCODE_OUTPUT_FUNCTION__", traits.EncodeOutputFunction, StringComparison.Ordinal)
                .Replace("__BLEND_AND_COMPOSE__", CompositionShaderSnippets.BlendAndCompose, StringComparison.Ordinal)
                .Replace("__LOAD_BACKDROP__", traits.LoadBackdropExpression, StringComparison.Ordinal)
                .Replace("__LOAD_SOURCE__", traits.LoadSourceExpression, StringComparison.Ordinal)
                .Replace("__STORE_OUTPUT__", traits.StoreOutputStatement, StringComparison.Ordinal);

            int byteCount = Encoding.UTF8.GetByteCount(source);
            code = new byte[byteCount + 1];
            _ = Encoding.UTF8.GetBytes(source, code);
            code[^1] = 0;
            ShaderCache[textureFormat] = code;
        }

        error = null;
        return true;
    }

    private static LayerShaderTraits GetTraits(TextureFormat textureFormat)
    {
        if (!WebGPUDrawingBackend.TryGetCompositeTextureShaderTraits(textureFormat, out WebGPUDrawingBackend.CompositeTextureShaderTraits traits))
        {
            return CreateFloatTraits("rgba8unorm");
        }

        return traits.EncodingKind switch
        {
            WebGPUDrawingBackend.CompositeTextureEncodingKind.Float => CreateFloatTraits(traits.OutputFormat),
            WebGPUDrawingBackend.CompositeTextureEncodingKind.Snorm => CreateSnormTraits(traits.OutputFormat),
            WebGPUDrawingBackend.CompositeTextureEncodingKind.Uint8 => CreateUintTraits(traits.OutputFormat, 255F),
            WebGPUDrawingBackend.CompositeTextureEncodingKind.Uint16 => CreateUintTraits(traits.OutputFormat, 65535F),
            WebGPUDrawingBackend.CompositeTextureEncodingKind.Sint16 => CreateSintTraits(traits.OutputFormat, -32768F, 32767F),
            _ => CreateFloatTraits(traits.OutputFormat),
        };
    }

    private static LayerShaderTraits CreateFloatTraits(string outputFormat)
    {
        const string decodeTexel =
            """
            fn decode_texel(texel: vec4<f32>) -> vec4<f32> {
                return texel;
            }
            """;

        const string encodeOutput =
            """
            fn encode_output(color: vec4<f32>) -> vec4<f32> {
                return color;
            }
            """;

        return new LayerShaderTraits(
            outputFormat,
            "f32",
            decodeTexel,
            encodeOutput,
            "textureLoad(backdrop_texture, vec2<i32>(dest_x, dest_y), 0)",
            "textureLoad(source_texture, vec2<i32>(src_x, src_y), 0)",
            "textureStore(output_texture, vec2<i32>(out_x, out_y), encode_output(vec4<f32>(rgb, alpha)));");
    }

    private static LayerShaderTraits CreateSnormTraits(string outputFormat)
    {
        const string decodeTexel =
            """
            fn decode_texel(texel: vec4<f32>) -> vec4<f32> {
                return (texel * 0.5) + vec4<f32>(0.5);
            }
            """;

        const string encodeOutput =
            """
            fn encode_output(color: vec4<f32>) -> vec4<f32> {
                let clamped = clamp(color, vec4<f32>(0.0), vec4<f32>(1.0));
                return (clamped * 2.0) - vec4<f32>(1.0);
            }
            """;

        return new LayerShaderTraits(
            outputFormat,
            "f32",
            decodeTexel,
            encodeOutput,
            "textureLoad(backdrop_texture, vec2<i32>(dest_x, dest_y), 0)",
            "textureLoad(source_texture, vec2<i32>(src_x, src_y), 0)",
            "textureStore(output_texture, vec2<i32>(out_x, out_y), encode_output(vec4<f32>(rgb, alpha)));");
    }

    private static LayerShaderTraits CreateUintTraits(string outputFormat, float maxValue)
    {
        string maxVector = $"vec4<f32>({maxValue:F1}, {maxValue:F1}, {maxValue:F1}, {maxValue:F1})";
        string decodeTexel = $@"const UINT_TEXEL_MAX: vec4<f32> = {maxVector};
fn decode_texel(texel: vec4<u32>) -> vec4<f32> {{
    return vec4<f32>(texel) / UINT_TEXEL_MAX;
}}";
        const string encodeOutput =
            """
            fn encode_output(color: vec4<f32>) -> vec4<u32> {
                let clamped = clamp(color, vec4<f32>(0.0), vec4<f32>(1.0));
                return vec4<u32>(round(clamped * UINT_TEXEL_MAX));
            }
            """;

        return new LayerShaderTraits(
            outputFormat,
            "u32",
            decodeTexel,
            encodeOutput,
            "textureLoad(backdrop_texture, vec2<i32>(dest_x, dest_y), 0)",
            "textureLoad(source_texture, vec2<i32>(src_x, src_y), 0)",
            "textureStore(output_texture, vec2<i32>(out_x, out_y), encode_output(vec4<f32>(rgb, alpha)));");
    }

    private static LayerShaderTraits CreateSintTraits(string outputFormat, float minValue, float maxValue)
    {
        string minVector = $"vec4<f32>({minValue:F1}, {minValue:F1}, {minValue:F1}, {minValue:F1})";
        string maxVector = $"vec4<f32>({maxValue:F1}, {maxValue:F1}, {maxValue:F1}, {maxValue:F1})";
        string decodeTexel = $@"const SINT_TEXEL_MIN: vec4<f32> = {minVector};
const SINT_TEXEL_MAX: vec4<f32> = {maxVector};
const SINT_TEXEL_RANGE: vec4<f32> = SINT_TEXEL_MAX - SINT_TEXEL_MIN;
fn decode_texel(texel: vec4<i32>) -> vec4<f32> {{
    return (vec4<f32>(texel) - SINT_TEXEL_MIN) / SINT_TEXEL_RANGE;
}}";
        const string encodeOutput =
            """
            fn encode_output(color: vec4<f32>) -> vec4<i32> {
                let clamped = clamp(color, vec4<f32>(0.0), vec4<f32>(1.0));
                return vec4<i32>(round((clamped * SINT_TEXEL_RANGE) + SINT_TEXEL_MIN));
            }
            """;

        return new LayerShaderTraits(
            outputFormat,
            "i32",
            decodeTexel,
            encodeOutput,
            "textureLoad(backdrop_texture, vec2<i32>(dest_x, dest_y), 0)",
            "textureLoad(source_texture, vec2<i32>(src_x, src_y), 0)",
            "textureStore(output_texture, vec2<i32>(out_x, out_y), encode_output(vec4<f32>(rgb, alpha)));");
    }

    private readonly struct LayerShaderTraits(
        string outputFormat,
        string texelType,
        string decodeTexelFunction,
        string encodeOutputFunction,
        string loadBackdropExpression,
        string loadSourceExpression,
        string storeOutputStatement)
    {
        public string OutputFormat { get; } = outputFormat;

        public string TexelType { get; } = texelType;

        public string DecodeTexelFunction { get; } = decodeTexelFunction;

        public string EncodeOutputFunction { get; } = encodeOutputFunction;

        public string LoadBackdropExpression { get; } = loadBackdropExpression;

        public string LoadSourceExpression { get; } = loadSourceExpression;

        public string StoreOutputStatement { get; } = storeOutputStatement;
    }
}
