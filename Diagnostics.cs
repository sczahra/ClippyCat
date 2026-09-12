using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace MarmaladeDesktopPet;

internal static class PetDiagnostics
{
    private static readonly object Sync = new();
    private static readonly Queue<string> RecentLines = new();
    private const int RecentLimit = 300;

    public static string LogPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "MarmaladeDesktopPet",
        "diagnostics.log"
    );

    public static void Initialize(string appVersion)
    {
        try
        {
            string? folder = Path.GetDirectoryName(LogPath);

            if (!string.IsNullOrWhiteSpace(folder))
                Directory.CreateDirectory(folder);
        }
        catch
        {
            // Diagnostics should never stop the pet from starting.
        }

        Log(
            "SESSION",
            $"start version={appVersion} pid={Environment.ProcessId} os={Environment.OSVersion} dotnet={Environment.Version}"
        );
        Log("DIAG_PATH", LogPath);
    }

    public static void Log(string category, string message)
    {
        string line = $"[DIAG {DateTime.Now:HH:mm:ss.fff}] {category} {message}";

        lock (Sync)
        {
            RecentLines.Enqueue(line);

            while (RecentLines.Count > RecentLimit)
                RecentLines.Dequeue();

            Console.WriteLine(line);

            try
            {
                File.AppendAllText(LogPath, line + Environment.NewLine);
            }
            catch
            {
                // Logging failure must never crash ClippyCat.
            }
        }
    }

    public static void Error(string category, Exception ex)
    {
        Log(
            category,
            $"type={ex.GetType().Name} message={Sanitize(ex.Message)} stack={Sanitize(ex.StackTrace ?? string.Empty)}"
        );
    }

    public static string GetRecentText(int maxLines)
    {
        lock (Sync)
        {
            int skip = Math.Max(0, RecentLines.Count - Math.Max(1, maxLines));
            return string.Join(Environment.NewLine, RecentLines.Skip(skip));
        }
    }

    private static string Sanitize(string text)
    {
        return text.Replace('\r', ' ').Replace('\n', ' ').Trim();
    }
}
