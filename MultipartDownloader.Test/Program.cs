using MultipartDownloader.Core;

namespace MultipartDownloader.Test;

internal class Program
{
    private static async Task Main(string[] args)
    {
        const string url = "https://dl2.soft98.ir/soft/g/Glary.Utilities.Pro.6.24.0.28.rar?1744038389";
        const string fileName = "Glary.Utilities.Pro.6.24.0.28.rar";
        var desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        var filePath = Path.Combine(desktopPath, fileName);

        var config = new DownloadConfiguration
        {
            Url = url,
            DownloadPartOutputDirectory = desktopPath,
            FilePath = filePath,
            PartCount = 8,
            ReserveStorageBeforeDownload = false,
            RetryCountOnFailure = 3,
            MaximumBytesPerSecond = 64 * 1024
        };

        var downloader = new DownloadService(config);
        downloader.DownloadStarted += DownloaderOnDownloadStarted;
        downloader.DownloadProgressChanged += DownloaderOnDownloadProgressChanged;
        downloader.PartDownloadProgressChanged += DownloaderOnPartDownloadProgressChanged;
        downloader.DownloadCompleted += DownloaderOnDownloadCompleted;

        await downloader.DownloadFileAsync();

        Console.ReadKey();
    }

    private static void DownloaderOnDownloadStarted(object? sender, EventArgs e)
    {
        Console.WriteLine("Download started...");
        Console.WriteLine("==================================================");
        Console.WriteLine("");
    }

    private static void DownloaderOnDownloadProgressChanged(object? sender, Core.DownloadEventArgs.DownloadProgressChangedEventArgs e)
    {
        Console.WriteLine("Download progress changed...");
        Console.WriteLine($"Progress: {e.ProgressPercentage}% - Current speed: {e.BytesPerSecondSpeed} bytes/s - Average speed: {e.AverageBytesPerSecondSpeed} bytes/s");
        Console.WriteLine($"Total file size: {e.TotalBytesSize} bytes - Downloaded size: {e.ReceivedBytesSize} bytes");
        Console.WriteLine("==================================================");
        Console.WriteLine("");
    }

    private static void DownloaderOnPartDownloadProgressChanged(object? sender, Core.DownloadEventArgs.PartDownloadProgressChangedEventArgs e)
    {
    }

    private static void DownloaderOnDownloadCompleted(object? sender, Core.DownloadEventArgs.DownloadFinishedEventArgs e)
    {
        Console.WriteLine("Download completed...");
        Console.WriteLine("==================================================");
        Console.WriteLine("");
    }
}