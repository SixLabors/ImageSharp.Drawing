// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

internal static class SolidBrushCompositeComputeShader
{
    // Compile-time constant backed by static PE data (no heap allocation).
    public static ReadOnlySpan<byte> Code =>
        """
        struct SolidBrushCompositeData {
            source_offset_x: i32,
            source_offset_y: i32,
            destination_x: i32,
            destination_y: i32,
            destination_width: i32,
            destination_height: i32,
            destination_buffer_width: i32,
            destination_buffer_height: i32,
            blend_percentage: f32,
            color_blending_mode: i32,
            alpha_composition_mode: i32,
            _common_pad0: i32,
            solid_brush_color: vec4<f32>,
        };

        @group(0) @binding(0)
        var coverage: texture_2d<f32>;

        @group(0) @binding(1)
        var<storage, read> instance: SolidBrushCompositeData;

        @group(0) @binding(2)
        var<storage, read_write> destination_pixels: array<vec4<f32>>;

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

        @compute @workgroup_size(8, 8, 1)
        fn cs_main(@builtin(global_invocation_id) global_id: vec3<u32>) {
            let params = instance;
            let local_x = i32(global_id.x);
            let local_y = i32(global_id.y);
            if (local_x >= params.destination_width || local_y >= params.destination_height) {
                return;
            }

            let destination_pixel_x = params.destination_x + local_x;
            let destination_pixel_y = params.destination_y + local_y;
            if (destination_pixel_x < 0 ||
                destination_pixel_y < 0 ||
                destination_pixel_x >= params.destination_buffer_width ||
                destination_pixel_y >= params.destination_buffer_height) {
                return;
            }

            let coverage_source = vec2<i32>(
                params.source_offset_x + local_x,
                params.source_offset_y + local_y);
            let coverage_value = textureLoad(coverage, coverage_source, 0).r;
            let brush = params.solid_brush_color;
            let source = vec4<f32>(brush.rgb, brush.a * coverage_value);

            let destination_index = (destination_pixel_y * params.destination_buffer_width) + destination_pixel_x;
            let destination = destination_pixels[destination_index];
            destination_pixels[destination_index] = compose_pixel(
                destination,
                source,
                params.blend_percentage,
                params.color_blending_mode,
                params.alpha_composition_mode);
        } 
        """u8;
}
