// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// Provides a shader-accelerated counterpart to <see cref="ColorMatrixLayerEffect"/>.
/// </summary>
/// <remarks>
/// This effect can be used with both WebGPU and CPU drawing backends. WebGPU transforms the layer with a shader;
/// a CPU backend applies the equivalent <see cref="ColorMatrixLayerEffect"/>.
/// </remarks>
public sealed class WebGPUColorMatrixLayerEffect : WebGPUShaderLayerEffect
{
    internal const string ShaderSource = """
        fn layer_effect(position: vec2<f32>) -> vec4<f32> {

            // ImageSharp colour matrices operate on logical unassociated RGBA components.
            let source = layer_load_unassociated(vec2<i32>(position));

            // The translation row is supplied separately because WGSL exposes only a 4x4 matrix.
            // Clamping matches ImageSharp's filter conversion before the result is stored.
            let transformed = clamp(
                source * imagesharp_uniforms.matrix + imagesharp_uniforms.offset,
                vec4<f32>(0.0),
                vec4<f32>(1.0));

            // Effect working textures use associated alpha, so reassociate exactly once here.
            return vec4<f32>(transformed.rgb * transformed.a, transformed.a);
        }
        """;

    internal static readonly WebGPUShaderUniformLayout UniformLayout = new(
    [
        new WebGPUShaderUniform("matrix", WebGPUShaderUniformType.Matrix4x4, 1),
        new WebGPUShaderUniform("offset", WebGPUShaderUniformType.Vector4, 1)
    ]);

    /// <summary>
    /// Initializes a new instance of the <see cref="WebGPUColorMatrixLayerEffect"/> class.
    /// </summary>
    /// <param name="matrix">The colour matrix applied to the layer content.</param>
    public WebGPUColorMatrixLayerEffect(ColorMatrix matrix)
        : base(new ColorMatrixLayerEffect(matrix), ShaderSource, UniformLayout)
    {
        this.Matrix = matrix;

        // ImageSharp stores its affine colour transform as a 5x4 matrix. The upper 4x4 block maps
        // directly to WGSL's row-vector multiplication; M51-M54 form the separate translation row.
        Matrix4x4 transform = new(matrix.M11, matrix.M12, matrix.M13, matrix.M14, matrix.M21, matrix.M22, matrix.M23, matrix.M24, matrix.M31, matrix.M32, matrix.M33, matrix.M34, matrix.M41, matrix.M42, matrix.M43, matrix.M44);

        // A pass retains an immutable uniform snapshot, so later instances cannot alter this matrix.
        this.AddShaderPass(uniforms =>
        {
            uniforms.SetMatrix4x4("matrix", transform);
            uniforms.SetVector4("offset", new Vector4(matrix.M51, matrix.M52, matrix.M53, matrix.M54));
        });
    }

    /// <summary>
    /// Gets the colour matrix applied to the layer content.
    /// </summary>
    public ColorMatrix Matrix { get; }
}
