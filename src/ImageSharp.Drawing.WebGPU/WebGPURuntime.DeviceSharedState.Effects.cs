// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Diagnostics;
using System.Text;
using SixLabors.ImageSharp.Drawing.Processing.Backends.Native;

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

internal static unsafe partial class WebGPURuntime
{
    internal sealed partial class DeviceSharedState
    {
        /// <summary>
        /// Gets or creates the exact render pipeline used by one generated layer-effect module.
        /// </summary>
        /// <param name="moduleSource">The complete generated WGSL module and source mapping.</param>
        /// <param name="uniformByteLength">The byte length of the user uniform binding.</param>
        /// <param name="outputFormat">The render target format written by the fragment shader.</param>
        /// <param name="bindGroupLayout">Receives the cached bind-group layout.</param>
        /// <param name="pipeline">Receives the cached render pipeline.</param>
        /// <param name="error">Receives the native resource-creation failure.</param>
        /// <returns><see langword="true"/> when the pipeline is available; otherwise <see langword="false"/>.</returns>
        public bool TryGetOrCreateEffectPipeline(
            WebGPUShaderModuleSource moduleSource,
            int uniformByteLength,
            WGPUTextureFormat outputFormat,
            out WGPUBindGroupLayoutImpl* bindGroupLayout,
            out WGPURenderPipelineImpl* pipeline,
            out string? error)
        {
            bindGroupLayout = null;
            pipeline = null;

            ObjectDisposedException.ThrowIf(this.disposed, this);

            WebGPUEffectPipelineKey key = new(moduleSource, uniformByteLength, outputFormat);

            // The dictionary prevents duplicate infrastructure objects, while the per-entry lock
            // serializes native creation for this exact key without blocking unrelated programs.
            WebGPUEffectPipelineInfrastructure infrastructure = this.effectPipelines.GetOrAdd(
                key,
                static _ => new WebGPUEffectPipelineInfrastructure());

            lock (infrastructure)
            {
                if (infrastructure.Pipeline is not null)
                {
                    bindGroupLayout = infrastructure.BindGroupLayout;
                    pipeline = infrastructure.Pipeline;
                    error = null;
                    return true;
                }

                if (!this.TryCreateEffectPipelineInfrastructure(
                        moduleSource,
                        uniformByteLength,
                        outputFormat,
                        out WGPUBindGroupLayoutImpl* createdBindGroupLayout,
                        out WGPUPipelineLayoutImpl* createdPipelineLayout,
                        out WGPUShaderModuleImpl* createdShaderModule,
                        out WGPURenderPipelineImpl* createdPipeline,
                        out error))
                {
                    return false;
                }

                infrastructure.BindGroupLayout = createdBindGroupLayout;
                infrastructure.PipelineLayout = createdPipelineLayout;
                infrastructure.ShaderModule = createdShaderModule;
                infrastructure.Pipeline = createdPipeline;

                bindGroupLayout = createdBindGroupLayout;
                pipeline = createdPipeline;
                return true;
            }
        }

        /// <summary>
        /// Compiles every pipeline used by one shader effect for the supplied initial source representation.
        /// </summary>
        /// <param name="effect">The effect whose ordered passes are compiled.</param>
        /// <param name="sourceDescriptor">The representation sampled by the first pass.</param>
        public void PrecompileEffect(IWebGPUShaderEffectSource effect, WebGPUTargetDescriptor sourceDescriptor)
        {
            ReadOnlySpan<WebGPUShaderPass> passes = effect.GetShaderPasses();

            for (int i = 0; i < passes.Length; i++)
            {
                WebGPUShaderPass pass = passes[i];
                WebGPUShaderProgram program = pass.Program;
                WebGPUShaderModuleSource moduleSource = program.GetModuleSource(
                    sourceDescriptor,
                    pass.XBorderMode,
                    pass.YBorderMode);

                if (!this.TryGetOrCreateEffectPipeline(
                        moduleSource,
                        program.UniformLayout.ByteLength,
                        WGPUTextureFormat.RGBA16Float,
                        out _,
                        out _,
                        out string? error))
                {
                    throw new InvalidOperationException(error);
                }

                // The first pass samples the drawing target. Every later pass samples the
                // associated Rgba16Float working texture produced by its predecessor.
                sourceDescriptor = WebGPUShaderEffectWorkingTexture.Descriptor;
            }
        }

        /// <summary>
        /// Creates every device-owned object behind one effect pipeline and releases partial state on failure.
        /// </summary>
        private bool TryCreateEffectPipelineInfrastructure(
            WebGPUShaderModuleSource moduleSource,
            int uniformByteLength,
            WGPUTextureFormat outputFormat,
            out WGPUBindGroupLayoutImpl* bindGroupLayout,
            out WGPUPipelineLayoutImpl* pipelineLayout,
            out WGPUShaderModuleImpl* shaderModule,
            out WGPURenderPipelineImpl* pipeline,
            out string? error)
        {
            bindGroupLayout = this.CreateEffectBindGroupLayout(uniformByteLength);
            pipelineLayout = null;
            shaderModule = null;
            pipeline = null;

            if (bindGroupLayout is null)
            {
                error = "Failed to create the WebGPU layer-effect bind-group layout.";
                return false;
            }

            WGPUBindGroupLayoutImpl** bindGroupLayouts = stackalloc WGPUBindGroupLayoutImpl*[1];
            bindGroupLayouts[0] = bindGroupLayout;
            WGPUPipelineLayoutDescriptor pipelineLayoutDescriptor = new()
            {
                bindGroupLayoutCount = 1,
                bindGroupLayouts = bindGroupLayouts
            };

            pipelineLayout = this.Api.DeviceCreatePipelineLayout(this.Device, in pipelineLayoutDescriptor);
            if (pipelineLayout is null)
            {
                this.Api.BindGroupLayoutRelease(bindGroupLayout);
                bindGroupLayout = null;
                error = "Failed to create the WebGPU layer-effect pipeline layout.";
                return false;
            }

            if (!this.TryCreateValidatedShaderModule(moduleSource, out shaderModule, out error))
            {
                this.Api.PipelineLayoutRelease(pipelineLayout);
                this.Api.BindGroupLayoutRelease(bindGroupLayout);
                pipelineLayout = null;
                bindGroupLayout = null;
                return false;
            }

            pipeline = this.CreateCompositePipeline(pipelineLayout, shaderModule, outputFormat, CompositePipelineBlendMode.None);
            if (pipeline is null)
            {
                this.Api.ShaderModuleRelease(shaderModule);
                this.Api.PipelineLayoutRelease(pipelineLayout);
                this.Api.BindGroupLayoutRelease(bindGroupLayout);
                shaderModule = null;
                pipelineLayout = null;
                bindGroupLayout = null;
                error = "Failed to create the WebGPU layer-effect render pipeline.";
                return false;
            }

            error = null;
            return true;
        }

        /// <summary>
        /// Creates the fixed source/framework/user/sampler binding layout shared by one exact shader module.
        /// </summary>
        private WGPUBindGroupLayoutImpl* CreateEffectBindGroupLayout(int uniformByteLength)
        {
            WGPUBindGroupLayoutEntry* entries = stackalloc WGPUBindGroupLayoutEntry[4];
            entries[0] = new WGPUBindGroupLayoutEntry
            {
                binding = 0,
                visibility = (ulong)ShaderStage.Fragment,
                texture = new WGPUTextureBindingLayout
                {
                    sampleType = WGPUTextureSampleType.Float,
                    viewDimension = WGPUTextureViewDimension._2D,
                    multisampled = 0U
                }
            };
            entries[1] = new WGPUBindGroupLayoutEntry
            {
                binding = 1,
                visibility = (ulong)ShaderStage.Fragment,
                buffer = new WGPUBufferBindingLayout
                {
                    type = WGPUBufferBindingType.Uniform,
                    minBindingSize = WebGPUShaderFrameworkUniforms.ByteLength
                }
            };
            entries[2] = new WGPUBindGroupLayoutEntry
            {
                binding = 2,
                visibility = (ulong)ShaderStage.Fragment,
                buffer = new WGPUBufferBindingLayout
                {
                    type = WGPUBufferBindingType.Uniform,
                    minBindingSize = checked((ulong)uniformByteLength)
                }
            };
            entries[3] = new WGPUBindGroupLayoutEntry
            {
                binding = 3,
                visibility = (ulong)ShaderStage.Fragment,
                sampler = new WGPUSamplerBindingLayout
                {
                    type = WGPUSamplerBindingType.Filtering
                }
            };

            WGPUBindGroupLayoutDescriptor descriptor = new()
            {
                entryCount = 4,
                entries = entries
            };

            return this.Api.DeviceCreateBindGroupLayout(this.Device, in descriptor);
        }

        /// <summary>
        /// Creates a module and rejects it when native WGSL compilation reports an error.
        /// </summary>
        private bool TryCreateValidatedShaderModule(WebGPUShaderModuleSource moduleSource, out WGPUShaderModuleImpl* shaderModule, out string? error)
        {
            WGPUPopErrorScopeStatus scopeStatus = WGPUPopErrorScopeStatus.CallbackCancelled;
            WGPUErrorType errorType = WGPUErrorType.Unknown;
            string? diagnosticMessage = null;
            Exception? callbackException = null;

            // wgpu-native 29 exports wgpuShaderModuleGetCompilationInfo but deliberately leaves
            // it unimplemented. A validation error scope is the supported path for reporting an
            // invalid WGSL module without allowing the error to escape as an uncaptured failure.
            this.Api.DevicePushErrorScope(this.Device, WGPUErrorFilter.Validation);
            shaderModule = this.CreateShaderModule(moduleSource.Utf8Source);

            // PopErrorScope may complete asynchronously even in AllowSpontaneous mode. Copy its
            // borrowed message during the callback and poll the device until completion.
            using ManualResetEventSlim callbackReady = new(false);
            using WebGPUPopErrorScopeCallback callback = WebGPUPopErrorScopeCallback.From((status, type, message, _) =>
            {
                scopeStatus = status;
                errorType = type;

                try
                {
                    diagnosticMessage = message.ToManagedString();
                }
                catch (Exception ex)
                {
                    // Exceptions cannot cross the unmanaged callback boundary. Preserve the
                    // failure until control has returned to the managed compilation path.
                    callbackException = ex;
                }
                finally
                {
                    callbackReady.Set();
                }
            });

            this.Api.DevicePopErrorScope(this.Device, callback, null);

            Stopwatch stopwatch = Stopwatch.StartNew();
            while (!callbackReady.IsSet && stopwatch.ElapsedMilliseconds < CallbackTimeoutMilliseconds)
            {
                _ = this.Api.DevicePoll(this.Device, false, null);

                if (!callbackReady.IsSet)
                {
                    Thread.Yield();
                }
            }

            callback.Dispose();

            // Disposal synchronizes with an in-flight callback. Recheck the signal afterwards so
            // completion at the timeout boundary is not incorrectly reported as a timeout.
            if (!callbackReady.IsSet)
            {
                if (shaderModule is not null)
                {
                    this.Api.ShaderModuleRelease(shaderModule);
                }

                shaderModule = null;
                error = "Timed out while validating the WebGPU layer-effect shader module.";
                return false;
            }

            if (callbackException is not null)
            {
                if (shaderModule is not null)
                {
                    this.Api.ShaderModuleRelease(shaderModule);
                }

                shaderModule = null;
                throw new InvalidOperationException("Failed to copy the WebGPU layer-effect shader validation message.", callbackException);
            }

            if (scopeStatus != WGPUPopErrorScopeStatus.Success)
            {
                if (shaderModule is not null)
                {
                    this.Api.ShaderModuleRelease(shaderModule);
                }

                shaderModule = null;
                error = $"WebGPU returned '{scopeStatus}' while validating the layer-effect shader module.";
                return false;
            }

            if (errorType != WGPUErrorType.NoError)
            {
                if (shaderModule is not null)
                {
                    this.Api.ShaderModuleRelease(shaderModule);
                }

                shaderModule = null;
                WebGPUShaderDiagnostic[] diagnostics =
                [
                    new WebGPUShaderDiagnostic(
                        WebGPUShaderDiagnosticSeverity.Error,
                        diagnosticMessage ?? $"WebGPU shader validation failed with '{errorType}'.",
                        0,
                        0)
                ];

                throw new WebGPUShaderCompilationException(CreateCompilationErrorMessage(diagnostics), diagnostics);
            }

            if (shaderModule is null)
            {
                error = "Failed to create the WebGPU layer-effect shader module.";
                return false;
            }

            error = null;
            return true;
        }

        /// <summary>
        /// Formats compiler errors while retaining the structured diagnostics on the exception.
        /// </summary>
        private static string CreateCompilationErrorMessage(ReadOnlySpan<WebGPUShaderDiagnostic> diagnostics)
        {
            StringBuilder builder = new("WebGPU layer-effect shader compilation failed.");
            for (int i = 0; i < diagnostics.Length; i++)
            {
                WebGPUShaderDiagnostic diagnostic = diagnostics[i];
                builder.AppendLine();
                builder.Append(diagnostic.Severity);
                if (diagnostic.Line > 0)
                {
                    builder.Append(" at line ").Append(diagnostic.Line);
                    if (diagnostic.Column > 0)
                    {
                        builder.Append(", column ").Append(diagnostic.Column);
                    }
                }

                builder.Append(": ").Append(diagnostic.Message);
            }

            return builder.ToString();
        }

        /// <summary>
        /// Releases every device-owned object retained for one exact effect pipeline.
        /// </summary>
        private void ReleaseEffectPipelineInfrastructure(WebGPUEffectPipelineInfrastructure infrastructure)
        {
            if (infrastructure.Pipeline is not null)
            {
                this.Api.RenderPipelineRelease(infrastructure.Pipeline);
                infrastructure.Pipeline = null;
            }

            if (infrastructure.PipelineLayout is not null)
            {
                this.Api.PipelineLayoutRelease(infrastructure.PipelineLayout);
                infrastructure.PipelineLayout = null;
            }

            if (infrastructure.ShaderModule is not null)
            {
                this.Api.ShaderModuleRelease(infrastructure.ShaderModule);
                infrastructure.ShaderModule = null;
            }

            if (infrastructure.BindGroupLayout is not null)
            {
                this.Api.BindGroupLayoutRelease(infrastructure.BindGroupLayout);
                infrastructure.BindGroupLayout = null;
            }
        }

        /// <summary>
        /// Device-owned objects behind one exact effect pipeline. The instance is also its creation lock.
        /// </summary>
        private sealed class WebGPUEffectPipelineInfrastructure
        {
            /// <summary>
            /// Gets or sets the shader bind-group layout.
            /// </summary>
            public WGPUBindGroupLayoutImpl* BindGroupLayout { get; set; }

            /// <summary>
            /// Gets or sets the render pipeline layout.
            /// </summary>
            public WGPUPipelineLayoutImpl* PipelineLayout { get; set; }

            /// <summary>
            /// Gets or sets the compiled shader module.
            /// </summary>
            public WGPUShaderModuleImpl* ShaderModule { get; set; }

            /// <summary>
            /// Gets or sets the render pipeline.
            /// </summary>
            public WGPURenderPipelineImpl* Pipeline { get; set; }
        }
    }
}
