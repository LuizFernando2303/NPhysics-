using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;

namespace NPhysics;

internal sealed class GpuModelProbeWindow : GameWindow
{
    private readonly string _modelPath;
    private GpuModelWeights? _weights;
    private int _probeProgram;
    private int _inputBuffer;
    private int _outputBuffer;

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
            var layer = _weights.GetLayer("node_encoder");
            var layerParameters = model.Layers.Single(item => item.Name == layer.Name);

            _probeProgram = GlProgram.CreateCompute(ShaderSource.Load("Neural/LinearProbe.comp"));
            var input = new float[layer.InputSize];
            for (var i = 0; i < input.Length; i++)
            {
                input[i] = (i - input.Length / 2) * 0.03125f;
            }
            var output = new float[layer.OutputSize];
            _inputBuffer = CreateBuffer(input);
            _outputBuffer = CreateBuffer(output);
            _weights.Bind(0);
            GL.BindBufferBase(BufferRangeTarget.ShaderStorageBuffer, 1, _inputBuffer);
            GL.BindBufferBase(BufferRangeTarget.ShaderStorageBuffer, 2, _outputBuffer);

            GL.UseProgram(_probeProgram);
            SetUniform("uInputSize", (uint)layer.InputSize);
            SetUniform("uOutputSize", (uint)layer.OutputSize);
            SetUniform("uWeightOffset", (uint)layer.WeightFloatOffset);
            SetUniform("uBiasOffset", (uint)layer.BiasFloatOffset);
            GL.DispatchCompute((layer.OutputSize + 63) / 64, 1, 1);
            GL.MemoryBarrier(
                MemoryBarrierFlags.ShaderStorageBarrierBit
                | MemoryBarrierFlags.BufferUpdateBarrierBit);

            GL.BindBuffer(BufferTarget.ShaderStorageBuffer, _outputBuffer);
            GL.GetBufferSubData(
                BufferTarget.ShaderStorageBuffer,
                IntPtr.Zero,
                output.Length * sizeof(float),
                output);
            var maxError = 0f;
            for (var row = 0; row < output.Length; row++)
            {
                if (!float.IsFinite(output[row]))
                {
                    throw new InvalidDataException("Compute shader produziu NaN ou Infinity.");
                }
                var expected = layerParameters.Bias[row];
                var weightOffset = row * layer.InputSize;
                for (var column = 0; column < layer.InputSize; column++)
                {
                    expected += layerParameters.Weights[weightOffset + column] * input[column];
                }
                maxError = MathF.Max(maxError, MathF.Abs(output[row] - expected));
            }
            if (maxError > 1e-4f)
            {
                throw new InvalidOperationException(
                    $"GPU model probe falhou: erro maximo {maxError:G8} excede 1e-4.");
            }

            Console.WriteLine(
                $"GPU model probe aprovado: {layer.Name}, {layer.InputSize}->{layer.OutputSize}, "
                + $"{_weights.ByteSize:N0} bytes SSBO, erro maximo={maxError:G8}.");
        }
        finally
        {
            Close();
        }
    }

    protected override void OnUnload()
    {
        if (_inputBuffer != 0) GL.DeleteBuffer(_inputBuffer);
        if (_outputBuffer != 0) GL.DeleteBuffer(_outputBuffer);
        if (_probeProgram != 0) GL.DeleteProgram(_probeProgram);
        _weights?.Dispose();
        base.OnUnload();
    }

    private static int CreateBuffer(float[] values)
    {
        var buffer = GL.GenBuffer();
        GL.BindBuffer(BufferTarget.ShaderStorageBuffer, buffer);
        GL.BufferData(
            BufferTarget.ShaderStorageBuffer,
            values.Length * sizeof(float),
            values,
            BufferUsageHint.DynamicDraw);
        return buffer;
    }

    private void SetUniform(string name, uint value)
    {
        var location = GL.GetUniformLocation(_probeProgram, name);
        if (location < 0)
        {
            throw new InvalidOperationException($"Uniform obrigatorio nao encontrado: {name}.");
        }
        GL.Uniform1(location, value);
    }
}
