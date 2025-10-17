using System;
using System.Text;

namespace MLP.Transcriber.ConsoleApp;

/// <summary>
/// Barre de progression ASCII sur une seule ligne, avec couleurs et suffix (ex: vitesse, ETA).
/// Utilise '\r' pour réécrire la même ligne.
/// </summary>
public static class ConsoleProgress
{
    /// <summary>
    /// Dessine/rafraîchit une barre type: "[####------] 42%  |  5.3 MB/s  ETA 00:18"
    /// </summary>
    /// <param name="percent">0..100</param>
    /// <param name="prefix">Ex: "[Download]" ou "[ASR]"</param>
    /// <param name="suffix">Texte optionnel à droite (vitesse, ETA, etc.)</param>
    /// <param name="width">Largeur de la barre (# et -)</param>
    /// <param name="filledColor">Couleur du remplissage</param>
    /// <param name="emptyColor">Couleur de la partie vide</param>
    /// <param name="textColor">Couleur du texte</param>
    public static void Draw(
        int percent,
        string prefix,
        string? suffix = null,
        int width = 28,
        ConsoleColor filledColor = ConsoleColor.Green,
        ConsoleColor emptyColor = ConsoleColor.DarkGray,
        ConsoleColor textColor = ConsoleColor.Gray)
    {
        if (percent < 0) percent = 0;
        if (percent > 100) percent = 100;

        int filled = (int)Math.Round(percent * width / 100.0);
        var sb = new StringBuilder();
        sb.Append('\r'); // retour début de ligne
        sb.Append(prefix);
        sb.Append(' ');
        sb.Append('[');

        // écrire la partie remplie en couleur
        WriteColored(sb.ToString(), textColor);
        WriteColored(new string('#', filled), filledColor);
        WriteColored(new string('-', Math.Max(0, width - filled)), emptyColor);

        WriteColored("] ", textColor);
        WriteColored(percent.ToString("00") + "%", textColor);

        if (!string.IsNullOrWhiteSpace(suffix))
        {
            WriteColored("  |  " + suffix, textColor);
        }
    }

    /// <summary>Termine la barre proprement en affichant 100% et ajoutant un saut de ligne.</summary>
    public static void Finish(string prefix, string? suffix = null, int width = 28)
    {
        Draw(100, prefix, suffix, width);
        Console.WriteLine();
        Console.ResetColor();
    }

    private static void WriteColored(string text, ConsoleColor color)
    {
        var prev = Console.ForegroundColor;
        Console.ForegroundColor = color;
        Console.Write(text);
        Console.ForegroundColor = prev;
    }

    /// <summary>Formate une durée (secondes) en "hh:mm:ss" ou "mm:ss".</summary>
    public static string FormatDuration(double seconds)
    {
        if (seconds < 0 || double.IsInfinity(seconds) || double.IsNaN(seconds)) return "--:--";
        var ts = TimeSpan.FromSeconds(seconds);
        if (ts.TotalHours >= 1)
            return $"{(int)ts.TotalHours:00}:{ts.Minutes:00}:{ts.Seconds:00}";
        return $"{ts.Minutes:00}:{ts.Seconds:00}";
    }
}
