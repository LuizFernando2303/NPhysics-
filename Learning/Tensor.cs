namespace NPhysics;

internal sealed class Tensor
{
    public Tensor(int[] shape)
    {
        Shape = (int[])shape.Clone();
        Data = new float[Shape.Aggregate(1, (count, size) => checked(count * size))];
    }

    public Tensor(int[] shape, float[] data)
    {
        Shape = (int[])shape.Clone();
        var length = Shape.Aggregate(1, (count, size) => checked(count * size));
        if (data.Length != length)
        {
            throw new ArgumentException("Tamanho de dados inconsistente com o tensor.");
        }
        Data = (float[])data.Clone();
    }

    public int[] Shape { get; }
    public float[] Data { get; }
}
