// Copyright 2024 the Vello Authors
// SPDX-License-Identifier: Apache-2.0 OR MIT OR Unlicense

#import config
#import bump

@group(0) @binding(0)
var<storage, read_write> config: Config;

@group(0) @binding(1)
var<storage, read_write> bump: BumpAllocators;

@compute @workgroup_size(1)
fn main() {
    // Never cancel. Let all stages run so the bump allocators report the true
    // demand for every buffer in a single pass. The CPU reads back the actuals
    // and retries once with the correct sizes.
    atomicStore(&bump.failed, 0u);
    atomicStore(&bump.binning, 0u);
    atomicStore(&bump.ptcl, 0u);
    atomicStore(&bump.tile, 0u);
    atomicStore(&bump.seg_counts, 0u);
    atomicStore(&bump.segments, 0u);
    atomicStore(&bump.blend_spill, 0u);
    atomicStore(&bump.lines, 0u);
}
