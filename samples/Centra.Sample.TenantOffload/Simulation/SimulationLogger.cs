namespace Centra.Sample.TenantOffload.Simulation;

public static class SimulationLogger
{
    public static void Log(string message, ConsoleColor color = ConsoleColor.Gray)
    {
        var prev = Console.ForegroundColor;
        Console.ForegroundColor = color;
        Console.WriteLine($"[{DateTime.UtcNow:HH:mm:ss.fff}] {message}");
        Console.ForegroundColor = prev;
    }

    public static void Header(string title)
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("================================================================================");
        Console.WriteLine($" {title} ");
        Console.WriteLine("================================================================================");
        Console.ResetColor();
    }

    public static void SubHeader(string title)
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($"\n--- {title} ---");
        Console.ResetColor();
    }
}
