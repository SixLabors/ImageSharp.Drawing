// Copyright (c) Six Labors and contributors.
// Licensed under the Apache License, Version 2.0.

using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Drawing.Tests.Processing;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace SixLabors.ImageSharp.Drawing.Tests.Processing
{
    public class ShapeGraphicsOptionsDefaultsExtensionsTests
    {
        [Fact]
        public void SetDefaultOptionsOnProcessingContext()
        {
            var option = new ShapeGraphicsOptions();
            var config = new Configuration();
            var context = new FakeImageOperationsProvider.FakeImageOperations<Rgba32>(config, null, true);

            context.SetShapeGraphicsOptions(option);

            // sets the prop on the processing context not on the configuration
            Assert.Equal(option, context.Properties[typeof(ShapeGraphicsOptions)]);
            Assert.DoesNotContain(typeof(ShapeGraphicsOptions), config.Properties.Keys);
        }

        [Fact]
        public void UpdateDefaultOptionsOnProcessingContext_AlwaysNewInstance()
        {
            var option = new ShapeGraphicsOptions()
            {
                BlendPercentage = 0.9f
            };
            var config = new Configuration();
            var context = new FakeImageOperationsProvider.FakeImageOperations<Rgba32>(config, null, true);
            context.SetShapeGraphicsOptions(option);

            context.SetShapeGraphicsOptions(o =>
            {
                Assert.Equal(0.9f, o.BlendPercentage); // has origional values
                o.BlendPercentage = 0.4f;
            });

            var returnedOption = context.GetShapeGraphicsOptions();
            Assert.Equal(0.4f, returnedOption.BlendPercentage);
            Assert.Equal(0.9f, option.BlendPercentage); // hasn't been mutated
        }

        [Fact]
        public void SetDefaultOptionsOnConfiguration()
        {
            var option = new ShapeGraphicsOptions();
            var config = new Configuration();

            config.SetShapeGraphicsOptions(option);

            Assert.Equal(option, config.Properties[typeof(ShapeGraphicsOptions)]);
        }

        [Fact]
        public void UpdateDefaultOptionsOnConfiguration_AlwaysNewInstance()
        {
            var option = new ShapeGraphicsOptions()
            {
                BlendPercentage = 0.9f
            };
            var config = new Configuration();
            config.SetShapeGraphicsOptions(option);

            config.SetShapeGraphicsOptions(o =>
            {
                Assert.Equal(0.9f, o.BlendPercentage); // has origional values
                o.BlendPercentage = 0.4f;
            });

            var returnedOption = config.GetShapeGraphicsOptions();
            Assert.Equal(0.4f, returnedOption.BlendPercentage);
            Assert.Equal(0.9f, option.BlendPercentage); // hasn't been mutated
        }

        [Fact]
        public void GetDefaultOptionsFromConfiguration_SettingNullThenReturnsNewInstance()
        {
            var config = new Configuration();

            var options = config.GetShapeGraphicsOptions();
            Assert.NotNull(options);
            config.SetShapeGraphicsOptions((ShapeGraphicsOptions)null);

            var options2 = config.GetShapeGraphicsOptions();
            Assert.NotNull(options2);

            // we set it to null should now be a new instance
            Assert.NotEqual(options, options2);
        }

        [Fact]
        public void GetDefaultOptionsFromConfiguration_IgnoreIncorectlyTypesDictionEntry()
        {
            var config = new Configuration();

            config.Properties[typeof(ShapeGraphicsOptions)] = "wronge type";
            var options = config.GetShapeGraphicsOptions();
            Assert.NotNull(options);
            Assert.IsType<ShapeGraphicsOptions>(options);
        }

        [Fact]
        public void GetDefaultOptionsFromConfiguration_AlwaysReturnsInstance()
        {
            var config = new Configuration();

            Assert.DoesNotContain(typeof(ShapeGraphicsOptions), config.Properties.Keys);
            var options = config.GetShapeGraphicsOptions();
            Assert.NotNull(options);
        }

        [Fact]
        public void GetDefaultOptionsFromConfiguration_AlwaysReturnsSameValue()
        {
            var config = new Configuration();

            var options = config.GetShapeGraphicsOptions();
            var options2 = config.GetShapeGraphicsOptions();
            Assert.Equal(options, options2);
        }

        [Fact]
        public void GetDefaultOptionsFromProcessingContext_AlwaysReturnsInstance()
        {
            var config = new Configuration();
            var context = new FakeImageOperationsProvider.FakeImageOperations<Rgba32>(config, null, true);

            var ctxOptions = context.GetShapeGraphicsOptions();
            Assert.NotNull(ctxOptions);
        }

        [Fact]
        public void GetDefaultOptionsFromProcessingContext_AlwaysReturnsInstanceEvenIfSetToNull()
        {
            var config = new Configuration();
            var context = new FakeImageOperationsProvider.FakeImageOperations<Rgba32>(config, null, true);

            context.SetShapeGraphicsOptions((ShapeGraphicsOptions)null);
            var ctxOptions = context.GetShapeGraphicsOptions();
            Assert.NotNull(ctxOptions);
        }

        [Fact]
        public void GetDefaultOptionsFromProcessingContext_FallbackToConfigsInstance()
        {
            var option = new ShapeGraphicsOptions();
            var config = new Configuration();
            config.SetShapeGraphicsOptions(option);
            var context = new FakeImageOperationsProvider.FakeImageOperations<Rgba32>(config, null, true);

            var ctxOptions = context.GetShapeGraphicsOptions();
            Assert.Equal(option, ctxOptions);
        }

        [Fact]
        public void GetDefaultOptionsFromProcessingContext_IgnoreIncorectlyTypesDictionEntry()
        {
            var config = new Configuration();
            var context = new FakeImageOperationsProvider.FakeImageOperations<Rgba32>(config, null, true);
            context.Properties[typeof(ShapeGraphicsOptions)] = "wronge type";
            var options = context.GetShapeGraphicsOptions();
            Assert.NotNull(options);
            Assert.IsType<ShapeGraphicsOptions>(options);
        }
    }
}
