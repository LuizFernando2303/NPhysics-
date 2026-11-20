#version 430 core
layout(local_size_x = 128) in;
layout(std430, binding = 0) readonly buffer Positions { vec4 positions[]; };
layout(std430, binding = 4) buffer Grid { int cells[]; };
layout(std430, binding = 5) writeonly buffer Links { int links[]; };
layout(std430, binding = 6) buffer Metrics { uint metrics[]; };

uniform uint uDiskCount;
uniform bool uMetricsEnabled;

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

void main()
{
    uint id = gl_GlobalInvocationID.x;
    if (id >= uDiskCount)
    {
        return;
    }

    ivec3 cell = ivec3(floor(positions[id].xyz / CELL_SIZE));
    uint slot = cellIndex(cell);
    int previous = atomicExchange(cells[slot], int(id));
    links[id] = previous;
    if (uMetricsEnabled && previous >= 0)
    {
        atomicAdd(metrics[4], 1u);
    }
}
