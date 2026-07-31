// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

// Clip-stack reduction stage. First pass of the stack-monoid scan over the
// scene's clip records: each workgroup reduces its span of 256 ClipInp
// entries to a single Bic (bicyclic semigroup) aggregate and records the
// stack elements (BeginClips left open at the end of the span) so clip_leaf
// can resolve pushes and pops across workgroup boundaries.
//
// Inputs: clip_inp (ClipInp records emitted by draw_leaf); path_bboxes
// (per-path bounds from the path bbox stages).
// Outputs: reduced[wg] = Bic aggregate of workgroup wg's span; clip_out =
// ClipEl (parent draw object index plus conservative bbox) for each open
// BeginClip, stored in stack order at the start of the workgroup's range.
//
// Ported from Vello's clip_reduce.wgsl (linebender/vello,
// vello_shaders/shader). Local divergence: each ClipInp carries a clip
// operation, and Difference clips publish an infinite bbox because they
// retain everything outside their path.

#import bbox
#import clip

@group(0) @binding(0)
var<storage> clip_inp: array<ClipInp>;

@group(0) @binding(1)
var<storage> path_bboxes: array<PathBbox>;

@group(0) @binding(2)
var<storage, read_write> reduced: array<Bic>;

@group(0) @binding(3)
var<storage, read_write> clip_out: array<ClipEl>;

const WG_SIZE = 256u;
var<workgroup> sh_bic: array<Bic, WG_SIZE>;
var<workgroup> sh_parent: array<u32, WG_SIZE>;
var<workgroup> sh_path_ix: array<u32, WG_SIZE>;
var<workgroup> sh_operation: array<u32, WG_SIZE>;

// Reduces one workgroup's span of clip records. A reverse inclusive scan
// computes the Bic aggregate (written to reduced[wg_id.x]); the suffix
// values from the same scan identify which BeginClips remain open at the
// end of the span, and those are written as ClipEl stack elements into the
// first sh_bic[0].b slots of this workgroup's clip_out range.
@compute @workgroup_size(256)
fn main(
    @builtin(global_invocation_id) global_id: vec3<u32>,
    @builtin(local_invocation_id) local_id: vec3<u32>,
    @builtin(workgroup_id) wg_id: vec3<u32>,
) {
    let clip_input = clip_inp[global_id.x];
    let inp = clip_input.path_ix;
    let operation = clip_input.operation;
    let is_push = inp >= 0;
    var bic = Bic(1u - u32(is_push), u32(is_push));

    // Reverse inclusive scan of the bicyclic semigroup: after the loop,
    // sh_bic[i] combines elements i..WG_SIZE-1, so sh_bic[0] is the span total.
    sh_bic[local_id.x] = bic;
    for (var i = 0u; i < firstTrailingBit(WG_SIZE); i += 1u) {
        workgroupBarrier();
        if local_id.x + (1u << i) < WG_SIZE {
            let other = sh_bic[local_id.x + (1u << i)];
            bic = bic_combine(bic, other);
        }
        workgroupBarrier();
        sh_bic[local_id.x] = bic;
    }
    if local_id.x == 0u {
        reduced[wg_id.x] = bic;
    }
    workgroupBarrier();
    let size = sh_bic[0].b;
    bic = Bic();
    if local_id.x + 1u < WG_SIZE {
        bic = sh_bic[local_id.x + 1u];
    }
    // A push survives the span when its suffix contains no unmatched pop
    // (bic.a == 0). Its slot orders the surviving pushes bottom-up: bic.b
    // counts the surviving pushes that follow it.
    if is_push && bic.a == 0u {
        let local_ix = size - bic.b - 1u;
        sh_parent[local_ix] = local_id.x;
        sh_path_ix[local_ix] = u32(inp);
        sh_operation[local_ix] = operation;
    }
    workgroupBarrier();
    // TODO: possibly do forward scan here if depth can exceed wg size
    if local_id.x < size {
        let path_ix = sh_path_ix[local_id.x];
        let path_bbox = path_bboxes[path_ix];
        let parent_ix = sh_parent[local_id.x] + wg_id.x * WG_SIZE;
        // Difference is ImageSharp's extension over Vello's clip record. The clip
        // still has a path for fine-stage coverage, but its retained area is outside
        // that path, so the path box cannot narrow descendant conservative bounds.
        let path_box = vec4(f32(path_bbox.x0), f32(path_bbox.y0), f32(path_bbox.x1), f32(path_bbox.y1));
        let bbox = select(path_box, vec4(-1e9, -1e9, 1e9, 1e9), sh_operation[local_id.x] == CLIP_OPERATION_DIFFERENCE);
        clip_out[global_id.x] = ClipEl(parent_ix, bbox);
    }
}
