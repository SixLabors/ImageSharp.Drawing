// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

// Set up dispatch size for path count stage.

#import bump

@group(0) @binding(0)
var<storage, read_write> bump: BumpAllocators;

@group(0) @binding(1)
var<storage, read_write> indirect: IndirectCount;

// Partition size for path count stage
const WG_SIZE = 256u;

@compute @workgroup_size(1)
fn main() {
    if atomicLoad(&bump.failed) != 0u {
        indirect.count_x = 0u;
    } else {
        let lines = atomicLoad(&bump.lines);
        indirect.count_x = (lines + (WG_SIZE - 1u)) / WG_SIZE;
    }
    indirect.count_y = 1u;
    indirect.count_z = 1u;
}
