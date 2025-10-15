using System.Text;

namespace MLP.Transcriber.ConsoleApp;

/// <summary>
/// Écrit un fichier .lrc à partir d'une liste de segments (ms / ms / texte).
/// Format attendu par la majorité des lecteurs de paroles synchronisées.
/// </summary>
public static class LrcWriter
{
    public static void Write(IReadOnlyList<Transcriber.Segment> segments, string outputPath)
    {
        var dir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var sb = new StringBuilder(segments.Count * 32);

        foreach (var seg in segments)
        {
            if (string.IsNullOrWhiteSpace(seg.Text)) continue;

            sb.Append('[').Append(ToLrcTimestamp(seg.StartMs)).Append(']');
            sb.Append(' ').AppendLine(seg.Text.Trim());
        }

        File.WriteAllText(outputPath, sb.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    /// <summary>
    /// Convertit millisecondes -> timestamp LRC [mm:ss.cs]
    /// </summary>
    private static string ToLrcTimestamp(int ms)
    {
        var totalSeconds = ms / 1000;
        var minutes = totalSeconds / 60;
        var seconds = totalSeconds % 60;
        var centis = (ms % 1000) / 10; // centi-secondes

        return $"{minutes:00}:{seconds:00}.{centis:00}";
    }
}
