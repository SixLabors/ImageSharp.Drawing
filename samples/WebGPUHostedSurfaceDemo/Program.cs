// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace WebGPUHostedSurfaceDemo;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }
}
