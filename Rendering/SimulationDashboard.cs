using System.Globalization;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;

namespace NPhysics;

internal readonly record struct SimulationDashboardState(
    int DiskCount,
    int PhysicsStepCount,
    double SimulationTimeSeconds,
    bool Paused,
    bool DiagnosticsEnabled,
    bool DatasetCaptureEnabled,
    long DatasetSampleCount,
    bool DetailedCountersAvailable,
    bool IsModelBackend,
    GpuTelemetrySample? Telemetry);

internal sealed class SimulationDashboard : IDisposable
{
    private const int VertexStride = 6;
    private const float Margin = 24f;
    private static readonly (float R, float G, float B, float A) Background = (0.035f, 0.051f, 0.071f, 1f);
    private static readonly (float R, float G, float B, float A) Panel = (0.063f, 0.082f, 0.110f, 1f);
    private static readonly (float R, float G, float B, float A) PanelRaised = (0.078f, 0.102f, 0.133f, 1f);
    private static readonly (float R, float G, float B, float A) Border = (0.145f, 0.184f, 0.231f, 1f);
    private static readonly (float R, float G, float B, float A) Primary = (0.890f, 0.925f, 0.957f, 1f);
    private static readonly (float R, float G, float B, float A) Secondary = (0.553f, 0.620f, 0.690f, 1f);
    private static readonly (float R, float G, float B, float A) Accent = (0.250f, 0.690f, 0.675f, 1f);
    private static readonly (float R, float G, float B, float A) Warning = (0.890f, 0.620f, 0.290f, 1f);

    private readonly int _program;
    private readonly int _vao;
    private readonly int _vertexBuffer;
    private readonly int _viewportLocation;
    private readonly SimulationTextRenderer _textRenderer;
    private readonly List<float> _vertices = [];
    private readonly List<double> _simulationMilliseconds = [];
    private readonly MouseButtonEdgeDetector _leftButtonEdge = new();
    private bool _disposed;

    public SimulationDashboard()
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
            throw new InvalidOperationException("Required dashboard shader uniform was not found: uViewport.");
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
        int height,
        SimulationDashboardState state,
        bool canCaptureFrame,
        bool allowActions)
    {
        if (!_leftButtonEdge.WasPressed(leftButtonPressed) || !allowActions)
        {
            return null;
        }

        foreach (var button in CreateButtons(width, height, state, canCaptureFrame))
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
        SimulationDashboardState state,
        bool canCaptureFrame)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var viewportWidth = Math.Max(1, width);
        var viewportHeight = Math.Max(1, height);
        _vertices.Clear();
        _textRenderer.BeginFrame();

        AddRectangle(new Rectangle(0, 0, viewportWidth, viewportHeight), Background);
        DrawHeader(viewportWidth, state);
        DrawSummaryCards(viewportWidth, state);
        DrawPerformancePanel(viewportWidth, state);
        DrawContactsPanel(viewportWidth, state);
        DrawPerformanceHistory(viewportWidth, viewportHeight);
        DrawControls(
            viewportWidth,
            viewportHeight,
            cursorPosition,
            state,
            canCaptureFrame);

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
        GL.Uniform2(_viewportLocation, (float)viewportWidth, (float)viewportHeight);
        GL.BindVertexArray(_vao);
        GL.DrawArrays(PrimitiveType.Triangles, 0, vertexData.Length / VertexStride);
        _textRenderer.Render(viewportWidth, viewportHeight);
        GL.Disable(EnableCap.Blend);
        GL.Enable(EnableCap.CullFace);
        GL.Enable(EnableCap.DepthTest);
    }

    private void DrawHeader(int width, SimulationDashboardState state)
    {
        AddRectangle(new Rectangle(0, 0, width, 70), Panel);
        AddRectangle(new Rectangle(Margin, 18, 3, 36), Accent);
        AddText("NPHYSICS / SIMULATION MONITOR", Margin + 16, 17, Primary, 2f);
        AddText(
            state.IsModelBackend ? "MODEL-DRIVEN GPU SOLVER" : "RIGID-BODY GPU SOLVER",
            Margin + 17,
            43,
            Secondary,
            1f);

        var status = state.Paused ? "PAUSED" : "RUNNING";
        var statusWidth = TextWidth(status, 1.2f) + 24f;
        var statusBounds = new Rectangle(width - Margin - statusWidth, 20, statusWidth, 28);
        AddRectangle(statusBounds, PanelRaised);
        AddRectangle(
            new Rectangle(statusBounds.X + 10, statusBounds.Y + 11, 6, 6),
            state.Paused ? Warning : Accent);
        AddText(status, statusBounds.X + 23, statusBounds.Y + 10, Primary, 1.2f);
        AddText(
            $"DATA CAPTURE {(state.DatasetCaptureEnabled ? "ON" : "OFF")}  |  "
                + $"{FormatCount(state.DatasetSampleCount)} SAMPLES  |  F6 TO OPEN 3D SCENE",
            Margin,
            76,
            Secondary,
            1f);
    }

    private void DrawSummaryCards(int width, SimulationDashboardState state)
    {
        const float top = 96f;
        const float cardHeight = 86f;
        const float gap = 12f;
        var cardWidth = (width - Margin * 2f - gap * 4f) / 5f;
        var telemetry = state.DiagnosticsEnabled ? state.Telemetry : null;
        var values = new[]
        {
            ("SIMULATION TIME", $"{FormatNumber(state.SimulationTimeSeconds, "0.00")} S"),
            ("SESSION STEPS", FormatCount(state.PhysicsStepCount)),
            ("ACTIVE BODIES", FormatCount(state.DiskCount)),
            ("PHYSICS RATE", telemetry is { IntervalSeconds: > 0d } sample
                ? $"{FormatNumber(sample.FrameWindow.PhysicsStepCount / sample.IntervalSeconds, "0")} STEP/S"
                : "--"),
            ("FIXED STEP", $"{FormatNumber(SimulationSettings.FixedStep * 1000d, "0.00")} MS"),
        };
        for (var index = 0; index < values.Length; index++)
        {
            var x = Margin + index * (cardWidth + gap);
            DrawPanel(new Rectangle(x, top, cardWidth, cardHeight));
            AddRectangle(new Rectangle(x, top, 3, cardHeight), index == 0 ? Accent : Border);
            AddText(values[index].Item1, x + 14, top + 14, Secondary, 1f);
            AddText(values[index].Item2, x + 14, top + 39, Primary, 2.2f);
        }
    }

    private void DrawPerformancePanel(int width, SimulationDashboardState state)
    {
        const float top = 194f;
        const float panelHeight = 174f;
        const float gap = 14f;
        var panelWidth = (width - Margin * 2f - gap) * 0.5f;
        var bounds = new Rectangle(Margin, top, panelWidth, panelHeight);
        DrawPanel(bounds);
        AddText("SOLVER PERFORMANCE", bounds.X + 16, bounds.Y + 15, Primary, 1.2f);
        AddText("LATEST ASYNCHRONOUS GPU SAMPLE", bounds.X + 16, bounds.Y + 33, Secondary, 0.9f);

        var telemetry = state.DiagnosticsEnabled ? state.Telemetry : null;
        var values = new[]
        {
            ("GPU SIMULATION", FormatMilliseconds(telemetry?.LastSimulationGpuMilliseconds)),
            ("GRID BUILD", FormatMilliseconds(telemetry?.LastGridBuildGpuMilliseconds)),
            ("GRID CLEAR", FormatMilliseconds(telemetry?.LastGridClearGpuMilliseconds)),
            ("CPU SUBMISSION", FormatMilliseconds(telemetry?.FrameWindow.AveragePhysicsSubmissionMilliseconds)),
            ("AVERAGE FRAME", FormatMilliseconds(telemetry?.FrameWindow.AverageFrameMilliseconds)),
            ("VIEW SUBMISSION", FormatMilliseconds(telemetry?.FrameWindow.AverageRenderSubmissionMilliseconds)),
        };
        DrawValueRows(bounds, values);
    }

    private void DrawContactsPanel(int width, SimulationDashboardState state)
    {
        const float top = 194f;
        const float panelHeight = 174f;
        const float gap = 14f;
        var panelWidth = (width - Margin * 2f - gap) * 0.5f;
        var bounds = new Rectangle(Margin + panelWidth + gap, top, panelWidth, panelHeight);
        DrawPanel(bounds);
        AddText("CONTACTS & VALIDATION", bounds.X + 16, bounds.Y + 15, Primary, 1.2f);
        var availabilityLabel = !state.DiagnosticsEnabled
            ? "DIAGNOSTICS PAUSED"
            : state.DetailedCountersAvailable
                ? "COUNTERS AVAILABLE"
                : "COUNTERS DISABLED ABOVE 4,096 BODIES";
        AddText(availabilityLabel, bounds.X + 16, bounds.Y + 33, Secondary, 0.9f);

        var telemetry = state.DiagnosticsEnabled ? state.Telemetry : null;
        var available = state.DetailedCountersAvailable && telemetry.HasValue;
        var values = new[]
        {
            ("CANDIDATE TESTS", available ? FormatCount(telemetry!.Value.CandidateChecks) : "N/A"),
            ("BODY CONTACTS", available ? FormatCount(telemetry!.Value.DiskContacts) : "N/A"),
            ("RAMP CONTACTS", available ? FormatCount(telemetry!.Value.RampContacts) : "N/A"),
            ("OBSTACLE CONTACTS", available ? FormatCount(telemetry!.Value.ObstacleContacts) : "N/A"),
            ("INVALID STATES", available ? FormatCount(telemetry!.Value.InvalidStates) : "N/A"),
            ("MAX SPEED", available ? $"{FormatNumber(telemetry!.Value.MaximumSpeed, "0.00")} M/S" : "N/A"),
            ("MAX ANGULAR SPEED", available
                ? $"{FormatNumber(telemetry!.Value.MaximumAngularSpeedRadiansPerSecond, "0.00")} RAD/S"
                : "N/A"),
        };
        DrawValueRows(bounds, values);
    }

    private void DrawPerformanceHistory(int width, int height)
    {
        var top = 382f;
        var panelHeight = Math.Max(80f, height - top - 78f);
        var bounds = new Rectangle(Margin, top, width - Margin * 2f, panelHeight);
        DrawPanel(bounds);
        AddText("GPU SIMULATION TIME", bounds.X + 16, bounds.Y + 15, Primary, 1.2f);
        AddText("MS / SAMPLE  |  1 SECOND TELEMETRY INTERVAL", bounds.X + 16, bounds.Y + 33, Secondary, 0.9f);

        var graph = new Rectangle(bounds.X + 16, bounds.Y + 55, bounds.Width - 32, Math.Max(16f, bounds.Height - 72));
        AddRectangle(new Rectangle(graph.X, graph.Bottom - 1, graph.Width, 1), Border);
        AddRectangle(new Rectangle(graph.X, graph.Y + graph.Height * 0.5f, graph.Width, 1), Border);
        if (_simulationMilliseconds.Count == 0 || graph.Width < 4f)
        {
            AddText("WAITING FOR GPU TELEMETRY", graph.X + 8, graph.Y + 12, Secondary, 1f);
            return;
        }

        var maximum = Math.Max(1d, _simulationMilliseconds.Max());
        var slotWidth = graph.Width / _simulationMilliseconds.Count;
        var barWidth = Math.Max(1f, slotWidth * 0.58f);
        for (var index = 0; index < _simulationMilliseconds.Count; index++)
        {
            var value = Math.Clamp(_simulationMilliseconds[index] / maximum, 0d, 1d);
            var barHeight = (float)Math.Max(2d, value * (graph.Height - 4f));
            var x = graph.X + index * slotWidth + (slotWidth - barWidth) * 0.5f;
            AddRectangle(new Rectangle(x, graph.Bottom - barHeight, barWidth, barHeight), Accent);
        }

        AddText($"PEAK {FormatNumber(maximum, "0.00")} MS", bounds.Right - 124f, bounds.Y + 15, Secondary, 0.9f);
    }

    private void DrawControls(
        int width,
        int height,
        Vector2 cursorPosition,
        SimulationDashboardState state,
        bool canCaptureFrame)
    {
        var controls = CreateButtons(width, height, state, canCaptureFrame);
        var footerTop = height - 58f;
        AddRectangle(new Rectangle(0, footerTop, width, 1), Border);
        foreach (var button in controls)
        {
            var hovered = button.Bounds.Contains(cursorPosition);
            AddRectangle(button.Bounds, !button.Enabled
                ? Panel
                : hovered ? (0.11f, 0.19f, 0.22f, 1f) : PanelRaised);
            AddRectangle(
                new Rectangle(button.Bounds.X, button.Bounds.Y, button.Bounds.Width, 1),
                hovered && button.Enabled ? Accent : Border);
            var labelWidth = TextWidth(button.Label, 1f);
            AddText(
                button.Label,
                button.Bounds.X + (button.Bounds.Width - labelWidth) * 0.5f,
                button.Bounds.Y + 13,
                button.Enabled ? Primary : Secondary,
                1f);
        }
    }

    private void DrawValueRows(
        Rectangle bounds,
        IReadOnlyList<(string Label, string Value)> values)
    {
        const float rowHeight = 19f;
        var top = bounds.Y + 55f;
        var columnGap = 18f;
        var columnWidth = (bounds.Width - 32f - columnGap) * 0.5f;
        for (var index = 0; index < values.Count; index++)
        {
            var column = index % 2;
            var row = index / 2;
            var x = bounds.X + 16f + column * (columnWidth + columnGap);
            var y = top + row * rowHeight;
            AddText(values[index].Label, x, y, Secondary, 0.85f);
            var valueWidth = TextWidth(values[index].Value, 0.95f);
            AddText(values[index].Value, x + columnWidth - valueWidth, y, Primary, 0.95f);
        }
    }

    private void DrawPanel(Rectangle bounds)
    {
        AddRectangle(bounds, Panel);
        AddRectangle(new Rectangle(bounds.X, bounds.Y, bounds.Width, 1), Border);
        AddRectangle(new Rectangle(bounds.X, bounds.Bottom - 1, bounds.Width, 1), Border);
        AddRectangle(new Rectangle(bounds.X, bounds.Y, 1, bounds.Height), Border);
        AddRectangle(new Rectangle(bounds.Right - 1, bounds.Y, 1, bounds.Height), Border);
    }

    private void AddText(
        string text,
        float x,
        float y,
        (float R, float G, float B, float A) color,
        float scale)
    {
        _textRenderer.AddText(text, x, y, color, 12f * scale);
    }

    private float TextWidth(string text, float scale)
    {
        return _textRenderer.MeasureText(text, 12f * scale);
    }

    private void AddRectangle(Rectangle bounds, (float R, float G, float B, float A) color)
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

    private DashboardButton[] CreateButtons(
        int width,
        int height,
        SimulationDashboardState state,
        bool canCaptureFrame)
    {
        var labels = new[]
        {
            (state.Paused ? "RESUME" : "PAUSE", SimulationControlAction.TogglePause, true),
            ("RESET", SimulationControlAction.Reset, true),
            (state.DatasetCaptureEnabled ? "DATA ON" : "DATA OFF", SimulationControlAction.ToggleDatasetCapture, !state.IsModelBackend),
            (state.DiagnosticsEnabled ? "TRACE ON" : "TRACE OFF", SimulationControlAction.ToggleDiagnostics, true),
            ("3D SCENE", SimulationControlAction.ToggleDashboard, true),
            ("REPORT", SimulationControlAction.OpenReport, true),
            ("SAVE FRAME", SimulationControlAction.CaptureFrame, canCaptureFrame),
        };
        return LayoutButtons(width, height, labels);
    }

    private DashboardButton[] LayoutButtons(
        int width,
        int height,
        IReadOnlyList<(string Label, SimulationControlAction Action, bool Enabled)> labels)
    {
        const float gap = 8f;
        const float buttonHeight = 34f;
        var totalTextWidth = labels.Sum(item => TextWidth(item.Label, 1f) + 24f);
        var totalWidth = totalTextWidth + gap * (labels.Count - 1);
        var x = Math.Max(Margin, (width - totalWidth) * 0.5f);
        var y = height - 51f;
        var result = new DashboardButton[labels.Count];
        for (var index = 0; index < labels.Count; index++)
        {
            var item = labels[index];
            var buttonWidth = TextWidth(item.Label, 1f) + 24f;
            result[index] = new DashboardButton(
                item.Label,
                item.Action,
                item.Enabled,
                new Rectangle(x, y, buttonWidth, buttonHeight));
            x += buttonWidth + gap;
        }

        return result;
    }

    private static string FormatCount(long value)
    {
        return value.ToString("N0", CultureInfo.InvariantCulture);
    }

    private static string FormatNumber(double value, string format)
    {
        return value.ToString(format, CultureInfo.InvariantCulture);
    }

    private static string FormatMilliseconds(double? value)
    {
        return value is { } milliseconds
            ? $"{FormatNumber(milliseconds, "0.00")} MS"
            : "--";
    }

    public void AddTelemetry(GpuTelemetrySample sample)
    {
        _simulationMilliseconds.Add(sample.LastSimulationGpuMilliseconds);
        if (_simulationMilliseconds.Count > 120)
        {
            _simulationMilliseconds.RemoveAt(0);
        }
    }

    public void ResetHistory()
    {
        _simulationMilliseconds.Clear();
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

    private sealed record DashboardButton(
        string Label,
        SimulationControlAction Action,
        bool Enabled,
        Rectangle Bounds);

    private readonly record struct Rectangle(float X, float Y, float Width, float Height)
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
