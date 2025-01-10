// File: Pulsar.Runtime/Buffers/CircularBuffer.cs

using System.Collections.Concurrent;

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
        var values = GetValues(duration).ToList();
        if (!values.Any()) return false;

        // All values must be above threshold
        return values.All(v => v.Value > threshold);
    }

    public bool IsBelowThresholdForDuration(double threshold, TimeSpan duration)
    {
        var values = GetValues(duration).ToList();
        if (!values.Any()) return false;

        // All values must be below threshold
        return values.All(v => v.Value < threshold);
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