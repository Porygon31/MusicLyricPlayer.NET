using System.Diagnostics;
using Whisper.net;
using Whisper.net.Ggml;

namespace MLP.Transcriber.ConsoleApp;

/// <summary>
/// Logique de téléchargement du modèle + transcription via Whisper.net.
/// - Télécharge un modèle GGML si absent (répertoire ./models) avec progression
/// - Convertit l'audio en WAV 16 kHz mono si nécessaire (ffmpeg)
/// - Transcrit en segments (start/end + texte)
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

    /// <summary>
    /// Transcrit un fichier audio en segments (timestamps + texte).
    /// </summary>
    public static async Task<List<Segment>> TranscribeAsync(
        string audioPath,
        string modelName,
        string? language,
        Action<string>? onInfo = null)
    {
        onInfo ??= _ => { };

        // 1) Résoudre le modèle (GGML) et le télécharger si besoin (avec progression)
        var modelPath = await EnsureModelAsync(modelName, onInfo);

        // 2) Préparer audio: convertir → WAV 16k mono PCM (évite CorruptedWaveException)
        string wavPath = audioPath;
        bool isTempWav = false;
        try
        {
            wavPath = await AudioPrep.ConvertToWhisperWavAsync(audioPath, onInfo);
            isTempWav = !string.Equals(Path.GetExtension(audioPath), ".wav", StringComparison.OrdinalIgnoreCase);

            // 3) Créer la factory + builder
            using var factory = WhisperFactory.FromPath(modelPath);
            var builder = factory.CreateBuilder();

            if (!string.IsNullOrWhiteSpace(language))
                builder.WithLanguage(language!);       // "en", "fr", etc.
            else
                builder.WithLanguage("auto");          // détection automatique

            using var processor = builder.Build();

            // 4) Traiter le flux audio et collecter les segments
            var segments = new List<Segment>();
            await using var audioStream = File.OpenRead(wavPath);

            await foreach (var s in processor.ProcessAsync(audioStream))
            {
                // Start / End sont TimeSpan -> convertir en ms
                var startMs = (int)s.Start.TotalMilliseconds;
                var endMs = (int)s.End.TotalMilliseconds;
                var text = (s.Text ?? string.Empty).Trim();

                if (!string.IsNullOrWhiteSpace(text))
                    segments.Add(new Segment(startMs, endMs, text));
            }

            // 5) Ordonner et retourner
            segments.Sort((a, b) => a.StartMs.CompareTo(b.StartMs));
            onInfo($"[ASR] {segments.Count} segments extraits");
            return segments;
        }
        finally
        {
            // Nettoyage du WAV temporaire si on en a créé un
            if (isTempWav)
            {
                try { File.Delete(wavPath); } catch { /* ignore */ }
            }
        }
    }

    /// <summary>
    /// Vérifie la présence du modèle GGML et le télécharge si nécessaire.
    /// Ajoute progression + check d'espace disque disponible.
    /// </summary>
    private static async Task<string> EnsureModelAsync(string modelName, Action<string> onInfo)
    {
        Directory.CreateDirectory(ModelsDir);

        var modelPath = Path.Combine(ModelsDir, $"{modelName}.ggml");
        if (File.Exists(modelPath))
        {
            onInfo($"[Model] Présent → {modelPath}");
            return modelPath;
        }

        // Mapping nom "convivial" -> enum GgmlType
        var ggmlType = MapToGgmlType(modelName);
        if (ggmlType is null)
            throw new InvalidOperationException($"Modèle non supporté: {modelName}");

        // Vérifier espace disque dispo (si estimation connue)
        if (ModelSizeHint.TryGetValue(modelName, out var expectedBytes))
        {
            var free = GetAvailableFreeBytes(ModelsDir);
            // marge +25%
            var need = (long)(expectedBytes * 1.25);
            if (free < need)
                throw new IOException($"Espace disque insuffisant: {FormatMB(free)} MB libres, besoin ≈ {FormatMB(need)} MB (marge incluse) pour {modelName}.");
        }

        onInfo($"[Model] Téléchargement du modèle {modelName} …");
        var sw = Stopwatch.StartNew();

        await using (var modelStream = await WhisperGgmlDownloader.Default.GetGgmlModelAsync(ggmlType.Value))
        await using (var fileWriter = File.OpenWrite(modelPath))
        {
            // Progression manuelle (taille inconnue -> on déduit approx via ModelSizeHint)
            var buffer = new byte[1024 * 128];
            int read;
            long total = 0;
            var lastLog = Stopwatch.StartNew();
            while ((read = await modelStream.ReadAsync(buffer)) > 0)
            {
                await fileWriter.WriteAsync(buffer.AsMemory(0, read));
                total += read;

                if (lastLog.ElapsedMilliseconds > 500)
                {
                    if (ModelSizeHint.TryGetValue(modelName, out var size))
                    {
                        var pct = Math.Min(99, (int)(100.0 * total / size));
                        onInfo($"[Download] {FormatMB(total)} MB — ~{pct}%");
                    }
                    else
                    {
                        onInfo($"[Download] {FormatMB(total)} MB");
                    }
                    lastLog.Restart();
                }
            }
        }

        sw.Stop();
        onInfo($"[Model] OK → {modelPath} ({sw.Elapsed:mm\\:ss})");
        return modelPath;
    }

    /// <summary>Mappe des alias pratiques vers l'énum GgmlType.</summary>
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
