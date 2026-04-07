namespace SpaceStationMonitor;

public class GameDisplay
{
    private string? _currentEvent;
    private string? _currentWarning;
    private string? _lastRepairMessage;

    public void SetEvent(string? message) => _currentEvent = message;
    public void SetWarning(string? message) => _currentWarning = message;
    public void SetRepairMessage(string? message) => _lastRepairMessage = message;

    public void Render(Station station)
    {
        Console.Clear();
        var uptime = DateTime.UtcNow - station.StartTime;
        var hullStr = $"{station.HullIntegrity:F0}%";
        var uptimeStr = $"{(int)uptime.TotalMinutes}m {uptime.Seconds}s";

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("╔══════════════════════════════════════════════════╗");
        Console.WriteLine("║          SPACE STATION MONITOR v1.0              ║");

        Console.ForegroundColor = station.HullIntegrity < 30 ? ConsoleColor.Red : ConsoleColor.Cyan;
        Console.WriteLine($"║          Hull Integrity: {hullStr,-25}║");

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

            Console.WriteLine($"║  [{i + 1}] {sub.Name,-16} {bar}  {healthStr,-4}{warning}  ║");
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
        Console.WriteLine("║  [1-4] Select   [R] Repair   [E] Emergency Pwr  ║");
        Console.WriteLine($"║  [Q] Quit       Emergency Power: {station.EmergencyPowerRemaining} left          ║");
        Console.WriteLine($"║  Cycle: {station.CycleCount,-7}|  Uptime: {uptimeStr,-19}║");
        Console.WriteLine("╚══════════════════════════════════════════════════╝");
        Console.ResetColor();
    }

    private static void WritePaddedLine(string content)
    {
        // Box is 50 chars wide including borders
        const int boxWidth = 50;
        var inner = content.Length > boxWidth - 4 ? content[..(boxWidth - 4)] : content;
        var padding = boxWidth - 2 - inner.Length; // 2 for ║ borders
        Console.WriteLine($"║{inner}{new string(' ', Math.Max(0, padding))}║");
    }
}
