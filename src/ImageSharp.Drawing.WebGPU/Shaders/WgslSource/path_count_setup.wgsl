// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

// Computes the indirect dispatch size for the per-line stages. Divides the
// flattened line count (bump.lines) by the workgroup size, or dispatches
// zero workgroups when an earlier stage recorded an allocation failure.
//
// Inputs: bump (lines counter, failed mask).
// Outputs: indirect (workgroup counts). The same indirect buffer drives
// both the path_row_span and path_count dispatches, which each run one
// thread per line with the same WG_SIZE.
//
// Ported from Vello's path_count_setup.wgsl; locally the result is reused
// for the sparse row span stage.

#import bump

@group(0) @binding(0)
var<storage, read_write> bump: BumpAllocators;

@group(0) @binding(1)
var<storage, read_write> indirect: IndirectCount;

// Partition size for path count stage
const WG_SIZE = 256u;

// Single-thread stage: writes ceil(lines / WG_SIZE) workgroups on x, or 0
// to cancel the per-line stages after an upstream failure; y and z are
// always 1.
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
