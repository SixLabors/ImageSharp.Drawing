// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Processing.Backends;

namespace SixLabors.ImageSharp.Drawing.Tests.Processing.Backends;

public class WebGPUEnvironmentErrorTests
{
    [Fact]
    public void EveryErrorValue_HasADedicatedExceptionMessage()
    {
        string fallback = WebGPURuntime.CreateEnvironmentExceptionMessage((WebGPUEnvironmentError)int.MaxValue);

        foreach (WebGPUEnvironmentError error in Enum.GetValues<WebGPUEnvironmentError>())
        {
            if (error == WebGPUEnvironmentError.Success)
            {
                continue;
            }

            string message = WebGPURuntime.CreateEnvironmentExceptionMessage(error);

            Assert.False(string.IsNullOrWhiteSpace(message));
            Assert.NotEqual(fallback, message);
        }
    }
}
