using System.Reflection;

namespace ComplimentGenerator;

public class ComplimentEngine
{
    private readonly List<string> _compliments;
    private readonly HashSet<int> _shownToday = new();
    private readonly Random _random = new();

    public ComplimentEngine()
    {
        _compliments = LoadCompliments();
    }

    public int TotalAvailable => _compliments.Count;
    public int ShownToday => _shownToday.Count;
    public bool HasRemaining => _shownToday.Count < _compliments.Count;

    public (string Text, int Index)? GetNextCompliment()
    {
        if (!HasRemaining)
            return null;

        int index;
        do
        {
            index = _random.Next(_compliments.Count);
        } while (_shownToday.Contains(index));

        _shownToday.Add(index);
        return (_compliments[index], index);
    }

    public TimeSpan GetNextInterval()
    {
        int seconds = _random.Next(10, 61); // 1-60 seconds
        return TimeSpan.FromSeconds(seconds);
    }

    public void ResetDaily()
    {
        _shownToday.Clear();
    }

    private static List<string> LoadCompliments()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = assembly.GetManifestResourceNames()
            .First(n => n.EndsWith("compliments.txt"));

        using var stream = assembly.GetManifestResourceStream(resourceName)!;
        using var reader = new StreamReader(stream);

        var compliments = new List<string>();
        while (reader.ReadLine() is { } line)
        {
            var trimmed = line.Trim();
            if (!string.IsNullOrEmpty(trimmed))
                compliments.Add(trimmed);
        }

        return compliments;
    }
}
