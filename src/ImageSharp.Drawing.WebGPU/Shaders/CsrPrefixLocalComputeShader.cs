// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// Phase 1 of the parallel CSR prefix sum: each workgroup computes a local
/// exclusive prefix sum over 256 band counts, writes per-band offsets, and
/// stores the workgroup total into a block_sums buffer.
/// </summary>
internal static class CsrPrefixLocalComputeShader
{
    /// <summary>
    /// The number of tiles processed by each workgroup.
    /// </summary>
    public const int TilesPerWorkgroup = 256;

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
        @group(0) @binding(2) var<storage, read_write> block_sums: array<u32>;
        @group(0) @binding(3) var<uniform> dispatch_config: DispatchConfig;

        var<workgroup> shared_data: array<u32, 256>;

        @compute @workgroup_size(256, 1, 1)
        fn cs_main(
            @builtin(local_invocation_id) local_id: vec3<u32>,
            @builtin(workgroup_id) wg_id: vec3<u32>
        ) {
            let tid = local_id.x;
            let global_index = wg_id.x * 256u + tid;

            // Load tile count (0 if out of range).
            var value = 0u;
            if (global_index < dispatch_config.tile_count) {
                value = atomicLoad(&tile_counts[global_index]);
            }
            shared_data[tid] = value;
            workgroupBarrier();

            // Up-sweep (reduce) phase.
            for (var stride = 1u; stride < 256u; stride = stride * 2u) {
                let index = (tid + 1u) * stride * 2u - 1u;
                if (index < 256u) {
                    shared_data[index] = shared_data[index] + shared_data[index - stride];
                }
                workgroupBarrier();
            }

            // Store total and clear last element for down-sweep.
            if (tid == 0u) {
                block_sums[wg_id.x] = shared_data[255];
                shared_data[255] = 0u;
            }
            workgroupBarrier();

            // Down-sweep phase.
            for (var stride = 128u; stride >= 1u; stride = stride / 2u) {
                let index = (tid + 1u) * stride * 2u - 1u;
                if (index < 256u) {
                    let temp = shared_data[index - stride];
                    shared_data[index - stride] = shared_data[index];
                    shared_data[index] = shared_data[index] + temp;
                }
                workgroupBarrier();
            }

            // Write exclusive prefix sum to output.
            if (global_index < dispatch_config.tile_count) {
                tile_starts[global_index] = shared_data[tid];
            }
        }
        """u8,
        0
    ];

    /// <summary>Gets the WGSL source for this shader as a null-terminated UTF-8 span.</summary>
    public static ReadOnlySpan<byte> Code => CodeBytes;
}
