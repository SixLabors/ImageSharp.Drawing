// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Processing.Backends;

namespace SixLabors.ImageSharp.Drawing.Tests.Processing.Backends;

public class WebGPUShaderSourceValidatorTests
{
    [Fact]
    public void FullWgslControlFlowAndUniformReferencesAreAccepted()
    {
        const string source = """
            struct SamplePair {
                offset: vec2<f32>,
                weight: f32,
            }

            const pairs = array<SamplePair, 2>(
                SamplePair(vec2<f32>(-1.0, 0.0), 0.25),
                SamplePair(vec2<f32>(1.0, 0.0), 0.25),
            );

            fn weighted_sample(position: vec2<f32>) -> vec4<f32> {
                var color = layer_sample(position) * imagesharp_uniforms.center_weight;

                for (var i = 0u; i < 2u; i++) {
                    color += layer_sample(position + pairs[i].offset) * pairs[i].weight;
                }

                var remaining = 1u;
                while (remaining > 0u) {
                    remaining--;
                }

                return color;
            }

            fn layer_effect(position: vec2<f32>) -> vec4<f32> {
                return weighted_sample(position);
            }
            """;

        WebGPUShaderSourceValidator.Validate(source, "source");
    }

    [Fact]
    public void NestedCommentsDoNotDeclareFrameworkConstructs()
    {
        const string source = """
            /*
                @group(0) @binding(0)
                @fragment fn fs_main() {}
                fn layer_effect(position: vec2<f32>) -> vec4<f32> {}
                /* let imagesharp_private = 1.0; */
            */
            // @compute fn imagesharp_compute() {}
            fn layer_effect(position: vec2<f32>) -> vec4<f32> {
                return layer_sample(position);
            }
            """;

        WebGPUShaderSourceValidator.Validate(source, "source");
    }

    [Theory]
    [InlineData("@group(0) var<uniform> user_data: vec4<f32>;")]
    [InlineData("@binding(0) var user_texture: texture_2d<f32>;")]
    [InlineData("@vertex fn user_vertex() -> @builtin(position) vec4<f32> { return vec4<f32>(); }")]
    [InlineData("@fragment fn user_fragment() -> @location(0) vec4<f32> { return vec4<f32>(); }")]
    [InlineData("@compute @workgroup_size(1) fn user_compute() {}")]
    public void FrameworkOwnedAttributesAreRejected(string declaration)
    {
        string source = $$"""
            {{declaration}}

            fn layer_effect(position: vec2<f32>) -> vec4<f32> {
                return layer_sample(position);
            }
            """;

        ArgumentException exception = Assert.Throws<ArgumentException>(
            () => WebGPUShaderSourceValidator.Validate(source, "source"));

        Assert.Equal("source", exception.ParamName);
    }

    [Theory]
    [InlineData("fn imagesharp_helper(position: vec2<f32>) -> vec4<f32> { return layer_sample(position); }")]
    [InlineData("alias imagesharp_scalar = f32;")]
    [InlineData("struct imagesharp_state { value: f32, }")]
    [InlineData("const imagesharp_value = 1.0;")]
    [InlineData("var<private> imagesharp_state: f32;")]
    [InlineData("var<private> imagesharp_uniforms: f32;")]
    [InlineData("fn helper(imagesharp_value: f32) -> f32 { return imagesharp_value; }")]
    [InlineData("struct State { imagesharp_value: f32, }")]
    public void FrameworkPrefixedDeclarationsAreRejected(string declaration)
    {
        string source = $$"""
            {{declaration}}

            fn layer_effect(position: vec2<f32>) -> vec4<f32> {
                return layer_sample(position);
            }
            """;

        ArgumentException exception = Assert.Throws<ArgumentException>(
            () => WebGPUShaderSourceValidator.Validate(source, "source"));

        Assert.Equal("source", exception.ParamName);
    }

    [Fact]
    public void FrameworkPrefixedLocalDeclarationIsRejected()
    {
        const string source = """
            fn layer_effect(position: vec2<f32>) -> vec4<f32> {
                let imagesharp_value = 0.5;
                return layer_sample(position) * imagesharp_value;
            }
            """;

        Assert.Throws<ArgumentException>(
            () => WebGPUShaderSourceValidator.Validate(source, "source"));
    }

    [Theory]
    [InlineData("fn layer_load(position: vec2<i32>) -> vec4<f32> { return vec4<f32>(0.0); }")]
    [InlineData("fn layer_load_unassociated(position: vec2<i32>) -> vec4<f32> { return vec4<f32>(0.0); }")]
    [InlineData("fn layer_sample(position: vec2<f32>) -> vec4<f32> { return vec4<f32>(0.0); }")]
    [InlineData("fn vs_main() {}")]
    [InlineData("fn fs_main() {}")]
    [InlineData("struct ImageSharpFramework { value: f32, }")]
    [InlineData("struct ImageSharpUniforms { value: f32, }")]
    public void FrameworkOwnedDeclarationsAreRejected(string declaration)
    {
        string source = $$"""
            {{declaration}}

            fn layer_effect(position: vec2<f32>) -> vec4<f32> {
                return layer_sample(position);
            }
            """;

        Assert.Throws<ArgumentException>(
            () => WebGPUShaderSourceValidator.Validate(source, "source"));
    }

    [Theory]
    [InlineData("let values = imagesharp_uniforms;")]
    [InlineData("let values = &imagesharp_uniforms;")]
    [InlineData("consume(imagesharp_uniforms);")]
    [InlineData("let value = imagesharp_uniforms[0];")]
    public void UniformBindingMustBeReadThroughADirectField(string statement)
    {
        string source = $$"""
            fn layer_effect(position: vec2<f32>) -> vec4<f32> {
                {{statement}}
                return layer_sample(position);
            }
            """;

        Assert.Throws<ArgumentException>(
            () => WebGPUShaderSourceValidator.Validate(source, "source"));
    }

    [Fact]
    public void UniformFieldMayFollowWhitespaceAndNestedComments()
    {
        const string source = """
            fn layer_effect(position: vec2<f32>) -> vec4<f32> {
                return layer_sample(position) * imagesharp_uniforms /* outer /* nested */ */ .opacity;
            }
            """;

        WebGPUShaderSourceValidator.Validate(source, "source");
    }

    [Fact]
    public void FrameworkPrivateIdentifierReferenceIsRejected()
    {
        const string source = "fn layer_effect(position: vec2<f32>) -> vec4<f32> { return imagesharp_layer_load_scaled(vec2<i32>(position)); }";

        Assert.Throws<ArgumentException>(
            () => WebGPUShaderSourceValidator.Validate(source, "source"));
    }

    [Theory]
    [InlineData("vs_main")]
    [InlineData("fs_main")]
    [InlineData("ImageSharpFramework")]
    [InlineData("ImageSharpUniforms")]
    public void FrameworkPrivateUnprefixedIdentifierReferenceIsRejected(string identifier)
    {
        string source = $$"""
            fn layer_effect(position: vec2<f32>) -> vec4<f32> {
                let forbidden = {{identifier}};
                return layer_sample(position);
            }
            """;

        Assert.Throws<ArgumentException>(
            () => WebGPUShaderSourceValidator.Validate(source, "source"));
    }

    [Fact]
    public void NullCharacterIsRejected()
    {
        const string source = "fn layer_effect(position: vec2<f32>) -> vec4<f32> { return layer_sample(position); }\0";

        ArgumentException exception = Assert.Throws<ArgumentException>(
            () => WebGPUShaderSourceValidator.Validate(source, "source"));

        Assert.Equal("source", exception.ParamName);
    }

    [Fact]
    public void MissingLayerEffectDeclarationIsRejected()
    {
        const string source = "fn helper(position: vec2<f32>) -> vec4<f32> { return layer_sample(position); }";

        Assert.Throws<ArgumentException>(
            () => WebGPUShaderSourceValidator.Validate(source, "source"));
    }

    [Fact]
    public void DuplicateLayerEffectDeclarationIsRejected()
    {
        const string source = """
            fn layer_effect(position: vec2<f32>) -> vec4<f32> {
                return layer_sample(position);
            }

            fn layer_effect(position: vec2<f32>) -> vec4<f32> {
                return layer_load(vec2<i32>(position));
            }
            """;

        Assert.Throws<ArgumentException>(
            () => WebGPUShaderSourceValidator.Validate(source, "source"));
    }
}
