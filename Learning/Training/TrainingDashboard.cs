using System.Globalization;
using System.Net;
using System.Text;

namespace NPhysics;

internal sealed record TargetStatistics(string Name, double Mean, double MeanAbsolute, double RootMeanSquare, double MaximumAbsolute);

internal sealed record DatasetOverview(
    string FilePath,
    long FileSizeBytes,
    DateTime LastWriteUtc,
    string Sha256,
    DatasetMetadata Metadata,
    long SampleCount,
    int BlockCount,
    long OverflowCount,
    long OverflowSampleCount,
    double MeanNeighborCount,
    int MaximumNeighborCount,
    long[] Categories,
    TargetStatistics[] Targets,
    ModelOverview? Model);

internal sealed record ModelOverview(
    string FilePath,
    long FileSizeBytes,
    string Sha256,
    bool DatasetHashMatches,
    string TrainingHistory,
    int LayerCount);

internal sealed record TrainingBlockResult(int BlockIndex, int SampleCount, float Loss);

internal static class TrainingDashboard
{
    private static readonly string[] CategoryNames =
    [
        "Sem contato",
        "Contato com a rampa",
        "Contato entre esferas",
        "Rampa e esferas",
        "Alta velocidade relativa",
        "Outro contato",
    ];

    private static readonly string[] TargetNames =
    [
        "Δx", "Δy", "Δz",
        "Δvx", "Δvy", "Δvz",
        "Δωx", "Δωy", "Δωz",
    ];

    public static DatasetOverview AnalyzeDataset(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        var fullPath = Path.GetFullPath(filePath);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("Dataset nao encontrado.", fullPath);
        }

        var sampleCount = 0L;
        var blockCount = 0;
        var overflowCount = 0L;
        var overflowSampleCount = 0L;
        var totalNeighbors = 0L;
        var maximumNeighborCount = 0;
        var categories = new long[CategoryNames.Length];
        var sums = new double[GraphNetwork.OutputSize];
        var absoluteSums = new double[GraphNetwork.OutputSize];
        var squaredSums = new double[GraphNetwork.OutputSize];
        var maximumAbsolute = new double[GraphNetwork.OutputSize];

        using (var reader = new DatasetReader(fullPath))
        {
            var metadata = reader.ReadMetadata();
            foreach (var block in reader.ReadBlocks())
            {
                blockCount++;
                overflowCount += block.OverflowCount;
                for (var i = 0; i < categories.Length; i++)
                {
                    categories[i] += block.Categories[i];
                }

                var blockSampleCount = block.Samples.Length / DatasetSchema.FloatsPerSample;
                sampleCount += blockSampleCount;
                for (var sample = 0; sample < blockSampleCount; sample++)
                {
                    var offset = sample * DatasetSchema.FloatsPerSample;
                    var neighborCount = Math.Clamp((int)block.Samples[offset + 375], 0, metadata.MaxNeighbors);
                    totalNeighbors += neighborCount;
                    maximumNeighborCount = Math.Max(maximumNeighborCount, neighborCount);
                    if (block.Samples[offset + 376] > 0f)
                    {
                        overflowSampleCount++;
                    }

                    for (var target = 0; target < GraphNetwork.OutputSize; target++)
                    {
                        var value = block.Samples[offset + 36 + target];
                        var absolute = Math.Abs(value);
                        sums[target] += value;
                        absoluteSums[target] += absolute;
                        squaredSums[target] += value * value;
                        maximumAbsolute[target] = Math.Max(maximumAbsolute[target], absolute);
                    }
                }
            }

            var targetStatistics = new TargetStatistics[GraphNetwork.OutputSize];
            for (var target = 0; target < targetStatistics.Length; target++)
            {
                var denominator = Math.Max(1L, sampleCount);
                targetStatistics[target] = new TargetStatistics(
                    TargetNames[target],
                    sums[target] / denominator,
                    absoluteSums[target] / denominator,
                    Math.Sqrt(squaredSums[target] / denominator),
                    maximumAbsolute[target]);
            }

            var overview = new DatasetOverview(
                fullPath,
                new FileInfo(fullPath).Length,
                File.GetLastWriteTimeUtc(fullPath),
                ModelSerializer.HashFile(fullPath),
                metadata,
                sampleCount,
                blockCount,
                overflowCount,
                overflowSampleCount,
                (double)totalNeighbors / Math.Max(1L, sampleCount),
                maximumNeighborCount,
                categories,
                targetStatistics,
                null);
            return AttachModel(overview);
        }
    }

    public static DatasetOverview AttachModel(DatasetOverview overview, string? modelPath = null)
    {
        modelPath ??= Path.ChangeExtension(overview.FilePath, ".nphysmodel");
        if (!File.Exists(modelPath))
        {
            return overview with { Model = null };
        }

        var parameters = ModelSerializer.Load(modelPath);
        var fullPath = Path.GetFullPath(modelPath);
        return overview with
        {
            Model = new ModelOverview(
                fullPath,
                new FileInfo(fullPath).Length,
                ModelSerializer.HashFile(fullPath),
                string.Equals(parameters.DatasetHash, overview.Sha256, StringComparison.OrdinalIgnoreCase),
                parameters.TrainingHistory,
                parameters.Layers.Length),
        };
    }

    public static string WriteDatasetReport(string datasetPath)
    {
        var overview = AnalyzeDataset(datasetPath);
        var reportPath = Path.ChangeExtension(overview.FilePath, ".report.html");
        WriteReport(reportPath, overview, "Dataset inspecionado", [], TimeSpan.Zero);
        return reportPath;
    }

    public static void WriteReport(
        string reportPath,
        DatasetOverview overview,
        string status,
        IReadOnlyList<TrainingBlockResult> trainingResults,
        TimeSpan elapsed,
        string? error = null,
        long trainingSampleLimit = 0)
    {
        var html = BuildHtml(
            overview,
            status,
            trainingResults,
            elapsed,
            error,
            trainingSampleLimit);
        File.WriteAllText(reportPath, html, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    private static string BuildHtml(
        DatasetOverview overview,
        string status,
        IReadOnlyList<TrainingBlockResult> trainingResults,
        TimeSpan elapsed,
        string? error,
        long trainingSampleLimit)
    {
        var builder = new StringBuilder(12_000);
        var isTraining = string.Equals(status, "Treinando", StringComparison.Ordinal);
        var usableSampleCount = Math.Max(0, overview.SampleCount - overview.OverflowSampleCount);
        var trainedSampleCount = trainingResults.Sum(result => (long)result.SampleCount);
        var progressTarget = trainingSampleLimit > 0
            ? Math.Min(usableSampleCount, trainingSampleLimit)
            : usableSampleCount;
        var progress = progressTarget == 0
            ? 0
            : Math.Min(100, trainedSampleCount * 100.0 / progressTarget);
        var meanLoss = trainingResults.Count == 0
            ? double.NaN
            : trainingResults.Average(result => result.Loss);
        var recentLoss = trainingResults.Count == 0
            ? double.NaN
            : trainingResults[^1].Loss;
        var metadata = overview.Metadata;

        builder.AppendLine("<!doctype html>");
        builder.AppendLine("<html lang=\"pt-BR\"><head><meta charset=\"utf-8\">");
        builder.AppendLine("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">");
        if (isTraining)
        {
            builder.AppendLine("<meta http-equiv=\"refresh\" content=\"3\">");
        }
        builder.AppendLine("<title>NPhysics | Painel de treinamento</title>");
        builder.AppendLine("<style>");
        builder.AppendLine(
            ":root{color-scheme:dark;--bg:#0b1220;--panel:#121d2e;--line:#26364b;--text:#e7eef8;"
            + "--muted:#9aabc1;--accent:#6ee7b7;--warn:#fbbf24;--bad:#fb7185}"
            + "*{box-sizing:border-box}body{margin:0;background:var(--bg);color:var(--text);font:15px/1.5 "
            + "Segoe UI,Arial,sans-serif}.wrap{max-width:1180px;margin:0 auto;padding:32px 22px 56px}"
            + "h1{font-size:30px;margin:0 0 4px}h2{font-size:19px;margin:0 0 16px}"
            + "p{margin:4px 0;color:var(--muted)}.top,.panel{background:var(--panel);border:1px solid var(--line);"
            + "border-radius:14px;padding:20px;margin-bottom:16px}.top{display:flex;justify-content:space-between;"
            + "align-items:flex-start;gap:20px}.status{border:1px solid var(--line);border-radius:99px;"
            + "padding:6px 12px;color:var(--accent);white-space:nowrap}.grid{display:grid;"
            + "grid-template-columns:repeat(auto-fit,minmax(170px,1fr));gap:12px;margin:16px 0}"
            + ".card{background:#0d1726;border:1px solid var(--line);border-radius:10px;padding:14px}"
            + ".label{font-size:12px;color:var(--muted);text-transform:uppercase;letter-spacing:.06em}"
            + ".value{font-size:23px;font-weight:650;margin-top:3px}.small{font-size:12px;color:var(--muted);"
            + "overflow-wrap:anywhere}table{width:100%;border-collapse:collapse;font-variant-numeric:tabular-nums}"
            + "th,td{text-align:left;padding:9px 10px;border-bottom:1px solid var(--line)}th{color:var(--muted);"
            + "font-weight:550;font-size:12px}td.num{text-align:right;font-family:Consolas,monospace}"
            + ".bar{height:8px;background:#203047;border-radius:8px;overflow:hidden;min-width:80px}"
            + ".bar i{display:block;height:100%;background:var(--accent)}.scroll{overflow-x:auto}"
            + "svg{width:100%;height:auto;max-height:300px}.warning{color:var(--warn)}.error{color:var(--bad)}"
            + "a{color:var(--accent)}@media(max-width:640px){.top{display:block}.status{display:inline-block;"
            + "margin-top:12px}.wrap{padding:18px 12px}}");
        builder.AppendLine("</style></head><body><main class=\"wrap\">");
        builder.AppendLine("<header class=\"top\"><div><h1>NPhysics · treinamento</h1>");
        builder.Append("<p>Relatorio local atualizado em ")
            .Append(Escape(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)))
            .AppendLine("</p></div><div class=\"status\">")
            .Append(Escape(status))
            .AppendLine("</div></header>");

        builder.AppendLine("<section class=\"panel\"><h2>Progresso</h2>");
        builder.AppendLine("<div class=\"grid\">");
        AddCard(builder, "Progresso", isTraining
            ? $"{progress.ToString("0.0", CultureInfo.InvariantCulture)}%"
            : status == "Concluido" ? "100%" : status);
        AddCard(
            builder,
            trainingResults.Count > 0 || isTraining || status is "Concluido" or "Falhou"
                ? "Blocos processados"
                : "Blocos no dataset",
            trainingResults.Count > 0 || isTraining || status is "Concluido" or "Falhou"
                ? $"{trainingResults.Count:N0} / {overview.BlockCount:N0}"
                : $"{overview.BlockCount:N0}");
        AddCard(builder, "Loss media por bloco", FormatMetric(meanLoss));
        AddCard(builder, "Loss do ultimo bloco", FormatMetric(recentLoss));
        AddCard(builder, "Tempo desta execucao", elapsed.ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture));
        builder.AppendLine("</div>");
        if (isTraining)
        {
            builder.Append("<div class=\"bar\" aria-label=\"Progresso do treinamento\"><i style=\"width:")
                .Append(progress.ToString("0.##", CultureInfo.InvariantCulture))
                .AppendLine("%\"></i></div>");
            builder.AppendLine("<p>Este arquivo atualiza automaticamente a cada 3 segundos enquanto o processo treina.</p>");
        }
        if (!string.IsNullOrWhiteSpace(error))
        {
            builder.Append("<p class=\"error\">").Append(Escape(error)).AppendLine("</p>");
        }
        builder.AppendLine("</section>");

        builder.AppendLine("<section class=\"panel\"><h2>Dataset</h2><div class=\"grid\">");
        AddCard(builder, "Amostras", $"{overview.SampleCount:N0}");
        AddCard(builder, "Tamanho", FormatBytes(overview.FileSizeBytes));
        AddCard(builder, "Vizinhos por amostra", overview.MeanNeighborCount.ToString("0.00", CultureInfo.InvariantCulture)
            + $" (max. {overview.MaximumNeighborCount})");
        AddCard(builder, "Overflow", $"{overview.OverflowCount:N0} relacoes / {overview.OverflowSampleCount:N0} amostras");
        builder.AppendLine("</div>");
        builder.Append("<p><strong>Arquivo:</strong> ").Append(Escape(overview.FilePath)).AppendLine("</p>");
        builder.Append("<p><strong>SHA-256:</strong> ").Append(Escape(overview.Sha256)).AppendLine("</p>");
        builder.Append("<p><strong>Sessao:</strong> ").Append(Escape(metadata.SessionId))
            .Append(" · schema ").Append(metadata.SchemaVersion)
            .Append(" · capturadas por bloco: ").Append(metadata.DiskCount.ToString("N0", CultureInfo.InvariantCulture))
            .Append(" · vizinhos limite: ").Append(metadata.MaxNeighbors)
            .Append(" · seed: ").Append(metadata.Seed)
            .AppendLine("</p>");
        builder.Append("<p><strong>Captura:</strong> a cada ").Append(metadata.CaptureFrequency)
            .Append(" passos · limite configurado: ").Append(metadata.MaxSamples.ToString("N0", CultureInfo.InvariantCulture))
            .Append(" amostras · arquivo alterado em ")
            .Append(Escape(overview.LastWriteUtc.ToString("yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture)))
            .AppendLine("</p>");
        builder.Append("<p><strong>Simulacao:</strong> passo ").Append(FormatNumber(metadata.FixedStep))
            .Append(" s · gravidade ").Append(FormatNumber(metadata.Gravity))
            .Append(" m/s² · inclinacao ").Append(FormatNumber(metadata.InclinationDegrees))
            .AppendLine("°</p>");

        var maximumCategory = Math.Max(1L, overview.Categories.Max());
        builder.AppendLine("<div class=\"scroll\"><table><thead><tr><th>Categoria da captura</th>"
            + "<th>Contagem</th><th>Distribuicao relativa</th></tr></thead><tbody>");
        for (var i = 0; i < CategoryNames.Length; i++)
        {
            var count = overview.Categories[i];
            builder.Append("<tr><td>").Append(Escape(CategoryNames[i])).Append("</td><td class=\"num\">")
                .Append(count.ToString("N0", CultureInfo.InvariantCulture))
                .Append("</td><td><div class=\"bar\"><i style=\"width:")
                .Append((count * 100.0 / maximumCategory).ToString("0.##", CultureInfo.InvariantCulture))
                .AppendLine("%\"></i></div></td></tr>");
        }
        builder.AppendLine("</tbody></table></div></section>");

        builder.AppendLine("<section class=\"panel\"><h2>Alvos fisicos capturados</h2>"
            + "<p>As correcoes medidas pelo solver Teacher que a rede tenta aprender.</p>"
            + "<div class=\"scroll\"><table><thead><tr><th>Componente</th><th>Media</th>"
            + "<th>Media absoluta</th><th>RMSE</th><th>Maximo absoluto</th></tr></thead><tbody>");
        foreach (var target in overview.Targets)
        {
            builder.Append("<tr><td>").Append(Escape(target.Name)).Append("</td><td class=\"num\">")
                .Append(FormatNumber(target.Mean)).Append("</td><td class=\"num\">")
                .Append(FormatNumber(target.MeanAbsolute)).Append("</td><td class=\"num\">")
                .Append(FormatNumber(target.RootMeanSquare)).Append("</td><td class=\"num\">")
                .Append(FormatNumber(target.MaximumAbsolute)).AppendLine("</td></tr>");
        }
        builder.AppendLine("</tbody></table></div></section>");

        builder.AppendLine("<section class=\"panel\"><h2>Curva de treinamento</h2>");
        if (trainingResults.Count == 0)
        {
            builder.AppendLine(overview.Model is null
                ? "<p>Nenhuma execucao de treinamento registrada. Use <code>--train</code> para treinar este dataset.</p>"
                : "<p>Este modelo ja foi treinado, mas o arquivo nao guarda a loss de cada bloco. "
                    + "O resumo gravado esta na secao do modelo; execute <code>--train</code> para gerar "
                    + "uma curva detalhada nesta versao do painel.</p>");
        }
        else
        {
            AppendLossChart(builder, trainingResults);
            builder.AppendLine("<div class=\"scroll\"><table><thead><tr><th>Bloco</th><th>Amostras</th>"
                + "<th>Loss Huber antes da atualizacao</th></tr></thead><tbody>");
            foreach (var result in trainingResults)
            {
                builder.Append("<tr><td>").Append(result.BlockIndex.ToString("N0", CultureInfo.InvariantCulture))
                    .Append("</td><td class=\"num\">").Append(result.SampleCount.ToString("N0", CultureInfo.InvariantCulture))
                    .Append("</td><td class=\"num\">").Append(FormatNumber(result.Loss)).AppendLine("</td></tr>");
            }
            builder.AppendLine("</tbody></table></div>");
        }
        builder.AppendLine("<p>A loss exibida e medida em cada bloco antes do update Adam. Nao representa validacao "
            + "em dados separados; o pipeline atual executa uma epoca sobre os dados de treino.</p></section>");

        builder.AppendLine("<section class=\"panel\"><h2>Modelo e resultado</h2>");
        if (overview.Model is null)
        {
            builder.AppendLine("<p>Nenhum arquivo .nphysmodel correspondente foi encontrado.</p>");
        }
        else
        {
            builder.Append("<p><strong>Modelo:</strong> ").Append(Escape(overview.Model.FilePath)).AppendLine("</p>");
            builder.Append("<p><strong>Tamanho:</strong> ").Append(FormatBytes(overview.Model.FileSizeBytes))
                .Append(" · ").Append(overview.Model.LayerCount)
                .AppendLine(" camadas · SHA-256: ")
                .Append(Escape(overview.Model.Sha256)).AppendLine("</p>");
            builder.Append("<p><strong>Dataset associado:</strong> ")
                .Append(overview.Model.DatasetHashMatches ? "sim (hash confere)" : "nao (hash diferente)")
                .AppendLine("</p><p><strong>Historico gravado no modelo:</strong> ")
                .Append(Escape(overview.Model.TrainingHistory)).AppendLine("</p>");
        }
        builder.AppendLine("</section><footer class=\"small\">Relatorio gerado localmente pelo NPhysics. "
            + "Nenhum dado e enviado para a internet.</footer></main></body></html>");
        return builder.ToString();
    }

    private static void AppendLossChart(StringBuilder builder, IReadOnlyList<TrainingBlockResult> results)
    {
        const double width = 900;
        const double height = 220;
        const double padding = 28;
        var minimum = results.Min(result => result.Loss);
        var maximum = results.Max(result => result.Loss);
        var range = Math.Max(1e-12, maximum - minimum);
        var stride = Math.Max(1, (int)Math.Ceiling(results.Count / 300.0));
        var points = new StringBuilder();
        for (var index = 0; index < results.Count; index += stride)
        {
            var x = padding + (width - 2 * padding) * index / Math.Max(1, results.Count - 1);
            var y = height - padding - (height - 2 * padding) * (results[index].Loss - minimum) / range;
            if (points.Length > 0)
            {
                points.Append(' ');
            }
            points.Append(x.ToString("0.##", CultureInfo.InvariantCulture))
                .Append(',')
                .Append(y.ToString("0.##", CultureInfo.InvariantCulture));
        }
        if ((results.Count - 1) % stride != 0)
        {
            var lastIndex = results.Count - 1;
            var x = width - padding;
            var y = height - padding
                - (height - 2 * padding) * (results[lastIndex].Loss - minimum) / range;
            points.Append(' ')
                .Append(x.ToString("0.##", CultureInfo.InvariantCulture))
                .Append(',')
                .Append(y.ToString("0.##", CultureInfo.InvariantCulture));
        }

        builder.AppendLine("<svg viewBox=\"0 0 900 220\" role=\"img\" aria-label=\"Loss por bloco\">");
        builder.AppendLine("<path d=\"M28 192H872M28 28V192\" stroke=\"#34465e\" fill=\"none\"/>");
        builder.Append("<polyline points=\"").Append(points)
            .AppendLine("\" fill=\"none\" stroke=\"#6ee7b7\" stroke-width=\"3\" stroke-linejoin=\"round\"/>");
        builder.Append("<text x=\"28\" y=\"216\" fill=\"#9aabc1\" font-size=\"12\">Faixa de loss: ")
            .Append(FormatNumber(minimum)).Append(" a ").Append(FormatNumber(maximum))
            .AppendLine("</text></svg>");
    }

    private static void AddCard(StringBuilder builder, string label, string value)
    {
        builder.Append("<div class=\"card\"><div class=\"label\">")
            .Append(Escape(label))
            .Append("</div><div class=\"value\">")
            .Append(Escape(value))
            .AppendLine("</div></div>");
    }

    private static string FormatMetric(double value)
    {
        return double.IsFinite(value) ? FormatNumber(value) : "—";
    }

    private static string FormatNumber(double value)
    {
        return value.ToString("0.######", CultureInfo.InvariantCulture);
    }

    private static string FormatBytes(long bytes)
    {
        return (bytes / (1024d * 1024d)).ToString("0.##", CultureInfo.InvariantCulture) + " MB";
    }

    private static string Escape(string value)
    {
        return WebUtility.HtmlEncode(value);
    }
}
