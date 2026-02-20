// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

internal static class CoverageRasterizationShader
{
    private static readonly byte[] CodeBytes =
    [
        .. """
        struct VertexOutput {
            @builtin(position) position: vec4<f32>,
        };

        @vertex
        fn vs_edge(@location(0) position: vec2<f32>) -> VertexOutput {
            var output: VertexOutput;
            output.position = vec4<f32>(position, 0.0, 1.0);
            return output;
        }

        @fragment
        fn fs_stencil() -> @location(0) vec4<f32> {
            // Color writes are disabled for the stencil pipeline.
            return vec4<f32>(0.0, 0.0, 0.0, 0.0);
        }

        @vertex
        fn vs_cover(@builtin(vertex_index) vertex_index: u32) -> VertexOutput {
            var positions = array<vec2<f32>, 3>(
                vec2<f32>(-1.0, -1.0),
                vec2<f32>(3.0, -1.0),
                vec2<f32>(-1.0, 3.0));

            var output: VertexOutput;
            output.position = vec4<f32>(positions[vertex_index], 0.0, 1.0);
            return output;
        }

        @fragment
        fn fs_cover() -> @location(0) vec4<f32> {
            return vec4<f32>(1.0, 0.0, 0.0, 1.0);
        }
        """u8,
        .. "\0"u8
    ];

    public static ReadOnlySpan<byte> Code => CodeBytes;
}
