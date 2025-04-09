namespace MultipartDownloader.Core.DownloadEventArgs;

public class DownloadFinishedEventArgs : EventArgs
{
    #region Properties

    public bool IsCompleted { get; set; }
    public bool IsStopped { get; set; }
    public Exception? Error { get; set; }

    #endregion Properties

    public DownloadFinishedEventArgs()
    {
    }

    public DownloadFinishedEventArgs(Exception error)
    {
        Error = error;
        IsCompleted = IsStopped = false;
    }
}