using OpenTK.Graphics.OpenGL4;

namespace NPhysics;

internal static class GlProgram
{
    public static int CreateCompute(string source)
    {
        var shader = Compile(ShaderType.ComputeShader, source);
        return Link([shader], "compute");
    }

    public static int CreateGraphics(string vertexSource, string fragmentSource)
    {
        var vertex = Compile(ShaderType.VertexShader, vertexSource);
        var fragment = Compile(ShaderType.FragmentShader, fragmentSource);
        return Link([vertex, fragment], "grafico");
    }

    private static int Compile(ShaderType type, string source)
    {
        var shader = GL.CreateShader(type);
        GL.ShaderSource(shader, source);
        GL.CompileShader(shader);
        GL.GetShader(shader, ShaderParameter.CompileStatus, out var status);
        if (status != 0)
        {
            return shader;
        }

        var log = GL.GetShaderInfoLog(shader);
        GL.DeleteShader(shader);
        throw new InvalidOperationException($"Falha ao compilar shader {type}: {log}");
    }

    private static int Link(int[] shaders, string description)
    {
        var program = GL.CreateProgram();
        foreach (var shader in shaders)
        {
            GL.AttachShader(program, shader);
        }

        GL.LinkProgram(program);
        GL.GetProgram(program, GetProgramParameterName.LinkStatus, out var status);
        foreach (var shader in shaders)
        {
            GL.DetachShader(program, shader);
            GL.DeleteShader(shader);
        }

        if (status != 0)
        {
            return program;
        }

        var log = GL.GetProgramInfoLog(program);
        GL.DeleteProgram(program);
        throw new InvalidOperationException($"Falha ao vincular shader {description}: {log}");
    }
}
