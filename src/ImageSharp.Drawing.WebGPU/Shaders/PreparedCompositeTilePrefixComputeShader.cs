// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

internal static class PreparedCompositeTilePrefixComputeShader
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
            pad0: u32,
            pad1: u32,
        };

        @group(0) @binding(0) var<storage, read_write> tile_counts: array<atomic<u32>>;
        @group(0) @binding(1) var<storage, read_write> tile_starts: array<u32>;
        @group(0) @binding(2) var<uniform> dispatch_config: DispatchConfig;

        @compute @workgroup_size(1, 1, 1)
        fn cs_main(@builtin(global_invocation_id) global_id: vec3<u32>) {
            if (global_id.x != 0u || global_id.y != 0u || global_id.z != 0u) {
                return;
            }

            var running: u32 = 0u;
            var tile_index: u32 = 0u;
            loop {
                if (tile_index >= dispatch_config.tile_count) {
                    break;
                }

                tile_starts[tile_index] = running;
                running = running + atomicLoad(&tile_counts[tile_index]);
                tile_index += 1u;
            }
        }
        """u8,
        0
    ];

    public static ReadOnlySpan<byte> Code => CodeBytes;
}
