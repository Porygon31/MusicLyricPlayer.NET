using System.Diagnostics;
using System.Runtime.CompilerServices;
using Whisper.net;
using Whisper.net.Ggml;

namespace MLP.Transcriber.ConsoleApp;

/// <summary>
/// Téléchargement du modèle (progression + ETA) + conversion WAV + transcription (progression + ETA).
/// </summary>
public static class Transcriber
{
    public record Segment(int StartMs, int EndMs, string Text);

    private static readonly string ModelsDir = Path.Combine(AppContext.BaseDirectory, "models");

    // Estimation tailles (octets) — sert pour % approx. et check espace disque
    private static readonly Dictionary<string, long> ModelSizeHint = new(StringComparer.OrdinalIgnoreCase)
    {
        ["tiny.en"] = 75L * 1024 * 1024,
        ["base.en"] = 145L * 1024 * 1024,
        ["small.en"] = 465L * 1024 * 1024,
        ["small"] = 480L * 1024 * 1024,
        ["medium"] = 1400L * 1024 * 1024,
        ["large-v3"] = 3100L * 1024 * 1024,
    };

    public static async Task<List<Segment>> TranscribeAsync(
    string audioPath,
    string modelName,
    string? language,
    Action<string>? onInfo = null)
    {
        onInfo ??= _ => { };

        // 1️⃣ Télécharger ou charger le modèle
        var modelPath = await EnsureModelAsync(modelName, onInfo);

        // 2️⃣ Conversion audio en WAV
        string wavPath = audioPath;
        bool isTempWav = false;
        try
        {
            wavPath = await AudioPrep.ConvertToWhisperWavAsync(audioPath, onInfo);
            isTempWav = !string.Equals(Path.GetExtension(audioPath), ".wav", StringComparison.OrdinalIgnoreCase);

            // 3️⃣ Animation “chargement modèle Whisper”
            using var spinner = new Spinner("Chargement du modèle Whisper...");
            await Task.Run(() =>
            {
                using var factory = WhisperFactory.FromPath(modelPath);
                var builder = factory.CreateBuilder();
                if (!string.IsNullOrWhiteSpace(language))
                    builder.WithLanguage(language);
                else
                    builder.WithLanguage("auto");

                using var processor = builder.Build();
                spinner.Stop("[Model] Whisper prêt !");
            });

            // 🔄 Recrée factory/processor pour la transcription réelle (le précédent n'était que pour pré-chargement)
            using var factory2 = WhisperFactory.FromPath(modelPath);
            var builder2 = factory2.CreateBuilder();
            builder2.WithLanguage(language ?? "auto");
            using var processor2 = builder2.Build();

            // 4️⃣ Préparation des données
            var segments = new List<Segment>();
            await using var audioStream = File.OpenRead(wavPath);

            // Durée totale (ms) basée sur PCM 16k mono s16 (32_000 B/s)
            long totalBytes = new FileInfo(wavPath).Length;
            long totalMs = (long)Math.Round((totalBytes / 32000.0) * 1000.0);
            if (totalMs <= 0) totalMs = 1;

            var swInfer = Stopwatch.StartNew();
            int lastPctInfer = -1;

            // 5️⃣ Boucle d’inférence avec barre, ETA et ratio x
            await foreach (var s in processor2.ProcessAsync(audioStream))
            {
                var startMs = (int)s.Start.TotalMilliseconds;
                var endMs = (int)s.End.TotalMilliseconds;
                var text = (s.Text ?? string.Empty).Trim();

                if (!string.IsNullOrWhiteSpace(text))
                    segments.Add(new Segment(startMs, endMs, text));

                var clampedEnd = Math.Min(Math.Max(endMs, 0), (int)totalMs);
                int pct = (int)Math.Min(99, Math.Round((clampedEnd * 100.0) / totalMs));

                var seconds = Math.Max(0.001, swInfer.Elapsed.TotalSeconds);
                var speedMsPerSec = clampedEnd / seconds;
                var remainMs = Math.Max(0, totalMs - clampedEnd);
                var etaSec = speedMsPerSec > 1 ? remainMs / speedMsPerSec : double.PositiveInfinity;
                var ratio = speedMsPerSec / 1000.0; // 1x = temps réel

                var suffix = $"x{ratio:F1}  ETA {ConsoleProgress.FormatDuration(etaSec)}";

                if (pct != lastPctInfer)
                {
                    lastPctInfer = pct;
                    ConsoleProgress.Draw(pct, "[ASR]", suffix, width: 30, filledColor: ConsoleColor.Cyan);
                }
            }

            ConsoleProgress.Finish("[ASR]");

            swInfer.Stop();
            segments.Sort((a, b) => a.StartMs.CompareTo(b.StartMs));

            // 🟢 Résumé clair et lisible
            var avgRatio = (totalMs / Math.Max(1, swInfer.Elapsed.TotalMilliseconds));
            var avgX = avgRatio.ToString("F1");
            Console.ForegroundColor = avgRatio >= 1 ? ConsoleColor.Green : ConsoleColor.Yellow;
            Console.WriteLine($"[INFO] Traitement {avgX}× {(avgRatio >= 1 ? "plus rapide" : "plus lent")} que le temps réel (vitesse moyenne : x{avgX})");
            Console.ResetColor();

            onInfo($"[ASR] {segments.Count} segments extraits");
            return segments;
        }
        finally
        {
            if (isTempWav)
            {
                try { File.Delete(wavPath); } catch { /* ignore */ }
            }
        }
    }

    private static async Task<string> EnsureModelAsync(string modelName, Action<string> onInfo)
    {
        Directory.CreateDirectory(ModelsDir);

        var modelPath = Path.Combine(ModelsDir, $"{modelName}.ggml");
        if (File.Exists(modelPath))
        {
            onInfo($"[Model] Présent → {modelPath}");
            return modelPath;
        }

        var ggmlType = MapToGgmlType(modelName);
        if (ggmlType is null)
            throw new InvalidOperationException($"Modèle non supporté: {modelName}");

        // Check espace disque
        if (ModelSizeHint.TryGetValue(modelName, out var expectedBytes))
        {
            var free = GetAvailableFreeBytes(ModelsDir);
            var need = (long)(expectedBytes * 1.25); // +25% marge
            if (free < need)
                throw new IOException($"Espace disque insuffisant: {FormatMB(free)} MB libres, besoin ≈ {FormatMB(need)} MB pour {modelName}.");
        }

        onInfo($"[Model] Téléchargement du modèle {modelName} …");
        var sw = Stopwatch.StartNew();

        await using (var modelStream = await WhisperGgmlDownloader.Default.GetGgmlModelAsync(ggmlType.Value))
        await using (var fileWriter = File.OpenWrite(modelPath))
        {
            var buffer = new byte[1024 * 128];
            int read;
            long total = 0;
            int lastPct = -1;
            var lastLog = Stopwatch.StartNew();

            while ((read = await modelStream.ReadAsync(buffer)) > 0)
            {
                await fileWriter.WriteAsync(buffer.AsMemory(0, read));
                total += read;

                if (lastLog.ElapsedMilliseconds > 100)
                {
                    // vitesse moyenne
                    var seconds = Math.Max(0.001, sw.Elapsed.TotalSeconds);
                    var mbPerSec = total / 1_000_000.0 / seconds;

                    string suffix;
                    int pct;

                    if (ModelSizeHint.TryGetValue(modelName, out var size))
                    {
                        pct = (int)Math.Min(99, Math.Round((total * 100.0) / size));
                        var remainBytes = Math.Max(0, size - total);
                        var etaSec = mbPerSec > 0.001 ? (remainBytes / 1_000_000.0) / mbPerSec : double.PositiveInfinity;
                        suffix = $"{mbPerSec:F1} MB/s  ETA {ConsoleProgress.FormatDuration(etaSec)}";
                    }
                    else
                    {
                        // Taille inconnue → pas d'ETA fiable
                        pct = (int)((total / (1024.0 * 1024.0)) % 100);
                        suffix = $"{mbPerSec:F1} MB/s";
                    }

                    if (pct != lastPct)
                    {
                        lastPct = pct;
                        ConsoleProgress.Draw(pct, "[Download]", suffix, width: 30, filledColor: ConsoleColor.Green);
                    }
                    lastLog.Restart();
                }
            }
        }

        ConsoleProgress.Finish("[Download]");
        sw.Stop();
        onInfo($"[Model] OK → {modelPath} ({sw.Elapsed:mm\\:ss})");
        return modelPath;
    }

    private static GgmlType? MapToGgmlType(string modelName)
    {
        var key = modelName.Trim().ToLowerInvariant();
        return key switch
        {
            "tiny.en" => GgmlType.TinyEn,
            "base.en" => GgmlType.BaseEn,
            "small.en" => GgmlType.SmallEn,
            "small" => GgmlType.Small,
            "medium" => GgmlType.Medium,
            "large-v3" => GgmlType.LargeV3,
            _ => null
        };
    }

    private static long GetAvailableFreeBytes(string path)
    {
        var root = Path.GetPathRoot(Path.GetFullPath(path))!;
        var di = new DriveInfo(root);
        return di.AvailableFreeSpace;
    }

    private static string FormatMB(long bytes) => (bytes / 1_000_000.0).ToString("F0");
}
