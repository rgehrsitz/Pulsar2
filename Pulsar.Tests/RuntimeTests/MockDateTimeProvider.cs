using Pulsar.Runtime.Buffers;

namespace Pulsar.Tests.Runtime.Buffers;

public class MockDateTimeProvider : IDateTimeProvider
{
    private DateTime _currentTime;

    public MockDateTimeProvider(DateTime initialTime)
    {
        _currentTime = initialTime;
    }

    public DateTime UtcNow
    {
        get => _currentTime;
        set => _currentTime = value;
    }

    public void Advance(TimeSpan duration)
    {
        _currentTime += duration;
    }
}
