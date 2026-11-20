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
    OpenReport,
}

internal sealed class SimulationHud : IDisposable
{
    private const int VertexStride = 6;
    private const float ButtonHeight = 38f;
    private const float ButtonGap = 8f;
    private const float HorizontalPadding = 12f;
    private const float VerticalPadding = 10f;
    private const float GlyphScale = 2f;
    private const float GlyphAdvance = 12f;
    private const float GlyphHeight = 14f;

    private static readonly IReadOnlyDictionary<char, string[]> Glyphs = new Dictionary<char, string[]>
    {
        ['A'] = ["01110", "10001", "10001", "11111", "10001", "10001", "10001"],
        ['B'] = ["11110", "10001", "10001", "11110", "10001", "10001", "11110"],
        ['C'] = ["01111", "10000", "10000", "10000", "10000", "10000", "01111"],
        ['D'] = ["11110", "10001", "10001", "10001", "10001", "10001", "11110"],
        ['E'] = ["11111", "10000", "10000", "11110", "10000", "10000", "11111"],
        ['F'] = ["11111", "10000", "10000", "11110", "10000", "10000", "10000"],
        ['G'] = ["01111", "10000", "10000", "10111", "10001", "10001", "01111"],
        ['H'] = ["10001", "10001", "10001", "11111", "10001", "10001", "10001"],
        ['I'] = ["11111", "00100", "00100", "00100", "00100", "00100", "11111"],
        ['J'] = ["00111", "00010", "00010", "00010", "10010", "10010", "01100"],
        ['K'] = ["10001", "10010", "10100", "11000", "10100", "10010", "10001"],
        ['L'] = ["10000", "10000", "10000", "10000", "10000", "10000", "11111"],
        ['M'] = ["10001", "11011", "10101", "10101", "10001", "10001", "10001"],
        ['N'] = ["10001", "11001", "10101", "10011", "10001", "10001", "10001"],
        ['O'] = ["01110", "10001", "10001", "10001", "10001", "10001", "01110"],
        ['P'] = ["11110", "10001", "10001", "11110", "10000", "10000", "10000"],
        ['Q'] = ["01110", "10001", "10001", "10001", "10101", "10010", "01101"],
        ['R'] = ["11110", "10001", "10001", "11110", "10100", "10010", "10001"],
        ['S'] = ["01111", "10000", "10000", "01110", "00001", "00001", "11110"],
        ['T'] = ["11111", "00100", "00100", "00100", "00100", "00100", "00100"],
        ['U'] = ["10001", "10001", "10001", "10001", "10001", "10001", "01110"],
        ['V'] = ["10001", "10001", "10001", "10001", "10001", "01010", "00100"],
        ['W'] = ["10001", "10001", "10001", "10101", "10101", "10101", "01010"],
        ['X'] = ["10001", "10001", "01010", "00100", "01010", "10001", "10001"],
        ['Y'] = ["10001", "10001", "01010", "00100", "00100", "00100", "00100"],
        ['Z'] = ["11111", "00001", "00010", "00100", "01000", "10000", "11111"],
    };

    private readonly int _program;
    private readonly int _vao;
    private readonly int _vertexBuffer;
    private readonly int _viewportLocation;
    private readonly List<float> _vertices = [];
    private readonly MouseButtonEdgeDetector _leftButtonEdge = new();
    private bool _disposed;

    public SimulationHud()
    {
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
                ? (0.16f, 0.20f, 0.26f, 0.88f)
                : hovered
                    ? (0.13f, 0.43f, 0.37f, 0.98f)
                    : (0.08f, 0.15f, 0.22f, 0.92f);
            AddRectangle(button.Bounds, color);
            AddBorder(button.Bounds, hovered && button.Enabled
                ? (0.43f, 0.93f, 0.72f, 1f)
                : (0.28f, 0.41f, 0.52f, 1f));

            var textWidth = Math.Max(0, button.Label.Length * GlyphAdvance - (GlyphAdvance - GlyphScale));
            var textX = button.Bounds.X + (button.Bounds.Width - textWidth) * 0.5f;
            var textY = button.Bounds.Y + (button.Bounds.Height - GlyphHeight) * 0.5f;
            var textColor = button.Enabled
                ? (0.91f, 0.96f, 0.99f, 1f)
                : (0.52f, 0.59f, 0.66f, 1f);
            AddText(button.Label, textX, textY, textColor);
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
        GL.Disable(EnableCap.Blend);
        GL.Enable(EnableCap.CullFace);
        GL.Enable(EnableCap.DepthTest);
    }

    private static HudButton[] CreateButtons(
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
                    ? datasetCaptureEnabled ? "DATA ON" : "DATA OFF"
                    : "MODEL",
                SimulationControlAction.ToggleDatasetCapture,
                datasetCaptureAvailable),
            new HudButton("REPORT", SimulationControlAction.OpenReport, true),
        };
        var result = new HudButton[buttons.Length];
        var x = 16f;
        var y = 16f;
        var maximumX = Math.Max(16f, viewportWidth - 16f);
        for (var i = 0; i < buttons.Length; i++)
        {
            var item = buttons[i];
            var textWidth = Math.Max(0, item.Label.Length * GlyphAdvance - (GlyphAdvance - GlyphScale));
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

    private void AddText(string text, float x, float y, (float R, float G, float B, float A) color)
    {
        foreach (var character in text)
        {
            if (Glyphs.TryGetValue(character, out var glyph))
            {
                for (var row = 0; row < glyph.Length; row++)
                for (var column = 0; column < glyph[row].Length; column++)
                {
                    if (glyph[row][column] == '1')
                    {
                        AddRectangle(
                            new HudRectangle(
                                x + column * GlyphScale,
                                y + row * GlyphScale,
                                GlyphScale,
                                GlyphScale),
                            color);
                    }
                }
            }
            x += GlyphAdvance;
        }
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
