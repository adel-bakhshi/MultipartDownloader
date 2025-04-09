using MultipartDownloader.Core.Infrastructure;

namespace MultipartDownloader.Core;

public class DownloadConfiguration : NotifyPropertyChangedBase
{
    #region Private fields

    private long _maximumBytesPerSecond;

    #endregion Private fields

    #region Properties

    public string Url { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public string DownloadPartOutputDirectory { get; set; } = string.Empty;
    public bool ReserveStorageBeforeDownload { get; set; }
    public int PartCount { get; set; }

    public long MaximumBytesPerSecond
    {
        get => _maximumBytesPerSecond;
        set => SetField(ref _maximumBytesPerSecond, value);
    }

    public long RetryCountOnFailure { get; set; }

    #endregion Properties
}