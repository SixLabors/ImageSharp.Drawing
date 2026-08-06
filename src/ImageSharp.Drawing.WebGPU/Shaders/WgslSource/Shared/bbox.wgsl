// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

// Per-path device-space bounding box record.
//
// Cleared by bbox_clear.wgsl, atomically widened by path_lowering.wgsl (via its
// field-compatible AtomicPathBbox view of the same buffer), then consumed by
// binning.wgsl, clip_reduce.wgsl, clip_leaf.wgsl, and draw_leaf.wgsl.
// Ported from Vello's shader/shared/bbox.wgsl (linebender/vello) with
// ImageSharp additions (coverage_data, interest).

// The annotated bounding box for a transformed path.
// Coordinates are integer pixels (for the convenience of atomic update)
// but will probably become fixed-point fractions for rectangles.
//
// `draw_flags` is propagated to the draw info stream and carries fill-rule, aliasing, and blend state.
// Field layout must mirror AtomicPathBbox in path_lowering.wgsl.
struct PathBbox {
    x0: i32, // min bounds; i32 max while empty (see bbox_clear)
    y0: i32,
    x1: i32, // max bounds; x0 > x1 denotes an empty box
    y1: i32,
    draw_flags: u32,
    trans_ix: u32, // index of the path's transform in the scene stream
    // Per-fill antialiased coverage adjustment propagated from the style record to the fine pass.
    // Aliased fills use exact centre sampling and leave this value at zero.
    coverage_data: f32,
    _padding: u32,
    interest: vec4<f32>, // raster interest rect (x0, y0, x1, y1) in pixels
}

// Intersection of two (x0, y0, x1, y1) boxes; may produce an empty or
// inverted box, which callers must treat as zero area.
fn bbox_intersect(a: vec4<f32>, b: vec4<f32>) -> vec4<f32> {
    return vec4(max(a.xy, b.xy), min(a.zw, b.zw));
}
