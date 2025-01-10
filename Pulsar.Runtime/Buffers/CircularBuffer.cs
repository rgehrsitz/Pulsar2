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

    public CircularBuffer(int capacity)
    {
        if (capacity <= 0) throw new ArgumentException("Capacity must be positive", nameof(capacity));
        _buffer = new TimestampedValue[capacity];
        _head = 0;
        _count = 0;
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

    public IEnumerable<TimestampedValue> GetValues(TimeSpan duration)
    {
        var cutoff = DateTime.UtcNow - duration;
        lock (_lock)
        {
            var values = new List<TimestampedValue>(_count);
            var idx = (_head - 1 + _buffer.Length) % _buffer.Length;

            for (int i = 0; i < _count; i++)
            {
                var value = _buffer[idx];
                if (value.Timestamp < cutoff) break;
                values.Add(value);
                idx = (idx - 1 + _buffer.Length) % _buffer.Length;
            }

            return values;
        }
    }

    public bool IsAboveThresholdForDuration(double threshold, TimeSpan duration)
    {
        var now = DateTime.UtcNow;
        var values = GetValues(duration).OrderBy(v => v.Timestamp).ToList();

        Debug.WriteLine($"\nChecking threshold {threshold} for duration {duration.TotalMilliseconds}ms");
        Debug.WriteLine($"Found {values.Count} values within duration window:");

        foreach (var value in values)
        {
            Debug.WriteLine($"  Time offset: {(value.Timestamp - now).TotalMilliseconds:F1}ms, Value: {value.Value}");
        }

        if (!values.Any())
        {
            Debug.WriteLine("No values found in window");
            return false;
        }

        DateTime? startTime = null;

        foreach (var value in values)
        {
            if (value.Value > threshold)
            {
                if (startTime == null)
                {
                    startTime = value.Timestamp;
                    Debug.WriteLine($"Found value above threshold, starting timer at offset {(startTime.Value - now).TotalMilliseconds:F1}ms");
                }

                var currentDuration = value.Timestamp - startTime.Value;
                Debug.WriteLine($"Current duration: {currentDuration.TotalMilliseconds:F1}ms");

                if (currentDuration >= duration)
                {
                    Debug.WriteLine("Duration met - returning true");
                    return true;
                }
            }
            else
            {
                if (startTime != null)
                {
                    Debug.WriteLine($"Value {value.Value} below threshold, resetting timer");
                }
                startTime = null;
            }
        }

        Debug.WriteLine("Duration not met - returning false");
        return false;
    }

    public bool IsBelowThresholdForDuration(double threshold, TimeSpan duration)
    {
        var values = GetValues(duration).OrderBy(v => v.Timestamp).ToList();
        if (!values.Any()) return false;

        DateTime? startTime = null;

        foreach (var value in values)
        {
            if (value.Value < threshold)
            {
                startTime ??= value.Timestamp;

                if (value.Timestamp - startTime.Value >= duration)
                {
                    return true;
                }
            }
            else
            {
                startTime = null;
            }
        }

        return false;
    }
}

/// <summary>
/// Manages ring buffers for multiple sensors
/// </summary>
public class RingBufferManager
{
    private readonly ConcurrentDictionary<string, CircularBuffer> _buffers;
    private readonly int _capacity;

    public RingBufferManager(int capacity = 100)
    {
        _capacity = capacity;
        _buffers = new ConcurrentDictionary<string, CircularBuffer>();
    }

    public void UpdateBuffers(Dictionary<string, double> currentValues)
    {
        var timestamp = DateTime.UtcNow;
        foreach (var (sensor, value) in currentValues)
        {
            var buffer = _buffers.GetOrAdd(sensor, _ => new CircularBuffer(_capacity));
            buffer.Add(value, timestamp);
        }
    }

    public bool IsAboveThresholdForDuration(string sensor, double threshold, TimeSpan duration)
    {
        return _buffers.TryGetValue(sensor, out var buffer) &&
               buffer.IsAboveThresholdForDuration(threshold, duration);
    }

    public bool IsBelowThresholdForDuration(string sensor, double threshold, TimeSpan duration)
    {
        return _buffers.TryGetValue(sensor, out var buffer) &&
               buffer.IsBelowThresholdForDuration(threshold, duration);
    }

    public void Clear()
    {
        _buffers.Clear();
    }
}