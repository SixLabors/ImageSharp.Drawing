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
    var should_cancel = false;
    let previous_failure = atomicLoad(&bump.failed);
    if (previous_failure & PREVIOUS_RUN) != 0u {
        atomicStore(&bump.failed, 0u);
    } else if previous_failure != 0u {
        if config.binning_size < atomicLoad(&bump.binning) {
            should_cancel = true;
        }
        if config.ptcl_size < atomicLoad(&bump.ptcl) {
            should_cancel = true;
        }
        if config.tiles_size < atomicLoad(&bump.tile) {
            should_cancel = true;
        }
        if config.seg_counts_size < atomicLoad(&bump.seg_counts) {
            should_cancel = true;
        }
        if config.segments_size < atomicLoad(&bump.segments) {
            should_cancel = true;
        }
        if config.lines_size < atomicLoad(&bump.lines) {
            should_cancel = true;
        }
        if config.blend_size < atomicLoad(&bump.blend_spill) {
            should_cancel = true;
        }
        if should_cancel {
            config.cancelled = 1u;
            atomicStore(&bump.failed, PREVIOUS_RUN);
        } else {
            atomicStore(&bump.failed, 0u);
        }
    }

    atomicStore(&bump.binning, 0u);
    atomicStore(&bump.ptcl, 0u);
    atomicStore(&bump.tile, 0u);
    atomicStore(&bump.seg_counts, 0u);
    atomicStore(&bump.segments, 0u);
    atomicStore(&bump.blend_spill, 0u);
    atomicStore(&bump.lines, 0u);
}
