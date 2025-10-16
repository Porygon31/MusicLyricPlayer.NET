using System;
using System.Text;

namespace MLP.Transcriber.ConsoleApp;

/// <summary>
/// Affichage d'une barre de progression ASCII sur une seule ligne.
/// Utilise '\r' pour réécrire la même ligne (pas de saut de ligne).
/// </summary>
public static class ConsoleProgress
{
    /// <summary>
    /// Dessine/rafraîchit une barre type: "[####------] 42%" avec un préfixe.
    /// </summary>
    /// <param name="percent">0..100</param>
    /// <param name="prefix">Ex: "[Download]" ou "[ASR]"</param>
    /// <param name="width">Largeur de la barre (# et -)</param>
    public static void Draw(int percent, string prefix, int width = 28)
    {
        if (percent < 0) percent = 0;
        if (percent > 100) percent = 100;

        int filled = (int)Math.Round(percent * width / 100.0);
        var sb = new StringBuilder();
        sb.Append('\r'); // retour début de ligne
        sb.Append(prefix);
        sb.Append(' ');
        sb.Append('[');
        sb.Append(new string('#', filled));
        sb.Append(new string('-', Math.Max(0, width - filled)));
        sb.Append(']');
        sb.Append(' ');
        sb.Append(percent.ToString("00"));
        sb.Append('%');

        Console.Write(sb.ToString());
        // Pas de Console.WriteLine ici pour garder la même ligne
    }

    /// <summary>
    /// Termine la barre proprement en affichant 100% et en ajoutant un saut de ligne.
    /// </summary>
    public static void Finish(string prefix, int width = 28)
    {
        Draw(100, prefix, width);
        Console.WriteLine();
    }
}
