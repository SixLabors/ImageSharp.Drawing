// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

internal static class CompositeCoverageShader
{
    private static readonly byte[] CodeBytes =
    [
        .. """
        struct CompositeParams {
            source_offset_x: u32,
            source_offset_y: u32,
            destination_x: u32,
            destination_y: u32,
            destination_width: u32,
            destination_height: u32,
            target_width: u32,
            target_height: u32,

            brush_kind: u32,
            _pad0: u32,
            _pad1: u32,
            _pad2: u32,

            solid_brush_color: vec4<f32>,
            blend_percentage: f32,
            _pad3: f32,
            _pad4: f32,
            _pad5: f32,
        };

        @group(0) @binding(0)
        var coverage: texture_2d<f32>;

        @group(0) @binding(1)
        var<uniform> params: CompositeParams;

        struct VertexOutput {
            @builtin(position) position: vec4<f32>,
            @location(0) local: vec2<f32>,
        };

        @vertex
        fn vs_main(@builtin(vertex_index) vertex_index: u32) -> VertexOutput {
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
            return output;
        }

        fn sample_brush(_local: vec2<f32>) -> vec4<f32> {
            switch params.brush_kind {
                case 0u: {
                    return params.solid_brush_color;
                }
                default: {
                    return vec4<f32>(0.0);
                }
            }
        }

        @fragment
        fn fs_main(input: VertexOutput) -> @location(0) vec4<f32> {
            let local_x = u32(floor(input.local.x));
            let local_y = u32(floor(input.local.y));
            let source = vec2<i32>(
                i32(params.source_offset_x + local_x),
                i32(params.source_offset_y + local_y));

            let coverage_value = textureLoad(coverage, source, 0).r;
            if (coverage_value <= 0.0) {
                discard;
            }

            let brush = sample_brush(input.local);
            if (brush.a <= 0.0) {
                discard;
            }

            let source_alpha = brush.a * coverage_value * params.blend_percentage;
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
