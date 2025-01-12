// File: Pulsar.Runtime/Services/RedisService.cs
using NRedisStack;
using NRedisStack.RedisStackCommands;
using StackExchange.Redis;
using System.Collections.Concurrent;
using Serilog;

namespace Pulsar.Runtime.Services;

public interface IRedisService
{
    Task<Dictionary<string, (double Value, DateTime Timestamp)>> GetSensorValuesAsync(IEnumerable<string> sensorKeys);
    Task SetOutputValuesAsync(Dictionary<string, double> outputs);
}

public class RedisService : IRedisService, IDisposable
{
    private readonly ConnectionMultiplexer _redis;
    private readonly IDatabase _db;
    private readonly ILogger _logger;
    private readonly SemaphoreSlim _connectionLock = new(1, 1);
    private readonly ConcurrentDictionary<string, DateTime> _lastErrorTime = new();
    private readonly TimeSpan _errorThrottleWindow = TimeSpan.FromSeconds(60);

    public RedisService(string connectionString, ILogger logger)
    {
        _logger = logger;

        try
        {
            var options = ConfigurationOptions.Parse(connectionString);
            options.AbortOnConnectFail = false; // More resilient default
            _redis = ConnectionMultiplexer.Connect(options);
            _db = _redis.GetDatabase();

            // Subscribe to connection events
            _redis.ConnectionFailed += (sender, e) =>
                _logger.Error("Redis connection failed: {@Error}", e.Exception);
            _redis.ConnectionRestored += (sender, e) =>
                _logger.Information("Redis connection restored");

            _logger.Information("Redis connection initialized");
        }
        catch (Exception ex)
        {
            _logger.Fatal(ex, "Failed to initialize Redis connection");
            throw;
        }
    }

    public async Task<Dictionary<string, (double Value, DateTime Timestamp)>> GetSensorValuesAsync(IEnumerable<string> sensorKeys)
    {
        var result = new Dictionary<string, (double Value, DateTime Timestamp)>();
        var keyArray = sensorKeys.ToArray();

        try
        {
            await _connectionLock.WaitAsync(); // Re-added connection lock

            var batch = _db.CreateBatch();
            var tasks = new List<Task<HashEntry[]>>();

            foreach (var key in keyArray)
            {
                tasks.Add(batch.HashGetAllAsync(key));
            }

            batch.Execute();
            await Task.WhenAll(tasks);

            for (int i = 0; i < keyArray.Length; i++)
            {
                var hashValues = tasks[i].Result;

                var valueEntry = hashValues.FirstOrDefault(he => he.Name == "value");
                var timestampEntry = hashValues.FirstOrDefault(he => he.Name == "timestamp");

                if (valueEntry.Value.HasValue && timestampEntry.Value.HasValue)
                {
                    if (double.TryParse(valueEntry.Value.ToString(), out double value) &&
                        long.TryParse(timestampEntry.Value.ToString(), out long ticksValue))
                    {
                        DateTime timestamp = new DateTime(ticksValue, DateTimeKind.Utc);
                        result[keyArray[i]] = (value, timestamp);
                    }
                    else
                    {
                        LogThrottledWarning($"Invalid value or timestamp format for sensor {keyArray[i]}");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error fetching sensor values from Redis");
            throw;
        }
        finally
        {
            _connectionLock.Release(); // Ensure lock is always released
        }

        return result;
    }

    public async Task SetOutputValuesAsync(Dictionary<string, double> outputs)
    {
        if (!outputs.Any()) return;

        try
        {
            await _connectionLock.WaitAsync();

            // Use Redis batch operation for better performance
            var batch = _db.CreateBatch();
            var tasks = new List<Task>();
            var timestamp = DateTime.UtcNow.Ticks;

            foreach (var kvp in outputs)
            {
                tasks.Add(batch.HashSetAsync(kvp.Key, new HashEntry[] {
                    new HashEntry("value", kvp.Value.ToString("G17")),
                    new HashEntry("timestamp", timestamp)
                }));
            }

            batch.Execute();
            await Task.WhenAll(tasks);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error writing output values to Redis");
            throw;
        }
        finally
        {
            _connectionLock.Release();
        }
    }

    private void LogThrottledWarning(string message)
    {
        if (_lastErrorTime.TryGetValue(message, out var lastTime))
        {
            if (DateTime.UtcNow - lastTime < _errorThrottleWindow)
            {
                return;
            }
        }

        _lastErrorTime.AddOrUpdate(message, DateTime.UtcNow, (_, _) => DateTime.UtcNow);
        _logger.Warning(message);
    }

    public void Dispose()
    {
        _connectionLock.Dispose();
        _redis?.Dispose();
    }
}
