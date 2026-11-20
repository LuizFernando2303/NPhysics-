using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;

namespace NPhysics;

internal sealed class SceneRenderer : IDisposable
{
    private const int TerrainLength = 180;
    private const int TerrainWidth = 37;
    private const float TerrainStep = 0.3f;

    private readonly ProceduralTrack _track;
    private readonly GpuPhysics _physics;
    private readonly float[] _terrainVertices = new float[TerrainLength * TerrainWidth * 6];
    private readonly uint[] _terrainIndices = CreateTerrainIndices();

    private int _program;
    private int _diskVao;
    private int _diskVertexBuffer;
    private int _diskVertexCount;
    private int _ballVao;
    private int _ballVertexBuffer;
    private const int BallVertexCount = 6;
    private int _cubeVao;
    private int _cubeVertexBuffer;
    private int _terrainVao;
    private int _terrainVertexBuffer;
    private int _terrainIndexBuffer;
    private int _viewProjectionLocation;
    private int _objectLocation;
    private int _instancesLocation;
    private int _surfaceColorLocation;
    private int _vertexColorLocation;
    private int _rampSlopeLocation;
    private int _billboardLocation;
    private int _terrainColorLocation;
    private int _objectPatternLocation;
    private int _cameraRightLocation;
    private int _cameraUpLocation;
    private int _gravityLocation;
    private int _potentialReferenceHeightLocation;
    private int _potentialEnergyRangeLocation;
    private float _potentialReferenceHeight;
    private float _potentialEnergyRange;

    public SceneRenderer(ProceduralTrack track, GpuPhysics physics)
    {
        _track = track;
        _physics = physics;
    }

    public void Initialize()
    {
        _program = GlProgram.CreateGraphics(
            ShaderSource.Load("Surface.vert"),
            ShaderSource.Load("Surface.frag"));

        CreateDiskMesh();
        CreateBallBillboardMesh();
        CreateCubeMesh();
        CreateTerrainMesh();

        _viewProjectionLocation = GL.GetUniformLocation(_program, "uViewProjection");
        _objectLocation = GL.GetUniformLocation(_program, "uObject");
        _instancesLocation = GL.GetUniformLocation(_program, "uUseInstances");
        _surfaceColorLocation = GL.GetUniformLocation(_program, "uSurfaceColor");
        _vertexColorLocation = GL.GetUniformLocation(_program, "uUseVertexColor");
        _rampSlopeLocation = GL.GetUniformLocation(_program, "uRampSlope");
        _billboardLocation = GL.GetUniformLocation(_program, "uUseBillboard");
        _terrainColorLocation = GL.GetUniformLocation(_program, "uUseTerrainColor");
        _objectPatternLocation = GL.GetUniformLocation(_program, "uUseObjectPattern");
        _cameraRightLocation = GL.GetUniformLocation(_program, "uCameraRight");
        _cameraUpLocation = GL.GetUniformLocation(_program, "uCameraUp");
        _gravityLocation = GL.GetUniformLocation(_program, "uGravity");
        _potentialReferenceHeightLocation = GL.GetUniformLocation(_program, "uPotentialReferenceHeight");
        _potentialEnergyRangeLocation = GL.GetUniformLocation(_program, "uPotentialEnergyRange");

        var disksPerColumn = SimulationSettings.LaneCount * SimulationSettings.VerticalLayerCount;
        var lastSpawnColumn = (SimulationSettings.DiskCount - 1) / disksPerColumn;
        var lastSpawnX = -8f + lastSpawnColumn * SimulationSettings.SpawnSpacing;
        _potentialReferenceHeight = _track.Height(lastSpawnX, 0f);
        var highestSpawnHeight = _track.Height(-8f, 0f)
            + (SimulationSettings.VerticalLayerCount - 1) * SimulationSettings.VerticalLayerSpacing;
        _potentialEnergyRange = MathF.Max(
            (highestSpawnHeight - _potentialReferenceHeight)
                * SimulationSettings.MaximumMass
                * SimulationSettings.Gravity
                * 1.15f,
            1f);
    }

    public void Render(
        Matrix4 viewProjection,
        float cameraX,
        Vector3 cameraRight,
        Vector3 cameraUp)
    {
        _physics.CullVisibleDisks(viewProjection, BallVertexCount);
        GL.UseProgram(_program);
        GL.UniformMatrix4(_viewProjectionLocation, false, ref viewProjection);
        GL.Uniform1(_rampSlopeLocation, SimulationSettings.RampSlope);
        GL.Uniform3(_cameraRightLocation, cameraRight);
        GL.Uniform3(_cameraUpLocation, cameraUp);
        GL.Uniform1(_gravityLocation, SimulationSettings.Gravity);
        GL.Uniform1(_potentialReferenceHeightLocation, _potentialReferenceHeight);
        GL.Uniform1(_potentialEnergyRangeLocation, _potentialEnergyRange);

        _physics.BeginDrawingTiming();
        DrawTerrain(cameraX);
        DrawObstacles(_physics.GetObstacles());
        DrawBalls();
        _physics.EndDrawingTiming();
    }

    public void Dispose()
    {
        GL.DeleteBuffer(_diskVertexBuffer);
        GL.DeleteBuffer(_ballVertexBuffer);
        GL.DeleteBuffer(_cubeVertexBuffer);
        GL.DeleteBuffer(_terrainVertexBuffer);
        GL.DeleteBuffer(_terrainIndexBuffer);
        GL.DeleteVertexArray(_diskVao);
        GL.DeleteVertexArray(_ballVao);
        GL.DeleteVertexArray(_cubeVao);
        GL.DeleteVertexArray(_terrainVao);
        GL.DeleteProgram(_program);
    }

    private void DrawTerrain(float cameraX)
    {
        var cursor = 0;
        for (var xIndex = 0; xIndex < TerrainLength; xIndex++)
        {
            var x = cameraX - 18f + xIndex * TerrainStep;
            for (var zIndex = 0; zIndex < TerrainWidth; zIndex++)
            {
                var z = -9f + zIndex * 0.5f;
                var normal = _track.SurfaceNormal(x, z);
                _terrainVertices[cursor++] = x;
                _terrainVertices[cursor++] = _track.Height(x, z);
                _terrainVertices[cursor++] = z;
                _terrainVertices[cursor++] = normal.X;
                _terrainVertices[cursor++] = normal.Y;
                _terrainVertices[cursor++] = normal.Z;
            }
        }

        GL.BindBuffer(BufferTarget.ArrayBuffer, _terrainVertexBuffer);
        GL.BufferSubData(
            BufferTarget.ArrayBuffer,
            IntPtr.Zero,
            _terrainVertices.Length * sizeof(float),
            _terrainVertices);
        var identity = Matrix4.Identity;
        GL.UniformMatrix4(_objectLocation, false, ref identity);
        SetSurface(new Vector3(0.48f, 0.62f, 0.46f), terrainColor: true);
        GL.BindVertexArray(_terrainVao);
        GL.DrawElements(
            PrimitiveType.Triangles,
            _terrainIndices.Length,
            DrawElementsType.UnsignedInt,
            IntPtr.Zero);
    }

    private void DrawObstacles(Obstacle[] obstacles)
    {
        foreach (var obstacle in obstacles)
        {
            SetSurface(obstacle.Color, objectPattern: true);
            var scale = obstacle.IsBox
                ? Matrix4.CreateScale(obstacle.HalfSize * 2f)
                : Matrix4.CreateScale(
                    obstacle.Radius,
                    obstacle.HalfSize.Y / SimulationSettings.ObstacleMeshHalfThicknessRatio,
                    obstacle.Radius);
            var model = scale * Matrix4.CreateTranslation(obstacle.Position);
            GL.UniformMatrix4(_objectLocation, false, ref model);
            GL.BindVertexArray(obstacle.IsBox ? _cubeVao : _diskVao);

            if (obstacle.IsBox)
            {
                GL.DrawArrays(PrimitiveType.Triangles, 0, 36);
            }
            else
            {
                GL.DrawArrays(PrimitiveType.Triangles, 0, _diskVertexCount);
            }
        }
    }

    private void DrawBalls()
    {
        SetSurface(Vector3.Zero, instanced: true, billboard: true);
        GL.BindBufferBase(BufferRangeTarget.ShaderStorageBuffer, 0, _physics.PositionBuffer);
        GL.BindBufferBase(BufferRangeTarget.ShaderStorageBuffer, 1, _physics.VisibleDrawBuffer);
        GL.BindBufferBase(BufferRangeTarget.ShaderStorageBuffer, 7, _physics.MaterialBuffer);
        GL.BindVertexArray(_ballVao);
        GL.BindBuffer(BufferTarget.DrawIndirectBuffer, _physics.VisibleDrawBuffer);
        GL.DrawArraysIndirect(PrimitiveType.Triangles, IntPtr.Zero);
    }

    private void SetSurface(
        Vector3 color,
        bool instanced = false,
        bool billboard = false,
        bool terrainColor = false,
        bool objectPattern = false)
    {
        GL.Uniform1(_instancesLocation, instanced ? 1 : 0);
        GL.Uniform1(_vertexColorLocation, instanced ? 1 : 0);
        GL.Uniform1(_billboardLocation, billboard ? 1 : 0);
        GL.Uniform1(_terrainColorLocation, terrainColor ? 1 : 0);
        GL.Uniform1(_objectPatternLocation, objectPattern ? 1 : 0);
        GL.Uniform3(_surfaceColorLocation, color);
    }

    private void CreateBallBillboardMesh()
    {
        var vertices = new float[]
        {
            -1f, -1f, 0f, 0f, 0f, 1f,
             1f, -1f, 0f, 0f, 0f, 1f,
             1f,  1f, 0f, 0f, 0f, 1f,
            -1f, -1f, 0f, 0f, 0f, 1f,
             1f,  1f, 0f, 0f, 0f, 1f,
            -1f,  1f, 0f, 0f, 0f, 1f,
        };
        _ballVao = GL.GenVertexArray();
        _ballVertexBuffer = GL.GenBuffer();
        GL.BindVertexArray(_ballVao);
        UploadVertices(_ballVertexBuffer, vertices);
        ConfigureVertexAttributes();
    }

    private void CreateDiskMesh()
    {
        const int segments = 12;
        const float halfThickness = SimulationSettings.ObstacleMeshHalfThicknessRatio;
        var vertices = new List<float>(segments * 12 * 6);

        for (var segment = 0; segment < segments; segment++)
        {
            var angleA = MathF.Tau * segment / segments;
            var angleB = MathF.Tau * (segment + 1) / segments;
            var current = new Vector3(MathF.Cos(angleA), 0f, MathF.Sin(angleA));
            var next = new Vector3(MathF.Cos(angleB), 0f, MathF.Sin(angleB));
            var lowerCurrent = new Vector3(current.X, -halfThickness, current.Z);
            var lowerNext = new Vector3(next.X, -halfThickness, next.Z);
            var upperCurrent = new Vector3(current.X, halfThickness, current.Z);
            var upperNext = new Vector3(next.X, halfThickness, next.Z);
            var sideNormal = new Vector3(
                MathF.Cos((angleA + angleB) * 0.5f),
                0f,
                MathF.Sin((angleA + angleB) * 0.5f));

            AddTriangle(vertices, lowerCurrent, upperCurrent, upperNext, sideNormal, sideNormal, sideNormal);
            AddTriangle(vertices, lowerCurrent, upperNext, lowerNext, sideNormal, sideNormal, sideNormal);
            AddTriangle(vertices, Vector3.Zero, next, current, Vector3.UnitY, Vector3.UnitY, Vector3.UnitY);
            AddTriangle(vertices, Vector3.Zero, current, next, -Vector3.UnitY, -Vector3.UnitY, -Vector3.UnitY);
        }

        _diskVertexCount = vertices.Count / 6;
        _diskVao = GL.GenVertexArray();
        _diskVertexBuffer = GL.GenBuffer();
        GL.BindVertexArray(_diskVao);
        UploadVertices(_diskVertexBuffer, vertices.ToArray());
        ConfigureVertexAttributes();
    }

    private void CreateCubeMesh()
    {
        var faces = new (Vector3 Normal, Vector3 A, Vector3 B, Vector3 C, Vector3 D)[]
        {
            (Vector3.UnitY, new(-.5f,.5f,-.5f), new(-.5f,.5f,.5f), new(.5f,.5f,.5f), new(.5f,.5f,-.5f)),
            (-Vector3.UnitY, new(-.5f,-.5f,.5f), new(-.5f,-.5f,-.5f), new(.5f,-.5f,-.5f), new(.5f,-.5f,.5f)),
            (Vector3.UnitX, new(.5f,-.5f,-.5f), new(.5f,.5f,-.5f), new(.5f,.5f,.5f), new(.5f,-.5f,.5f)),
            (-Vector3.UnitX, new(-.5f,-.5f,.5f), new(-.5f,.5f,.5f), new(-.5f,.5f,-.5f), new(-.5f,-.5f,-.5f)),
            (Vector3.UnitZ, new(.5f,-.5f,.5f), new(.5f,.5f,.5f), new(-.5f,.5f,.5f), new(-.5f,-.5f,.5f)),
            (-Vector3.UnitZ, new(-.5f,-.5f,-.5f), new(-.5f,.5f,-.5f), new(.5f,.5f,-.5f), new(.5f,-.5f,-.5f)),
        };
        var vertices = new float[36 * 6];
        var cursor = 0;
        foreach (var face in faces)
        {
            WriteVertex(vertices, ref cursor, face.A, face.Normal);
            WriteVertex(vertices, ref cursor, face.B, face.Normal);
            WriteVertex(vertices, ref cursor, face.C, face.Normal);
            WriteVertex(vertices, ref cursor, face.A, face.Normal);
            WriteVertex(vertices, ref cursor, face.C, face.Normal);
            WriteVertex(vertices, ref cursor, face.D, face.Normal);
        }

        _cubeVao = GL.GenVertexArray();
        _cubeVertexBuffer = GL.GenBuffer();
        GL.BindVertexArray(_cubeVao);
        UploadVertices(_cubeVertexBuffer, vertices);
        ConfigureVertexAttributes();
    }

    private void CreateTerrainMesh()
    {
        _terrainVao = GL.GenVertexArray();
        _terrainVertexBuffer = GL.GenBuffer();
        _terrainIndexBuffer = GL.GenBuffer();
        GL.BindVertexArray(_terrainVao);

        GL.BindBuffer(BufferTarget.ArrayBuffer, _terrainVertexBuffer);
        GL.BufferData(
            BufferTarget.ArrayBuffer,
            _terrainVertices.Length * sizeof(float),
            _terrainVertices,
            BufferUsageHint.DynamicDraw);
        UploadIndices(_terrainIndexBuffer, _terrainIndices);
        ConfigureVertexAttributes();
    }

    private void ConfigureVertexAttributes()
    {
        GL.EnableVertexAttribArray(0);
        GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 6 * sizeof(float), 0);
        GL.EnableVertexAttribArray(1);
        GL.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, 6 * sizeof(float), 3 * sizeof(float));
    }

    private static void UploadVertices(int buffer, float[] vertices)
    {
        GL.BindBuffer(BufferTarget.ArrayBuffer, buffer);
        GL.BufferData(BufferTarget.ArrayBuffer, vertices.Length * sizeof(float), vertices, BufferUsageHint.StaticDraw);
    }

    private static void UploadIndices(int buffer, uint[] indices)
    {
        GL.BindBuffer(BufferTarget.ElementArrayBuffer, buffer);
        GL.BufferData(BufferTarget.ElementArrayBuffer, indices.Length * sizeof(uint), indices, BufferUsageHint.StaticDraw);
    }

    private static void WriteVertex(float[] vertices, ref int cursor, Vector3 position, Vector3 normal)
    {
        vertices[cursor++] = position.X;
        vertices[cursor++] = position.Y;
        vertices[cursor++] = position.Z;
        vertices[cursor++] = normal.X;
        vertices[cursor++] = normal.Y;
        vertices[cursor++] = normal.Z;
    }

    private static void AddTriangle(
        List<float> vertices,
        Vector3 a,
        Vector3 b,
        Vector3 c,
        Vector3 normalA,
        Vector3 normalB,
        Vector3 normalC)
    {
        AddVertex(vertices, a, normalA);
        AddVertex(vertices, b, normalB);
        AddVertex(vertices, c, normalC);
    }

    private static void AddVertex(List<float> vertices, Vector3 position, Vector3 normal)
    {
        vertices.Add(position.X);
        vertices.Add(position.Y);
        vertices.Add(position.Z);
        vertices.Add(normal.X);
        vertices.Add(normal.Y);
        vertices.Add(normal.Z);
    }

    private static uint[] CreateTerrainIndices()
    {
        var indices = new uint[(TerrainLength - 1) * (TerrainWidth - 1) * 6];
        for (var x = 0; x < TerrainLength - 1; x++)
        for (var z = 0; z < TerrainWidth - 1; z++)
        {
            var a = (uint)(x * TerrainWidth + z);
            var b = a + TerrainWidth;
            var index = (x * (TerrainWidth - 1) + z) * 6;
            indices[index] = a;
            indices[index + 1] = a + 1;
            indices[index + 2] = b;
            indices[index + 3] = a + 1;
            indices[index + 4] = b + 1;
            indices[index + 5] = b;
        }

        return indices;
    }
}
