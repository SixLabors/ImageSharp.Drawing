// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

// Allocates sparse per-path tile-row metadata after draw reduction has
// produced final draw object bounds. One thread per draw object: converts
// the pixel-space draw bbox to a tile-space bbox clamped to the current
// chunk's tile-row window, bump-allocates one PathRow record per covered
// row, writes the Path record (tile bbox plus row base offset) and resets
// each row to an empty span (x0 = u32 max, x1 = 0, no backdrop, no flags).
//
// Inputs: config uniform (chunk window, buffer limits), scene (draw tags),
// draw_bboxes (from draw_leaf).
// Outputs: paths (Path records), rows (initialized AtomicPathRow records),
// bump.path_rows; sets the STAGE_TILE_ALLOC failure bit on overflow.
//
// Local addition for the sparse tile-row model; no Vello counterpart
// (it takes over the per-path setup half of Vello's tile_alloc, which
// allocates a dense tile grid instead).

#import config
#import bump
#import drawtag
#import bbox
#import tile

@group(0) @binding(0)
var<uniform> config: Config;

@group(0) @binding(1)
var<storage> scene: array<u32>;

@group(0) @binding(2)
var<storage> draw_bboxes: array<vec4<f32>>;

@group(0) @binding(3)
var<storage, read_write> bump: BumpAllocators;

@group(0) @binding(4)
var<storage, read_write> paths: array<Path>;

@group(0) @binding(5)
var<storage, read_write> rows: array<AtomicPathRow>;

@group(0) @binding(6)
// Original path bounds and drawing bounds. The reduced draw_bboxes buffer no longer contains the
// unclipped left edge needed to set PATH_FLAGS_CLIPPED_LEFT.
var<storage> path_bboxes: array<PathBbox>;

// Allocates and initializes the row records for one draw object. NOP and
// end-clip objects, and objects with an empty bbox, keep an all-zero tile
// bbox and therefore allocate no rows.
@compute @workgroup_size(256)
fn main(
    @builtin(global_invocation_id) global_id: vec3<u32>,
) {
    let drawobj_ix = global_id.x;
    if drawobj_ix >= config.n_drawobj {
        return;
    }

    let drawtag = scene[config.drawtag_base + drawobj_ix];
    let path_bbox = path_bboxes[drawobj_ix];
    // Only aliased fills use the original left edge. Fine needs this distinction for the one-pixel
    // extension of an interval that started before the drawing bounds.
    let path_flags = select(
        0u,
        PATH_FLAGS_CLIPPED_LEFT,
        (path_bbox.draw_flags & DRAW_INFO_FLAGS_ALIASED_BIT) != 0u && f32(path_bbox.x0) < path_bbox.interest.x);

    var ux0 = 0u;
    var uy0 = 0u;
    var ux1 = 0u;
    var uy1 = 0u;

    if drawtag != DRAWTAG_NOP && drawtag != DRAWTAG_END_CLIP {
        let bbox = draw_bboxes[drawobj_ix];
        if bbox.x < bbox.z && bbox.y < bbox.w {
            let chunk_y0 = i32(config.chunk_tile_y_start);
            let chunk_y1 = chunk_y0 + i32(config.chunk_tile_height);
            let x0 = i32(floor(bbox.x / f32(TILE_WIDTH)));
            let y0 = i32(floor(bbox.y / f32(TILE_HEIGHT)));
            let x1 = i32(ceil(bbox.z / f32(TILE_WIDTH)));
            let y1 = i32(ceil(bbox.w / f32(TILE_HEIGHT)));
            ux0 = u32(clamp(x0, 0, i32(config.width_in_tiles)));
            uy0 = u32(clamp(y0, chunk_y0, chunk_y1));
            ux1 = u32(clamp(x1, 0, i32(config.width_in_tiles)));
            uy1 = u32(clamp(y1, chunk_y0, chunk_y1));
        }
    }

    let bbox = vec4(ux0, uy0, ux1, uy1);
    let row_count = uy1 - uy0;
    let row_base = atomicAdd(&bump.path_rows, row_count);
    let row_limit_exceeded = row_base + row_count > config.path_rows_size;

    // On overflow still write a Path record (with row base 0) so the
    // buffer holds no uninitialized data; the failure bit makes the later
    // setup stages dispatch zero work, and the CPU resizes and retries.
    if row_limit_exceeded {
        atomicOr(&bump.failed, STAGE_TILE_ALLOC);
        paths[drawobj_ix] = Path(bbox, 0u, path_flags);
        return;
    }

    paths[drawobj_ix] = Path(bbox, row_base, path_flags);

    // Empty-span sentinel: x0 at u32 max and x1 at 0 so the atomicMin/Max
    // updates in path_row_span establish the true span; a row with
    // x0 >= x1 after that stage covers no tiles.
    for (var i = 0u; i < row_count; i += 1u) {
        let row_ix = row_base + i;
        atomicStore(&rows[row_ix].x0, 0xffffffffu);
        atomicStore(&rows[row_ix].x1, 0u);
        atomicStore(&rows[row_ix].backdrop, 0);
        atomicStore(&rows[row_ix].tiles, 0u);
    }
}
