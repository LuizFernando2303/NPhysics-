#version 430 core
layout(local_size_x = 64) in;

layout(std430, binding = 0) readonly buffer ModelParameters { float parameters[]; };
layout(std430, binding = 1) readonly buffer ProbeInput { float inputValues[]; };
layout(std430, binding = 2) writeonly buffer ProbeOutput { float outputValues[]; };

uniform uint uInputSize;
uniform uint uOutputSize;
uniform uint uWeightOffset;
uniform uint uBiasOffset;

void main()
{
    uint row = gl_GlobalInvocationID.x;
    if (row >= uOutputSize)
    {
        return;
    }

    float value = parameters[uBiasOffset + row];
    uint rowOffset = uWeightOffset + row * uInputSize;
    for (uint column = 0u; column < uInputSize; column++)
    {
        value += parameters[rowOffset + column] * inputValues[column];
    }
    outputValues[row] = value;
}
