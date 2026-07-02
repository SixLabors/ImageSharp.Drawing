// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

// The backdrop propagation stage.
//
// Converts the per-tile backdrop deltas accumulated by earlier stages into
// absolute winding numbers. For each sparse path row, a running prefix sum
// walks the row's tiles left to right, seeded with the row's own backdrop,
// so that each tile's backdrop field ends up holding the winding number that
// applies at that tile (its own delta included).
//
// Inputs: paths (tile-space bbox and row base per draw object), rows (sparse
// row extents, seed backdrop, and base tile index), tiles (per-tile deltas).
// Outputs: tiles (backdrop rewritten in place as absolute winding).
//
// Derived from Vello's backdrop_dyn.wgsl. Local divergences: rows come from
// the sparse row records rather than a dense per-path tile grid, each row
// carries a backdrop seed for winding that enters from the left of its span,
// and rows are walked serially per draw object instead of being distributed
// across the workgroup.

#import bump
#import config
#import tile

@group(0) @binding(0)
var<uniform> config: Config;

@group(0) @binding(1)
var<storage, read_write> bump: BumpAllocators;

@group(0) @binding(2)
var<storage> paths: array<Path>;

@group(0) @binding(3)
var<storage> rows: array<PathRow>;

@group(0) @binding(4)
var<storage, read_write> tiles: array<Tile>;

// One thread per draw object. Exits the whole dispatch when any prior stage
// recorded an allocation failure, then prefix-sums the backdrop deltas of
// each of the object's sparse rows.
@compute @workgroup_size(256)
fn main(
    @builtin(global_invocation_id) global_id: vec3<u32>,
) {
    if atomicLoad(&bump.failed) != 0u {
        return;
    }

    let drawobj_ix = global_id.x;
    if drawobj_ix >= config.n_drawobj {
        return;
    }

    let path = paths[drawobj_ix];
    let row_count = path.bbox.w - path.bbox.y;
    for (var row = 0u; row < row_count; row += 1u) {
        let path_row = rows[path.rows + row];
        if path_row.x0 >= path_row.x1 {
            continue;
        }

        let width = path_row.x1 - path_row.x0;
        var tile_ix = path_row.tiles;
        // The row seed is the winding contributed by geometry left of the
        // row's first allocated tile; each tile then adds its own delta.
        var sum = path_row.backdrop;
        for (var x = 0u; x < width; x += 1u) {
            sum += tiles[tile_ix].backdrop;
            tiles[tile_ix].backdrop = sum;
            tile_ix += 1u;
        }
    }
}
