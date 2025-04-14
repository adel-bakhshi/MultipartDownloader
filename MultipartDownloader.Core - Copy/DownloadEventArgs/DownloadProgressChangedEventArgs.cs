namespace MultipartDownloader.Core.DownloadEventArgs;

public class DownloadProgressChangedEventArgs
{
    #region Properties

    public long TotalBytesSize { get; }
    public long ReceivedBytesSize { get; }
    public long BytesPerSecondSpeed { get; }
    public long AverageBytesPerSecondSpeed { get; }

    public double ProgressPercentage => TotalBytesSize > 0 ? ReceivedBytesSize / (double)TotalBytesSize * 100 : 0;

    #endregion Properties

    public DownloadProgressChangedEventArgs(
        long totalBytesSize,
        long receivedBytesSize,
        long bytesPerSecondSpeed,
        long averageBytesPerSecondSpeed)
    {
        TotalBytesSize = totalBytesSize;
        ReceivedBytesSize = receivedBytesSize;
        BytesPerSecondSpeed = bytesPerSecondSpeed;
        AverageBytesPerSecondSpeed = averageBytesPerSecondSpeed;
    }
}