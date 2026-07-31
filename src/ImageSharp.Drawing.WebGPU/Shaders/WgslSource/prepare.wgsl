// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

// First stage of a scheduling run: clears every bump allocator counter and
// the failure mask on the GPU so the pipeline starts from a clean slate
// without a CPU buffer write.
//
// Inputs/outputs: bump (all counters and the failed mask zeroed).
//
// Local addition; no Vello shader of this name exists (Vello clears the
// bump buffer with a recorded buffer clear instead).

#import bump

@group(0) @binding(0)
var<storage, read_write> bump: BumpAllocators;

// Single-thread stage that zeroes all bump state.
@compute @workgroup_size(1)
fn main() {
    // Never cancel. Let all stages run so the bump allocators report the true
    // demand for every buffer in a single pass. The CPU reads back the actuals
    // and retries once with the correct sizes.
    atomicStore(&bump.failed, 0u);
    atomicStore(&bump.binning, 0u);
    atomicStore(&bump.ptcl, 0u);
    atomicStore(&bump.path_rows, 0u);
    atomicStore(&bump.tile, 0u);
    atomicStore(&bump.seg_counts, 0u);
    atomicStore(&bump.segments, 0u);
    atomicStore(&bump.blend_spill, 0u);
    atomicStore(&bump.lines, 0u);
}
