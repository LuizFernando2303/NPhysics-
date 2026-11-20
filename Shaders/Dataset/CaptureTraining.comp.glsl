#version 430 core
layout(local_size_x = 128) in;

layout(std430, binding = 0) readonly buffer PositionsIn { vec4 positionsIn[]; };
layout(std430, binding = 1) readonly buffer VelocitiesIn { vec4 velocitiesIn[]; };
layout(std430, binding = 2) readonly buffer PositionsOut { vec4 positionsOut[]; };
layout(std430, binding = 3) readonly buffer VelocitiesOut { vec4 velocitiesOut[]; };
layout(std430, binding = 4) readonly buffer Grid { int cells[]; };
layout(std430, binding = 5) readonly buffer Links { int links[]; };
layout(std430, binding = 7) readonly buffer Materials { vec4 materials[]; };
layout(std430, binding = 9) readonly buffer SpinStatesIn { vec4 spinStatesIn[]; };
layout(std430, binding = 10) readonly buffer SpinStatesOut { vec4 spinStatesOut[]; };
layout(std430, binding = 11) writeonly buffer CaptureData { float capture[]; };
layout(std430, binding = 12) buffer CaptureMetadata { uint metadata[]; };

uniform uint uDiskCount;
uniform uint uMaxNeighbors;
uniform float uDeltaTime;

const float GRAVITY = @GRAVITY@;
const float AIR_DENSITY = @AIR_DENSITY@;
const float AIR_DRAG_COEFFICIENT = @AIR_DRAG_COEFFICIENT@;
const float SPHERE_INERTIA_FACTOR = @SPHERE_INERTIA_FACTOR@;
const float RAMP_SLOPE = @RAMP_SLOPE@;
const float RAMP_HALF_WIDTH = @RAMP_HALF_WIDTH@;
const float CELL_SIZE = @CELL_SIZE@;
const uint GRID_MASK = @GRID_MASK@u;

uint cellIndex(ivec3 cell)
{
    uint hash = uint(cell.x) * 0x8da6b343u
        ^ uint(cell.y) * 0xd8163841u
        ^ uint(cell.z) * 0xcb1ab31fu;
    hash ^= hash >> 16;
    hash *= 0x7feb352du;
    hash ^= hash >> 15;
    hash *= 0x846ca68bu;
    hash ^= hash >> 16;
    return hash & GRID_MASK;
}

float groundHeight(float x, float z)
{
    return -RAMP_SLOPE * x
        + 0.48 * sin(x * 0.055)
        + 0.16 * sin(x * 0.19 + 1.4)
        + 0.065 * sin(x * 0.47)
        + 0.14 * sin(z * 0.55 + x * 0.035)
        + 0.045 * sin(z * 1.25 - x * 0.11)
        + 0.006 * z * z;
}

void writeVec3(uint baseIndex, vec3 value)
{
    capture[baseIndex] = value.x;
    capture[baseIndex + 1u] = value.y;
    capture[baseIndex + 2u] = value.z;
}

void main()
{
    uint id = gl_GlobalInvocationID.x;
    if (id >= uDiskCount)
    {
        return;
    }

    vec4 diskBefore = positionsIn[id];
    vec4 diskAfter = positionsOut[id];
    vec4 material = materials[id];
    vec3 velocityBefore = velocitiesIn[id].xyz;
    vec3 velocityAfter = velocitiesOut[id].xyz;
    vec4 quaternionBefore = spinStatesIn[id * 2u];
    vec3 angularBefore = spinStatesIn[id * 2u + 1u].xyz;
    vec3 angularAfter = spinStatesOut[id * 2u + 1u].xyz;
    float radius = diskBefore.w;
    float inverseMass = 1.0 / material.x;
    float inertia = 1.0 / (inverseMass / (SPHERE_INERTIA_FACTOR * radius * radius));

    vec3 freeVelocity = velocityBefore + vec3(0.0, -GRAVITY, 0.0) * uDeltaTime;
    float speed = length(freeVelocity);
    if (speed > 0.00001)
    {
        float area = 3.14159265359 * radius * radius;
        vec3 dragAcceleration = -0.5 * AIR_DENSITY * AIR_DRAG_COEFFICIENT
            * area * speed * freeVelocity * inverseMass;
        freeVelocity += dragAcceleration * uDeltaTime;
    }
    vec3 freePosition = diskBefore.xyz + freeVelocity * uDeltaTime;

    float slopeX = (groundHeight(freePosition.x + 0.01, freePosition.z)
        - groundHeight(freePosition.x - 0.01, freePosition.z)) / 0.02;
    float slopeZ = (groundHeight(freePosition.x, freePosition.z + 0.01)
        - groundHeight(freePosition.x, freePosition.z - 0.01)) / 0.02;
    vec3 rampNormal = normalize(vec3(-slopeX, 1.0, -slopeZ));
    vec3 rampSurface = vec3(
        freePosition.x,
        groundHeight(freePosition.x, freePosition.z),
        freePosition.z);
    float signedDistance = dot(freePosition - rampSurface, rampNormal);

    vec3 rampPointOffset = -rampNormal * radius;
    vec3 rampContactVelocity = freeVelocity + cross(angularBefore, rampPointOffset);
    float rampNormalSpeed = dot(rampContactVelocity, rampNormal);
    vec3 rampTangentVelocity = rampContactVelocity - rampNormal * rampNormalSpeed;

    uint baseIndex = id * 384u;
    for (uint field = 0u; field < 384u; field++)
    {
        capture[baseIndex + field] = 0.0;
    }

    writeVec3(baseIndex + 0u, diskBefore.xyz);
    capture[baseIndex + 3u] = radius;
    writeVec3(baseIndex + 4u, velocityBefore);
    capture[baseIndex + 7u] = material.x;
    capture[baseIndex + 8u] = quaternionBefore.x;
    capture[baseIndex + 9u] = quaternionBefore.y;
    capture[baseIndex + 10u] = quaternionBefore.z;
    capture[baseIndex + 11u] = quaternionBefore.w;
    writeVec3(baseIndex + 12u, angularBefore);
    capture[baseIndex + 15u] = inertia;
    writeVec3(baseIndex + 16u, freePosition);
    capture[baseIndex + 19u] = inverseMass;
    writeVec3(baseIndex + 20u, freeVelocity);
    capture[baseIndex + 23u] = signedDistance;
    writeVec3(baseIndex + 24u, diskAfter.xyz);
    writeVec3(baseIndex + 27u, rampNormal);
    writeVec3(baseIndex + 30u, velocityAfter);
    writeVec3(baseIndex + 33u, angularAfter);
    writeVec3(baseIndex + 36u, diskAfter.xyz - freePosition);
    writeVec3(baseIndex + 39u, velocityAfter - freeVelocity);
    writeVec3(baseIndex + 42u, angularAfter - angularBefore);
    capture[baseIndex + 45u] = rampNormalSpeed;
    capture[baseIndex + 46u] = length(rampTangentVelocity);
    capture[baseIndex + 368u] = material.y;
    capture[baseIndex + 369u] = material.z;
    capture[baseIndex + 370u] = material.w;
    capture[baseIndex + 371u] = -GRAVITY;
    capture[baseIndex + 372u] = uDeltaTime;
    capture[baseIndex + 373u] = slopeX;
    capture[baseIndex + 374u] = slopeZ;

    ivec3 gridCell = ivec3(floor(diskAfter.xyz / CELL_SIZE));
    uint neighborCount = 0u;
    uint contactCount = 0u;
    bool neighborOverflow = false;
    bool hasRampContact = signedDistance < radius + 0.002;
    uint category = hasRampContact ? 1u : 0u;
    bool hasSphereContact = false;
    float highestRelativeSpeed = 0.0;

    for (int z = -1; z <= 1; z++)
    for (int y = -1; y <= 1; y++)
    for (int x = -1; x <= 1; x++)
    {
        int other = cells[cellIndex(gridCell + ivec3(x, y, z))];
        while (other >= 0)
        {
            uint otherId = uint(other);
            if (otherId != id)
            {
                vec4 otherDisk = positionsIn[otherId];
                vec4 otherMaterial = materials[otherId];
                ivec3 otherCell = ivec3(floor(otherDisk.xyz / CELL_SIZE));
                ivec3 candidateCell = gridCell + ivec3(x, y, z);
                if (all(equal(otherCell, candidateCell)))
                {
                    vec3 relativePosition = otherDisk.xyz - freePosition;
                    float centerDistance = length(relativePosition);
                    float contactDistance = radius + otherDisk.w;
                    float penetration = contactDistance - centerDistance;
                    if (penetration > 0.0)
                    {
                        hasSphereContact = true;
                        contactCount++;
                    }
                    vec3 otherVelocity = velocitiesIn[otherId].xyz;
                    vec3 relativeVelocity = otherVelocity - freeVelocity;
                    vec3 otherAngular = spinStatesIn[otherId * 2u + 1u].xyz;
                    vec3 relativeAngular = otherAngular - angularBefore;
                    vec3 normal = centerDistance > 0.000001
                        ? -relativePosition / centerDistance
                        : vec3(1.0, 0.0, 0.0);
                    vec3 relativeContactVelocity = freeVelocity
                        + cross(angularBefore, -normal * radius)
                        - otherVelocity
                        - cross(otherAngular, normal * otherDisk.w);
                    float relativeSpeed = length(relativeContactVelocity);
                    highestRelativeSpeed = max(highestRelativeSpeed, relativeSpeed);

                    if (centerDistance <= contactDistance + 0.05)
                    {
                        if (neighborCount < uMaxNeighbors)
                        {
                            uint neighborBase = baseIndex + 48u + neighborCount * 20u;
                            writeVec3(neighborBase + 0u, relativePosition);
                            capture[neighborBase + 3u] = centerDistance;
                            writeVec3(neighborBase + 4u, relativeVelocity);
                            writeVec3(neighborBase + 7u, relativeAngular);
                            capture[neighborBase + 10u] = penetration;
                            capture[neighborBase + 11u] = otherDisk.w;
                            capture[neighborBase + 12u] = otherMaterial.x;
                            capture[neighborBase + 13u] = otherMaterial.y;
                            capture[neighborBase + 14u] = otherMaterial.z;
                            capture[neighborBase + 15u] = otherMaterial.w;
                            writeVec3(neighborBase + 16u, relativeContactVelocity);
                            capture[neighborBase + 19u] = float(otherId);
                            neighborCount++;
                        }
                        else
                        {
                            atomicAdd(metadata[1], 1u);
                            neighborOverflow = true;
                        }
                    }
                }
            }
            other = links[other];
        }
    }

    if (hasSphereContact)
    {
        category = hasRampContact ? 3u : 2u;
    }
    if (highestRelativeSpeed > 1.5)
    {
        category = 4u;
    }
    else if (hasRampContact || hasSphereContact)
    {
        category = category == 0u ? 5u : category;
    }
    capture[baseIndex + 47u] = float(category);
    capture[baseIndex + 375u] = float(neighborCount);
    capture[baseIndex + 376u] = neighborOverflow ? 1.0 : 0.0;
    capture[baseIndex + 377u] = hasRampContact ? 1.0 : 0.0;
    capture[baseIndex + 378u] = hasSphereContact ? 1.0 : 0.0;
    capture[baseIndex + 379u] = float(contactCount);
    capture[baseIndex + 380u] = highestRelativeSpeed;

    if (hasRampContact) atomicAdd(metadata[2], 1u);
    if (hasSphereContact) atomicAdd(metadata[3], 1u);
    if (hasRampContact && hasSphereContact) atomicAdd(metadata[4], 1u);
    if (highestRelativeSpeed > 1.5) atomicAdd(metadata[5], 1u);
    else if (hasRampContact || hasSphereContact) atomicAdd(metadata[6], 1u);
    if (any(isnan(diskAfter.xyz)) || any(isinf(diskAfter.xyz))
        || any(isnan(velocityAfter)) || any(isinf(velocityAfter))
        || any(isnan(angularAfter)) || any(isinf(angularAfter)))
    {
        atomicAdd(metadata[7], 1u);
    }
}
