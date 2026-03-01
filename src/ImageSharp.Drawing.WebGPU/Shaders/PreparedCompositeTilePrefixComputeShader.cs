// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// Prefix-sums per-tile command counts into tile starts for the fill pass.
/// </summary>
internal static class PreparedCompositeTilePrefixComputeShader
{
    /// <summary>
    /// Gets the null-terminated WGSL source for tile prefix sum calculation.
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

        @group(0) @binding(0) var<storage, read> tile_counts: array<atomic<u32>>;
        @group(0) @binding(1) var<storage, read_write> tile_starts: array<u32>;
        @group(0) @binding(2) var<uniform> dispatch_config: DispatchConfig;

        @compute @workgroup_size(1, 1, 1)
        fn cs_main(@builtin(global_invocation_id) global_id: vec3<u32>) {
            if (global_id.x != 0u || global_id.y != 0u || global_id.z != 0u) {
                return;
            }

            var sum = 0u;
            var tile_index = 0u;
            loop {
                if (tile_index >= dispatch_config.tile_count) {
                    break;
                }
                let count = atomicLoad(&tile_counts[tile_index]);
                tile_starts[tile_index] = sum;
                sum = sum + count;
                tile_index = tile_index + 1u;
            }
        }
        """u8,
        0
    ];

    /// <summary>Gets the WGSL source for this shader as a null-terminated UTF-8 span.</summary>
    public static ReadOnlySpan<byte> Code => CodeBytes;
}
