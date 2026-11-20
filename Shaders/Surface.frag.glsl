#version 430 core
in vec3 vNormal;
in vec3 vColor;
in vec3 vWorldPosition;
in vec3 vLocalPosition;
in vec2 vBillboardCoordinate;
in vec3 vSphereCenter;
in float vSphereRadius;

uniform mat4 uViewProjection;
uniform vec3 uSurfaceColor;
uniform int uUseVertexColor;
uniform int uUseBillboard;
uniform int uUseTerrainColor;
uniform int uUseObjectPattern;
uniform float uRampSlope;
uniform vec3 uCameraRight;
uniform vec3 uCameraUp;
out vec4 outputColor;

void main()
{
    gl_FragDepth = gl_FragCoord.z;
    vec3 color = uUseVertexColor == 1 ? vColor : uSurfaceColor;
    if (uUseBillboard == 1)
    {
        float radiusSquared = dot(vBillboardCoordinate, vBillboardCoordinate);
        if (radiusSquared > 1.0)
        {
            discard;
        }

        float sphereHeight = sqrt(max(1.0 - radiusSquared, 0.0));
        vec3 cameraFacing = normalize(cross(uCameraRight, uCameraUp));
        float outline = smoothstep(0.78, 0.94, radiusSquared);
        float highlightDistance = length(vBillboardCoordinate - vec2(-0.32, 0.32));
        float highlight = 1.0 - smoothstep(0.10, 0.16, highlightDistance);
        vec3 sphereColor = mix(color, vec3(0.035, 0.045, 0.08), outline);
        sphereColor = mix(sphereColor, min(color * 1.35 + 0.12, vec3(1.0)), highlight);
        vec3 surfacePosition = vSphereCenter + vSphereRadius * (
            uCameraRight * vBillboardCoordinate.x
            + uCameraUp * vBillboardCoordinate.y
            + cameraFacing * sphereHeight);
        vec4 clipPosition = uViewProjection * vec4(surfacePosition, 1.0);
        gl_FragDepth = 0.5 * (clipPosition.z / clipPosition.w) + 0.5;
        outputColor = vec4(sphereColor, 1.0);
        return;
    }

    if (uUseTerrainColor == 1)
    {
        float relief = vWorldPosition.y + uRampSlope * vWorldPosition.x;
        float bands = 0.5 + 0.5 * sin(
            relief * 8.0 + sin(vWorldPosition.z * 0.38 + vWorldPosition.x * 0.07));
        float contour = 1.0 - smoothstep(
            0.0,
            0.055,
            abs(fract(relief * 2.8) - 0.5));
        vec3 lowTerrain = vec3(0.32, 0.72, 0.42);
        vec3 highTerrain = vec3(0.55, 0.88, 0.48);
        color = mix(lowTerrain, highTerrain, 0.22 + bands * 0.38);
        color = mix(color, vec3(0.16, 0.42, 0.30), contour * 0.55);
    }
    else if (uUseObjectPattern == 1)
    {
        float stripe = step(
            0.52,
            fract((vLocalPosition.x + vLocalPosition.z) * 3.2
                + (vLocalPosition.y + 0.5) * 1.5));
        color = mix(color, color * 0.32, stripe * 0.55);
    }

    outputColor = vec4(color, 1.0);
}
