// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

// Initializes the per-path bounding boxes to an empty (inverted) box so
// that later stages (flatten) can accumulate extents with atomic min/max.
//
// Inputs: config uniform (n_path).
// Outputs: path_bboxes, one PathBbox per path with min fields set to the
// i32 maximum and max fields set to the i32 minimum.
//
// Ported from Vello's bbox_clear.wgsl.

#import config
#import bbox

@group(0) @binding(0)
var<uniform> config: Config;

@group(0) @binding(1)
var<storage, read_write> path_bboxes: array<PathBbox>;

// Resets one path bbox per thread; any box where x0 > x1 after
// accumulation is treated as empty downstream.
@compute @workgroup_size(256)
fn main(
    @builtin(global_invocation_id) global_id: vec3<u32>,
) {
    let ix = global_id.x;
    if ix < config.n_path {
        path_bboxes[ix].x0 = 0x7fffffff;
        path_bboxes[ix].y0 = 0x7fffffff;
        path_bboxes[ix].x1 = -0x80000000;
        path_bboxes[ix].y1 = -0x80000000;
    }
}
