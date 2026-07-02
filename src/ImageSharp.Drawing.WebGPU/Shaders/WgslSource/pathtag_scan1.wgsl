// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

// Middle stage of the large path tag scan: computes the exclusive prefix
// of the first-level partials given two levels of reduction. Only
// dispatched when the tag stream exceeds the single-level scan capacity.
//
// Inputs: reduced (first-level partials from pathtag_reduce), reduced2
// (second-level partials from pathtag_reduce2).
// Outputs: tag_monoids, here holding one exclusive prefix per first-level
// partial (granularity of 4 tag bytes * workgroup size); consumed as the
// per-workgroup carry-in by the large variant of pathtag_scan.
//
// Ported from Vello's pathtag_scan1.wgsl.

#import config
#import pathtag

@group(0) @binding(0)
var<storage> reduced: array<TagMonoid>;

@group(0) @binding(1)
var<storage> reduced2: array<TagMonoid>;

@group(0) @binding(2)
var<storage, read_write> tag_monoids: array<TagMonoid>;

const LG_WG_SIZE = 8u;
const WG_SIZE = 256u;

var<workgroup> sh_parent: array<TagMonoid, WG_SIZE>;
// Note: sh_parent and sh_monoid could potentially share storage.
var<workgroup> sh_monoid: array<TagMonoid, WG_SIZE>;

// Computes the exclusive prefix for each first-level partial. First
// suffix-reduces the second-level totals of all preceding workgroups into
// sh_parent[0] to form the carry-in, then performs an inclusive
// Hillis-Steele scan of this workgroup's first-level partials and combines
// carry-in with the preceding thread's inclusive value.
@compute @workgroup_size(256)
fn main(
    @builtin(global_invocation_id) global_id: vec3<u32>,
    @builtin(local_invocation_id) local_id: vec3<u32>,
    @builtin(workgroup_id) wg_id: vec3<u32>,
) {
    var agg = tag_monoid_identity();
    if local_id.x < wg_id.x {
        agg = reduced2[local_id.x];
    }
    sh_parent[local_id.x] = agg;
    for (var i = 0u; i < LG_WG_SIZE; i += 1u) {
        workgroupBarrier();
        if local_id.x + (1u << i) < WG_SIZE {
            let other = sh_parent[local_id.x + (1u << i)];
            agg = combine_tag_monoid(agg, other);
        }
        workgroupBarrier();
        sh_parent[local_id.x] = agg;
    }

    let ix = global_id.x;
    agg = reduced[ix];
    sh_monoid[local_id.x] = agg;
    for (var i = 0u; i < LG_WG_SIZE; i += 1u) {
        workgroupBarrier();
        if local_id.x >= 1u << i {
            let other = sh_monoid[local_id.x - (1u << i)];
            agg = combine_tag_monoid(other, agg);
        }
        workgroupBarrier();
        sh_monoid[local_id.x] = agg;
    }
    workgroupBarrier();
    // prefix up to this workgroup
    var tm = sh_parent[0];
    if local_id.x > 0u {
        tm = combine_tag_monoid(tm, sh_monoid[local_id.x - 1u]);
    }
    // exclusive prefix sum, granularity of 4 tag bytes * workgroup size
    tag_monoids[ix] = tm;
}
