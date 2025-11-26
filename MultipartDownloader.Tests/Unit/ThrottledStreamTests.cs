using FluentAssertions;
using MultipartDownloader.Core;
using System.Diagnostics;

namespace MultipartDownloader.Tests.Unit;

public class ThrottledStreamTests
{
    [Fact]
    public async Task ReadAsync_ShouldThrottle_WhenBandwidthLimitSet()
    {
        // Arrange
        var data = new byte[1024 * 100]; // 100KB
        var memoryStream = new MemoryStream(data);
        var throttledStream = new ThrottledStream(memoryStream, 1024 * 10); // 10KB/s limit
        var buffer = new byte[1024 * 50]; // 50KB buffer
        var stopwatch = Stopwatch.StartNew();

        // Act
        await throttledStream.ReadExactlyAsync(buffer);
        stopwatch.Stop();

        // Assert
        stopwatch.ElapsedMilliseconds.Should().BeGreaterThan(4000, "because it should take ~5 seconds for 50KB at 10KB/s");
    }
}