// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace SixLabors.ImageSharp.Drawing.Tests.Processing
{
    public class ShapeOptionsDefaultsExtensionsTests
    {
        [Fact]
        public void SetDefaultOptionsOnProcessingContext()
        {
            var option = new ShapeOptions();
            var config = new Configuration();
            var context = new FakeImageOperationsProvider.FakeImageOperations<Rgba32>(config, null, true);

            context.SetShapeOptions(option);

            // sets the prop on the processing context not on the configuration
            Assert.Equal(option, context.Properties[typeof(ShapeOptions)]);
            Assert.DoesNotContain(typeof(ShapeOptions), config.Properties.Keys);
        }

        [Fact]
        public void UpdateDefaultOptionsOnProcessingContext_AlwaysNewInstance()
        {
            var option = new ShapeOptions()
            {
                ClippingOperation = ClippingOperation.Intersection,
                IntersectionRule = IntersectionRule.NonZero
            };
            var config = new Configuration();
            var context = new FakeImageOperationsProvider.FakeImageOperations<Rgba32>(config, null, true);
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
            var option = new ShapeOptions();
            var config = new Configuration();

            config.SetShapeOptions(option);

            Assert.Equal(option, config.Properties[typeof(ShapeOptions)]);
        }

        [Fact]
        public void UpdateDefaultOptionsOnConfiguration_AlwaysNewInstance()
        {
            var option = new ShapeOptions()
            {
                ClippingOperation = ClippingOperation.Intersection,
                IntersectionRule = IntersectionRule.NonZero
            };
            var config = new Configuration();
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
            var config = new Configuration();

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
            var config = new Configuration();

            config.Properties[typeof(ShapeOptions)] = "wronge type";
            ShapeOptions options = config.GetShapeOptions();
            Assert.NotNull(options);
            Assert.IsType<ShapeOptions>(options);
        }

        [Fact]
        public void GetDefaultOptionsFromConfiguration_AlwaysReturnsInstance()
        {
            var config = new Configuration();

            Assert.DoesNotContain(typeof(ShapeOptions), config.Properties.Keys);
            ShapeOptions options = config.GetShapeOptions();
            Assert.NotNull(options);
        }

        [Fact]
        public void GetDefaultOptionsFromConfiguration_AlwaysReturnsSameValue()
        {
            var config = new Configuration();

            ShapeOptions options = config.GetShapeOptions();
            ShapeOptions options2 = config.GetShapeOptions();
            Assert.Equal(options, options2);
        }

        [Fact]
        public void GetDefaultOptionsFromProcessingContext_AlwaysReturnsInstance()
        {
            var config = new Configuration();
            var context = new FakeImageOperationsProvider.FakeImageOperations<Rgba32>(config, null, true);

            ShapeOptions ctxOptions = context.GetShapeOptions();
            Assert.NotNull(ctxOptions);
        }

        [Fact]
        public void GetDefaultOptionsFromProcessingContext_AlwaysReturnsInstanceEvenIfSetToNull()
        {
            var config = new Configuration();
            var context = new FakeImageOperationsProvider.FakeImageOperations<Rgba32>(config, null, true);

            context.SetShapeOptions((ShapeOptions)null);
            ShapeOptions ctxOptions = context.GetShapeOptions();
            Assert.NotNull(ctxOptions);
        }

        [Fact]
        public void GetDefaultOptionsFromProcessingContext_FallbackToConfigsInstance()
        {
            var option = new ShapeOptions();
            var config = new Configuration();
            config.SetShapeOptions(option);
            var context = new FakeImageOperationsProvider.FakeImageOperations<Rgba32>(config, null, true);

            ShapeOptions ctxOptions = context.GetShapeOptions();
            Assert.Equal(option, ctxOptions);
        }

        [Fact]
        public void GetDefaultOptionsFromProcessingContext_IgnoreIncorectlyTypesDictionEntry()
        {
            var config = new Configuration();
            var context = new FakeImageOperationsProvider.FakeImageOperations<Rgba32>(config, null, true);
            context.Properties[typeof(ShapeOptions)] = "wronge type";
            ShapeOptions options = context.GetShapeOptions();
            Assert.NotNull(options);
            Assert.IsType<ShapeOptions>(options);
        }
    }
}
