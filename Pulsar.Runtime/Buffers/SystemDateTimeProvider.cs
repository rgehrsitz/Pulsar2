// File: Pulsar.Runtime/Buffers/SystemDateTimeProvider.cs

namespace Pulsar.Runtime.Buffers
{
    public class SystemDateTimeProvider : IDateTimeProvider
    {
        public DateTime UtcNow => DateTime.UtcNow;
    }
}
