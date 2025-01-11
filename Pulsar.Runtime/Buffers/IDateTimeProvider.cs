// File: Pulsar.Runtime/Buffers/IDateTimeProvider.cs

namespace Pulsar.Runtime.Buffers
{
    public interface IDateTimeProvider
    {
        DateTime UtcNow { get; }
    }
}
