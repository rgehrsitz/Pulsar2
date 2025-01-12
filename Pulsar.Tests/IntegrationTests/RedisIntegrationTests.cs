// File: Pulsar.Tests/IntegrationTests/RedisIntegrationTests.cs

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using StackExchange.Redis;
using Serilog;
using Xunit;
using Xunit.Abstractions;
using Pulsar.Runtime.Services;

namespace Pulsar.Tests.IntegrationTests
{
    public class RedisIntegrationTests : IDisposable
    {
        private readonly ConnectionMultiplexer _redis;
        private readonly IRedisService _redisService;
        private readonly ILogger _logger;
        private readonly ITestOutputHelper _output;
        private const string TestKeyPrefix = "pulsar_test_";

        public RedisIntegrationTests(ITestOutputHelper output)
        {
            _output = output;

            // Setup logger
            _logger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .WriteTo.TestOutput(output)
                .CreateLogger();

            try
            {
                // Connect to Redis
                _redis = ConnectionMultiplexer.Connect("localhost:6379");
                _redisService = new RedisService("localhost:6379", _logger);
            }
            catch (Exception ex)
            {
                _output.WriteLine($"Failed to connect to Redis: {ex.Message}");
                _output.WriteLine("Ensure Redis is running on localhost:6379");
                throw;
            }
        }

        [Fact]
        public async Task GetSetSensorValues_SingleValue_WorksCorrectly()
        {
            // Arrange
            var key = $"{TestKeyPrefix}temperature";
            var value = 98.6;

            // Act
            await _redisService.SetOutputValuesAsync(new Dictionary<string, double>
            {
                [key] = value
            });

            var result = await _redisService.GetSensorValuesAsync(new[] { key });

            // Assert
            Assert.Single(result);
            Assert.Equal(value, result[key].Item1);
            Assert.True((DateTime.UtcNow - result[key].Item2).TotalSeconds < 5);
        }

        [Fact]
        public async Task GetSetSensorValues_MultipleValues_WorksCorrectly()
        {
            // Arrange
            var testData = new Dictionary<string, double>
            {
                [$"{TestKeyPrefix}temp"] = 98.6,
                [$"{TestKeyPrefix}pressure"] = 1013.25,
                [$"{TestKeyPrefix}humidity"] = 65.4
            };

            // Act
            await _redisService.SetOutputValuesAsync(testData);
            var results = await _redisService.GetSensorValuesAsync(testData.Keys);

            // Assert
            Assert.Equal(testData.Count, results.Count);
            foreach (var kvp in testData)
            {
                Assert.True(results.ContainsKey(kvp.Key));
                Assert.Equal(kvp.Value, results[kvp.Key].Item1);
                Assert.True((DateTime.UtcNow - results[kvp.Key].Item2).TotalSeconds < 5);
            }
        }

        [Fact]
        public async Task GetSetSensorValues_BulkOperation_HandlesLargeDataSet()
        {
            // Arrange
            var testData = new Dictionary<string, double>();
            for (int i = 0; i < 1000; i++)
            {
                testData[$"{TestKeyPrefix}sensor_{i}"] = i * 1.1;
            }

            // Act
            var sw = System.Diagnostics.Stopwatch.StartNew();
            await _redisService.SetOutputValuesAsync(testData);
            var setTime = sw.ElapsedMilliseconds;

            sw.Restart();
            var results = await _redisService.GetSensorValuesAsync(testData.Keys);
            var getTime = sw.ElapsedMilliseconds;

            // Assert
            _output.WriteLine($"Set time for 1000 values: {setTime}ms");
            _output.WriteLine($"Get time for 1000 values: {getTime}ms");

            Assert.Equal(testData.Count, results.Count);
            foreach (var kvp in testData)
            {
                Assert.True(results.ContainsKey(kvp.Key));
                Assert.Equal(kvp.Value, results[kvp.Key].Item1);
                Assert.True((DateTime.UtcNow - results[kvp.Key].Item2).TotalSeconds < 5);
            }
        }

        [Fact]
        public async Task GetSensorValues_NonexistentKeys_ReturnsEmptyDictionary()
        {
            // Arrange
            var nonexistentKeys = new[]
            {
                $"{TestKeyPrefix}nonexistent1",
                $"{TestKeyPrefix}nonexistent2"
            };

            // Act
            var results = await _redisService.GetSensorValuesAsync(nonexistentKeys);

            // Assert
            Assert.Empty(results);
        }

        [Fact]
        public async Task SetOutputValues_EmptyDictionary_HandlesGracefully()
        {
            // Act & Assert
            await _redisService.SetOutputValuesAsync(new Dictionary<string, double>());
            // Should not throw any exception
        }

        [Fact]
        public async Task GetSetSensorValues_SpecialValues_HandlesCorrectly()
        {
            // Arrange
            var testData = new Dictionary<string, double>
            {
                [$"{TestKeyPrefix}zero"] = 0.0,
                [$"{TestKeyPrefix}negative"] = -273.15,
                [$"{TestKeyPrefix}very_small"] = 0.000001,
                [$"{TestKeyPrefix}very_large"] = 1000000.0
            };

            // Act
            await _redisService.SetOutputValuesAsync(testData);
            var results = await _redisService.GetSensorValuesAsync(testData.Keys);

            // Assert
            foreach (var kvp in testData)
            {
                Assert.True(results.ContainsKey(kvp.Key));
                Assert.Equal(kvp.Value, results[kvp.Key].Item1, 10); // precision to 10 decimal places
                Assert.True((DateTime.UtcNow - results[kvp.Key].Item2).TotalSeconds < 5);
            }
        }

        [Fact]
        public async Task Pipeline_SetThenGet_PreservesOrdering()
        {
            // Arrange
            var iterations = 10;
            var key = $"{TestKeyPrefix}pipeline_test";

            // Act & Assert
            for (int i = 0; i < iterations; i++)
            {
                // Set new value
                await _redisService.SetOutputValuesAsync(new Dictionary<string, double>
                {
                    [key] = i
                });

                // Immediately get it back
                var result = await _redisService.GetSensorValuesAsync(new[] { key });

                Assert.True(result.ContainsKey(key));
                Assert.Equal(i, result[key].Item1);
                Assert.True((DateTime.UtcNow - result[key].Item2).TotalSeconds < 5);
            }
        }

        public void Dispose()
        {
            try
            {
                // Cleanup all test keys
                var server = _redis.GetServer("localhost:6379");
                var testKeys = server.Keys(pattern: $"{TestKeyPrefix}*").ToArray();
                if (testKeys.Any())
                {
                    var db = _redis.GetDatabase();
                    db.KeyDelete(testKeys);
                }
            }
            catch (Exception ex)
            {
                _output.WriteLine($"Warning: Cleanup failed: {ex.Message}");
            }
            finally
            {
                _redis.Dispose();
                (_redisService as IDisposable)?.Dispose();
            }
        }
    }
}