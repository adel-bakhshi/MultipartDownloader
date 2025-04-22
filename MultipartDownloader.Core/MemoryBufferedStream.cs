namespace MultipartDownloader.Core;

public class MemoryBufferedStream : Stream
{
    #region Private fields

    private readonly Stream _baseStream;
    private readonly SemaphoreSlim _writeSemaphore;
    private readonly Queue<Packet> _memoryPackets;
    private long _filePosition;
    private bool _disposed;

    private long _maxMemoryBuffer;

    #endregion Private fields

    #region Properties

    public override bool CanRead => _baseStream.CanRead;

    public override bool CanSeek => _baseStream.CanSeek;

    public override bool CanWrite => _baseStream.CanWrite;

    public override long Length => _baseStream.Length;

    public override long Position
    {
        get => _filePosition;
        set
        {
            _filePosition = value < 0 ? 0 : value;
            _baseStream.Seek(_filePosition, SeekOrigin.Begin);
        }
    }

    public long MemoryLength => _memoryPackets.Sum(p => p.Length);

    public long MaxMemoryBuffer
    {
        get => _maxMemoryBuffer;
        set => _maxMemoryBuffer = value < 0 ? 0 : value;
    }

    #endregion Properties

    public MemoryBufferedStream(string filePath, long maxMemoryBuffer, long initialPosition = 0)
    {
        _writeSemaphore = new SemaphoreSlim(1, 1);
        _memoryPackets = [];
        _filePosition = initialPosition;
        _baseStream = new FileStream(filePath, FileMode.OpenOrCreate, FileAccess.Write, FileShare.ReadWrite) { Position = 0 };

        MaxMemoryBuffer = maxMemoryBuffer;
    }

    public MemoryBufferedStream(Stream baseStream, long maxMemoryBuffer, long initialPosition = 0)
    {
        _writeSemaphore = new SemaphoreSlim(1, 1);
        _memoryPackets = [];
        _filePosition = initialPosition;
        _baseStream = baseStream;
        _baseStream.Position = 0;

        MaxMemoryBuffer = maxMemoryBuffer;
    }

    public override async Task FlushAsync(CancellationToken cancellationToken)
    {
        try
        {
            // Wait for write operation to complete
            await _writeSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            // Write all data stored in memory to the disk
            await WriteToDiskAsync(cancellationToken).ConfigureAwait(false);
            // Flush base stream
            await _baseStream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _writeSemaphore.Release();
        }
    }

    public override void Flush()
    {
        FlushAsync(CancellationToken.None).GetAwaiter().GetResult();
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        return _baseStream.Read(buffer, offset, count);
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        return _baseStream.Seek(offset, origin);
    }

    public override void SetLength(long value)
    {
        _baseStream.SetLength(value);
    }

    public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        try
        {
            // Wait for write operation to finish
            await _writeSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            // Write data to memory
            _memoryPackets.Enqueue(new Packet(buffer, offset, count));
            // Check for memory limit size
            if (MemoryLength >= MaxMemoryBuffer)
                await WriteToDiskAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _writeSemaphore.Release();
        }
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        WriteAsync(buffer, offset, count, CancellationToken.None).GetAwaiter().GetResult();
    }

    public override async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        try
        {
            await _writeSemaphore.WaitAsync();

            await WriteToDiskAsync(CancellationToken.None).ConfigureAwait(false);
            await _baseStream.DisposeAsync().ConfigureAwait(false);
            _memoryPackets.Clear();
            _disposed = true;
        }
        finally
        {
            _writeSemaphore.Release();
            _writeSemaphore.Dispose();
        }

        await base.DisposeAsync();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_disposed)
        {
            try
            {
                _writeSemaphore.Wait();

                WriteToDiskAsync(CancellationToken.None).GetAwaiter().GetResult();
                _baseStream.Dispose();
                _memoryPackets.Clear();
                _disposed = true;
            }
            finally
            {
                _writeSemaphore.Release();
                _writeSemaphore.Dispose();
            }
        }

        base.Dispose(disposing);
    }

    #region Helpers

    private async Task WriteToDiskAsync(CancellationToken cancellationToken = default)
    {
        // Check memory size
        if (MemoryLength == 0)
            return;

        // Seek to the current position of the file
        _baseStream.Seek(_filePosition, SeekOrigin.Begin);

        // Copy memory data to disk
        while (_memoryPackets.Count > 0)
        {
            // Get first item from queue
            var packet = _memoryPackets.Dequeue();
            // Write data to disk
            await _baseStream.WriteAsync(packet.Data, cancellationToken).ConfigureAwait(false);
            // Update file position
            _filePosition += packet.Length;
            // Clear packet
            packet.Clear();
        }
    }

    #endregion Helpers
}