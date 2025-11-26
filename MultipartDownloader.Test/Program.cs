using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
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
        var services = CreateServiceCollection();

        const string url = "https://dl2.soft98.ir/soft/m/Mozilla.Firefox.145.0.2.EN.x64.zip?1764139330";
        const string fileName = "Mozilla.Firefox.145.0.2.EN.x64.zip";
        var desktopDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        var configuration = GetDownloadConfiguration(desktopDirectory);
        var downloadService = GetDownloadService(configuration, services);

        var filePath = Path.Combine(desktopDirectory, fileName);
        _ = downloadService.DownloadFileTaskAsync(url, filePath);

        while (!_isMerged)
        {
            if (_isMerging)
            {
                await Task.Delay(100);
                continue;
            }

            Console.WriteLine();
            Console.WriteLine("Please choose:");
            Console.WriteLine("P: Pause/Resume");
            Console.WriteLine("S: Stop/Start");
            Console.WriteLine("Esc: Close");
            Console.WriteLine();

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
                            downloadService = GetDownloadService(configuration, services);
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

    private static ServiceProvider CreateServiceCollection()
    {
        var serviceCollection = new ServiceCollection();

        serviceCollection.AddLogging(builder =>
        {
            builder.SetMinimumLevel(LogLevel.Debug);
            builder.AddConsole();
        });

        return serviceCollection.BuildServiceProvider();
    }

    private static DownloadConfiguration GetDownloadConfiguration(string outputDirectory)
    {
        return new DownloadConfiguration
        {
            ChunkFilesOutputDirectory = outputDirectory,
            ChunkCount = 8,
            //MaximumBytesPerSecond = 512 * 1024, // 512 KB/s
            MaximumBytesPerSecond = 0, // No limit
            ParallelDownload = true,
            ReserveStorageSpaceBeforeStartingDownload = true,
            MaximumMemoryBufferBytes = 80 * 1024 * 1024, // 80 MB
            MaxRestartWithoutClearTempFile = 5,
            //MaximumBytesPerSecondForMerge = 1024 * 1024 // 1 MB/s
            MaximumBytesPerSecondForMerge = 0 // No limit
        };
    }

    private static DownloadService GetDownloadService(DownloadConfiguration configuration, ServiceProvider services)
    {
        var loggerFactory = services.GetRequiredService<ILoggerFactory>();
        var downloadService = new DownloadService(configuration, loggerFactory);
        // Events
        //downloadService.MergeStarted += DownloadServiceOnMergeStarted;
        //downloadService.MergeProgressChanged += DownloadServiceOnMergeProgressChanged;
        //downloadService.DownloadFileCompleted += DownloadServiceOnDownloadFileCompleted;
        //downloadService.ChunkDownloadRestarted += DownloadServiceOnChunkDownloadRestarted;
        //downloadService.DownloadProgressChanged += DownloadServiceOnDownloadProgressChanged;

        return downloadService;
    }

    private static void DownloadServiceOnMergeStarted(object? sender, Core.CustomEventArgs.MergeStartedEventArgs e)
    {
        _isMerging = true;
        Console.WriteLine("Merge started...");
    }

    private static void DownloadServiceOnMergeProgressChanged(object? sender, Core.CustomEventArgs.MergeProgressChangedEventArgs e)
    {
        Console.WriteLine(e.Progress);
        _isMerged = e.Progress >= 100;
    }

    private static void DownloadServiceOnDownloadFileCompleted(object? sender, System.ComponentModel.AsyncCompletedEventArgs e)
    {
        if (e.Cancelled)
        {
            Console.WriteLine("Stopped");
        }
        else if (e.Error != null)
        {
            Console.WriteLine($"Error occurred. Error message: {e.Error.Message}");
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