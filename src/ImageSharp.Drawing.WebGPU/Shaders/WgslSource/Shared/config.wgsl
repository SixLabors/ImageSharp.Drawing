// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

// The per-scene GPU configuration uniform plus tile-geometry constants.
//
// Imported by every compute stage; bound at group slot zero. Derived from
// Vello's shader/shared/config.wgsl (linebender/vello), where the struct was
// synced with ConfigUniform in vello_encoding/src/config.rs.
//
// Written by the C# side each render attempt. Field order and sizes must be
// kept in sync with GpuSceneConfig in WebGPUSceneResources.cs (values are
// computed by WebGPUSceneConfig.cs).
struct Config {
    width_in_tiles: u32,
    height_in_tiles: u32,

    target_width: u32,
    target_height: u32,

    // Chunked rendering window: the first global tile row rendered by this
    // attempt and the number of real tile rows it covers.
    chunk_tile_y_start: u32,
    chunk_tile_height: u32,

    // The initial color applied to the pixels in a tile during the fine stage.
    // The format is packed RGBA8 in MSB order.
    base_color: u32,

    n_drawobj: u32, // number of draw objects in the scene
    n_path: u32, // number of paths in the scene
    n_clip: u32, // number of clip begin/end records in the scene

    // To reduce the number of bindings, info, auxiliary brush data, and bin data are combined
    // into one buffer.
    bin_data_start: u32, // start of bump-allocated bin data within that buffer
    brush_data_base: u32, // start of auxiliary brush data within that buffer
    ptcl_dyn_start: u32, // start of the bump-allocated region of the ptcl buffer

    // offsets within scene buffer (in u32 units)
    pathtag_base: u32,
    pathdata_base: u32,

    drawtag_base: u32,
    drawdata_base: u32,

    transform_base: u32,
    style_base: u32,

    // Sizes of bump allocated buffers (in element size units)
    lines_size: u32,
    binning_size: u32,
    path_rows_size: u32,
    tiles_size: u32,
    seg_counts_size: u32,
    segments_size: u32,
    blend_size: u32,
    ptcl_size: u32,

    // Word offset of the segment-profile tag table, or zero when the table is absent. Each encoded
    // point pair has one slot, which lets a fill segment find its X and Y profile identifiers.
    profile_slots_base: u32,
    // Word offset of the profile record table, or zero when the table is absent. The table starts
    // with the X and Y record counts. Each record then stores two axis limits and one contour link.
    profile_records_base: u32,
    profile_pad0: u32,
    profile_pad1: u32,
    profile_pad2: u32,
}

// Geometry of tiles and bins

const TILE_WIDTH = 16u;
const TILE_HEIGHT = 16u;
// Number of tiles per bin
const N_TILE_X = 16u;
const N_TILE_Y = 16u;
const N_TILE = N_TILE_X * N_TILE_Y;

// Reciprocal of TILE_WIDTH/TILE_HEIGHT, used to map pixel coordinates to
// tile coordinates. Not currently supporting non-square tiles.
const TILE_SCALE = 0.0625;

// The "split" point between using local memory in fine for the blend stack and spilling to the blend_spill buffer.
// A higher value will increase vgpr ("register") pressure in fine, but decrease required dynamic memory allocation.
// Used by coarse.wgsl to reserve blend_spill scratch for tiles whose clip depth
// exceeds the split, and by fine.wgsl to pick the stack slot at run time.
const BLEND_STACK_SPLIT = 4u;

// The following are computed in draw_leaf from the generic gradient parameters
// encoded in the scene, and stored in the gradient's info struct, for
// consumption during fine rasterization.

// Radial gradient kinds
const RAD_GRAD_KIND_CIRCULAR = 1u;
const RAD_GRAD_KIND_STRIP = 2u;
const RAD_GRAD_KIND_FOCAL_ON_CIRCLE = 3u;
const RAD_GRAD_KIND_CONE = 4u;

// Radial gradient flags
const RAD_GRAD_SWAPPED = 1u;

// Elliptic gradient kinds. Degenerate kinds preserve the CPU brush's IEEE-754
// division behavior without placing infinities or NaNs in the transform matrix.
const ELLIPTIC_GRAD_KIND_NORMAL = 0u;
const ELLIPTIC_GRAD_KIND_POINT = 1u;
const ELLIPTIC_GRAD_KIND_LINE = 2u;
