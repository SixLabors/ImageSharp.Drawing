// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

// The coarse rasterization stage.
//
// One workgroup per 16x16-tile bin (wg x = bin column, wg y = bin row within
// the current chunk); each of the 256 threads owns one tile. The stage
// streams the per-partition bin element lists written by binning, merges
// them back into draw order, marks which draw objects touch which tiles via
// shared bitmaps, and then each thread serializes its own tile's per-tile
// command list (PTCL) for the fine stage: fill/solid coverage, paint,
// and clip/blend commands.
//
// Inputs: scene (drawtags and drawdata), draw_monoids, info_bin_data (draw
// info, bin headers, and bin element lists), paths and rows (sparse
// tile-grid lookup), tiles (per-tile segment counts and backdrops).
// Outputs: ptcl (command lists), tiles (segment_count_or_ix rewritten to the
// allocated segment index), bump (ptcl, segments, blend_spill, failed).
//
// Derived from Vello's coarse.wgsl. Local divergences: sparse row-based tile
// lookup instead of dense per-path tile grids, per-draw coverage data and
// interest rectangles, centre-sampled aliased fills, difference clip bits,
// extra draw tags (recolor, elliptic/sweep/path gradients), and
// chunked rendering via config.chunk_tile_y_start.

#import config
#import bump
#import drawtag
#import ptcl
#import tile

@group(0) @binding(0)
var<uniform> config: Config;

@group(0) @binding(1)
var<storage> scene: array<u32>;

@group(0) @binding(2)
var<storage> draw_monoids: array<DrawMonoid>;

@group(0) @binding(3)
var<storage> info_bin_data: array<u32>;

@group(0) @binding(4)
var<storage> paths: array<Path>;

@group(0) @binding(5)
var<storage> rows: array<PathRow>;

@group(0) @binding(6)
var<storage, read_write> tiles: array<Tile>;

@group(0) @binding(7)
var<storage, read_write> bump: BumpAllocators;

@group(0) @binding(8)
var<storage, read_write> ptcl: array<u32>;

// Much of this code assumes WG_SIZE == N_TILE. If these diverge, then
// a fair amount of fixup is needed.
const WG_SIZE = 256u;
// Packed blend word (mix 128 = clip, compose 3 = src-over) identifying a
// plain clip layer with no custom blending.
const BLEND_CLIP = (128u << 8u) | 3u;
const N_SLICE = WG_SIZE / 32u;

var<workgroup> sh_bitmaps: array<array<atomic<u32>, N_TILE>, N_SLICE>;
var<workgroup> sh_part_count: array<u32, WG_SIZE>;
var<workgroup> sh_part_offsets: array<u32, WG_SIZE>;
var<workgroup> sh_drawobj_ix: array<u32, WG_SIZE>;
var<workgroup> sh_tile_count: array<u32, WG_SIZE>;

// Result of decode_bin_tile: a tile located by its bin-relative coordinates
// (x, y) plus its global storage index. valid is 0 when the sequential index
// fell outside the path's sparse rows.
struct SparseBinTileRef {
    valid: u32,
    x: u32,
    y: u32,
    tile_ix: u32,
}

// Result of lookup_tile: valid is 0 when the queried tile coordinates lie
// outside the path's sparse coverage.
struct SparseTileRef {
    valid: u32,
    tile_ix: u32,
}

// Per-partition, per-bin header written by the binning stage.
struct BinHeader {
    element_count: u32,
    chunk_offset: u32,
}

// helper functions for writing ptcl

var<private> cmd_offset: u32;
var<private> cmd_limit: u32;

// Ensures the current PTCL chunk has room for a command of the given size
// plus jump headroom. When the chunk is exhausted, bump-allocates a new one
// from bump.ptcl and links it with a CMD_JUMP; on overflow the coarse
// failure flag is raised and writes are redirected to the buffer start.
fn alloc_cmd(size: u32) {
    if cmd_offset + size >= cmd_limit {
        var new_cmd = atomicAdd(&bump.ptcl, PTCL_INCREMENT);
        if new_cmd + PTCL_INCREMENT > config.ptcl_size {
            // This sets us up for technical UB, as lots of threads will be writing
            // to the same locations. But I think it's fine, and predicating the
            // writes would probably slow things down.
            new_cmd = 0u;
            atomicOr(&bump.failed, STAGE_COARSE);
        }
        new_cmd += config.ptcl_dyn_start;
        ptcl[cmd_offset] = CMD_JUMP;
        ptcl[cmd_offset + 1u] = new_cmd;
        cmd_offset = new_cmd;
        cmd_limit = cmd_offset + (PTCL_INCREMENT - PTCL_HEADROOM);
    }
}

// Determines whether a tile with no crossing segments produces any visible
// coverage. The backdrop winding is resolved through the fill rule: even-odd
// folds the winding and non-zero saturates it. The same rule serves aliased
// fills, which sample the shape at pixel centres: a segment-free tile has one
// winding everywhere, so its pixels are all inside or all outside.
fn solid_tile_has_coverage(draw_flags: u32, backdrop: i32) -> bool {
    let even_odd = (draw_flags & DRAW_INFO_FLAGS_FILL_RULE_BIT) != 0u;
    var coverage = f32(backdrop);

    if even_odd {
        coverage = abs(coverage - 2.0 * round(0.5 * coverage));
    } else {
        coverage = min(abs(coverage), 1.0);
    }

    return coverage != 0.0;
}

// Writes the coverage command for a tile: CMD_FILL when line segments cross
// it (also reserving the tile's segment allocation and storing the inverted
// index back into the tile so path_tiling can find it), otherwise CMD_SOLID.
// When emit_empty_solid is false, solid tiles whose backdrop resolves to
// zero coverage are skipped entirely. Returns true when a command was
// written, meaning the caller should emit the matching paint command.
fn write_path(tile: Tile, tile_ix: u32, path: Path, global_x: u32, global_y: u32, draw_flags: u32, coverage_data: f32, interest: vec4<f32>, emit_empty_solid: bool) -> bool {
    // We overload the "segments" field to store both count (written by
    // path_count stage) and segment allocation (used by path_tiling and
    // fine).
    let n_segs = tile.segment_count_or_ix;
    if n_segs != 0u {
        var seg_ix = atomicAdd(&bump.segments, n_segs);
        tiles[tile_ix].segment_count_or_ix = ~seg_ix;
        alloc_cmd(9u);
        ptcl[cmd_offset] = CMD_FILL;
        let even_odd = (draw_flags & DRAW_INFO_FLAGS_FILL_RULE_BIT) != 0u;
        let aliased = (draw_flags & DRAW_INFO_FLAGS_ALIASED_BIT) != 0u;
        var packed_coverage_data = bitcast<u32>(coverage_data);
        if aliased {
            // Pack the data needed by the aliased half-pixel halo into one word:
            // bits 0..28 contain this path-tile index;
            // bit 29 means the original path started left of its clipped drawing bounds;
            // bit 30 means the tile on the left has segments for the same path row;
            // bit 31 means the tile on the right has segments for the same path row.
            // A path-tile record is eight bytes, so its storage-buffer index fits in 29 bits.
            var tile_and_neighbors = tile_ix;
            if (path.flags & PATH_FLAGS_CLIPPED_LEFT) != 0u {
                tile_and_neighbors |= ALIASED_CLIPPED_LEFT_BIT;
            }

            if global_x > 0u {
                let left = lookup_tile(path, global_x - 1u, global_y);
                if left.valid != 0u && tiles[left.tile_ix].segment_count_or_ix != 0u {
                    tile_and_neighbors |= ALIASED_LEFT_NEIGHBOR_BIT;
                }
            }

            let right = lookup_tile(path, global_x + 1u, global_y);
            if right.valid != 0u && tiles[right.tile_ix].segment_count_or_ix != 0u {
                tile_and_neighbors |= ALIASED_RIGHT_NEIGHBOR_BIT;
            }

            packed_coverage_data = tile_and_neighbors;
        }

        // size_and_rule: bit 0 = even-odd, bit 1 = aliased coverage, bits 2.. = segment count.
        let size_and_rule = (n_segs << 2u) | (u32(aliased) << 1u) | u32(even_odd);
        let fill = CmdFill(size_and_rule, seg_ix, tile.backdrop, packed_coverage_data, interest);
        ptcl[cmd_offset + 1u] = fill.size_and_rule;
        ptcl[cmd_offset + 2u] = fill.seg_data;
        ptcl[cmd_offset + 3u] = u32(fill.backdrop);
        ptcl[cmd_offset + 4u] = fill.coverage_data;
        ptcl[cmd_offset + 5u] = bitcast<u32>(fill.interest.x);
        ptcl[cmd_offset + 6u] = bitcast<u32>(fill.interest.y);
        ptcl[cmd_offset + 7u] = bitcast<u32>(fill.interest.z);
        ptcl[cmd_offset + 8u] = bitcast<u32>(fill.interest.w);
        // The winding backdrop is now in PTCL. Reuse its tile field for the original segment
        // count so fine can read adjacent slices after segment_count_or_ix becomes the allocation.
        tiles[tile_ix].backdrop = i32(n_segs);
        cmd_offset += 9u;
        return true;
    } else {
        if !emit_empty_solid && !solid_tile_has_coverage(draw_flags, tile.backdrop) {
            return false;
        }

        alloc_cmd(5u);
        ptcl[cmd_offset] = CMD_SOLID;
        ptcl[cmd_offset + 1u] = bitcast<u32>(interest.x);
        ptcl[cmd_offset + 2u] = bitcast<u32>(interest.y);
        ptcl[cmd_offset + 3u] = bitcast<u32>(interest.z);
        ptcl[cmd_offset + 4u] = bitcast<u32>(interest.w);
        cmd_offset += 5u;
        return true;
    }
}

// Emits a CMD_COLOR paint command (binary16 RGBA color plus draw flags).
fn write_color(color: CmdColor) {
    alloc_cmd(4u);
    ptcl[cmd_offset] = CMD_COLOR;
    ptcl[cmd_offset + 1u] = color.color_rg;
    ptcl[cmd_offset + 2u] = color.color_ba;
    ptcl[cmd_offset + 3u] = color.draw_flags;
    cmd_offset += 4u;
}

// Emits a CMD_RECOLOR command referencing one target-specialized auxiliary record.
fn write_recolor(data_offset: u32, draw_flags: u32) {
    alloc_cmd(3u);
    ptcl[cmd_offset] = CMD_RECOLOR;
    ptcl[cmd_offset + 1u] = data_offset;
    ptcl[cmd_offset + 2u] = draw_flags;
    cmd_offset += 3u;
}

// Emits a gradient paint command. ty selects the CMD_*_GRAD opcode, index is
// the packed gradient index word from the scene, and info_offset points at
// the gradient parameters computed by draw_leaf.
fn write_grad(ty: u32, index: u32, info_offset: u32) {
    alloc_cmd(3u);
    ptcl[cmd_offset] = ty;
    ptcl[cmd_offset + 1u] = index;
    ptcl[cmd_offset + 2u] = info_offset;
    cmd_offset += 3u;
}

// Emits a CMD_PATH_GRAD paint command. data_offset points at the packed edge
// data in the combined info/bin-data buffer; edge_count edges follow.
fn write_path_grad(data_offset: u32, edge_count: u32, flags: u32, draw_flags: u32) {
    alloc_cmd(5u);
    ptcl[cmd_offset] = CMD_PATH_GRAD;
    ptcl[cmd_offset + 1u] = data_offset;
    ptcl[cmd_offset + 2u] = edge_count;
    ptcl[cmd_offset + 3u] = flags;
    ptcl[cmd_offset + 4u] = draw_flags;
    cmd_offset += 5u;
}

// Emits a CMD_IMAGE paint command; info_offset points at the image draw info
// (transform, extents, and atlas placement) written by draw_leaf.
fn write_image(info_offset: u32) {
    alloc_cmd(2u);
    ptcl[cmd_offset] = CMD_IMAGE;
    ptcl[cmd_offset + 1u] = info_offset;
    cmd_offset += 2u;
}

// Emits CMD_BEGIN_CLIP, which pushes a new group in the fine stage. The payload word
// carries bit 0 = isolated: 1 seeds the group transparent (layer semantics), 0 seeds it
// with a copy of the current tile content (clip mask semantics).
fn write_begin_clip(isolated: u32) {
    alloc_cmd(2u);
    ptcl[cmd_offset] = CMD_BEGIN_CLIP;
    ptcl[cmd_offset + 1u] = isolated;
    cmd_offset += 2u;
}

// Emits CMD_END_CLIP with the blend word and alpha used to composite the
// layer back onto its parent.
fn write_end_clip(end_clip: CmdEndClip) {
    alloc_cmd(3u);
    ptcl[cmd_offset] = CMD_END_CLIP;
    ptcl[cmd_offset + 1u] = end_clip.blend;
    ptcl[cmd_offset + 2u] = bitcast<u32>(end_clip.alpha);
    cmd_offset += 3u;
}

// Counts how many of the path's sparse tiles fall inside the bin whose
// top-left tile is (bin_tile_x, bin_tile_y), by clipping each row span to
// the bin's horizontal range.
fn get_bin_tile_count(path: Path, bin_tile_x: u32, bin_tile_y: u32) -> u32 {
    let y0 = max(path.bbox.y, bin_tile_y);
    let y1 = min(path.bbox.w, bin_tile_y + N_TILE_Y);
    var count = 0u;
    for (var y = y0; y < y1; y += 1u) {
        let row = rows[path.rows + y - path.bbox.y];
        let x0 = max(row.x0, bin_tile_x);
        let x1 = min(row.x1, bin_tile_x + N_TILE_X);
        if x0 < x1 {
            count += x1 - x0;
        }
    }

    return count;
}

// Maps a sequential tile index within this bin (seq_ix, in the same
// row-major order counted by get_bin_tile_count) back to a concrete tile.
// Returns bin-relative coordinates plus the global tile index, or valid = 0
// when seq_ix falls outside the path's coverage of the bin.
fn decode_bin_tile(path: Path, bin_tile_x: u32, bin_tile_y: u32, seq_ix: u32) -> SparseBinTileRef {
    let y0 = max(path.bbox.y, bin_tile_y);
    let y1 = min(path.bbox.w, bin_tile_y + N_TILE_Y);
    var remaining = seq_ix;
    for (var y = y0; y < y1; y += 1u) {
        let row = rows[path.rows + y - path.bbox.y];
        let x0 = max(row.x0, bin_tile_x);
        let x1 = min(row.x1, bin_tile_x + N_TILE_X);
        if x0 < x1 {
            let width = x1 - x0;
            if remaining < width {
                let x = x0 + remaining;
                let tile_ix = row.tiles + x - row.x0;
                return SparseBinTileRef(1u, x - bin_tile_x, y - bin_tile_y, tile_ix);
            }

            remaining -= width;
        }
    }

    return SparseBinTileRef(0u, 0u, 0u, 0u);
}

// Resolves the tile storage index for global tile coordinates through the
// path's sparse rows. Returns valid = 0 when the coordinates fall outside
// the path's row coverage.
fn lookup_tile(path: Path, global_x: u32, global_y: u32) -> SparseTileRef {
    if global_y < path.bbox.y || global_y >= path.bbox.w {
        return SparseTileRef(0u, 0u);
    }

    let row = rows[path.rows + global_y - path.bbox.y];
    if global_x < row.x0 || global_x >= row.x1 {
        return SparseTileRef(0u, 0u);
    }

    return SparseTileRef(1u, row.tiles + global_x - row.x0);
}

// Reads the (element count, chunk offset) bin header written by the binning
// stage. bin_ix is the flat header slot index, already including the
// per-partition stride; headers live after the binning_size element-list
// region of info_bin_data.
fn load_bin_header(bin_ix: u32) -> BinHeader {
    let base = config.bin_data_start + config.binning_size + (bin_ix * 2u);
    return BinHeader(info_bin_data[base], info_bin_data[base + 1u]);
}

// One workgroup per bin; each thread owns one tile. The outer loop batches
// up to N_TILE draw objects at a time: bin element lists from successive
// partitions are prefix-summed and binary-searched into a contiguous,
// draw-ordered window (sh_drawobj_ix), each object's bin tiles are scattered
// into per-tile bitmaps, and finally every thread walks its own tile's
// bitmap in draw order to emit PTCL commands, tracking clip and blend depth
// as it goes.
@compute @workgroup_size(256)
fn main(
    @builtin(local_invocation_id) local_id: vec3<u32>,
    @builtin(workgroup_id) wg_id: vec3<u32>,
) {
    // Exit early if prior stages failed, as we can't run this stage.
    // We need to check only prior stages, as if this stage has failed in
    // another workgroup, we still want to know this workgroup's memory
    // requirement.
    if local_id.x == 0u {
        var failed = atomicLoad(&bump.failed) & (STAGE_BINNING | STAGE_TILE_ALLOC | STAGE_PATH_LOWERING);
        if atomicLoad(&bump.seg_counts) > config.seg_counts_size {
            failed |= STAGE_PATH_COUNT;
        }
        // Reuse sh_part_count to hold failed flag, shmem is tight
        sh_part_count[0] = u32(failed);
    }
    let failed = workgroupUniformLoad(&sh_part_count[0]);
    if failed != 0u {
        if wg_id.x == 0u && local_id.x == 0u {
            // propagate PATH_COUNT failure to path_tiling_setup so it doesn't need to bind config
            atomicOr(&bump.failed, failed);
        }
        return;
    }
    let width_in_bins = (config.width_in_tiles + N_TILE_X - 1u) / N_TILE_X;
    let height_in_bins = (config.height_in_tiles + N_TILE_Y - 1u) / N_TILE_Y;
    let chunk_bin_y = config.chunk_tile_y_start / N_TILE_Y;
    let bin_ix = width_in_bins * (chunk_bin_y + wg_id.y) + wg_id.x;
    let n_partitions = (config.n_drawobj + N_TILE - 1u) / N_TILE;
    // Bin-header stride per draw-partition: the full bin grid aligned up to
    // N_TILE so every binning workgroup contributes a dense slot block.
    let n_bins_total = width_in_bins * height_in_bins;
    let bin_header_stride = (n_bins_total + N_TILE - 1u) / N_TILE * N_TILE;

    // Coordinates of the top left of this bin, in tiles.
    let bin_tile_x = N_TILE_X * wg_id.x;
    let bin_tile_y = config.chunk_tile_y_start + N_TILE_Y * wg_id.y;

    let tile_x = local_id.x % N_TILE_X;
    let tile_y = local_id.x / N_TILE_X;
    let this_tile_ix = (N_TILE_Y * wg_id.y + tile_y) * config.width_in_tiles + bin_tile_x + tile_x;
    cmd_offset = this_tile_ix * PTCL_INITIAL_ALLOC;
    cmd_limit = cmd_offset + (PTCL_INITIAL_ALLOC - PTCL_HEADROOM);

    // clip state
    var clip_zero_depth = 0u;
    var clip_depth = 0u;

    var partition_ix = 0u;
    var rd_ix = 0u;
    var wr_ix = 0u;
    var part_start_ix = 0u;
    var ready_ix = 0u;

    // blend state
    var render_blend_depth = 0u;
    var max_blend_depth = 0u;

    // The first word of each tile's PTCL is reserved for the blend-spill
    // offset and patched in after the command list is complete.
    let blend_offset = cmd_offset;
    cmd_offset += 1u;

    while true {
        for (var i = 0u; i < N_SLICE; i += 1u) {
            atomicStore(&sh_bitmaps[i][local_id.x], 0u);
        }

        while true {
            if ready_ix == wr_ix && partition_ix < n_partitions {
                part_start_ix = ready_ix;
                var count = 0u;
                if partition_ix + local_id.x < n_partitions {
                    let in_ix = (partition_ix + local_id.x) * bin_header_stride + bin_ix;
                    let bin_header = load_bin_header(in_ix);
                    count = bin_header.element_count;
                    sh_part_offsets[local_id.x] = bin_header.chunk_offset;
                }
                // prefix sum the element counts
                for (var i = 0u; i < firstTrailingBit(WG_SIZE); i += 1u) {
                    sh_part_count[local_id.x] = count;
                    workgroupBarrier();
                    if local_id.x >= (1u << i) {
                        count += sh_part_count[local_id.x - (1u << i)];
                    }
                    workgroupBarrier();
                }
                sh_part_count[local_id.x] = part_start_ix + count;
                ready_ix = workgroupUniformLoad(&sh_part_count[WG_SIZE - 1u]);
                partition_ix += WG_SIZE;
            }
            // use binary search to find draw object to read
            var ix = rd_ix + local_id.x;
            if ix >= wr_ix && ix < ready_ix {
                var part_ix = 0u;
                for (var i = 0u; i < firstTrailingBit(WG_SIZE); i += 1u) {
                    let probe = part_ix + ((N_TILE / 2u) >> i);
                    if ix >= sh_part_count[probe - 1u] {
                        part_ix = probe;
                    }
                }
                ix -= select(part_start_ix, sh_part_count[part_ix - 1u], part_ix > 0u);
                let offset = config.bin_data_start + sh_part_offsets[part_ix];
                sh_drawobj_ix[local_id.x] = info_bin_data[offset + ix];
            }
            wr_ix = min(rd_ix + N_TILE, ready_ix);
            if wr_ix - rd_ix >= N_TILE || (wr_ix >= ready_ix && partition_ix >= n_partitions) {
                break;
            }
            workgroupBarrier();
        }
        // At this point, sh_drawobj_ix[0.. wr_ix - rd_ix] contains merged binning results.
        var tag = DRAWTAG_NOP;
        var drawobj_ix: u32;
        if local_id.x + rd_ix < wr_ix {
            drawobj_ix = sh_drawobj_ix[local_id.x];
            tag = scene[config.drawtag_base + drawobj_ix];
        }

        var tile_count = 0u;
        // I think this predicate is the same as the last, maybe they can be combined
        if tag != DRAWTAG_NOP {
            let path_ix = draw_monoids[drawobj_ix].path_ix;
            let path = paths[path_ix];
            tile_count = get_bin_tile_count(path, bin_tile_x, bin_tile_y);
        }

        // Prefix sum of tile counts
        sh_tile_count[local_id.x] = tile_count;
        for (var i = 0u; i < firstTrailingBit(N_TILE); i += 1u) {
            workgroupBarrier();
            if local_id.x >= (1u << i) {
                tile_count += sh_tile_count[local_id.x - (1u << i)];
            }
            workgroupBarrier();
            sh_tile_count[local_id.x] = tile_count;
        }
        workgroupBarrier();
        let total_tile_count = sh_tile_count[N_TILE - 1u];
        // Parallel iteration over all tiles
        for (var ix = local_id.x; ix < total_tile_count; ix += N_TILE) {
            // Binary search to find draw object which contains this tile
            var el_ix = 0u;
            for (var i = 0u; i < firstTrailingBit(N_TILE); i += 1u) {
                let probe = el_ix + ((N_TILE / 2u) >> i);
                if ix >= sh_tile_count[probe - 1u] {
                    el_ix = probe;
                }
            }
            drawobj_ix = sh_drawobj_ix[el_ix];
            tag = scene[config.drawtag_base + drawobj_ix];
            let seq_ix = ix - select(0u, sh_tile_count[el_ix - 1u], el_ix > 0u);
            let path_ix = draw_monoids[drawobj_ix].path_ix;
            let path = paths[path_ix];
            let tile_ref = decode_bin_tile(path, bin_tile_x, bin_tile_y, seq_ix);
            if tile_ref.valid == 0u {
                continue;
            }

            let x = tile_ref.x;
            let y = tile_ref.y;
            let tile_ix = tile_ref.tile_ix;
            let tile = tiles[tile_ix];
            // Bit 0 of the draw tag marks clip operations (begin and end).
            let is_clip = (tag & 1u) != 0u;
            var is_blend = false;
            var is_difference_clip = false;
            if is_clip {
                let scene_offset = draw_monoids[drawobj_ix].scene_offset;
                let dd = config.drawdata_base + scene_offset;
                // Difference clips carry their operation in the high bit of the blend word.
                // Coarse only needs to know whether this is a plain clip marker or a true
                // blend layer, so mask the operation bit before comparing with BLEND_CLIP.
                let raw_blend = scene[dd];
                is_difference_clip = (raw_blend & CLIP_DIFFERENCE_MASK_BIT) != 0u;
                var blend = raw_blend & ~(CLIP_DIFFERENCE_MASK_BIT | CLIP_ISOLATED_MASK_BIT);

                // Isolated groups (layers) must never take the solid-tile skip below: their
                // contents composite against a transparent seed, so skipping the group would
                // change results for any non-src-over content inside. Treat them like blend
                // layers so every covered tile opens the group.
                is_blend = blend != BLEND_CLIP || (raw_blend & CLIP_ISOLATED_MASK_BIT) != 0u;
            }

            let di = draw_monoids[drawobj_ix].info_offset;
            let draw_flags = info_bin_data[di];
            let even_odd = (draw_flags & DRAW_INFO_FLAGS_FILL_RULE_BIT) != 0u;
            let n_segs = tile.segment_count_or_ix;

            // If this draw object represents an even-odd fill and we know that no line segment
            // crosses this tile, then this draw object should not contribute to the tile if its
            // backdrop (i.e. the winding number of its top-left corner) is even.
            let backdrop_clear = select(tile.backdrop, abs(tile.backdrop) & 1, even_odd) == 0;
            let include_clip_tile = select(backdrop_clear, !backdrop_clear, is_difference_clip);
            let include_tile = n_segs != 0u || (include_clip_tile == is_clip) || is_blend;
            if include_tile {
                let el_slice = el_ix / 32u;
                let el_mask = 1u << (el_ix & 31u);
                atomicOr(&sh_bitmaps[el_slice][y * N_TILE_X + x], el_mask);
            }
        }
        workgroupBarrier();
        // At this point bit drawobj % 32 is set in sh_bitmaps[drawobj / 32][y * N_TILE_X + x]
        // if drawobj touches tile (x, y).

        // Write per-tile command list for this tile
        var slice_ix = 0u;
        var bitmap = atomicLoad(&sh_bitmaps[0u][local_id.x]);
        while true {
            if bitmap == 0u {
                slice_ix += 1u;
                // potential optimization: make iteration limit dynamic
                if slice_ix == N_SLICE {
                    break;
                }
                bitmap = atomicLoad(&sh_bitmaps[slice_ix][local_id.x]);
                if bitmap == 0u {
                    continue;
                }
            }

            let el_ix = slice_ix * 32u + firstTrailingBit(bitmap);
            drawobj_ix = sh_drawobj_ix[el_ix];
            // clear LSB of bitmap, using bit magic
            bitmap &= bitmap - 1u;
            let drawtag = scene[config.drawtag_base + drawobj_ix];
            let dm = draw_monoids[drawobj_ix];
            let dd = config.drawdata_base + dm.scene_offset;
            let di = dm.info_offset;
            let draw_flags = info_bin_data[di];
            var coverage_data = 0.0;
            var interest = vec4<f32>(0.0, 0.0, f32(config.target_width), f32(config.target_height));
            // Draw tags whose info block spans at least five words append a
            // coverage data plus interest rectangle at the end of it.
            let drawtag_info_size = (drawtag >> 6u) & 0xfu;
            if drawtag_info_size >= 5u {
                let interest_offset = di + drawtag_info_size - 5u;
                coverage_data = bitcast<f32>(info_bin_data[interest_offset]);
                interest = vec4<f32>(
                    bitcast<f32>(info_bin_data[interest_offset + 1u]),
                    bitcast<f32>(info_bin_data[interest_offset + 2u]),
                    bitcast<f32>(info_bin_data[interest_offset + 3u]),
                    bitcast<f32>(info_bin_data[interest_offset + 4u]));
            }

            if clip_zero_depth == 0u {
                let path = paths[dm.path_ix];
                let tile_ref = lookup_tile(path, bin_tile_x + tile_x, bin_tile_y + tile_y);
                if tile_ref.valid == 0u {
                    continue;
                }

                let tile_ix = tile_ref.tile_ix;
                let tile = tiles[tile_ix];
                switch drawtag {
                    case DRAWTAG_FILL_COLOR: {
                        if write_path(tile, tile_ix, path, bin_tile_x + tile_x, bin_tile_y + tile_y, draw_flags, coverage_data, interest, false) {
                            write_color(CmdColor(scene[dd], scene[dd + 1u], draw_flags));
                        }
                    }
                    case DRAWTAG_FILL_RECOLOR: {
                        if write_path(tile, tile_ix, path, bin_tile_x + tile_x, bin_tile_y + tile_y, draw_flags, coverage_data, interest, false) {
                            write_recolor(config.brush_data_base + scene[dd], draw_flags);
                        }
                    }
                    case DRAWTAG_FILL_LIN_GRADIENT: {
                        if write_path(tile, tile_ix, path, bin_tile_x + tile_x, bin_tile_y + tile_y, draw_flags, coverage_data, interest, false) {
                            let index = scene[dd];
                            let info_offset = di + 1u;
                            write_grad(CMD_LIN_GRAD, index, info_offset);
                        }
                    }
                    case DRAWTAG_FILL_RAD_GRADIENT: {
                        if write_path(tile, tile_ix, path, bin_tile_x + tile_x, bin_tile_y + tile_y, draw_flags, coverage_data, interest, false) {
                            let index = scene[dd];
                            let info_offset = di + 1u;
                            write_grad(CMD_RAD_GRAD, index, info_offset);
                        }
                    }
                    case DRAWTAG_FILL_ELLIPTIC_GRADIENT: {
                        if write_path(tile, tile_ix, path, bin_tile_x + tile_x, bin_tile_y + tile_y, draw_flags, coverage_data, interest, false) {
                            let index = scene[dd];
                            let info_offset = di + 1u;
                            write_grad(CMD_ELLIPTIC_GRAD, index, info_offset);
                        }
                    }
                    case DRAWTAG_FILL_SWEEP_GRADIENT: {
                        if write_path(tile, tile_ix, path, bin_tile_x + tile_x, bin_tile_y + tile_y, draw_flags, coverage_data, interest, false) {
                            let index = scene[dd];
                            let info_offset = di + 1u;
                            write_grad(CMD_SWEEP_GRAD, index, info_offset);
                        }
                    }
                    case DRAWTAG_FILL_PATH_GRADIENT: {
                        if write_path(tile, tile_ix, path, bin_tile_x + tile_x, bin_tile_y + tile_y, draw_flags, coverage_data, interest, false) {
                            write_path_grad(config.brush_data_base + scene[dd], scene[dd + 1u], scene[dd + 2u], draw_flags);
                        }
                    }
                    case DRAWTAG_FILL_IMAGE: {
                        if write_path(tile, tile_ix, path, bin_tile_x + tile_x, bin_tile_y + tile_y, draw_flags, coverage_data, interest, false) {
                            write_image(di + 1u);
                        }
                    }
                    case DRAWTAG_BEGIN_CLIP: {
                        let even_odd = (draw_flags & DRAW_INFO_FLAGS_FILL_RULE_BIT) != 0u;
                        let backdrop_clear = select(tile.backdrop, abs(tile.backdrop) & 1, even_odd) == 0;
                        let raw_blend = scene[dd];
                        let is_difference_clip = (raw_blend & CLIP_DIFFERENCE_MASK_BIT) != 0u;
                        let retained_area_clear = select(backdrop_clear, !backdrop_clear, is_difference_clip);
                        if tile.segment_count_or_ix == 0u && retained_area_clear {
                            clip_zero_depth = clip_depth + 1u;
                        } else {
                            let isolated = u32((raw_blend & CLIP_ISOLATED_MASK_BIT) != 0u);
                            write_begin_clip(isolated);
                            render_blend_depth += 1u;
                            max_blend_depth = max(max_blend_depth, render_blend_depth);
                        }

                        clip_depth += 1u;
                    }
                    case DRAWTAG_END_CLIP: {
                        clip_depth -= 1u;
                        let blend = scene[dd];
                        write_path(tile, tile_ix, path, bin_tile_x + tile_x, bin_tile_y + tile_y, draw_flags, coverage_data, interest, true);
                        let alpha = bitcast<f32>(scene[dd + 1u]);
                        write_end_clip(CmdEndClip(blend, alpha));
                        render_blend_depth -= 1u;
                    }
                    default: {}
                }
            } else {
                // In "clip zero" state, suppress all drawing
                switch drawtag {
                    case DRAWTAG_BEGIN_CLIP: {
                        clip_depth += 1u;
                    }
                    case DRAWTAG_END_CLIP: {
                        if clip_depth == clip_zero_depth {
                            clip_zero_depth = 0u;
                        }
                        clip_depth -= 1u;
                    }
                    default: {}
                }
            }
        }

        rd_ix += N_TILE;
        if rd_ix >= ready_ix && partition_ix >= n_partitions {
            break;
        }
        workgroupBarrier();
    }
    // Only tiles inside the real viewport finalize their command list: write
    // CMD_END and, when the clip stack ran deeper than the in-register blend
    // stack, reserve blend-spill scratch and patch its offset into the
    // reserved first word.
    if bin_tile_x + tile_x < config.width_in_tiles && bin_tile_y + tile_y < config.height_in_tiles {
        ptcl[cmd_offset] = CMD_END;
        var blend_ix = 0u;
        if max_blend_depth > BLEND_STACK_SPLIT {
            let scratch_size = (max_blend_depth - BLEND_STACK_SPLIT) * TILE_WIDTH * TILE_HEIGHT;
            blend_ix = atomicAdd(&bump.blend_spill, scratch_size);
            if blend_ix + scratch_size > config.blend_size {
                atomicOr(&bump.failed, STAGE_COARSE);
            }
        }
        ptcl[blend_offset] = blend_ix;
    }
}
