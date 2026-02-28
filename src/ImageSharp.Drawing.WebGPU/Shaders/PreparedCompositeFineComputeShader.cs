// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System;
using System.Collections.Generic;
using System.Text;
using Silk.NET.WebGPU;

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

internal static class PreparedCompositeFineComputeShader
{
    private static readonly object CacheSync = new();
    private static readonly Dictionary<TextureFormat, byte[]> ShaderCache = new();

    private static readonly string ShaderTemplate =
        """
        struct Params {
            destination_x: u32,
            destination_y: u32,
            destination_width: u32,
            destination_height: u32,
            coverage_offset_x: u32,
            coverage_offset_y: u32,
            target_width: u32,
            brush_type: u32,
            brush_origin_x: u32,
            brush_origin_y: u32,
            brush_region_x: u32,
            brush_region_y: u32,
            brush_region_width: u32,
            brush_region_height: u32,
            color_blend_mode: u32,
            alpha_composition_mode: u32,
            blend_percentage: u32,
            solid_r: u32,
            solid_g: u32,
            solid_b: u32,
            solid_a: u32,
            tile_emit_offset: u32,
            tile_emit_count: u32,
        };

        struct DispatchConfig {
            target_width: u32,
            target_height: u32,
            tile_count_x: u32,
            tile_count_y: u32,
            tile_count: u32,
            command_count: u32,
            source_origin_x: u32,
            source_origin_y: u32,
            output_origin_x: u32,
            output_origin_y: u32,
        };

        @group(0) @binding(0) var coverage_texture: texture_2d<f32>;
        @group(0) @binding(1) var backdrop_texture: texture_2d<__BACKDROP_TEXEL_TYPE__>;
        @group(0) @binding(2) var brush_texture: texture_2d<__BACKDROP_TEXEL_TYPE__>;
        @group(0) @binding(3) var output_texture: texture_storage_2d<__OUTPUT_FORMAT__, write>;
        @group(0) @binding(4) var<storage, read> commands: array<Params>;
        @group(0) @binding(5) var<storage, read> tile_starts: array<u32>;
        @group(0) @binding(6) var<storage, read_write> tile_counts: array<atomic<u32>>;
        @group(0) @binding(7) var<storage, read> tile_command_indices: array<u32>;
        @group(0) @binding(8) var<uniform> dispatch_config: DispatchConfig;

        fn u32_to_f32(bits: u32) -> f32 {
            return bitcast<f32>(bits);
        }

        __DECODE_TEXEL_FUNCTION__

        __ENCODE_OUTPUT_FUNCTION__

        fn unpremultiply(rgb: vec3<f32>, alpha: f32) -> vec3<f32> {
            if (alpha <= 0.0) {
                return vec3<f32>(0.0);
            }

            return rgb / alpha;
        }

        fn blend_color(backdrop: vec3<f32>, source: vec3<f32>, mode: u32) -> vec3<f32> {
            switch mode {
                case 1u: {
                    return backdrop * source;
                }
                case 2u: {
                    return backdrop + source;
                }
                case 3u: {
                    return backdrop - source;
                }
                case 4u: {
                    return 1.0 - ((1.0 - backdrop) * (1.0 - source));
                }
                case 5u: {
                    return min(backdrop, source);
                }
                case 6u: {
                    return max(backdrop, source);
                }
                case 7u: {
                    return select(
                        2.0 * backdrop * source,
                        1.0 - (2.0 * (1.0 - backdrop) * (1.0 - source)),
                        backdrop >= vec3<f32>(0.5));
                }
                case 8u: {
                    return select(
                        2.0 * backdrop * source,
                        1.0 - (2.0 * (1.0 - backdrop) * (1.0 - source)),
                        source >= vec3<f32>(0.5));
                }
                default: {
                    return source;
                }
            }
        }

        fn compose_pixel(destination_premul: vec4<f32>, source: vec4<f32>, color_mode: u32, alpha_mode: u32) -> vec4<f32> {
            let destination_alpha = destination_premul.a;
            let destination_rgb_straight = unpremultiply(destination_premul.rgb, destination_alpha);
            let source_alpha = source.a;
            let source_rgb = source.rgb;
            let source_premul = source_rgb * source_alpha;
            let forward_blend = blend_color(destination_rgb_straight, source_rgb, color_mode);
            let reverse_blend = blend_color(source_rgb, destination_rgb_straight, color_mode);
            let shared_alpha = source_alpha * destination_alpha;

            switch alpha_mode {
                case 1u: {
                    return vec4<f32>(source_premul, source_alpha);
                }
                case 2u: {
                    let premul = (destination_rgb_straight * (destination_alpha - shared_alpha)) + (forward_blend * shared_alpha);
                    return vec4<f32>(premul, destination_alpha);
                }
                case 3u: {
                    let alpha = source_alpha * destination_alpha;
                    return vec4<f32>(source_premul * destination_alpha, alpha);
                }
                case 4u: {
                    let alpha = source_alpha * (1.0 - destination_alpha);
                    return vec4<f32>(source_premul * (1.0 - destination_alpha), alpha);
                }
                case 5u: {
                    return destination_premul;
                }
                case 6u: {
                    let premul = (source_rgb * (source_alpha - shared_alpha)) + (reverse_blend * shared_alpha);
                    return vec4<f32>(premul, source_alpha);
                }
                case 7u: {
                    let alpha = destination_alpha + source_alpha - shared_alpha;
                    let premul =
                        (source_rgb * (source_alpha - shared_alpha)) +
                        (destination_rgb_straight * (destination_alpha - shared_alpha)) +
                        (reverse_blend * shared_alpha);
                    return vec4<f32>(premul, alpha);
                }
                case 8u: {
                    let alpha = destination_alpha * source_alpha;
                    return vec4<f32>(destination_premul.rgb * source_alpha, alpha);
                }
                case 9u: {
                    let alpha = destination_alpha * (1.0 - source_alpha);
                    return vec4<f32>(destination_premul.rgb * (1.0 - source_alpha), alpha);
                }
                case 10u: {
                    return vec4<f32>(0.0, 0.0, 0.0, 0.0);
                }
                case 11u: {
                    let source_term = source_premul * (1.0 - destination_alpha);
                    let destination_term = destination_premul.rgb * (1.0 - source_alpha);
                    let alpha = source_alpha * (1.0 - destination_alpha) + destination_alpha * (1.0 - source_alpha);
                    return vec4<f32>(source_term + destination_term, alpha);
                }
                default: {
                    let alpha = source_alpha + destination_alpha - shared_alpha;
                    let premul =
                        (destination_rgb_straight * (destination_alpha - shared_alpha)) +
                        (source_rgb * (source_alpha - shared_alpha)) +
                        (forward_blend * shared_alpha);
                    return vec4<f32>(premul, alpha);
                }
            }
        }

        fn positive_mod(value: i32, divisor: i32) -> i32 {
            let m = value % divisor;
            return select(m + divisor, m, m >= 0);
        }

        @compute @workgroup_size(8, 8, 1)
        fn cs_main(@builtin(global_invocation_id) global_id: vec3<u32>) {
            let tile_index = global_id.z;
            if (tile_index >= dispatch_config.tile_count) {
                return;
            }

            if (global_id.x >= 16u || global_id.y >= 16u) {
                return;
            }

            let tile_x = tile_index % dispatch_config.tile_count_x;
            let tile_y = tile_index / dispatch_config.tile_count_x;
            let dest_x = (tile_x * 16u) + global_id.x;
            let dest_y = (tile_y * 16u) + global_id.y;

            if (dest_x >= dispatch_config.target_width || dest_y >= dispatch_config.target_height) {
                return;
            }

            let source_x = i32(dest_x + dispatch_config.source_origin_x);
            let source_y = i32(dest_y + dispatch_config.source_origin_y);
            let output_x_i32 = i32(dest_x + dispatch_config.output_origin_x);
            let output_y_i32 = i32(dest_y + dispatch_config.output_origin_y);
            let source = __LOAD_BACKDROP__;
            var destination = vec4<f32>(source.rgb * source.a, source.a);
            let dest_x_i32 = i32(dest_x);
            let dest_y_i32 = i32(dest_y);

            let tile_command_start = tile_starts[tile_index];
            let tile_command_count = atomicLoad(&tile_counts[tile_index]);
            var tile_command_offset: u32 = 0u;
            loop {
                if (tile_command_offset >= tile_command_count) {
                    break;
                }

                let command_index = tile_command_indices[tile_command_start + tile_command_offset];
                let command = commands[command_index];
                let command_min_x = bitcast<i32>(command.destination_x);
                let command_min_y = bitcast<i32>(command.destination_y);
                let command_max_x = command_min_x + i32(command.destination_width);
                let command_max_y = command_min_y + i32(command.destination_height);
                if (dest_x_i32 >= command_min_x && dest_x_i32 < command_max_x && dest_y_i32 >= command_min_y && dest_y_i32 < command_max_y) {
                    let local_x = dest_x_i32 - command_min_x;
                    let local_y = dest_y_i32 - command_min_y;
                    let coverage_x = bitcast<i32>(command.coverage_offset_x) + local_x;
                    let coverage_y = bitcast<i32>(command.coverage_offset_y) + local_y;
                    let coverage_value = textureLoad(coverage_texture, vec2<i32>(coverage_x, coverage_y), 0).x;
                    if (coverage_value > 0.0) {
                        let blend_percentage = u32_to_f32(command.blend_percentage);
                        let effective_coverage = coverage_value * blend_percentage;

                        var brush = vec4<f32>(
                            u32_to_f32(command.solid_r),
                            u32_to_f32(command.solid_g),
                            u32_to_f32(command.solid_b),
                            u32_to_f32(command.solid_a));

                        if (command.brush_type == 1u) {
                            let origin_x = bitcast<i32>(command.brush_origin_x);
                            let origin_y = bitcast<i32>(command.brush_origin_y);
                            let region_x = i32(command.brush_region_x);
                            let region_y = i32(command.brush_region_y);
                            let region_w = i32(command.brush_region_width);
                            let region_h = i32(command.brush_region_height);
                            let sample_x = positive_mod(dest_x_i32 - origin_x, region_w) + region_x;
                            let sample_y = positive_mod(dest_y_i32 - origin_y, region_h) + region_y;
                            brush = __LOAD_BRUSH__;
                        }

                        let src = vec4<f32>(brush.rgb, brush.a * effective_coverage);
                        destination = compose_pixel(destination, src, command.color_blend_mode, command.alpha_composition_mode);
                    }
                }

                tile_command_offset += 1u;
            }

            let alpha = destination.a;
            let rgb = unpremultiply(destination.rgb, alpha);
            __STORE_OUTPUT__
        }
        """;

    public static bool TryGetInputSampleType(TextureFormat textureFormat, out TextureSampleType sampleType)
    {
        if (TryGetTraits(textureFormat, out ShaderTraits traits))
        {
            sampleType = traits.SampleType;
            return true;
        }

        sampleType = default;
        return false;
    }

    public static bool TryGetCode(TextureFormat textureFormat, out byte[] code, out string? error)
    {
        if (!TryGetTraits(textureFormat, out ShaderTraits traits))
        {
            code = Array.Empty<byte>();
            error = $"Prepared composite fine shader does not support texture format '{textureFormat}'.";
            return false;
        }

        lock (CacheSync)
        {
            if (ShaderCache.TryGetValue(textureFormat, out byte[]? cachedCode) && cachedCode is not null)
            {
                code = cachedCode;
                error = null;
                return true;
            }

            string source = ShaderTemplate
                .Replace("__BACKDROP_TEXEL_TYPE__", traits.BackdropTexelType, StringComparison.Ordinal)
                .Replace("__OUTPUT_FORMAT__", traits.OutputFormat, StringComparison.Ordinal)
                .Replace("__DECODE_TEXEL_FUNCTION__", traits.DecodeTexelFunction, StringComparison.Ordinal)
                .Replace("__ENCODE_OUTPUT_FUNCTION__", traits.EncodeOutputFunction, StringComparison.Ordinal)
                .Replace("__LOAD_BACKDROP__", traits.LoadBackdropExpression, StringComparison.Ordinal)
                .Replace("__LOAD_BRUSH__", traits.LoadBrushExpression, StringComparison.Ordinal)
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

    private static bool TryGetTraits(TextureFormat textureFormat, out ShaderTraits traits)
    {
        switch (textureFormat)
        {
            case TextureFormat.R8Unorm:
                traits = CreateFloatTraits("r8unorm");
                return true;
            case TextureFormat.RG8Unorm:
                traits = CreateFloatTraits("rg8unorm");
                return true;
            case TextureFormat.Rgba8Unorm:
                traits = CreateFloatTraits("rgba8unorm");
                return true;
            case TextureFormat.Bgra8Unorm:
                traits = CreateFloatTraits("bgra8unorm");
                return true;
            case TextureFormat.Rgb10A2Unorm:
                traits = CreateFloatTraits("rgb10a2unorm");
                return true;
            case TextureFormat.R16float:
                traits = CreateFloatTraits("r16float");
                return true;
            case TextureFormat.RG16float:
                traits = CreateFloatTraits("rg16float");
                return true;
            case TextureFormat.Rgba16float:
                traits = CreateFloatTraits("rgba16float");
                return true;
            case TextureFormat.Rgba32float:
                traits = CreateFloatTraits("rgba32float");
                return true;
            case TextureFormat.RG8Snorm:
                traits = CreateSnormTraits("rg8snorm");
                return true;
            case TextureFormat.Rgba8Snorm:
                traits = CreateSnormTraits("rgba8snorm");
                return true;
            case TextureFormat.Rgba8Uint:
                traits = CreateUintTraits("rgba8uint", 255F);
                return true;
            case TextureFormat.R16Uint:
                traits = CreateUintTraits("r16uint", 65535F);
                return true;
            case TextureFormat.RG16Uint:
                traits = CreateUintTraits("rg16uint", 65535F);
                return true;
            case TextureFormat.Rgba16Uint:
                traits = CreateUintTraits("rgba16uint", 65535F);
                return true;
            case TextureFormat.RG16Sint:
                traits = CreateSintTraits("rg16sint", -32768F, 32767F);
                return true;
            case TextureFormat.Rgba16Sint:
                traits = CreateSintTraits("rgba16sint", -32768F, 32767F);
                return true;
            default:
                traits = default;
                return false;
        }
    }

    private static ShaderTraits CreateFloatTraits(string outputFormat)
    {
        const string DecodeTexel =
            """
            fn decode_texel(texel: vec4<f32>) -> vec4<f32> {
                return texel;
            }
            """;

        const string EncodeOutput =
            """
            fn encode_output(color: vec4<f32>) -> vec4<f32> {
                return color;
            }
            """;

        return new ShaderTraits(
            outputFormat,
            "f32",
            TextureSampleType.Float,
            DecodeTexel,
            EncodeOutput,
            "decode_texel(textureLoad(backdrop_texture, vec2<i32>(source_x, source_y), 0))",
            "decode_texel(textureLoad(brush_texture, vec2<i32>(sample_x, sample_y), 0))",
            "textureStore(output_texture, vec2<i32>(output_x_i32, output_y_i32), encode_output(vec4<f32>(rgb, alpha)));");
    }

    private static ShaderTraits CreateSnormTraits(string outputFormat)
    {
        const string DecodeTexel =
            """
            fn decode_texel(texel: vec4<f32>) -> vec4<f32> {
                return (texel * 0.5) + vec4<f32>(0.5);
            }
            """;

        const string EncodeOutput =
            """
            fn encode_output(color: vec4<f32>) -> vec4<f32> {
                let clamped = clamp(color, vec4<f32>(0.0), vec4<f32>(1.0));
                return (clamped * 2.0) - vec4<f32>(1.0);
            }
            """;

        return new ShaderTraits(
            outputFormat,
            "f32",
            TextureSampleType.Float,
            DecodeTexel,
            EncodeOutput,
            "decode_texel(textureLoad(backdrop_texture, vec2<i32>(source_x, source_y), 0))",
            "decode_texel(textureLoad(brush_texture, vec2<i32>(sample_x, sample_y), 0))",
            "textureStore(output_texture, vec2<i32>(output_x_i32, output_y_i32), encode_output(vec4<f32>(rgb, alpha)));");
    }

    private static ShaderTraits CreateUintTraits(string outputFormat, float maxValue)
    {
        string maxVector = $"vec4<f32>({maxValue:F1}, {maxValue:F1}, {maxValue:F1}, {maxValue:F1})";
        string decodeTexel = $@"const UINT_TEXEL_MAX: vec4<f32> = {maxVector};
fn decode_texel(texel: vec4<u32>) -> vec4<f32> {{
    return vec4<f32>(texel) / UINT_TEXEL_MAX;
}}";
        const string EncodeOutput =
            """
            fn encode_output(color: vec4<f32>) -> vec4<u32> {
                let clamped = clamp(color, vec4<f32>(0.0), vec4<f32>(1.0));
                return vec4<u32>(round(clamped * UINT_TEXEL_MAX));
            }
            """;

        return new ShaderTraits(
            outputFormat,
            "u32",
            TextureSampleType.Uint,
            decodeTexel,
            EncodeOutput,
            "decode_texel(textureLoad(backdrop_texture, vec2<i32>(source_x, source_y), 0))",
            "decode_texel(textureLoad(brush_texture, vec2<i32>(sample_x, sample_y), 0))",
            "textureStore(output_texture, vec2<i32>(output_x_i32, output_y_i32), encode_output(vec4<f32>(rgb, alpha)));");
    }

    private static ShaderTraits CreateSintTraits(string outputFormat, float minValue, float maxValue)
    {
        string minVector = $"vec4<f32>({minValue:F1}, {minValue:F1}, {minValue:F1}, {minValue:F1})";
        string maxVector = $"vec4<f32>({maxValue:F1}, {maxValue:F1}, {maxValue:F1}, {maxValue:F1})";
        string decodeTexel = $@"const SINT_TEXEL_MIN: vec4<f32> = {minVector};
const SINT_TEXEL_MAX: vec4<f32> = {maxVector};
const SINT_TEXEL_RANGE: vec4<f32> = SINT_TEXEL_MAX - SINT_TEXEL_MIN;
fn decode_texel(texel: vec4<i32>) -> vec4<f32> {{
    return (vec4<f32>(texel) - SINT_TEXEL_MIN) / SINT_TEXEL_RANGE;
}}";
        const string EncodeOutput =
            """
            fn encode_output(color: vec4<f32>) -> vec4<i32> {
                let clamped = clamp(color, vec4<f32>(0.0), vec4<f32>(1.0));
                return vec4<i32>(round((clamped * SINT_TEXEL_RANGE) + SINT_TEXEL_MIN));
            }
            """;

        return new ShaderTraits(
            outputFormat,
            "i32",
            TextureSampleType.Sint,
            decodeTexel,
            EncodeOutput,
            "decode_texel(textureLoad(backdrop_texture, vec2<i32>(source_x, source_y), 0))",
            "decode_texel(textureLoad(brush_texture, vec2<i32>(sample_x, sample_y), 0))",
            "textureStore(output_texture, vec2<i32>(output_x_i32, output_y_i32), encode_output(vec4<f32>(rgb, alpha)));");
    }

    private readonly struct ShaderTraits(
        string outputFormat,
        string backdropTexelType,
        TextureSampleType sampleType,
        string decodeTexelFunction,
        string encodeOutputFunction,
        string loadBackdropExpression,
        string loadBrushExpression,
        string storeOutputStatement)
    {
        public string OutputFormat { get; } = outputFormat;

        public string BackdropTexelType { get; } = backdropTexelType;

        public TextureSampleType SampleType { get; } = sampleType;

        public string DecodeTexelFunction { get; } = decodeTexelFunction;

        public string EncodeOutputFunction { get; } = encodeOutputFunction;

        public string LoadBackdropExpression { get; } = loadBackdropExpression;

        public string LoadBrushExpression { get; } = loadBrushExpression;

        public string StoreOutputStatement { get; } = storeOutputStatement;
    }
}
