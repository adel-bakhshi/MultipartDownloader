using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MultipartDownloader.Core;
using System.Reflection;

namespace MultipartDownloader.Test;

internal class Program
{
    private static bool _isPaused;
    private static DownloadPackage? _downloadPackage;
    private static bool _isMerging;
    private static bool _isMerged;

    private static ILogger<Program>? _logger;

    private static async Task Main(string[] args)
    {
        var services = CreateServiceCollection();
        _logger = services.GetRequiredService<ILogger<Program>>();

        const string url = "https://dl2.soft98.ir/soft/i/Internet.Download.Manager.6.42.57.rar?1764922092";
        const string fileName = "Internet.Download.Manager.6.42.57.rar";
        var desktopDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        var configuration = GetDownloadConfiguration(desktopDirectory);
        var downloadService = GetDownloadService(configuration, services);

        var filePath = Path.Combine(desktopDirectory, fileName);
        _ = downloadService.DownloadFileTaskAsync(url, filePath);

        //while (!_isMerged)
        //{
        //    if (_isMerging)
        //    {
        //        await Task.Delay(100);
        //        continue;
        //    }

        //    Console.WriteLine();
        //    Console.WriteLine("Please choose:");
        //    Console.WriteLine("P: Pause/Resume");
        //    Console.WriteLine("S: Stop/Start");
        //    Console.WriteLine("Esc: Close");
        //    Console.WriteLine();

        //    switch (Console.ReadKey().Key)
        //    {
        //        case ConsoleKey.P:
        //            {
        //                if (_isMerging)
        //                    break;

        //                if (_isPaused)
        //                {
        //                    downloadService.Resume();
        //                    _isPaused = false;
        //                }
        //                else
        //                {
        //                    downloadService.Pause();
        //                    _isPaused = true;
        //                }

        //                break;
        //            }

        //        case ConsoleKey.S:
        //            {
        //                if (_isMerging)
        //                    break;

        //                if (_downloadPackage == null)
        //                {
        //                    await downloadService.CancelTaskAsync();
        //                    _downloadPackage = downloadService.Package;
        //                }
        //                else
        //                {
        //                    var package = _downloadPackage;
        //                    configuration = GetDownloadConfiguration(Path.Combine(desktopDirectory, "NewFolder"));
        //                    downloadService = GetDownloadService(configuration, services);
        //                    _ = downloadService.DownloadFileTaskAsync(package);
        //                    _downloadPackage = null;
        //                }

        //                break;
        //            }

        //        case ConsoleKey.Escape:
        //            {
        //                if (_isMerging)
        //                    break;

        //                return;
        //            }
        //    }
        //}

        Console.ReadKey();
        await downloadService.CancelTaskAsync().ConfigureAwait(false);
        await Task.Delay(5000).ConfigureAwait(false);
    }

    private static ServiceProvider CreateServiceCollection()
    {
        var serviceCollection = new ServiceCollection();

        var baseDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly()?.Location) ?? Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        var logsDir = Path.Combine(baseDir, "Logs");

        if (!Directory.Exists(logsDir))
            Directory.CreateDirectory(logsDir);

        var logFilePath = Path.Combine(logsDir, $"{DateTime.Now:yyyy-MM-dd}.log");

        serviceCollection.AddLogging(builder =>
        {
            builder.SetMinimumLevel(LogLevel.Debug);
            builder.AddConsole();
            builder.AddFile(logFilePath, minimumLevel: LogLevel.Debug);
        });

        return serviceCollection.BuildServiceProvider();
    }

    private static DownloadConfiguration GetDownloadConfiguration(string outputDirectory)
    {
        return new DownloadConfiguration
        {
            ChunkFilesOutputDirectory = outputDirectory,
            ChunkCount = 8,
            //MaximumBytesPerSecond = 64 * 1024,
            ParallelDownload = true,
            ReserveStorageSpaceBeforeStartingDownload = true,
            MaximumMemoryBufferBytes = 5 * 1024 * 1024,
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
        _logger?.LogInformation("Merge started...");
    }

    private static void DownloadServiceOnMergeProgressChanged(object? sender, Core.CustomEventArgs.MergeProgressChangedEventArgs e)
    {
        _logger?.LogInformation("Merge progress: {Progress}", e.Progress);
        _isMerged = e.Progress >= 100;
    }

    private static void DownloadServiceOnDownloadFileCompleted(object? sender, System.ComponentModel.AsyncCompletedEventArgs e)
    {
        if (e.Cancelled)
        {
            _logger?.LogInformation("Stopped");
        }
        else if (e.Error != null)
        {
            _logger?.LogError("Error occurred. Error message: {ErrorMessage}", e.Error.Message);
        }
        else
        {
            _logger?.LogInformation("Completed");
        }
    }

    private static void DownloadServiceOnChunkDownloadRestarted(object? sender, Core.CustomEventArgs.ChunkDownloadRestartedEventArgs e)
    {
        //var currentColor = Console.ForegroundColor;
        //Console.ForegroundColor = ConsoleColor.Red;
        //Console.WriteLine($"Chunk {e.ChunkId} restarted. Reason: {e.Reason}");
        //Console.ForegroundColor = currentColor;

        _logger?.LogInformation("Chunk {ChunkId} restarted. Reason: {Reason}", e.ChunkId, e.Reason);
    }

    private static void DownloadServiceOnDownloadProgressChanged(object? sender, Core.CustomEventArgs.DownloadProgressChangedEventArgs e)
    {
        _logger?.LogInformation("Download progress: {ProgressPercentage}, Received bytes size: {Size}", e.ProgressPercentage, e.ProgressedByteSize);
    }
}