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
        var initialTime = DateTime.UtcNow;
        var mockDateTimeProvider = new MockDateTimeProvider(initialTime);
        var buffer = new CircularBuffer(3, mockDateTimeProvider);

        // Act
        buffer.Add(1.0, initialTime.AddMilliseconds(-300));
        buffer.Add(2.0, initialTime.AddMilliseconds(-200));
        buffer.Add(3.0, initialTime.AddMilliseconds(-100));

        // Assert
        var values = buffer.GetValues(TimeSpan.FromMilliseconds(500)).ToList();
        Assert.Equal(3, values.Count);
        Assert.Equal(1.0, values[0].Value);
        Assert.Equal(2.0, values[1].Value);
        Assert.Equal(3.0, values[2].Value);
    }

    [Fact]
    public void CircularBuffer_Overflow_HandlesCorrectly()
    {
        // Arrange
        var initialTime = DateTime.UtcNow;
        var mockDateTimeProvider = new MockDateTimeProvider(initialTime);
        var buffer = new CircularBuffer(2, mockDateTimeProvider);

        // Act
        buffer.Add(1.0, initialTime.AddMilliseconds(-300));
        buffer.Add(2.0, initialTime.AddMilliseconds(-200));
        buffer.Add(3.0, initialTime.AddMilliseconds(-100));

        // Assert
        var values = buffer.GetValues(TimeSpan.FromMilliseconds(500)).ToList();

        // Because the code returns ascending by time, we expect [2.0, 3.0].
        Assert.Equal(2, values.Count);
        Assert.Equal(2.0, values[0].Value);
        Assert.Equal(3.0, values[1].Value);
    }

    [Fact]
    public void RingBufferManager_UpdateBuffers_WorksCorrectly()
    {
        var initialTime = DateTime.UtcNow;
        var mockDateTimeProvider = new MockDateTimeProvider(initialTime);
        Debug.WriteLine($"\nTest started at: {initialTime:HH:mm:ss.fff}");

        // Arrange
        var manager = new RingBufferManager(capacity: 10, dateTimeProvider: mockDateTimeProvider);
        var values = new Dictionary<string, double>
        {
            ["temp1"] = 75.0,
            ["temp2"] = 85.0
        };

        // Initial update
        Debug.WriteLine($"\nFirst update at {mockDateTimeProvider.UtcNow:HH:mm:ss.fff}");
        manager.UpdateBuffers(values);

        // Add debug verification of values right after update
        Debug.WriteLine("\nVerifying values after initial update:");
        var buffer = manager._buffers["temp2"]; // We'll need to make _buffers protected internal
        var initialValues = buffer.GetValues(TimeSpan.FromMilliseconds(1000)).ToList();
        foreach (var v in initialValues)
        {
            Debug.WriteLine($"Value: {v.Value} at {v.Timestamp:HH:mm:ss.fff}");
        }

        // Advance time
        mockDateTimeProvider.Advance(TimeSpan.FromMilliseconds(300));
        Debug.WriteLine($"After advance, time elapsed: {(mockDateTimeProvider.UtcNow - initialTime).TotalMilliseconds:F1}ms");

        // Test extended mode
        bool extendedResult = manager.IsAboveThresholdForDuration(
            "temp2",
            70.0,
            TimeSpan.FromMilliseconds(200),
            extendLastKnown: true);

        Assert.True(extendedResult,
            $"temp2 should remain above 70.0 for 200ms in extended mode");
    }

    [Fact]
    public void CircularBuffer_TimeBasedRetrieval_RespectsTimeWindow()
    {
        // Arrange
        var initialTime = DateTime.UtcNow;
        var mockDateTimeProvider = new MockDateTimeProvider(initialTime);
        var buffer = new CircularBuffer(5, mockDateTimeProvider);

        // Act - Add values at different times
        buffer.Add(1.0, initialTime.AddMilliseconds(-600));  // Outside 500ms window
        buffer.Add(2.0, initialTime.AddMilliseconds(-400));  // Inside window
        buffer.Add(3.0, initialTime.AddMilliseconds(-200));  // Inside window
        buffer.Add(4.0, initialTime.AddMilliseconds(-100));  // Inside window

        // Assert
        var values = buffer.GetValues(TimeSpan.FromMilliseconds(500)).ToList();
        Assert.Equal(3, values.Count);  // Should only get values within last 500ms
        Assert.Equal(2.0, values[0].Value);
        Assert.Equal(3.0, values[1].Value);
        Assert.Equal(4.0, values[2].Value);
    }

    [Fact]
    public void RingBuffer_ThresholdBehavior_WorksCorrectly()
    {
        var initialTime = DateTime.UtcNow;
        var mockDateTimeProvider = new MockDateTimeProvider(initialTime);
        var buffer = new CircularBuffer(10, mockDateTimeProvider);
        var threshold = 30.0;
        var duration = TimeSpan.FromMilliseconds(300);

        // Setup scenario where strict mode should NOT trigger
        buffer.Add(25.0, initialTime);  // Value before window
        buffer.Add(35.0, initialTime.AddMilliseconds(100));  // First in window
        buffer.Add(35.0, initialTime.AddMilliseconds(200));  // Second in window
        buffer.Add(35.0, initialTime.AddMilliseconds(300));  // Third (at end of window)

        // Strict mode should return false because full time coverage is not explicitly verified
        Assert.False(buffer.IsAboveThresholdForDuration(threshold, duration),
            "Strict mode should not trigger without full time coverage verification");
    }

    [Fact]
    public void RingBuffer_BelowThreshold_HandlesDifferentScenarios()
    {
        var now = DateTime.UtcNow;
        var mockDateTimeProvider = new MockDateTimeProvider(now);
        var threshold = 30.0;
        var duration = TimeSpan.FromMilliseconds(300);

        // Test Case 1: Single value below threshold - should not trigger
        var buffer1 = new CircularBuffer(10, mockDateTimeProvider);
        buffer1.Add(25.0, mockDateTimeProvider.UtcNow);
        Assert.False(buffer1.IsBelowThresholdForDuration(threshold, duration),
            "Single value should not trigger duration threshold");

        // Test Case 2: Interrupted sequence - should not trigger
        var buffer2 = new CircularBuffer(10, mockDateTimeProvider);
        mockDateTimeProvider.Advance(TimeSpan.FromMilliseconds(100));
        buffer2.Add(25.0, mockDateTimeProvider.UtcNow);
        mockDateTimeProvider.Advance(TimeSpan.FromMilliseconds(100));
        buffer2.Add(35.0, mockDateTimeProvider.UtcNow);  // Interruption
        mockDateTimeProvider.Advance(TimeSpan.FromMilliseconds(100));
        buffer2.Add(25.0, mockDateTimeProvider.UtcNow);
        Assert.False(buffer2.IsBelowThresholdForDuration(threshold, duration),
            "Interrupted sequence should not trigger");

        // Test Case 3: Continuous sequence meeting duration - should trigger
        var buffer3 = new CircularBuffer(10, mockDateTimeProvider);
        mockDateTimeProvider.Advance(TimeSpan.FromMilliseconds(100));
        buffer3.Add(25.0, mockDateTimeProvider.UtcNow);
        mockDateTimeProvider.Advance(TimeSpan.FromMilliseconds(100));
        buffer3.Add(25.0, mockDateTimeProvider.UtcNow);
        mockDateTimeProvider.Advance(TimeSpan.FromMilliseconds(200));
        buffer3.Add(25.0, mockDateTimeProvider.UtcNow);
        Assert.True(buffer3.IsBelowThresholdForDuration(threshold, duration),
            "Continuous sequence meeting duration should trigger");

        // Test Case 4: Values at threshold - should not trigger
        var bufferAtThreshold = new CircularBuffer(10, mockDateTimeProvider);
        mockDateTimeProvider.Advance(TimeSpan.FromMilliseconds(100));
        bufferAtThreshold.Add(30.0, mockDateTimeProvider.UtcNow);
        mockDateTimeProvider.Advance(TimeSpan.FromMilliseconds(200));
        bufferAtThreshold.Add(30.0, mockDateTimeProvider.UtcNow);
        mockDateTimeProvider.Advance(TimeSpan.FromMilliseconds(200));
        bufferAtThreshold.Add(30.0, mockDateTimeProvider.UtcNow);
        Assert.False(bufferAtThreshold.IsBelowThresholdForDuration(threshold, duration),
            "Values at threshold should not trigger below threshold check");
    }

    [Fact]
    public void RingBuffer_EdgeCases()
    {
        var initialTime = DateTime.UtcNow;
        var mockDateTimeProvider = new MockDateTimeProvider(initialTime);
        var threshold = 30.0;
        var duration = TimeSpan.FromMilliseconds(300);

        // Test Case 1: Empty buffer
        var bufferEmpty = new CircularBuffer(10, mockDateTimeProvider);
        Assert.False(bufferEmpty.IsAboveThresholdForDuration(threshold, duration),
            "Empty buffer should not trigger above threshold");
        Assert.False(bufferEmpty.IsBelowThresholdForDuration(threshold, duration),
            "Empty buffer should not trigger below threshold");

        // Test Case 2: Exactly duration length sequence
        var bufferDuration = new CircularBuffer(10, mockDateTimeProvider);
        bufferDuration.Add(35.0, mockDateTimeProvider.UtcNow);
        mockDateTimeProvider.Advance(TimeSpan.FromMilliseconds(150));
        bufferDuration.Add(35.0, mockDateTimeProvider.UtcNow);
        mockDateTimeProvider.Advance(TimeSpan.FromMilliseconds(150));
        bufferDuration.Add(35.0, mockDateTimeProvider.UtcNow);
        Assert.True(bufferDuration.IsAboveThresholdForDuration(threshold, duration),
            "Sequence exactly meeting duration should trigger");

        // Test Case 3: Buffer overflow behavior
        var overflowBuffer = new CircularBuffer(3, mockDateTimeProvider);
        overflowBuffer.Add(35.0, mockDateTimeProvider.UtcNow);
        mockDateTimeProvider.Advance(TimeSpan.FromMilliseconds(100));
        overflowBuffer.Add(35.0, mockDateTimeProvider.UtcNow);
        mockDateTimeProvider.Advance(TimeSpan.FromMilliseconds(100));
        overflowBuffer.Add(35.0, mockDateTimeProvider.UtcNow);
        mockDateTimeProvider.Advance(TimeSpan.FromMilliseconds(100));
        overflowBuffer.Add(25.0, mockDateTimeProvider.UtcNow); // Should push out the oldest value
        Assert.False(overflowBuffer.IsAboveThresholdForDuration(threshold, duration),
            "Buffer overflow should maintain correct sequence behavior");

        // Test Case 4: Boundary conditions (using a fresh buffer again)
        var bufferBoundary = new CircularBuffer(10, mockDateTimeProvider);
        mockDateTimeProvider.Advance(TimeSpan.FromMilliseconds(400));
        bufferBoundary.Add(double.MaxValue, mockDateTimeProvider.UtcNow);
        mockDateTimeProvider.Advance(TimeSpan.FromMilliseconds(100));
        bufferBoundary.Add(double.MinValue, mockDateTimeProvider.UtcNow);
        Assert.False(bufferBoundary.IsAboveThresholdForDuration(threshold, duration),
            "Extreme values should be handled correctly");
    }

    [Fact]
    public void RingBuffer_ExtendedMode_HandlesLastKnownValue()
    {
        var initialTime = DateTime.UtcNow;
        var mockDateTimeProvider = new MockDateTimeProvider(initialTime);
        var buffer = new CircularBuffer(10, mockDateTimeProvider);
        var threshold = 30.0;
        var duration = TimeSpan.FromMilliseconds(200);

        Debug.WriteLine($"\n=== Test Case 1: Single Value ===");
        Debug.WriteLine($"Current time: {mockDateTimeProvider.UtcNow}");

        // Add a value above threshold
        buffer.Add(35.0, mockDateTimeProvider.UtcNow);
        Debug.WriteLine($"Adding value 35.0 at {mockDateTimeProvider.UtcNow:HH:mm:ss.fff}");

        // Advance time by full duration + a little extra to ensure we cover it
        mockDateTimeProvider.Advance(TimeSpan.FromMilliseconds(250));
        Debug.WriteLine($"Advanced time to: {mockDateTimeProvider.UtcNow:HH:mm:ss.fff}");

        Debug.WriteLine("\nTesting extended mode:");
        var extendedResult = buffer.IsAboveThresholdForDuration(threshold, duration, extendLastKnown: true);
        Debug.WriteLine($"Extended mode result: {extendedResult}");

        Debug.WriteLine("\nTesting strict mode:");
        var strictResult = buffer.IsAboveThresholdForDuration(threshold, duration, extendLastKnown: false);
        Debug.WriteLine($"Strict mode result: {strictResult}");

        Assert.True(extendedResult, "Single value should trigger in extended mode");
        Assert.False(strictResult, "Single value should not trigger in strict mode");
    }

    [Fact]
    public void RingBuffer_AboveThreshold_HandlesDifferentScenarios()
    {
        var initialTime = DateTime.UtcNow;
        var mockDateTimeProvider = new MockDateTimeProvider(initialTime);
        var buffer = new CircularBuffer(10, mockDateTimeProvider);
        var threshold = 30.0;
        var duration = TimeSpan.FromMilliseconds(300);

        // Test Case 1: Single value above threshold - should NOT trigger
        buffer.Add(35.0, mockDateTimeProvider.UtcNow);
        Assert.False(buffer.IsAboveThresholdForDuration(threshold, duration),
            "Single value should not trigger duration threshold in strict mode");

        // Test Case 2: Interrupted sequence - should not trigger
        mockDateTimeProvider.Advance(TimeSpan.FromMilliseconds(100));
        buffer.Add(35.0, mockDateTimeProvider.UtcNow);
        Debug.WriteLine($"Added 35.0 at {mockDateTimeProvider.UtcNow:HH:mm:ss.fff}");

        mockDateTimeProvider.Advance(TimeSpan.FromMilliseconds(100));
        buffer.Add(25.0, mockDateTimeProvider.UtcNow);  // Interruption
        Debug.WriteLine($"Added 25.0 at {mockDateTimeProvider.UtcNow:HH:mm:ss.fff}");

        mockDateTimeProvider.Advance(TimeSpan.FromMilliseconds(100));
        buffer.Add(35.0, mockDateTimeProvider.UtcNow);
        Debug.WriteLine($"Added 35.0 at {mockDateTimeProvider.UtcNow:HH:mm:ss.fff}");

        Assert.False(buffer.IsAboveThresholdForDuration(threshold, duration),
            "Interrupted sequence should not trigger");

        // Test Case 3: Continuous sequence meeting duration - should trigger
        mockDateTimeProvider.Advance(TimeSpan.FromMilliseconds(100));
        buffer.Add(35.0, mockDateTimeProvider.UtcNow);
        Debug.WriteLine($"Added 35.0 at {mockDateTimeProvider.UtcNow:HH:mm:ss.fff}");

        mockDateTimeProvider.Advance(TimeSpan.FromMilliseconds(100));
        buffer.Add(35.0, mockDateTimeProvider.UtcNow);
        Debug.WriteLine($"Added 35.0 at {mockDateTimeProvider.UtcNow:HH:mm:ss.fff}");

        mockDateTimeProvider.Advance(TimeSpan.FromMilliseconds(200));
        buffer.Add(35.0, mockDateTimeProvider.UtcNow);
        Debug.WriteLine($"Added 35.0 at {mockDateTimeProvider.UtcNow:HH:mm:ss.fff}");

        Assert.True(buffer.IsAboveThresholdForDuration(threshold, duration),
            "Continuous sequence meeting duration should trigger");

        // Test Case 4: Values at threshold - should not trigger
        var bufferAtThreshold = new CircularBuffer(10, mockDateTimeProvider);
        bufferAtThreshold.Add(30.0, mockDateTimeProvider.UtcNow);
        bufferAtThreshold.Add(30.0, mockDateTimeProvider.UtcNow.AddMilliseconds(200));
        bufferAtThreshold.Add(30.0, mockDateTimeProvider.UtcNow.AddMilliseconds(400));
        Debug.WriteLine($"Added 30.0 at {mockDateTimeProvider.UtcNow:HH:mm:ss.fff}");
        Debug.WriteLine($"Added 30.0 at {mockDateTimeProvider.UtcNow.AddMilliseconds(200):HH:mm:ss.fff}");
        Debug.WriteLine($"Added 30.0 at {mockDateTimeProvider.UtcNow.AddMilliseconds(400):HH:mm:ss.fff}");

        Assert.False(bufferAtThreshold.IsAboveThresholdForDuration(threshold, duration),
            "Values at threshold should not trigger above threshold check");
    }


    [Fact]
    public void RingBuffer_ComplexSequence_StrictMode()
    {
        // Arrange:
        // We assume that strict mode uses the timestamp of the last reported value as "now"
        // and evaluates that all readings in the window [now - requiredDuration, now] (plus the reading immediately preceding that window)
        // are above threshold.
        var requiredDuration = TimeSpan.FromMilliseconds(200);
        var threshold = 70.0;

        // Get an initial "anchor" time. For clarity, we set baseTime as the time when the last reading will be recorded.
        var initialTime = DateTime.UtcNow;
        var mockDateTimeProvider = new MockDateTimeProvider(initialTime);
        var buffer = new CircularBuffer(10, mockDateTimeProvider);

        // We want to simulate a sequence where the continuous block (including the reading immediately preceding the window)
        // covers at least 200ms.
        //
        // For Scenario A (expected true):
        // Let baseTime be the timestamp of the last reading.
        // We then simulate:
        //   - A reading just prior to the window (e.g. at baseTime - 250ms) that is above threshold.
        //   - Readings within the window.
        //
        // We want the earliest reading in the continuous block to occur at or before (baseTime - 200ms).
        // For example, let’s choose:
        //   Reading 1: baseTime - 250ms (the "prior" reading)
        //   Reading 2: baseTime - 200ms (begins the continuous block)
        //   Reading 3: baseTime - 100ms
        //   Reading 4: baseTime - 0ms (the last reading)
        //
        // That means the continuous block (from reading 2 to reading 4) spans 200 ms.

        var baseTime = initialTime + TimeSpan.FromSeconds(1); // Arbitrary future time for the last reading

        // Add the reading immediately before the window (at baseTime - 250ms)
        mockDateTimeProvider.UtcNow = baseTime - TimeSpan.FromMilliseconds(250);
        buffer.Add(85, mockDateTimeProvider.UtcNow);
        Debug.WriteLine($"Scenario A: Added value 85 at {mockDateTimeProvider.UtcNow:HH:mm:ss.fff}");

        // Add a reading at baseTime - 200ms (beginning of the continuous block)
        mockDateTimeProvider.UtcNow = baseTime - TimeSpan.FromMilliseconds(200);
        buffer.Add(85, mockDateTimeProvider.UtcNow);
        Debug.WriteLine($"Scenario A: Added value 85 at {mockDateTimeProvider.UtcNow:HH:mm:ss.fff}");

        // Add another reading at baseTime - 100ms (within the window)
        mockDateTimeProvider.UtcNow = baseTime - TimeSpan.FromMilliseconds(100);
        buffer.Add(85, mockDateTimeProvider.UtcNow);
        Debug.WriteLine($"Scenario A: Added value 85 at {mockDateTimeProvider.UtcNow:HH:mm:ss.fff}");

        // Add the last reading at baseTime (the anchor t = 0 for strict mode evaluation)
        mockDateTimeProvider.UtcNow = baseTime;
        buffer.Add(85, mockDateTimeProvider.UtcNow);
        Debug.WriteLine($"Scenario A: Added value 85 at {mockDateTimeProvider.UtcNow:HH:mm:ss.fff}");

        // At this point, the continuous block (from reading at baseTime - 200ms to the last reading at baseTime) spans exactly 200ms.
        // The reading immediately prior (at baseTime - 250ms) is also above threshold.
        bool resultA = buffer.IsAboveThresholdForDuration(threshold, requiredDuration, extendLastKnown: false);
        Debug.WriteLine($"Scenario A (valid continuous period) strict mode returned: {resultA}");
        Assert.True(resultA, "Scenario A: Expected true because the continuous period (and immediate prior reading) are above threshold");


        // --- Scenario B: Invalid continuous period ---
        // Now we simulate a history identical to Scenario A except that the reading immediately preceding the window is below threshold.
        // Sensor history for Scenario B:
        //   Reading 1: baseTime - 250ms: value = 65 (below threshold)
        //   Reading 2: baseTime - 200ms: value = 85
        //   Reading 3: baseTime - 100ms: value = 85
        //   Reading 4: baseTime: value = 85
        //
        // In this case, even though the in-window readings are above threshold,
        // the fact that the reading immediately prior (at baseTime - 250ms) is below threshold should cause strict mode to return false.

        buffer = new CircularBuffer(10, mockDateTimeProvider);

        // For Scenario B, use the same baseTime.
        mockDateTimeProvider.UtcNow = baseTime - TimeSpan.FromMilliseconds(250);
        buffer.Add(65, mockDateTimeProvider.UtcNow);
        Debug.WriteLine($"Scenario B: Added value 65 at {mockDateTimeProvider.UtcNow:HH:mm:ss.fff}");

        mockDateTimeProvider.UtcNow = baseTime - TimeSpan.FromMilliseconds(200);
        buffer.Add(85, mockDateTimeProvider.UtcNow);
        Debug.WriteLine($"Scenario B: Added value 85 at {mockDateTimeProvider.UtcNow:HH:mm:ss.fff}");

        mockDateTimeProvider.UtcNow = baseTime - TimeSpan.FromMilliseconds(100);
        buffer.Add(85, mockDateTimeProvider.UtcNow);
        Debug.WriteLine($"Scenario B: Added value 85 at {mockDateTimeProvider.UtcNow:HH:mm:ss.fff}");

        mockDateTimeProvider.UtcNow = baseTime;
        buffer.Add(85, mockDateTimeProvider.UtcNow);
        Debug.WriteLine($"Scenario B: Added value 85 at {mockDateTimeProvider.UtcNow:HH:mm:ss.fff}");

        bool resultB = buffer.IsAboveThresholdForDuration(threshold, requiredDuration, extendLastKnown: false);
        Debug.WriteLine($"Scenario B (invalid due to prior reading) strict mode returned: {resultB}");
        Assert.False(resultB, "Scenario B: Expected false because the reading immediately preceding the window is below threshold");
    }


    [Fact]
    public void RingBuffer_TemporalConditions_WorksCorrectly()
    {
        // Arrange
        var initialTime = DateTime.UtcNow;
        var mockDateTimeProvider = new MockDateTimeProvider(initialTime);
        var manager = new RingBufferManager(capacity: 10, dateTimeProvider: mockDateTimeProvider);
        var sensor = "temperature";
        var threshold = 100.0;
        var requiredDuration = TimeSpan.FromMilliseconds(200);

        // Add values over time:
        // t=0ms:  90°F  -> below threshold, starts off below.
        manager.UpdateBuffers(new Dictionary<string, double> { [sensor] = 90 });
        Debug.WriteLine($"Added 90 at {mockDateTimeProvider.UtcNow:HH:mm:ss.fff}");

        // t=100ms: 100°F -> equals threshold, still not above.
        mockDateTimeProvider.Advance(TimeSpan.FromMilliseconds(100));
        manager.UpdateBuffers(new Dictionary<string, double> { [sensor] = 100 });
        Debug.WriteLine($"Added 100 at {mockDateTimeProvider.UtcNow:HH:mm:ss.fff}");

        // t=200ms: 110°F -> above threshold, potential start of a valid streak.
        mockDateTimeProvider.Advance(TimeSpan.FromMilliseconds(100));
        manager.UpdateBuffers(new Dictionary<string, double> { [sensor] = 110 });
        Debug.WriteLine($"Added 110 at {mockDateTimeProvider.UtcNow:HH:mm:ss.fff}");

        // t=300ms: 115°F -> still above threshold.
        mockDateTimeProvider.Advance(TimeSpan.FromMilliseconds(100));
        manager.UpdateBuffers(new Dictionary<string, double> { [sensor] = 115 });
        Debug.WriteLine($"Added 115 at {mockDateTimeProvider.UtcNow:HH:mm:ss.fff}");

        // At this point, in strict mode:
        // - The last update was at T=300ms with a value of 115°F.
        // - In strict mode, the continuous above threshold streak is measured from 300ms backward.
        //   For a required duration of 200ms, we check the window from T=100ms to T=300ms.
        //   In that window, we see:
        //      T=100ms: 100°F  (at threshold, not above threshold)
        //      T=200ms: 110°F  (above)
        //      T=300ms: 115°F  (above)
        //   So strict mode should fail because the value at T=100ms isn't above threshold.
        Assert.False(manager.IsAboveThresholdForDuration(sensor, threshold, TimeSpan.FromMilliseconds(200), extendLastKnown: false),
            $"Strict mode: Should not trigger since continuous period ending at the last reading does not fully satisfy the threshold");

        // For extended mode, however, we assume that the last reported value persists until rule evaluation.
        // Advance time so that the rule is evaluated at T=400ms.
        mockDateTimeProvider.Advance(TimeSpan.FromMilliseconds(100));
        // Extended mode uses the current time (T=400ms) as t = now.
        // Even though T=100ms had a borderline value, the fallback via extended mode should look at the latest value.
        // Here, the last reported value (115°F at T=300ms) is assumed to persist until T=400ms.
        // The elapsed period is 400ms - 300ms = 100ms, which is not enough to meet the required 200ms.
        Assert.False(manager.IsAboveThresholdForDuration(sensor, threshold, requiredDuration, extendLastKnown: true),
            $"Extended mode: Should not trigger because the elapsed persistence period is insufficient");

        // Now, add one more update in extended mode so that the elapsed duration from the last update is sufficient.
        // t=500ms: 120°F -> still above threshold.
        mockDateTimeProvider.Advance(TimeSpan.FromMilliseconds(100));
        manager.UpdateBuffers(new Dictionary<string, double> { [sensor] = 120 });
        Debug.WriteLine($"Added 120 at {mockDateTimeProvider.UtcNow:HH:mm:ss.fff}");

        // Now in strict mode:
        // - The last update is at T=500ms. The window for 200ms is [300ms, 500ms].
        //   The sequence in that interval is: T=300ms:115°F, T=500ms:120°F.
        //   Given that the value at T=300ms was above the threshold, strict mode should now trigger.
        Assert.True(manager.IsAboveThresholdForDuration(sensor, threshold, TimeSpan.FromMilliseconds(200), extendLastKnown: false),
            $"Strict mode: Should trigger because a valid continuous period exists ending at the last reading");

        // In extended mode, the rule is evaluated at the current time (T=500ms),
        // so the elapsed time since the last update is 0ms; we need to advance time.
        mockDateTimeProvider.Advance(TimeSpan.FromMilliseconds(250));  // Now rule evaluation time is T=750ms.
                                                                       // In extended mode, the last reading at T=500ms is assumed to persist until T=750ms,
                                                                       // so the elapsed duration is 250ms, which satisfies the 200ms requirement.
        Assert.True(manager.IsAboveThresholdForDuration(sensor, threshold, TimeSpan.FromMilliseconds(200), extendLastKnown: true),
            $"Extended mode: Should trigger because the persistence duration meets the requirement");
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
}