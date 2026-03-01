// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// Counts the number of composite commands affecting each tile using bin headers.
/// </summary>
internal static class PreparedCompositeTileCountComputeShader
{
    /// <summary>
    /// Gets the null-terminated WGSL source for per-tile command counts.
    /// </summary>
    private static readonly byte[] CodeBytes =
    [
        .. """
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
            width_in_bins: u32,
            height_in_bins: u32,
            bin_count: u32,
            partition_count: u32,
            binning_size: u32,
            bin_data_start: u32,
        };

        struct CommandBbox {
            x0: i32,
            y0: i32,
            x1: i32,
            y1: i32,
        };

        struct BinHeader {
            element_count: u32,
            chunk_offset: u32,
        };

        @group(0) @binding(0) var<storage, read> command_bboxes: array<CommandBbox>;
        @group(0) @binding(1) var<storage, read> bin_header: array<BinHeader>;
        @group(0) @binding(2) var<storage, read> bin_data: array<u32>;
        @group(0) @binding(3) var<storage, read_write> tile_counts: array<atomic<u32>>;
        @group(0) @binding(4) var<uniform> dispatch_config: DispatchConfig;

        const TILE_WIDTH: u32 = 16u;
        const TILE_HEIGHT: u32 = 16u;
        const N_TILE_X: u32 = 16u;
        const N_TILE_Y: u32 = 16u;
        const N_TILE: u32 = N_TILE_X * N_TILE_Y;

        @compute @workgroup_size(256)
        fn cs_main(
            @builtin(local_invocation_id) local_id: vec3<u32>,
            @builtin(workgroup_id) wg_id: vec3<u32>,
        ) {
            let bin_x = wg_id.x;
            let bin_y = wg_id.y;
            if (bin_x >= dispatch_config.width_in_bins || bin_y >= dispatch_config.height_in_bins) {
                return;
            }

            let tile_x = local_id.x % N_TILE_X;
            let tile_y = local_id.x / N_TILE_X;
            let global_tile_x = bin_x * N_TILE_X + tile_x;
            let global_tile_y = bin_y * N_TILE_Y + tile_y;
            if (global_tile_x >= dispatch_config.tile_count_x || global_tile_y >= dispatch_config.tile_count_y) {
                return;
            }

            let tile_index = global_tile_y * dispatch_config.tile_count_x + global_tile_x;
            let tile_min_x = i32(global_tile_x * TILE_WIDTH);
            let tile_min_y = i32(global_tile_y * TILE_HEIGHT);
            let tile_max_x = tile_min_x + i32(TILE_WIDTH);
            let tile_max_y = tile_min_y + i32(TILE_HEIGHT);
            let bin_ix = bin_y * dispatch_config.width_in_bins + bin_x;

            var count = 0u;
            var part_ix = 0u;
            loop {
                if (part_ix >= dispatch_config.partition_count) {
                    break;
                }

                let header = bin_header[part_ix * N_TILE + bin_ix];
                let element_count = header.element_count;
                let base = header.chunk_offset;
                for (var i = 0u; i < element_count; i += 1u) {
                    let cmd_index = bin_data[dispatch_config.bin_data_start + base + i];
                    let bbox = command_bboxes[cmd_index];
                    if (bbox.x1 > tile_min_x && bbox.x0 < tile_max_x && bbox.y1 > tile_min_y && bbox.y0 < tile_max_y) {
                        count = count + 1u;
                    }
                }

                part_ix = part_ix + 1u;
            }

            atomicStore(&tile_counts[tile_index], count);
        }
        """u8,
        0
    ];

    /// <summary>Gets the WGSL source for this shader as a null-terminated UTF-8 span.</summary>
    public static ReadOnlySpan<byte> Code => CodeBytes;
}
