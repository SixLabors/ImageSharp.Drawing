// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

// The binning stage

#import config
#import drawtag
#import bbox
#import bump

@group(0) @binding(0)
var<uniform> config: Config;

@group(0) @binding(1)
var<storage> draw_monoids: array<DrawMonoid>;

@group(0) @binding(2)
var<storage> path_bbox_buf: array<PathBbox>;

@group(0) @binding(3)
var<storage> clip_bbox_buf: array<vec4<f32>>;

@group(0) @binding(4)
var<storage, read_write> intersected_bbox: array<vec4<f32>>;

@group(0) @binding(5)
var<storage, read_write> bump: BumpAllocators;

@group(0) @binding(6)
var<storage, read_write> info_bin_data: array<u32>;

// conversion factors from coordinates to bin
const SX = 1.0 / f32(N_TILE_X * TILE_WIDTH);
const SY = 1.0 / f32(N_TILE_Y * TILE_HEIGHT);

const WG_SIZE = 256u;
const N_SLICE = WG_SIZE / 32u;
const N_SUBSLICE = 4u;

// sh_bitmaps holds one bit per (element, bin-in-chunk) pair, so its inner
// dimension is fixed at N_TILE (= 256) — the number of bins processed by a
// single workgroup. The dispatch tiles bin space in Y so the full bin grid
// can exceed 256 without overflowing this shared array.
var<workgroup> sh_bitmaps: array<array<atomic<u32>, N_TILE>, N_SLICE>;
// store count values packed two u16's to a u32
var<workgroup> sh_count: array<array<u32, N_TILE>, N_SUBSLICE>;
var<workgroup> sh_chunk_offset: array<u32, N_TILE>;
var<workgroup> sh_chunk_valid: array<u32, N_TILE>;
var<workgroup> sh_previous_failed: u32;

// Total number of bin-header slots reserved per draw-partition. Each
// workgroup covers at most N_TILE bins, so the stride is the full bin grid
// aligned up to N_TILE.
fn bin_header_stride(width_in_bins: u32, height_in_bins: u32) -> u32 {
    let n_bins = width_in_bins * height_in_bins;
    return (n_bins + N_TILE - 1u) / N_TILE * N_TILE;
}

fn bin_header_ix(partition_ix: u32, bin_ix: u32, stride: u32) -> u32 {
    return config.bin_data_start + config.binning_size + (partition_ix * stride + bin_ix) * 2u;
}

@compute @workgroup_size(256)
fn main(
    @builtin(global_invocation_id) global_id: vec3<u32>,
    @builtin(local_invocation_id) local_id: vec3<u32>,
    @builtin(workgroup_id) wg_id: vec3<u32>,
) {
    for (var i = 0u; i < N_SLICE; i += 1u) {
        atomicStore(&sh_bitmaps[i][local_id.x], 0u);
    }
    if local_id.x == 0u {
        let failed = atomicLoad(&bump.lines) > config.lines_size;
        sh_previous_failed = u32(failed);
    }
    // also functions as barrier to protect zeroing of bitmaps
    let failed = workgroupUniformLoad(&sh_previous_failed);
    if failed != 0u {
        if global_id.x == 0u {
            atomicOr(&bump.failed, STAGE_FLATTEN);
        }
        return;
    }

    let width_in_bins = (config.width_in_tiles + N_TILE_X - 1u) / N_TILE_X;
    let height_in_bins = (config.height_in_tiles + N_TILE_Y - 1u) / N_TILE_Y;
    let total_bins = width_in_bins * height_in_bins;
    let header_stride = bin_header_stride(width_in_bins, height_in_bins);

    // This workgroup covers draw-partition wg_id.x and the N_TILE-wide bin
    // chunk starting at bin_chunk_start (inclusive) through bin_chunk_end
    // (exclusive) in the flat global bin grid.
    let partition_ix = wg_id.x;
    let bin_chunk_start = wg_id.y * N_TILE;
    let bin_chunk_end = min(bin_chunk_start + N_TILE, total_bins);

    // Read inputs and determine coverage of bins
    let element_ix = partition_ix * WG_SIZE + local_id.x;
    var x0 = 0;
    var y0 = 0;
    var x1 = 0;
    var y1 = 0;
    if element_ix < config.n_drawobj {
        let draw_monoid = draw_monoids[element_ix];
        var clip_bbox = vec4(-1e9, -1e9, 1e9, 1e9);
        if draw_monoid.clip_ix > 0u {
            // TODO: `clip_ix` should always be valid as long as the monoids are correct. Leaving
            // the bounds check in here for correctness but we should assert this condition instead
            // once there is a debug-assertion mechanism.
            clip_bbox = clip_bbox_buf[min(draw_monoid.clip_ix - 1u, config.n_clip - 1u)];
        }
        // For clip elements, clip_box is the bbox of the clip path,
        // intersected with enclosing clips.
        // For other elements, it is the bbox of the enclosing clips.
        // TODO check this is true

        let path_bbox = path_bbox_buf[draw_monoid.path_ix];
        let pb = vec4<f32>(vec4(path_bbox.x0, path_bbox.y0, path_bbox.x1, path_bbox.y1));
        let bbox = bbox_intersect(clip_bbox, pb);

        // Only the first bin-chunk workgroup writes the intersected bbox, since
        // the value is identical across chunks and later stages read it once.
        if wg_id.y == 0u {
            intersected_bbox[element_ix] = bbox;
        }

        // `bbox_intersect` can result in a zero or negative area intersection if the path bbox lies
        // outside the clip bbox. If that is the case, Don't round up the bottom-right corner of the
        // and leave the coordinates at 0. This way the path will get clipped out and won't get
        // assigned to a bin.
        if bbox.x < bbox.z && bbox.y < bbox.w {
            x0 = i32(floor(bbox.x * SX));
            y0 = i32(floor(bbox.y * SY));
            x1 = i32(ceil(bbox.z * SX));
            y1 = i32(ceil(bbox.w * SY));
        }
    }
    let width_in_bins_i = i32(width_in_bins);
    let height_in_bins_i = i32(height_in_bins);
    x0 = clamp(x0, 0, width_in_bins_i);
    y0 = clamp(y0, 0, height_in_bins_i);
    x1 = clamp(x1, 0, width_in_bins_i);
    y1 = clamp(y1, 0, height_in_bins_i);
    if x0 == x1 {
        y1 = y0;
    }
    var x = x0;
    var y = y0;
    let my_slice = local_id.x / 32u;
    let my_mask = 1u << (local_id.x & 31u);
    while y < y1 {
        let bin_ix = u32(y) * width_in_bins + u32(x);
        if bin_ix >= bin_chunk_start && bin_ix < bin_chunk_end {
            atomicOr(&sh_bitmaps[my_slice][bin_ix - bin_chunk_start], my_mask);
        }
        x += 1;
        if x == x1 {
            x = x0;
            y += 1;
        }
    }

    workgroupBarrier();
    // Allocate output segments
    var element_count = 0u;
    for (var i = 0u; i < N_SUBSLICE; i += 1u) {
        element_count += countOneBits(atomicLoad(&sh_bitmaps[i * 2u][local_id.x]));
        let element_count_lo = element_count;
        element_count += countOneBits(atomicLoad(&sh_bitmaps[i * 2u + 1u][local_id.x]));
        let element_count_hi = element_count;
        let element_count_packed = element_count_lo | (element_count_hi << 16u);
        sh_count[i][local_id.x] = element_count_packed;
    }
    // element_count is the number of draw objects covering this thread's bin
    var chunk_offset = atomicAdd(&bump.binning, element_count);
    var chunk_valid = 1u;
    if chunk_offset + element_count > config.binning_size {
        chunk_offset = 0u;
        chunk_valid = 0u;
        atomicOr(&bump.failed, STAGE_BINNING);
    }
    sh_chunk_offset[local_id.x] = chunk_offset;
    sh_chunk_valid[local_id.x] = chunk_valid;

    // Write the bin header for this thread's bin. Threads whose bin lies past
    // the real grid still write a zero-count header into the padded stride so
    // coarse sees a consistent layout.
    let this_bin_ix = bin_chunk_start + local_id.x;
    if this_bin_ix < header_stride {
        let header_ix = bin_header_ix(partition_ix, this_bin_ix, header_stride);
        info_bin_data[header_ix] = element_count;
        info_bin_data[header_ix + 1u] = chunk_offset;
    }
    workgroupBarrier();

    // loop over bbox of bins touched by this draw object
    x = x0;
    y = y0;
    while y < y1 {
        let bin_ix = u32(y) * width_in_bins + u32(x);
        if bin_ix >= bin_chunk_start && bin_ix < bin_chunk_end {
            let local_bin_ix = bin_ix - bin_chunk_start;
            let out_mask = atomicLoad(&sh_bitmaps[my_slice][local_bin_ix]);
            // I think this predicate will always be true...
            if (out_mask & my_mask) != 0u && sh_chunk_valid[local_bin_ix] != 0u {
                var idx = countOneBits(out_mask & (my_mask - 1u));
                if my_slice > 0u {
                    let count_ix = my_slice - 1u;
                    let count_packed = sh_count[count_ix / 2u][local_bin_ix];
                    idx += (count_packed >> (16u * (count_ix & 1u))) & 0xffffu;
                }
                let offset = config.bin_data_start + sh_chunk_offset[local_bin_ix];
                info_bin_data[offset + idx] = element_ix;
            }
        }
        x += 1;
        if x == x1 {
            x = x0;
            y += 1;
        }
    }
}
