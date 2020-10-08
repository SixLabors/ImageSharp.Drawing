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

        private void TestScan(IPath path, float min, float max, float dy, float[][] expected)
        {
            PolygonScanner scanner = PolygonScanner.Create(path, min, max, dy, Configuration.Default.MemoryAllocator);

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
        public void Wooo()
        {
            IPath poly1 = PolygonTest.CreatePolygon((0, 0), (10, 0),(10, 10), (0, 10));
            IPath poly2 = PolygonTest.CreatePolygon((0, 10), (10, 10), (10, 0), (0, 0));
            IPath poly3 = PolygonTest.CreatePolygon((0, 0), (10, 0),(10, 10), (0, 10), (0,0));

            PrintPoints(poly1.Flatten().Single().Points.ToArray());
            PrintPoints(poly2.Flatten().Single().Points.ToArray());
            PrintPoints(poly3.Flatten().Single().Points.ToArray());
            DebugDraw.Polygon(poly1);
            DebugDraw.Polygon(poly2);
        }
        
        [Fact]
        public void BasicConcave01()
        {
            var stuff = new[] {(0, 0), (10, 10), (20, 0), (20, 20), (0f, 20f)};
            
            IPath poly = PolygonTest.CreatePolygon((0,0), (10,10), (20,0), (20,20), (0,20) );
            DebugDraw.Polygon(poly);

            float[][] expected =
            {
                new float[] { /*0f, 20.000000f*/ },
                new float[] { 0f, 1.0000000f, 19.000000f, 20.000000f },
                new float[] { 0f, 2.0000000f, 18.000000f, 20.000000f },
                new float[] { 0f, 3.0000000f, 17.000000f, 20.000000f },
                new float[] { 0f, 4.0000000f, 16.000000f, 20.000000f },
                new float[] { 0f, 5.0000000f, 15.000000f, 20.000000f },
                new float[] { 0f, 6.0000000f, 14.000000f, 20.000000f },
                new float[] { 0f, 7.0000000f, 13.000000f, 20.000000f },
                new float[] { 0f, 8.0000000f, 12.000000f, 20.000000f },
                new float[] { 0f, 9.0000000f, 11.000000f, 20.000000f },
                new float[] { 0f, /*10.000000f,*/ 20.000000f },
                new float[] { 0f, 20.000000f },
                new float[] { 0f, 20.000000f },
                new float[] { 0f, 20.000000f },
                new float[] { 0f, 20.000000f },
                new float[] { 0f, 20.000000f },
                new float[] { 0f, 20.000000f },
                new float[] { 0f, 20.000000f },
                new float[] { 0f, 20.000000f },
                new float[] { 0f, 20.000000f },
                new float[] {  },
            };
            
            TestScan(poly, 0, 20, 1f, expected);
        }
        
        [Fact]
        public void BasicConcave02()
        {
            IPath poly = PolygonTest.CreatePolygon((0, 3), (3, 3), (3, 0), (1, 2), (1, 1), (0, 0));
            DebugDraw.Polygon(poly, 1f, 100f);

            float[][] expected =
            {
                new float[] {/*0f, 3.0000000f*/},
                new float[] { 0f, 0.50000000f, 2.5000000f, 3.0000000f },
                new float[] { 0f, 1.0000000f, 2.0000000f, 3.0000000f },
                new float[] { 0f, 1.0000000f, 1.5000000f, 3.0000000f },
                new float[] { 0f, /*1.0000000f,*/ 3.0000000f },
                new float[] { 0f, 3.0000000f },
                new float[] { 0f, 3.0000000f },
            };
            TestScan(poly, 0, 3, 0.5f, expected);
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
                new float[] { 0f, /*1.0000000f, 3.0000000f,*/ 4.0000000f, 5.0000000f, 6.0000000f },
                new float[] { 0f, 0.80000000f, 1.2000000f, 4.0000000f, 5.0000000f, 6.0000000f },
                new float[] { 0f, 0.60000000f, 1.4000000f, 4.0000000f, 5.0000000f, 6.0000000f },
                new float[] { 0f, 0.40000000f, 1.6000000f, 4.0000000f, 5.0000000f, 6.0000000f },
                new float[] { 0f, 0.20000000f, 1.8000000f, 4.0000000f, 5.0000000f, 6.0000000f },
                new float[] { /*0f,*/ 2.0000000f, 4.0000000f, 5.0000000f, 6.0000000f },
            };
            TestScan(poly, 0, 2, 0.2f, expected);
        }

        [Fact]
        public void BasicConcave04()
        {
            IPath poly = PolygonTest.CreatePolygon((10, 0), (20, 0), (20, 30), (10, 30), (10, 20), (0, 20), (0, 10), (10, 10));
            DebugDraw.Polygon(poly);

            float[][] expected =
            {
                new float[] {10.000000f, 20.000000f},
                new float[] {0f,/* 10.000000f,*/ 20.000000f},
                new float[] {0f, /*10.000000f,*/ 20.000000f},
                new float[] {10.000000f, 20.000000f},
            };
            
            TestScan(poly, 0, 30, 10, expected);

            // var l = MakeHLine(0);
            // PrintPoints(poly.FindIntersections(l.Start, l.End).ToArray());
        }

        // https://i.pinimg.com/originals/81/ae/17/81ae1726065726a5c0b380e9035dd32c.jpg
        [Fact]
        public void CartesianBear()
        {
            IPath poly = PolygonTest.CreatePolygon((10, 10), (50, 10), (50, 30), (40, 30), (40, 20), (30, 20), (30, 30), (20, 30),
                (20, 20), (10, 20));
            DebugDraw.Polygon(poly);

            var l = PolygonTest.CreateHorizontalLine(20);

            PointF[] isc = poly.FindIntersections(l.Start, l.End).ToArray();
            PrintPointsX(isc);

            float[][] expected =
            {
                new float[] { },
                new float[] {10.000000f, 50.000000f},
                new float[] {10.000000f, 50.000000f},
                new float[] {10.000000f, 20.000000f, 30.000000f, 40.000000f, 50.000000f},
                new float[] {20.000000f, 30.000000f, 40.000000f, 50.000000f},
                new float[] {20.000000f, 30.000000f, 40.000000f, 50.000000f},
                new float[] { },
            };
        }

        [Fact]
        public void SelfIntersecting01()
        {
            IPath poly = PolygonTest.CreatePolygon((0, 0), (10, 0), (0, 10), (10, 10));
            DebugDraw.Polygon(poly, 10f, 10f);

            float[][] expected =
            {
                new float[] {/*0f, 10.000000f*/},
                new float[] {2.5000000f, 7.5000000f},
                new float[] {5.0000000f, 5.0000000f }, // self-intersection point should be duplicate!!
                new float[] {2.5000000f, 7.5000000f},
                new float[] {/*0f, 10.000000f*/},
            };
            TestScan(poly, 0, 10, 2.5f, expected);
        }
        
        [Fact]
        public void SelfIntersecting02()
        {
            IPath poly = PolygonTest.CreatePolygon((0, 0), (10, 10), (10, 0), (0, 10));
            DebugDraw.Polygon(poly, 10f, 10f);

            float[][] expected =
            {
                new float[] {/*0f, 10.000000f*/},
                new float[] {0f, 2.5000000f, 7.5000000f, 10.000000f},
                new float[] {0f, 5.0000000f, 5.0000000f, 10.000000f}, // self-intersection point should be duplicate!!
                new float[] {0f, 2.5000000f, 7.5000000f, 10.000000f},
                new float[] {/*0f, 10.000000f*/},
            };
            TestScan(poly, 0, 10, 2.5f, expected);
        }

        [Theory]
        [InlineData(IntersectionRule.OddEven)]
        [InlineData(IntersectionRule.Nonzero)]
        public void SelfIntersecting03(IntersectionRule rule)
        {
            
            IPath poly = PolygonTest.CreatePolygon((10, 30), (10, 20), (50, 20), (50, 50), (20, 50), (20, 10), (30, 10), (30, 40),
                (40, 40), (40, 30), (10, 30));
            DebugDraw.Polygon(poly, 10f, 10f);

            float[][] expected =
            {
                /*  0 */ new float[] {  },
                /*  1 */ new float[] { 20.000000f, 30.000000f },
                /*  2 */ new float[] { 20.000000f, 30.000000f },
                /*  3 */ new float[] { 10.000000f, 50.000000f }, 
                /*  4 */ new float[] { 10.000000f, 50.000000f },
                /*  5 */ new float[] { 10.000000f, 50.000000f },
                /*  6 */ new float[] { 20.000000f, 30.000000f, 40.000000f, 50.000000f },
                /*  7 */ new float[] { 20.000000f, 30.000000f, 40.000000f, 50.000000f },
                /*  8 */ new float[] { 20.000000f, 50.000000f },
                /*  9 */ new float[] { 20.000000f, 50.000000f },
                /* 10 */ new float[] {  },
            };

            if (rule == IntersectionRule.OddEven)
            {
                expected[3] = new float[] {10.000000f, 20.000000f, 30.000000f, 50.000000f};
                expected[4] = new float[] {10.000000f, 20.000000f, 30.000000f, 50.000000f};
                expected[5] = new float[] {10.000000f, 20.000000f, 30.000000f, 40.000000f, 50.000000f};
            }
        }

        [Fact]
        public void Case10()
        {
            IPath poly = PolygonTest.CreatePolygon((82.142F, 63.157F), (37, 85), (65, 137), (103.792F, 79.11F), (200, 150), (50, 300), (10, 10));
            
            PointF[] pleas = poly.Flatten().First().Points.ToArray();
            
            PrintPointsX(pleas);

            DebugDraw.Polygon(poly, 10f, 5f);
        }

        public static readonly TheoryData<string, (float y, float[] x)[] > NumericCornerCasesData =
            new TheoryData<string, (float y, float[] x)[] >
            {
                {"A", new[]
                {
                    (2.5f, new float[] {2, 11}),
                    (2.75f, new float[] {2, 11}),
                    (3f, new float[]{2, 8, 8, 11}),
                    (3.25f, new float[]{11,11})
                }},
                {"B", new[]
                {
                    (2.5f, new float[] {12, 21}),
                    (2.75f, new float[] {12, 21}),
                    (3f, new float[]{12, 15, 15, 21}),
                    (3.25f, new float[]{18, 21}),
                }},
                {"C", new[]
                {
                    (3.5f, new float[] {2, 8}),
                    (3.75f, new float[] {2, 8}),
                    (4f, new float[] {2, 8}),
                }},
                {"D", new[]
                {
                    (3.25f, new float[] {12,12}),
                    (3.5f, new float[] {12, 18}),
                    (3.75f, new float[] {12, 15, 15, 18}),
                    (4f, new float[] {12, 12, 18, 18}),
                }},
                {"E", new[]
                {
                    (4.5f, new float[] {3,3,6,6}),
                    (4.75f, new float[] { 2.4166667f, 4, 4, 6}),
                    (5f, new float[] {2, 6}),
                }},
                {"F", new[]
                {
                    (4.25f, new float[] {13,13}),
                    (4.5f, new float[] {12.714286f, 13.444444f,16,16}),
                    (4.75f, new float[] {12.357143f, 14, 14, 3.2857143f}),
                    (5f, new float[] {12, 16}),
                }},
                {"G", new[]
                {
                    (1.75f, new float[] { 6, 6}),
                    (2f, new float[] { 4.6315789f, 7.3684211f }),
                    (2.25f, new float[]{2, 10})
                }}
            };
        
        [Theory]
        [MemberData(nameof(NumericCornerCasesData))]
        public void NumericCornerCases(string name, (float y, float[] x)[] expectedIntersections)
        {
            Polygon poly = CornerCasePolygons.GetByName(name);
            DebugDraw.Polygon(poly, 0.25f, 100f, $"{nameof(NumericCornerCases)}_{name}");
        }
    }
}