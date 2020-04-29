// Copyright (c) Six Labors and contributors.
// Licensed under the Apache License, Version 2.0.

using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Drawing.Tests.Processing;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace SixLabors.ImageSharp.Drawing.Tests.Drawing.Text
{
    public class TextGraphicOptionsDefaultsExtensionsTests
    {
        [Fact]
        public void SetDefaultOptionsOnProcessingContext()
        {
            var option = new TextGraphicsOptions();
            var config = new Configuration();
            var context = new FakeImageOperationsProvider.FakeImageOperations<Rgba32>(config, null, true);

            context.SetTextGraphicsOptions(option);

            // sets the prop on the processing context not on the configuration
            Assert.Equal(option, context.Properties[typeof(TextGraphicsOptions)]);
            Assert.DoesNotContain(typeof(TextGraphicsOptions), config.Properties.Keys);
        }

        [Fact]
        public void UpdateDefaultOptionsOnProcessingContext_AlwaysNewInstance()
        {
            var option = new TextGraphicsOptions()
            {
                BlendPercentage = 0.9f
            };
            var config = new Configuration();
            var context = new FakeImageOperationsProvider.FakeImageOperations<Rgba32>(config, null, true);
            context.SetTextGraphicsOptions(option);

            context.SetTextGraphicsOptions(o =>
            {
                Assert.Equal(0.9f, o.BlendPercentage); // has origional values
                o.BlendPercentage = 0.4f;
            });

            var returnedOption = context.GetTextGraphicsOptions();
            Assert.Equal(0.4f, returnedOption.BlendPercentage);
            Assert.Equal(0.9f, option.BlendPercentage); // hasn't been mutated
        }

        [Fact]
        public void SetDefaultOptionsOnConfiguration()
        {
            var option = new TextGraphicsOptions();
            var config = new Configuration();

            config.SetTextGraphicsOptions(option);

            Assert.Equal(option, config.Properties[typeof(TextGraphicsOptions)]);
        }

        [Fact]
        public void UpdateDefaultOptionsOnConfiguration_AlwaysNewInstance()
        {
            var option = new TextGraphicsOptions()
            {
                BlendPercentage = 0.9f
            };
            var config = new Configuration();
            config.SetTextGraphicsOptions(option);

            config.SetTextGraphicsOptions(o =>
            {
                Assert.Equal(0.9f, o.BlendPercentage); // has origional values
                o.BlendPercentage = 0.4f;
            });

            var returnedOption = config.GetTextGraphicsOptions();
            Assert.Equal(0.4f, returnedOption.BlendPercentage);
            Assert.Equal(0.9f, option.BlendPercentage); // hasn't been mutated
        }

        [Fact]
        public void GetDefaultOptionsFromConfiguration_SettingNullThenReturnsNewInstance()
        {
            var config = new Configuration();

            var options = config.GetTextGraphicsOptions();
            Assert.NotNull(options);
            config.SetTextGraphicsOptions((TextGraphicsOptions)null);

            var options2 = config.GetTextGraphicsOptions();
            Assert.NotNull(options2);

            // we set it to null should now be a new instance
            Assert.NotEqual(options, options2);
        }

        [Fact]
        public void GetDefaultOptionsFromConfiguration_IgnoreIncorectlyTypesDictionEntry()
        {
            var config = new Configuration();

            config.Properties[typeof(TextGraphicsOptions)] = "wronge type";
            var options = config.GetTextGraphicsOptions();
            Assert.NotNull(options);
            Assert.IsType<TextGraphicsOptions>(options);
        }

        [Fact]
        public void GetDefaultOptionsFromConfiguration_AlwaysReturnsInstance()
        {
            var config = new Configuration();

            Assert.DoesNotContain(typeof(TextGraphicsOptions), config.Properties.Keys);
            var options = config.GetTextGraphicsOptions();
            Assert.NotNull(options);
        }

        [Fact]
        public void GetDefaultOptionsFromConfiguration_AlwaysReturnsSameValue()
        {
            var config = new Configuration();

            var options = config.GetTextGraphicsOptions();
            var options2 = config.GetTextGraphicsOptions();
            Assert.Equal(options, options2);
        }

        [Fact]
        public void GetDefaultOptionsFromProcessingContext_AlwaysReturnsInstance()
        {
            var config = new Configuration();
            var context = new FakeImageOperationsProvider.FakeImageOperations<Rgba32>(config, null, true);

            var ctxOptions = context.GetTextGraphicsOptions();
            Assert.NotNull(ctxOptions);
        }

        [Fact]
        public void GetDefaultOptionsFromProcessingContext_AlwaysReturnsInstanceEvenIfSetToNull()
        {
            var config = new Configuration();
            var context = new FakeImageOperationsProvider.FakeImageOperations<Rgba32>(config, null, true);

            context.SetTextGraphicsOptions((TextGraphicsOptions)null);
            var ctxOptions = context.GetTextGraphicsOptions();
            Assert.NotNull(ctxOptions);
        }

        [Fact]
        public void GetDefaultOptionsFromProcessingContext_FallbackToConfigsInstance()
        {
            var option = new TextGraphicsOptions();
            var config = new Configuration();
            config.SetTextGraphicsOptions(option);
            var context = new FakeImageOperationsProvider.FakeImageOperations<Rgba32>(config, null, true);

            var ctxOptions = context.GetTextGraphicsOptions();
            Assert.Equal(option, ctxOptions);
        }

        [Fact]
        public void GetDefaultOptionsFromProcessingContext_IgnoreIncorectlyTypesDictionEntry()
        {
            var config = new Configuration();
            var context = new FakeImageOperationsProvider.FakeImageOperations<Rgba32>(config, null, true);
            context.Properties[typeof(TextGraphicsOptions)] = "wronge type";
            var options = context.GetTextGraphicsOptions();
            Assert.NotNull(options);
            Assert.IsType<TextGraphicsOptions>(options);
        }
    }
}
