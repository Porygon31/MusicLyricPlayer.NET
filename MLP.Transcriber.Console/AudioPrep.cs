using System.Diagnostics;

namespace MLP.Transcriber.ConsoleApp;

/// <summary>
/// Prépare l'audio pour Whisper:
/// - Convertit tout format (flac, mp3, m4a, ogg, wav exotique) en WAV PCM s16 mono 16 kHz,
///   que Whisper.net sait consommer sans surprise.
/// - Utilise ffmpeg (doit être dans le PATH).
/// Retourne le chemin du WAV temporaire prêt pour Whisper.
/// </summary>
public static class AudioPrep
{
    /// <summary>
    /// Convertit un fichier audio arbitraire en WAV 16 kHz mono PCM s16.
    /// Si l'entrée est déjà un WAV compatible, on peut la renvoyer telle quelle (optionnel).
    /// </summary>
    public static async Task<string> ConvertToWhisperWavAsync(string inputPath, Action<string>? onInfo = null)
    {
        onInfo ??= _ => { };

        var ffmpeg = "ffmpeg"; // doit être dans le PATH
        // Dossier temp par utilisateur/session
        var tempDir = Path.Combine(Path.GetTempPath(), "MLP.Transcriber", "wav");
        Directory.CreateDirectory(tempDir);

        // Sortie wav temporaire
        var outWav = Path.Combine(tempDir, $"{Path.GetFileNameWithoutExtension(inputPath)}_{Guid.NewGuid():N}.wav");

        // Filtre audio:
        // -1- Convert mono 16 kHz
        // -2- Force PCM s16 (compatibilité Whisper.net)
        // -> Pas de normalisation ici pour garder un pipeline simple au prototype.
        var args = new[]
        {
            "-y",
            "-i", $"\"{inputPath}\"",
            "-vn",
            "-ac", "1",
            "-ar", "16000",
            "-acodec", "pcm_s16le",
            "-f", "wav",
            $"\"{outWav}\""
        };

        var psi = new ProcessStartInfo
        {
            FileName = ffmpeg,
            Arguments = string.Join(" ", args),
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        onInfo("[Audio] Conversion → WAV 16 kHz mono (ffmpeg) …");
        try
        {
            using var proc = Process.Start(psi)!;
            var stderr = await proc.StandardError.ReadToEndAsync(); // ffmpeg écrit beaucoup sur stderr
            var stdout = await proc.StandardOutput.ReadToEndAsync();
            await proc.WaitForExitAsync();

            if (proc.ExitCode != 0 || !File.Exists(outWav))
            {
                onInfo($"[Audio] ffmpeg a échoué (code={proc.ExitCode}).");
                if (!string.IsNullOrWhiteSpace(stderr)) onInfo("[ffmpeg] " + stderr.Trim());
                if (!string.IsNullOrWhiteSpace(stdout)) onInfo("[ffmpeg] " + stdout.Trim());
                throw new InvalidOperationException("Conversion WAV échouée (ffmpeg). Installe ffmpeg et assure-le dans le PATH.");
            }
            onInfo("[Audio] Conversion OK.");
            return outWav;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("FFmpeg introuvable ou conversion impossible. Installe ffmpeg et redémarre.", ex);
        }
    }
}
