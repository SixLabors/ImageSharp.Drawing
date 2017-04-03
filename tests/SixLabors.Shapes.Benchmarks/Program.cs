using System;
using System.Reflection;
using BenchmarkDotNet.Running;

namespace SixLabors.Shapes.Benchmarks
{
    class Program
    {
        static void Main(string[] args)
        {
            //var p = new InteralPath_FindIntersections();
            //for (var i = 0; i < 10; i++)
            //{
            //    p.InternalNew();
            //    Console.WriteLine(i);
            //}

            new BenchmarkSwitcher(typeof(Program).GetTypeInfo().Assembly).Run(args);
            Console.ReadKey();
        }
    }
}