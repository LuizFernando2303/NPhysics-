#version 430 core
layout(local_size_x = 256) in;
layout(std430, binding = 4) buffer Grid { int cells[]; };
uniform uint uCellCount;

void main()
{
    uint id = gl_GlobalInvocationID.x;
    if (id < uCellCount)
    {
        cells[id] = -1;
    }
}
