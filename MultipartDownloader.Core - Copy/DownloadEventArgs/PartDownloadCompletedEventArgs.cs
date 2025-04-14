namespace MultipartDownloader.Core.DownloadEventArgs;

public class PartDownloadCompletedEventArgs
{
    #region Properties

    public bool IsCompleted { get; set; }
    public DownloadPart DownloadPart { get; set; } = null!;
    public Exception? Error { get; set; }

    #endregion Properties
}