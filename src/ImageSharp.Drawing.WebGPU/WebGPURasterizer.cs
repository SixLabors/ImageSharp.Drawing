// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Runtime.CompilerServices;
using Silk.NET.WebGPU;
using SixLabors.ImageSharp.Drawing.Shapes.Rasterization;
using WgpuBuffer = Silk.NET.WebGPU.Buffer;

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// Owns WebGPU coverage rasterization resources and converts vector paths into reusable
/// coverage textures using a stencil-and-cover render pass.
/// </summary>
internal sealed unsafe class WebGPURasterizer
{
    private const uint CoverageCoverVertexCount = 3;
    private const uint CoverageSampleCount = 4;

    private readonly WebGPU webGPU;

    private PipelineLayout* coveragePipelineLayout;
    private RenderPipeline* coverageStencilEvenOddPipeline;
    private RenderPipeline* coverageStencilNonZeroIncrementPipeline;
    private RenderPipeline* coverageStencilNonZeroDecrementPipeline;
    private RenderPipeline* coverageCoverPipeline;
    private Texture* coverageScratchMultisampleTexture;
    private TextureView* coverageScratchMultisampleView;
    private Texture* coverageScratchStencilTexture;
    private TextureView* coverageScratchStencilView;
    private int coverageScratchWidth;
    private int coverageScratchHeight;
    private WgpuBuffer* coverageScratchVertexBuffer;
    private ulong coverageScratchVertexCapacityBytes;

    public WebGPURasterizer(WebGPU webGPU) => this.webGPU = webGPU;

    private static ReadOnlySpan<byte> CoverageStencilVertexEntryPoint => "vs_edge\0"u8;

    private static ReadOnlySpan<byte> CoverageStencilFragmentEntryPoint => "fs_stencil\0"u8;

    private static ReadOnlySpan<byte> CoverageCoverVertexEntryPoint => "vs_cover\0"u8;

    private static ReadOnlySpan<byte> CoverageCoverFragmentEntryPoint => "fs_cover\0"u8;

    public bool IsInitialized =>
        this.coveragePipelineLayout is not null &&
        this.coverageStencilEvenOddPipeline is not null &&
        this.coverageStencilNonZeroIncrementPipeline is not null &&
        this.coverageStencilNonZeroDecrementPipeline is not null &&
        this.coverageCoverPipeline is not null;

    public bool Initialize(Device* device)
    {
        if (this.IsInitialized)
        {
            return true;
        }

        return this.TryCreateCoveragePipelineLocked(device);
    }

    public bool TryCreateCoverageTexture(
        IPath path,
        in RasterizerOptions rasterizerOptions,
        Device* device,
        Queue* queue,
        out Texture* coverageTexture,
        out TextureView* coverageView)
    {
        coverageTexture = null;
        coverageView = null;

        if (!this.IsInitialized)
        {
            return false;
        }

        if (!TryBuildCoverageTriangles(
                path,
                rasterizerOptions.Interest.Location,
                rasterizerOptions.Interest.Size,
                rasterizerOptions.SamplingOrigin,
                out CoverageTriangleData coverageTriangleData))
        {
            return false;
        }

        return this.TryRasterizeCoverageTextureLocked(
            in coverageTriangleData,
            in rasterizerOptions,
            device,
            queue,
            out coverageTexture,
            out coverageView);
    }

    public void Release()
    {
        this.ReleaseCoverageScratchResourcesLocked();

        if (this.coverageCoverPipeline is not null)
        {
            this.webGPU.RenderPipelineRelease(this.coverageCoverPipeline);
            this.coverageCoverPipeline = null;
        }

        if (this.coverageStencilNonZeroDecrementPipeline is not null)
        {
            this.webGPU.RenderPipelineRelease(this.coverageStencilNonZeroDecrementPipeline);
            this.coverageStencilNonZeroDecrementPipeline = null;
        }

        if (this.coverageStencilNonZeroIncrementPipeline is not null)
        {
            this.webGPU.RenderPipelineRelease(this.coverageStencilNonZeroIncrementPipeline);
            this.coverageStencilNonZeroIncrementPipeline = null;
        }

        if (this.coverageStencilEvenOddPipeline is not null)
        {
            this.webGPU.RenderPipelineRelease(this.coverageStencilEvenOddPipeline);
            this.coverageStencilEvenOddPipeline = null;
        }

        if (this.coveragePipelineLayout is not null)
        {
            this.webGPU.PipelineLayoutRelease(this.coveragePipelineLayout);
            this.coveragePipelineLayout = null;
        }
    }

    /// <summary>
    /// Creates the render pipeline used for coverage rasterization.
    /// </summary>
    private bool TryCreateCoveragePipelineLocked(Device* device)
    {
        PipelineLayoutDescriptor pipelineLayoutDescriptor = new()
        {
            BindGroupLayoutCount = 0,
            BindGroupLayouts = null
        };

        this.coveragePipelineLayout = this.webGPU.DeviceCreatePipelineLayout(device, in pipelineLayoutDescriptor);
        if (this.coveragePipelineLayout is null)
        {
            return false;
        }

        ShaderModule* shaderModule = null;
        try
        {
            ReadOnlySpan<byte> shaderCode = CoverageRasterizationShader.Code;
            fixed (byte* shaderCodePtr = shaderCode)
            {
                ShaderModuleWGSLDescriptor wgslDescriptor = new()
                {
                    Chain = new ChainedStruct
                    {
                        SType = SType.ShaderModuleWgslDescriptor
                    },
                    Code = shaderCodePtr
                };

                ShaderModuleDescriptor shaderDescriptor = new()
                {
                    NextInChain = (ChainedStruct*)&wgslDescriptor
                };

                shaderModule = this.webGPU.DeviceCreateShaderModule(device, in shaderDescriptor);
            }

            if (shaderModule is null)
            {
                return false;
            }

            ReadOnlySpan<byte> stencilVertexEntryPoint = CoverageStencilVertexEntryPoint;
            ReadOnlySpan<byte> stencilFragmentEntryPoint = CoverageStencilFragmentEntryPoint;
            ReadOnlySpan<byte> coverVertexEntryPoint = CoverageCoverVertexEntryPoint;
            ReadOnlySpan<byte> coverFragmentEntryPoint = CoverageCoverFragmentEntryPoint;
            fixed (byte* stencilVertexEntryPointPtr = stencilVertexEntryPoint)
            {
                fixed (byte* stencilFragmentEntryPointPtr = stencilFragmentEntryPoint)
                {
                    VertexAttribute* stencilVertexAttributes = stackalloc VertexAttribute[1];
                    stencilVertexAttributes[0] = new VertexAttribute
                    {
                        Format = VertexFormat.Float32x2,
                        Offset = 0,
                        ShaderLocation = 0
                    };

                    VertexBufferLayout* stencilVertexBuffers = stackalloc VertexBufferLayout[1];
                    stencilVertexBuffers[0] = new VertexBufferLayout
                    {
                        ArrayStride = (ulong)Unsafe.SizeOf<StencilVertex>(),
                        StepMode = VertexStepMode.Vertex,
                        AttributeCount = 1,
                        Attributes = stencilVertexAttributes
                    };

                    VertexState stencilVertexState = new()
                    {
                        Module = shaderModule,
                        EntryPoint = stencilVertexEntryPointPtr,
                        BufferCount = 1,
                        Buffers = stencilVertexBuffers
                    };

                    ColorTargetState* stencilColorTargets = stackalloc ColorTargetState[1];
                    stencilColorTargets[0] = new ColorTargetState
                    {
                        Format = TextureFormat.R8Unorm,
                        Blend = null,
                        WriteMask = ColorWriteMask.None
                    };

                    FragmentState stencilFragmentState = new()
                    {
                        Module = shaderModule,
                        EntryPoint = stencilFragmentEntryPointPtr,
                        TargetCount = 1,
                        Targets = stencilColorTargets
                    };

                    PrimitiveState primitiveState = new()
                    {
                        Topology = PrimitiveTopology.TriangleList,
                        StripIndexFormat = IndexFormat.Undefined,
                        FrontFace = FrontFace.Ccw,
                        CullMode = CullMode.None
                    };

                    MultisampleState multisampleState = new()
                    {
                        Count = CoverageSampleCount,
                        Mask = uint.MaxValue,
                        AlphaToCoverageEnabled = false
                    };

                    StencilFaceState evenOddStencilFace = new()
                    {
                        Compare = CompareFunction.Always,
                        FailOp = StencilOperation.Keep,
                        DepthFailOp = StencilOperation.Keep,
                        PassOp = StencilOperation.Invert
                    };

                    DepthStencilState evenOddDepthStencilState = new()
                    {
                        Format = TextureFormat.Depth24PlusStencil8,
                        DepthWriteEnabled = false,
                        DepthCompare = CompareFunction.Always,
                        StencilFront = evenOddStencilFace,
                        StencilBack = evenOddStencilFace,
                        StencilReadMask = uint.MaxValue,
                        StencilWriteMask = uint.MaxValue,
                        DepthBias = 0,
                        DepthBiasSlopeScale = 0F,
                        DepthBiasClamp = 0F
                    };

                    RenderPipelineDescriptor evenOddPipelineDescriptor = new()
                    {
                        Layout = this.coveragePipelineLayout,
                        Vertex = stencilVertexState,
                        Primitive = primitiveState,
                        DepthStencil = &evenOddDepthStencilState,
                        Multisample = multisampleState,
                        Fragment = &stencilFragmentState
                    };

                    this.coverageStencilEvenOddPipeline = this.webGPU.DeviceCreateRenderPipeline(device, in evenOddPipelineDescriptor);
                    if (this.coverageStencilEvenOddPipeline is null)
                    {
                        return false;
                    }

                    StencilFaceState incrementStencilFace = new()
                    {
                        Compare = CompareFunction.Always,
                        FailOp = StencilOperation.Keep,
                        DepthFailOp = StencilOperation.Keep,
                        PassOp = StencilOperation.IncrementWrap
                    };

                    DepthStencilState incrementDepthStencilState = new()
                    {
                        Format = TextureFormat.Depth24PlusStencil8,
                        DepthWriteEnabled = false,
                        DepthCompare = CompareFunction.Always,
                        StencilFront = incrementStencilFace,
                        StencilBack = incrementStencilFace,
                        StencilReadMask = uint.MaxValue,
                        StencilWriteMask = uint.MaxValue,
                        DepthBias = 0,
                        DepthBiasSlopeScale = 0F,
                        DepthBiasClamp = 0F
                    };

                    RenderPipelineDescriptor incrementPipelineDescriptor = new()
                    {
                        Layout = this.coveragePipelineLayout,
                        Vertex = stencilVertexState,
                        Primitive = primitiveState,
                        DepthStencil = &incrementDepthStencilState,
                        Multisample = multisampleState,
                        Fragment = &stencilFragmentState
                    };

                    this.coverageStencilNonZeroIncrementPipeline = this.webGPU.DeviceCreateRenderPipeline(device, in incrementPipelineDescriptor);
                    if (this.coverageStencilNonZeroIncrementPipeline is null)
                    {
                        return false;
                    }

                    StencilFaceState decrementStencilFace = new()
                    {
                        Compare = CompareFunction.Always,
                        FailOp = StencilOperation.Keep,
                        DepthFailOp = StencilOperation.Keep,
                        PassOp = StencilOperation.DecrementWrap
                    };

                    DepthStencilState decrementDepthStencilState = new()
                    {
                        Format = TextureFormat.Depth24PlusStencil8,
                        DepthWriteEnabled = false,
                        DepthCompare = CompareFunction.Always,
                        StencilFront = decrementStencilFace,
                        StencilBack = decrementStencilFace,
                        StencilReadMask = uint.MaxValue,
                        StencilWriteMask = uint.MaxValue,
                        DepthBias = 0,
                        DepthBiasSlopeScale = 0F,
                        DepthBiasClamp = 0F
                    };

                    RenderPipelineDescriptor decrementPipelineDescriptor = new()
                    {
                        Layout = this.coveragePipelineLayout,
                        Vertex = stencilVertexState,
                        Primitive = primitiveState,
                        DepthStencil = &decrementDepthStencilState,
                        Multisample = multisampleState,
                        Fragment = &stencilFragmentState
                    };

                    this.coverageStencilNonZeroDecrementPipeline = this.webGPU.DeviceCreateRenderPipeline(device, in decrementPipelineDescriptor);
                    if (this.coverageStencilNonZeroDecrementPipeline is null)
                    {
                        return false;
                    }
                }
            }

            fixed (byte* coverVertexEntryPointPtr = coverVertexEntryPoint)
            {
                fixed (byte* coverFragmentEntryPointPtr = coverFragmentEntryPoint)
                {
                    VertexState coverVertexState = new()
                    {
                        Module = shaderModule,
                        EntryPoint = coverVertexEntryPointPtr,
                        BufferCount = 0,
                        Buffers = null
                    };

                    ColorTargetState* coverColorTargets = stackalloc ColorTargetState[1];
                    coverColorTargets[0] = new ColorTargetState
                    {
                        Format = TextureFormat.R8Unorm,
                        Blend = null,
                        WriteMask = ColorWriteMask.Red
                    };

                    FragmentState coverFragmentState = new()
                    {
                        Module = shaderModule,
                        EntryPoint = coverFragmentEntryPointPtr,
                        TargetCount = 1,
                        Targets = coverColorTargets
                    };

                    StencilFaceState coverStencilFace = new()
                    {
                        Compare = CompareFunction.NotEqual,
                        FailOp = StencilOperation.Keep,
                        DepthFailOp = StencilOperation.Keep,
                        PassOp = StencilOperation.Keep
                    };

                    DepthStencilState coverDepthStencilState = new()
                    {
                        Format = TextureFormat.Depth24PlusStencil8,
                        DepthWriteEnabled = false,
                        DepthCompare = CompareFunction.Always,
                        StencilFront = coverStencilFace,
                        StencilBack = coverStencilFace,
                        StencilReadMask = uint.MaxValue,
                        StencilWriteMask = 0,
                        DepthBias = 0,
                        DepthBiasSlopeScale = 0F,
                        DepthBiasClamp = 0F
                    };

                    RenderPipelineDescriptor coverPipelineDescriptor = new()
                    {
                        Layout = this.coveragePipelineLayout,
                        Vertex = coverVertexState,
                        Primitive = new PrimitiveState
                        {
                            Topology = PrimitiveTopology.TriangleList,
                            StripIndexFormat = IndexFormat.Undefined,
                            FrontFace = FrontFace.Ccw,
                            CullMode = CullMode.None
                        },
                        DepthStencil = &coverDepthStencilState,
                        Multisample = new MultisampleState
                        {
                            Count = CoverageSampleCount,
                            Mask = uint.MaxValue,
                            AlphaToCoverageEnabled = false
                        },
                        Fragment = &coverFragmentState
                    };

                    this.coverageCoverPipeline = this.webGPU.DeviceCreateRenderPipeline(device, in coverPipelineDescriptor);
                }
            }

            return this.coverageCoverPipeline is not null;
        }
        finally
        {
            if (shaderModule is not null)
            {
                this.webGPU.ShaderModuleRelease(shaderModule);
            }
        }
    }

    private bool TryEnsureCoverageScratchTargetsLocked(
        Device* device,
        int width,
        int height,
        out TextureView* multisampleCoverageView,
        out TextureView* stencilView)
    {
        multisampleCoverageView = null;
        stencilView = null;

        if (this.coverageScratchMultisampleView is not null &&
            this.coverageScratchStencilView is not null &&
            this.coverageScratchWidth == width &&
            this.coverageScratchHeight == height)
        {
            multisampleCoverageView = this.coverageScratchMultisampleView;
            stencilView = this.coverageScratchStencilView;
            return true;
        }

        this.ReleaseTextureViewLocked(this.coverageScratchMultisampleView);
        this.ReleaseTextureLocked(this.coverageScratchMultisampleTexture);
        this.ReleaseTextureViewLocked(this.coverageScratchStencilView);
        this.ReleaseTextureLocked(this.coverageScratchStencilTexture);
        this.coverageScratchMultisampleView = null;
        this.coverageScratchMultisampleTexture = null;
        this.coverageScratchStencilView = null;
        this.coverageScratchStencilTexture = null;
        this.coverageScratchWidth = 0;
        this.coverageScratchHeight = 0;

        TextureDescriptor multisampleCoverageTextureDescriptor = new()
        {
            Usage = TextureUsage.RenderAttachment,
            Dimension = TextureDimension.Dimension2D,
            Size = new Extent3D((uint)width, (uint)height, 1),
            Format = TextureFormat.R8Unorm,
            MipLevelCount = 1,
            SampleCount = CoverageSampleCount
        };

        Texture* createdMultisampleCoverageTexture =
            this.webGPU.DeviceCreateTexture(device, in multisampleCoverageTextureDescriptor);
        if (createdMultisampleCoverageTexture is null)
        {
            return false;
        }

        TextureViewDescriptor coverageViewDescriptor = new()
        {
            Format = TextureFormat.R8Unorm,
            Dimension = TextureViewDimension.Dimension2D,
            BaseMipLevel = 0,
            MipLevelCount = 1,
            BaseArrayLayer = 0,
            ArrayLayerCount = 1,
            Aspect = TextureAspect.All
        };

        TextureView* createdMultisampleCoverageView = this.webGPU.TextureCreateView(createdMultisampleCoverageTexture, in coverageViewDescriptor);
        if (createdMultisampleCoverageView is null)
        {
            this.ReleaseTextureLocked(createdMultisampleCoverageTexture);
            return false;
        }

        TextureDescriptor stencilTextureDescriptor = new()
        {
            Usage = TextureUsage.RenderAttachment,
            Dimension = TextureDimension.Dimension2D,
            Size = new Extent3D((uint)width, (uint)height, 1),
            Format = TextureFormat.Depth24PlusStencil8,
            MipLevelCount = 1,
            SampleCount = CoverageSampleCount
        };

        Texture* createdStencilTexture = this.webGPU.DeviceCreateTexture(device, in stencilTextureDescriptor);
        if (createdStencilTexture is null)
        {
            this.ReleaseTextureViewLocked(createdMultisampleCoverageView);
            this.ReleaseTextureLocked(createdMultisampleCoverageTexture);
            return false;
        }

        TextureViewDescriptor stencilViewDescriptor = new()
        {
            Format = TextureFormat.Depth24PlusStencil8,
            Dimension = TextureViewDimension.Dimension2D,
            BaseMipLevel = 0,
            MipLevelCount = 1,
            BaseArrayLayer = 0,
            ArrayLayerCount = 1,
            Aspect = TextureAspect.All
        };

        TextureView* createdStencilView = this.webGPU.TextureCreateView(createdStencilTexture, in stencilViewDescriptor);
        if (createdStencilView is null)
        {
            this.ReleaseTextureLocked(createdStencilTexture);
            this.ReleaseTextureViewLocked(createdMultisampleCoverageView);
            this.ReleaseTextureLocked(createdMultisampleCoverageTexture);
            return false;
        }

        this.coverageScratchMultisampleTexture = createdMultisampleCoverageTexture;
        this.coverageScratchMultisampleView = createdMultisampleCoverageView;
        this.coverageScratchStencilTexture = createdStencilTexture;
        this.coverageScratchStencilView = createdStencilView;
        this.coverageScratchWidth = width;
        this.coverageScratchHeight = height;

        multisampleCoverageView = createdMultisampleCoverageView;
        stencilView = createdStencilView;
        return true;
    }

    private bool TryEnsureCoverageScratchVertexBufferLocked(Device* device, ulong requiredByteCount)
    {
        if (this.coverageScratchVertexBuffer is not null &&
            this.coverageScratchVertexCapacityBytes >= requiredByteCount)
        {
            return true;
        }

        this.ReleaseBufferLocked(this.coverageScratchVertexBuffer);
        this.coverageScratchVertexBuffer = null;
        this.coverageScratchVertexCapacityBytes = 0;

        BufferDescriptor vertexBufferDescriptor = new()
        {
            Usage = BufferUsage.Vertex | BufferUsage.CopyDst,
            Size = requiredByteCount
        };

        WgpuBuffer* createdVertexBuffer = this.webGPU.DeviceCreateBuffer(device, in vertexBufferDescriptor);
        if (createdVertexBuffer is null)
        {
            return false;
        }

        this.coverageScratchVertexBuffer = createdVertexBuffer;
        this.coverageScratchVertexCapacityBytes = requiredByteCount;
        return true;
    }

    /// <summary>
    /// Rasterizes edge triangles through a stencil-and-cover pass into an <c>R8Unorm</c> texture.
    /// </summary>
    private bool TryRasterizeCoverageTextureLocked(
        in CoverageTriangleData coverageTriangleData,
        in RasterizerOptions rasterizerOptions,
        Device* device,
        Queue* queue,
        out Texture* coverageTexture,
        out TextureView* coverageView)
    {
        coverageTexture = null;
        coverageView = null;

        Texture* createdCoverageTexture = null;
        TextureView* createdCoverageView = null;
        CommandEncoder* commandEncoder = null;
        RenderPassEncoder* passEncoder = null;
        CommandBuffer* commandBuffer = null;
        bool success = false;
        try
        {
            if (!this.TryEnsureCoverageScratchTargetsLocked(
                    device,
                    rasterizerOptions.Interest.Width,
                    rasterizerOptions.Interest.Height,
                    out TextureView* multisampleCoverageView,
                    out TextureView* stencilView))
            {
                return false;
            }

            TextureDescriptor coverageTextureDescriptor = new()
            {
                Usage = TextureUsage.RenderAttachment | TextureUsage.TextureBinding | TextureUsage.CopySrc,
                Dimension = TextureDimension.Dimension2D,
                Size = new Extent3D((uint)rasterizerOptions.Interest.Width, (uint)rasterizerOptions.Interest.Height, 1),
                Format = TextureFormat.R8Unorm,
                MipLevelCount = 1,
                SampleCount = 1
            };

            createdCoverageTexture = this.webGPU.DeviceCreateTexture(device, in coverageTextureDescriptor);
            if (createdCoverageTexture is null)
            {
                return false;
            }

            TextureViewDescriptor coverageViewDescriptor = new()
            {
                Format = TextureFormat.R8Unorm,
                Dimension = TextureViewDimension.Dimension2D,
                BaseMipLevel = 0,
                MipLevelCount = 1,
                BaseArrayLayer = 0,
                ArrayLayerCount = 1,
                Aspect = TextureAspect.All
            };

            createdCoverageView = this.webGPU.TextureCreateView(createdCoverageTexture, in coverageViewDescriptor);
            if (createdCoverageView is null)
            {
                return false;
            }

            ulong vertexByteCount = checked(coverageTriangleData.TotalVertexCount * (ulong)Unsafe.SizeOf<StencilVertex>());
            if (!this.TryEnsureCoverageScratchVertexBufferLocked(device, vertexByteCount) || this.coverageScratchVertexBuffer is null)
            {
                return false;
            }

            fixed (StencilVertex* verticesPtr = coverageTriangleData.Vertices)
            {
                this.webGPU.QueueWriteBuffer(queue, this.coverageScratchVertexBuffer, 0, verticesPtr, (nuint)vertexByteCount);
            }

            CommandEncoderDescriptor commandEncoderDescriptor = default;
            commandEncoder = this.webGPU.DeviceCreateCommandEncoder(device, in commandEncoderDescriptor);
            if (commandEncoder is null)
            {
                return false;
            }

            RenderPassColorAttachment colorAttachment = new()
            {
                View = multisampleCoverageView,
                ResolveTarget = createdCoverageView,
                LoadOp = LoadOp.Clear,
                StoreOp = StoreOp.Discard,
                ClearValue = default
            };

            RenderPassDepthStencilAttachment depthStencilAttachment = new()
            {
                View = stencilView,
                DepthLoadOp = LoadOp.Clear,
                DepthStoreOp = StoreOp.Discard,
                DepthClearValue = 1F,
                DepthReadOnly = false,
                StencilLoadOp = LoadOp.Clear,
                StencilStoreOp = StoreOp.Discard,
                StencilClearValue = 0,
                StencilReadOnly = false
            };

            RenderPassDescriptor renderPassDescriptor = new()
            {
                ColorAttachmentCount = 1,
                ColorAttachments = &colorAttachment,
                DepthStencilAttachment = &depthStencilAttachment
            };

            passEncoder = this.webGPU.CommandEncoderBeginRenderPass(commandEncoder, in renderPassDescriptor);
            if (passEncoder is null)
            {
                return false;
            }

            this.webGPU.RenderPassEncoderSetStencilReference(passEncoder, 0);
            this.webGPU.RenderPassEncoderSetVertexBuffer(passEncoder, 0, this.coverageScratchVertexBuffer, 0, vertexByteCount);
            if (rasterizerOptions.IntersectionRule == IntersectionRule.EvenOdd)
            {
                this.webGPU.RenderPassEncoderSetPipeline(passEncoder, this.coverageStencilEvenOddPipeline);
                this.webGPU.RenderPassEncoderDraw(passEncoder, coverageTriangleData.TotalVertexCount, 1, 0, 0);
            }
            else
            {
                if (coverageTriangleData.IncrementVertexCount > 0)
                {
                    this.webGPU.RenderPassEncoderSetPipeline(passEncoder, this.coverageStencilNonZeroIncrementPipeline);
                    this.webGPU.RenderPassEncoderDraw(passEncoder, coverageTriangleData.IncrementVertexCount, 1, 0, 0);
                }

                if (coverageTriangleData.DecrementVertexCount > 0)
                {
                    this.webGPU.RenderPassEncoderSetPipeline(passEncoder, this.coverageStencilNonZeroDecrementPipeline);
                    this.webGPU.RenderPassEncoderDraw(
                        passEncoder,
                        coverageTriangleData.DecrementVertexCount,
                        1,
                        coverageTriangleData.IncrementVertexCount,
                        0);
                }
            }

            this.webGPU.RenderPassEncoderSetStencilReference(passEncoder, 0);
            this.webGPU.RenderPassEncoderSetPipeline(passEncoder, this.coverageCoverPipeline);
            this.webGPU.RenderPassEncoderDraw(passEncoder, CoverageCoverVertexCount, 1, 0, 0);

            this.webGPU.RenderPassEncoderEnd(passEncoder);
            this.webGPU.RenderPassEncoderRelease(passEncoder);
            passEncoder = null;

            CommandBufferDescriptor commandBufferDescriptor = default;
            commandBuffer = this.webGPU.CommandEncoderFinish(commandEncoder, in commandBufferDescriptor);
            if (commandBuffer is null)
            {
                return false;
            }

            this.webGPU.QueueSubmit(queue, 1, ref commandBuffer);

            this.webGPU.CommandBufferRelease(commandBuffer);
            commandBuffer = null;
            coverageTexture = createdCoverageTexture;
            coverageView = createdCoverageView;
            createdCoverageTexture = null;
            createdCoverageView = null;
            success = true;
            return true;
        }
        finally
        {
            if (passEncoder is not null)
            {
                this.webGPU.RenderPassEncoderRelease(passEncoder);
            }

            if (commandBuffer is not null)
            {
                this.webGPU.CommandBufferRelease(commandBuffer);
            }

            if (commandEncoder is not null)
            {
                this.webGPU.CommandEncoderRelease(commandEncoder);
            }

            if (!success)
            {
                this.ReleaseTextureViewLocked(createdCoverageView);
                this.ReleaseTextureLocked(createdCoverageTexture);
            }
        }
    }

    /// <summary>
    /// Flattens a path into local-interest coordinates and converts each non-horizontal edge
    /// into a trapezoid (two triangles) anchored at a left-side sentinel X.
    /// </summary>
    private static bool TryBuildCoverageTriangles(
        IPath path,
        Point interestLocation,
        Size interestSize,
        RasterizerSamplingOrigin samplingOrigin,
        out CoverageTriangleData coverageTriangleData)
    {
        coverageTriangleData = default;
        if (interestSize.Width <= 0 || interestSize.Height <= 0)
        {
            return false;
        }

        float sampleShift = samplingOrigin == RasterizerSamplingOrigin.PixelBoundary ? 0.5F : 0F;
        float offsetX = sampleShift - interestLocation.X;
        float offsetY = sampleShift - interestLocation.Y;

        List<CoverageSegment> segments = [];
        float minX = float.PositiveInfinity;

        foreach (ISimplePath simplePath in path.Flatten())
        {
            ReadOnlySpan<PointF> points = simplePath.Points.Span;
            if (points.Length < 2)
            {
                continue;
            }

            for (int i = 1; i < points.Length; i++)
            {
                AddCoverageSegment(points[i - 1], points[i], offsetX, offsetY, segments, ref minX);
            }

            if (simplePath.IsClosed)
            {
                AddCoverageSegment(points[^1], points[0], offsetX, offsetY, segments, ref minX);
            }
        }

        if (segments.Count == 0 || !float.IsFinite(minX))
        {
            return false;
        }

        int incrementEdgeCount = 0;
        int decrementEdgeCount = 0;
        foreach (CoverageSegment segment in segments)
        {
            if (segment.FromY == segment.ToY)
            {
                continue;
            }

            if (segment.ToY > segment.FromY)
            {
                incrementEdgeCount++;
            }
            else
            {
                decrementEdgeCount++;
            }
        }

        int totalEdgeCount = incrementEdgeCount + decrementEdgeCount;
        if (totalEdgeCount == 0)
        {
            return false;
        }

        float sentinelX = minX - 1F;
        float widthScale = 2F / interestSize.Width;
        float heightScale = 2F / interestSize.Height;
        int incrementVertexCount = checked(incrementEdgeCount * 6);
        int decrementVertexCount = checked(decrementEdgeCount * 6);
        StencilVertex[] vertices = new StencilVertex[checked(incrementVertexCount + decrementVertexCount)];

        int vertexIndex = 0;
        foreach (CoverageSegment segment in segments)
        {
            if (segment.ToY <= segment.FromY)
            {
                continue;
            }

            AppendCoverageEdgeQuad(
                vertices,
                ref vertexIndex,
                sentinelX,
                segment.FromX,
                segment.FromY,
                segment.ToX,
                segment.ToY,
                widthScale,
                heightScale);
        }

        int decrementStartIndex = incrementVertexCount;
        vertexIndex = decrementStartIndex;
        foreach (CoverageSegment segment in segments)
        {
            if (segment.ToY >= segment.FromY)
            {
                continue;
            }

            AppendCoverageEdgeQuad(
                vertices,
                ref vertexIndex,
                sentinelX,
                segment.FromX,
                segment.FromY,
                segment.ToX,
                segment.ToY,
                widthScale,
                heightScale);
        }

        coverageTriangleData = new CoverageTriangleData(
            vertices,
            (uint)incrementVertexCount,
            (uint)decrementVertexCount);
        return true;
    }

    private static void AddCoverageSegment(
        PointF from,
        PointF to,
        float offsetX,
        float offsetY,
        List<CoverageSegment> destination,
        ref float minX)
    {
        if (from.Equals(to))
        {
            return;
        }

        if (!float.IsFinite(from.X) ||
            !float.IsFinite(from.Y) ||
            !float.IsFinite(to.X) ||
            !float.IsFinite(to.Y))
        {
            return;
        }

        float fromX = from.X + offsetX;
        float fromY = from.Y + offsetY;
        float toX = to.X + offsetX;
        float toY = to.Y + offsetY;

        destination.Add(new CoverageSegment(fromX, fromY, toX, toY));
        minX = MathF.Min(minX, MathF.Min(fromX, toX));
    }

    private static void AppendCoverageEdgeQuad(
        StencilVertex[] destination,
        ref int destinationIndex,
        float sentinelX,
        float fromX,
        float fromY,
        float toX,
        float toY,
        float widthScale,
        float heightScale)
    {
        StencilVertex a = ToStencilVertex(sentinelX, fromY, widthScale, heightScale);
        StencilVertex b = ToStencilVertex(fromX, fromY, widthScale, heightScale);
        StencilVertex c = ToStencilVertex(toX, toY, widthScale, heightScale);
        StencilVertex d = ToStencilVertex(sentinelX, toY, widthScale, heightScale);

        destination[destinationIndex++] = a;
        destination[destinationIndex++] = b;
        destination[destinationIndex++] = c;
        destination[destinationIndex++] = a;
        destination[destinationIndex++] = c;
        destination[destinationIndex++] = d;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static StencilVertex ToStencilVertex(float x, float y, float widthScale, float heightScale)
        => new()
        {
            X = (x * widthScale) - 1F,
            Y = 1F - (y * heightScale)
        };

    private void ReleaseCoverageScratchResourcesLocked()
    {
        this.ReleaseBufferLocked(this.coverageScratchVertexBuffer);
        this.ReleaseTextureViewLocked(this.coverageScratchStencilView);
        this.ReleaseTextureLocked(this.coverageScratchStencilTexture);
        this.ReleaseTextureViewLocked(this.coverageScratchMultisampleView);
        this.ReleaseTextureLocked(this.coverageScratchMultisampleTexture);
        this.coverageScratchVertexBuffer = null;
        this.coverageScratchVertexCapacityBytes = 0;
        this.coverageScratchStencilView = null;
        this.coverageScratchStencilTexture = null;
        this.coverageScratchMultisampleView = null;
        this.coverageScratchMultisampleTexture = null;
        this.coverageScratchWidth = 0;
        this.coverageScratchHeight = 0;
    }

    private void ReleaseTextureViewLocked(TextureView* textureView)
    {
        if (textureView is null)
        {
            return;
        }

        this.webGPU.TextureViewRelease(textureView);
    }

    private void ReleaseTextureLocked(Texture* texture)
    {
        if (texture is null)
        {
            return;
        }

        this.webGPU.TextureRelease(texture);
    }

    private void ReleaseBufferLocked(WgpuBuffer* buffer)
    {
        if (buffer is null)
        {
            return;
        }

        this.webGPU.BufferRelease(buffer);
    }

    private struct StencilVertex
    {
        public float X;
        public float Y;
    }

    private readonly struct CoverageSegment
    {
        public CoverageSegment(float fromX, float fromY, float toX, float toY)
        {
            this.FromX = fromX;
            this.FromY = fromY;
            this.ToX = toX;
            this.ToY = toY;
        }

        public float FromX { get; }

        public float FromY { get; }

        public float ToX { get; }

        public float ToY { get; }
    }

    private readonly struct CoverageTriangleData
    {
        public CoverageTriangleData(StencilVertex[] vertices, uint incrementVertexCount, uint decrementVertexCount)
        {
            this.Vertices = vertices;
            this.IncrementVertexCount = incrementVertexCount;
            this.DecrementVertexCount = decrementVertexCount;
        }

        public StencilVertex[] Vertices { get; }

        public uint IncrementVertexCount { get; }

        public uint DecrementVertexCount { get; }

        public uint TotalVertexCount => this.IncrementVertexCount + this.DecrementVertexCount;
    }
}
