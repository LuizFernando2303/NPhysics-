#version 430 core
layout(local_size_x = 64) in;

layout(std430, binding = 0) readonly buffer ModelParameters { float parameters[]; };
layout(std430, binding = 1) readonly buffer NodeInputs { float nodeInputs[]; };
layout(std430, binding = 2) writeonly buffer LatentNodes { float latentNodes[]; };

uniform uint uNodeCount;
uniform uint uWeightOffset;
uniform uint uBiasOffset;

const uint NODE_INPUT_SIZE = 48u;
const uint LATENT_SIZE = 64u;

float silu(float value)
{
    return value / (1.0 + exp(-value));
}

void main()
{
    uint node = gl_GlobalInvocationID.x;
    if (node >= uNodeCount) return;

    uint inputOffset = node * NODE_INPUT_SIZE;
    uint outputOffset = node * LATENT_SIZE;
    for (uint row = 0u; row < LATENT_SIZE; row++)
    {
        float value = parameters[uBiasOffset + row];
        uint rowOffset = uWeightOffset + row * NODE_INPUT_SIZE;
        for (uint column = 0u; column < NODE_INPUT_SIZE; column++)
        {
            value += parameters[rowOffset + column] * nodeInputs[inputOffset + column];
        }
        latentNodes[outputOffset + row] = silu(value);
    }
}
