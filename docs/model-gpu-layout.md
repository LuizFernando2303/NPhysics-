# GPU model parameter layout

The `.nphysmodel` format version 2 is the interchange format for the CPU trainer and the OpenGL inference path. Loading rejects incompatible model/schema versions, dimensions, duplicate or missing layer names, non-finite tensors, and trailing data.

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

The uploader checks `GL_MAX_SHADER_STORAGE_BLOCK_SIZE` before allocating one immutable-for-inference SSBO and binds it at the caller-selected binding point. The current model is about 181 KiB and fits in a single SSBO on OpenGL 4.3 implementations meeting the minimum limit.

## Validation command

`dotnet run -- --gpu-model-check <model.nphysmodel>` creates a temporary OpenGL 4.3 context, loads and uploads the model, runs a nonzero-input `node_encoder` linear probe, and compares its result against the C# calculation. This validates model decoding, packed offsets, SSBO upload, shader indexing, and CPU/GPU numerical agreement for a linear layer only. It does not yet execute the graph message-passing pipeline or change the physics backend.
