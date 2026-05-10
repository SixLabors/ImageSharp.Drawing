// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

// Set up dispatch size for path tiling stage.

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

@compute @workgroup_size(1)
fn main() {
    let segments = atomicLoad(&bump.seg_counts);
    let overflowed = segments > config.segments_size;
    if atomicLoad(&bump.failed) != 0u || overflowed {
        if overflowed {
            atomicOr(&bump.failed, STAGE_COARSE);
        }
        indirect.count_x = 0u;
        ptcl[0] = ~0u;
    } else {
        indirect.count_x = (segments + (WG_SIZE - 1u)) / WG_SIZE;
    }
    indirect.count_y = 1u;
    indirect.count_z = 1u;
}
