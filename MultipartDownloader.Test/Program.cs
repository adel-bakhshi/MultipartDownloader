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
        const string url = "https://dl2.soft98.ir/soft/a/AnyDesk.9.5.1.zip?1744453565";
        var desktopDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        var configuration = new DownloadConfiguration
        {
            ChunkFilesOutputDirectory = desktopDirectory,
            ChunkCount = 8,
            MaximumBytesPerSecond = 128 * 1024,
            ParallelDownload = true,
            ReserveStorageSpaceBeforeStartingDownload = true
        };

        var downloadService = new DownloadService(configuration);
        // Events
        downloadService.MergeStarted += DownloadServiceOnMergeStarted;
        downloadService.MergeProgressChanged += DownloadServiceOnMergeProgressChanged;

        var filePath = Path.Combine(desktopDirectory, "AnyDesk.9.5.1.zip");
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
}