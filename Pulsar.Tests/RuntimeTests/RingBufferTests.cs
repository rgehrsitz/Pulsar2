// File: Pulsar.Tests/RuntimeTests/RingBufferTests.cs

using Xunit;
using Pulsar.Runtime.Buffers;

namespace Pulsar.Tests.Runtime.Buffers;

public class RingBufferTests
{
    [Fact]
    public void CircularBuffer_AddAndRetrieve_WorksCorrectly()
    {
        // Arrange
        var buffer = new CircularBuffer(3);
        var now = DateTime.UtcNow;

        // Act
        buffer.Add(1.0, now.AddMilliseconds(-300));
        buffer.Add(2.0, now.AddMilliseconds(-200));
        buffer.Add(3.0, now.AddMilliseconds(-100));

        // Assert
        var values = buffer.GetValues(TimeSpan.FromMilliseconds(500)).ToList();
        Assert.Equal(3, values.Count);
        Assert.Equal(3.0, values[0].Value);
        Assert.Equal(2.0, values[1].Value);
        Assert.Equal(1.0, values[2].Value);
    }

    [Fact]
    public void CircularBuffer_Overflow_HandlesCorrectly()
    {
        // Arrange
        var buffer = new CircularBuffer(2);
        var now = DateTime.UtcNow;

        // Act
        buffer.Add(1.0, now.AddMilliseconds(-300));
        buffer.Add(2.0, now.AddMilliseconds(-200));
        buffer.Add(3.0, now.AddMilliseconds(-100));

        // Assert
        var values = buffer.GetValues(TimeSpan.FromMilliseconds(500)).ToList();
        Assert.Equal(2, values.Count);
        Assert.Equal(3.0, values[0].Value);
        Assert.Equal(2.0, values[1].Value);
    }

    [Fact]
    public void RingBufferManager_UpdateBuffers_WorksCorrectly()
    {
        // Arrange
        var manager = new RingBufferManager(capacity: 10);
        var values = new Dictionary<string, double>
        {
            ["temp1"] = 75.0,
            ["temp2"] = 80.0
        };

        // Act
        manager.UpdateBuffers(values);

        // Assert - add values above threshold
        values["temp1"] = 105.0;
        values["temp2"] = 85.0;
        manager.UpdateBuffers(values);

        // Check thresholds
        Assert.False(manager.IsAboveThresholdForDuration("temp1", 100.0, TimeSpan.FromMilliseconds(200)));
        Assert.True(manager.IsAboveThresholdForDuration("temp2", 70.0, TimeSpan.FromMilliseconds(200)));
    }

    [Fact]
    public void RingBufferManager_ThresholdChecking_WorksCorrectly()
    {
        // Arrange
        var manager = new RingBufferManager(capacity: 5);
        var startTime = DateTime.UtcNow;
        var sensor = "test_sensor";

        // Add values over 1 second
        for (int i = 0; i < 5; i++)
        {
            manager.UpdateBuffers(new Dictionary<string, double> { [sensor] = 110.0 });
            Thread.Sleep(50); // Small delay to spread values
        }

        // Assert
        Assert.True(manager.IsAboveThresholdForDuration(sensor, 100.0, TimeSpan.FromMilliseconds(200)));
        Assert.False(manager.IsAboveThresholdForDuration(sensor, 120.0, TimeSpan.FromMilliseconds(200)));
    }

    [Fact]
    public void CircularBuffer_TimeBasedRetrieval_RespectsTimeWindow()
    {
        // Arrange
        var buffer = new CircularBuffer(5);
        var now = DateTime.UtcNow;

        // Act - Add values at different times
        buffer.Add(1.0, now.AddMilliseconds(-600));  // Outside 500ms window
        buffer.Add(2.0, now.AddMilliseconds(-400));  // Inside window
        buffer.Add(3.0, now.AddMilliseconds(-200));  // Inside window
        buffer.Add(4.0, now.AddMilliseconds(-100));  // Inside window

        // Assert
        var values = buffer.GetValues(TimeSpan.FromMilliseconds(500)).ToList();
        Assert.Equal(3, values.Count);  // Should only get values within last 500ms
        Assert.Equal(4.0, values[0].Value);
        Assert.Equal(3.0, values[1].Value);
        Assert.Equal(2.0, values[2].Value);
    }
}