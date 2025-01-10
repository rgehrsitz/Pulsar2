// File: Pulsar.Tests/RuntimeTests/RingBufferTests.cs

using Xunit;
using Pulsar.Runtime.Buffers;
using System.Diagnostics;

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
        var startTime = DateTime.UtcNow;
        Debug.WriteLine($"\nTest started at: {startTime:HH:mm:ss.fff}");

        // Arrange
        var manager = new RingBufferManager(capacity: 10);
        var values = new Dictionary<string, double>
        {
            ["temp1"] = 75.0,
            ["temp2"] = 80.0
        };

        // --- 1) FIRST update at ~0 ms ---------------
        Debug.WriteLine($"\nFirst update at {(DateTime.UtcNow - startTime).TotalMilliseconds:F1}ms");
        manager.UpdateBuffers(values);

        // Sleep ~100 ms
        Thread.Sleep(100);
        Debug.WriteLine($"Slept 100ms, offset: {(DateTime.UtcNow - startTime).TotalMilliseconds:F1}ms");

        // --- 2) SECOND update at ~100 ms -------------
        // Raise temp2 above threshold
        values["temp2"] = 85.0;
        Debug.WriteLine($"\nSecond update at {(DateTime.UtcNow - startTime).TotalMilliseconds:F1}ms");
        manager.UpdateBuffers(values);

        // Sleep ~50 ms
        Thread.Sleep(50);
        Debug.WriteLine($"Slept 50ms, offset: {(DateTime.UtcNow - startTime).TotalMilliseconds:F1}ms");

        // --- 3) THIRD update at ~150 ms --------------
        // Re-affirm temp2=85
        Debug.WriteLine($"\nThird update at {(DateTime.UtcNow - startTime).TotalMilliseconds:F1}ms");
        manager.UpdateBuffers(values);

        // Sleep ~50 ms
        Thread.Sleep(50);
        Debug.WriteLine($"Slept 50ms, offset: {(DateTime.UtcNow - startTime).TotalMilliseconds:F1}ms");

        // --- 4) FOURTH (final) update at ~200 ms ------
        // This final update is crucial; it sets the "start time" for threshold tracking
        Debug.WriteLine($"\nFourth update at {(DateTime.UtcNow - startTime).TotalMilliseconds:F1}ms");
        manager.UpdateBuffers(values);

        // -------------------------------------------------
        // IMPORTANT: Now we wait LONGER than 200 ms
        // so that the final reading at ~200 ms remains
        // continuously above threshold for the entire window.
        // -------------------------------------------------
        Thread.Sleep(300);  // 300 ms ensures we exceed 200 ms
        Debug.WriteLine($"Slept 300ms, offset: {(DateTime.UtcNow - startTime).TotalMilliseconds:F1}ms");

        // --- Final check at ~500 ms --------------------
        Debug.WriteLine($"\nRunning assertions at {(DateTime.UtcNow - startTime).TotalMilliseconds:F1}ms");

        // Expecting temp2=85 to have been above threshold (70)
        // for at least 200 ms since the last update at ~200 ms.
        bool temp2Result = manager.IsAboveThresholdForDuration("temp2", 70.0, TimeSpan.FromMilliseconds(200));
        Debug.WriteLine($"temp2 threshold check result: {temp2Result}");
        Assert.True(temp2Result, "temp2 should remain above 70.0 for 200 ms");

        // Meanwhile, temp1 was updated to 105 as well,
        // but let's say we do NOT expect it to be above 100.0 for that full duration
        Assert.False(manager.IsAboveThresholdForDuration("temp1", 100.0, TimeSpan.FromMilliseconds(200)),
                    "temp1 should NOT remain above 100.0 for 200 ms");
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

    [Fact]
    public void RingBuffer_TemporalConditions_WorksCorrectly()
    {
        // Arrange
        var manager = new RingBufferManager(capacity: 10);
        var startTime = DateTime.UtcNow;
        var sensor = "temperature";

        // Add values over time
        // t=0: 90°F
        manager.UpdateBuffers(new Dictionary<string, double> { [sensor] = 90 });

        // t=100ms: 100°F
        Thread.Sleep(100);
        manager.UpdateBuffers(new Dictionary<string, double> { [sensor] = 100 });

        // t=200ms: 110°F
        Thread.Sleep(100);
        manager.UpdateBuffers(new Dictionary<string, double> { [sensor] = 110 });

        // t=300ms: 115°F
        Thread.Sleep(100);
        manager.UpdateBuffers(new Dictionary<string, double> { [sensor] = 115 });

        // Assert
        // Should be true - temp was > 100 for last 200ms
        Assert.True(manager.IsAboveThresholdForDuration(sensor, 100, TimeSpan.FromMilliseconds(200)));

        // Should be false - temp wasn't > 100 for full 300ms
        Assert.False(manager.IsAboveThresholdForDuration(sensor, 100, TimeSpan.FromMilliseconds(300)));
    }

    [Fact]
    public void RingBuffer_ThresholdBehavior_WorksCorrectly()
    {
        // Arrange
        var buffer = new CircularBuffer(10);
        var now = DateTime.UtcNow;
        var threshold = 30.0;
        var duration = TimeSpan.FromMilliseconds(300);

        Debug.WriteLine($"Testing threshold > {threshold} for duration {duration.TotalMilliseconds}ms");

        // Initial value
        buffer.Add(25.0, now);
        LogBufferState(buffer, duration, threshold);
        Assert.False(buffer.IsAboveThresholdForDuration(threshold, duration),
            "Should not trigger with value below threshold");

        // First value above threshold
        buffer.Add(35.0, now.AddMilliseconds(100));
        LogBufferState(buffer, duration, threshold);
        Assert.False(buffer.IsAboveThresholdForDuration(threshold, duration),
            "Should not trigger - duration not met");

        // Second value above threshold
        buffer.Add(35.0, now.AddMilliseconds(200));
        LogBufferState(buffer, duration, threshold);
        Assert.False(buffer.IsAboveThresholdForDuration(threshold, duration),
            "Should not trigger - duration not met");

        // Wait until the initial low value is outside our window
        buffer.Add(35.0, now.AddMilliseconds(500));  // Changed from 300 to 500
        LogBufferState(buffer, duration, threshold);
        Assert.True(buffer.IsAboveThresholdForDuration(threshold, duration),
            "Should trigger - values > threshold for full duration");
    }

    private void LogBufferState(CircularBuffer buffer, TimeSpan duration, double threshold)
    {
        var values = buffer.GetValues(duration).ToList();
        Debug.WriteLine($"\nBuffer state - {values.Count} values in last {duration.TotalMilliseconds}ms:");
        foreach (var v in values.OrderBy(x => x.Timestamp))
        {
            Debug.WriteLine($"  Time: {v.Timestamp:HH:mm:ss.fff}, Value: {v.Value}");
        }
    }

    [Fact]
    public void RingBuffer_AboveThreshold_HandlesDifferentScenarios()
    {
        // Arrange
        var buffer = new CircularBuffer(10);
        var now = DateTime.UtcNow;
        var threshold = 30.0;
        var duration = TimeSpan.FromMilliseconds(300);

        // Test Case 1: Single value above threshold - should not trigger
        buffer.Add(35.0, now);
        Assert.False(buffer.IsAboveThresholdForDuration(threshold, duration),
            "Single value should not trigger duration threshold");

        // Test Case 2: Interrupted sequence - should not trigger
        buffer.Add(35.0, now.AddMilliseconds(100));
        buffer.Add(25.0, now.AddMilliseconds(200));  // Interruption
        buffer.Add(35.0, now.AddMilliseconds(300));
        Assert.False(buffer.IsAboveThresholdForDuration(threshold, duration),
            "Interrupted sequence should not trigger");

        // Test Case 3: Continuous sequence meeting duration - should trigger
        buffer.Add(35.0, now.AddMilliseconds(400));
        buffer.Add(35.0, now.AddMilliseconds(500));
        buffer.Add(35.0, now.AddMilliseconds(700));
        Assert.True(buffer.IsAboveThresholdForDuration(threshold, duration),
            "Continuous sequence meeting duration should trigger");

        // Test Case 4: Values at threshold - should not trigger
        var bufferAtThreshold = new CircularBuffer(10);
        bufferAtThreshold.Add(30.0, now);
        bufferAtThreshold.Add(30.0, now.AddMilliseconds(200));
        bufferAtThreshold.Add(30.0, now.AddMilliseconds(400));
        Assert.False(bufferAtThreshold.IsAboveThresholdForDuration(threshold, duration),
            "Values at threshold should not trigger above threshold check");
    }

    [Fact]
    public void RingBuffer_BelowThreshold_HandlesDifferentScenarios()
    {
        // Arrange
        var buffer = new CircularBuffer(10);
        var now = DateTime.UtcNow;
        var threshold = 30.0;
        var duration = TimeSpan.FromMilliseconds(300);

        // Test Case 1: Single value below threshold - should not trigger
        buffer.Add(25.0, now);
        Assert.False(buffer.IsBelowThresholdForDuration(threshold, duration),
            "Single value should not trigger duration threshold");

        // Test Case 2: Interrupted sequence - should not trigger
        buffer.Add(25.0, now.AddMilliseconds(100));
        buffer.Add(35.0, now.AddMilliseconds(200));  // Interruption
        buffer.Add(25.0, now.AddMilliseconds(300));
        Assert.False(buffer.IsBelowThresholdForDuration(threshold, duration),
            "Interrupted sequence should not trigger");

        // Test Case 3: Continuous sequence meeting duration - should trigger
        buffer.Add(25.0, now.AddMilliseconds(400));
        buffer.Add(25.0, now.AddMilliseconds(500));
        buffer.Add(25.0, now.AddMilliseconds(700));
        Assert.True(buffer.IsBelowThresholdForDuration(threshold, duration),
            "Continuous sequence meeting duration should trigger");

        // Test Case 4: Values at threshold - should not trigger
        var bufferAtThreshold = new CircularBuffer(10);
        bufferAtThreshold.Add(30.0, now);
        bufferAtThreshold.Add(30.0, now.AddMilliseconds(200));
        bufferAtThreshold.Add(30.0, now.AddMilliseconds(400));
        Assert.False(bufferAtThreshold.IsBelowThresholdForDuration(threshold, duration),
            "Values at threshold should not trigger below threshold check");
    }

    [Fact]
    public void RingBuffer_EdgeCases()
    {
        // Arrange
        var buffer = new CircularBuffer(10);
        var now = DateTime.UtcNow;
        var threshold = 30.0;
        var duration = TimeSpan.FromMilliseconds(300);

        // Test Case 1: Empty buffer
        Assert.False(buffer.IsAboveThresholdForDuration(threshold, duration),
            "Empty buffer should not trigger above threshold");
        Assert.False(buffer.IsBelowThresholdForDuration(threshold, duration),
            "Empty buffer should not trigger below threshold");

        // Test Case 2: Exactly duration length sequence
        buffer.Add(35.0, now);
        buffer.Add(35.0, now.AddMilliseconds(150));
        buffer.Add(35.0, now.AddMilliseconds(300));
        Assert.True(buffer.IsAboveThresholdForDuration(threshold, duration),
            "Sequence exactly meeting duration should trigger");

        // Test Case 3: Buffer overflow behavior
        var overflowBuffer = new CircularBuffer(3);
        overflowBuffer.Add(35.0, now);
        overflowBuffer.Add(35.0, now.AddMilliseconds(100));
        overflowBuffer.Add(35.0, now.AddMilliseconds(200));
        overflowBuffer.Add(25.0, now.AddMilliseconds(300)); // Should push out first value
        Assert.False(overflowBuffer.IsAboveThresholdForDuration(threshold, duration),
            "Buffer overflow should maintain correct sequence behavior");

        // Test Case 4: Boundary conditions
        buffer.Add(Double.MaxValue, now.AddMilliseconds(400));
        buffer.Add(Double.MinValue, now.AddMilliseconds(500));
        Assert.False(buffer.IsAboveThresholdForDuration(threshold, duration),
            "Extreme values should be handled correctly");
    }

    [Fact]
    public void RingBuffer_ComplexSequence()
    {
        // Arrange
        var buffer = new CircularBuffer(10);
        var now = DateTime.UtcNow;
        var threshold = 30.0;
        var duration = TimeSpan.FromMilliseconds(300);

        // Complex sequence of values
        var sequence = new[]
        {
        (now, 25.0),                               // Below
        (now.AddMilliseconds(100), 35.0),          // Above
        (now.AddMilliseconds(200), 35.0),          // Above
        (now.AddMilliseconds(300), 28.0),          // Below - interrupts
        (now.AddMilliseconds(400), 35.0),          // Above - starts new sequence
        (now.AddMilliseconds(500), 35.0),          // Above
        (now.AddMilliseconds(700), 35.0),          // Above - should trigger
        (now.AddMilliseconds(800), 25.0),          // Below
    };

        foreach (var (time, value) in sequence)
        {
            buffer.Add(value, time);
            Debug.WriteLine($"Added value {value} at time offset {(time - now).TotalMilliseconds}ms");

            var values = buffer.GetValues(duration).OrderBy(v => v.Timestamp).ToList();
            Debug.WriteLine("Current buffer state:");
            foreach (var v in values)
            {
                Debug.WriteLine($"  Time offset: {(v.Timestamp - now).TotalMilliseconds}ms, Value: {v.Value}");
            }

            if (value > threshold)
            {
                Debug.WriteLine("Checking IsAboveThresholdForDuration...");
            }
        }

        // Final assertions
        Assert.True(buffer.IsAboveThresholdForDuration(threshold, duration),
            "Should find a valid duration of high values in complex sequence");

        // After adding a sequence of low values, should no longer trigger
        buffer.Add(25.0, now.AddMilliseconds(900));
        buffer.Add(25.0, now.AddMilliseconds(1000));
        buffer.Add(25.0, now.AddMilliseconds(1100));

        Assert.False(buffer.IsAboveThresholdForDuration(threshold, duration),
            "Should not trigger after sequence of low values");
    }
}