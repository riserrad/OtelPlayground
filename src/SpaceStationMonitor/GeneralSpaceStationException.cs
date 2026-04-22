namespace SpaceStationMonitor;

[Serializable]
internal class GeneralSpaceStationException : Exception
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
