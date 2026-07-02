// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

// Computes the indirect dispatch size for the path tiling stage from the
// number of SegmentCount records produced by path_count, and performs the
// late overflow check for that counter.
//
// Inputs: config uniform (segments_size), bump (seg_counts, failed mask).
// Outputs: indirect (workgroup counts for path_tiling). On a prior stage
// failure or seg_counts overflow the dispatch is zeroed, STAGE_COARSE is
// flagged for the overflow case, and ptcl[0] is set to ~0u as an abort
// marker for the fine stage.
//
// Ported from Vello's path_tiling_setup.wgsl.

#import config
#import bump

@group(0) @binding(0)
var<uniform> config: Config;

@group(0) @binding(1)
var<storage, read_write> bump: BumpAllocators;

@group(0) @binding(2)
var<storage, read_write> indirect: IndirectCount;

@group(0) @binding(3)
var<storage, read_write> ptcl: array<u32>;

// Partition size for path tiling stage
const WG_SIZE = 256u;

// Single-thread stage: writes ceil(seg_counts / WG_SIZE) workgroups on x,
// or 0 plus the ptcl abort marker on failure; y and z are always 1.
@compute @workgroup_size(1)
fn main() {
    let segments = atomicLoad(&bump.seg_counts);
    // Compared against segments_size (not seg_counts_size) deliberately: every counted
    // crossing becomes one Segment written by the path_tiling stage this dispatch launches,
    // so it is the segments buffer that must hold the full count. path_count clamps its own
    // seg_counts writes against seg_counts_size at write time.
    let overflowed = segments > config.segments_size;
    if atomicLoad(&bump.failed) != 0u || overflowed {
        if overflowed {
            atomicOr(&bump.failed, STAGE_COARSE);
        }
        indirect.count_x = 0u;
        // Signal the fine rasterizer that a failure happened; fine does
        // not bind the bump buffer, so it cannot read the failed mask.
        ptcl[0] = ~0u;
    } else {
        indirect.count_x = (segments + (WG_SIZE - 1u)) / WG_SIZE;
    }
    indirect.count_y = 1u;
    indirect.count_z = 1u;
}
