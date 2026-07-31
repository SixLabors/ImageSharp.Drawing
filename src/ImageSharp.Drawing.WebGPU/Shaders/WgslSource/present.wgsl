@group(0) @binding(0) var source_texture: texture_2d<f32>;

@vertex
fn vs_main(@builtin(vertex_index) vertex_index: u32) -> @builtin(position) vec4<f32> {
    // One oversized triangle covers the viewport without a vertex buffer or a shared-edge seam.
    let positions = array<vec2<f32>, 3>(
        vec2<f32>(-1.0, -1.0),
        vec2<f32>(3.0, -1.0),
        vec2<f32>(-1.0, 3.0));

    return vec4<f32>(positions[vertex_index], 0.0, 1.0);
}

@fragment
fn fs_main(@builtin(position) position: vec4<f32>) -> @location(0) vec4<f32> {
    // Fragment positions address the same top-left-origin texels in the equally sized source.
    return textureLoad(source_texture, vec2<i32>(position.xy), 0);
}
