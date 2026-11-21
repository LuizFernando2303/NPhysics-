using OpenTK.Graphics.OpenGL4;
using StbTrueTypeNext;

namespace NPhysics;

internal sealed class SimulationTextRenderer : IDisposable
{
    private const int FontPixelHeight = 36;
    private const int AtlasColumns = 16;
    private const int CellWidth = 40;
    private const int CellHeight = 44;
    private const int FirstCharacter = 32;
    private const int LastCharacter = 126;
    private const int VertexStride = 8;

    private readonly STBFont _font;
    private readonly float _fontScale;
    private readonly Glyph[] _glyphs = new Glyph[LastCharacter - FirstCharacter + 1];
    private readonly List<float> _vertices = [];
    private readonly int _program;
    private readonly int _vao;
    private readonly int _vertexBuffer;
    private readonly int _viewportLocation;
    private readonly int _atlasLocation;
    private readonly int _texture;
    private float _ascent;
    private bool _disposed;

    public SimulationTextRenderer()
    {
        using var fontStream = typeof(SimulationTextRenderer).Assembly
            .GetManifestResourceStream("NPhysics.Rendering.Fonts.Roboto-Variable.ttf")
            ?? throw new InvalidOperationException("The embedded Roboto font resource was not found.");
        using var fontData = new MemoryStream();
        fontStream.CopyTo(fontData);
        _font = new STBFont(fontData.ToArray());
        _fontScale = _font.ScaleForPixelHeight(FontPixelHeight);
        var atlas = CreateAtlas();
        _texture = GL.GenTexture();
        GL.BindTexture(TextureTarget.Texture2D, _texture);
        GL.PixelStore(PixelStoreParameter.UnpackAlignment, 1);
        GL.TexImage2D(
            TextureTarget.Texture2D,
            0,
            PixelInternalFormat.R8,
            AtlasColumns * CellWidth,
            GetAtlasRows() * CellHeight,
            0,
            PixelFormat.Red,
            PixelType.UnsignedByte,
            atlas);
        GL.PixelStore(PixelStoreParameter.UnpackAlignment, 4);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);

        const string vertexShader = """
            #version 430 core
            layout(location = 0) in vec2 aPosition;
            layout(location = 1) in vec2 aTexCoord;
            layout(location = 2) in vec4 aColor;
            uniform vec2 uViewport;
            out vec2 vTexCoord;
            out vec4 vColor;
            void main()
            {
                vec2 clip = vec2(
                    (aPosition.x / uViewport.x) * 2.0 - 1.0,
                    1.0 - (aPosition.y / uViewport.y) * 2.0);
                gl_Position = vec4(clip, 0.0, 1.0);
                vTexCoord = aTexCoord;
                vColor = aColor;
            }
            """;
        const string fragmentShader = """
            #version 430 core
            in vec2 vTexCoord;
            in vec4 vColor;
            uniform sampler2D uFontAtlas;
            out vec4 fragmentColor;
            void main()
            {
                fragmentColor = vec4(vColor.rgb, vColor.a * texture(uFontAtlas, vTexCoord).r);
            }
            """;
        _program = GlProgram.CreateGraphics(vertexShader, fragmentShader);
        _viewportLocation = GL.GetUniformLocation(_program, "uViewport");
        _atlasLocation = GL.GetUniformLocation(_program, "uFontAtlas");
        if (_viewportLocation < 0 || _atlasLocation < 0)
        {
            throw new InvalidOperationException("Required text-rendering shader uniforms were not found.");
        }

        _vao = GL.GenVertexArray();
        _vertexBuffer = GL.GenBuffer();
        GL.BindVertexArray(_vao);
        GL.BindBuffer(BufferTarget.ArrayBuffer, _vertexBuffer);
        GL.BufferData(BufferTarget.ArrayBuffer, 0, IntPtr.Zero, BufferUsageHint.DynamicDraw);
        GL.EnableVertexAttribArray(0);
        GL.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, VertexStride * sizeof(float), 0);
        GL.EnableVertexAttribArray(1);
        GL.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, VertexStride * sizeof(float), 2 * sizeof(float));
        GL.EnableVertexAttribArray(2);
        GL.VertexAttribPointer(2, 4, VertexAttribPointerType.Float, false, VertexStride * sizeof(float), 4 * sizeof(float));
    }

    public void BeginFrame()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _vertices.Clear();
    }

    public float MeasureText(string text, float pixelHeight)
    {
        ArgumentNullException.ThrowIfNull(text);
        var scale = pixelHeight / FontPixelHeight;
        var width = 0f;
        var previousGlyph = 0;
        foreach (var character in text)
        {
            var glyphIndex = GetGlyphIndex(character);
            if (previousGlyph != 0 && glyphIndex != 0)
            {
                width += _font.GetGlyphKernAdvance(previousGlyph, glyphIndex) * _fontScale * scale;
            }

            width += _glyphs[GetAtlasIndex(character)].Advance * scale;
            previousGlyph = glyphIndex;
        }

        return width;
    }

    public void AddText(string text, float x, float y, (float R, float G, float B, float A) color, float pixelHeight)
    {
        ArgumentNullException.ThrowIfNull(text);
        var scale = pixelHeight / FontPixelHeight;
        var baseline = y + _ascent * scale;
        var penX = x;
        var previousGlyph = 0;
        foreach (var character in text)
        {
            var glyphIndex = GetGlyphIndex(character);
            var glyph = _glyphs[GetAtlasIndex(character)];
            if (previousGlyph != 0 && glyphIndex != 0)
            {
                penX += _font.GetGlyphKernAdvance(previousGlyph, glyphIndex) * _fontScale * scale;
            }

            if (glyph.Width > 0 && glyph.Height > 0)
            {
                var left = penX + glyph.XOffset * scale;
                var top = baseline + glyph.YOffset * scale;
                var right = left + glyph.Width * scale;
                var bottom = top + glyph.Height * scale;
                AddVertex(left, top, glyph.U0, glyph.V0, color);
                AddVertex(right, top, glyph.U1, glyph.V0, color);
                AddVertex(right, bottom, glyph.U1, glyph.V1, color);
                AddVertex(left, top, glyph.U0, glyph.V0, color);
                AddVertex(right, bottom, glyph.U1, glyph.V1, color);
                AddVertex(left, bottom, glyph.U0, glyph.V1, color);
            }

            penX += glyph.Advance * scale;
            previousGlyph = glyphIndex;
        }
    }

    public void Render(int width, int height)
    {
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
        GL.UseProgram(_program);
        GL.Uniform2(_viewportLocation, (float)Math.Max(1, width), (float)Math.Max(1, height));
        GL.Uniform1(_atlasLocation, 0);
        GL.ActiveTexture(TextureUnit.Texture0);
        GL.BindTexture(TextureTarget.Texture2D, _texture);
        GL.BindVertexArray(_vao);
        GL.DrawArrays(PrimitiveType.Triangles, 0, vertexData.Length / VertexStride);
    }

    private byte[] CreateAtlas()
    {
        var rows = GetAtlasRows();
        var atlasWidth = AtlasColumns * CellWidth;
        var atlasHeight = rows * CellHeight;
        var atlas = new byte[atlasWidth * atlasHeight];
        _font.GetScaledFontVMetrics(FontPixelHeight, out _ascent, out _, out _);
        var scale = _fontScale;
        for (var codepoint = FirstCharacter; codepoint <= LastCharacter; codepoint++)
        {
            var index = codepoint - FirstCharacter;
            var column = index % AtlasColumns;
            var row = index / AtlasColumns;
            var glyphIndex = _font.FindGlyphIndex(codepoint);
            _font.GetGlyphHMetrics(glyphIndex, out var advance, out _);
            var bitmap = _font.CreateGlyphBitmap(glyphIndex, scale, scale);
            var glyphWidth = bitmap?.Width ?? 0;
            var glyphHeight = bitmap?.Height ?? 0;
            var x = column * CellWidth;
            var y = row * CellHeight;
            if (bitmap is { Width: > 0, Height: > 0 })
            {
                if (bitmap.Width > CellWidth || bitmap.Height > CellHeight)
                {
                    throw new InvalidOperationException($"Font glyph '{(char)codepoint}' exceeds its atlas cell.");
                }

                x += 1;
                y += 1;
                for (var bitmapRow = 0; bitmapRow < bitmap.Height; bitmapRow++)
                {
                    System.Buffer.BlockCopy(
                        bitmap.Bitmap,
                        bitmap.BitmapOffset + bitmapRow * bitmap.Width,
                        atlas,
                        (y + bitmapRow) * atlasWidth + x,
                        bitmap.Width);
                }
            }

            _glyphs[index] = new Glyph(
                advance * scale,
                bitmap?.XOffset ?? 0,
                bitmap?.YOffset ?? 0,
                glyphWidth,
                glyphHeight,
                (float)x / atlasWidth,
                (float)y / atlasHeight,
                (float)(x + glyphWidth) / atlasWidth,
                (float)(y + glyphHeight) / atlasHeight);
        }

        return atlas;
    }

    private static int GetAtlasRows()
    {
        return (LastCharacter - FirstCharacter + AtlasColumns) / AtlasColumns;
    }

    private static int GetAtlasIndex(char character)
    {
        var codepoint = character >= FirstCharacter && character <= LastCharacter ? character : '?';
        return codepoint - FirstCharacter;
    }

    private int GetGlyphIndex(char character)
    {
        return _font.FindGlyphIndex(GetAtlasIndex(character) + FirstCharacter);
    }

    private void AddVertex(float x, float y, float u, float v, (float R, float G, float B, float A) color)
    {
        _vertices.Add(x);
        _vertices.Add(y);
        _vertices.Add(u);
        _vertices.Add(v);
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

        GL.DeleteTexture(_texture);
        GL.DeleteBuffer(_vertexBuffer);
        GL.DeleteVertexArray(_vao);
        GL.DeleteProgram(_program);
        _disposed = true;
    }

    private readonly record struct Glyph(
        float Advance,
        float XOffset,
        float YOffset,
        int Width,
        int Height,
        float U0,
        float V0,
        float U1,
        float V1);
}
