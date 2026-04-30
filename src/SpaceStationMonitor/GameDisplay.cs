using SpaceStationMonitor.Sampling;

namespace SpaceStationMonitor;

public class GameDisplay
{
    private const int InnerWidth = 50;

    private string? _currentEvent;
    private string? _currentWarning;
    private string? _lastRepairMessage;
    private string? _currentAchievement;

    public void SetEvent(string? message) => _currentEvent = message;
    public void SetWarning(string? message) => _currentWarning = message;
    public void SetRepairMessage(string? message) => _lastRepairMessage = message;
    public void SetAchievement(string? message) => _currentAchievement = message;

    public static void RenderModeScreen()
    {
        ClearIfInteractive();
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("╔══════════════════════════════════════════════════╗");
        WritePaddedLine("          SPACE STATION MONITOR v1.0              ");
        Console.WriteLine("╠══════════════════════════════════════════════════╣");
        WritePaddedLine("                                                  ");
        RenderPickerRow("1", "Just Playing", ConsoleColor.Green);
        RenderExplainerRow("Relax. Manage your station. Survive cascades.");
        WritePaddedLine("                                                  ");
        RenderPickerRow("2", "Playing + Learning Observability", ConsoleColor.Cyan);
        RenderExplainerRow("A hidden bug is sabotaging your telemetry.");
        RenderExplainerRow("Find it via the Aspire dashboard.");
        WritePaddedLine("                                                  ");
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("╠══════════════════════════════════════════════════╣");
        WritePaddedLine("                    Q to quit                     ");
        Console.WriteLine("╚══════════════════════════════════════════════════╝");
        Console.ResetColor();
    }

    public static void RenderDifficultyScreen(GameMode mode)
    {
        _ = mode;
        ClearIfInteractive();
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("╔══════════════════════════════════════════════════╗");
        WritePaddedLine("              SELECT DIFFICULTY                   ");
        Console.WriteLine("╠══════════════════════════════════════════════════╣");
        WritePaddedLine("                                                  ");
        RenderPickerRow("1", "Tutorial", ConsoleColor.Green);
        RenderExplainerRow("Generous repairs and slow degradation.");
        WritePaddedLine("                                                  ");
        RenderPickerRow("2", "Normal", ConsoleColor.Cyan);
        RenderExplainerRow("Standard pace.");
        WritePaddedLine("                                                  ");
        RenderPickerRow("3", "Hard", ConsoleColor.Yellow);
        RenderExplainerRow("Tight repair budget; events bite.");
        WritePaddedLine("                                                  ");
        RenderPickerRow("4", "Expert", ConsoleColor.Red);
        RenderExplainerRow("One repair per cycle. Nothing is forgiving.");
        WritePaddedLine("                                                  ");
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("╠══════════════════════════════════════════════════╣");
        WritePaddedLine("                    Q to quit                     ");
        Console.WriteLine("╚══════════════════════════════════════════════════╝");
        Console.ResetColor();
    }

    public void Render(Station station, int selectedSubsystem, SampleRegimeIndicator? indicator = null)
    {
        ClearIfInteractive();
        var hullStr = $"{station.HullIntegrity:F0}%";

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("╔══════════════════════════════════════════════════╗");
        WritePaddedLine("          SPACE STATION MONITOR v1.0              ");

        var hullColor = station.HullIntegrity switch
        {
            < 15 => ConsoleColor.Red,
            < 30 => ConsoleColor.Yellow,
            _ => ConsoleColor.Green
        };
        var hullBarLength = (int)(station.HullIntegrity / 100.0 * 16);
        var hullBar = new string('█', hullBarLength) + new string('░', 16 - hullBarLength);
        WritePaddedSegments(
            ("      Hull Integrity: ", ConsoleColor.Cyan),
            (hullBar, hullColor),
            ("  ", hullColor),
            ($"{hullStr,-4}", hullColor),
            ("      ", ConsoleColor.Cyan));

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("╠══════════════════════════════════════════════════╣");

        for (int i = 0; i < station.Subsystems.Length; i++)
        {
            var sub = station.Subsystems[i];
            var barLength = (int)(sub.Health / 100.0 * 16);
            var bar = new string('█', barLength) + new string('░', 16 - barLength);
            var warning = sub.IsCritical ? " ⚠" : "  ";
            var healthStr = $"{sub.Health:F0}%";
            var healthColor = sub.Health switch
            {
                < 15 => ConsoleColor.Red,
                < 30 => ConsoleColor.Yellow,
                _ => ConsoleColor.Green
            };

            if (i == selectedSubsystem)
            {
                WritePaddedSegments(
                    ("▶ ", ConsoleColor.Cyan),
                    ($"[{i + 1}] {sub.Name,-16} {bar}  {healthStr,-4}{warning}   ", healthColor));
            }
            else
            {
                Console.ForegroundColor = healthColor;
                WritePaddedLine($"  [{i + 1}] {sub.Name,-16} {bar}  {healthStr,-4}{warning}   ");
            }
        }

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("╠══════════════════════════════════════════════════╣");

        var hasMessage = false;
        if (_currentWarning != null)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            WritePaddedLine($"  ⚠ {_currentWarning}");
            hasMessage = true;
        }
        if (_currentEvent != null)
        {
            Console.ForegroundColor = ConsoleColor.Magenta;
            WritePaddedLine($"  ☄ {_currentEvent}");
            hasMessage = true;
        }
        if (_currentAchievement != null)
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            WritePaddedLine($"  ★ {_currentAchievement}");
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
        WritePaddedLine("  [1-4] Select [R] Repair [C] Cancel [E] Emerg Pwr");
        RenderFooterCounters(station, indicator);
        RenderHandsRow(station);
        Console.WriteLine("╚══════════════════════════════════════════════════╝");
        Console.ResetColor();
    }

    // Footer column budget: 19 + 2 + 9 + 8 = 38 (+12 trailing pad) = 50.
    // Badge color is regime-dependent; everything else is frame Cyan.
    private static void RenderFooterCounters(Station station, SampleRegimeIndicator? indicator)
    {
        var (badgeText, badgeColor) = indicator?.CurrentBadge ?? ("        ", ConsoleColor.DarkGray);
        WritePaddedSegments(
            ("  [Q] Quit  Emerg: ", ConsoleColor.Cyan),
            ($"{station.EmergencyPowerRemaining,-2}", ConsoleColor.Cyan),
            ("    Smp: ", ConsoleColor.Cyan),
            ($"{badgeText,-8}", badgeColor));
    }

    // Yellow at saturation gives a glance-at-cell affordance: the player can tell
    // whether pressing R will succeed without parsing the n/cap numbers.
    private static void RenderHandsRow(Station station)
    {
        var inFlight = station.ActiveRepairs.InFlightCount;
        var capacity = inFlight + station.ActiveRepairs.AvailableSlots;
        var handsColor = inFlight >= capacity ? ConsoleColor.Yellow : ConsoleColor.White;
        WritePaddedSegments(
            ("  Cycle: ", ConsoleColor.Cyan),
            ($"{station.CycleCount,-4}", ConsoleColor.White),
            ("|  Score: ", ConsoleColor.Cyan),
            ($"{station.Score,-7}", ConsoleColor.White),
            ("|  Hands: ", ConsoleColor.Cyan),
            ($"{inFlight}/{capacity}", handsColor));
    }

    // Splash picker row: "  [N] {modeName}" with mode label colored, padded to 50 cols.
    private static void RenderPickerRow(string number, string modeName, ConsoleColor labelColor)
    {
        WritePaddedSegments(
            ($"  [{number}] ", ConsoleColor.Cyan),
            (modeName, labelColor));
    }

    // Splash explainer row sits under the picker row and renders dim grey for visual hierarchy.
    private static void RenderExplainerRow(string text)
    {
        WritePaddedSegments(
            ("      ", ConsoleColor.Cyan),
            (text, ConsoleColor.DarkGray));
    }

    private static void WritePaddedLine(string content)
    {
        var inner = content.Length > InnerWidth ? content[..InnerWidth] : content;
        Console.WriteLine($"║{inner}{new string(' ', InnerWidth - inner.Length)}║");
    }

    /// <summary>
    /// Writes a single inner row composed of multiple colored segments. Caller is responsible
    /// for sizing segments so the sum matches <see cref="InnerWidth"/>; the method pads with
    /// spaces when shorter and truncates the last segment when longer.
    /// </summary>
    /// <param name="segments">Ordered (text, foreground color) pairs rendered left-to-right.</param>
    private static void WritePaddedSegments(params (string text, ConsoleColor color)[] segments)
    {
        var prevColor = Console.ForegroundColor;

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.Write("║");

        int written = 0;
        foreach (var (text, color) in segments)
        {
            var remaining = InnerWidth - written;
            if (remaining <= 0) break;
            var slice = text.Length > remaining ? text[..remaining] : text;
            Console.ForegroundColor = color;
            Console.Write(slice);
            written += slice.Length;
        }

        if (written < InnerWidth)
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.Write(new string(' ', InnerWidth - written));
        }

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("║");

        Console.ForegroundColor = prevColor;
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
