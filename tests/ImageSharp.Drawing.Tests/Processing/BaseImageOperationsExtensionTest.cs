// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System.Collections.Generic;
using SixLabors.ImageSharp.Advanced;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

using Xunit;

namespace SixLabors.ImageSharp.Drawing.Tests.Processing
{
    public abstract class BaseImageOperationsExtensionTest
    {
        protected readonly IImageProcessingContext operations;
        private readonly FakeImageOperationsProvider.FakeImageOperations<Rgba32> internalOperations;
        protected readonly Rectangle rect;
        protected readonly GraphicsOptions graphicsOptions;
        protected readonly ShapeOptions shapeOptions;
        private readonly Image<Rgba32> source;

        public Rectangle SourceBounds() => this.source.Bounds();

        public BaseImageOperationsExtensionTest()
        {
            this.graphicsOptions = new GraphicsOptions
            {
                AntialiasSubpixelDepth = 99,
                Antialias = false,
                BlendPercentage = 0.9f,
                AlphaCompositionMode = PixelAlphaCompositionMode.DestOut,
                ColorBlendingMode = PixelColorBlendingMode.Multiply
            };

            this.shapeOptions = new ShapeOptions { IntersectionRule = IntersectionRule.NonZero };
            this.source = new Image<Rgba32>(91 + 324, 123 + 56);
            this.rect = new Rectangle(91, 123, 324, 56); // make this random?
            this.internalOperations = new FakeImageOperationsProvider.FakeImageOperations<Rgba32>(this.source.GetConfiguration(), this.source, false);
            this.internalOperations.SetShapeOptions(this.shapeOptions);
            this.internalOperations.SetGraphicsOptions(this.graphicsOptions);
            this.operations = this.internalOperations;
        }

        public IEnumerable<T> VerifyAll<T>()
        {
            List<T> items = new();
            Assert.All(this.internalOperations.Applied, operation =>
            {
                if (operation.NonGenericProcessor != null)
                {
                    items.Add(Assert.IsType<T>(operation.NonGenericProcessor));
                    return;
                }

                items.Add(Assert.IsType<T>(operation.GenericProcessor));
            });

            return items;
        }

        public T Verify<T>(int index = 0)
        {
            Assert.InRange(index, 0, this.internalOperations.Applied.Count - 1);

            FakeImageOperationsProvider.FakeImageOperations<Rgba32>.AppliedOperation operation = this.internalOperations.Applied[index];

            if (operation.NonGenericProcessor != null)
            {
                return Assert.IsType<T>(operation.NonGenericProcessor);
            }

            return Assert.IsType<T>(operation.GenericProcessor);
        }

        public T Verify<T>(Rectangle rect, int index = 0)
        {
            Assert.InRange(index, 0, this.internalOperations.Applied.Count - 1);

            FakeImageOperationsProvider.FakeImageOperations<Rgba32>.AppliedOperation operation = this.internalOperations.Applied[index];

            Assert.Equal(rect, operation.Rectangle);

            if (operation.NonGenericProcessor != null)
            {
                return Assert.IsType<T>(operation.NonGenericProcessor);
            }

            return Assert.IsType<T>(operation.GenericProcessor);
        }
    }
}
