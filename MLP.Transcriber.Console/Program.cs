using System.Diagnostics;
using System.Globalization;

namespace MLP.Transcriber.ConsoleApp;

internal static class Program
{
    // Liste des modèles acceptés (cohérente avec Transcriber.MapToGgmlType)
    private static readonly string[] AllowedModels = new[]
    {
        "tiny.en", "base.en", "small.en",
        "small", "medium", "large-v3"
    };

    private static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;

        if (args.Length == 0)
        {
            PrintUsage();
            return 1;
        }

        var audioPath = args[0];
        if (!File.Exists(audioPath))
        {
            Error($"Fichier introuvable: {audioPath}");
            return 2;
        }

        var modelName = args.Length >= 2 ? args[1] : "small.en";
        // Validation modèle
        if (!AllowedModels.Contains(modelName, StringComparer.OrdinalIgnoreCase))
        {
            Error($"Modèle invalide: \"{modelName}\"");
            var suggestion = SuggestModel(modelName);
            Console.WriteLine("Modèles valides : " + string.Join(", ", AllowedModels));
            if (!string.IsNullOrEmpty(suggestion))
                Console.WriteLine($"Suggestion la plus proche : {suggestion}");
            return 4;
        }

        string? language = args.Length >= 3 ? (string.IsNullOrWhiteSpace(args[2]) ? null : args[2]) : "en";
        var outLrc = Path.ChangeExtension(audioPath, ".lrc");

        try
        {
            Info($"Audio     : {audioPath}");
            Info($"Modèle    : {modelName}");
            Info($"Langue    : {(language ?? "(auto)")}");
            Info($"Sortie LRC: {outLrc}");
            Console.WriteLine();

            var sw = Stopwatch.StartNew();
            Console.WriteLine(); // espace pour ne pas chevaucher la barre

            var segments = await Transcriber.TranscribeAsync(
                audioPath: audioPath,
                modelName: modelName,
                language: language,
                onInfo: msg => Console.WriteLine(msg)
            );

            LrcWriter.Write(segments, outLrc);

            sw.Stop();
            Console.WriteLine();
            Success($"Terminé en {sw.Elapsed:mm\\:ss} — {segments.Count} segments → {outLrc}");
            return 0;
        }
        catch (Exception ex)
        {
            Error($"{ex.GetType().Name}: {ex.Message}");
            return 3;
        }
    }

    private static void PrintUsage()
    {
        Console.WriteLine("Usage:");
        Console.WriteLine("  MLP.Transcriber.Console.exe \"C:\\music\\track.flac\" [model=small.en] [lang=en|\"\"(auto)]");
        Console.WriteLine();
        Console.WriteLine("Modèles valides: tiny.en, base.en, small.en, small, medium, large-v3");
        Console.WriteLine();
        Console.WriteLine("Exemples:");
        Console.WriteLine("  MLP.Transcriber.Console.exe \"C:\\music\\track.mp3\"");
        Console.WriteLine("  MLP.Transcriber.Console.exe \"/home/me/track.wav\" small");
        Console.WriteLine("  MLP.Transcriber.Console.exe \"/home/me/track.wav\" medium \"\"   (lang auto)");
    }

    private static void Info(string msg) => WriteColored($"[INFO] {msg}", ConsoleColor.Gray);
    private static void Success(string msg) => WriteColored($"[OK] {msg}", ConsoleColor.Green);
    private static void Error(string msg) => WriteColored($"[FATAL] {msg}", ConsoleColor.Red);

    private static void WriteColored(string msg, ConsoleColor color)
    {
        var prev = Console.ForegroundColor;
        Console.ForegroundColor = color;
        Console.WriteLine(msg);
        Console.ForegroundColor = prev;
    }

    // Suggestion basique par distance d'édition (Levenshtein) + prefix match
    private static string SuggestModel(string input)
    {
        string? best = null;
        int bestScore = int.MaxValue;

        foreach (var m in AllowedModels)
        {
            if (m.StartsWith(input, StringComparison.OrdinalIgnoreCase))
                return m;

            var d = EditDistance(input.ToLowerInvariant(), m.ToLowerInvariant());
            if (d < bestScore)
            {
                bestScore = d;
                best = m;
            }
        }

        // seuil arbitraire : si trop éloigné, on ne propose rien
        return bestScore <= 5 ? best! : "";
    }

    private static int EditDistance(string a, string b)
    {
        var dp = new int[a.Length + 1, b.Length + 1];
        for (int i = 0; i <= a.Length; i++) dp[i, 0] = i;
        for (int j = 0; j <= b.Length; j++) dp[0, j] = j;

        for (int i = 1; i <= a.Length; i++)
        {
            for (int j = 1; j <= b.Length; j++)
            {
                int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                dp[i, j] = Math.Min(
                    Math.Min(dp[i - 1, j] + 1, dp[i, j - 1] + 1), // del/ins
                    dp[i - 1, j - 1] + cost                        // sub
                );
            }
        }
        return dp[a.Length, b.Length];
    }
}
