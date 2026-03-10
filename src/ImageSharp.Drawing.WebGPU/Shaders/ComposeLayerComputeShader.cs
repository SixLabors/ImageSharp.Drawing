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
        fn cs_main(@builtin(global_invocation_id) gid: vec3<u32>) {
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
                let backdrop = decode_texel(__LOAD_BACKDROP__);
                let alpha = backdrop.a;
                let rgb = unpremultiply(backdrop.rgb, alpha);
                __STORE_OUTPUT__
                return;
            }

            let backdrop = decode_texel(__LOAD_BACKDROP__);
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

    /// <summary>
    /// Gets the null-terminated WGSL source for the layer composite shader variant.
    /// </summary>
    public static bool TryGetCode(TextureFormat textureFormat, out byte[] code, out string? error)
    {
        if (!CompositeComputeShader.TryGetInputSampleType(textureFormat, out _))
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

            byte[] sourceBytes = Encoding.UTF8.GetBytes(source);
            code = new byte[sourceBytes.Length + 1];
            sourceBytes.CopyTo(code, 0);
            code[^1] = 0;
            ShaderCache[textureFormat] = code;
        }

        error = null;
        return true;
    }

    private static LayerShaderTraits GetTraits(TextureFormat textureFormat)
    {
        return textureFormat switch
        {
            TextureFormat.R8Unorm => CreateFloatTraits("r8unorm"),
            TextureFormat.RG8Unorm => CreateFloatTraits("rg8unorm"),
            TextureFormat.Rgba8Unorm => CreateFloatTraits("rgba8unorm"),
            TextureFormat.Bgra8Unorm => CreateFloatTraits("bgra8unorm"),
            TextureFormat.Rgb10A2Unorm => CreateFloatTraits("rgb10a2unorm"),
            TextureFormat.R16float => CreateFloatTraits("r16float"),
            TextureFormat.RG16float => CreateFloatTraits("rg16float"),
            TextureFormat.Rgba16float => CreateFloatTraits("rgba16float"),
            TextureFormat.Rgba32float => CreateFloatTraits("rgba32float"),
            TextureFormat.RG8Snorm => CreateSnormTraits("rg8snorm"),
            TextureFormat.Rgba8Snorm => CreateSnormTraits("rgba8snorm"),
            _ => CreateFloatTraits("rgba8unorm"),
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
