using FluentAssertions;
using MultipartDownloader.Core;

namespace MultipartDownloader.Tests.Unit;

public class DownloadConfigurationTests
{
    [Fact]
    public void BufferBlockSize_ShouldThrowException_WhenValueTooSmall()
    {
        // Arrange
        var config = new DownloadConfiguration();

        // Act
        Action act = () => config.BufferBlockSize = 0;

        // Assert
        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithMessage("*must be between 1 byte and 1024 KB*");
    }

    [Theory]
    [InlineData(8, 1024 * 1024, 128 * 1024)]  // 8 chunks, 1MB/s total = 128KB/s per chunk
    [InlineData(4, 512 * 1024, 128 * 1024)]   // 4 chunks, 512KB/s total = 128KB/s per chunk
    public void MaximumSpeedPerChunk_ShouldCalculateCorrectly(int chunkCount, long totalSpeed, long expectedPerChunk)
    {
        // Arrange
        var config = new DownloadConfiguration
        {
            ChunkCount = chunkCount,
            MaximumBytesPerSecond = totalSpeed,
            ActiveChunks = chunkCount
        };

        // Act
        var speedPerChunk = config.MaximumSpeedPerChunk;

        // Assert
        speedPerChunk.Should().Be(expectedPerChunk);
    }
}