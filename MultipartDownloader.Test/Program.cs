using MultipartDownloader.Core;

namespace MultipartDownloader.Test;

internal class Program
{
    private static bool _isPaused;
    private static DownloadPackage? _downloadPackage;
    private static bool _isMerging;
    private static bool _isMerged;

    private static async Task Main(string[] args)
    {
        const string url = "https://dl2.soft98.ir/soft/a/AnyDesk.9.5.5.zip?1749289119";
        var desktopDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        var configuration = GetDownloadConfiguration(desktopDirectory);
        var downloadService = GetDownloadService(configuration);

        var filePath = Path.Combine(desktopDirectory, "AnyDesk.9.5.5.zip");
        _ = downloadService.DownloadFileTaskAsync(url, filePath);

        while (!_isMerged)
        {
            if (_isMerging)
            {
                await Task.Delay(100);
                continue;
            }

            Console.Clear();
            Console.WriteLine("Please choose:");
            Console.WriteLine("P: Pause/Resume");
            Console.WriteLine("S: Stop/Start");
            Console.WriteLine("Esc: Close");
            Console.Write("Your choice: ");

            switch (Console.ReadKey().Key)
            {
                case ConsoleKey.P:
                    {
                        if (_isMerging)
                            break;

                        if (_isPaused)
                        {
                            downloadService.Resume();
                            _isPaused = false;
                        }
                        else
                        {
                            downloadService.Pause();
                            _isPaused = true;
                        }

                        break;
                    }

                case ConsoleKey.S:
                    {
                        if (_isMerging)
                            break;

                        if (_downloadPackage == null)
                        {
                            await downloadService.CancelTaskAsync();
                            _downloadPackage = downloadService.Package;
                        }
                        else
                        {
                            var package = _downloadPackage;
                            configuration = GetDownloadConfiguration(Path.Combine(desktopDirectory, "NewFolder"));
                            downloadService = GetDownloadService(configuration);
                            _ = downloadService.DownloadFileTaskAsync(package);
                            _downloadPackage = null;
                        }

                        break;
                    }

                case ConsoleKey.Escape:
                    {
                        if (_isMerging)
                            break;

                        return;
                    }
            }
        }
    }

    private static DownloadConfiguration GetDownloadConfiguration(string outputDirectory)
    {
        return new DownloadConfiguration
        {
            ChunkFilesOutputDirectory = outputDirectory,
            ChunkCount = 8,
            MaximumBytesPerSecond = 512 * 1024, // 512 KB/s
            ParallelDownload = true,
            ReserveStorageSpaceBeforeStartingDownload = true,
            MaximumMemoryBufferBytes = 10 * 1024 * 1024, // 10 MB
            MaxRestartWithoutClearTempFile = 5,
            MaximumBytesPerSecondForMerge = 1024 * 1024 // 1 MB/s
        };
    }

    private static DownloadService GetDownloadService(DownloadConfiguration configuration)
    {
        var downloadService = new DownloadService(configuration);
        // Events
        downloadService.MergeStarted += DownloadServiceOnMergeStarted;
        downloadService.MergeProgressChanged += DownloadServiceOnMergeProgressChanged;
        downloadService.DownloadFileCompleted += DownloadServiceOnDownloadFileCompleted;
        downloadService.ChunkDownloadRestarted += DownloadServiceOnChunkDownloadRestarted;
        downloadService.DownloadProgressChanged += DownloadServiceOnDownloadProgressChanged;

        return downloadService;
    }

    private static void DownloadServiceOnMergeStarted(object? sender, Core.CustomEventArgs.MergeStartedEventArgs e)
    {
        _isMerging = true;
        Console.Clear();
        Console.WriteLine("Merge started...");
    }

    private static void DownloadServiceOnMergeProgressChanged(object? sender, Core.CustomEventArgs.MergeProgressChangedEventArgs e)
    {
        Console.WriteLine(e.Progress);
        _isMerged = e.Progress >= 100;
    }

    private static void DownloadServiceOnDownloadFileCompleted(object? sender, System.ComponentModel.AsyncCompletedEventArgs e)
    {
        Console.Clear();

        if (e.Error != null)
        {
            Console.WriteLine($"Error occurred. Error message: {e.Error.Message}");
        }
        else if (e.Cancelled)
        {
            Console.WriteLine("Stopped");
        }
        else
        {
            Console.WriteLine("Completed");
        }
    }

    private static void DownloadServiceOnChunkDownloadRestarted(object? sender, Core.CustomEventArgs.ChunkDownloadRestartedEventArgs e)
    {
        var currentColor = Console.ForegroundColor;
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"Chunk {e.ChunkId} restarted. Reason: {e.Reason}");
        Console.ForegroundColor = currentColor;
    }

    private static void DownloadServiceOnDownloadProgressChanged(object? sender, Core.CustomEventArgs.DownloadProgressChangedEventArgs e)
    {
        Console.WriteLine(e.ProgressPercentage);
    }
}