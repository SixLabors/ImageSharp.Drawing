// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

internal static class PreparedCompositeTileSortComputeShader
{
    private static readonly byte[] CodeBytes =
    [
        ..
        """
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

        @group(0) @binding(0) var<storage, read> tile_starts: array<u32>;
        @group(0) @binding(1) var<storage, read_write> tile_counts: array<atomic<u32>>;
        @group(0) @binding(2) var<storage, read_write> tile_command_indices: array<u32>;
        @group(0) @binding(3) var<uniform> dispatch_config: DispatchConfig;

        @compute @workgroup_size(1, 1, 1)
        fn cs_main(@builtin(global_invocation_id) global_id: vec3<u32>) {
            let tile_index = global_id.x;
            if (tile_index >= dispatch_config.tile_count) {
                return;
            }

            let start = tile_starts[tile_index];
            let count = atomicLoad(&tile_counts[tile_index]);
            if (count <= 1u) {
                return;
            }

            var i: u32 = 1u;
            loop {
                if (i >= count) {
                    break;
                }

                let key = tile_command_indices[start + i];
                var j: u32 = i;
                loop {
                    if (j == 0u) {
                        break;
                    }

                    let previous_index = start + j - 1u;
                    let previous_value = tile_command_indices[previous_index];
                    if (previous_value <= key) {
                        break;
                    }

                    tile_command_indices[start + j] = previous_value;
                    j = j - 1u;
                }

                tile_command_indices[start + j] = key;
                i = i + 1u;
            }
        }
        """u8,
        0
    ];

    public static ReadOnlySpan<byte> Code => CodeBytes;
}
