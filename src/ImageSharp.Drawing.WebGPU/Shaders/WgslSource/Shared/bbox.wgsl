// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

// The annotated bounding box for a transformed path.
// Coordinates are integer pixels (for the convenience of atomic update)
// but will probably become fixed-point fractions for rectangles.
//
// `draw_flags` is propagated to the draw info stream and carries fill-rule, aliasing, and blend state.
struct PathBbox {
    x0: i32,
    y0: i32,
    x1: i32,
    y1: i32,
    draw_flags: u32,
    trans_ix: u32,
    // Per-fill aliased coverage threshold, propagated from the style record to the fine pass.
    // A negative value means antialiased (analytic coverage); [0,1] means aliased at that cutoff.
    coverage_threshold: f32,
    _padding: u32,
    interest: vec4<f32>,
}

fn bbox_intersect(a: vec4<f32>, b: vec4<f32>) -> vec4<f32> {
    return vec4(max(a.xy, b.xy), min(a.zw, b.zw));
}
