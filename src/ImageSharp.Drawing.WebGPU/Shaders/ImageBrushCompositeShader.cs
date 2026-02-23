// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

internal static class ImageBrushCompositeShader
{
    private static readonly byte[] CodeBytes =
    [
        .. """
        struct CompositeInstanceData {
            source_offset_x: i32,
            source_offset_y: i32,
            destination_x: i32,
            destination_y: i32,
            destination_width: i32,
            destination_height: i32,
            target_width: i32,
            target_height: i32,

            image_region_x: i32,
            image_region_y: i32,
            image_region_width: i32,
            image_region_height: i32,
            image_brush_origin_x: i32,
            image_brush_origin_y: i32,
            _pad0: i32,
            _pad1: i32,

            solid_brush_color: vec4<f32>,
            blend_data: vec4<f32>,
        };

        @group(0) @binding(0)
        var coverage: texture_2d<f32>;

        @group(0) @binding(1)
        var<storage, read> instances: array<CompositeInstanceData>;

        @group(0) @binding(2)
        var source_image: texture_2d<f32>;

        struct VertexOutput {
            @builtin(position) position: vec4<f32>,
            @location(0) local: vec2<f32>,
            @location(1) @interpolate(flat) instance_index: u32,
        };

        @vertex
        fn vs_main(
            @builtin(vertex_index) vertex_index: u32,
            @builtin(instance_index) instance_index: u32) -> VertexOutput {
            let params = instances[instance_index];
            var vertices = array<vec2<f32>, 6>(
                vec2<f32>(0.0, 0.0),
                vec2<f32>(f32(params.destination_width), 0.0),
                vec2<f32>(0.0, f32(params.destination_height)),
                vec2<f32>(0.0, f32(params.destination_height)),
                vec2<f32>(f32(params.destination_width), 0.0),
                vec2<f32>(f32(params.destination_width), f32(params.destination_height)));

            let local = vertices[vertex_index];
            let pixel = vec2<f32>(f32(params.destination_x), f32(params.destination_y)) + local;
            let ndc_x = (pixel.x / f32(params.target_width)) * 2.0 - 1.0;
            let ndc_y = 1.0 - (pixel.y / f32(params.target_height)) * 2.0;

            var output: VertexOutput;
            output.position = vec4<f32>(ndc_x, ndc_y, 0.0, 1.0);
            output.local = local;
            output.instance_index = instance_index;
            return output;
        }

        fn positive_mod(value: i32, divisor: i32) -> i32 {
            return ((value % divisor) + divisor) % divisor;
        }

        fn sample_brush(params: CompositeInstanceData, local: vec2<f32>) -> vec4<f32> {
            let local_x = i32(floor(local.x));
            let local_y = i32(floor(local.y));
            let destination_x = params.destination_x + local_x;
            let destination_y = params.destination_y + local_y;

            let source_x = positive_mod(destination_x - params.image_brush_origin_x, params.image_region_width) + params.image_region_x;
            let source_y = positive_mod(destination_y - params.image_brush_origin_y, params.image_region_height) + params.image_region_y;

            return textureLoad(source_image, vec2<i32>(source_x, source_y), 0);
        }

        @fragment
        fn fs_main(input: VertexOutput) -> @location(0) vec4<f32> {
            let params = instances[input.instance_index];
            let local_x = i32(floor(input.local.x));
            let local_y = i32(floor(input.local.y));
            let source = vec2<i32>(
                params.source_offset_x + local_x,
                params.source_offset_y + local_y);

            let coverage_value = textureLoad(coverage, source, 0).r;
            if (coverage_value <= 0.0) {
                discard;
            }

            let brush = sample_brush(params, input.local);
            if (brush.a <= 0.0) {
                discard;
            }

            let source_alpha = brush.a * coverage_value * params.blend_data.x;
            if (source_alpha <= 0.0) {
                discard;
            }

            return vec4<f32>(brush.rgb * source_alpha, source_alpha);
        }
        """u8,
        .. "\0"u8
    ];

    public static ReadOnlySpan<byte> Code => CodeBytes;
}
