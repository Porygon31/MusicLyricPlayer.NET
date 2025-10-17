using System;
using System.Threading;

namespace MLP.Transcriber.ConsoleApp;

/// <summary>
/// Petit spinner animé pour afficher les chargements (ex: "Chargement du modèle Whisper...")
/// </summary>
public sealed class Spinner : IDisposable
{
    private readonly string _message;
    private readonly Thread _thread;
    private bool _running = true;

    private static readonly char[] Sequence = { '⠋', '⠙', '⠹', '⠸', '⠼', '⠴', '⠦', '⠧', '⠇', '⠏' };

    public Spinner(string message)
    {
        _message = message;
        _thread = new Thread(Spin);
        _thread.Start();
    }

    private void Spin()
    {
        int counter = 0;
        while (_running)
        {
            var frame = Sequence[counter++ % Sequence.Length];
            Console.Write($"\r{frame} {_message}");
            Thread.Sleep(80);
        }
    }

    public void Stop(string? doneMessage = null)
    {
        _running = false;
        Thread.Sleep(100);
        Console.Write("\r");
        if (!string.IsNullOrWhiteSpace(doneMessage))
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"{doneMessage}");
            Console.ResetColor();
        }
        else
        {
            Console.WriteLine();
        }
    }

    public void Dispose() => Stop();
}
