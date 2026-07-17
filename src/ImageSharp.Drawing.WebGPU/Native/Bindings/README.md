# WebGPU native bindings

The checked-in bindings are generated from the `webgpu.h` and `wgpu.h` headers shipped with
the configured wgpu-native release. They are internal implementation details of the WebGPU backend.

Regenerate them from the repository root with:

```powershell
dotnet msbuild src/ImageSharp.Drawing.WebGPU/ImageSharp.Drawing.WebGPU.csproj `
    -t:GenerateWebGPUBindings `
    -p:Configuration=Release
```

Download the matching native release assets and refresh the headers with:

```powershell
dotnet msbuild src/ImageSharp.Drawing.WebGPU/ImageSharp.Drawing.WebGPU.csproj `
    -t:DownloadWebGPUNative `
    -p:Configuration=Release
```

The target restores the project-scoped ClangSharp tool at its pinned version and writes
`Generated/WebGPUNative.g.cs`. Normal builds compile the checked-in output without restoring or
running ClangSharp.

Generated imports use wgpu-native's upstream `wgpu_native` library name. Platform-specific native
assets retain their upstream filenames so the runtime's standard native-library resolution selects
`wgpu_native.dll`, `libwgpu_native.so`, or `libwgpu_native.dylib` as appropriate.

The downloader verifies the configured release's published SHA-256 digest before copying each asset
into `runtimes/<rid>/native`. Its archive cache lives under `obj/WebGPUNative` and is not packaged.

Windows runtime directories also contain `dxcompiler.dll` and `dxil.dll` from the configured
Microsoft DirectX Shader Compiler package. The Windows backend explicitly selects DX12 and this
packaged compiler because wgpu-native's legacy compiler makes the staged fine shader prohibitively
slow to compile. Packaging both DLLs keeps compiler selection independent of the developer
machine's SDK.

The iOS device and simulator assets are static libraries in the same standard runtime layout. The
.NET iOS SDK selects the library matching the application's runtime identifier and links it as a
static native library. A separate `NativeReference` must not be added because that would submit the
same archive to the native linker twice.

On Windows, Visual Studio C++ Build Tools and a Windows SDK are required because libclang uses their
C standard-library headers while parsing the WebGPU headers. The generator script locates the
newest installed toolset and SDK; those paths do not become part of the generated source.

Do not edit the generated file. When changing the wgpu-native version, replace both headers from
the same official release before regenerating so the managed structure layouts and function
signatures remain ABI-compatible with the native library.
