// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// Copies the destination texture into a composition backdrop for read-only sampling.
/// </summary>
internal static class BackdropComputeShader
{
    /// <summary>
    /// Gets the null-terminated WGSL source for the backdrop copy pass.
    /// </summary>
    private static readonly byte[] CodeBytes =
    [
        ..
        """
        struct Tile {
            backdrop: i32,
            segment_count_or_ix: u32,
        }

        struct Config {
            width_in_tiles: u32,
            height_in_tiles: u32,
            target_width: u32,
            target_height: u32,
            base_color: u32,
            n_drawobj: u32,
            n_path: u32,
            n_clip: u32,
            bin_data_start: u32,
            pathtag_base: u32,
            pathdata_base: u32,
            drawtag_base: u32,
            drawdata_base: u32,
            transform_base: u32,
            style_base: u32,
            lines_size: u32,
            binning_size: u32,
            tiles_size: u32,
            seg_counts_size: u32,
            segments_size: u32,
            blend_size: u32,
            ptcl_size: u32,
        }

        @group(0) @binding(0)
        var<uniform> config: Config;

        @group(0) @binding(1)
        var<storage, read_write> tiles: array<Tile>;

        const WG_SIZE = 64u;
        var<workgroup> sh_backdrop: array<i32, WG_SIZE>;
        var<workgroup> running_backdrop: i32;

        @compute @workgroup_size(64)
        fn cs_main(
            @builtin(local_invocation_id) local_id: vec3<u32>,
            @builtin(workgroup_id) wg_id: vec3<u32>,
        ) {
            let width_in_tiles = config.width_in_tiles;
            let row_index = wg_id.x;
            if row_index >= config.height_in_tiles {
                return;
            }

            if local_id.x == 0u {
                running_backdrop = 0;
            }
            workgroupBarrier();

            var chunk_start = 0u;
            loop {
                if chunk_start >= width_in_tiles {
                    break;
                }

                let count = min(WG_SIZE, width_in_tiles - chunk_start);
                var backdrop = 0;
                if local_id.x < count {
                    let ix = row_index * width_in_tiles + chunk_start + local_id.x;
                    backdrop = tiles[ix].backdrop;
                }

                sh_backdrop[local_id.x] = backdrop;
                for (var i = 0u; i < firstTrailingBit(WG_SIZE); i += 1u) {
                    workgroupBarrier();
                    if local_id.x >= (1u << i) {
                        backdrop += sh_backdrop[local_id.x - (1u << i)];
                    }

                    workgroupBarrier();
                    sh_backdrop[local_id.x] = backdrop;
                }

                workgroupBarrier();
                if local_id.x < count {
                    let ix = row_index * width_in_tiles + chunk_start + local_id.x;
                    let accumulated = sh_backdrop[local_id.x] + running_backdrop;
                    tiles[ix].backdrop = accumulated;
                    if local_id.x + 1u == count {
                        running_backdrop = accumulated;
                    }
                }

                workgroupBarrier();
                chunk_start += WG_SIZE;
            }
        }
        """u8,
        0
    ];

    /// <summary>Gets the WGSL source for this shader as a null-terminated UTF-8 span.</summary>
    public static ReadOnlySpan<byte> Code => CodeBytes;
}
