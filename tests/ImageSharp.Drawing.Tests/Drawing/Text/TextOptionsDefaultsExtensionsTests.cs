// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Drawing.Tests.Processing;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace SixLabors.ImageSharp.Drawing.Tests.Drawing.Text
{
    public class TextOptionsDefaultsExtensionsTests
    {
        [Fact]
        public void SetDefaultOptionsOnProcessingContext()
        {
            var option = new TextOptions();
            var config = new Configuration();
            var context = new FakeImageOperationsProvider.FakeImageOperations<Rgba32>(config, null, true);

            context.SetTextOptions(option);

            // sets the prop on the processing context not on the configuration
            Assert.Equal(option, context.Properties[typeof(TextOptions)]);
            Assert.DoesNotContain(typeof(TextOptions), config.Properties.Keys);
        }

        [Fact]
        public void UpdateDefaultOptionsOnProcessingContext_AlwaysNewInstance()
        {
            var option = new TextOptions()
            {
                TabWidth = 99
            };
            var config = new Configuration();
            var context = new FakeImageOperationsProvider.FakeImageOperations<Rgba32>(config, null, true);
            context.SetTextOptions(option);

            context.SetTextOptions(o =>
            {
                Assert.Equal(99, o.TabWidth); // has origional values
                o.TabWidth = 9;
            });

            TextOptions returnedOption = context.GetTextOptions();
            Assert.Equal(9, returnedOption.TabWidth);
            Assert.Equal(99, option.TabWidth); // hasn't been mutated
        }

        [Fact]
        public void SetDefaultOptionsOnConfiguration()
        {
            var option = new TextOptions();
            var config = new Configuration();

            config.SetTextOptions(option);

            Assert.Equal(option, config.Properties[typeof(TextOptions)]);
        }

        [Fact]
        public void UpdateDefaultOptionsOnConfiguration_AlwaysNewInstance()
        {
            var option = new TextOptions()
            {
                TabWidth = 99
            };
            var config = new Configuration();
            config.SetTextOptions(option);

            config.SetTextOptions(o =>
            {
                Assert.Equal(99, o.TabWidth); // has origional values
                o.TabWidth = 9;
            });

            TextOptions returnedOption = config.GetTextOptions();
            Assert.Equal(9, returnedOption.TabWidth);
            Assert.Equal(99, option.TabWidth); // hasn't been mutated
        }

        [Fact]
        public void GetDefaultOptionsFromConfiguration_SettingNullThenReturnsNewInstance()
        {
            var config = new Configuration();

            TextOptions options = config.GetTextOptions();
            Assert.NotNull(options);
            config.SetTextOptions((TextOptions)null);

            TextOptions options2 = config.GetTextOptions();
            Assert.NotNull(options2);

            // we set it to null should now be a new instance
            Assert.NotEqual(options, options2);
        }

        [Fact]
        public void GetDefaultOptionsFromConfiguration_IgnoreIncorectlyTypesDictionEntry()
        {
            var config = new Configuration();

            config.Properties[typeof(TextOptions)] = "wronge type";
            TextOptions options = config.GetTextOptions();
            Assert.NotNull(options);
            Assert.IsType<TextOptions>(options);
        }

        [Fact]
        public void GetDefaultOptionsFromConfiguration_AlwaysReturnsInstance()
        {
            var config = new Configuration();

            Assert.DoesNotContain(typeof(TextOptions), config.Properties.Keys);
            TextOptions options = config.GetTextOptions();
            Assert.NotNull(options);
        }

        [Fact]
        public void GetDefaultOptionsFromConfiguration_AlwaysReturnsSameValue()
        {
            var config = new Configuration();

            TextOptions options = config.GetTextOptions();
            TextOptions options2 = config.GetTextOptions();
            Assert.Equal(options, options2);
        }

        [Fact]
        public void GetDefaultOptionsFromProcessingContext_AlwaysReturnsInstance()
        {
            var config = new Configuration();
            var context = new FakeImageOperationsProvider.FakeImageOperations<Rgba32>(config, null, true);

            TextOptions ctxOptions = context.GetTextOptions();
            Assert.NotNull(ctxOptions);
        }

        [Fact]
        public void GetDefaultOptionsFromProcessingContext_AlwaysReturnsInstanceEvenIfSetToNull()
        {
            var config = new Configuration();
            var context = new FakeImageOperationsProvider.FakeImageOperations<Rgba32>(config, null, true);

            context.SetTextOptions((TextOptions)null);
            TextOptions ctxOptions = context.GetTextOptions();
            Assert.NotNull(ctxOptions);
        }

        [Fact]
        public void GetDefaultOptionsFromProcessingContext_FallbackToConfigsInstance()
        {
            var option = new TextOptions();
            var config = new Configuration();
            config.SetTextOptions(option);
            var context = new FakeImageOperationsProvider.FakeImageOperations<Rgba32>(config, null, true);

            TextOptions ctxOptions = context.GetTextOptions();
            Assert.Equal(option, ctxOptions);
        }

        [Fact]
        public void GetDefaultOptionsFromProcessingContext_IgnoreIncorectlyTypesDictionEntry()
        {
            var config = new Configuration();
            var context = new FakeImageOperationsProvider.FakeImageOperations<Rgba32>(config, null, true);
            context.Properties[typeof(TextOptions)] = "wronge type";
            TextOptions options = context.GetTextOptions();
            Assert.NotNull(options);
            Assert.IsType<TextOptions>(options);
        }
    }
}
