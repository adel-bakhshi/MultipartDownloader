namespace MultipartDownloader.Core;

public class DownloadSpeed
{
    #region Properties

    public long ReceivedBytes { get; set; }
    public double TotalMilliseconds { get; set; }

    public long BytesPerSecondSpeed => ReceivedBytes == 0 || TotalMilliseconds < 1 ? 0 : (long)(ReceivedBytes * 1000 / TotalMilliseconds);

    #endregion Properties
}