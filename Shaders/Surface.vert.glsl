#version 430 core
layout(location = 0) in vec3 aPosition;
layout(location = 1) in vec3 aNormal;
layout(std430, binding = 0) readonly buffer Positions { vec4 positions[]; };
layout(std430, binding = 1) readonly buffer VisibleIndices
{
    uint indexCount;
    uint instanceCount;
    uint firstIndex;
    int baseVertex;
    uint baseInstance;
    uint visibleIndices[];
};
layout(std430, binding = 7) readonly buffer Materials { vec4 materials[]; };
uniform mat4 uViewProjection;
uniform mat4 uObject;
uniform int uUseInstances;
uniform vec3 uObjectColor;
uniform float uRampSlope;
uniform int uUseBillboard;
uniform vec3 uCameraRight;
uniform vec3 uCameraUp;
uniform float uGravity;
uniform float uPotentialReferenceHeight;
uniform float uPotentialEnergyRange;

out vec3 vNormal;
out vec3 vColor;
out vec3 vWorldPosition;
out vec3 vLocalPosition;
out vec2 vBillboardCoordinate;
out vec3 vSphereCenter;
out float vSphereRadius;

void main()
{
    vec4 worldPosition;
    vBillboardCoordinate = vec2(0.0);
    vSphereCenter = vec3(0.0);
    vSphereRadius = 0.0;
    if (uUseInstances == 1)
    {
        uint diskId = visibleIndices[gl_InstanceID];
        vec4 disk = positions[diskId];
        vec4 material = materials[diskId];
        if (uUseBillboard == 1)
        {
            vSphereCenter = disk.xyz;
            vSphereRadius = disk.w;
            worldPosition = vec4(
                disk.xyz + disk.w * (uCameraRight * aPosition.x + uCameraUp * aPosition.y),
                1.0);
            float potentialEnergy = material.x * uGravity
                * max(disk.y - uPotentialReferenceHeight, 0.0);
            float energy = clamp(potentialEnergy / uPotentialEnergyRange, 0.0, 1.0);
            vec3 lowEnergyColor = vec3(0.95, 0.12, 0.06);
            vec3 middleEnergyColor = vec3(1.0, 0.82, 0.08);
            vec3 highEnergyColor = vec3(0.05, 0.72, 1.0);
            vColor = energy < 0.5
                ? mix(lowEnergyColor, middleEnergyColor, energy * 2.0)
                : mix(middleEnergyColor, highEnergyColor, (energy - 0.5) * 2.0);
            vNormal = normalize(cross(uCameraRight, uCameraUp));
            vBillboardCoordinate = aPosition.xy;
        }
        else
        {
            vec3 rampNormal = normalize(vec3(-uRampSlope, 1.0, 0.0));
            vec3 rampTangent = normalize(vec3(1.0, uRampSlope, 0.0));
            vec3 rampSide = normalize(cross(rampTangent, rampNormal));
            vec3 localPosition = rampTangent * aPosition.x
                + rampNormal * aPosition.y
                + rampSide * aPosition.z;
            worldPosition = vec4(disk.xyz + localPosition * disk.w, 1.0);
            vColor = vec3(1.0);
            vNormal = normalize(rampTangent * aNormal.x
                + rampNormal * aNormal.y
                + rampSide * aNormal.z);
        }
    }
    else
    {
        worldPosition = uObject * vec4(aPosition, 1.0);
        vColor = uObjectColor;
        vNormal = normalize(mat3(uObject) * aNormal);
    }

    vWorldPosition = worldPosition.xyz;
    vLocalPosition = aPosition;
    gl_Position = uViewProjection * worldPosition;
}
