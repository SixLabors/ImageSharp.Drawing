// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// Phase 2 of the parallel CSR prefix sum: a single workgroup performs an
/// in-place exclusive prefix sum over the block_sums array from phase 1.
/// Supports up to 65536 blocks (256 * 256 = 16M bands).
/// </summary>
internal static class CsrPrefixBlockScanComputeShader
{
    private static readonly byte[] CodeBytes =
    [
        .. """
        struct PrefixConfig {
            block_count: u32,
        };

        @group(0) @binding(0) var<storage, read_write> block_sums: array<u32>;
        @group(0) @binding(1) var<uniform> prefix_config: PrefixConfig;

        var<workgroup> shared_data: array<u32, 256>;

        @compute @workgroup_size(256, 1, 1)
        fn cs_main(@builtin(local_invocation_id) local_id: vec3<u32>) {
            let tid = local_id.x;
            let block_count = prefix_config.block_count;

            // Each thread processes multiple chunks of 256 blocks sequentially.
            // This handles up to 65536 blocks (256 threads * 256 elements each).
            var running_total = 0u;
            var chunk_start = 0u;
            loop {
                if (chunk_start >= block_count) {
                    break;
                }

                let global_index = chunk_start + tid;
                var value = 0u;
                if (global_index < block_count) {
                    value = block_sums[global_index];
                }
                shared_data[tid] = value;
                workgroupBarrier();

                // Up-sweep.
                for (var stride = 1u; stride < 256u; stride = stride * 2u) {
                    let index = (tid + 1u) * stride * 2u - 1u;
                    if (index < 256u) {
                        shared_data[index] = shared_data[index] + shared_data[index - stride];
                    }
                    workgroupBarrier();
                }

                // Store chunk total and clear for down-sweep.
                var chunk_total = 0u;
                if (tid == 0u) {
                    chunk_total = shared_data[255];
                    shared_data[255] = 0u;
                }
                workgroupBarrier();

                // Down-sweep.
                for (var stride = 128u; stride >= 1u; stride = stride / 2u) {
                    let index = (tid + 1u) * stride * 2u - 1u;
                    if (index < 256u) {
                        let temp = shared_data[index - stride];
                        shared_data[index - stride] = shared_data[index];
                        shared_data[index] = shared_data[index] + temp;
                    }
                    workgroupBarrier();
                }

                // Write back with running total offset.
                if (global_index < block_count) {
                    block_sums[global_index] = shared_data[tid] + running_total;
                }
                workgroupBarrier();

                // Broadcast chunk_total from thread 0 for next iteration.
                if (tid == 0u) {
                    shared_data[0] = chunk_total;
                }
                workgroupBarrier();
                running_total = running_total + shared_data[0];
                workgroupBarrier();

                chunk_start = chunk_start + 256u;
            }
        }
        """u8,
        0
    ];

    /// <summary>Gets the WGSL source for this shader as a null-terminated UTF-8 span.</summary>
    public static ReadOnlySpan<byte> Code => CodeBytes;
}
