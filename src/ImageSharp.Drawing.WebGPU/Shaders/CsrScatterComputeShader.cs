// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// GPU compute shader that scatters edge indices into CSR buckets.
/// Each thread processes one edge. For each band the edge overlaps,
/// it atomically claims a slot in <c>csr_indices</c> via a write cursor.
/// </summary>
internal static class CsrScatterComputeShader
{
    private static readonly byte[] CodeBytes =
    [
        .. """
        struct Edge {
            x0: i32,
            y0: i32,
            x1: i32,
            y1: i32,
            min_row: i32,
            max_row: i32,
            csr_band_offset: u32,
            definition_edge_start: u32,
        }

        struct CsrConfig {
            total_edge_count: u32,
        };

        @group(0) @binding(0) var<storage, read> edges: array<Edge>;
        @group(0) @binding(1) var<storage, read> csr_offsets: array<u32>;
        @group(0) @binding(2) var<storage, read_write> write_cursors: array<atomic<u32>>;
        @group(0) @binding(3) var<storage, read_write> csr_indices: array<u32>;
        @group(0) @binding(4) var<uniform> config: CsrConfig;

        @compute @workgroup_size(256, 1, 1)
        fn cs_main(@builtin(global_invocation_id) gid: vec3<u32>) {
            let edge_idx = gid.x;
            if (edge_idx >= config.total_edge_count) {
                return;
            }
            let edge = edges[edge_idx];
            if (edge.min_row > edge.max_row) {
                return;
            }
            let local_idx = edge_idx - edge.definition_edge_start;
            let min_band = edge.min_row / 16;
            let max_band = edge.max_row / 16;
            for (var band = min_band; band <= max_band; band++) {
                let band_offset = edge.csr_band_offset + u32(band);
                let offset = csr_offsets[band_offset];
                let slot = atomicAdd(&write_cursors[band_offset], 1u);
                csr_indices[offset + slot] = local_idx;
            }
        }
        """u8,
        0
    ];

    /// <summary>Gets the WGSL source for this shader as a null-terminated UTF-8 span.</summary>
    public static ReadOnlySpan<byte> Code => CodeBytes;
}
