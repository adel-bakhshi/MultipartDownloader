using Microsoft.Extensions.Logging;
using MultipartDownloader.Core.Extensions.Helpers;
using System.ComponentModel;

namespace MultipartDownloader.Core;

/// <summary>
/// Concrete implementation of the <see cref="AbstractDownloadService"/> class.
/// </summary>
public class DownloadService : AbstractDownloadService
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DownloadService"/> class with the specified options.
    /// </summary>
    /// <param name="options">The configuration options for the download service.</param>
    /// <param name="loggerFactory">Pass standard logger factory</param>
    public DownloadService(DownloadConfiguration? options, ILoggerFactory? loggerFactory = null) : base(options)
    {
        Logger = loggerFactory?.CreateLogger<DownloadService>();
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="DownloadService"/> class with default options.
    /// </summary>
    public DownloadService(ILoggerFactory? loggerFactory = null) : this(null, loggerFactory) { }

    /// <summary>
    /// Starts the download operation.
    /// </summary>
    /// <returns>A task that represents the asynchronous download operation. The task result contains the downloaded stream.</returns>
    protected override async Task<Stream?> StartDownload(bool forceBuildStorage = true)
    {
        try
        {
            await SingleInstanceSemaphore.WaitAsync().ConfigureAwait(false);
            Package.TotalFileSize = await RequestInstances[0].GetFileSize().ConfigureAwait(false);
            Package.IsSupportDownloadInRange = await RequestInstances[0].IsSupportDownloadInRange().ConfigureAwait(false);

            if (forceBuildStorage || !Package.IsStorageExists())
                Package.BuildStorage(Options.ReserveStorageSpaceBeforeStartingDownload, Options.MaximumMemoryBufferBytes);

            ValidateBeforeChunking();
            ChunkHub.SetFileChunks(Package);

            // firing the start event after creating chunks
            OnDownloadStarted(new DownloadStartedEventArgs(Package.FileName, Package.TotalFileSize));

            if (Options.ParallelDownload)
            {
                await ParallelDownload(PauseTokenSource.Token).ConfigureAwait(false);
            }
            else
            {
                await SerialDownload(PauseTokenSource.Token).ConfigureAwait(false);
            }

            await MergeChunksAsync();

            await SendDownloadCompletionSignal(DownloadStatus.Completed).ConfigureAwait(false);
        }
        catch (OperationCanceledException exp) // or TaskCanceledException
        {
            await SendDownloadCompletionSignal(DownloadStatus.Stopped, exp).ConfigureAwait(false);
        }
        catch (Exception exp)
        {
            await SendDownloadCompletionSignal(DownloadStatus.Failed, exp).ConfigureAwait(false);
        }
        finally
        {
            SingleInstanceSemaphore.Release();
            await Task.Yield();
        }

        return Package.GetStorageStream();
    }

    private async Task MergeChunksAsync()
    {
        if (Package.Chunks.Length == 0)
            return;

        // Open or create final file
        await using var finalStream = new FileStream(Package.FileName, FileMode.OpenOrCreate, FileAccess.Write, FileShare.ReadWrite);
        // Clear final stream
        finalStream.SetLength(0);
        // Order parts by Start value
        var sortedChunks = Package.Chunks.OrderBy(chunk => chunk.Start).ToList();
        foreach (var chunk in sortedChunks)
        {
            await using var tempStream = new FileStream(chunk.ChunkFilePath, FileMode.Open, FileAccess.Read);
            finalStream.Seek(chunk.Start, SeekOrigin.Begin);
            await tempStream.CopyToAsync(finalStream).ConfigureAwait(false);
        }

        // Check final size
        if (finalStream.Length != Package.TotalFileSize)
            throw new InvalidOperationException($"Final file size mismatch! Expected: {Package.TotalFileSize} bytes, Actual: {finalStream.Length} bytes");

        // Remove temporary files
        for (var i = 0; i < sortedChunks.Count; i++)
        {
            var tempFilePath = sortedChunks[i].ChunkFilePath;
            if (File.Exists(tempFilePath))
                File.Delete(tempFilePath);

            // Remove temp directory after delete last download part
            if (i == sortedChunks.Count - 1)
            {
                var directory = Path.GetDirectoryName(tempFilePath);
                if (Directory.Exists(directory))
                    Directory.Delete(directory, true);
            }
        }
    }

    /// <summary>
    /// Sends the download completion signal with the specified <paramref name="state"/> and optional <paramref name="error"/>.
    /// </summary>
    /// <param name="state">The state of the download operation.</param>
    /// <param name="error">The exception that caused the download to fail, if any.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    private async Task SendDownloadCompletionSignal(DownloadStatus state, Exception? error = null)
    {
        var isCancelled = state == DownloadStatus.Stopped;
        Package.IsSaveComplete = state == DownloadStatus.Completed;
        Status = state;
        OnDownloadFileCompleted(new AsyncCompletedEventArgs(error, isCancelled, Package));
    }

    /// <summary>
    /// Validates the download configuration before chunking the file.
    /// </summary>
    private void ValidateBeforeChunking()
    {
        CheckSingleChunkDownload();
        CheckSupportDownloadInRange();
        SetRangedSizes();
        CheckSizes();
        CheckOutputDirectory();
    }

    private void CheckOutputDirectory()
    {
        Options.ChunkFilesOutputDirectory = Options.ChunkFilesOutputDirectory?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(Options.ChunkFilesOutputDirectory?.Trim()) && !string.IsNullOrEmpty(Package.FileName?.Trim()))
        {
            var directory = Path.GetDirectoryName(Package.FileName);
            if (!string.IsNullOrEmpty(directory))
                Options.ChunkFilesOutputDirectory = directory;
        }

        if (string.IsNullOrEmpty(Options.ChunkFilesOutputDirectory))
            return;

        var fileName = Path.GetFileNameWithoutExtension(Package.FileName) ?? Guid.NewGuid().ToString();
        Options.ChunkFilesOutputDirectory = Path.Combine(Options.ChunkFilesOutputDirectory, fileName);

        if (!Directory.Exists(Options.ChunkFilesOutputDirectory))
            Directory.CreateDirectory(Options.ChunkFilesOutputDirectory);
    }

    /// <summary>
    /// Sets the range sizes for the download operation.
    /// </summary>
    private void SetRangedSizes()
    {
        if (Options.RangeDownload)
        {
            if (!Package.IsSupportDownloadInRange)
            {
                throw new NotSupportedException(
                    "The server of your desired address does not support download in a specific range");
            }

            if (Options.RangeHigh < Options.RangeLow)
            {
                Options.RangeLow = Options.RangeHigh - 1;
            }

            if (Options.RangeLow < 0)
            {
                Options.RangeLow = 0;
            }

            if (Options.RangeHigh < 0)
            {
                Options.RangeHigh = Options.RangeLow;
            }

            if (Package.TotalFileSize > 0)
            {
                Options.RangeHigh = Math.Min(Package.TotalFileSize, Options.RangeHigh);
            }

            Package.TotalFileSize = Options.RangeHigh - Options.RangeLow + 1;
        }
        else
        {
            Options.RangeHigh = Options.RangeLow = 0; // reset range options
        }
    }

    /// <summary>
    /// Checks if there is enough disk space before starting the download.
    /// </summary>
    private void CheckSizes()
    {
        if (Options.CheckDiskSizeBeforeDownload)
            FileHelper.ThrowIfNotEnoughSpace(Package.TotalFileSize, Package.FileName);
    }

    /// <summary>
    /// Checks if the download should be handled as a single chunk.
    /// </summary>
    private void CheckSingleChunkDownload()
    {
        if (Package.TotalFileSize <= 1)
            Package.TotalFileSize = 0;

        if (Package.TotalFileSize <= Options.MinimumSizeOfChunking)
            SetSingleChunkDownload();
    }

    /// <summary>
    /// Checks if the server supports download in a specific range.
    /// </summary>
    private void CheckSupportDownloadInRange()
    {
        if (!Package.IsSupportDownloadInRange)
            SetSingleChunkDownload();
    }

    /// <summary>
    /// Sets the download configuration to handle the file as a single chunk.
    /// </summary>
    private void SetSingleChunkDownload()
    {
        Options.ChunkCount = 1;
        Options.ParallelCount = 1;
        ParallelSemaphore = new SemaphoreSlim(1, 1);
    }

    /// <summary>
    /// Downloads the file in parallel chunks.
    /// </summary>
    /// <param name="pauseToken">The pause token for pausing the download.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    private async Task ParallelDownload(PauseToken pauseToken)
    {
        var tasks = GetChunksTasks(pauseToken);
        var result = Task.WhenAll(tasks);
        await result.ConfigureAwait(false);

        if (result.IsFaulted)
        {
            throw result.Exception;
        }
    }

    /// <summary>
    /// Downloads the file in serial chunks.
    /// </summary>
    /// <param name="pauseToken">The pause token for pausing the download.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    private async Task SerialDownload(PauseToken pauseToken)
    {
        foreach (var task in GetChunksTasks(pauseToken))
            await task.ConfigureAwait(false);
    }

    /// <summary>
    /// Gets the tasks for downloading the chunks.
    /// </summary>
    /// <param name="pauseToken">The pause token for pausing the download.</param>
    /// <returns>An enumerable collection of tasks representing the chunk downloads.</returns>
    private IEnumerable<Task> GetChunksTasks(PauseToken pauseToken)
    {
        for (int i = 0; i < Package.Chunks.Length; i++)
        {
            var request = RequestInstances[i % RequestInstances.Count];
            yield return DownloadChunk(Package.Chunks[i], request, pauseToken, GlobalCancellationTokenSource);
        }
    }

    /// <summary>
    /// Downloads a specific chunk of the file.
    /// </summary>
    /// <param name="chunk">The chunk to download.</param>
    /// <param name="request">The request to use for the download.</param>
    /// <param name="pause">The pause token for pausing the download.</param>
    /// <param name="cancellationTokenSource">The cancellation token source for cancelling the download.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains the downloaded chunk.</returns>
    private async Task<Chunk> DownloadChunk(Chunk chunk, Request request, PauseToken pause, CancellationTokenSource cancellationTokenSource)
    {
        //ChunkDownloader chunkDownloader = new(chunk, Options, Package.Storage!, Logger);

        ChunkDownloader chunkDownloader = GetChunkDownloader(chunk);
        chunkDownloader.DownloadProgressChanged += OnChunkDownloadProgressChanged;
        await ParallelSemaphore.WaitAsync(cancellationTokenSource.Token).ConfigureAwait(false);
        try
        {
            cancellationTokenSource.Token.ThrowIfCancellationRequested();
            return await chunkDownloader.Download(request, pause, cancellationTokenSource.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            cancellationTokenSource.Token.ThrowIfCancellationRequested();
            cancellationTokenSource.Cancel(false);
            throw;
        }
        finally
        {
            ParallelSemaphore.Release();
        }
    }

    private ChunkDownloader GetChunkDownloader(Chunk chunk)
    {
        if (string.IsNullOrEmpty(chunk.ChunkFilePath))
        {
            var fileName = Path.GetFileNameWithoutExtension(Package.FileName) + $".part{int.Parse(chunk.Id) + 1}.tmp";
            chunk.ChunkFilePath = Path.Combine(Options.ChunkFilesOutputDirectory, fileName);
        }

        return new(chunk, Options, Logger);
    }
}