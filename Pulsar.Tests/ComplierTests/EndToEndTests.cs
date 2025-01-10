// File: Pulsar.Tests/ComplierTests/EndToEndTests.cs

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using Pulsar.Compiler;
using Pulsar.Compiler.Generation;
using Pulsar.Compiler.Models;
using Pulsar.Compiler.Parsers;
using Pulsar.Runtime.Buffers;
using Pulsar.Compiler.Analysis;
using Xunit;
using Xunit.Abstractions;

namespace Pulsar.Tests.CompilerTests
{
    public class CompilerEndToEndTests : IDisposable
    {
        private readonly string _outputPath;
        private readonly ITestOutputHelper _output;

        public CompilerEndToEndTests(ITestOutputHelper output)
        {
            _output = output;
            // Create a unique filename for each test run
            _outputPath = Path.Combine(Path.GetTempPath(), $"CompiledRules_{Guid.NewGuid()}.dll");
        }

        public void Dispose()
        {
            try
            {
                if (File.Exists(_outputPath))
                {
                    // Try to clear any readonly attributes
                    File.SetAttributes(_outputPath, FileAttributes.Normal);
                    // Add a small delay to ensure file handle is released
                    Thread.Sleep(100);
                    File.Delete(_outputPath);
                }
            }
            catch (Exception ex)
            {
                _output.WriteLine(
                    $"Warning: Could not delete temporary file {_outputPath}: {ex.Message}"
                );
                // Don't rethrow - cleanup failure shouldn't fail the test
            }
        }

        [Fact]
        public void CompileRules_YamlToDll_SuccessfullyCompilesAndRuns()
        {
            // Arrange
            const string yamlContent = @"
rules:
  - name: 'TemperatureAlert'
    conditions:
      all:
        - condition:
            type: comparison
            sensor: 'temperature'
            operator: '>'
            value: 100
    actions:
      - set_value:
          key: 'alert'
          value: 1";

            var validSensors = new List<string> { "temperature", "alert" };

            try
            {
                // Step 1: Parse YAML to RuleDefinitions
                var parser = new DslParser();
                var rules = parser.ParseRules(yamlContent, validSensors);

                // Step 2: Generate C# code
                var csharpCode = CodeGenerator.GenerateCSharp(rules);
                _output.WriteLine("\nGenerated C# Code:");
                _output.WriteLine(csharpCode);

                // Step 3: Compile to DLL
                RoslynCompiler.CompileSource(csharpCode, _outputPath);
                Assert.True(File.Exists(_outputPath), "DLL file should be created");

                // Step 4: Load and test the compiled rules
                var assembly = Assembly.LoadFrom(_outputPath);
                var rulesType = assembly.GetType("CompiledRules")
                    ?? throw new InvalidOperationException("CompiledRules type not found in assembly");

                var rulesInstance = Activator.CreateInstance(rulesType)
                    ?? throw new InvalidOperationException("Failed to create CompiledRules instance");

                var evaluateMethod = rulesType.GetMethod("Evaluate")
                    ?? throw new InvalidOperationException("Evaluate method not found");

                // Create test inputs and outputs
                var inputs = new Dictionary<string, double> { ["temperature"] = 120 };
                var outputs = new Dictionary<string, double>();
                var bufferManager = new RingBufferManager();

                _output.WriteLine("\nExecuting rules with inputs:");
                foreach (var kvp in inputs)
                {
                    _output.WriteLine($"  {kvp.Key}: {kvp.Value}");
                }

                // Execute the compiled rules
                try
                {
                    evaluateMethod.Invoke(rulesInstance, new object[] { inputs, outputs, bufferManager });
                }
                catch (Exception ex)
                {
                    _output.WriteLine($"Rule execution failed: {ex}");
                    if (ex.InnerException != null)
                    {
                        _output.WriteLine($"Inner exception: {ex.InnerException}");
                    }
                    throw;
                }

                // Verify results
                Assert.True(outputs.ContainsKey("alert"));
                Assert.Equal(1, outputs["alert"]);

                // Test with temperature below threshold
                inputs["temperature"] = 80;
                outputs.Clear();
                evaluateMethod.Invoke(rulesInstance, new object[] { inputs, outputs, bufferManager });
                Assert.False(outputs.ContainsKey("alert"));
            }
            catch (Exception ex)
            {
                _output.WriteLine($"Test failed with exception: {ex}");
                throw;
            }
        }


        [Fact]
        public void CompileRules_ComplexExample_SuccessfullyCompilesAndRuns()
        {
            // Arrange
            const string yamlContent =
                @"
rules:
  - name: 'TemperatureConversion'
    conditions:
      all:
        - condition:
            type: expression
            expression: 'temperature_f >= 32'
    actions:
      - set_value:
          key: 'temperature_c'
          value_expression: '(temperature_f - 32) * 5/9'

  - name: 'HighTempAlert'
    conditions:
      all:
        - condition:
            type: comparison
            sensor: 'temperature_c'
            operator: '>'
            value: 37
    actions:
      - set_value:
          key: 'high_temp_alert'
          value: 1";

            var validSensors = new List<string>
    {
        "temperature_f",
        "temperature_c",
        "high_temp_alert",
    };

            var ringBufferManager = new RingBufferManager();

            try
            {
                // Parse and compile as before
                var parser = new DslParser();
                var rules = parser.ParseRules(yamlContent, validSensors);
                var csharpCode = CodeGenerator.GenerateCSharp(rules);
                RoslynCompiler.CompileSource(csharpCode, _outputPath);

                // Load and test
                var assembly = Assembly.LoadFrom(_outputPath);
                var rulesType =
                    assembly.GetType("CompiledRules")
                    ?? throw new InvalidOperationException(
                        "CompiledRules type not found in assembly"
                    );

                var rulesInstance =
                    Activator.CreateInstance(rulesType)
                    ?? throw new InvalidOperationException(
                        "Failed to create CompiledRules instance"
                    );

                var evaluateMethod =
                    rulesType.GetMethod("Evaluate")
                    ?? throw new InvalidOperationException("Evaluate method not found");

                // Test case 1: 100°F should trigger alert
                var inputs = new Dictionary<string, double> { ["temperature_f"] = 100 };
                var outputs = new Dictionary<string, double>();

                evaluateMethod.Invoke(rulesInstance, new object[] { inputs, outputs, ringBufferManager });

                Assert.True(outputs.ContainsKey("temperature_c"), "Should convert to Celsius");
                Assert.True(
                    outputs.ContainsKey("high_temp_alert"),
                    "Should trigger high temp alert"
                );
                Assert.Equal(37.777777777777779, outputs["temperature_c"], 8);
                Assert.Equal(1, outputs["high_temp_alert"]);

                // Test case 2: 50°F should not trigger alert
                inputs["temperature_f"] = 50;
                outputs.Clear();

                evaluateMethod.Invoke(rulesInstance, new object[] { inputs, outputs, ringBufferManager });

                Assert.True(outputs.ContainsKey("temperature_c"), "Should convert to Celsius");
                Assert.False(outputs.ContainsKey("high_temp_alert"), "Should not trigger alert");
                Assert.Equal(10, outputs["temperature_c"], 8);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Test failed with exception: {ex}");
                throw;
            }
        }


        [Fact]
        public void CompileTemporalRule_GeneratesAndExecutesCorrectly()
        {
            // Arrange
            const string yamlContent = @"
rules:
  - name: 'TemperatureAlert'
    description: 'Alerts when temperature stays high for specified duration'
    conditions:
      all:
        - condition:
            type: threshold_over_time
            sensor: 'temperature'
            threshold: 100
            duration: 500  # 500ms
    actions:
      - set_value:
          key: 'temp_alert'
          value: 1
      - send_message:
          channel: 'alerts'
          message: 'Temperature exceeded threshold for specified duration'
";
            var validSensors = new List<string> { "temperature", "temp_alert", "alerts" };

            try
            {
                // Act - Full Pipeline
                var parser = new DslParser();
                var rules = parser.ParseRules(yamlContent, validSensors);
                _output.WriteLine("Rules parsed successfully");

                var analyzer = new DependencyAnalyzer();
                var sortedRules = analyzer.AnalyzeDependencies(rules);
                _output.WriteLine("Dependencies analyzed");

                var code = CodeGenerator.GenerateCSharp(sortedRules);
                _output.WriteLine("\nGenerated C# Code:");
                _output.WriteLine(code);

                RoslynCompiler.CompileSource(code, _outputPath);
                _output.WriteLine("Code compiled successfully");

                // Test the compiled rules
                var assembly = Assembly.LoadFrom(_outputPath);
                var rulesType = assembly.GetType("CompiledRules");
                Assert.NotNull(rulesType);

                var instance = Activator.CreateInstance(rulesType);
                Assert.NotNull(instance);

                var inputs = new Dictionary<string, double>();
                var outputs = new Dictionary<string, double>();
                var bufferManager = new RingBufferManager();

                // Test Case 1: Temperature just breaching threshold
                inputs["temperature"] = 101;
                ((dynamic)instance).Evaluate(inputs, outputs, bufferManager);
                Assert.False(outputs.ContainsKey("temp_alert"), "Alert shouldn't trigger immediately");

                // Test Case 2: Temperature maintained above threshold
                for (int i = 0; i < 6; i++)
                {
                    bufferManager.UpdateBuffers(inputs);
                    Thread.Sleep(100); // Total 600ms > 500ms threshold
                }

                outputs.Clear();
                ((dynamic)instance).Evaluate(inputs, outputs, bufferManager);
                Assert.True(outputs.ContainsKey("temp_alert"), "Alert should trigger after duration");
                Assert.Equal(1, outputs["temp_alert"]);
            }
            catch (Exception ex)
            {
                _output.WriteLine($"Test failed with exception: {ex}");
                throw;
            }
        }

        [Fact]
        public void CompileComplexDependencyChain_ExecutesInCorrectOrder()
        {
            // Arrange
            const string yamlContent = @"
rules:
  - name: 'RawTempConversion'
    description: 'Converts raw temperature to Celsius'
    conditions:
      all:
        - condition:
            type: comparison
            sensor: 'raw_temp'
            operator: '>'
            value: -273.15  # Absolute zero check
    actions:
      - set_value:
          key: 'temp_c'
          value_expression: 'raw_temp * 0.1'  # Assuming raw is in decicelsius

  - name: 'TempRateCalculation'
    description: 'Calculates rate of temperature change'
    conditions:
      all:
        - condition:
            type: comparison
            sensor: 'temp_c'
            operator: '!='
            value: 0
    actions:
      - set_value:
          key: 'temp_rate'
          value_expression: '(temp_c - previous_temp_c) / 0.1'  # Rate per second

  - name: 'HighTempAlert'
    description: 'Alerts on high temperature and rate'
    conditions:
      all:
        - condition:
            type: threshold_over_time
            sensor: 'temp_c'
            threshold: 50
            duration: 300
        - condition:
            type: comparison
            sensor: 'temp_rate'
            operator: '>'
            value: 5
    actions:
      - set_value:
          key: 'temp_alert'
          value: 1
";
            var validSensors = new List<string>
    {
        "raw_temp", "temp_c", "previous_temp_c",
        "temp_rate", "temp_alert"
    };

            try
            {
                // Act - Compile Pipeline
                var parser = new DslParser();
                var rules = parser.ParseRules(yamlContent, validSensors);

                var analyzer = new DependencyAnalyzer();
                var sortedRules = analyzer.AnalyzeDependencies(rules);

                // Verify rule ordering
                var ruleOrder = sortedRules.Select(r => r.Name).ToList();
                _output.WriteLine("Rule execution order:");
                foreach (var rule in ruleOrder)
                {
                    _output.WriteLine($"  {rule}");
                }

                // RawTempConversion should be before TempRateCalculation
                Assert.True(
                    ruleOrder.IndexOf("RawTempConversion") < ruleOrder.IndexOf("TempRateCalculation"),
                    "Temperature conversion must happen before rate calculation"
                );

                // TempRateCalculation should be before HighTempAlert
                Assert.True(
                    ruleOrder.IndexOf("TempRateCalculation") < ruleOrder.IndexOf("HighTempAlert"),
                    "Rate calculation must happen before alert check"
                );

                var code = CodeGenerator.GenerateCSharp(sortedRules);
                _output.WriteLine("\nGenerated Code:");
                _output.WriteLine(code);

                RoslynCompiler.CompileSource(code, _outputPath);

                // Test execution
                var assembly = Assembly.LoadFrom(_outputPath);
                var rulesType = assembly.GetType("CompiledRules");
                var instance = Activator.CreateInstance(rulesType);

                var inputs = new Dictionary<string, double>();
                var outputs = new Dictionary<string, double>();
                var bufferManager = new RingBufferManager();

                // Simulate temperature rise
                double rawTemp = 250; // 25.0°C
                inputs["raw_temp"] = rawTemp;
                inputs["previous_temp_c"] = 20.0; // Previous temperature

                // First evaluation
                ((dynamic)instance).Evaluate(inputs, outputs, bufferManager);
                Assert.True(outputs.ContainsKey("temp_c"), "Should compute Celsius temperature");
                Assert.True(outputs.ContainsKey("temp_rate"), "Should compute rate");
                Assert.False(outputs.ContainsKey("temp_alert"), "Shouldn't alert yet");

                // Simulate rapid temperature rise
                for (int i = 0; i < 4; i++)
                {
                    rawTemp += 100; // +10°C per iteration
                    inputs["raw_temp"] = rawTemp;
                    inputs["previous_temp_c"] = outputs["temp_c"]; // Use previous output
                    bufferManager.UpdateBuffers(outputs);

                    outputs.Clear();
                    Thread.Sleep(100);
                    ((dynamic)instance).Evaluate(inputs, outputs, bufferManager);
                }

                Assert.True(outputs.ContainsKey("temp_alert"), "Should trigger alert on rapid rise");
            }
            catch (Exception ex)
            {
                _output.WriteLine($"Test failed with exception: {ex}");
                throw;
            }
        }

    }
}
