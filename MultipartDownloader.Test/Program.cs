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
        //const string url = "https://dl2.soft98.ir/soft/x-y-z/Yamicsoft.Windows.Manager.2.1.4.x64.rar?1744962507";
        const string url = "https://codeload.github.com/adel-bakhshi/MultipartDownloader/zip/refs/heads/master";
        var desktopDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        var configuration = new DownloadConfiguration
        {
            ChunkFilesOutputDirectory = desktopDirectory,
            ChunkCount = 8,
            MaximumBytesPerSecond = 0 * 1024,
            ParallelDownload = true,
            ReserveStorageSpaceBeforeStartingDownload = true,
            MaximumMemoryBufferBytes = 10 * 1024 * 1024
        };

        var downloadService = new DownloadService(configuration);
        // Events
        downloadService.MergeStarted += DownloadServiceOnMergeStarted;
        downloadService.MergeProgressChanged += DownloadServiceOnMergeProgressChanged;
        downloadService.DownloadFileCompleted += DownloadServiceOnDownloadFileCompleted;
        downloadService.ChunkDownloadRestarted += DownloadServiceOnChunkDownloadRestarted;

        var filePath = Path.Combine(desktopDirectory, "Yamicsoft.Windows.Manager.2.1.4.x64.rar");
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
                            downloadService = new DownloadService(configuration);
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
        if (e.Error != null)
            Console.WriteLine(e.Error);
    }

    private static void DownloadServiceOnChunkDownloadRestarted(object? sender, Core.CustomEventArgs.ChunkDownloadRestartedEventArgs e)
    {
        var currentColor = Console.ForegroundColor;
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"Chunk {e.ChunkId} restarted. Reason: {e.Reason}");
        Console.ForegroundColor = currentColor;
    }
}