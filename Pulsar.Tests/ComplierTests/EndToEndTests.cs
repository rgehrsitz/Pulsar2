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
            const string yamlContent =
                @"
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
                _output.WriteLine("\nStarting YAML parsing:");
                _output.WriteLine(yamlContent);

                // Show raw YAML being processed
                _output.WriteLine("\nProcessing YAML content:");
                _output.WriteLine(yamlContent);

                var parser = new DslParser();
                var rules = parser.ParseRules(yamlContent, validSensors);

                // Show parsed rule details
                _output.WriteLine("\nParsed Rule Details:");
                foreach (var rule in rules)
                {
                    _output.WriteLine($"Rule: {rule.Name}");
                    _output.WriteLine("Actions:");
                    foreach (var action in rule.Actions)
                    {
                        if (action is SetValueAction setValueAction)
                        {
                            _output.WriteLine($"  SetValueAction:");
                            _output.WriteLine($"    Key: {setValueAction.Key}");
                            _output.WriteLine($"    Value: {setValueAction.Value}");
                            _output.WriteLine(
                                $"    ValueExpression: {setValueAction.ValueExpression}"
                            );
                        }
                    }
                }

                _output.WriteLine("\nParsed rules:");
                foreach (var rule in rules)
                {
                    _output.WriteLine($"Rule: {rule.Name}");
                    foreach (var action in rule.Actions)
                    {
                        if (action is SetValueAction setValueAction)
                        {
                            _output.WriteLine($"  Action: SetValue");
                            _output.WriteLine($"    Key: {setValueAction.Key}");
                            _output.WriteLine($"    Value: {setValueAction.Value}");
                            _output.WriteLine($"    Expression: {setValueAction.ValueExpression}");
                        }
                    }
                }

                // Step 2: Generate C# code
                var csharpCode = CodeGenerator.GenerateCSharp(rules);
                _output.WriteLine("\nGenerated C# Code:");
                _output.WriteLine(csharpCode);

                // Step 3: Compile to DLL
                _output.WriteLine("\nGenerating and compiling C# code:");
                _output.WriteLine(csharpCode);
                RoslynCompiler.CompileSource(csharpCode, _outputPath);
                Assert.True(File.Exists(_outputPath), "DLL file should be created");

                // Step 4: Load and test the compiled rules
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

                // Create test inputs and outputs
                var inputs = new Dictionary<string, double> { ["temperature"] = 120 };
                var outputs = new Dictionary<string, double>();

                _output.WriteLine("\nExecuting rules with inputs:");
                foreach (var kvp in inputs)
                {
                    _output.WriteLine($"  {kvp.Key}: {kvp.Value}");
                }

                // Execute the compiled rules
                // Wrap the evaluation in a try-catch to see any runtime errors
                try
                {
                    evaluateMethod.Invoke(rulesInstance, new object[] { inputs, outputs });
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

                // Print outputs for debugging
                _output.WriteLine("\nRule execution outputs:");
                foreach (var kvp in outputs)
                {
                    _output.WriteLine($"  {kvp.Key}: {kvp.Value}");
                }

                // Verify results
                Assert.True(outputs.ContainsKey("alert"), "Output should contain 'alert' key");
                Assert.Equal(1, outputs["alert"]);

                // Test with temperature below threshold
                inputs["temperature"] = 80;
                outputs.Clear();
                evaluateMethod.Invoke(rulesInstance, new object[] { inputs, outputs });

                // Verify no alert for lower temperature
                Assert.False(
                    outputs.ContainsKey("alert"),
                    "No alert should be set for temperature below threshold"
                );
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

                evaluateMethod.Invoke(rulesInstance, new object[] { inputs, outputs });

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

                evaluateMethod.Invoke(rulesInstance, new object[] { inputs, outputs });

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
    }
}