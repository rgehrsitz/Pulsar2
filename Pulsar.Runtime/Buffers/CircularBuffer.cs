// File: Pulsar.Runtime/Buffers/CircularBuffer.cs

using System.Collections.Concurrent;
using System.Diagnostics;

namespace Pulsar.Runtime.Buffers;

/// <summary>
/// Represents a single value with its timestamp
/// </summary>
public readonly struct TimestampedValue
{
    public readonly DateTime Timestamp { get; init; }
    public readonly double Value { get; init; }

    public TimestampedValue(DateTime timestamp, double value)
    {
        Timestamp = timestamp;
        Value = value;
    }
}

/// <summary>
/// A fixed-size ring buffer for a single sensor's values
/// </summary>
public class CircularBuffer
{
    private readonly TimestampedValue[] _buffer;
    private int _head;
    private int _count;
    private readonly object _lock = new();
    private readonly IDateTimeProvider _dateTimeProvider;


    public CircularBuffer(int capacity, IDateTimeProvider dateTimeProvider)
    {
        if (capacity <= 0) throw new ArgumentException("Capacity must be positive", nameof(capacity));
        _buffer = new TimestampedValue[capacity];
        _head = 0;
        _count = 0;
        _dateTimeProvider = dateTimeProvider;
    }

    public void Add(double value, DateTime timestamp)
    {
        lock (_lock)
        {
            _buffer[_head] = new TimestampedValue(timestamp, value);
            _head = (_head + 1) % _buffer.Length;
            if (_count < _buffer.Length) _count++;
        }
    }

    public IEnumerable<TimestampedValue> GetValues(TimeSpan duration, bool includeOlder = false)
    {
        lock (_lock)
        {
            if (_count == 0)
                return Enumerable.Empty<TimestampedValue>();

            // Anchor the window using the current time from the provider.
            var now = _dateTimeProvider.UtcNow;
            var cutoff = now - duration;

            var valuesInWindow = new List<TimestampedValue>(_count);
            int idx = (_head - 1 + _buffer.Length) % _buffer.Length;
            for (int i = 0; i < _count; i++)
            {
                var value = _buffer[idx];
                if (value.Timestamp >= cutoff && value.Timestamp <= now)
                {
                    valuesInWindow.Add(value);
                }
                idx = (idx - 1 + _buffer.Length) % _buffer.Length;
            }

            // Order the values chronologically.
            var orderedValues = valuesInWindow.OrderBy(x => x.Timestamp).ToList();

            if (includeOlder)
            {
                // Try to obtain a guard value: the most recent value that is older than the window.
                // (Even if we already have some values in the window, we want the reading immediately preceding the window.)
                int guardIdx = (_head - 1 + _buffer.Length) % _buffer.Length;
                TimestampedValue? guardValue = null;
                for (int i = 0; i < _count; i++)
                {
                    var candidate = _buffer[guardIdx];
                    if (candidate.Timestamp < cutoff)
                    {
                        // Found a candidate; choose the one with the greatest timestamp (i.e. the most recent before cutoff)
                        if (guardValue == null || candidate.Timestamp > guardValue.Value.Timestamp)
                        {
                            guardValue = candidate;
                        }
                    }
                    guardIdx = (guardIdx - 1 + _buffer.Length) % _buffer.Length;
                }
                // If we found a guard value, prepend it to the ordered list.
                if (guardValue.HasValue)
                {
                    // Insert at the beginning of the list.
                    orderedValues.Insert(0, guardValue.Value);
                }
            }

            // If no values are found, return empty.
            return orderedValues;
        }
    }



    public bool IsAboveThresholdForDuration(double threshold, TimeSpan duration, bool extendLastKnown = false)
    {
        // In strict mode (extendLastKnown == false), we now pass includeOlder: true so that we capture
        // a reading immediately preceding the window as a guard value.
        var values = GetValues(duration, includeOlder: extendLastKnown ? true : true)
                        .OrderBy(v => v.Timestamp)
                        .ToList();

        Debug.WriteLine($"\nChecking threshold {threshold} for duration {duration.TotalMilliseconds}ms (Mode: {(extendLastKnown ? "extend_last_known" : "strict")})");
        Debug.WriteLine($"Total values: {values.Count}");

        if (!values.Any())
        {
            Debug.WriteLine("No values in buffer");
            return false;
        }

        if (!extendLastKnown)
        {
            // Strict mode: use the last reported reading as the anchor (t = last-value-time)
            return HasContinuousSequenceAboveThreshold(values, threshold, duration);
        }

        // Extended mode: use the current time (t = rule-run time)
        var now = _dateTimeProvider.UtcNow;
        var lastValue = values.Last();

        // The last reported value must be above threshold.
        if (lastValue.Value <= threshold)
        {
            Debug.WriteLine("Extended mode - Last reading below threshold");
            return false;
        }

        // Compute the duration from the rule-run time to the last value's timestamp.
        var durationSinceLastReading = now - lastValue.Timestamp;
        Debug.WriteLine($"Extended mode - Duration since last reading: {durationSinceLastReading.TotalMilliseconds}ms");
        Debug.WriteLine($"Required duration: {duration.TotalMilliseconds}ms");

        return durationSinceLastReading >= duration;
    }

    private bool HasContinuousSequenceAboveThreshold(List<TimestampedValue> values, double threshold, TimeSpan requiredDuration)
    {
        if (!values.Any())
        {
            Debug.WriteLine("No values in buffer");
            return false;
        }

        // In strict mode we require at least two data points (a guard plus a value in the window)
        // to have any chance of covering a duration.
        if (values.Count < 2)
        {
            Debug.WriteLine("Not enough values (guard + at least one in-window) to cover the duration");
            return false;
        }

        // The last reading in the list is our anchor (most recent report).
        var lastReading = values.Last();
        var windowEnd = lastReading.Timestamp;
        var windowStart = windowEnd - requiredDuration;

        // Get readings that fall within the window.
        var windowValues = values.Where(v => v.Timestamp >= windowStart && v.Timestamp <= windowEnd).ToList();

        // Find the guard reading: the most recent value that occurred before the window.
        var previousReading = values
             .Where(v => v.Timestamp < windowStart)
             .OrderByDescending(v => v.Timestamp)
             .FirstOrDefault();

        Debug.WriteLine($"Window start: {windowStart:HH:mm:ss.fff}");
        Debug.WriteLine($"Window end: {windowEnd:HH:mm:ss.fff}");
        Debug.WriteLine($"Values in window: {windowValues.Count}");
        if (previousReading.Timestamp != default)
        {
            Debug.WriteLine($"Previous reading: {previousReading.Value} at {previousReading.Timestamp:HH:mm:ss.fff}");
        }
        else
        {
            Debug.WriteLine("No previous reading found.");
        }

        // Validate that every reading in the window is above threshold.
        bool windowValid = windowValues.All(v => v.Value > threshold);
        // Validate that the guard reading (if present) is above threshold.
        bool previousValid = previousReading.Timestamp == default || previousReading.Value > threshold;

        Debug.WriteLine($"Window valid: {windowValid}");
        Debug.WriteLine($"Previous reading valid: {previousValid}");

        return windowValid && previousValid;
    }



    public bool IsBelowThresholdForDuration(double threshold, TimeSpan duration, bool extendLastKnown = false)
    {
        // For extended mode, pass includeOlder=true to obtain fallback data.
        var values = GetValues(duration, includeOlder: extendLastKnown)
                        .OrderBy(v => v.Timestamp)
                        .ToList();

        Debug.WriteLine($"\nChecking threshold {threshold} for duration {duration.TotalMilliseconds}ms (Mode: {(extendLastKnown ? "extend_last_known" : "strict")})");
        Debug.WriteLine($"Total values: {values.Count}");

        if (!values.Any())
        {
            Debug.WriteLine("No values in buffer");
            return false;
        }

        if (!extendLastKnown)
        {
            // Strict mode: use the reported readings (with the last reading's timestamp as t=0)
            return HasContinuousSequenceBelowThreshold(values, threshold, duration);
        }

        // Extended mode: use the current time as t=0.
        var now = _dateTimeProvider.UtcNow;
        var lastValue = values.Last();

        // The last reported value must be below threshold.
        if (lastValue.Value >= threshold)
        {
            Debug.WriteLine("Extended mode - Last reading above or at threshold");
            return false;
        }

        // Compute the duration from the rule-run time to the last reading's timestamp.
        var durationSinceLastReading = now - lastValue.Timestamp;
        Debug.WriteLine($"Extended mode - Duration since last reading: {durationSinceLastReading.TotalMilliseconds}ms");
        Debug.WriteLine($"Required duration: {duration.TotalMilliseconds}ms");

        return durationSinceLastReading >= duration;
    }

    private bool HasContinuousSequenceBelowThreshold(List<TimestampedValue> values, double threshold, TimeSpan requiredDuration)
    {
        if (!values.Any())
        {
            Debug.WriteLine("No values in buffer");
            return false;
        }

        // Ensure the very last reading is below the threshold.
        var lastReading = values.Last();
        if (lastReading.Value >= threshold)
        {
            Debug.WriteLine("Last reading is not below threshold. Failing strict mode check.");
            return false;
        }

        // Walk backwards from the last reading to find the contiguous block of below-threshold readings.
        TimestampedValue firstInSequence = lastReading;
        for (int i = values.Count - 2; i >= 0; i--)
        {
            if (values[i].Value < threshold)
            {
                firstInSequence = values[i];
            }
            else
            {
                break;
            }
        }

        var continuousDuration = lastReading.Timestamp - firstInSequence.Timestamp;
        Debug.WriteLine($"Continuous sequence (below): from {firstInSequence.Timestamp:HH:mm:ss.fff} to {lastReading.Timestamp:HH:mm:ss.fff} ({continuousDuration.TotalMilliseconds}ms)");
        return continuousDuration >= requiredDuration;
    }


}

/// <summary>
/// Manages ring buffers for multiple sensors
/// </summary>
public class RingBufferManager : IDisposable
{
    public readonly ConcurrentDictionary<string, CircularBuffer> _buffers;
    private readonly int _capacity;
    private readonly IDateTimeProvider _dateTimeProvider;
    private bool _disposed;

    public RingBufferManager(int capacity = 100, IDateTimeProvider dateTimeProvider = null)
    {
        _capacity = capacity;
        _buffers = new ConcurrentDictionary<string, CircularBuffer>();
        _dateTimeProvider = dateTimeProvider ?? new SystemDateTimeProvider();
    }

    public void UpdateBuffer(string sensor, double value, DateTime timestamp)
    {
        var buffer = _buffers.GetOrAdd(sensor, _ => new CircularBuffer(_capacity, _dateTimeProvider));
        buffer.Add(value, timestamp);
    }

    public void UpdateBuffers(Dictionary<string, double> currentValues)
    {
        var timestamp = _dateTimeProvider.UtcNow;
        foreach (var (sensor, value) in currentValues)
        {
            UpdateBuffer(sensor, value, timestamp);
        }
    }

    public bool IsAboveThresholdForDuration(string sensor, double threshold, TimeSpan duration, bool extendLastKnown = false)
    {
        return _buffers.TryGetValue(sensor, out var buffer) &&
               buffer.IsAboveThresholdForDuration(threshold, duration, extendLastKnown);
    }

    public bool IsBelowThresholdForDuration(string sensor, double threshold, TimeSpan duration, bool extendLastKnown = false)
    {
        return _buffers.TryGetValue(sensor, out var buffer) &&
               buffer.IsBelowThresholdForDuration(threshold, duration, extendLastKnown);
    }

    public void Clear()
    {
        _buffers.Clear();
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            Clear();
            _disposed = true;
        }
    }
}