# Drawing Backend Benchmark

A Windows sample based on the original `ImageSharpBenchmark` workload from the `Csharp-Data-Visualization` repo.

https://swharden.com/csdv/platforms/compare/

It renders a randomized line scene repeatedly and lets you compare:

- `CPU` via the default `ImageSharp.Drawing` backend
- `WebGPU` via the offscreen `ImageSharp.Drawing.WebGPU` backend

The sample shows:

- a preview of the last rendered frame
- the most recent render time
- running mean
- standard deviation

## Running

```powershell
dotnet run --project samples/DrawingBackendBenchmark -c Release
```

The WebGPU path renders to an offscreen native texture and reads the final frame back for preview. The reported benchmark time measures scene rendering and flush time only, not the preview readback.
