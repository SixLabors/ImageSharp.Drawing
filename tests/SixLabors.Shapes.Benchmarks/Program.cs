using System;
using System.Reflection;
using BenchmarkDotNet.Running;

namespace SixLabors.Shapes.Benchmarks
{
    class Program
    {
        static void Main(string[] args)
        {
            new BenchmarkSwitcher(typeof(Program).GetTypeInfo().Assembly).Run(args);
            Console.ReadKey();
        }
    }
}