using MultipartDownloader.Core.DownloadEventArgs;
using MultipartDownloader.Core.Exceptions;
using MultipartDownloader.Core.Helpers;
using MultipartDownloader.Core.Infrastructure;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Timers;

namespace MultipartDownloader.Core;

public class DownloadPart
{
    #region Private fields

    private readonly DownloadOptions _downloadOptions;
    private FileStream? _partFileStream;

    private readonly System.Timers.Timer _calculateAverageSpeedTimer;
    private long _lastAverageDownloadSpeed;
    private long _lastReportedPosition;
    private DateTime _lastProgressChanged;

    private long _bytesUsedInCurrentSecond;
    private DateTime _lastResetTime;

    #endregion Private fields

    #region Properties

    public int PartNumber { get; set; }
    public long Start { get; set; }
    public long End { get; set; }
    public long Position { get; private set; }
    public string? TempFilePath { get; private set; }
    public FileStream PartFileStream => _partFileStream ?? throw new DownloadFileException("Part file stream is not defined.");
    public bool IsActive { get; private set; }
    public int FailureCount { get; set; }
    public bool UseRangeDownload { get; set; }
    public List<DownloadSpeed> DownloadSpeeds { get; } = [];
    public List<long> MedianDownloadSpeeds { get; } = [];

    #endregion Properties

    #region Events

    public event EventHandler<PartDownloadCompletedEventArgs>? DownloadCompleted;

    public event EventHandler<PartDownloadProgressChangedEventArgs>? DownloadProgressChanged;

    #endregion Events

    public DownloadPart(DownloadOptions downloadOptions)
    {
        _downloadOptions = downloadOptions;

        _calculateAverageSpeedTimer = new System.Timers.Timer(1000);
        _calculateAverageSpeedTimer.Elapsed += CalculateAverageSpeedTimerOnElapsed;
    }

    public void Initial(int partNumber, long start, long end, bool useRangeDownload)
    {
        PartNumber = partNumber;
        Start = start;
        End = end < 0 ? long.MaxValue : end;
        Position = 0;
        UseRangeDownload = useRangeDownload;

        CreateTempFileName();
    }

    public async Task DownloadPartAsync()
    {
        try
        {
            // Set download part active flag to true
            IsActive = true;

            var handler = new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(10), // جلوگیری از قطعی اتصال
                PooledConnectionIdleTimeout = TimeSpan.FromMinutes(5),
                MaxConnectionsPerServer = 50, // کنترل اتصالات همزمان
                ConnectTimeout = TimeSpan.FromSeconds(30),
                ResponseDrainTimeout = TimeSpan.FromSeconds(10),
                UseCookies = false, // اگر نیازی به کوکی نیست
                AutomaticDecompression = DecompressionMethods.GZip // کاهش حجم دانلود
            };

            using var httpClient = new HttpClient(handler);
            httpClient.Timeout = Timeout.InfiniteTimeSpan;
            using var request = new HttpRequestMessage(HttpMethod.Get, _downloadOptions.Url);

            // Set Range header for downloading a specific part
            if (UseRangeDownload)
                request.Headers.Range = new RangeHeaderValue(Start, End);

            // Send request
            using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            // Reading stream and writing in file
            await using var contentStream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
            byte[] buffer = new byte[_downloadOptions.StreamBufferSize];
            int bytesRead;

            // Reset report parameters
            _lastReportedPosition = 0;
            _lastProgressChanged = DateTime.Now;
            // Start calculate average download speed timer to calculate average download speed
            _calculateAverageSpeedTimer.Start();

            // Read data from server throttled
            await using var throttledResponse = new ThrottledStream(contentStream, _downloadOptions.MaximumBytesPerSecondsPerDownloadPart);
            // Read data from stream
            while ((bytesRead = await throttledResponse.ReadAsync(buffer, CancellationToken.None).ConfigureAwait(false)) > 0)
            {
                // Write data to final stream
                await PartFileStream.WriteAsync(buffer.AsMemory(0, bytesRead), CancellationToken.None).ConfigureAwait(false);

                // Save download state
                Position += bytesRead;

                // Raise the DownloadProgressChanged event whenever a certain amount of the file has been downloaded
                // or a certain amount of time has passed since the previous update
                var now = DateTime.Now;
                if (Position - _lastReportedPosition >= DownloadConstants.ProgressThreshold || now - _lastProgressChanged >= TimeSpan.FromSeconds(1))
                    CalculateDownloadProgress();
            }

            // If at the end the values ​​of Position and lastReportedPosition are not the same, still raise the DownloadProgressChanged event
            if (Position != _lastReportedPosition)
            {
                // Calculate download progress
                CalculateDownloadProgress();
            }

            // Validate final file
            if (UseRangeDownload && PartFileStream.Length != (End - Start + 1))
            {
                // TODO: Use DownloadPartFailed event
                throw new DownloadFileException($"Incomplete download! Expected: {End - Start + 1} bytes, Received: {Position} bytes");
            }

            // Dispose file stream
            await DisposeStreamAsync().ConfigureAwait(false);
            // Stop calculate average download speed timer
            StopCalculateAverageDownloadSpeed();

            // Set download part active flag to true
            IsActive = false;
            // Raise completed event
            DownloadCompleted?.Invoke(this, new PartDownloadCompletedEventArgs { IsCompleted = true });
        }
        catch (Exception ex)
        {
            // Dispose file stream
            await DisposeStreamAsync().ConfigureAwait(false);
            // Stop calculate average download speed timer
            StopCalculateAverageDownloadSpeed();

            // Set download part active flag to true
            IsActive = false;
            // Add 1 value to failure count
            FailureCount++;
            // Raise completed event
            DownloadCompleted?.Invoke(this, new PartDownloadCompletedEventArgs { DownloadPart = this, Error = ex });
        }
    }

    public async Task MergeDownloadPartAsync(FileStream finalStream)
    {
        if (!File.Exists(TempFilePath))
            throw new DownloadFileException($"Temporary file for part ({Start}-{End}) is missing!");

        await using var tempStream = new FileStream(TempFilePath, FileMode.Open, FileAccess.Read);
        finalStream.Seek(Start, SeekOrigin.Begin);
        await tempStream.CopyToAsync(finalStream).ConfigureAwait(false);
    }

    public async Task DisposeStreamAsync()
    {
        try
        {
            await PartFileStream.FlushAsync().ConfigureAwait(false);
            await PartFileStream.DisposeAsync().ConfigureAwait(false);
        }
        catch
        {
            // Ignore errors
        }
    }

    public void DeleteTempFile()
    {
        if (File.Exists(TempFilePath))
            File.Delete(TempFilePath);
    }

    public long GetAverageSpeed()
    {
        return _lastAverageDownloadSpeed == 0 ? (long)DownloadSpeeds.Average(speed => speed?.BytesPerSecondSpeed ?? 0) : _lastAverageDownloadSpeed;
    }

    #region Helpers

    private void CreateTempFileName()
    {
        // Choose a name for directory
        var fileName = Path.GetFileNameWithoutExtension(_downloadOptions.FilePath);
        // Define directory path
        var tempDirectory = Path.Combine(_downloadOptions.DownloadPartOutputDirectory, fileName!);
        // Create temp directory
        StorageHelper.CreateOutputDirectory(tempDirectory);
        // Choose a temp file name for download part
        var tempFileName = $"{fileName}.part{PartNumber}.tmp";
        // Set temp file path
        TempFilePath = Path.Combine(tempDirectory, tempFileName);

        // Delete old temp file name
        DeleteTempFile();

        // Create download part file stream
        _partFileStream = StorageHelper.CreateDownloadPartFile(TempFilePath);
    }

    private void CalculateDownloadProgress()
    {
        // Calculate current speed
        StoreCurrentDownloadSpeed();
        // Raise progress changed event
        RaiseProgressChangedEvent();

        // Store current data for next update
        _lastReportedPosition = Position;
        _lastProgressChanged = DateTime.Now;
    }

    private void StoreCurrentDownloadSpeed()
    {
        var now = DateTime.Now;
        // Add new speed sample to list
        DownloadSpeeds.Add(new DownloadSpeed
        {
            ReceivedBytes = Position - _lastReportedPosition,
            TotalMilliseconds = (now - _lastProgressChanged).TotalMilliseconds
        });

        // Due to optimization issues, we only store a limited number of speed data samples
        if (DownloadSpeeds.Count > DownloadConstants.NumberOfSpeedSamples)
            DownloadSpeeds.RemoveAt(0);
    }

    private void RaiseProgressChangedEvent()
    {
        // Create event args and pass to event
        var eventArgs = GetProgressChangedEventArgs();
        // Raise event
        DownloadProgressChanged?.Invoke(this, eventArgs);
    }

    private PartDownloadProgressChangedEventArgs GetProgressChangedEventArgs()
    {
        // Current speed
        var currentSpeed = DownloadSpeeds.Count > 0 ? (DownloadSpeeds.LastOrDefault(speed => speed != null)?.BytesPerSecondSpeed ?? 0) : 0;

        // Create a new instance of PartDownloadProgressChangedEventArgs and pass it
        return new PartDownloadProgressChangedEventArgs(
            partNumber: PartNumber,
            startRange: Start,
            endRange: End,
            receivedBytesSize: Position,
            bytesPerSecondSpeed: currentSpeed,
            averageBytesPerSecondSpeed: GetAverageSpeed(),
            isCompleted: IsDownloadPartCompleted());
    }

    private async Task LimitDownloadSpeedAsync(int bytesRead)
    {
        // Speed management
        while (!CanDownloadBytes(bytesRead, _downloadOptions.MaximumBytesPerSecondsPerDownloadPart))
        {
            var waitTime = GetRemainingTimeUntilNextSecond();
            if (waitTime > TimeSpan.Zero)
                await Task.Delay(waitTime);
        }
    }

    private bool CanDownloadBytes(int requestedBytes, long maxBytesPerSecond)
    {
        if (maxBytesPerSecond == long.MaxValue)
            return true;

        var now = DateTime.Now;
        var elapsedSeconds = (now - _lastResetTime).TotalSeconds;

        if (elapsedSeconds >= 1)
        {
            _bytesUsedInCurrentSecond = 0;
            _lastResetTime = now;
        }

        return _bytesUsedInCurrentSecond + requestedBytes <= maxBytesPerSecond;
    }

    private TimeSpan GetRemainingTimeUntilNextSecond()
    {
        var now = DateTime.Now;
        var elapsed = now - _lastResetTime;
        return TimeSpan.FromSeconds(1) - elapsed;
    }

    private void StopCalculateAverageDownloadSpeed()
    {
        // Stop calculate average download speed timer
        _calculateAverageSpeedTimer?.Stop();
        _calculateAverageSpeedTimer?.Close();
        _calculateAverageSpeedTimer?.Dispose();
    }

    private bool IsDownloadPartCompleted()
    {
        return Start + Position == End;
    }

    #endregion Helpers

    #region Event handlers

    private void CalculateAverageSpeedTimerOnElapsed(object? sender, ElapsedEventArgs e)
    {
        // Find median of the speeds and store it
        var sortedSpeeds = DownloadSpeeds.Select(speed => speed?.BytesPerSecondSpeed ?? 0).Order().ToList();
        var medianSpeed = sortedSpeeds.Count > 0 ? sortedSpeeds[(int)Math.Ceiling(sortedSpeeds.Count / 2.0)] : 0;
        MedianDownloadSpeeds.Add(medianSpeed);

        // Due to optimization issues, we only store a limited number of median data samples
        if (MedianDownloadSpeeds.Count > DownloadConstants.NumberOfMedianSamples)
            MedianDownloadSpeeds.RemoveAt(0);

        // Calculate average download speed
        _lastAverageDownloadSpeed = (long)MedianDownloadSpeeds.Average();
    }

    #endregion Event handlers
}