// File: Pulsar.Tests/RuntimeTests/RuntimeConfigurationTests.cs

using System;
using System.IO;
using System.Text.Json;
using Serilog.Events;
using Xunit;
using Pulsar.Runtime;

namespace Pulsar.Tests.Runtime
{
    public class RuntimeConfigurationTests : IDisposable
    {
        private readonly string _testConfigPath;

        public RuntimeConfigurationTests()
        {
            _testConfigPath = Path.Combine(Path.GetTempPath(), "appsettings.json");
        }

        public void Dispose()
        {
            if (File.Exists(_testConfigPath))
            {
                File.Delete(_testConfigPath);
            }
        }

        [Fact]
        public void LoadConfiguration_WithNoArgs_UsesDefaults()
        {
            var config = Program.LoadConfiguration(Array.Empty<string>(), requireSensors: false);

            Assert.Equal("localhost:6379", config.RedisConnectionString);
            Assert.Equal(100, config.BufferCapacity);
            Assert.Equal(LogEventLevel.Information, config.LogLevel);
            Assert.Null(config.CycleTime);
            Assert.Empty(config.RequiredSensors);
        }

        [Fact]
        public void LoadConfiguration_WithCommandLineArgs_OverridesDefaults()
        {
            var args = new[]
            {
                "--redis", "redis.example.com:6379",
                "--cycle", "200",
                "--log-level", "Debug",
                "--capacity", "500"
            };

            var config = Program.LoadConfiguration(args, requireSensors: false);

            Assert.Equal("redis.example.com:6379", config.RedisConnectionString);
            Assert.Equal(500, config.BufferCapacity);
            Assert.Equal(LogEventLevel.Debug, config.LogLevel);
            Assert.Equal(TimeSpan.FromMilliseconds(200), config.CycleTime);
        }

        [Fact]
        public void LoadConfiguration_WithConfigFile_LoadsCorrectly()
        {
            var configJson = @"{
        ""RedisConnectionString"": ""redis.config.com:6379"",
        ""CycleTime"": ""00:00:00.150"",
        ""LogLevel"": ""Warning"",
        ""BufferCapacity"": 300,
        ""RequiredSensors"": [""temp1"", ""temp2""]
    }";

            File.WriteAllText(_testConfigPath, configJson);

            var config = Program.LoadConfiguration(Array.Empty<string>(), requireSensors: false, configPath: _testConfigPath);

            Assert.Equal("redis.config.com:6379", config.RedisConnectionString);
            Assert.Equal(300, config.BufferCapacity);
            Assert.Equal(LogEventLevel.Warning, config.LogLevel);
            Assert.Equal(TimeSpan.FromMilliseconds(150), config.CycleTime);
            Assert.Equal(2, config.RequiredSensors.Length);
        }

        [Fact]
        public void LoadConfiguration_CommandLineOverridesFile()
        {
            var configJson = @"{
        ""RedisConnectionString"": ""redis.config.com:6379"",
        ""BufferCapacity"": 300
    }";

            File.WriteAllText(_testConfigPath, configJson);

            var args = new[] { "--redis", "override.example.com:6379" };
            var config = Program.LoadConfiguration(args, requireSensors: false, configPath: _testConfigPath);

            Assert.Equal("override.example.com:6379", config.RedisConnectionString);
            Assert.Equal(300, config.BufferCapacity);  // Unchanged from file
        }

        [Fact]
        public void LoadConfiguration_MissingConfigFile_UsesDefaults()
        {
            var nonExistentPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString() + ".json");
            var config = Program.LoadConfiguration(Array.Empty<string>(), requireSensors: false, configPath: nonExistentPath);

            Assert.Equal("localhost:6379", config.RedisConnectionString);
            Assert.Equal(100, config.BufferCapacity);  // Default value
        }

        [Fact]
        public void LoadConfiguration_InvalidArgs_ThrowsException()
        {
            var args = new[] { "--cycle", "invalid" };

            var ex = Assert.Throws<ArgumentException>(() =>
                Program.LoadConfiguration(args));
            Assert.Contains("Cycle time", ex.Message);
        }
    }
}