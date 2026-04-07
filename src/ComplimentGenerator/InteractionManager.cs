namespace ComplimentGenerator;

public enum FeedbackResult
{
    Liked,
    Disliked,
    Skipped
}

public class InteractionManager
{
    /// <summary>
    /// Waits for user to press L (like) or D (dislike) until the cancellation token fires.
    /// Returns Skipped if no valid input before cancellation.
    /// </summary>
    public async Task<FeedbackResult> WaitForFeedbackAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                if (Console.KeyAvailable)
                {
                    var key = Console.ReadKey(intercept: true);
                    switch (char.ToUpperInvariant(key.KeyChar))
                    {
                        case 'L':
                            return FeedbackResult.Liked;
                        case 'D':
                            return FeedbackResult.Disliked;
                    }
                }

                await Task.Delay(100, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Timer fired before user responded
        }

        return FeedbackResult.Skipped;
    }
}
