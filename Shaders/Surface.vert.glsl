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
layout(std430, binding = 8) readonly buffer Velocities { vec4 velocities[]; };
layout(std430, binding = 9) readonly buffer SpinStates { vec4 spinStates[]; };
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
uniform int uVisualizationMode;
uniform float uVelocityRange;
uniform float uAngularVelocityRange;

out vec3 vNormal;
out vec3 vColor;
out vec3 vWorldPosition;
out vec3 vLocalPosition;
out vec2 vBillboardCoordinate;
out vec3 vSphereCenter;
out float vSphereRadius;

vec3 scientificColor(float value)
{
    float t = clamp(value, 0.0, 1.0);
    vec3 low = vec3(0.10, 0.28, 0.62);
    vec3 middle = vec3(0.20, 0.82, 0.72);
    vec3 high = vec3(1.0, 0.78, 0.12);
    vec3 hot = vec3(0.95, 0.12, 0.06);
    if (t < 0.5)
    {
        return mix(low, middle, t * 2.0);
    }
    if (t < 0.75)
    {
        return mix(middle, high, (t - 0.5) * 4.0);
    }
    return mix(high, hot, (t - 0.75) * 4.0);
}

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
            if (uVisualizationMode == 0)
            {
                float potentialEnergy = material.x * uGravity
                    * max(disk.y - uPotentialReferenceHeight, 0.0);
                vColor = scientificColor(potentialEnergy / uPotentialEnergyRange);
            }
            else if (uVisualizationMode == 1)
            {
                vColor = scientificColor(length(velocities[diskId].xyz) / uVelocityRange);
            }
            else if (uVisualizationMode == 2)
            {
                vec4 angularState = spinStates[diskId * 2u + 1u];
                vColor = scientificColor(length(angularState.xyz) / uAngularVelocityRange);
            }
            else if (uVisualizationMode == 3)
            {
                vColor = scientificColor((material.x - 0.2) / 1.8);
            }
            else
            {
                vColor = scientificColor(material.w);
            }
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
