using System.Diagnostics;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.GraphicsLibraryFramework;

namespace NPhysics;

internal sealed class PhysicsSimulation : GameWindow
{
    private const float FixedStep = SimulationSettings.FixedStep;
    private const float CameraSpeed = 5.5f;

    private readonly ProceduralTrack _track = new();
    private readonly GpuPhysics _physics = new();
    private readonly Random _random = new(SimulationSettings.Seed);
    private readonly string? _neuralModelPath;
    private readonly int? _maximumPhysicsSteps;
    private int _totalPhysicsSteps;
    private SceneRenderer? _renderer;
    private SimulationHud? _hud;
    private SimulationDashboard? _dashboard;
    private SimulationTrace? _trace;
    private SimulationFrameCapture? _frameCapture;
    private float _cameraX;
    private readonly SimulationClock _simulationClock =
        new(SimulationSettings.FixedStep, maximumStepsPerFrame: 8);
    private double _simulationTimeSeconds;
    private double _frameCaptureTimeSeconds;
    private int _automaticFrameCaptureCount;
    private bool _manualFrameCaptureRequested;
    private bool _showDashboard;
    private GpuTelemetrySample? _latestTelemetry;
    private bool _paused;
    private int _framesInWindow;
    private int _physicsStepsInWindow;
    private double _frameMillisecondsInWindow;
    private double _physicsSubmissionMillisecondsInWindow;
    private double _renderSubmissionMillisecondsInWindow;

    public PhysicsSimulation(
        string? neuralModelPath = null,
        int? maximumPhysicsSteps = null,
        SimulationPresentation presentation = SimulationPresentation.Dashboard)
        : base(GameWindowSettings.Default, new NativeWindowSettings
        {
            ClientSize = new Vector2i(1440, 900),
            Title = presentation == SimulationPresentation.Dashboard
                ? "NPhysics | Simulation Monitor"
                : "NPhysics | 3D Scene",
            APIVersion = new Version(4, 3),
            Profile = ContextProfile.Core,
        })
    {
        _neuralModelPath = neuralModelPath;
        _showDashboard = presentation == SimulationPresentation.Dashboard;
        if (maximumPhysicsSteps <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumPhysicsSteps));
        }
        _maximumPhysicsSteps = maximumPhysicsSteps;
    }

    protected override void OnLoad()
    {
        base.OnLoad();
        GL.ClearColor(0.035f, 0.051f, 0.071f, 1f);
        GL.Enable(EnableCap.DepthTest);
        GL.Enable(EnableCap.CullFace);
        GL.CullFace(TriangleFace.Back);
        GL.Viewport(0, 0, ClientSize.X, ClientSize.Y);

        try
        {
            _physics.Initialize(_track, CreateRandomSource(), _neuralModelPath);
        }
        catch (NotSupportedException exception)
        {
            Console.Error.WriteLine($"Hardware ou driver incompativel: {exception.Message}");
            Close();
            return;
        }
        _hud = new SimulationHud();
        _dashboard = new SimulationDashboard();
        if (!_showDashboard)
        {
            InitializeSceneRenderer();
        }
        _trace = new SimulationTrace(
            GL.GetString(StringName.Vendor) ?? "unknown",
            GL.GetString(StringName.Renderer) ?? "unknown",
            GL.GetString(StringName.Version) ?? "unknown",
            _physics.DetailedCountersAvailable,
            _physics.IsModelBackend ? "neural-experimental" : "teacher",
            _neuralModelPath);
        _frameCapture = new SimulationFrameCapture(_trace.FilePath);
        Console.WriteLine($"Rastreamento da simulacao: {_trace.FilePath}");
        if (_physics.IsModelBackend)
        {
            Console.WriteLine("Backend neural ativo; dados do dataset Teacher desativados nesta sessao.");
        }
        Console.WriteLine("Capturas da apresentacao: automaticas no inicio e sob demanda com F4.");
        _cameraX = -8f;
    }

    protected override void OnResize(ResizeEventArgs e)
    {
        base.OnResize(e);
        GL.Viewport(0, 0, Math.Max(1, e.Width), Math.Max(1, e.Height));
    }

    protected override void OnUpdateFrame(FrameEventArgs e)
    {
        base.OnUpdateFrame(e);
        ReadTelemetry();
        var dashboardState = GetDashboardState();
        var mouseAction = _showDashboard
            ? _dashboard?.Update(
                MousePosition,
                MouseState.IsButtonPressed(MouseButton.Button1),
                ClientSize.X,
                ClientSize.Y,
                dashboardState,
                _frameCapture?.CanCapture == true,
                IsFocused)
            : _hud?.Update(
                MousePosition,
                MouseState.IsButtonPressed(MouseButton.Button1),
                ClientSize.X,
                _paused,
                _trace?.IsEnabled == true,
                _physics.IsDatasetCaptureEnabled,
                _frameCapture?.CanCapture == true,
                IsFocused,
                datasetCaptureAvailable: !_physics.IsModelBackend);
        if (IsFocused)
        {
            _frameCaptureTimeSeconds += Math.Min(e.Time, 1.0 / 20.0);
            HandleKeyboardControls();
            if (mouseAction is { } action)
            {
                HandleControlAction(action);
            }
        }

        var frameTime = Math.Min((float)e.Time, 1f / 20f);
        var stepCount = _simulationClock.ConsumeSteps(frameTime);
        var physicsStart = Stopwatch.GetTimestamp();
        for (var step = 0; step < stepCount; step++)
        {
            _physics.Step(FixedStep);
            _totalPhysicsSteps++;
            if (_maximumPhysicsSteps is { } maximumSteps && _totalPhysicsSteps >= maximumSteps)
            {
                Close();
                break;
            }
        }
        _simulationTimeSeconds = _simulationClock.SimulationTimeSeconds;
        var frameTimeForCamera = _paused ? 0f : frameTime;

        if (stepCount > 0)
        {
            _physicsSubmissionMillisecondsInWindow += Stopwatch.GetElapsedTime(physicsStart).TotalMilliseconds;
            _physicsStepsInWindow += stepCount;
        }

        _cameraX += frameTimeForCamera * CameraSpeed;
        _physics.UpdateObstacles(_track, _cameraX);
    }

    protected override void OnRenderFrame(FrameEventArgs e)
    {
        base.OnRenderFrame(e);
        GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
        var renderStart = Stopwatch.GetTimestamp();
        if (_showDashboard)
        {
            _dashboard?.Render(
                ClientSize.X,
                ClientSize.Y,
                MousePosition,
                GetDashboardState(),
                _frameCapture?.CanCapture == true);
        }
        else
        {
            RenderScene();
            _hud?.Render(
                ClientSize.X,
                ClientSize.Y,
                MousePosition,
                _paused,
                _trace?.IsEnabled == true,
                _physics.IsDatasetCaptureEnabled,
                _frameCapture?.CanCapture == true,
                datasetCaptureAvailable: !_physics.IsModelBackend);
        }
        CaptureFrameIfRequested();
        SwapBuffers();
        _renderSubmissionMillisecondsInWindow += Stopwatch.GetElapsedTime(renderStart).TotalMilliseconds;
        _frameMillisecondsInWindow += e.Time * 1000.0;
        _framesInWindow++;

        var frameWindow = new TelemetryFrameWindow(
            _framesInWindow,
            _physicsStepsInWindow,
            _physics.GetObstacles().Length,
            _cameraX,
            IsFocused,
            _paused,
            _simulationClock.SimulationTimeSeconds,
            _simulationClock.DroppedTimeSeconds,
            _simulationClock.AccumulatorSeconds,
            _framesInWindow == 0 ? 0 : _frameMillisecondsInWindow / _framesInWindow,
            _framesInWindow == 0 ? 0 : _physicsSubmissionMillisecondsInWindow / _framesInWindow,
            _framesInWindow == 0 ? 0 : _renderSubmissionMillisecondsInWindow / _framesInWindow);
        if (_physics.TryScheduleTelemetrySnapshot(frameWindow))
        {
            _framesInWindow = 0;
            _physicsStepsInWindow = 0;
            _frameMillisecondsInWindow = 0;
            _physicsSubmissionMillisecondsInWindow = 0;
            _renderSubmissionMillisecondsInWindow = 0;
        }
    }

    protected override void OnUnload()
    {
        _renderer?.Dispose();
        _hud?.Dispose();
        _dashboard?.Dispose();
        _physics.Dispose();
        _trace?.Dispose();
        base.OnUnload();
    }

    private void ResetSimulation()
    {
        _cameraX = -8f;
        _latestTelemetry = null;
        _dashboard?.ResetHistory();
        _paused = false;
        _simulationClock.SetPaused(false);
        _simulationClock.Reset();
        _simulationTimeSeconds = 0d;
        _physics.Reset(_track, CreateRandomSource());
        _physics.UpdateObstacles(_track, _cameraX, force: true);
    }

    private Random CreateRandomSource()
    {
        return new Random(_random.Next());
    }

    private void HandleKeyboardControls()
    {
        if (KeyboardState.IsKeyPressed(Keys.Space))
        {
            HandleControlAction(SimulationControlAction.TogglePause);
        }
        if (KeyboardState.IsKeyPressed(Keys.R))
        {
            HandleControlAction(SimulationControlAction.Reset);
        }
        if (KeyboardState.IsKeyPressed(Keys.F3))
        {
            HandleControlAction(SimulationControlAction.ToggleDiagnostics);
        }
        if (KeyboardState.IsKeyPressed(Keys.F4))
        {
            HandleControlAction(SimulationControlAction.CaptureFrame);
        }
        if (KeyboardState.IsKeyPressed(Keys.F5))
        {
            HandleControlAction(SimulationControlAction.ToggleDatasetCapture);
        }
        if (KeyboardState.IsKeyPressed(Keys.V))
        {
            HandleControlAction(SimulationControlAction.CycleVisualization);
        }
        if (KeyboardState.IsKeyPressed(Keys.F6))
        {
            HandleControlAction(SimulationControlAction.ToggleDashboard);
        }
    }

    private void HandleControlAction(SimulationControlAction action)
    {
        switch (action)
        {
            case SimulationControlAction.TogglePause:
                _paused = !_paused;
                _simulationClock.SetPaused(_paused);
                _trace?.WriteEvent(_paused ? "paused" : "resumed", _cameraX, _paused);
                break;
            case SimulationControlAction.Reset:
                ResetSimulation();
                _trace?.WriteEvent("reset", _cameraX, _paused);
                break;
            case SimulationControlAction.ToggleDiagnostics:
                if (_trace is null)
                {
                    return;
                }
                _trace.SetEnabled(!_trace.IsEnabled);
                _physics.SetDiagnosticsEnabled(_trace.IsEnabled);
                Console.WriteLine(
                    $"Rastreamento {(_trace.IsEnabled ? "ativado" : "pausado")}: {_trace.FilePath}");
                break;
            case SimulationControlAction.CaptureFrame:
                if (_frameCapture?.CanCapture == true)
                {
                    _manualFrameCaptureRequested = true;
                }
                break;
            case SimulationControlAction.ToggleDatasetCapture:
                var enabled = !_physics.IsDatasetCaptureEnabled;
                try
                {
                    _physics.SetDatasetCaptureEnabled(enabled);
                }
                catch (Exception exception) when (
                    exception is NotSupportedException
                        or OutOfMemoryException
                        or InvalidOperationException
                        or OverflowException)
                {
                    Console.Error.WriteLine($"Nao foi possivel ativar a captura do dataset: {exception.Message}");
                    return;
                }
                Console.WriteLine(
                    $"Captura dataset {(enabled ? "ativada" : "pausada")}; "
                    + $"amostras={_physics.DatasetSampleCount:N0}");
                break;
            case SimulationControlAction.CycleVisualization:
                _renderer?.CycleVisualizationMode();
                if (_renderer is not null)
                {
                    Console.WriteLine($"Visualizacao: {_renderer.VisualizationLabel}");
                    _trace?.WriteEvent(
                        $"visualization-{_renderer.VisualizationLabel}",
                        _cameraX,
                        _paused);
                }
                break;
            case SimulationControlAction.ToggleDashboard:
                _showDashboard = !_showDashboard;
                Title = _showDashboard
                    ? "NPhysics | Simulation Monitor"
                    : "NPhysics | 3D Scene";
                if (_showDashboard)
                {
                    _renderer?.Dispose();
                    _renderer = null;
                }
                else
                {
                    InitializeSceneRenderer();
                }
                _trace?.WriteEvent(
                    _showDashboard ? "dashboard-view" : "scene-view",
                    _cameraX,
                    _paused);
                break;
            case SimulationControlAction.OpenReport:
                OpenLatestReport();
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(action), action, "Acao de interface desconhecida.");
        }
    }

    private static void OpenLatestReport()
    {
        var outputDirectory = DatasetCaptureSettings.DefaultOutputDirectory;
        if (!Directory.Exists(outputDirectory))
        {
            Console.WriteLine($"Pasta de datasets ainda nao existe: {outputDirectory}");
            return;
        }

        try
        {
            var report = Directory.EnumerateFiles(outputDirectory, "*.report.html")
                .Concat(Directory.EnumerateFiles(outputDirectory, "*.training.html"))
                .Select(path => new FileInfo(path))
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .FirstOrDefault();
            if (report is null)
            {
                Console.WriteLine(
                    $"Nenhum relatorio encontrado em {outputDirectory}. "
                    + "Gere um com --dataset-report ou treine um dataset.");
                return;
            }

            using var process = Process.Start(new ProcessStartInfo(report.FullName)
            {
                UseShellExecute = true,
            });
            if (process is null)
            {
                Console.Error.WriteLine($"Nao foi possivel abrir o relatorio {report.FullName}.");
                return;
            }
            Console.WriteLine($"Relatorio aberto: {report.FullName}");
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or InvalidOperationException
                or System.ComponentModel.Win32Exception)
        {
            Console.Error.WriteLine($"Falha ao abrir relatorio: {exception.Message}");
        }
    }

    private void ReadTelemetry()
    {
        while (_physics.TryReadTelemetry(out var sample))
        {
            _latestTelemetry = sample;
            _dashboard?.AddTelemetry(sample);
            _trace?.WriteSample(sample);
        }
    }

    private SimulationDashboardState GetDashboardState()
    {
        return new SimulationDashboardState(
            SimulationSettings.DiskCount,
            _totalPhysicsSteps,
            _simulationClock.SimulationTimeSeconds,
            _paused,
            _trace?.IsEnabled == true,
            _physics.IsDatasetCaptureEnabled,
            _physics.DatasetSampleCount,
            _physics.DetailedCountersAvailable,
            _physics.IsModelBackend,
            _latestTelemetry);
    }

    private void InitializeSceneRenderer()
    {
        _renderer = new SceneRenderer(_track, _physics);
        _renderer.Initialize();
    }

    private void RenderScene()
    {
        var targetX = _cameraX + 5f;
        var target = new Vector3(targetX, _track.Height(targetX, 0f) + 0.8f, 0f);
        var eye = target + _track.SurfaceNormal(targetX, 0f) * 26f + new Vector3(0f, 0f, 8f);
        var cameraForward = (target - eye).Normalized();
        var cameraRight = Vector3.Cross(cameraForward, Vector3.UnitY).Normalized();
        var cameraUp = Vector3.Cross(cameraRight, cameraForward).Normalized();
        var view = Matrix4.LookAt(eye, target, Vector3.UnitY);
        var projection = Matrix4.CreatePerspectiveFieldOfView(
            MathHelper.DegreesToRadians(50f),
            (float)Math.Max(1, ClientSize.X) / Math.Max(1, ClientSize.Y),
            0.1f,
            180f);
        _renderer?.Render(view * projection, _cameraX, cameraRight, cameraUp);
    }

    private void CaptureFrameIfRequested()
    {
        if (_frameCapture is null || _trace is null || !_frameCapture.CanCapture)
        {
            _manualFrameCaptureRequested = false;
            return;
        }

        var automaticCaptureDue = _trace.IsEnabled
            && _automaticFrameCaptureCount < 5
            && _frameCaptureTimeSeconds >= (_automaticFrameCaptureCount + 1);
        if (!_manualFrameCaptureRequested && !automaticCaptureDue)
        {
            return;
        }

        _manualFrameCaptureRequested = false;
        var imagePath = _frameCapture.Capture(ClientSize.X, ClientSize.Y);
        if (automaticCaptureDue)
        {
            _automaticFrameCaptureCount++;
        }

        _trace.WriteFrameCapture(
            _frameCapture.CaptureCount,
            imagePath,
            ClientSize.X,
            ClientSize.Y,
            _simulationTimeSeconds,
            _cameraX,
            _paused,
            IsFocused);
        Console.WriteLine(
            $"Frame salvo: {imagePath} (t={_simulationTimeSeconds:0.00}s, cameraX={_cameraX:0.00}).");
    }
}
