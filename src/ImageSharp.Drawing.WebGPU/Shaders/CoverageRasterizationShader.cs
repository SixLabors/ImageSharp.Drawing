// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

internal static class CoverageRasterizationShader
{
    public static ReadOnlySpan<byte> Code =>
        """
        struct Edge {
            x0: f32,
            y0: f32,
            x1: f32,
            y1: f32,
        };

        struct CoverageParams {
            edge_count: u32,
            intersection_rule: u32,
            antialias: u32,
            _pad0: u32,
            sample_origin_x: f32,
            sample_origin_y: f32,
            _pad1: f32,
            _pad2: f32,
        };

        @group(0) @binding(0)
        var<storage, read> edges: array<Edge>;

        @group(0) @binding(1)
        var<uniform> params: CoverageParams;

        struct VertexOutput {
            @builtin(position) position: vec4<f32>,
        };

        @vertex
        fn vs_main(@builtin(vertex_index) vertex_index: u32) -> VertexOutput {
            var positions = array<vec2<f32>, 3>(
                vec2<f32>(-1.0, -1.0),
                vec2<f32>(3.0, -1.0),
                vec2<f32>(-1.0, 3.0));

            var output: VertexOutput;
            output.position = vec4<f32>(positions[vertex_index], 0.0, 1.0);
            return output;
        }

        fn is_inside(sample: vec2<f32>) -> bool {
            var winding: i32 = 0;
            var crossings: u32 = 0u;

            for (var i: u32 = 0u; i < params.edge_count; i = i + 1u) {
                let edge = edges[i];
                if (edge.y0 == edge.y1) {
                    continue;
                }

                let upward = (edge.y0 <= sample.y) && (edge.y1 > sample.y);
                let downward = (edge.y0 > sample.y) && (edge.y1 <= sample.y);
                if (!(upward || downward)) {
                    continue;
                }

                let t = (sample.y - edge.y0) / (edge.y1 - edge.y0);
                let x = edge.x0 + t * (edge.x1 - edge.x0);
                if (x > sample.x) {
                    crossings = crossings + 1u;
                    if (upward) {
                        winding = winding + 1;
                    } else {
                        winding = winding - 1;
                    }
                }
            }

            if (params.intersection_rule == 0u) {
                return (crossings & 1u) == 1u;
            }

            return winding != 0;
        }

        fn single_sample(pixel: vec2<f32>) -> f32 {
            let sample = pixel + vec2<f32>(params.sample_origin_x, params.sample_origin_y);
            return select(0.0, 1.0, is_inside(sample));
        }

        fn antialias_sample(pixel: vec2<f32>) -> f32 {
            // Supersample a fixed grid around the configured sample origin.
            // This produces smoother coverage than the previous 2x2 tap pattern.
            let grid: u32 = 8u;
            let inv_sample_count = 1.0 / f32(grid * grid);
            let origin = vec2<f32>(params.sample_origin_x, params.sample_origin_y);
            let base = origin - vec2<f32>(0.5, 0.5);

            var covered = 0.0;
            for (var y: u32 = 0u; y < grid; y = y + 1u) {
                let fy = (f32(y) + 0.5) / f32(grid);
                for (var x: u32 = 0u; x < grid; x = x + 1u) {
                    let fx = (f32(x) + 0.5) / f32(grid);
                    covered = covered + select(0.0, 1.0, is_inside(pixel + base + vec2<f32>(fx, fy)));
                }
            }

            return covered * inv_sample_count;
        }

        @fragment
        fn fs_main(@builtin(position) position: vec4<f32>) -> @location(0) vec4<f32> {
            let pixel = floor(position.xy);
            let coverage = select(single_sample(pixel), antialias_sample(pixel), params.antialias != 0u);
            return vec4<f32>(coverage, 0.0, 0.0, 1.0);
        }
        """u8;
}
