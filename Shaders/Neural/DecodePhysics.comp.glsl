#version 430 core
layout(local_size_x = 64) in;

layout(std430, binding = 0) readonly buffer ModelParameters { float parameters[]; };
layout(std430, binding = 1) readonly buffer LatentNodes { float latentNodes[]; };
layout(std430, binding = 2) writeonly buffer PhysicsCorrections { float corrections[]; };

uniform uint uNodeCount;
uniform uint uWeightOffset;
uniform uint uBiasOffset;

const uint LATENT_SIZE = 64u;
const uint OUTPUT_SIZE = 9u;

void main()
{
    uint node = gl_GlobalInvocationID.x;
    if (node >= uNodeCount) return;

    for (uint row = 0u; row < OUTPUT_SIZE; row++)
    {
        float value = parameters[uBiasOffset + row];
        uint rowOffset = uWeightOffset + row * LATENT_SIZE;
        for (uint column = 0u; column < LATENT_SIZE; column++)
        {
            value += parameters[rowOffset + column]
                * latentNodes[node * LATENT_SIZE + column];
        }
        corrections[node * OUTPUT_SIZE + row] = value;
    }
}
