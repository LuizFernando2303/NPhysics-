using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;

namespace NPhysics;

internal enum SimulationControlAction
{
    TogglePause,
    Reset,
    ToggleDiagnostics,
    CaptureFrame,
    ToggleDatasetCapture,
    CycleVisualization,
    ToggleDashboard,
    OpenReport,
}

internal sealed class SimulationHud : IDisposable
{
    private const int VertexStride = 6;
    private const float ButtonHeight = 38f;
    private const float ButtonGap = 8f;
    private const float HorizontalPadding = 12f;
    private const float VerticalPadding = 10f;

    private readonly int _program;
    private readonly int _vao;
    private readonly int _vertexBuffer;
    private readonly int _viewportLocation;
    private readonly SimulationTextRenderer _textRenderer;
    private readonly List<float> _vertices = [];
    private readonly MouseButtonEdgeDetector _leftButtonEdge = new();
    private bool _disposed;

    public SimulationHud()
    {
        _textRenderer = new SimulationTextRenderer();
        const string vertexShader = """
            #version 430 core
            layout(location = 0) in vec2 aPosition;
            layout(location = 1) in vec4 aColor;
            uniform vec2 uViewport;
            out vec4 vColor;
            void main()
            {
                vec2 clip = vec2(
                    (aPosition.x / uViewport.x) * 2.0 - 1.0,
                    1.0 - (aPosition.y / uViewport.y) * 2.0);
                gl_Position = vec4(clip, 0.0, 1.0);
                vColor = aColor;
            }
            """;
        const string fragmentShader = """
            #version 430 core
            in vec4 vColor;
            out vec4 fragmentColor;
            void main()
            {
                fragmentColor = vColor;
            }
            """;
        _program = GlProgram.CreateGraphics(vertexShader, fragmentShader);
        _viewportLocation = GL.GetUniformLocation(_program, "uViewport");
        if (_viewportLocation < 0)
        {
            throw new InvalidOperationException("Uniform obrigatorio nao encontrado: uViewport.");
        }

        _vao = GL.GenVertexArray();
        _vertexBuffer = GL.GenBuffer();
        GL.BindVertexArray(_vao);
        GL.BindBuffer(BufferTarget.ArrayBuffer, _vertexBuffer);
        GL.BufferData(BufferTarget.ArrayBuffer, 0, IntPtr.Zero, BufferUsageHint.DynamicDraw);
        GL.EnableVertexAttribArray(0);
        GL.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, VertexStride * sizeof(float), 0);
        GL.EnableVertexAttribArray(1);
        GL.VertexAttribPointer(
            1,
            4,
            VertexAttribPointerType.Float,
            false,
            VertexStride * sizeof(float),
            2 * sizeof(float));
    }

    public SimulationControlAction? Update(
        Vector2 cursorPosition,
        bool leftButtonPressed,
        int width,
        bool paused,
        bool diagnosticsEnabled,
        bool datasetCaptureEnabled,
        bool canCaptureFrame,
        bool allowActions,
        bool datasetCaptureAvailable = true)
    {
        if (!_leftButtonEdge.WasPressed(leftButtonPressed))
        {
            return null;
        }
        if (!allowActions)
        {
            return null;
        }

        foreach (var button in CreateButtons(
            width,
            paused,
            diagnosticsEnabled,
            datasetCaptureEnabled,
            canCaptureFrame,
            datasetCaptureAvailable))
        {
            if (button.Enabled && button.Bounds.Contains(cursorPosition))
            {
                return button.Action;
            }
        }

        return null;
    }

    public void Render(
        int width,
        int height,
        Vector2 cursorPosition,
        bool paused,
        bool diagnosticsEnabled,
        bool datasetCaptureEnabled,
        bool canCaptureFrame,
        bool datasetCaptureAvailable = true)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _vertices.Clear();
        _textRenderer.BeginFrame();
        foreach (var button in CreateButtons(
            width,
            paused,
            diagnosticsEnabled,
            datasetCaptureEnabled,
            canCaptureFrame,
            datasetCaptureAvailable))
        {
            var hovered = button.Bounds.Contains(cursorPosition);
            var color = !button.Enabled
                ? (0.09f, 0.11f, 0.14f, 0.96f)
                : hovered
                    ? (0.10f, 0.23f, 0.24f, 1f)
                    : (0.07f, 0.09f, 0.12f, 0.98f);
            AddRectangle(button.Bounds, color);
            AddBorder(button.Bounds, hovered && button.Enabled
                ? (0.28f, 0.65f, 0.63f, 1f)
                : (0.18f, 0.23f, 0.29f, 1f));

            const float fontSize = 14f;
            var textWidth = _textRenderer.MeasureText(button.Label, fontSize);
            var textX = button.Bounds.X + (button.Bounds.Width - textWidth) * 0.5f;
            var textY = button.Bounds.Y + (button.Bounds.Height - fontSize) * 0.5f;
            var textColor = button.Enabled
                ? (0.87f, 0.90f, 0.93f, 1f)
                : (0.49f, 0.55f, 0.61f, 1f);
            AddText(button.Label, textX, textY, textColor, fontSize);
        }

        if (_vertices.Count == 0)
        {
            return;
        }

        var vertexData = _vertices.ToArray();
        GL.BindBuffer(BufferTarget.ArrayBuffer, _vertexBuffer);
        GL.BufferData(
            BufferTarget.ArrayBuffer,
            vertexData.Length * sizeof(float),
            vertexData,
            BufferUsageHint.DynamicDraw);

        GL.Disable(EnableCap.DepthTest);
        GL.Disable(EnableCap.CullFace);
        GL.Enable(EnableCap.Blend);
        GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
        GL.UseProgram(_program);
        GL.Uniform2(_viewportLocation, (float)Math.Max(1, width), (float)Math.Max(1, height));
        GL.BindVertexArray(_vao);
        GL.DrawArrays(PrimitiveType.Triangles, 0, vertexData.Length / VertexStride);
        _textRenderer.Render(width, height);
        GL.Disable(EnableCap.Blend);
        GL.Enable(EnableCap.CullFace);
        GL.Enable(EnableCap.DepthTest);
    }

    private HudButton[] CreateButtons(
        int viewportWidth,
        bool paused,
        bool diagnosticsEnabled,
        bool datasetCaptureEnabled,
        bool canCaptureFrame,
        bool datasetCaptureAvailable)
    {
        var buttons = new[]
        {
            new HudButton(paused ? "RESUME" : "PAUSE", SimulationControlAction.TogglePause, true),
            new HudButton("RESET", SimulationControlAction.Reset, true),
            new HudButton(diagnosticsEnabled ? "TRACE ON" : "TRACE OFF", SimulationControlAction.ToggleDiagnostics, true),
            new HudButton("SAVE PNG", SimulationControlAction.CaptureFrame, canCaptureFrame),
            new HudButton(
                datasetCaptureAvailable
                    ? datasetCaptureEnabled ? "CAPTURE ON" : "CAPTURE OFF"
                    : "MODEL",
                SimulationControlAction.ToggleDatasetCapture,
                datasetCaptureAvailable),
            new HudButton("SCI VIEW", SimulationControlAction.CycleVisualization, true),
            new HudButton("DASHBOARD", SimulationControlAction.ToggleDashboard, true),
            new HudButton("REPORT", SimulationControlAction.OpenReport, true),
        };
        var result = new HudButton[buttons.Length];
        var x = 16f;
        var y = 16f;
        var maximumX = Math.Max(16f, viewportWidth - 16f);
        for (var i = 0; i < buttons.Length; i++)
        {
            var item = buttons[i];
            var textWidth = _textRenderer.MeasureText(item.Label, 14f);
            var buttonWidth = textWidth + HorizontalPadding * 2f;
            if (x + buttonWidth > maximumX && x > 16f)
            {
                x = 16f;
                y += ButtonHeight + ButtonGap;
            }
            result[i] = item with
            {
                Bounds = new HudRectangle(x, y, buttonWidth, ButtonHeight),
            };
            x += buttonWidth + ButtonGap;
        }
        return result;
    }

    private void AddText(string text, float x, float y, (float R, float G, float B, float A) color, float pixelHeight)
    {
        _textRenderer.AddText(text, x, y, color, pixelHeight);
    }

    private void AddBorder(HudRectangle bounds, (float R, float G, float B, float A) color)
    {
        const float thickness = 1f;
        AddRectangle(new HudRectangle(bounds.X, bounds.Y, bounds.Width, thickness), color);
        AddRectangle(new HudRectangle(bounds.X, bounds.Bottom - thickness, bounds.Width, thickness), color);
        AddRectangle(new HudRectangle(bounds.X, bounds.Y, thickness, bounds.Height), color);
        AddRectangle(new HudRectangle(bounds.Right - thickness, bounds.Y, thickness, bounds.Height), color);
    }

    private void AddRectangle(HudRectangle bounds, (float R, float G, float B, float A) color)
    {
        AddVertex(bounds.X, bounds.Y, color);
        AddVertex(bounds.Right, bounds.Y, color);
        AddVertex(bounds.Right, bounds.Bottom, color);
        AddVertex(bounds.X, bounds.Y, color);
        AddVertex(bounds.Right, bounds.Bottom, color);
        AddVertex(bounds.X, bounds.Bottom, color);
    }

    private void AddVertex(float x, float y, (float R, float G, float B, float A) color)
    {
        _vertices.Add(x);
        _vertices.Add(y);
        _vertices.Add(color.R);
        _vertices.Add(color.G);
        _vertices.Add(color.B);
        _vertices.Add(color.A);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        GL.DeleteBuffer(_vertexBuffer);
        GL.DeleteVertexArray(_vao);
        GL.DeleteProgram(_program);
        _textRenderer.Dispose();
        _disposed = true;
    }

    private sealed record HudButton(
        string Label,
        SimulationControlAction Action,
        bool Enabled,
        HudRectangle Bounds = default);

    private readonly record struct HudRectangle(float X, float Y, float Width, float Height)
    {
        public float Right => X + Width;
        public float Bottom => Y + Height;

        public bool Contains(Vector2 position)
        {
            return position.X >= X && position.X <= Right
                && position.Y >= Y && position.Y <= Bottom;
        }
    }

}

internal sealed class MouseButtonEdgeDetector
{
    private bool _wasDown;

    public bool WasPressed(bool isDown)
    {
        var wasPressed = isDown && !_wasDown;
        _wasDown = isDown;
        return wasPressed;
    }
}
