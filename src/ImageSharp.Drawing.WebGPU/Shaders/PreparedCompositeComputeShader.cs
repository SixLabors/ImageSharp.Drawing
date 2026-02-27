// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// Null-terminated WGSL compute shader for prepared composition batches.
/// </summary>
internal static class PreparedCompositeComputeShader
{
    private static readonly byte[] CodeBytes =
    [
        ..
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
        };

        struct DispatchConfig {
            target_width: u32,
            target_height: u32,
            tile_count_x: u32,
            tile_count_y: u32,
            tile_count: u32,
            command_count: u32,
            pad0: u32,
            pad1: u32,
        };

        @group(0) @binding(0) var coverage_texture: texture_2d<f32>;
        @group(0) @binding(1) var source_texture: texture_2d<f32>;
        @group(0) @binding(2) var<storage, read_write> destination_pixels: array<vec4<f32>>;
        @group(0) @binding(3) var<storage, read> commands: array<Params>;
        @group(0) @binding(4) var<storage, read> tile_starts: array<u32>;
        @group(0) @binding(5) var<storage, read_write> tile_counts: array<atomic<u32>>;
        @group(0) @binding(6) var<storage, read> tile_command_indices: array<u32>;
        @group(0) @binding(7) var<uniform> dispatch_config: DispatchConfig;

        fn u32_to_f32(bits: u32) -> f32 {
            return bitcast<f32>(bits);
        }

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
            if (global_id.x >= dispatch_config.target_width || global_id.y >= dispatch_config.target_height) {
                return;
            }

            let tile_width: u32 = 16u;
            let tile_height: u32 = 16u;
            let tile_x = global_id.x / tile_width;
            let tile_y = global_id.y / tile_height;
            let tile_index = (tile_y * dispatch_config.tile_count_x) + tile_x;
            let tile_command_start = tile_starts[tile_index];
            let tile_command_count = atomicLoad(&tile_counts[tile_index]);

            let dest_x = i32(global_id.x);
            let dest_y = i32(global_id.y);
            let dest_index = (global_id.y * dispatch_config.target_width) + global_id.x;
            var destination = destination_pixels[dest_index];

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
                if (dest_x >= command_min_x && dest_x < command_max_x && dest_y >= command_min_y && dest_y < command_max_y) {
                    let local_x = dest_x - command_min_x;
                    let local_y = dest_y - command_min_y;
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
                            let src_x = positive_mod(dest_x - origin_x, region_w) + region_x;
                            let src_y = positive_mod(dest_y - origin_y, region_h) + region_y;
                            brush = textureLoad(source_texture, vec2<i32>(src_x, src_y), 0);
                        }

                        let source = vec4<f32>(brush.rgb, brush.a * effective_coverage);
                        destination = compose_pixel(destination, source, command.color_blend_mode, command.alpha_composition_mode);
                    }
                }

                tile_command_offset = tile_command_offset + 1u;
            }

            destination_pixels[dest_index] = destination;
        }
        """u8,
        0
    ];

    /// <summary>
    /// Gets the null-terminated UTF-8 WGSL source bytes.
    /// </summary>
    public static ReadOnlySpan<byte> Code => CodeBytes;
}
