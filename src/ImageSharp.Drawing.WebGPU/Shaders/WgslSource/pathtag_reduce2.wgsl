// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

// Second-level reduction for the path tag monoid scan, dispatched only
// when the tag stream is too large for a single level of workgroup
// partials (the "large" scan variant). Reduces the first-level partials
// produced by pathtag_reduce by another factor of WG_SIZE.
//
// Inputs: reduced_in (first-level partials from pathtag_reduce).
// Outputs: reduced (one TagMonoid per WG_SIZE input partials), consumed
// by pathtag_scan1.
//
// Ported from Vello's pathtag_reduce2.wgsl.

#import config
#import pathtag

@group(0) @binding(0)
var<storage> reduced_in: array<TagMonoid>;

@group(0) @binding(1)
var<storage, read_write> reduced: array<TagMonoid>;

const LG_WG_SIZE = 8u;
const WG_SIZE = 256u;

var<workgroup> sh_scratch: array<TagMonoid, WG_SIZE>;

// Reduces WG_SIZE first-level partials to a single TagMonoid using the
// same rightward shared-memory tree as pathtag_reduce; thread 0 writes
// the workgroup total to reduced[workgroup index].
@compute @workgroup_size(256)
fn main(
    @builtin(global_invocation_id) global_id: vec3<u32>,
    @builtin(local_invocation_id) local_id: vec3<u32>,
) {
    let ix = global_id.x;
    var agg = reduced_in[ix];
    sh_scratch[local_id.x] = agg;
    for (var i = 0u; i < firstTrailingBit(WG_SIZE); i += 1u) {
        workgroupBarrier();
        if local_id.x + (1u << i) < WG_SIZE {
            let other = sh_scratch[local_id.x + (1u << i)];
            agg = combine_tag_monoid(agg, other);
        }
        workgroupBarrier();
        sh_scratch[local_id.x] = agg;
    }
    if local_id.x == 0u {
        reduced[ix >> LG_WG_SIZE] = agg;
    }
}
