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

        const string url = "https://aec-333.pishtazmovie.ir/dl1vgdl/vgtrldl1/dl1/SoftWare/apk/soundtrack/2024/Nov/Silent-Hill-2-Remake-Soundtrack-by-Akira-Yamaoka_vgdl.ir.rar";
        const string fileName = "Silent-Hill-2-Remake-Soundtrack-by-Akira-Yamaoka_vgdl.ir.rar";
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
        downloadService.MergeStarted += DownloadServiceOnMergeStarted;
        downloadService.MergeProgressChanged += DownloadServiceOnMergeProgressChanged;
        downloadService.DownloadFileCompleted += DownloadServiceOnDownloadFileCompleted;
        downloadService.ChunkDownloadRestarted += DownloadServiceOnChunkDownloadRestarted;
        downloadService.DownloadProgressChanged += DownloadServiceOnDownloadProgressChanged;

        return downloadService;
    }

    private static void DownloadServiceOnMergeStarted(object? sender, MergeStartedEventArgs e)
    {
        _logger?.LogInformation("Merge started...");
    }

    private static void DownloadServiceOnMergeProgressChanged(object? sender, MergeProgressChangedEventArgs e)
    {
        _logger?.LogInformation("Merge progress: {Progress}", e.Progress);
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
        _logger?.LogInformation("Download progress: {ProgressPercentage}, Received bytes size: {Size}", e.ProgressPercentage, e.ProgressedByteSize);
    }
}