namespace SpaceStationMonitor;

[Serializable]
public class GeneralSpaceStationException : Exception
{
    public GeneralSpaceStationException()
    {
    }

    public GeneralSpaceStationException(string? message) : base(message)
    {
    }

    public GeneralSpaceStationException(string? message, Exception? innerException) : base(message, innerException)
    {
    }
}
