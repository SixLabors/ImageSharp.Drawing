// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// Phase 3 of the parallel CSR prefix sum: each workgroup adds its
/// block prefix from block_sums to all CSR offsets in its range.
/// Workgroup 0 is skipped (its prefix is 0).
/// </summary>
internal static class CsrPrefixPropagateComputeShader
{
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

        @group(0) @binding(0) var<storage, read> block_sums: array<u32>;
        @group(0) @binding(1) var<storage, read_write> tile_starts: array<u32>;
        @group(0) @binding(2) var<uniform> dispatch_config: DispatchConfig;

        @compute @workgroup_size(256, 1, 1)
        fn cs_main(
            @builtin(local_invocation_id) local_id: vec3<u32>,
            @builtin(workgroup_id) wg_id: vec3<u32>
        ) {
            if (wg_id.x == 0u) {
                return;
            }

            let global_index = wg_id.x * 256u + local_id.x;
            if (global_index < dispatch_config.tile_count) {
                tile_starts[global_index] = tile_starts[global_index] + block_sums[wg_id.x];
            }
        }
        """u8,
        0
    ];

    /// <summary>Gets the WGSL source for this shader as a null-terminated UTF-8 span.</summary>
    public static ReadOnlySpan<byte> Code => CodeBytes;
}
