using FluentAssertions;
using MultipartDownloader.Core;

namespace MultipartDownloader.Tests.Unit;

public class BandwidthTests
{
    [Fact]
    public void Constructor_ShouldInitializeWithDefaultValues()
    {
        // Arrange & Act
        var bandwidth = new Bandwidth();

        // Assert
        bandwidth.Speed.Should().Be(0);
        bandwidth.AverageSpeed.Should().Be(0);
        bandwidth.BandwidthLimit.Should().Be(long.MaxValue);
    }

    [Fact]
    public async Task CalculateSpeed_ShouldUpdateSpeed_WhenCalledAfterOneSecondAsync()
    {
        // Arrange
        var bandwidth = new Bandwidth();
        const long bytesReceived = 1024 * 1024; // 1MB

        // Act
        await Task.Delay(1100);
        bandwidth.CalculateSpeed(bytesReceived);

        // Assert
        bandwidth.Speed.Should().BeGreaterThan(0);
        bandwidth.Speed.Should().BeLessThanOrEqualTo(bytesReceived);
    }

    [Theory]
    [InlineData(1024, 1024)] // 1KB limit, 1KB received
    [InlineData(2048, 4096)] // 2KB limit, 4KB received
    [InlineData(5120, 10240)] // 5KB limit, 10KB received
    public void CalculateSpeed_ShouldAddDelay_WhenExceedingBandwidthLimit(long limit, long received)
    {
        // Arrange
        var bandwidth = new Bandwidth { BandwidthLimit = limit };

        // Act
        bandwidth.CalculateSpeed(received);
        var delayTime = bandwidth.PopSpeedRetrieveTime();

        // Assert
        delayTime.Should().BeGreaterThan(0, "because bandwidth limit was exceeded");
    }

    [Fact]
    public void Reset_ShouldResetAllValues()
    {
        // Arrange
        var bandwidth = new Bandwidth();
        bandwidth.CalculateSpeed(1024);

        // Act
        bandwidth.Reset();

        // Assert
        bandwidth.Speed.Should().Be(0);
        bandwidth.AverageSpeed.Should().Be(0);
    }
}