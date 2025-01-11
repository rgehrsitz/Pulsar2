// File: Pulsar.Runtime.Tests/Buffers/MockDateTimeProvider.cs

using Pulsar.Runtime.Buffers;
using System;

namespace Pulsar.Runtime.Tests.Buffers
{
    public class MockDateTimeProvider : IDateTimeProvider
    {
        public DateTime UtcNow { get; set; }

        public MockDateTimeProvider(DateTime initialTime)
        {
            UtcNow = initialTime;
        }

        public void Advance(TimeSpan timeSpan)
        {
            UtcNow = UtcNow.Add(timeSpan);
        }
    }
}
