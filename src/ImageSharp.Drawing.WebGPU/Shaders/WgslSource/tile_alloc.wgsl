// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

// The tile allocation stage.
//
// Finalizes the horizontal extent of every sparse path row and bump-allocates
// the backing Tile storage. A row is widened to the path bbox left edge when
// it carries a nonzero backdrop seed (the fill continues left of the leftmost
// recorded segment) and to the bbox right edge when a segment touched the
// right boundary (PATH_ROW_FLAG_TOUCHES_RIGHT). Each row's tiles field is
// then rewritten from its flag value to the base index of the row's
// contiguous tile run, and all allocated tiles are zeroed.
//
// Inputs: paths (tile-space bbox and row base, written by path_row_alloc),
// rows (extents, backdrop seeds, and flags accumulated by path_row_span).
// Outputs: rows (final x0/x1 and base tile index), tiles (zeroed),
// bump.tile.
//
// Derived from Vello's tile_alloc.wgsl. Local divergence: upstream allocates
// a dense width-by-height tile grid per path from the draw-object bboxes;
// this variant allocates only the tiles inside each sparse row span.

#import config
#import bump
#import tile

@group(0) @binding(0)
var<uniform> config: Config;

@group(0) @binding(1)
var<storage, read_write> bump: BumpAllocators;

@group(0) @binding(2)
var<storage> paths: array<Path>;

@group(0) @binding(3)
var<storage, read_write> rows: array<AtomicPathRow>;

@group(0) @binding(4)
var<storage, read_write> tiles: array<Tile>;

// One thread per draw object. Widens each of the object's rows, reserves the
// path's total tile count from bump.tile in a single allocation, assigns
// per-row base indices, and clears the newly allocated tiles.
@compute @workgroup_size(256)
fn main(
    @builtin(global_invocation_id) global_id: vec3<u32>,
) {
    // Row allocation overflowed; path_row_alloc already raised
    // STAGE_TILE_ALLOC, so bail without touching the (invalid) row records.
    if atomicLoad(&bump.path_rows) > config.path_rows_size {
        return;
    }

    let drawobj_ix = global_id.x;
    if drawobj_ix >= config.n_drawobj {
        return;
    }

    let path = paths[drawobj_ix];
    let row_count = path.bbox.w - path.bbox.y;

    var total_tile_count = 0u;
    for (var row = 0u; row < row_count; row += 1u) {
        let row_ix = path.rows + row;
        let stored_x0 = atomicLoad(&rows[row_ix].x0);
        let stored_x1 = atomicLoad(&rows[row_ix].x1);
        var x0 = stored_x0;
        var x1 = stored_x1;
        let backdrop = atomicLoad(&rows[row_ix].backdrop);
        // The tiles field is overloaded: it holds row flags until the second
        // loop below replaces it with the row's base tile index.
        let row_flags = atomicLoad(&rows[row_ix].tiles);
        if backdrop != 0 {
            x0 = min(x0, path.bbox.x);
        }

        if (row_flags & PATH_ROW_FLAG_TOUCHES_RIGHT) != 0u {
            x1 = max(x1, path.bbox.z);
        }

        if x0 != stored_x0 {
            atomicStore(&rows[row_ix].x0, x0);
        }

        if x1 != stored_x1 {
            atomicStore(&rows[row_ix].x1, x1);
        }

        if x0 < x1 {
            total_tile_count += x1 - x0;
        }
    }

    let tile_base = atomicAdd(&bump.tile, total_tile_count);
    if tile_base + total_tile_count > config.tiles_size {
        atomicOr(&bump.failed, STAGE_TILE_ALLOC);
        return;
    }

    var next_tile = tile_base;
    for (var row = 0u; row < row_count; row += 1u) {
        let row_ix = path.rows + row;
        let x0 = atomicLoad(&rows[row_ix].x0);
        let x1 = atomicLoad(&rows[row_ix].x1);
        atomicStore(&rows[row_ix].tiles, next_tile);
        if x0 < x1 {
            next_tile += x1 - x0;
        }
    }

    for (var i = 0u; i < total_tile_count; i += 1u) {
        tiles[tile_base + i] = Tile(0, 0u);
    }
}
