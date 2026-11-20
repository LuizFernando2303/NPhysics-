#version 430 core
layout(local_size_x = 64) in;

layout(std430, binding = 0) readonly buffer ModelParameters { float parameters[]; };
layout(std430, binding = 1) readonly buffer EdgeFeatures { float edgeFeatures[]; };
layout(std430, binding = 2) readonly buffer EdgeTargets { uint edgeTargets[]; };
layout(std430, binding = 3) readonly buffer EdgeOffsets { uint edgeOffsets[]; };
layout(std430, binding = 4) readonly buffer LatentNodesIn { float latentNodesIn[]; };
layout(std430, binding = 5) writeonly buffer LatentNodesOut { float latentNodesOut[]; };

uniform uint uNodeCount;
uniform uint uEdgeWeightOffset;
uniform uint uEdgeBiasOffset;
uniform uint uMessageWeightOffset;
uniform uint uMessageBiasOffset;
uniform uint uUpdateWeightOffset;
uniform uint uUpdateBiasOffset;

const uint EDGE_INPUT_SIZE = 20u;
const uint LATENT_SIZE = 64u;
const uint MESSAGE_INPUT_SIZE = 192u;
const uint UPDATE_INPUT_SIZE = 128u;

float silu(float value)
{
    return value / (1.0 + exp(-value));
}

void main()
{
    uint node = gl_GlobalInvocationID.x;
    if (node >= uNodeCount) return;

    uint firstEdge = edgeOffsets[node];
    uint pastLastEdge = edgeOffsets[node + 1u];
    uint edgeCount = pastLastEdge - firstEdge;
    float aggregate[LATENT_SIZE];
    for (uint latent = 0u; latent < LATENT_SIZE; latent++)
    {
        aggregate[latent] = 0.0;
    }

    for (uint edge = firstEdge; edge < pastLastEdge; edge++)
    {
        uint target = edgeTargets[edge];
        uint edgeInputOffset = edge * EDGE_INPUT_SIZE;
        float edgeLatent[LATENT_SIZE];
        for (uint row = 0u; row < LATENT_SIZE; row++)
        {
            float value = parameters[uEdgeBiasOffset + row];
            uint rowOffset = uEdgeWeightOffset + row * EDGE_INPUT_SIZE;
            for (uint column = 0u; column < EDGE_INPUT_SIZE; column++)
            {
                value += parameters[rowOffset + column]
                    * edgeFeatures[edgeInputOffset + column];
            }
            edgeLatent[row] = silu(value);
        }

        float message[LATENT_SIZE];
        for (uint row = 0u; row < LATENT_SIZE; row++)
        {
            float value = parameters[uMessageBiasOffset + row];
            uint rowOffset = uMessageWeightOffset + row * MESSAGE_INPUT_SIZE;
            for (uint column = 0u; column < LATENT_SIZE; column++)
            {
                value += parameters[rowOffset + column]
                    * latentNodesIn[node * LATENT_SIZE + column];
                value += parameters[rowOffset + LATENT_SIZE + column]
                    * latentNodesIn[target * LATENT_SIZE + column];
                value += parameters[rowOffset + 2u * LATENT_SIZE + column]
                    * edgeLatent[column];
            }
            message[row] = silu(value);
        }

        float scale = 1.0 / sqrt(float(edgeCount));
        for (uint latent = 0u; latent < LATENT_SIZE; latent++)
        {
            aggregate[latent] += message[latent] * scale;
        }
    }

    for (uint row = 0u; row < LATENT_SIZE; row++)
    {
        float value = parameters[uUpdateBiasOffset + row];
        uint rowOffset = uUpdateWeightOffset + row * UPDATE_INPUT_SIZE;
        for (uint column = 0u; column < LATENT_SIZE; column++)
        {
            value += parameters[rowOffset + column]
                * latentNodesIn[node * LATENT_SIZE + column];
            value += parameters[rowOffset + LATENT_SIZE + column] * aggregate[column];
        }
        latentNodesOut[node * LATENT_SIZE + row] = silu(value);
    }
}
