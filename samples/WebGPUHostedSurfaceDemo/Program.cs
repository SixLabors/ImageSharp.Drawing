// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace WebGPUHostedSurfaceDemo;

/// <summary>
/// Entry point for the hosted WebGPU surface sample.
/// </summary>
internal static class Program
{
    /// <summary>
    /// Starts the WinForms message loop and shows the sample form.
    /// </summary>
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }
}
