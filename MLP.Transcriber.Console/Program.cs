using System.Diagnostics;
using System.Globalization;

namespace MLP.Transcriber.ConsoleApp;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        System.Console.OutputEncoding = System.Text.Encoding.UTF8;
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;

        if (args.Length == 0)
        {
            System.Console.WriteLine("Usage:");
            System.Console.WriteLine("  dotnet run -- \"C:\\music\\track.flac\" [model=small.en] [lang=en|\"\"(auto)]");
            System.Console.WriteLine();
            System.Console.WriteLine("Exemples:");
            System.Console.WriteLine("  dotnet run -- \"C:\\music\\track.mp3\"");
            System.Console.WriteLine("  dotnet run -- \"/home/me/track.wav\" small");
            System.Console.WriteLine("  dotnet run -- \"/home/me/track.wav\" medium \"\"   (lang auto)");
            return 1;
        }

        var audioPath = args[0];
        if (!File.Exists(audioPath))
        {
            System.Console.Error.WriteLine($"[ERR] Fichier introuvable: {audioPath}");
            return 2;
        }

        var modelName = args.Length >= 2 ? args[1] : "small.en";
        string? language = args.Length >= 3 ? (string.IsNullOrWhiteSpace(args[2]) ? null : args[2]) : "en";

        var outLrc = Path.ChangeExtension(audioPath, ".lrc");

        try
        {
            System.Console.WriteLine($"[INFO] Audio     : {audioPath}");
            System.Console.WriteLine($"[INFO] Modèle    : {modelName}");
            System.Console.WriteLine($"[INFO] Langue    : {(language ?? "(auto)")}");
            System.Console.WriteLine($"[INFO] Sortie LRC: {outLrc}");
            System.Console.WriteLine();

            var sw = Stopwatch.StartNew();

            var segments = await Transcriber.TranscribeAsync(
                audioPath: audioPath,
                modelName: modelName,
                language: language,
                onInfo: msg => System.Console.WriteLine(msg)
            );

            LrcWriter.Write(segments, outLrc);

            sw.Stop();
            System.Console.WriteLine();
            System.Console.WriteLine($"[OK] Terminé en {sw.Elapsed:mm\\:ss} — {segments.Count} segments → {outLrc}");
            return 0;
        }
        catch (Exception ex)
        {
            System.Console.Error.WriteLine($"[FATAL] {ex.GetType().Name}: {ex.Message}");
            return 3;
        }
    }
}
