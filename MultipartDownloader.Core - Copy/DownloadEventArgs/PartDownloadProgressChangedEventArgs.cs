namespace MultipartDownloader.Core.DownloadEventArgs;

public class PartDownloadProgressChangedEventArgs
{
    #region Properties

    public int PartNumber { get; set; }
    public long StartRange { get; }
    public long EndRange { get; }
    public long ReceivedBytesSize { get; }
    public long BytesPerSecondSpeed { get; }
    public long AverageBytesPerSecondSpeed { get; }
    public bool IsCompleted { get; set; }

    public long TotalBytesSize => EndRange - StartRange;
    public double ProgressPercentage => TotalBytesSize > 0 ? ReceivedBytesSize / (double)TotalBytesSize * 100 : 0;

    #endregion Properties

    public PartDownloadProgressChangedEventArgs(
        int partNumber,
        long startRange,
        long endRange,
        long receivedBytesSize,
        long bytesPerSecondSpeed,
        long averageBytesPerSecondSpeed,
        bool isCompleted)
    {
        PartNumber = partNumber;
        StartRange = startRange;
        EndRange = endRange;
        ReceivedBytesSize = receivedBytesSize;
        BytesPerSecondSpeed = bytesPerSecondSpeed;
        AverageBytesPerSecondSpeed = averageBytesPerSecondSpeed;
        IsCompleted = isCompleted;
    }
}