// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

// Types for the clip stack monoid used to resolve nested clips.
//
// Imported by clip_reduce.wgsl and clip_leaf.wgsl, which match BeginClip and
// EndClip records via a stack-monoid scan, and by draw_leaf.wgsl, which emits
// the ClipInp stream. Ported from Vello's shader/shared/clip.wgsl
// (linebender/vello) with the ImageSharp clip-operation extension.

// Stack-monoid element (the "bicyclic semigroup"): a is the number of
// unmatched EndClips (pops) and b the number of unmatched BeginClips
// (pushes) in a range of the clip stream.
struct Bic {
    a: u32,
    b: u32,
}

// The monoid combine operator: pops from y cancel pushes from x.
fn bic_combine(x: Bic, y: Bic) -> Bic {
    let m = min(x.b, y.a);
    return Bic(x.a + y.a - m, x.b + y.b - m);
}

// One clip stream record, written per BeginClip/EndClip draw object by
// draw_leaf (mirrored by GpuClipInp in WebGPUSceneResources.cs).
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

// ClipInp.operation values, matching ImageSharp's ClipOperation enum as
// encoded by the C# encoder. clip_leaf treats a Difference clip's bbox as
// infinite so it never narrows descendant bounds (removing area from a clip
// cannot be bounded by the removed shape).
const CLIP_OPERATION_INTERSECTION = 0u;
const CLIP_OPERATION_DIFFERENCE = 1u;

// Per-BeginClip stack element written by clip_reduce and consumed by
// clip_leaf when resolving each clip's ancestor chain.
struct ClipEl {
    parent_ix: u32, // index of the enclosing clip stream element
    bbox: vec4<f32>, // clip path bounds used to narrow descendant draw bboxes
}
