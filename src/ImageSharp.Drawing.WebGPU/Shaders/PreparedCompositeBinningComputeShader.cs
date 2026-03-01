// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

internal static class PreparedCompositeBinningComputeShader
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

        struct BumpAllocators {
            failed: atomic<u32>,
            binning: atomic<u32>,
        };

        @group(0) @binding(0) var<storage, read> command_bboxes: array<CommandBbox>;
        @group(0) @binding(1) var<storage, read_write> bin_header: array<BinHeader>;
        @group(0) @binding(2) var<storage, read_write> bin_data: array<u32>;
        @group(0) @binding(3) var<storage, read_write> bump: BumpAllocators;
        @group(0) @binding(4) var<uniform> dispatch_config: DispatchConfig;

        const TILE_WIDTH: u32 = 16u;
        const TILE_HEIGHT: u32 = 16u;
        const N_TILE_X: u32 = 16u;
        const N_TILE_Y: u32 = 16u;
        const N_TILE: u32 = N_TILE_X * N_TILE_Y;
        const WG_SIZE: u32 = 256u;
        const N_SLICE: u32 = WG_SIZE / 32u;
        const N_SUBSLICE: u32 = 4u;
        const SX: f32 = 1.0 / f32(N_TILE_X * TILE_WIDTH);
        const SY: f32 = 1.0 / f32(N_TILE_Y * TILE_HEIGHT);
        const STAGE_BINNING: u32 = 1u;

        var<workgroup> sh_bitmaps: array<array<atomic<u32>, N_TILE>, N_SLICE>;
        var<workgroup> sh_count: array<array<u32, N_TILE>, N_SUBSLICE>;
        var<workgroup> sh_chunk_offset: array<u32, N_TILE>;

        @compute @workgroup_size(256)
        fn cs_main(
            @builtin(global_invocation_id) global_id: vec3<u32>,
            @builtin(local_invocation_id) local_id: vec3<u32>,
        ) {
            for (var i = 0u; i < N_SLICE; i += 1u) {
                atomicStore(&sh_bitmaps[i][local_id.x], 0u);
            }
            workgroupBarrier();

            let element_ix = global_id.x;
            var x0 = 0;
            var y0 = 0;
            var x1 = 0;
            var y1 = 0;
            if (element_ix < dispatch_config.command_count) {
                let bbox = command_bboxes[element_ix];
                let fbbox = vec4<f32>(vec4(bbox.x0, bbox.y0, bbox.x1, bbox.y1));
                if (fbbox.x < fbbox.z && fbbox.y < fbbox.w) {
                    x0 = i32(floor(fbbox.x * SX));
                    y0 = i32(floor(fbbox.y * SY));
                    x1 = i32(ceil(fbbox.z * SX));
                    y1 = i32(ceil(fbbox.w * SY));
                }
            }

            let width_in_bins = i32(dispatch_config.width_in_bins);
            let height_in_bins = i32(dispatch_config.height_in_bins);
            x0 = clamp(x0, 0, width_in_bins);
            y0 = clamp(y0, 0, height_in_bins);
            x1 = clamp(x1, 0, width_in_bins);
            y1 = clamp(y1, 0, height_in_bins);
            if (x0 == x1) {
                y1 = y0;
            }

            var x = x0;
            var y = y0;
            let my_slice = local_id.x / 32u;
            let my_mask = 1u << (local_id.x & 31u);
            while y < y1 {
                atomicOr(&sh_bitmaps[my_slice][u32(y * width_in_bins + x)], my_mask);
                x += 1;
                if x == x1 {
                    x = x0;
                    y += 1;
                }
            }

            workgroupBarrier();

            var element_count = 0u;
            for (var i = 0u; i < N_SUBSLICE; i += 1u) {
                element_count += countOneBits(atomicLoad(&sh_bitmaps[i * 2u][local_id.x]));
                let element_count_lo = element_count;
                element_count += countOneBits(atomicLoad(&sh_bitmaps[i * 2u + 1u][local_id.x]));
                let element_count_hi = element_count;
                let element_count_packed = element_count_lo | (element_count_hi << 16u);
                sh_count[i][local_id.x] = element_count_packed;
            }

            var chunk_offset = atomicAdd(&bump.binning, element_count);
            if chunk_offset + element_count > dispatch_config.binning_size {
                chunk_offset = 0u;
                atomicOr(&bump.failed, STAGE_BINNING);
            }

            sh_chunk_offset[local_id.x] = chunk_offset;
            bin_header[global_id.x].element_count = element_count;
            bin_header[global_id.x].chunk_offset = chunk_offset;
            workgroupBarrier();

            x = x0;
            y = y0;
            while y < y1 {
                let bin_ix = u32(y * width_in_bins + x);
                let out_mask = atomicLoad(&sh_bitmaps[my_slice][bin_ix]);
                if (out_mask & my_mask) != 0u {
                    var idx = countOneBits(out_mask & (my_mask - 1u));
                    if my_slice > 0u {
                        let count_ix = my_slice - 1u;
                        let count_packed = sh_count[count_ix / 2u][bin_ix];
                        idx += (count_packed >> (16u * (count_ix & 1u))) & 0xffffu;
                    }
                    let offset = dispatch_config.bin_data_start + sh_chunk_offset[bin_ix];
                    bin_data[offset + idx] = element_ix;
                }
                x += 1;
                if x == x1 {
                    x = x0;
                    y += 1;
                }
            }
        }
        """u8,
        0
    ];

    public static ReadOnlySpan<byte> Code => CodeBytes;
}
