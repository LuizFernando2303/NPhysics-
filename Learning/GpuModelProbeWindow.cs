using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;

namespace NPhysics;

internal sealed class GpuModelProbeWindow : GameWindow
{
    private readonly string _modelPath;
    private GpuModelWeights? _weights;
    private GpuGraphInference? _inference;

    public GpuModelProbeWindow(string modelPath)
        : base(GameWindowSettings.Default, new NativeWindowSettings
        {
            ClientSize = new Vector2i(320, 240),
            Title = "NPhysics - GPU model probe",
            APIVersion = new Version(4, 3),
            Profile = ContextProfile.Core,
        })
    {
        _modelPath = modelPath;
    }

    protected override void OnLoad()
    {
        base.OnLoad();
        try
        {
            var model = ModelSerializer.Load(_modelPath);
            _weights = new GpuModelWeights(model);
            _inference = new GpuGraphInference(_weights);

            var nodeCount = 3;
            var nodeInputs = new float[nodeCount * GraphNetwork.NodeInputSize];
            for (var i = 0; i < nodeInputs.Length; i++)
            {
                nodeInputs[i] = MathF.Sin(i * 0.17f) * 0.2f;
            }
            var edgeCounts = new[] { 2, 1, 0 };
            var edgeTargets = new[] { 1, 2, 0 };
            var edgeFeatures = new float[edgeTargets.Length * GraphNetwork.EdgeInputSize];
            for (var i = 0; i < edgeFeatures.Length; i++)
            {
                edgeFeatures[i] = MathF.Cos(i * 0.11f) * 0.15f;
            }

            var reference = new GraphNetwork(seed: 0);
            reference.LoadParameters(model);
            var expected = reference.Forward(nodeInputs, edgeFeatures, edgeCounts, edgeTargets);
            var actual = _inference.Forward(nodeInputs, edgeFeatures, edgeCounts, edgeTargets);
            var maximumError = 0f;
            var squaredError = 0d;
            for (var i = 0; i < expected.Length; i++)
            {
                var error = MathF.Abs(expected[i] - actual[i]);
                maximumError = MathF.Max(maximumError, error);
                squaredError += error * error;
            }
            var rootMeanSquaredError = Math.Sqrt(squaredError / expected.Length);
            const float tolerance = 5e-4f;
            if (maximumError > tolerance)
            {
                throw new InvalidOperationException(
                    $"GNN GPU probe falhou: erro maximo {maximumError:G8} excede {tolerance:G8}.");
            }

            Console.WriteLine(
                $"GNN GPU probe aprovado: nos={nodeCount}, arestas={edgeTargets.Length}, "
                + $"pesos={_weights.ByteSize:N0} bytes, erro maximo={maximumError:G8}, "
                + $"RMSE={rootMeanSquaredError:G8}.");
        }
        finally
        {
            Close();
        }
    }

    protected override void OnUnload()
    {
        _inference?.Dispose();
        _weights?.Dispose();
        base.OnUnload();
    }
}
