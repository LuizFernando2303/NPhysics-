#version 430 core
layout(local_size_x = 128) in;
layout(std430, binding = 0) readonly buffer PositionsIn { vec4 positionsIn[]; };
layout(std430, binding = 1) readonly buffer VelocitiesIn { vec4 velocitiesIn[]; };
layout(std430, binding = 2) writeonly buffer PositionsOut { vec4 positionsOut[]; };
layout(std430, binding = 3) writeonly buffer VelocitiesOut { vec4 velocitiesOut[]; };
layout(std430, binding = 4) readonly buffer Grid { int cells[]; };
layout(std430, binding = 5) readonly buffer Links { int links[]; };
layout(std430, binding = 6) readonly buffer Obstacles { vec4 obstacleData[]; };
layout(std430, binding = 7) readonly buffer Materials { vec4 materials[]; };
layout(std430, binding = 8) buffer Metrics { uint metrics[]; };
layout(std430, binding = 9) readonly buffer SpinStatesIn { vec4 spinStatesIn[]; };
layout(std430, binding = 10) writeonly buffer SpinStatesOut { vec4 spinStatesOut[]; };

uniform uint uDiskCount;
uniform int uObstacleCount;
uniform float uDeltaTime;
uniform bool uMetricsEnabled;

const float CELL_SIZE = @CELL_SIZE@;
const float GRAVITY = @GRAVITY@;
const uint GRID_MASK = @GRID_MASK@u;
const float RAMP_HALF_WIDTH = @RAMP_HALF_WIDTH@;
const float RAMP_SLOPE = @RAMP_SLOPE@;
const float AIR_DENSITY = @AIR_DENSITY@;
const float AIR_DRAG_COEFFICIENT = @AIR_DRAG_COEFFICIENT@;
const float SPHERE_INERTIA_FACTOR = @SPHERE_INERTIA_FACTOR@;
const float POSITION_SLOP = 0.001;
const float POSITION_CORRECTION = 0.8;

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

float inverseSphereInertia(float inverseMass, float radius)
{
    return inverseMass / (SPHERE_INERTIA_FACTOR * radius * radius);
}

void applyImpulse(
    inout vec3 velocity,
    inout vec3 angularVelocity,
    vec3 contactOffset,
    vec3 impulse,
    float inverseMass,
    float inverseInertia)
{
    velocity += impulse * inverseMass;
    angularVelocity += cross(contactOffset, impulse) * inverseInertia;
}

void resolveStaticContact(
    inout vec3 position,
    inout vec3 velocity,
    inout vec3 angularVelocity,
    float radius,
    vec4 material,
    vec3 normal,
    float penetration,
    float restitution)
{
    position += normal * max(penetration - POSITION_SLOP, 0.0) * POSITION_CORRECTION;

    float inverseMass = 1.0 / material.x;
    float inverseInertia = inverseSphereInertia(inverseMass, radius);
    vec3 contactOffset = -normal * radius;
    vec3 contactVelocity = velocity + cross(angularVelocity, contactOffset);
    float normalSpeed = dot(contactVelocity, normal);
    if (normalSpeed >= 0.0)
    {
        return;
    }

    float normalDenominator = inverseMass
        + inverseInertia * dot(cross(contactOffset, normal), cross(contactOffset, normal));
    float normalImpulseMagnitude = -(1.0 + restitution) * normalSpeed / normalDenominator;
    vec3 normalImpulse = normal * normalImpulseMagnitude;
    applyImpulse(
        velocity,
        angularVelocity,
        contactOffset,
        normalImpulse,
        inverseMass,
        inverseInertia);

    contactVelocity = velocity + cross(angularVelocity, contactOffset);
    vec3 tangentVelocity = contactVelocity - normal * dot(contactVelocity, normal);
    float tangentSpeed = length(tangentVelocity);
    if (tangentSpeed < 0.00001)
    {
        return;
    }

    vec3 tangent = tangentVelocity / tangentSpeed;
    vec3 angularTangent = cross(contactOffset, tangent);
    float tangentDenominator = inverseMass
        + inverseInertia * dot(angularTangent, angularTangent);
    float requestedFriction = tangentSpeed / tangentDenominator;
    float staticLimit = material.y * normalImpulseMagnitude;
    float frictionImpulseMagnitude = requestedFriction <= staticLimit
        ? requestedFriction
        : material.z * normalImpulseMagnitude;
    vec3 frictionImpulse = -tangent * frictionImpulseMagnitude;
    applyImpulse(
        velocity,
        angularVelocity,
        contactOffset,
        frictionImpulse,
        inverseMass,
        inverseInertia);
}

bool getBoxContact(
    vec3 position,
    vec3 halfSize,
    float radius,
    out vec3 normal,
    out float penetration)
{
    vec3 closest = clamp(position, -halfSize, halfSize);
    vec3 difference = position - closest;
    float distanceSquared = dot(difference, difference);

    if (distanceSquared > 0.000001)
    {
        float distanceToBox = sqrt(distanceSquared);
        penetration = radius - distanceToBox;
        if (penetration <= 0.0)
        {
            return false;
        }

        normal = difference / distanceToBox;
        return true;
    }

    vec3 faceDistance = halfSize - abs(position);
    if (faceDistance.x < faceDistance.y && faceDistance.x < faceDistance.z)
    {
        normal = vec3(position.x >= 0.0 ? 1.0 : -1.0, 0.0, 0.0);
        penetration = radius + faceDistance.x;
    }
    else if (faceDistance.y < faceDistance.z)
    {
        normal = vec3(0.0, position.y >= 0.0 ? 1.0 : -1.0, 0.0);
        penetration = radius + faceDistance.y;
    }
    else
    {
        normal = vec3(0.0, 0.0, position.z >= 0.0 ? 1.0 : -1.0);
        penetration = radius + faceDistance.z;
    }

    return true;
}

bool getCylinderContact(
    vec3 position,
    float cylinderRadius,
    float halfHeight,
    float sphereRadius,
    out vec3 normal,
    out float penetration)
{
    float radialDistance = length(position.xz);
    float clampedRadius = min(radialDistance, cylinderRadius);
    vec2 radialDirection = radialDistance > 0.00001
        ? position.xz / radialDistance
        : vec2(1.0, 0.0);
    vec3 closest = vec3(
        radialDirection.x * clampedRadius,
        clamp(position.y, -halfHeight, halfHeight),
        radialDirection.y * clampedRadius);
    vec3 difference = position - closest;
    float distanceSquared = dot(difference, difference);

    if (distanceSquared > 0.000001)
    {
        float distanceToCylinder = sqrt(distanceSquared);
        penetration = sphereRadius - distanceToCylinder;
        if (penetration <= 0.0)
        {
            return false;
        }

        normal = difference / distanceToCylinder;
        return true;
    }

    float sideDistance = cylinderRadius - radialDistance;
    float capDistance = halfHeight - abs(position.y);
    if (sideDistance < capDistance)
    {
        normal = vec3(radialDirection.x, 0.0, radialDirection.y);
        penetration = sphereRadius + sideDistance;
    }
    else
    {
        normal = vec3(0.0, position.y >= 0.0 ? 1.0 : -1.0, 0.0);
        penetration = sphereRadius + capDistance;
    }

    return true;
}

void resolveObstacle(
    inout vec3 position,
    inout vec3 velocity,
    inout vec3 angularVelocity,
    float radius,
    vec4 material,
    vec4 obstaclePosition,
    vec4 obstacleShape)
{
    vec3 normal;
    float penetration;
    bool hasContact;

    if (obstacleShape.w > 0.5)
    {
        hasContact = getBoxContact(
            position - obstaclePosition.xyz,
            obstacleShape.xyz,
            radius,
            normal,
            penetration);
    }
    else
    {
        hasContact = getCylinderContact(
            position - obstaclePosition.xyz,
            obstaclePosition.w,
            obstacleShape.y,
            radius,
            normal,
            penetration);
    }

    if (!hasContact)
    {
        return;
    }

    if (uMetricsEnabled)
    {
        atomicAdd(metrics[3], 1u);
    }

    resolveStaticContact(
        position,
        velocity,
        angularVelocity,
        radius,
        material,
        normal,
        penetration,
        material.w);
}

void resolveRamp(
    inout vec3 position,
    inout vec3 velocity,
    inout vec3 angularVelocity,
    float radius,
    vec4 material)
{
    float slopeX = (groundHeight(position.x + 0.01, position.z)
        - groundHeight(position.x - 0.01, position.z)) / 0.02;
    float slopeZ = (groundHeight(position.x, position.z + 0.01)
        - groundHeight(position.x, position.z - 0.01)) / 0.02;
    vec3 normal = normalize(vec3(-slopeX, 1.0, -slopeZ));
    vec3 surfacePoint = vec3(position.x, groundHeight(position.x, position.z), position.z);
    float distanceToSurface = dot(position - surfacePoint, normal);
    float penetration = radius - distanceToSurface;

    if (penetration < -0.002)
    {
        return;
    }

    if (uMetricsEnabled)
    {
        atomicAdd(metrics[2], 1u);
    }

    float normalSpeed = dot(
        velocity + cross(angularVelocity, -normal * radius),
        normal);
    float restitution = -normalSpeed > 0.35 ? material.w : 0.0;
    resolveStaticContact(
        position,
        velocity,
        angularVelocity,
        radius,
        material,
        normal,
        penetration,
        restitution);
}

void resolveSphereCollision(
    inout vec3 position,
    inout vec3 velocity,
    inout vec3 angularVelocity,
    float radius,
    vec4 material,
    vec4 otherPosition,
    vec4 otherVelocity,
    vec4 otherSpin,
    vec4 otherMaterial)
{
    vec3 difference = position - otherPosition.xyz;
    float distanceSquared = dot(difference, difference);
    float contactDistance = radius + otherPosition.w;
    if (distanceSquared >= contactDistance * contactDistance)
    {
        return;
    }

    if (uMetricsEnabled)
    {
        atomicAdd(metrics[1], 1u);
    }

    float distanceBetweenCenters = sqrt(max(distanceSquared, 0.000001));
    vec3 normal = distanceSquared < 0.000001
        ? vec3(1.0, 0.0, 0.0)
        : difference / distanceBetweenCenters;
    float penetration = contactDistance - distanceBetweenCenters;
    float inverseMass = 1.0 / material.x;
    float otherInverseMass = 1.0 / otherMaterial.x;
    float inverseInertia = inverseSphereInertia(inverseMass, radius);
    float otherInverseInertia = inverseSphereInertia(otherInverseMass, otherPosition.w);
    float inverseMassSum = inverseMass + otherInverseMass;

    position += normal
        * max(penetration - POSITION_SLOP, 0.0)
        * POSITION_CORRECTION
        * inverseMass / inverseMassSum;

    vec3 contactOffset = -normal * radius;
    vec3 otherContactOffset = normal * otherPosition.w;
    vec3 relativeContactVelocity = velocity
        + cross(angularVelocity, contactOffset)
        - otherVelocity.xyz
        - cross(otherSpin.xyz, otherContactOffset);
    float normalSpeed = dot(relativeContactVelocity, normal);
    if (normalSpeed >= 0.0)
    {
        return;
    }

    vec3 angularNormalA = cross(contactOffset, normal);
    vec3 angularNormalB = cross(otherContactOffset, normal);
    float normalDenominator = inverseMassSum
        + inverseInertia * dot(angularNormalA, angularNormalA)
        + otherInverseInertia * dot(angularNormalB, angularNormalB);
    float restitution = min(material.w, otherMaterial.w);
    float normalImpulseMagnitude = -(1.0 + restitution) * normalSpeed / normalDenominator;

    relativeContactVelocity = velocity
        + cross(angularVelocity, contactOffset)
        - otherVelocity.xyz
        - cross(otherSpin.xyz, otherContactOffset);
    vec3 tangentVelocity = relativeContactVelocity
        - normal * dot(relativeContactVelocity, normal);
    float tangentSpeed = length(tangentVelocity);
    if (tangentSpeed < 0.00001)
    {
        velocity += normal * normalImpulseMagnitude * inverseMass;
        angularVelocity += cross(contactOffset, normal * normalImpulseMagnitude) * inverseInertia;
        return;
    }

    vec3 tangent = tangentVelocity / tangentSpeed;
    vec3 angularTangentA = cross(contactOffset, tangent);
    vec3 angularTangentB = cross(otherContactOffset, tangent);
    float tangentDenominator = inverseMassSum
        + inverseInertia * dot(angularTangentA, angularTangentA)
        + otherInverseInertia * dot(angularTangentB, angularTangentB);
    float requestedFriction = tangentSpeed / tangentDenominator;
    float staticFriction = 0.5 * (material.y + otherMaterial.y);
    float dynamicFriction = 0.5 * (material.z + otherMaterial.z);
    float frictionImpulseMagnitude = requestedFriction <= staticFriction * normalImpulseMagnitude
        ? requestedFriction
        : dynamicFriction * normalImpulseMagnitude;

    vec3 impulseOnCurrent = normal * normalImpulseMagnitude
        - tangent * frictionImpulseMagnitude;
    applyImpulse(
        velocity,
        angularVelocity,
        contactOffset,
        impulseOnCurrent,
        inverseMass,
        inverseInertia);
}

vec4 integrateOrientation(vec4 orientation, vec3 angularVelocity, float deltaTime)
{
    vec4 derivative = vec4(
        cross(angularVelocity, orientation.xyz) + orientation.w * angularVelocity,
        -dot(angularVelocity, orientation.xyz));
    orientation += 0.5 * deltaTime * derivative;
    float magnitude = length(orientation);
    return magnitude > 0.000001 ? orientation / magnitude : vec4(0.0, 0.0, 0.0, 1.0);
}

void main()
{
    uint id = gl_GlobalInvocationID.x;
    if (id >= uDiskCount)
    {
        return;
    }

    vec4 disk = positionsIn[id];
    vec4 material = materials[id];
    uint spinOffset = id * 2u;
    vec4 orientation = spinStatesIn[spinOffset];
    vec3 angularVelocity = spinStatesIn[spinOffset + 1u].xyz;
    float radius = disk.w;
    float inverseMass = 1.0 / material.x;
    vec3 position = disk.xyz;
    vec3 velocity = velocitiesIn[id].xyz;
    float dt = uDeltaTime;

    velocity += vec3(0.0, -GRAVITY, 0.0) * dt;
    float speed = length(velocity);
    if (speed > 0.00001)
    {
        float area = 3.14159265359 * radius * radius;
        vec3 dragAcceleration = -0.5 * AIR_DENSITY * AIR_DRAG_COEFFICIENT
            * area * speed * velocity * inverseMass;
        velocity += dragAcceleration * dt;
    }
    position += velocity * dt;

    resolveRamp(position, velocity, angularVelocity, radius, material);

    for (int obstacle = 0; obstacle < uObstacleCount; obstacle++)
    {
        resolveObstacle(
            position,
            velocity,
            angularVelocity,
            radius,
            material,
            obstacleData[obstacle * 2],
            obstacleData[obstacle * 2 + 1]);
    }

    ivec3 gridCell = ivec3(floor(position / CELL_SIZE));
    for (int z = -1; z <= 1; z++)
    for (int y = -1; y <= 1; y++)
    for (int x = -1; x <= 1; x++)
    {
        int other = cells[cellIndex(gridCell + ivec3(x, y, z))];
        while (other >= 0)
        {
            if (uint(other) != id)
            {
                if (uMetricsEnabled)
                {
                    atomicAdd(metrics[0], 1u);
                }

                uint otherId = uint(other);
                resolveSphereCollision(
                    position,
                    velocity,
                    angularVelocity,
                    radius,
                    material,
                    positionsIn[otherId],
                    velocitiesIn[otherId],
                    spinStatesIn[otherId * 2u + 1u],
                    materials[otherId]);
            }

            other = links[other];
        }
    }

    if (position.z < -RAMP_HALF_WIDTH + radius)
    {
        position.z = -RAMP_HALF_WIDTH + radius;
        resolveStaticContact(
            position,
            velocity,
            angularVelocity,
            radius,
            material,
            vec3(0.0, 0.0, 1.0),
            0.0,
            material.w);
    }
    if (position.z > RAMP_HALF_WIDTH - radius)
    {
        position.z = RAMP_HALF_WIDTH - radius;
        resolveStaticContact(
            position,
            velocity,
            angularVelocity,
            radius,
            material,
            vec3(0.0, 0.0, -1.0),
            0.0,
            material.w);
    }

    orientation = integrateOrientation(orientation, angularVelocity, dt);

    if (uMetricsEnabled)
    {
        if (any(isnan(position)) || any(isinf(position))
            || any(isnan(velocity)) || any(isinf(velocity))
            || any(isnan(angularVelocity)) || any(isinf(angularVelocity))
            || any(isnan(orientation)) || any(isinf(orientation)))
        {
            atomicAdd(metrics[5], 1u);
        }
        else
        {
            atomicMax(metrics[6], floatBitsToUint(length(velocity)));
            atomicMax(metrics[8], floatBitsToUint(length(angularVelocity)));
        }
    }

    positionsOut[id] = vec4(position, radius);
    velocitiesOut[id] = vec4(velocity, 0.0);
    spinStatesOut[spinOffset] = orientation;
    spinStatesOut[spinOffset + 1u] = vec4(angularVelocity, 0.0);
}
