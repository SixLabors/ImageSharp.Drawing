// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// WGSL compute shader for tiled brush composition over prepared path coverage.
/// </summary>
/// <remarks>
/// The shader resolves tile-local command ranges, samples brush/source data, applies color blending
/// and Porter-Duff alpha composition, and writes updated destination pixels into storage.
/// </remarks>
internal static class TiledCompositeComputeShader
{
    /// <summary>
    /// Gets the UTF-8 WGSL source bytes used by the tiled composite compute pipeline.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The literal intentionally includes a trailing U+0000 null terminator before the <see langword="u8"/> suffix.
    /// </para>
    /// <para>
    /// Native WebGPU shader creation expects WGSL as a null-terminated byte pointer. The explicit
    /// terminator keeps shader bytes as a compile-time constant and avoids runtime append/copy overhead.
    /// </para>
    /// </remarks>
    public static ReadOnlySpan<byte> Code =>
        """
        struct CompositeCommand {
            source_offset_x: i32,
            source_offset_y: i32,
            destination_x: i32,
            destination_y: i32,
            destination_width: i32,
            destination_height: i32,
            blend_percentage: f32,
            color_blending_mode: i32,
            alpha_composition_mode: i32,
            brush_data_index: i32,
            _pad0: i32,
            _pad1: i32,
        };

        struct TileRange {
            start_index: u32,
            count: u32,
        };

        struct BrushData {
            source_region_x: i32,
            source_region_y: i32,
            source_region_width: i32,
            source_region_height: i32,
            brush_origin_x: i32,
            brush_origin_y: i32,
            source_layer: i32,
            _pad0: i32,
        };

        struct TiledCompositeParams {
            destination_width: i32,
            destination_height: i32,
            tiles_x: i32,
            tile_size: i32,
        };

        @group(0) @binding(0)
        var coverage: texture_2d<f32>;

        @group(0) @binding(1)
        var<storage, read> commands: array<CompositeCommand>;

        @group(0) @binding(2)
        var<storage, read> tile_ranges: array<TileRange>;

        @group(0) @binding(3)
        var<storage, read> tile_command_indices: array<u32>;

        @group(0) @binding(4)
        var<storage, read> brushes: array<BrushData>;

        @group(0) @binding(5)
        var source_layers: texture_2d_array<f32>;

        @group(0) @binding(6)
        var<storage, read_write> destination_pixels: array<vec4<f32>>;

        @group(0) @binding(7)
        var<uniform> params: TiledCompositeParams;

        fn overlay_value(backdrop: f32, source: f32) -> f32 {
            if (backdrop <= 0.5) {
                return 2.0 * backdrop * source;
            }

            return 1.0 - (2.0 * (1.0 - source) * (1.0 - backdrop));
        }

        fn blend_color(backdrop: vec3<f32>, source: vec3<f32>, color_mode: i32) -> vec3<f32> {
            switch color_mode {
                case 0 {
                    return source;
                }

                case 1 {
                    return backdrop * source;
                }

                case 2 {
                    return min(vec3<f32>(1.0), backdrop + source);
                }

                case 3 {
                    return max(vec3<f32>(0.0), backdrop - source);
                }

                case 4 {
                    return vec3<f32>(1.0) - ((vec3<f32>(1.0) - backdrop) * (vec3<f32>(1.0) - source));
                }

                case 5 {
                    return min(backdrop, source);
                }

                case 6 {
                    return max(backdrop, source);
                }

                case 7 {
                    return vec3<f32>(
                        overlay_value(backdrop.r, source.r),
                        overlay_value(backdrop.g, source.g),
                        overlay_value(backdrop.b, source.b));
                }

                case 8 {
                    return vec3<f32>(
                        overlay_value(source.r, backdrop.r),
                        overlay_value(source.g, backdrop.g),
                        overlay_value(source.b, backdrop.b));
                }

                default {
                    return source;
                }
            }
        }

        fn unpremultiply(premultiplied_rgb: vec3<f32>, alpha: f32) -> vec4<f32> {
            let clamped_alpha = clamp(alpha, 0.0, 1.0);
            if (clamped_alpha <= 0.0) {
                return vec4<f32>(0.0, 0.0, 0.0, 0.0);
            }

            let color = clamp(
                premultiplied_rgb / clamped_alpha,
                vec3<f32>(0.0),
                vec3<f32>(1.0));
            return vec4<f32>(color, clamped_alpha);
        }

        fn compose_over(destination: vec4<f32>, source: vec4<f32>, blend: vec3<f32>) -> vec4<f32> {
            let source_weight = source.a;
            let destination_weight = destination.a;
            let blend_weight = source_weight * destination_weight;
            let destination_only_weight = destination_weight - blend_weight;
            let source_only_weight = source_weight - blend_weight;
            let alpha = destination_only_weight + source_weight;
            let premultiplied_color =
                (destination.rgb * destination_only_weight) +
                (source.rgb * source_only_weight) +
                (blend * blend_weight);
            return unpremultiply(premultiplied_color, alpha);
        }

        fn compose_atop(destination: vec4<f32>, source: vec4<f32>, blend: vec3<f32>) -> vec4<f32> {
            let source_weight = source.a;
            let destination_weight = destination.a;
            let blend_weight = source_weight * destination_weight;
            let destination_only_weight = destination_weight - blend_weight;
            let premultiplied_color =
                (destination.rgb * destination_only_weight) +
                (blend * blend_weight);
            return unpremultiply(premultiplied_color, destination_weight);
        }

        fn compose_in(destination: vec4<f32>, source: vec4<f32>) -> vec4<f32> {
            let alpha = destination.a * source.a;
            return unpremultiply(source.rgb * alpha, alpha);
        }

        fn compose_out(destination: vec4<f32>, source: vec4<f32>) -> vec4<f32> {
            let alpha = (1.0 - destination.a) * source.a;
            return unpremultiply(source.rgb * alpha, alpha);
        }

        fn compose_xor(destination: vec4<f32>, source: vec4<f32>) -> vec4<f32> {
            let source_weight = 1.0 - destination.a;
            let destination_weight = 1.0 - source.a;
            let alpha = (source.a * source_weight) + (destination.a * destination_weight);
            let premultiplied_color =
                (source.a * source.rgb * source_weight) +
                (destination.a * destination.rgb * destination_weight);
            return unpremultiply(premultiplied_color, alpha);
        }

        fn compose_pixel(
            destination: vec4<f32>,
            source: vec4<f32>,
            blend_percentage: f32,
            color_mode: i32,
            alpha_mode: i32) -> vec4<f32> {
            let source_alpha = clamp(source.a * blend_percentage, 0.0, 1.0);
            let source_color = clamp(source.rgb, vec3<f32>(0.0), vec3<f32>(1.0));
            let source_with_opacity = vec4<f32>(source_color, source_alpha);
            let destination_color = clamp(destination.rgb, vec3<f32>(0.0), vec3<f32>(1.0));
            let destination_alpha = clamp(destination.a, 0.0, 1.0);
            let destination_pixel = vec4<f32>(destination_color, destination_alpha);

            switch alpha_mode {
                case 0 {
                    let blend = blend_color(destination_color, source_color, color_mode);
                    return compose_over(destination_pixel, source_with_opacity, blend);
                }

                case 1 {
                    return source_with_opacity;
                }

                case 2 {
                    let blend = blend_color(destination_color, source_color, color_mode);
                    return compose_atop(destination_pixel, source_with_opacity, blend);
                }

                case 3 {
                    return compose_in(destination_pixel, source_with_opacity);
                }

                case 4 {
                    return compose_out(destination_pixel, source_with_opacity);
                }

                case 5 {
                    return destination_pixel;
                }

                case 6 {
                    let blend = blend_color(source_color, destination_color, color_mode);
                    return compose_atop(source_with_opacity, destination_pixel, blend);
                }

                case 7 {
                    let blend = blend_color(source_color, destination_color, color_mode);
                    return compose_over(source_with_opacity, destination_pixel, blend);
                }

                case 8 {
                    return compose_in(source_with_opacity, destination_pixel);
                }

                case 9 {
                    return compose_out(source_with_opacity, destination_pixel);
                }

                case 10 {
                    return vec4<f32>(0.0, 0.0, 0.0, 0.0);
                }

                case 11 {
                    return compose_xor(destination_pixel, source_with_opacity);
                }

                default {
                    let blend = blend_color(destination_color, source_color, color_mode);
                    return compose_over(destination_pixel, source_with_opacity, blend);
                }
            }
        }

        fn positive_mod(value: i32, divisor: i32) -> i32 {
            return ((value % divisor) + divisor) % divisor;
        }

        fn sample_brush(brush_data: BrushData, destination_x: i32, destination_y: i32) -> vec4<f32> {
            if (brush_data.source_region_width <= 0 || brush_data.source_region_height <= 0) {
                return vec4<f32>(0.0, 0.0, 0.0, 0.0);
            }

            let source_x = positive_mod(
                destination_x - brush_data.brush_origin_x,
                brush_data.source_region_width) + brush_data.source_region_x;
            let source_y = positive_mod(
                destination_y - brush_data.brush_origin_y,
                brush_data.source_region_height) + brush_data.source_region_y;
            return textureLoad(source_layers, vec2<i32>(source_x, source_y), brush_data.source_layer, 0);
        }

        @compute @workgroup_size(8, 8, 1)
        fn cs_main(
            @builtin(workgroup_id) workgroup_id: vec3<u32>,
            @builtin(local_invocation_id) local_id: vec3<u32>)
        {
            let tile_x = i32(workgroup_id.x);
            let tile_y = i32(workgroup_id.y);
            if (tile_x < 0 || tile_x >= params.tiles_x || tile_y < 0) {
                return;
            }

            let pixel_x = tile_x * params.tile_size + i32(local_id.x);
            let pixel_y = tile_y * params.tile_size + i32(local_id.y);
            if (pixel_x < 0 ||
                pixel_y < 0 ||
                pixel_x >= params.destination_width ||
                pixel_y >= params.destination_height)
            {
                return;
            }

            let destination_index = (pixel_y * params.destination_width) + pixel_x;
            var destination = destination_pixels[destination_index];

            let tile_index = (tile_y * params.tiles_x) + tile_x;
            let tile_range = tile_ranges[tile_index];
            let tile_end = tile_range.start_index + tile_range.count;
            var tile_cursor = tile_range.start_index;
            loop {
                if (tile_cursor >= tile_end) {
                    break;
                }

                let command_index = tile_command_indices[tile_cursor];
                let command = commands[command_index];
                if (pixel_x >= command.destination_x &&
                    pixel_y >= command.destination_y &&
                    pixel_x < (command.destination_x + command.destination_width) &&
                    pixel_y < (command.destination_y + command.destination_height))
                {
                    let local_x = pixel_x - command.destination_x;
                    let local_y = pixel_y - command.destination_y;
                    let coverage_source = vec2<i32>(
                        command.source_offset_x + local_x,
                        command.source_offset_y + local_y);
                    let coverage_value = textureLoad(coverage, coverage_source, 0).r;
                    if (coverage_value > 0.0) {
                        let brush = sample_brush(brushes[command.brush_data_index], pixel_x, pixel_y);
                        let source = vec4<f32>(brush.rgb, brush.a * coverage_value);
                        destination = compose_pixel(
                            destination,
                            source,
                            command.blend_percentage,
                            command.color_blending_mode,
                            command.alpha_composition_mode);
                    }
                }

                tile_cursor = tile_cursor + 1u;
            }

            destination_pixels[destination_index] = destination;
        } 
        """u8;
}
