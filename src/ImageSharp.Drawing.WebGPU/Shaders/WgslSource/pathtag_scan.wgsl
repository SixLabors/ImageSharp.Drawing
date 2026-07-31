// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

// Final stage of the path tag monoid scan: writes, for every 4-byte tag
// word, the exclusive prefix TagMonoid (counts of transforms, segments,
// segment data words, styles and paths preceding that word). Flatten uses
// these prefixes to locate each path element's data in the scene buffer.
//
// Two compile-time variants exist, selected by the "small" define:
// - small: reduced holds per-workgroup totals from pathtag_reduce; this
//   shader scans them in shared memory to get the carry-in per workgroup.
// - large: reduced holds per-workgroup exclusive prefixes precomputed by
//   pathtag_scan1, read directly.
//
// Inputs: config uniform, scene tag words, reduced (see variants above).
// Outputs: tag_monoids (exclusive prefix per tag word).
//
// Ported from Vello's pathtag_scan.wgsl.

#import config
#import pathtag

@group(0) @binding(0)
var<uniform> config: Config;

@group(0) @binding(1)
var<storage> scene: array<u32>;

@group(0) @binding(2)
var<storage> reduced: array<TagMonoid>;

@group(0) @binding(3)
var<storage, read_write> tag_monoids: array<TagMonoid>;

const LG_WG_SIZE = 8u;
const WG_SIZE = 256u;

#ifdef small
var<workgroup> sh_parent: array<TagMonoid, WG_SIZE>;
#endif
// Note: sh_parent and sh_monoid could potentially share storage.
var<workgroup> sh_monoid: array<TagMonoid, WG_SIZE>;

// Computes the exclusive prefix monoid for each tag word in this
// workgroup's slice. In the small variant, first suffix-reduces the
// per-workgroup totals of all preceding workgroups into sh_parent[0] to
// form the carry-in; the large variant reads the carry-in directly from
// reduced[wg_id.x]. Then performs an inclusive Hillis-Steele scan of this
// slice's tag monoids in shared memory and combines carry-in with the
// preceding thread's inclusive value to produce the exclusive prefix.
@compute @workgroup_size(256)
fn main(
    @builtin(global_invocation_id) global_id: vec3<u32>,
    @builtin(local_invocation_id) local_id: vec3<u32>,
    @builtin(workgroup_id) wg_id: vec3<u32>,
) {
#ifdef small
    var agg = tag_monoid_identity();
    if local_id.x < wg_id.x {
        agg = reduced[local_id.x];
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
#endif

    let ix = global_id.x;
    let tag_word = scene[config.pathtag_base + ix];
    var agg_part = reduce_tag(tag_word);
    sh_monoid[local_id.x] = agg_part;
    for (var i = 0u; i < LG_WG_SIZE; i += 1u) {
        workgroupBarrier();
        if local_id.x >= 1u << i {
            let other = sh_monoid[local_id.x - (1u << i)];
            agg_part = combine_tag_monoid(other, agg_part);
        }
        workgroupBarrier();
        sh_monoid[local_id.x] = agg_part;
    }
    workgroupBarrier();
    // prefix up to this workgroup
#ifdef small
    var tm = sh_parent[0];
#else
    var tm = reduced[wg_id.x];
#endif
    if local_id.x > 0u {
        tm = combine_tag_monoid(tm, sh_monoid[local_id.x - 1u]);
    }
    // exclusive prefix sum, granularity of 4 tag bytes
    tag_monoids[ix] = tm;
}
