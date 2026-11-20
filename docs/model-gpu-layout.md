# GPU model parameter layout

The `.nphysmodel` format version 2 is the interchange format for the experimental CPU training path and the GPU weight-upload probe. It is not yet a complete runtime inference format. Loading rejects incompatible model/schema versions, dimensions, duplicate or missing layer names, non-finite tensors or normalization values, and trailing data.

## Layer manifest

The layer order in the packed SSBO is fixed:

| Layer | Input | Output |
|---|---:|---:|
| `node_encoder` | 48 | 64 |
| `edge_encoder` | 20 | 64 |
| `message_0` | 192 | 64 |
| `message_1` | 192 | 64 |
| `update_0` | 128 | 64 |
| `update_1` | 128 | 64 |
| `decoder` | 64 | 9 |

Each layer contributes row-major weights followed immediately by its bias vector. `GpuModelWeights` provides each layer's weight and bias offsets in float elements; shaders index those offsets in one `float[]` SSBO. A linear layer computes:

```text
y[row] = bias[biasOffset + row]
       + sum(weight[weightOffset + row * inputSize + column] * x[column])
```

The uploader checks `GL_MAX_SHADER_STORAGE_BLOCK_SIZE` before allocating one SSBO with `BufferUsageHint.StaticDraw` and binds it at the caller-selected binding point. A current model is about 181 KiB and fits in a single SSBO on OpenGL 4.3 implementations meeting the minimum limit.

## Validation command

`dotnet run -- --gpu-model-check <model.nphysmodel>` creates a temporary OpenGL 4.3 context, loads and uploads the model, runs a nonzero-input `node_encoder` matrix-vector probe, and compares its result against the C# calculation (before SiLU). This validates model decoding, packed offsets, SSBO upload, shader indexing, and CPU/GPU numerical agreement for one linear operation only. It does not yet execute the graph message-passing pipeline or change the physics backend.

## Current limitations

- The current `--train` command is an experimental one-pass loader/training path, not a production training loop.
- Backpropagation currently updates the decoder only; encoder/message/update layer gradients are not propagated.
- Normalization arrays are stored and validated, but the current CLI writes zero means and unit standard deviations rather than computing them from the dataset.
- There is no GPU forward implementation for SiLU, edge encoding, aggregation, the two message/update steps, or decoding.
- No neural backend is connected to `GpuPhysics`; the existing teacher shader remains the only simulation backend.
