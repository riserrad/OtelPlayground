namespace OTelWizard;

public static class ConsoleRenderer
{
    public static void WriteTrace(string message)
    {
        WriteSection(ConsoleColor.Cyan, "TRACE", message);
    }

    public static void WriteMetric(string message)
    {
        WriteSection(ConsoleColor.Yellow, "METRIC", message);
    }

    public static void WriteLog(string message)
    {
        WriteSection(ConsoleColor.Green, "LOG", message);
    }

    public static void WriteWizard(string explanation)
    {
        Console.ForegroundColor = ConsoleColor.DarkMagenta;
        Console.WriteLine($"  >> {explanation}");
        Console.ResetColor();
    }

    public static void WriteSeparator()
    {
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine("  ─────────────────────────────────────────────────────");
        Console.ResetColor();
    }

    private static void WriteSection(ConsoleColor color, string label, string message)
    {
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.Write($"  [{DateTime.Now:HH:mm:ss}] ");
        Console.ForegroundColor = color;
        Console.Write($"[{label}] ");
        Console.ResetColor();
        Console.WriteLine(message);
    }
}
