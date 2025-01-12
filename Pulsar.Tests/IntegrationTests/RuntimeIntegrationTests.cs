// File: Pulsar.Tests/IntegrationTests/RuntimeIntegrationTests.cs

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Diagnostics;
using Serilog;
using Xunit;
using Xunit.Abstractions;
using Pulsar.Runtime;
using Pulsar.Runtime.Services;
using Pulsar.Compiler;
using Pulsar.Compiler.Parsers;
using Pulsar.Compiler.Analysis;
using Pulsar.Compiler.Generation;
using StackExchange.Redis;

namespace Pulsar.Tests.IntegrationTests
{
    public class RuntimeIntegrationTests : IDisposable
    {
        private readonly IRedisService _redis;
        private readonly ILogger _logger;
        private readonly string _testDllPath;
        private readonly RuntimeOrchestrator _orchestrator;
        private readonly ITestOutputHelper _output;
        private const string TestKeyPrefix = "pulsar_test_";

        public RuntimeIntegrationTests(ITestOutputHelper output)
        {
            _output = output;
            _logger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .WriteTo.TestOutput(output)
                .CreateLogger();

            _redis = new RedisService("localhost:6379", _logger);
            _testDllPath = Path.Combine(Path.GetTempPath(), $"TestRules_{Guid.NewGuid()}.dll");

            var requiredSensors = new[]
            {
                $"{TestKeyPrefix}temperature",
                $"{TestKeyPrefix}pressure",
                $"{TestKeyPrefix}humidity"
            };

            _orchestrator = new RuntimeOrchestrator(
                _redis,
                _logger,
                requiredSensors,
                TimeSpan.FromMilliseconds(100));
        }

        [Fact]
        public async Task SimpleRule_ExecutesCorrectly()
        {
            // Arrange
            const string yamlContent = @"
rules:
  - name: 'SimpleTemperatureAlert'
    conditions:
      all:
        - condition:
            type: comparison
            sensor: 'pulsar_test_temperature'
            operator: '>'
            value: 30
    actions:
      - set_value:
          key: 'pulsar_test_temp_alert'
          value: 1
";
            var validSensors = new List<string>
            {
                $"{TestKeyPrefix}temperature",
                $"{TestKeyPrefix}temp_alert"
            };

            // Compile the rules
            CompileRules(yamlContent, validSensors);

            try
            {
                // Load rules and start orchestrator
                _orchestrator.LoadRules(_testDllPath);
                await _orchestrator.StartAsync();

                // Set initial temperature
                await _redis.SetOutputValuesAsync(new Dictionary<string, double>
                {
                    [$"{TestKeyPrefix}temperature"] = 25.0 // Below threshold
                });

                // Let it run one cycle
                await Task.Delay(150);

                // Verify no alert
                var results = await _redis.GetSensorValuesAsync(
                    new[] { $"{TestKeyPrefix}temp_alert" });
                Assert.False(results.ContainsKey($"{TestKeyPrefix}temp_alert"));

                // Set temperature above threshold
                await _redis.SetOutputValuesAsync(new Dictionary<string, double>
                {
                    [$"{TestKeyPrefix}temperature"] = 35.0
                });

                // Let it run one cycle
                await Task.Delay(150);

                // Verify alert was set
                results = await _redis.GetSensorValuesAsync(
                    new[] { $"{TestKeyPrefix}temp_alert" });
                Assert.True(results.ContainsKey($"{TestKeyPrefix}temp_alert"));
                Assert.Equal(1, results[$"{TestKeyPrefix}temp_alert"].Item1);
            }
            finally
            {
                await _orchestrator.StopAsync();
            }
        }

        [Fact]
        public async Task TemporalRule_ExecutesCorrectly()
        {
            // Arrange
            const string yamlContent = @"
rules:
  - name: 'SustainedTemperatureAlert'
    conditions:
      all:
        - condition:
            type: threshold_over_time
            sensor: 'pulsar_test_temperature'
            threshold: 30
            duration: 300
    actions:
      - set_value:
          key: 'pulsar_test_sustained_alert'
          value: 1
";
            var validSensors = new List<string>
            {
                $"{TestKeyPrefix}temperature",
                $"{TestKeyPrefix}sustained_alert"
            };

            CompileRules(yamlContent, validSensors);

            try
            {
                _orchestrator.LoadRules(_testDllPath);
                await _orchestrator.StartAsync();

                // Set high temperature
                await _redis.SetOutputValuesAsync(new Dictionary<string, double>
                {
                    [$"{TestKeyPrefix}temperature"] = 35.0
                });

                // Wait less than duration - should not trigger
                await Task.Delay(200);
                var results = await _redis.GetSensorValuesAsync(
                    new[] { $"{TestKeyPrefix}sustained_alert" });
                Assert.False(results.ContainsKey($"{TestKeyPrefix}sustained_alert"));

                // Wait remainder of duration plus margin - should trigger
                await Task.Delay(200);
                results = await _redis.GetSensorValuesAsync(
                    new[] { $"{TestKeyPrefix}sustained_alert" });
                Assert.True(results.ContainsKey($"{TestKeyPrefix}sustained_alert"));
                Assert.Equal(1, results[$"{TestKeyPrefix}sustained_alert"].Item1);
            }
            finally
            {
                await _orchestrator.StopAsync();
            }
        }

        [Fact]
        public async Task ComplexRuleChain_ExecutesCorrectly()
        {
            const string yamlContent = @"
rules:
  - name: 'TemperatureConversion'
    conditions:
      all:
        - condition:
            type: comparison
            sensor: 'pulsar_test_temperature'
            operator: '>'
            value: -273.15
    actions:
      - set_value:
          key: 'pulsar_test_temp_c'
          value_expression: '(pulsar_test_temperature - 32) * 5/9'

  - name: 'HumidityCheck'
    conditions:
      all:
        - condition:
            type: comparison
            sensor: 'pulsar_test_humidity'
            operator: '>'
            value: 60
    actions:
      - set_value:
          key: 'pulsar_test_humid_alert'
          value: 1

  - name: 'CombinedAlert'
    conditions:
      all:
        - condition:
            type: comparison
            sensor: 'pulsar_test_temp_c'
            operator: '>'
            value: 25
        - condition:
            type: comparison
            sensor: 'pulsar_test_humid_alert'
            operator: '=='
            value: 1
    actions:
      - set_value:
          key: 'pulsar_test_comfort_alert'
          value: 1
";
            var validSensors = new List<string>
            {
                $"{TestKeyPrefix}temperature",
                $"{TestKeyPrefix}humidity",
                $"{TestKeyPrefix}temp_c",
                $"{TestKeyPrefix}humid_alert",
                $"{TestKeyPrefix}comfort_alert"
            };

            CompileRules(yamlContent, validSensors);

            try
            {
                _orchestrator.LoadRules(_testDllPath);
                await _orchestrator.StartAsync();

                // Set initial conditions
                await _redis.SetOutputValuesAsync(new Dictionary<string, double>
                {
                    [$"{TestKeyPrefix}temperature"] = 86.0,  // 30°C
                    [$"{TestKeyPrefix}humidity"] = 65.0
                });

                // Wait for multiple cycles to ensure all rules execute
                await Task.Delay(300);

                // Verify all computed values
                var results = await _redis.GetSensorValuesAsync(new[]
                {
                    $"{TestKeyPrefix}temp_c",
                    $"{TestKeyPrefix}humid_alert",
                    $"{TestKeyPrefix}comfort_alert"
                });

                Assert.True(results.ContainsKey($"{TestKeyPrefix}temp_c"));
                Assert.True(results.ContainsKey($"{TestKeyPrefix}humid_alert"));
                Assert.True(results.ContainsKey($"{TestKeyPrefix}comfort_alert"));

                // Verify values
                Assert.Equal(30.0, results[$"{TestKeyPrefix}temp_c"].Item1, 1); // 1 decimal precision
                Assert.Equal(1, results[$"{TestKeyPrefix}humid_alert"].Item1);
                Assert.Equal(1, results[$"{TestKeyPrefix}comfort_alert"].Item1);
            }
            finally
            {
                await _orchestrator.StopAsync();
            }
        }

        [Fact]
        public async Task RuntimePerformance_HandlesHighThroughput()
        {
            // Create a rule that processes multiple inputs
            var yamlBuilder = new System.Text.StringBuilder();
            yamlBuilder.AppendLine("rules:");

            // Create 100 rules that each process a different sensor
            for (int i = 0; i < 100; i++)
            {
                yamlBuilder.AppendLine($@"
  - name: 'Rule{i}'
    conditions:
      all:
        - condition:
            type: comparison
            sensor: 'pulsar_test_sensor_{i}'
            operator: '>'
            value: 50
    actions:
      - set_value:
          key: 'pulsar_test_output_{i}'
          value_expression: 'pulsar_test_sensor_{i} * 2'");
            }

            var validSensors = new List<string>();
            for (int i = 0; i < 100; i++)
            {
                validSensors.Add($"{TestKeyPrefix}sensor_{i}");
                validSensors.Add($"{TestKeyPrefix}output_{i}");
            }

            CompileRules(yamlBuilder.ToString(), validSensors);

            try
            {
                _orchestrator.LoadRules(_testDllPath);
                await _orchestrator.StartAsync();

                // Set all sensor values
                var inputs = new Dictionary<string, double>();
                for (int i = 0; i < 100; i++)
                {
                    inputs[$"{TestKeyPrefix}sensor_{i}"] = 75.0;
                }
                await _redis.SetOutputValuesAsync(inputs);

                var sw = Stopwatch.StartNew();
                await Task.Delay(500); // Let it run for multiple cycles
                sw.Stop();

                // Verify outputs
                var outputKeys = validSensors.Where(s => s.Contains("output"));
                var results = await _redis.GetSensorValuesAsync(outputKeys);

                Assert.Equal(100, results.Count);
                Assert.All(results.Values, value => Assert.Equal(150.0, value.Item1));

                _output.WriteLine($"Processed 100 rules over {sw.ElapsedMilliseconds}ms");
            }
            finally
            {
                await _orchestrator.StopAsync();
            }
        }

        private void CompileRules(string yamlContent, List<string> validSensors)
        {
            var parser = new DslParser();
            var rules = parser.ParseRules(yamlContent, validSensors);

            var analyzer = new DependencyAnalyzer();
            var sortedRules = analyzer.AnalyzeDependencies(rules);

            var code = CodeGenerator.GenerateCSharp(sortedRules);
            RoslynCompiler.CompileSource(code, _testDllPath);
        }

        public void Dispose()
        {
            try
            {
                // Cleanup test keys from Redis
                var redis = ConnectionMultiplexer.Connect("localhost:6379");
                var db = redis.GetDatabase();
                var server = redis.GetServer("localhost:6379");
                var testKeys = server.Keys(pattern: $"{TestKeyPrefix}*").ToArray();
                if (testKeys.Any())
                {
                    db.KeyDelete(testKeys);
                }
                redis.Dispose();

                // Cleanup test DLL
                if (File.Exists(_testDllPath))
                {
                    File.Delete(_testDllPath);
                }

                _orchestrator.Dispose();
            }
            catch (Exception ex)
            {
                _output.WriteLine($"Warning: Cleanup failed: {ex.Message}");
            }
        }
    }
}