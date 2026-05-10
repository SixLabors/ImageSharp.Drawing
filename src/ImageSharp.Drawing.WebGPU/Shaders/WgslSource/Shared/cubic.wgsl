// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

struct Cubic {
    p0: vec2<f32>,
    p1: vec2<f32>,
    p2: vec2<f32>,
    p3: vec2<f32>,
    stroke: vec2<f32>,
    path_ix: u32,
    flags: u32,
}

const CUBIC_IS_STROKE = 1u;
