// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

// First stage of the multi-level parallel prefix scan over the packed path
// tag stream. Each workgroup reduces WG_SIZE consecutive tag words from the
// scene buffer into a single TagMonoid (running counts of transforms, path
// segments, segment data words, styles and paths) and writes one element
// per workgroup to the reduced buffer. The scan stages (pathtag_scan /
// pathtag_scan1) later turn these partials into exclusive prefixes.
//
// Inputs: config uniform, scene buffer (tag words at config.pathtag_base).
// Outputs: reduced (one TagMonoid per workgroup).
//
// Ported from Vello's pathtag_reduce.wgsl.

#import config
#import pathtag

@group(0) @binding(0)
var<uniform> config: Config;

@group(0) @binding(1)
var<storage> scene: array<u32>;

@group(0) @binding(2)
var<storage, read_write> reduced: array<TagMonoid>;

const LG_WG_SIZE = 8u;
const WG_SIZE = 256u;

var<workgroup> sh_scratch: array<TagMonoid, WG_SIZE>;

// Reduces one WG_SIZE slice of the tag stream to a single TagMonoid.
// Each thread reduces its own tag word, then a log2(WG_SIZE) shared-memory
// tree folds in partials from threads to the right, so after the loop
// thread 0 holds the reduction of the whole slice and writes it to
// reduced[workgroup index].
@compute @workgroup_size(256)
fn main(
    @builtin(global_invocation_id) global_id: vec3<u32>,
    @builtin(local_invocation_id) local_id: vec3<u32>,
) {
    let ix = global_id.x;
    let tag_word = scene[config.pathtag_base + ix];
    var agg = reduce_tag(tag_word);
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
