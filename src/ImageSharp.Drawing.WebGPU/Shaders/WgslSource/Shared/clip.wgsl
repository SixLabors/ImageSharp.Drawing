// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

struct Bic {
    a: u32,
    b: u32,
}

fn bic_combine(x: Bic, y: Bic) -> Bic {
    let m = min(x.b, y.a);
    return Bic(x.a + y.a - m, x.b + y.b - m);
}

struct ClipInp {
    // Index of the draw object.
    ix: u32,
    // This is a packed encoding of an enum with the sign bit as the tag. If positive,
    // this entry is a BeginClip and contains the associated path index. If negative,
    // it is an EndClip and contains the bitwise-not of the EndClip draw object index.
    path_ix: i32,
    // ImageSharp extension over Vello's clip record. Vello's Clip has only ix/path_ix;
    // we carry the render clip operation so Difference clips can remain stack records
    // instead of being rewritten as inverse geometry.
    operation: u32,
}

const CLIP_OPERATION_INTERSECTION = 0u;
const CLIP_OPERATION_DIFFERENCE = 1u;
const CLIP_DIFFERENCE_MASK_BIT = 0x80000000u;

struct ClipEl {
    parent_ix: u32,
    bbox: vec4<f32>,
}
