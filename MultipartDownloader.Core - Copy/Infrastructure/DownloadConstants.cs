namespace MultipartDownloader.Core.Infrastructure;

public static class DownloadConstants
{
    public const long ProgressThreshold = 1024 * 1024; // 1 MB
    public const int NumberOfSpeedSamples = 101;
    public const int NumberOfMedianSamples = 100;
}