namespace ComplimentGenerator;

public class DailyReport
{
    public int Generated { get; private set; }
    public int Liked { get; private set; }
    public int Disliked { get; private set; }
    public int Skipped { get; private set; }

    public void RecordGenerated() => Generated++;
    public void RecordLiked() => Liked++;
    public void RecordDisliked() => Disliked++;
    public void RecordSkipped() => Skipped++;

    public string Format()
    {
        return $"""

        ╔══════════════════════════════════╗
        ║        Daily Report              ║
        ╠══════════════════════════════════╣
        ║  Compliments generated: {Generated,5}     ║
        ║  Liked:                 {Liked,5}     ║
        ║  Disliked:              {Disliked,5}     ║
        ║  Skipped:               {Skipped,5}     ║
        ╚══════════════════════════════════╝
        """;
    }

    public void Reset()
    {
        Generated = 0;
        Liked = 0;
        Disliked = 0;
        Skipped = 0;
    }
}
