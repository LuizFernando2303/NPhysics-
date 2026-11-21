# Benchmarks and reproducibility

## Purpose

Use benchmarks only to compare the same configuration on the same machine and driver.
The project does not fabricate or publish performance values.

## Minimum benchmark configuration

Run one process at a time and keep the window visible:

```powershell
dotnet run --project SimulationApp -c Release -- --count 4096 --seed 2026 --capture false --save-experiment benchmark.json --view scene
```

Repeat with larger counts when device memory and runtime allow:

```powershell
dotnet run --project SimulationApp -c Release -- --experiment benchmark.json --count 10000 --view scene
```

The JSON configuration records count and seed. The JSONL session trace records:

- GPU vendor, renderer, and driver version
- solver backend and model path
- process architecture and operating system
- .NET runtime version
- fixed timestep and simulation settings
- CPU submission averages
- GPU grid, simulation, culling, and drawing timings
- dropped simulation time
- candidate, contact, and invalid-state counters where enabled

## Reported measurements

Report at least:

- particle count
- GPU model and driver version
- average frame time
- average physics submission time
- last grid-clear, grid-build, simulation, culling, and drawing GPU time
- physics steps per real second
- simulated time versus real time
- dropped time
- body and contact counts
- capture enabled/disabled

Do not report GPU tests or runtime shader checks as passed unless they were executed
on an OpenGL 4.3-compatible context.

## CI guarantees

The GitHub Actions workflow on `windows-latest` proves that all solution projects
restore and compile in Debug and Release, that the CPU-only xUnit suite passes,
and that `--self-test` reaches the one-shot smoke-test exit. It does not create
an OpenGL 4.3 context and therefore does not prove shader compilation, GPU
dispatch correctness, or live frame rendering. GPU/runtime validation requires a
physical or GPU-enabled Windows runner and must be reported separately.
