// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Linq;
using System.Text;
using SixLabors.ImageSharp.Drawing.Shapes.Scan;
using Xunit;
using Xunit.Abstractions;
using IOPath = System.IO.Path;

namespace SixLabors.ImageSharp.Drawing.Tests.Shapes.Scan
{
    public class PolygonScannerTests
    {
        private readonly ITestOutputHelper output;

        private static readonly DebugDraw DebugDraw = new DebugDraw(nameof(PolygonScannerTests));
        public PolygonScannerTests(ITestOutputHelper output)
        {
            this.output = output;
        }

        private void PrintPoints(ReadOnlySpan<PointF> points)
        {
            StringBuilder sb = new StringBuilder();

            foreach (PointF p in points)
            {
                sb.Append($"({p.X},{p.Y}), ");
            }
            this.output.WriteLine(sb.ToString());
        }
        
        
        private void PrintPointsX(PointF[] isc)
        {
            string s = string.Join(",", isc.Select(p => $"{p.X}"));
            this.output.WriteLine(s);
        }

        private static void VerifyScanline(ReadOnlySpan<float> expected, ReadOnlySpan<float> actual)
        {
            Assert.Equal(expected.Length, actual.Length);
            ApproximateFloatComparer cmp = new ApproximateFloatComparer(1e-5f);

            for (int i = 0; i < expected.Length; i++)
            {
                Assert.Equal(expected[i], actual[i], cmp);
            }
        }

        private void TestScan(IPath path,int min, int max, int subsampling, float[][] expected) =>
            TestScan(path, min, max, subsampling, expected, IntersectionRule.OddEven);

        private void TestScan(IPath path, int min, int max, int subsampling, float[][] expected, IntersectionRule intersectionRule)
        {
            PolygonScanner scanner = PolygonScanner.Create(path, min, max, subsampling, intersectionRule, Configuration.Default.MemoryAllocator);

            try
            {
                int i = 0;
                while (scanner.MoveToNextScanline())
                {
                    ReadOnlySpan<float> intersections = scanner.ScanCurrentLine();

                    VerifyScanline(expected[i], intersections);
                    i++;
                }
                
                Assert.Equal(expected.Length, i);
            }
            finally
            {
                scanner.Dispose();
            }
        }

        [Fact]
        public void BasicConcave00()
        {
            IPath poly = PolygonTest.CreatePolygon((2, 2), (5, 3), (5, 6), (8, 6), (8, 9), (5, 11), (2, 7));
            DebugDraw.Polygon(poly, 1f, 50f);

            float[][] expected =
            {
                new float[] { 2, 2 },
                new float[] { 2, 5 },
                new float[] { 2, 5 },
                new float[] { 2, 5 },
                new float[] { 2, 5, 5, 8 },
                new float[] { 2, 8 },
                new float[] { 2.75f, 8},
                new float[] { 3.5f, 8 },
                new float[] { 4.25f, 6.5f},
                new float[] { 5, 5 },
            };
            
            TestScan(poly, 2, 11, 1, expected);
        }

        [Fact]
        public void BasicConcave01()
        {
            IPath poly = PolygonTest.CreatePolygon((0,0), (10,10), (20,0), (20,20), (0,20) );
            DebugDraw.Polygon(poly);

            float[][] expected =
            {
                new float[] { 0f, 0f, 20.000000f, 20.000000f, },
                new float[] { 0f, 1.0000000f, 19.000000f, 20.000000f },
                new float[] { 0f, 2.0000000f, 18.000000f, 20.000000f },
                new float[] { 0f, 3.0000000f, 17.000000f, 20.000000f },
                new float[] { 0f, 4.0000000f, 16.000000f, 20.000000f },
                new float[] { 0f, 5.0000000f, 15.000000f, 20.000000f },
                new float[] { 0f, 6.0000000f, 14.000000f, 20.000000f },
                new float[] { 0f, 7.0000000f, 13.000000f, 20.000000f },
                new float[] { 0f, 8.0000000f, 12.000000f, 20.000000f },
                new float[] { 0f, 9.0000000f, 11.000000f, 20.000000f },
                new float[] { 0f, 10.000000f, 10.000000f, 20.000000f },
                new float[] { 0f, 20.000000f },
                new float[] { 0f, 20.000000f },
                new float[] { 0f, 20.000000f },
                new float[] { 0f, 20.000000f },
                new float[] { 0f, 20.000000f },
                new float[] { 0f, 20.000000f },
                new float[] { 0f, 20.000000f },
                new float[] { 0f, 20.000000f },
                new float[] { 0f, 20.000000f },
                new float[] { 0f, 20.000000f  },
            };
            
            TestScan(poly, 0, 20, 1, expected);
        }
        
        [Fact]
        public void BasicConcave02()
        {
            IPath poly = PolygonTest.CreatePolygon((0, 3), (3, 3), (3, 0), (1, 2), (1, 1), (0, 0));
            DebugDraw.Polygon(poly, 1f, 100f);

            float[][] expected =
            {
                new float[] { 0f, 0f, 3.0000000f, 3.0000000f },
                new float[] { 0f, 0.50000000f, 2.5000000f, 3.0000000f },
                new float[] { 0f, 1.0000000f, 2.0000000f, 3.0000000f },
                new float[] { 0f, 1.0000000f, 1.5000000f, 3.0000000f },
                new float[] { 0f, 1.0000000f, 1.0000000f, 3.0000000f },
                new float[] { 0f, 3.0000000f },
                new float[] { 0f, 3.0000000f },
            };
            TestScan(poly, 0, 3, 2, expected);
        }

        [Fact]
        public void BasicConcave03()
        {
            IPath poly = PolygonTest.CreatePolygon((0, 0), (2, 0), (3, 1), (3, 0), (6, 0), (6, 2), (5, 2), (5, 1), (4, 1), (4, 2), (2, 2), (1, 1), (0, 2));
            DebugDraw.Polygon(poly, 1f, 100f);

            float[][] expected =
            {
                new float[] { 0f, 2.0000000f, 3.0000000f, 6.0000000f },
                new float[] { 0f, 2.2000000f, 3.0000000f, 6.0000000f },
                new float[] { 0f, 2.4000000f, 3.0000000f, 6.0000000f },
                new float[] { 0f, 2.6000000f, 3.0000000f, 6.0000000f },
                new float[] { 0f, 2.8000000f, 3.0000000f, 6.0000000f },
                new float[] { 0f, 1.0000000f, 1.0000000f, 3.0000000f, 3.0000000f, 4.0000000f, 4.0000000f, 5.0000000f, 5.0000000f, 6.0000000f },
                new float[] { 0f, 0.80000000f, 1.2000000f, 4.0000000f, 5.0000000f, 6.0000000f },
                new float[] { 0f, 0.60000000f, 1.4000000f, 4.0000000f, 5.0000000f, 6.0000000f },
                new float[] { 0f, 0.40000000f, 1.6000000f, 4.0000000f, 5.0000000f, 6.0000000f },
                new float[] { 0f, 0.20000000f, 1.8000000f, 4.0000000f, 5.0000000f, 6.0000000f },
                new float[] { 0f, 0f, 2.0000000f, 4.0000000f, 5.0000000f, 6.0000000f },
            };
            TestScan(poly, 0, 2, 5, expected);
        }

        [Fact]
        public void SelfIntersecting01()
        {
            IPath poly = PolygonTest.CreatePolygon((0, 0), (10, 0), (0, 10), (10, 10));
            DebugDraw.Polygon(poly, 10f, 10f);

            float[][] expected =
            {
                new float[] { 0f, 10.000000f },
                new float[] { 0.50000000f, 9.5000000f },
                new float[] { 1.0000000f, 9.0000000f },
                new float[] { 1.5000000f, 8.5000000f },
                new float[] { 2.0000000f, 8.0000000f },
                new float[] { 2.5000000f, 7.5000000f },
                new float[] { 3.0000000f, 7.0000000f },
                new float[] { 3.5000000f, 6.5000000f },
                new float[] { 4.0000000f, 6.0000000f },
                new float[] { 4.5000000f, 5.5000000f },
                new float[] { 5.0000000f, 5.0000000f },
                new float[] { 4.5000000f, 5.5000000f },
                new float[] { 4.0000000f, 6.0000000f },
                new float[] { 3.5000000f, 6.5000000f },
                new float[] { 3.0000000f, 7.0000000f },
                new float[] { 2.5000000f, 7.5000000f },
                new float[] { 2.0000000f, 8.0000000f },
                new float[] { 1.5000000f, 8.5000000f },
                new float[] { 1.0000000f, 9.0000000f },
                new float[] { 0.50000000f, 9.5000000f },
                new float[] { 0f, 10.000000f },
            };
            TestScan(poly, 0, 10, 2, expected);
        }
        
        [Fact]
        public void SelfIntersecting02()
        {
            IPath poly = PolygonTest.CreatePolygon((0, 0), (10, 10), (10, 0), (0, 10));
            DebugDraw.Polygon(poly, 10f, 10f);

            float[][] expected =
            {
                new float[] { 0f, 0f, 10.000000f,10.000000f },
                new float[] { 0f, 0.50000000f, 9.5000000f, 10.000000f },
                new float[] { 0f, 1.0000000f, 9.0000000f, 10.000000f },
                new float[] { 0f, 1.5000000f, 8.5000000f, 10.000000f },
                new float[] { 0f, 2.0000000f, 8.0000000f, 10.000000f },
                new float[] { 0f, 2.5000000f, 7.5000000f, 10.000000f },
                new float[] { 0f, 3.0000000f, 7.0000000f, 10.000000f },
                new float[] { 0f, 3.5000000f, 6.5000000f, 10.000000f },
                new float[] { 0f, 4.0000000f, 6.0000000f, 10.000000f },
                new float[] { 0f, 4.5000000f, 5.5000000f, 10.000000f },
                new float[] { 0f, 5.0000000f, 5.0000000f, 10.000000f },
                new float[] { 0f, 4.5000000f, 5.5000000f, 10.000000f },
                new float[] { 0f, 4.0000000f, 6.0000000f, 10.000000f },
                new float[] { 0f, 3.5000000f, 6.5000000f, 10.000000f },
                new float[] { 0f, 3.0000000f, 7.0000000f, 10.000000f },
                new float[] { 0f, 2.5000000f, 7.5000000f, 10.000000f },
                new float[] { 0f, 2.0000000f, 8.0000000f, 10.000000f },
                new float[] { 0f, 1.5000000f, 8.5000000f, 10.000000f },
                new float[] { 0f, 1.0000000f, 9.0000000f, 10.000000f },
                new float[] { 0f, 0.50000000f, 9.5000000f, 10.000000f },
                new float[] { 0f, 0f, 10.000000f, 10.000000f },
            };
            TestScan(poly, 0, 10, 2, expected);
        }

        [Theory]
        [InlineData(IntersectionRule.OddEven)]
        [InlineData(IntersectionRule.Nonzero)]
        public void SelfIntersecting03(IntersectionRule rule)
        {
            
            IPath poly = PolygonTest.CreatePolygon((1, 3), (1, 2), (5, 2), (5, 5), (2, 5), (2, 1), (3, 1), (3, 4), (4, 4), (4, 3), (1, 3));
            DebugDraw.Polygon(poly, 1f, 100f);

            float[][] expected;
            if (rule == IntersectionRule.OddEven)
            {
                expected = new[]
                {
                    new float[] {2.0000000f, 3.0000000f},
                    new float[] {2.0000000f, 3.0000000f},
                    new float[] {1.0000000f, 2.0000000f, 3.0000000f, 5.0000000f},
                    new float[] {1.0000000f, 2.0000000f, 3.0000000f, 5.0000000f},
                    new float[] {1.0000000f, 2.0000000f, 3.0000000f, 4.0000000f, 4.0000000f, 5.0000000f},
                    new float[] {2.0000000f, 3.0000000f, 4.0000000f, 5.0000000f},
                    new float[] {2.0000000f, 3.0000000f, 3.0000000f, 4.0000000f, 4.0000000f, 5.0000000f},
                    new float[] {2.0000000f, 5.0000000f},
                    new float[] {2.0000000f, 5.0000000f},
                };
            }
            else
            {
                expected = new[]
                {
                    new float[] {2.0000000f, 3.0000000f},
                    new float[] {2.0000000f, 3.0000000f},
                    new float[] {1.0000000f, 5.0000000f},
                    new float[] {1.0000000f, 5.0000000f},
                    new float[] {1.0000000f, 4.0000000f, 4.0000000f, 5.0000000f},
                    new float[] {2.0000000f, 3.0000000f, 4.0000000f, 5.0000000f},
                    new float[] {2.0000000f, 3.0000000f, 3.0000000f, 4.0000000f, 4.0000000f, 5.0000000f},
                    new float[] {2.0000000f, 5.0000000f},
                    new float[] {2.0000000f, 5.0000000f},
                };
            }
            
            TestScan(poly, 1, 5, 2, expected, rule);
        }

        private static (float y, float[] x) Empty(float y) => (y, new float[0]);

        public static readonly TheoryData<string, (float y, float[] x)[] > NumericCornerCasesData =
            new TheoryData<string, (float y, float[] x)[] >
            {
                {"A", new[]
                {
                    Empty(2f), Empty(2.25f),
                    
                    (2.5f, new float[] {2, 11}),
                    (2.75f, new float[] {2, 11}),
                    (3f, new float[]{2, 8, 8, 11}),
                    (3.25f, new float[]{11,11}),
                    
                    Empty(3.5f), Empty(3.75f), Empty(4f),
                }},
                {"B", new[]
                {
                    Empty(2f), Empty(2.25f),
                    
                    (2.5f, new float[] {12, 21}),
                    (2.75f, new float[] {12, 21}),
                    (3f, new float[]{12, 15, 15, 21}),
                    (3.25f, new float[]{18, 21}),
                    
                    Empty(3.5f), Empty(3.75f), Empty(4f),
                }},
                {"C", new[]
                {
                    Empty(3f), Empty(3.25f),
                    
                    (3.5f, new float[] {2, 8}),
                    (3.75f, new float[] {2, 8}),
                    (4f, new float[] {2, 8}),
                }},
                {"D", new[]
                {
                    Empty(3f),

                    (3.25f, new float[] {12,12}),
                    (3.5f, new float[] {12, 18}),
                    (3.75f, new float[] {12, 15, 15, 18}),
                    (4f, new float[] {12, 12, 18, 18}),
                }},
                {"E", new[]
                {
                    Empty(4f), Empty(4.25f),

                    (4.5f, new float[] {3,3,6,6}),
                    (4.75f, new float[] { 2.4166667f, 4, 4, 6}),
                    (5f, new float[] {2, 6}),
                }},
                {"F", new[]
                {
                    Empty(4f),

                    (4.25f, new float[] {13,13}),
                    (4.5f, new float[] {12.714286f, 13.444444f,16,16}),
                    (4.75f, new float[] {12.357143f, 14, 14, 3.2857143f}),
                    (5f, new float[] {12, 16}),
                }},
                {"G", new[]
                {
                    Empty(1f), Empty(1.25f), Empty(1.5f),

                    (1.75f, new float[] { 6, 6}),
                    (2f, new float[] { 4.6315789f, 7.3684211f }),
                    (2.25f, new float[]{2, 10}),
                    
                    Empty(2.5f), Empty(1.75f), Empty(3f),
                }},
                {"H", new []
                {
                    Empty(1f), Empty(1.25f), Empty(1.5f),
                    
                    (1.75f, new float[] { 16, 16 }),
                    (2f, new float[]{14, 14, 14, 16}), // this emits 2 dummy points, but normally it should not corrupt quality too much
                    (2.25f, new float[]{ 16, 16 }),
                    
                    Empty(2.5f), Empty(1.75f), Empty(3f),
                }}
            };
        
        [Theory]
        [MemberData(nameof(NumericCornerCasesData))]
        public void NumericCornerCases(string name, (float y, float[] x)[] expectedIntersections)
        {
            Polygon poly = NumericCornerCasePolygons.GetByName(name);
            DebugDraw.Polygon(poly, 0.25f, 100f, $"{nameof(NumericCornerCases)}_{name}");

            int min = (int)expectedIntersections.First().y;
            int max = (int)expectedIntersections.Last().y;
            
            TestScan(poly, min, max, 4, expectedIntersections.Select(i => i.x).ToArray());
        }
    }
}