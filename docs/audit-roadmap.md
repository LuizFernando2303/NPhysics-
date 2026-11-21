# Initial technical audit and roadmap

## Audit scope

The audit covered the C# entry points and subsystem classes, GLSL compute/graphics shaders,
project configuration, dataset and learning components, diagnostics, and technical
documentation. The main project builds, and the built-in smoke tests pass.

## Architecture

- `PhysicsSimulation` owns the OpenTK window, fixed-loop integration, rendering, HUD, trace,
  and frame capture lifecycle.
- `GpuPhysics` owns simulation buffers, compute programs, grid buffers, obstacle data,
  rendering indirect commands, dataset capture, neural backend state, and diagnostics.
- `Simulate.comp.glsl` performs semi-implicit Euler integration, ramp/obstacle/sphere
  contact handling, position correction, normal impulses, and tangential friction in GPU
  single-dispatch invocations.
- `DatasetGenerator` performs a compute capture and asynchronous GPU readback into ring
  staging buffers.
- `GraphNetwork`, `GraphTrainer`, model serialization, and `GpuGraphInference` provide CPU
  training and an experimental GPU GNN inference path.
- `SceneRenderer` performs rendering; it is not the physical solver.

## Verified implemented features

- OpenGL 4.3 compute and graphics pipeline.
- Fixed `1/120 s` physics timestep with a bounded accumulator added by this modernization.
- Explicit dropped-time accounting in the simulation clock.
- Ping-pong position, velocity, and spin buffers.
- Spatial hash grid with linked bucket lists.
- Ramp, sphere-sphere, and obstacle collision responses.
- Friction, restitution, aerodynamic drag, gravity, and solid-sphere rotational inertia.
- GPU indirect culling and indirect sphere drawing.
- Asynchronous dataset and diagnostics readback using ring buffers and fences.
- JSONL simulation tracing and PNG frame capture.
- Dataset header/block validation and finite-value checks.
- CPU GNN forward/backward smoke validation and model serialization.
- Complete GNN compute shaders for encoder, message passing, and decoder.

## Findings

### Critical

| Finding | Evidence | Status |
|---|---|---|
| Collision contacts are resolved from a single parallel pass | `Simulate.comp.glsl` updates only the invoking body's output state and has no iterative global solve. | Implemented limitation remains documented |
| Spatial hash has only hash-bucket conflict metrics and no deterministic candidate-capacity validation | `BuildGrid.comp.glsl` hashes 3D cells into 4,194,304 buckets and uses atomic linked-list insertion. | Hash collisions are handled, but candidate distributions are not benchmarked |
| GPU readbacks can discard data instead of applying backpressure | Dataset ring scheduling returns `false` when all slots are occupied. | Behavior is explicit; no adaptive capture backpressure |

### High

| Finding | Evidence | Status |
|---|---|---|
| Previous accumulator could execute unlimited catch-up steps | `PhysicsSimulation.OnUpdateFrame` used an unbounded `while`. | Fixed by `SimulationClock` with a configurable 8-step cap |
| No CCD exists | Solver uses discrete collision tests. | Not implemented |
| No warm starting or contact caching | Contact state is not persisted between steps. | Not implemented |
| Neural runtime uses CPU arrays and uploads full state | `NeuralPhysicsBackend` builds node/edge features on CPU and uploads arrays. | Experimental; remains outside the GPU-teacher hot path |
| Teacher/Neural execution is not user-selectable in the main executable | `PhysicsSimulation` accepts a model through `--model` in the main executable and through the model simulator. | Implemented for controlled experiments |
| Detailed atomics are disabled above 4,096 bodies | `GpuDiagnostics.MaximumDiskCountForDetailedCounters`. | Intentionally safe, with GPU timers retained |

### Medium

| Finding | Evidence | Status |
|---|---|---|
| Training lacks cross-trajectory validation/test splitting | `DatasetLoader.Read` streams blocks sequentially without scenario or trajectory-aware splits. | Not implemented |
| Dataset metadata does not include all requested provenance fields | The 512-byte header includes schema, count, seed, capture settings, timestep, constants, and hashes, but not GPU/driver metadata or full feature/target definitions. | Partial |
| Physical validation is limited | Only general smoke tests and GNN finite/gradient checks exist; no conservation, free-fall, or friction reference scenarios. | Partial |
| Hardware/resource capability checks are incomplete | Allocation checks exist for dataset and model buffers, but device workgroup and dispatch limits are not centralized. | Partial |
| UI remains a minimal OpenGL HUD | The application has essential controls and telemetry title data, but no dockable scene/property/console panels. | Partial |

### Low

| Finding | Evidence | Status |
|---|---|---|
| Documentation previously reported a 1,000,000-body default | `SimulationSettings.DefaultDiskCount` is 100,000. | Corrected in technical documentation |
| No separate .NET test project exists | Smoke checks execute from `Program.Main`. | Partial |

## Prioritized roadmap

1. Complete fixed-clock integration and dropped-time telemetry.
2. Add CPU and GPU physical validation scenarios with reference tolerances.
3. Centralize GPU capability detection and resource allocation validation.
4. Add dataset provenance and corruption-report coverage.
5. Add a bounded, documented continuous-collision strategy.
6. Add iterative contact resolution or isolate it behind benchmarked optional solver
   configuration.
7. Connect normalization, trajectory-aware splitting, and validation metrics to training.
8. Complete hardware-independent neural runtime/backend selection and CPU-GPU consistency.
9. Extend the CLI-first workstation with broader dockable panels and editable numeric properties.
10. Keep an automated test project and CI build matrix; add OpenGL runtime coverage on a GPU-equipped runner.
