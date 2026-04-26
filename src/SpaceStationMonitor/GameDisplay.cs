namespace SpaceStationMonitor;

public class GameDisplay
{
    private const int InnerWidth = 50;

    private string? _currentEvent;
    private string? _currentWarning;
    private string? _lastRepairMessage;

    public void SetEvent(string? message) => _currentEvent = message;
    public void SetWarning(string? message) => _currentWarning = message;
    public void SetRepairMessage(string? message) => _lastRepairMessage = message;

    public static void RenderStartScreen()
    {
        ClearIfInteractive();
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("╔══════════════════════════════════════════════════╗");
        Console.WriteLine("║          SPACE STATION MONITOR v1.0              ║");
        Console.WriteLine("╠══════════════════════════════════════════════════╣");
        Console.WriteLine("║                                                  ║");
        Console.WriteLine("║             Press any key to start               ║");
        Console.WriteLine("║                                                  ║");
        Console.WriteLine("╠══════════════════════════════════════════════════╣");
        Console.WriteLine("║                    Q to quit                     ║");
        Console.WriteLine("╚══════════════════════════════════════════════════╝");
        Console.ResetColor();
    }

    public void Render(Station station)
    {
        ClearIfInteractive();
        var uptime = DateTime.UtcNow - station.StartTime;
        var hullStr = $"{station.HullIntegrity:F0}%";
        var uptimeStr = $"{(int)uptime.TotalMinutes}m {uptime.Seconds}s";

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("╔══════════════════════════════════════════════════╗");
        WritePaddedLine("          SPACE STATION MONITOR v1.0              ");

        Console.ForegroundColor = station.HullIntegrity < 30 ? ConsoleColor.Red : ConsoleColor.Cyan;
        WritePaddedLine($"          Hull Integrity: {hullStr,-24}");

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("╠══════════════════════════════════════════════════╣");

        for (int i = 0; i < station.Subsystems.Length; i++)
        {
            var sub = station.Subsystems[i];
            var barLength = (int)(sub.Health / 100.0 * 16);
            var bar = new string('\u2588', barLength) + new string('\u2591', 16 - barLength);
            var warning = sub.IsCritical ? " \u26A0" : "  ";
            var healthStr = $"{sub.Health:F0}%";

            Console.ForegroundColor = sub.Health switch
            {
                < 15 => ConsoleColor.Red,
                < 30 => ConsoleColor.Yellow,
                _ => ConsoleColor.Green
            };

            WritePaddedLine($"  [{i + 1}] {sub.Name,-16} {bar}  {healthStr,-4}{warning}   ");
        }

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("╠══════════════════════════════════════════════════╣");

        var hasMessage = false;
        if (_currentWarning != null)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            WritePaddedLine($"  \u26A0 {_currentWarning}");
            hasMessage = true;
        }
        if (_currentEvent != null)
        {
            Console.ForegroundColor = ConsoleColor.Magenta;
            WritePaddedLine($"  \u2604 {_currentEvent}");
            hasMessage = true;
        }
        if (_lastRepairMessage != null)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            WritePaddedLine($"  > {_lastRepairMessage}");
            hasMessage = true;
        }
        if (!hasMessage)
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            WritePaddedLine("  All systems nominal.");
        }

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("╠══════════════════════════════════════════════════╣");
        WritePaddedLine("  [1-4] Select   [R] Repair   [E] Emergency Pwr   ");
        WritePaddedLine($"  [Q] Quit   Emerg Pwr: {station.EmergencyPowerRemaining,-2} |  Repairs: {station.RepairsRemainingThisCycle,-2} left");
        WritePaddedLine($"  Cycle: {station.CycleCount,-9}|  Uptime: {uptimeStr,-21}");
        Console.WriteLine("╚══════════════════════════════════════════════════╝");
        Console.ResetColor();
    }

    private static void WritePaddedLine(string content)
    {
        var inner = content.Length > InnerWidth ? content[..InnerWidth] : content;
        Console.WriteLine($"║{inner}{new string(' ', InnerWidth - inner.Length)}║");
    }

    // Console.Clear() throws IOException when stdout is redirected (xUnit test runner).
    // Skip the clear in that case so the game logic can be exercised in tests.
    private static void ClearIfInteractive()
    {
        if (Console.IsOutputRedirected) return;
        try { Console.Clear(); }
        catch (IOException) { }
    }
}
