// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

// Allocate sparse per-path row metadata after draw reduction has produced final path bounds.

#import config
#import bump
#import drawtag
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

@compute @workgroup_size(256)
fn main(
    @builtin(global_invocation_id) global_id: vec3<u32>,
) {
    let drawobj_ix = global_id.x;
    if drawobj_ix >= config.n_drawobj {
        return;
    }

    let drawtag = scene[config.drawtag_base + drawobj_ix];

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

    if row_limit_exceeded {
        atomicOr(&bump.failed, STAGE_TILE_ALLOC);
        paths[drawobj_ix] = Path(bbox, 0u);
        return;
    }

    paths[drawobj_ix] = Path(bbox, row_base);

    for (var i = 0u; i < row_count; i += 1u) {
        let row_ix = row_base + i;
        atomicStore(&rows[row_ix].x0, 0xffffffffu);
        atomicStore(&rows[row_ix].x1, 0u);
        atomicStore(&rows[row_ix].backdrop, 0);
        atomicStore(&rows[row_ix].tiles, 0u);
    }
}
