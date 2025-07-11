// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;

namespace SixLabors.ImageSharp.Drawing.Tests.Processing;

public class ShapeOptionsDefaultsExtensionsTests
{
    [Fact]
    public void SetDefaultOptionsOnProcessingContext()
    {
        ShapeOptions option = new();
        Configuration config = new();
        FakeImageOperationsProvider.FakeImageOperations<Rgba32> context = new(config, null, true);

        context.SetShapeOptions(option);

        // sets the prop on the processing context not on the configuration
        Assert.Equal(option, context.Properties[typeof(ShapeOptions)]);
        Assert.DoesNotContain(typeof(ShapeOptions), config.Properties.Keys);
    }

    [Fact]
    public void UpdateDefaultOptionsOnProcessingContext_AlwaysNewInstance()
    {
        ShapeOptions option = new()
        {
            ClippingOperation = ClippingOperation.Intersection,
            IntersectionRule = IntersectionRule.NonZero
        };
        Configuration config = new();
        FakeImageOperationsProvider.FakeImageOperations<Rgba32> context = new(config, null, true);
        context.SetShapeOptions(option);

        context.SetShapeOptions(o =>
        {
            Assert.Equal(ClippingOperation.Intersection, o.ClippingOperation); // has original values
            Assert.Equal(IntersectionRule.NonZero, o.IntersectionRule);

            o.ClippingOperation = ClippingOperation.Xor;
            o.IntersectionRule = IntersectionRule.EvenOdd;
        });

        ShapeOptions returnedOption = context.GetShapeOptions();

        Assert.Equal(ClippingOperation.Xor, returnedOption.ClippingOperation);
        Assert.Equal(IntersectionRule.EvenOdd, returnedOption.IntersectionRule);
        Assert.Equal(ClippingOperation.Intersection, option.ClippingOperation); // hasn't been mutated
        Assert.Equal(IntersectionRule.NonZero, option.IntersectionRule);
    }

    [Fact]
    public void SetDefaultOptionsOnConfiguration()
    {
        ShapeOptions option = new();
        Configuration config = new();

        config.SetShapeOptions(option);

        Assert.Equal(option, config.Properties[typeof(ShapeOptions)]);
    }

    [Fact]
    public void UpdateDefaultOptionsOnConfiguration_AlwaysNewInstance()
    {
        ShapeOptions option = new()
        {
            ClippingOperation = ClippingOperation.Intersection,
            IntersectionRule = IntersectionRule.NonZero
        };
        Configuration config = new();
        config.SetShapeOptions(option);

        config.SetShapeOptions(o =>
        {
            Assert.Equal(ClippingOperation.Intersection, o.ClippingOperation); // has original values
            Assert.Equal(IntersectionRule.NonZero, o.IntersectionRule);
            o.ClippingOperation = ClippingOperation.Xor;
            o.IntersectionRule = IntersectionRule.EvenOdd;
        });

        ShapeOptions returnedOption = config.GetShapeOptions();
        Assert.Equal(ClippingOperation.Xor, returnedOption.ClippingOperation);
        Assert.Equal(IntersectionRule.EvenOdd, returnedOption.IntersectionRule);
        Assert.Equal(ClippingOperation.Intersection, option.ClippingOperation); // hasn't been mutated
        Assert.Equal(IntersectionRule.NonZero, option.IntersectionRule);
    }

    [Fact]
    public void GetDefaultOptionsFromConfiguration_SettingNullThenReturnsNewInstance()
    {
        Configuration config = new();

        ShapeOptions options = config.GetShapeOptions();
        Assert.NotNull(options);
        config.SetShapeOptions((ShapeOptions)null);

        ShapeOptions options2 = config.GetShapeOptions();
        Assert.NotNull(options2);

        // we set it to null should now be a new instance
        Assert.NotEqual(options, options2);
    }

    [Fact]
    public void GetDefaultOptionsFromConfiguration_IgnoreIncorectlyTypesDictionEntry()
    {
        Configuration config = new();

        config.Properties[typeof(ShapeOptions)] = "wronge type";
        ShapeOptions options = config.GetShapeOptions();
        Assert.NotNull(options);
        Assert.IsType<ShapeOptions>(options);
    }

    [Fact]
    public void GetDefaultOptionsFromConfiguration_AlwaysReturnsInstance()
    {
        Configuration config = new();

        Assert.DoesNotContain(typeof(ShapeOptions), config.Properties.Keys);
        ShapeOptions options = config.GetShapeOptions();
        Assert.NotNull(options);
    }

    [Fact]
    public void GetDefaultOptionsFromConfiguration_AlwaysReturnsSameValue()
    {
        Configuration config = new();

        ShapeOptions options = config.GetShapeOptions();
        ShapeOptions options2 = config.GetShapeOptions();
        Assert.Equal(options, options2);
    }

    [Fact]
    public void GetDefaultOptionsFromProcessingContext_AlwaysReturnsInstance()
    {
        Configuration config = new();
        FakeImageOperationsProvider.FakeImageOperations<Rgba32> context = new(config, null, true);

        ShapeOptions ctxOptions = context.GetShapeOptions();
        Assert.NotNull(ctxOptions);
    }

    [Fact]
    public void GetDefaultOptionsFromProcessingContext_AlwaysReturnsInstanceEvenIfSetToNull()
    {
        Configuration config = new();
        FakeImageOperationsProvider.FakeImageOperations<Rgba32> context = new(config, null, true);

        context.SetShapeOptions((ShapeOptions)null);
        ShapeOptions ctxOptions = context.GetShapeOptions();
        Assert.NotNull(ctxOptions);
    }

    [Fact]
    public void GetDefaultOptionsFromProcessingContext_FallbackToConfigsInstance()
    {
        ShapeOptions option = new();
        Configuration config = new();
        config.SetShapeOptions(option);
        FakeImageOperationsProvider.FakeImageOperations<Rgba32> context = new(config, null, true);

        ShapeOptions ctxOptions = context.GetShapeOptions();
        Assert.Equal(option, ctxOptions);
    }

    [Fact]
    public void GetDefaultOptionsFromProcessingContext_IgnoreIncorectlyTypesDictionEntry()
    {
        Configuration config = new();
        FakeImageOperationsProvider.FakeImageOperations<Rgba32> context = new(config, null, true);
        context.Properties[typeof(ShapeOptions)] = "wronge type";
        ShapeOptions options = context.GetShapeOptions();
        Assert.NotNull(options);
        Assert.IsType<ShapeOptions>(options);
    }
}
