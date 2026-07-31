// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

// Path tag stream decoding: the tag monoid and its scan operators.
//
// The scene encodes each path as a byte stream of tags (one per segment,
// plus transform/style/path markers). The pathtag_reduce/scan stages compute
// a prefix sum of TagMonoid over that stream so flatten.wgsl can locate each
// segment's point data, transform, and style in O(1). Imported by
// flatten.wgsl, pathtag_reduce.wgsl, pathtag_reduce2.wgsl, pathtag_scan.wgsl,
// and pathtag_scan1.wgsl.
//
// Ported from Vello's shader/shared/pathtag.wgsl (linebender/vello). Tag
// bytes are produced by the C# encoder (WebGPUSceneEncoder.cs), so the
// constants below document the shared wire format even where WGSL does not
// reference them all.

// Running counts accumulated over the tag stream. Each field is the number
// of stream elements of that kind preceding the current position.
struct TagMonoid {
    trans_ix: u32, // transform marker count (index into the transform stream)
    pathseg_offset: u32, // offset into the path data stream, in u32 words
    style_ix: u32, // style marker count premultiplied by STYLE_SIZE_IN_WORDS
    path_ix: u32, // path marker count (index of the current path)
}

// Path tag byte layout. The low two bits select the segment type; bit 2
// marks the last segment of a subpath, bit 3 selects f32 (vs i16) point
// encoding, and the high bits mark path/transform/style stream advances.
const PATH_TAG_SEG_TYPE = 3u;
const PATH_TAG_LINETO = 1u;
const PATH_TAG_QUADTO = 2u;
const PATH_TAG_CUBICTO = 3u;
const PATH_TAG_F32 = 8u;
const PATH_TAG_TRANSFORM = 0x20u;
const PATH_TAG_PATH = 0x10u;
const PATH_TAG_STYLE = 0x40u;
const PATH_TAG_SUBPATH_END = 4u;

// Size of the `Style` data structure in words
const STYLE_SIZE_IN_WORDS: u32 = 10u;

// Flag bits of the first word of a Style record, packed by the C# encoder.
// STYLE marks a stroke (vs fill) and FILL marks the even-odd fill rule
// (clear = non-zero); the cap and join fields drive the GPU stroker in
// flatten.wgsl.
const STYLE_FLAGS_STYLE: u32 = 0x80000000u;
const STYLE_FLAGS_FILL: u32 = 0x40000000u;

const STYLE_FLAGS_START_CAP_MASK: u32 = 0x0C000000u;
const STYLE_FLAGS_END_CAP_MASK: u32 = 0x03000000u;

const STYLE_FLAGS_CAP_BUTT: u32 = 0u;
const STYLE_FLAGS_CAP_SQUARE: u32 = 0x01000000u;
const STYLE_FLAGS_CAP_ROUND: u32 = 0x02000000u;

const STYLE_FLAGS_JOIN_MASK: u32 = 0x30000000u;
const STYLE_FLAGS_JOIN_BEVEL: u32 = 0u;
const STYLE_FLAGS_JOIN_MITER: u32 = 0x10000000u;
const STYLE_FLAGS_JOIN_ROUND: u32 = 0x20000000u;
const STYLE_FLAGS_JOIN_MITER_REVERT: u32 = 0x00400000u;
const STYLE_FLAGS_JOIN_MITER_ROUND: u32 = 0x00800000u;

// TODO: Declare the remaining STYLE flags here.

// The scan identity: all counters zero.
fn tag_monoid_identity() -> TagMonoid {
    return TagMonoid();
}

// The monoid combine operator: plain component-wise addition.
fn combine_tag_monoid(a: TagMonoid, b: TagMonoid) -> TagMonoid {
    var c: TagMonoid;
    c.trans_ix = a.trans_ix + b.trans_ix;
    c.pathseg_offset = a.pathseg_offset + b.pathseg_offset;
    c.style_ix = a.style_ix + b.style_ix;
    c.path_ix = a.path_ix + b.path_ix;
    return c;
}

// Maps four packed tag bytes to their combined TagMonoid contribution.
// Works on all four bytes at once using SWAR arithmetic: each 0x01010101
// multiply broadcasts a mask to every byte lane.
fn reduce_tag(tag_word: u32) -> TagMonoid {
    var c: TagMonoid;
    let point_count = tag_word & 0x3030303u;
    c.trans_ix = countOneBits(tag_word & (PATH_TAG_TRANSFORM * 0x1010101u));
    // Points consumed per byte: the 2-bit segment type (1..3), plus one when
    // the subpath-end bit is set since that segment's last point is not
    // shared with a successor. The f32 flag (bit 3) doubles the word count
    // (2 words per point instead of 1 for packed i16). The trailing shifts
    // horizontally add the four byte lanes into a single total.
    let n_points = point_count + ((tag_word >> 2u) & 0x1010101u);
    var a = n_points + (n_points & (((tag_word >> 3u) & 0x1010101u) * 15u));
    a += a >> 8u;
    a += a >> 16u;
    c.pathseg_offset = a & 0xffu;
    c.path_ix = countOneBits(tag_word & (PATH_TAG_PATH * 0x1010101u));
    c.style_ix = countOneBits(tag_word & (PATH_TAG_STYLE * 0x1010101u)) * STYLE_SIZE_IN_WORDS;
    return c;
}
