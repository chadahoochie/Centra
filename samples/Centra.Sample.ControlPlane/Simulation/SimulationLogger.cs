using System;

namespace Centra.Sample.ControlPlane.Simulation;

public static class SimulationLogger
{
    public static void Header(string title)
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine();
        Console.WriteLine("================================================================================");
        Console.WriteLine($" {title}");
        Console.WriteLine("================================================================================");
        Console.ResetColor();
    }

    public static void PhaseHeader(int stepNumber, string name)
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine();
        Console.WriteLine($"--- STEP {stepNumber}: {name} ---");
        Console.ResetColor();
    }

    public static void Info(string message)
    {
        Console.ForegroundColor = ConsoleColor.Gray;
        Console.WriteLine($"[{DateTime.UtcNow:HH:mm:ss.fff}] {message}");
        Console.ResetColor();
    }

    public static void Success(string message)
    {
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"[{DateTime.UtcNow:HH:mm:ss.fff}] ✓ {message}");
        Console.ResetColor();
    }

    public static void Warning(string message)
    {
        Console.ForegroundColor = ConsoleColor.DarkYellow;
        Console.WriteLine($"[{DateTime.UtcNow:HH:mm:ss.fff}] ⚠ {message}");
        Console.ResetColor();
    }

    public static void Error(string message)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"[{DateTime.UtcNow:HH:mm:ss.fff}] ✗ {message}");
        Console.ResetColor();
    }
}
