using System.Diagnostics;

namespace MultipartDownloader.Core;

public class ThrottledStream : Stream
{
    #region Private fields

    private readonly Stream _baseStream;
    private readonly long _maxBytesPerSecond;
    private readonly Stopwatch _stopwatch = new();
    private long _bytesReadOrWrittenThisSecond;

    #endregion Private fields

    #region Properties

    public override bool CanRead => _baseStream.CanRead;
    public override bool CanSeek => _baseStream.CanSeek;
    public override bool CanWrite => _baseStream.CanWrite;
    public override long Length => _baseStream.Length;
    public override long Position { get => _baseStream.Position; set => _baseStream.Position = value; }

    #endregion Properties

    public ThrottledStream(Stream baseStream, long maxBytesPerSecond)
    {
        _baseStream = baseStream ?? throw new ArgumentNullException(nameof(baseStream));
        _maxBytesPerSecond = maxBytesPerSecond;
        _stopwatch.Start();
    }

    public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        var bytesRead = await _baseStream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
        if (bytesRead > 0)
            await ThrottleAsync(bytesRead, cancellationToken).ConfigureAwait(false);

        return bytesRead;
    }

    public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        await _baseStream.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
        await ThrottleAsync(buffer.Length, cancellationToken).ConfigureAwait(false);
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        var bytesRead = _baseStream.Read(buffer, offset, count);
        if (bytesRead > 0)
            ThrottleAsync(bytesRead, CancellationToken.None).GetAwaiter().GetResult();

        return bytesRead;
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        _baseStream.Write(buffer, offset, count);
        ThrottleAsync(count, CancellationToken.None).GetAwaiter().GetResult();
    }

    public override void Flush() => _baseStream.Flush();

    public override long Seek(long offset, SeekOrigin origin) => _baseStream.Seek(offset, origin);

    public override void SetLength(long value) => _baseStream.SetLength(value);

    #region Helpers

    private async Task ThrottleAsync(int bytes, CancellationToken cancellationToken)
    {
        _bytesReadOrWrittenThisSecond += bytes;

        var elapsed = _stopwatch.Elapsed;
        if (_bytesReadOrWrittenThisSecond >= _maxBytesPerSecond)
        {
            var expectedElapsed = TimeSpan.FromSeconds((double)_bytesReadOrWrittenThisSecond / _maxBytesPerSecond);
            if (expectedElapsed > elapsed)
            {
                var delay = expectedElapsed - elapsed;
                if (delay > TimeSpan.Zero)
                    await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }

            _stopwatch.Restart();
            _bytesReadOrWrittenThisSecond = 0;
        }
    }

    #endregion Helpers
}