// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

internal static class PreparedCompositeTileScatterComputeShader
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

        @group(0) @binding(0) var<storage, read> commands: array<Params>;
        @group(0) @binding(1) var<storage, read> tile_starts: array<u32>;
        @group(0) @binding(2) var<storage, read_write> tile_write_offsets: array<atomic<u32>>;
        @group(0) @binding(3) var<storage, read_write> tile_command_indices: array<u32>;
        @group(0) @binding(4) var<uniform> dispatch_config: DispatchConfig;

        @compute @workgroup_size(1, 1, 1)
        fn cs_main(@builtin(global_invocation_id) global_id: vec3<u32>) {
            if (global_id.x != 0u || global_id.y != 0u || global_id.z != 0u) {
                return;
            }

            if (dispatch_config.tile_count_x == 0u || dispatch_config.tile_count_y == 0u) {
                return;
            }

            var command_index: u32 = 0u;
            loop {
                if (command_index >= dispatch_config.command_count) {
                    break;
                }

                let command = commands[command_index];
                if (command.destination_width == 0u || command.destination_height == 0u) {
                    command_index += 1u;
                    continue;
                }

                let destination_x = bitcast<i32>(command.destination_x);
                let destination_y = bitcast<i32>(command.destination_y);
                let destination_max_x = destination_x + i32(command.destination_width) - 1;
                let destination_max_y = destination_y + i32(command.destination_height) - 1;
                let min_tile_x = u32(max(0, destination_x / 16));
                let min_tile_y = u32(max(0, destination_y / 16));
                let max_tile_x = u32(min(i32(dispatch_config.tile_count_x) - 1, destination_max_x / 16));
                let max_tile_y = u32(min(i32(dispatch_config.tile_count_y) - 1, destination_max_y / 16));

                if (max_tile_x < min_tile_x || max_tile_y < min_tile_y) {
                    command_index += 1u;
                    continue;
                }

                var tile_y = min_tile_y;
                loop {
                    if (tile_y > max_tile_y) {
                        break;
                    }

                    let row_offset = tile_y * dispatch_config.tile_count_x;
                    var tile_x = min_tile_x;
                    loop {
                        if (tile_x > max_tile_x) {
                            break;
                        }

                        let tile_index = row_offset + tile_x;
                        let local_offset = atomicAdd(&tile_write_offsets[tile_index], 1u);
                        let write_index = tile_starts[tile_index] + local_offset;
                        tile_command_indices[write_index] = command_index;
                        tile_x += 1u;
                    }

                    tile_y += 1u;
                }

                command_index += 1u;
            }
        }
        """u8,
        0
    ];

    public static ReadOnlySpan<byte> Code => CodeBytes;
}
