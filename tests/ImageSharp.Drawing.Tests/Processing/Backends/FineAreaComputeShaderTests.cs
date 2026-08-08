// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Text;
using SixLabors.ImageSharp.Drawing.Processing.Backends;
using SixLabors.ImageSharp.PixelFormats;
using TextureFormat = SixLabors.ImageSharp.Drawing.Processing.Backends.Native.WGPUTextureFormat;

namespace SixLabors.ImageSharp.Drawing.Tests.Processing.Backends;

public class FineAreaComputeShaderTests
{
    [Fact]
    public void GetCode_CachesByTargetDescriptor()
    {
        byte[] associated = FineAreaComputeShader.GetCode(TextureFormat.RGBA8Unorm, PixelAlphaRepresentation.Associated, WebGPUTargetNumericEncoding.Unit);
        byte[] associatedAgain = FineAreaComputeShader.GetCode(TextureFormat.RGBA8Unorm, PixelAlphaRepresentation.Associated, WebGPUTargetNumericEncoding.Unit);
        byte[] unassociated = FineAreaComputeShader.GetCode(TextureFormat.RGBA8Unorm, PixelAlphaRepresentation.Unassociated, WebGPUTargetNumericEncoding.Unit);

        Assert.Same(associated, associatedAgain);
        Assert.NotSame(associated, unassociated);
    }

    [Fact]
    public void GetCode_AssociatedTargetPreservesAssociatedBackdropAndOutput()
    {
        string source = GetSource(TextureFormat.RGBA8Unorm, PixelAlphaRepresentation.Associated, WebGPUTargetNumericEncoding.Unit);

        Assert.Contains("fn decode_target(color: vec4<f32>) -> vec4<f32> {\n    return decode_numeric(color);\n}", source);
        Assert.Contains("fn encode_target(color: vec4<f32>) -> vec4<f32> {\n    return encode_numeric(color);\n}", source);
        Assert.Contains("rgba[i] = decode_target(backdrop_raw);", source);
        Assert.Contains("textureStore(output, vec2<i32>(coords), round_trip_target_format(encode_target(rgba[i])));", source);
    }

    [Fact]
    public void GetCode_UnassociatedTargetAssociatesBackdropAndUnassociatesOutput()
    {
        string source = GetSource(TextureFormat.RGBA8Unorm, PixelAlphaRepresentation.Unassociated, WebGPUTargetNumericEncoding.Unit);

        Assert.Contains("return premul_alpha(decode_numeric(color));", source);
        Assert.Contains("if color.a == 0.0 {\n        return encode_numeric(color);\n    }", source);
        Assert.Contains("return encode_numeric(vec4<f32>(color.rgb / color.a, color.a));", source);
    }

    [Fact]
    public void GetCode_SnormTargetDecodesAndEncodesNumericRange()
    {
        string source = GetSource(TextureFormat.RGBA8Snorm, PixelAlphaRepresentation.Associated, WebGPUTargetNumericEncoding.SignedUnit);

        Assert.Contains("return (color + vec4<f32>(1.0)) * 0.5;", source);
        Assert.Contains("return (color * 2.0) - vec4<f32>(1.0);", source);
    }

    [Fact]
    public void GetCode_HalfTargetUsesUnitValues()
    {
        string source = GetSource(TextureFormat.RGBA16Float, PixelAlphaRepresentation.Associated, WebGPUTargetNumericEncoding.Unit);

        Assert.Contains("fn decode_numeric(color: vec4<f32>) -> vec4<f32> {\n    return color;\n}", source);
        Assert.Contains("fn encode_numeric(color: vec4<f32>) -> vec4<f32> {\n    return color;\n}", source);

        // Binary16 stores quantize in-shader with round-to-nearest-even so the hardware
        // conversion is lossless and matches the CPU renderer's float-to-half rounding.
        Assert.Contains("textureStore(output, vec2<i32>(coords), round_trip_target_format(encode_target(rgba[i])));", source);
        Assert.Contains("return vec4<f32>(quantize_f16_rtne(color.x), quantize_f16_rtne(color.y), quantize_f16_rtne(color.z), quantize_f16_rtne(color.w));", source);
    }

    private static string GetSource(
        TextureFormat textureFormat,
        PixelAlphaRepresentation alphaRepresentation,
        WebGPUTargetNumericEncoding numericEncoding)
    {
        byte[] code = FineAreaComputeShader.GetCode(textureFormat, alphaRepresentation, numericEncoding);
        return Encoding.UTF8.GetString(code.AsSpan(0, code.Length - 1));
    }
}
