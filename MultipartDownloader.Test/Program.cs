using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MultipartDownloader.Core;
using MultipartDownloader.Core.CustomEventArgs;
using System.ComponentModel;
using System.Reflection;

namespace MultipartDownloader.Test;

internal class Program
{
    private static ILogger<Program>? _logger;
    private static bool _isCompleted;

    private static async Task Main(string[] args)
    {
        var services = CreateServiceCollection();
        _logger = services.GetRequiredService<ILogger<Program>>();

        const string url = "https://dl2.soft98.ir/soft/g/Google.Chrome.145.0.7632.110.x64.zip?1771573542";
        const string fileName = "Google.Chrome.145.0.7632.110.x64.zip";
        var desktopDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        var configuration = GetDownloadConfiguration(desktopDirectory);
        var downloadService = GetDownloadService(configuration, services);

        var filePath = Path.Combine(desktopDirectory, fileName);
        _ = downloadService.DownloadFileTaskAsync(url, filePath);

        Console.ReadKey();

        if (!_isCompleted)
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
            builder.SetMinimumLevel(LogLevel.Information);
            builder.AddConsole();
            builder.AddFile(logFilePath, minimumLevel: LogLevel.Information);
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
            ReserveStorageSpaceBeforeStartingDownload = false,
            MaximumMemoryBufferBytes = 100 * 1024 * 1024,
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
        downloadService.DownloadStarted += DownloadServiceOnDownloadStarted;
        downloadService.MergeStarted += DownloadServiceOnMergeStarted;
        downloadService.MergeProgressChanged += DownloadServiceOnMergeProgressChanged;
        downloadService.DownloadFileCompleted += DownloadServiceOnDownloadFileCompleted;
        downloadService.ChunkDownloadRestarted += DownloadServiceOnChunkDownloadRestarted;
        downloadService.DownloadProgressChanged += DownloadServiceOnDownloadProgressChanged;

        return downloadService;
    }

    private static void DownloadServiceOnDownloadStarted(object? sender, DownloadStartedEventArgs e)
    {
        _logger?.LogInformation("Download stated. File name: '{FileName}', File size: {FileSize}, Url: '{Url}'", e.FileName, e.TotalBytesToReceive, e.Urls[0]);
    }

    private static void DownloadServiceOnMergeStarted(object? sender, MergeStartedEventArgs e)
    {
        _logger?.LogInformation("Merge started...");
    }

    private static void DownloadServiceOnMergeProgressChanged(object? sender, MergeProgressChangedEventArgs e)
    {
        _logger?.LogInformation("Merge progress: {Progress}", e.Progress.ToString("N2"));
    }

    private static void DownloadServiceOnDownloadFileCompleted(object? sender, AsyncCompletedEventArgs e)
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

        _isCompleted = true;
    }

    private static void DownloadServiceOnChunkDownloadRestarted(object? sender, ChunkDownloadRestartedEventArgs e)
    {
        _logger?.LogInformation("Chunk {ChunkId} restarted. Reason: {Reason}", e.ChunkId, e.Reason);
    }

    private static void DownloadServiceOnDownloadProgressChanged(object? sender, DownloadProgressChangedEventArgs e)
    {
        _logger?.LogInformation("Download progress: {ProgressPercentage}, Received bytes size: {Size}", e.ProgressPercentage.ToString("N2"), e.ProgressedByteSize);
    }
}