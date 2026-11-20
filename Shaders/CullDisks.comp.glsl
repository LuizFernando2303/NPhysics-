#version 430 core
layout(local_size_x = 256) in;
layout(std430, binding = 0) readonly buffer Positions { vec4 positions[]; };
layout(std430, binding = 1) buffer DrawCommand
{
    uint indexCount;
    uint instanceCount;
    uint firstIndex;
    int baseVertex;
    uint baseInstance;
    uint visibleIndices[];
};
layout(std430, binding = 2) buffer Metrics { uint metrics[]; };

uniform uint uDiskCount;
uniform mat4 uViewProjection;
uniform bool uMetricsEnabled;

void main()
{
    uint diskId = gl_GlobalInvocationID.x;
    if (diskId >= uDiskCount)
    {
        return;
    }

    vec4 disk = positions[diskId];
    vec4 clip = uViewProjection * vec4(disk.xyz, 1.0);
    float radius = disk.w * 1.5;
    float marginX = radius * length(vec3(uViewProjection[0][0], uViewProjection[1][0], uViewProjection[2][0]));
    float marginY = radius * length(vec3(uViewProjection[0][1], uViewProjection[1][1], uViewProjection[2][1]));
    float marginZ = radius * length(vec3(uViewProjection[0][2], uViewProjection[1][2], uViewProjection[2][2]));

    if (clip.w <= 0.0
        || clip.x < -clip.w - marginX || clip.x > clip.w + marginX
        || clip.y < -clip.w - marginY || clip.y > clip.w + marginY
        || clip.z < -clip.w - marginZ || clip.z > clip.w + marginZ)
    {
        return;
    }

    uint visibleIndex = atomicAdd(instanceCount, 1u);
    visibleIndices[visibleIndex] = diskId;
    if (uMetricsEnabled)
    {
        atomicAdd(metrics[7], 1u);
    }
}
