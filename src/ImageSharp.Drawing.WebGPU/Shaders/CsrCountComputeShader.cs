// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// GPU compute shader that counts edges per CSR band.
/// Each thread processes one edge and atomically increments band counts
/// for each 16-row band the edge overlaps.
/// </summary>
internal static class CsrCountComputeShader
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
        @group(0) @binding(1) var<storage, read_write> band_counts: array<atomic<u32>>;
        @group(0) @binding(2) var<uniform> config: CsrConfig;

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
            let min_band = edge.min_row / 16;
            let max_band = edge.max_row / 16;
            for (var band = min_band; band <= max_band; band++) {
                atomicAdd(&band_counts[edge.csr_band_offset + u32(band)], 1u);
            }
        }
        """u8,
        0
    ];

    /// <summary>Gets the WGSL source for this shader as a null-terminated UTF-8 span.</summary>
    public static ReadOnlySpan<byte> Code => CodeBytes;
}
