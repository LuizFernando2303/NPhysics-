namespace NPhysics;

internal static class DatasetCaptureSmokeTests
{
    public static void Run()
    {
        if (!SimulationSettings.DatasetCaptureEnabled)
        {
            throw new InvalidOperationException(
                "A captura de dataset deve iniciar automaticamente em cada sessao.");
        }

        const int hundredThousandDisks = 100_000;
        var bufferBytes = DatasetGenerator.CalculateCaptureBufferBytes(hundredThousandDisks);
        if (bufferBytes != 153_600_064
            || bufferBytes > DatasetGenerator.MaximumCaptureBufferBytes)
        {
            throw new InvalidOperationException(
                "A captura de 100.000 esferas deve caber no limite seguro do SSBO.");
        }

        if (DatasetCaptureSettings.GetCaptureFrequency(hundredThousandDisks) != 275)
        {
            throw new InvalidOperationException(
                "A captura de 100.000 esferas deve ser limitada a aproximadamente 64 MiB/s.");
        }
    }
}
