using MultipartDownloader.Core.DownloadEventArgs;
using MultipartDownloader.Core.Exceptions;
using MultipartDownloader.Core.Helpers;
using MultipartDownloader.Core.Infrastructure;
using System.ComponentModel;

namespace MultipartDownloader.Core;

public class DownloadService
{
    #region Private fields

    private readonly System.Timers.Timer _calculateAverageSpeedTimer;
    private readonly List<DownloadSpeed> _totalDownloadSpeeds = [];
    private readonly List<long> _medianDownloadSpeeds = [];
    private long _lastTotalReportedPosition;
    private DateTime _lastTotalUpdateDate;
    private long _lastAverageDownloadSpeed;
    private DateTime _lastServiceUpdate = DateTime.MinValue;
    private readonly TimeSpan _updateInterval = TimeSpan.FromSeconds(0.5);

    #endregion Private fields

    #region Properties

    public DownloadOptions DownloadOptions { get; }
    public DownloadConfiguration DownloadConfiguration { get; }

    #endregion Properties

    #region Events

    public event EventHandler? DownloadStarted;

    public event EventHandler<DownloadFinishedEventArgs>? DownloadCompleted;

    public event EventHandler<DownloadProgressChangedEventArgs>? DownloadProgressChanged;

    public event EventHandler<PartDownloadProgressChangedEventArgs>? PartDownloadProgressChanged;

    #endregion Events

    public DownloadService(DownloadConfiguration config)
    {
        _calculateAverageSpeedTimer = new System.Timers.Timer(1000);
        _calculateAverageSpeedTimer.Elapsed += CalculateAverageSpeedTimerOnElapsed;

        // Create download options
        DownloadOptions = new DownloadOptions(
            config.Url,
            config.FilePath,
            config.DownloadPartOutputDirectory,
            config.MaximumBytesPerSecond);

        // Set DownloadConfig
        DownloadConfiguration = config;
        // Subscribe to PropertyChanged event
        DownloadConfiguration.PropertyChanged += DownloadConfigurationOnPropertyChanged;
    }

    public async Task DownloadFileAsync()
    {
        try
        {
            // TODO: Make exceptions better
            // TODO: If download failed and program crashed, Delete old download parts
            // Check url validation
            if (!DownloadOptions.CheckUrlValidation(DownloadOptions.Url))
                throw new DownloadFileException();

            // Get request info
            await DownloadOptions.GetRequestInfoAsync(DownloadOptions.Url).ConfigureAwait(false);

            // Create output directory
            StorageHelper.CreateOutputDirectoryByFilePath(DownloadOptions.FilePath);
            // Create part output directory
            StorageHelper.CreateOutputDirectory(DownloadOptions.DownloadPartOutputDirectory);

            if (DownloadOptions.SupportsContentLength)
            {
                // Validate file
                if (DownloadOptions.TotalFileSize <= 0)
                    throw new DownloadFileException();

                // Reserve storage
                if (DownloadConfiguration.ReserveStorageBeforeDownload)
                    StorageHelper.ReserveStorageBeforeDownload(DownloadOptions.FilePath, DownloadOptions.TotalFileSize);
            }

            // Validate part count
            if (DownloadConfiguration.PartCount <= 0)
                throw new DownloadFileException();

            // Download in one part
            if (!DownloadOptions.SupportsContentLength || !DownloadOptions.SupportsAcceptRanges)
            {
                CreateDownloadPart(1, 0, DownloadOptions.TotalFileSize - 1, false);
            }
            // Download in multiple parts
            else
            {
                CreateDownloadParts();
            }

            // Set last data for calculating download speed
            _lastTotalReportedPosition = 0;
            _lastTotalUpdateDate = DateTime.Now;
            // Start calculate average download speed
            _calculateAverageSpeedTimer.Start();

            // Raise DownloadStarted event
            DownloadStarted?.Invoke(this, EventArgs.Empty);

            // Download all parts asynchronously
            DownloadOptions.DownloadParts.ForEach(part => _ = part.DownloadPartAsync().ConfigureAwait(false));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error message: {ex.Message}.");
            throw;
        }
    }

    public void PauseDownload()
    {
    }

    public void StopDownload()
    {
    }

    #region Helpers

    private void CreateDownloadParts()
    {
        // Calculate part size
        var partSize = DownloadOptions.TotalFileSize / DownloadConfiguration.PartCount;
        // Create download parts
        for (var i = 0; i < DownloadConfiguration.PartCount; i++)
        {
            // Find start and end positions
            var start = i * partSize;
            var end = start + partSize - 1;
            // Check if last part
            if (i == DownloadConfiguration.PartCount - 1)
            {
                // Calculate remain bytes and add them to the end of last part
                var remainBytes = DownloadOptions.TotalFileSize % DownloadConfiguration.PartCount;
                end = remainBytes == 0 ? end : end + remainBytes;
            }

            CreateDownloadPart(i + 1, start, end, true);
        }
    }

    private void CreateDownloadPart(int partNumber, long start, long end, bool useRangeDownload)
    {
        // Create a new instance of download part
        var downloadPart = new DownloadPart(DownloadOptions);
        // Initial download part
        downloadPart.Initial(partNumber, start, end, useRangeDownload);
        // Subscribe to DownloadCompleted event
        downloadPart.DownloadCompleted += DownloadPartOnDownloadCompleted;
        // Subscribe to DownloadProgressChanged event
        downloadPart.DownloadProgressChanged += DownloadPartOnDownloadProgressChanged;

        // Add download part to list
        DownloadOptions.DownloadParts.Add(downloadPart);
    }

    private async Task MergeDownloadPartsAsync()
    {
        try
        {
            // Remove previous file
            if (File.Exists(DownloadOptions.FilePath))
                File.Delete(DownloadOptions.FilePath);

            // Open or create final file
            await using var finalStream = new FileStream(DownloadOptions.FilePath, FileMode.OpenOrCreate, FileAccess.Write, FileShare.ReadWrite);

            // Order parts by Start value
            var sortedParts = DownloadOptions.DownloadParts.OrderBy(part => part.Start).ToList();
            // Merge download parts with final stream
            foreach (var part in sortedParts)
                await part.MergeDownloadPartAsync(finalStream);

            // Check final size
            if (finalStream.Length != DownloadOptions.TotalFileSize)
                throw new DownloadFileException($"Final file size mismatch! Expected: {DownloadOptions.TotalFileSize} bytes, Actual: {finalStream.Length} bytes");

            // Remove temporary files
            for (var i = 0; i < sortedParts.Count; i++)
            {
                sortedParts[i].DeleteTempFile();

                // Remove temp directory after delete last download part
                if (i == sortedParts.Count - 1)
                {
                    var directory = Path.GetDirectoryName(sortedParts[i].TempFilePath);
                    StorageHelper.RemoveDirectory(directory);
                }
            }
        }
        catch (Exception ex)
        {
            throw new DownloadFileException($"Error merging parts: {ex.Message}");
        }
    }

    private bool CheckDownloadCompleted()
    {
        // Check all parts are finished or not
        return DownloadOptions.DownloadParts.TrueForAll(part => !part.IsActive);
    }

    private async Task CompleteDownloadAndMergeFilesAsync()
    {
        // Dispose calculate average download speed
        _calculateAverageSpeedTimer?.Stop();
        _calculateAverageSpeedTimer?.Close();
        _calculateAverageSpeedTimer?.Dispose();

        // Raise download completed event
        DownloadCompleted?.Invoke(this, new DownloadFinishedEventArgs { IsCompleted = true });
        // Merge all parts after download finished
        await MergeDownloadPartsAsync();
    }

    private async Task DownloadPartErrorActionAsync(DownloadPart downloadPart, Exception error)
    {
        // Dispose previous file stream
        await downloadPart.DisposeStreamAsync();

        // Check if download part FailureCount reached the RetryCountOnFailure
        // If FailureCount was greater than RetryCountOnFailure, we have to finish download and raise DownloadFinished event
        // Otherwise let's start download part again
        if (downloadPart.FailureCount <= DownloadConfiguration.RetryCountOnFailure)
        {
            // TODO: Resume download from last byte that received
            // Delete temp file
            downloadPart.DeleteTempFile();
            // Initialize download part
            downloadPart.Initial(downloadPart.PartNumber, downloadPart.Start, downloadPart.End, downloadPart.UseRangeDownload);
            // Start download again
            _ = downloadPart.DownloadPartAsync();
        }
        else
        {
            // TODO: Finish download
            // Finish();
            DownloadCompleted?.Invoke(this, new DownloadFinishedEventArgs(error));
        }
    }

    private void RaiseDownloadProgressChangedEvent()
    {
        var receivedBytesSize = DownloadOptions.DownloadParts.Sum(part => part.Position);
        var now = DateTime.Now;
        var elapsedMilliseconds = (now - _lastTotalUpdateDate).TotalMilliseconds;

        if (elapsedMilliseconds > 0) // Avoiding divide by 0
        {
            // Add new speed sample to list
            _totalDownloadSpeeds.Add(new DownloadSpeed
            {
                ReceivedBytes = receivedBytesSize - _lastTotalReportedPosition,
                TotalMilliseconds = elapsedMilliseconds
            });

            // Due to optimization issues, we only store a limited number of speed data samples
            if (_totalDownloadSpeeds.Count > DownloadConstants.NumberOfSpeedSamples)
                _totalDownloadSpeeds.RemoveAt(0);
        }

        var currentSpeed = _totalDownloadSpeeds.Count > 0 ? (_totalDownloadSpeeds.LastOrDefault(speed => speed != null)?.BytesPerSecondSpeed ?? 0) : 0;
        var averageSpeed = _lastAverageDownloadSpeed == 0 ? (long)_totalDownloadSpeeds.Average(speed => speed?.BytesPerSecondSpeed ?? 0) : _lastAverageDownloadSpeed;

        var eventArgs = new DownloadProgressChangedEventArgs(
            totalBytesSize: DownloadOptions.TotalFileSize,
            receivedBytesSize: receivedBytesSize,
            bytesPerSecondSpeed: currentSpeed,
            averageBytesPerSecondSpeed: averageSpeed);

        DownloadProgressChanged?.Invoke(this, eventArgs);

        _lastTotalReportedPosition = receivedBytesSize;
        _lastTotalUpdateDate = now;
    }

    #endregion Helpers

    #region Event handlers

    private void CalculateAverageSpeedTimerOnElapsed(object? sender, System.Timers.ElapsedEventArgs e)
    {
        // Find median of the speeds and store it
        var sortedSpeeds = _totalDownloadSpeeds.Select(speed => speed?.BytesPerSecondSpeed ?? 0).Order().ToList();
        var medianSpeed = sortedSpeeds.Count > 0 ? sortedSpeeds[(int)Math.Ceiling(sortedSpeeds.Count / 2.0)] : 0;
        _medianDownloadSpeeds.Add(medianSpeed);

        // Due to optimization issues, we only store a limited number of median data samples
        if (_medianDownloadSpeeds.Count > DownloadConstants.NumberOfMedianSamples)
            _medianDownloadSpeeds.RemoveAt(0);

        // Calculate average download speed
        _lastAverageDownloadSpeed = (long)_medianDownloadSpeeds.Average();
    }

    private void DownloadConfigurationOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Update MaximumBytesPerSecond speed
        if (e.PropertyName?.Equals(nameof(DownloadConfiguration.MaximumBytesPerSecond)) == true)
            DownloadOptions.ChangeMaximumBytesPerSecond(DownloadConfiguration.MaximumBytesPerSecond);
    }

    private async void DownloadPartOnDownloadCompleted(object? sender, PartDownloadCompletedEventArgs e)
    {
        // Check download part for errors
        if (e.Error != null)
            await DownloadPartErrorActionAsync(e.DownloadPart, e.Error);

        // Check download completed
        if (CheckDownloadCompleted())
            await CompleteDownloadAndMergeFilesAsync();

        // TODO: Implement the logic of adding a new DownloadPart
    }

    private void DownloadPartOnDownloadProgressChanged(object? sender, PartDownloadProgressChangedEventArgs e)
    {
        // Raise PartDownloadProgressChanged event
        PartDownloadProgressChanged?.Invoke(this, e);

        // Raise DownloadProgressChanged event after a period of time
        // or when download part completed
        var now = DateTime.Now;
        if (now - _lastServiceUpdate >= _updateInterval || e.IsCompleted)
        {
            RaiseDownloadProgressChangedEvent();
            _lastServiceUpdate = now;
        }
    }

    #endregion Event handlers
}