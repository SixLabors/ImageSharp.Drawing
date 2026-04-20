// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

// Prefix sum for dynamically allocated backdrops

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
        var sum = path_row.backdrop;
        for (var x = 0u; x < width; x += 1u) {
            sum += tiles[tile_ix].backdrop;
            tiles[tile_ix].backdrop = sum;
            tile_ix += 1u;
        }
    }
}
