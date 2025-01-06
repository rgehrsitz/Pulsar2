// File: Pulsar.Tests/DslParserTests.cs

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Pulsar.Compiler.Models;
using Pulsar.Compiler.Parsers;
using Xunit;

namespace Pulsar.Tests.CompilerTests
{
    public class DslParserTests
    {
        private readonly DslParser _parser;

        public DslParserTests()
        {
            _parser = new DslParser();
        }

        [Fact]
        public void ParseRules_ShouldParseValidYaml()
        {
            // Arrange
            string yamlContent =
                @"
rules:
  - name: 'SampleRule'
    conditions:
      all:
        - condition:
            type: comparison
            sensor: 'temperature_f'
            operator: '>'
            value: 100
    actions:
      - set_value:
          key: 'alert_temp'
          value_expression: 'temperature_f * 1.8 + 32'
";
            var validSensors = new List<string> { "temperature_f", "alert_temp" };

            // Act
            var rules = _parser.ParseRules(yamlContent, validSensors);

            // Assert
            Assert.Single(rules);
            Assert.Equal("SampleRule", rules[0].Name);

            var conditions = rules[0].Conditions;
            Assert.Single(conditions.All);
            Assert.IsType<ComparisonCondition>(conditions.All[0]);

            var actions = rules[0].Actions;
            Assert.Single(actions);
            Assert.IsType<SetValueAction>(actions[0]);
        }

        [Fact]
        public void ParseRules_ShouldThrowExceptionForInvalidSensor()
        {
            // Arrange
            string yamlContent =
                @"
rules:
  - name: 'InvalidSensorRule'
    conditions:
      all:
        - condition:
            type: comparison
            sensor: 'invalid_sensor'
            operator: '>'
            value: 100
";
            var validSensors = new List<string> { "temperature_f", "alert_temp" };

            // Act & Assert
            var exception = Assert.Throws<InvalidOperationException>(
                () => _parser.ParseRules(yamlContent, validSensors)
            );

            Assert.Contains("invalid_sensor", exception.Message);
        }

        [Fact]
        public void ParseRules_ShouldThrowExceptionForUnsupportedConditionType()
        {
            // Arrange
            string yamlContent =
                @"
rules:
  - name: 'UnsupportedConditionRule'
    conditions:
      all:
        - condition:
            type: unsupported_type
            sensor: 'temperature_f'
            operator: '>'
            value: 100
";
            var validSensors = new List<string> { "temperature_f" };

            // Act & Assert
            var exception = Assert.Throws<NotImplementedException>(
                () => _parser.ParseRules(yamlContent, validSensors)
            );

            Assert.Contains("unsupported_type", exception.Message);
        }

        [Fact]
        public void ParseRules_ShouldValidateComplexRules()
        {
            // Arrange
            string yamlContent = File.ReadAllText("TestData/rules.yaml");
            var validSensors = new List<string>
            {
                "temperature_f",
                "temperature_c",
                "humidity",
                "pressure",
                "alerts",
                "alert_channel",
                "converted_temp",
            };

            // Act
            var rules = _parser.ParseRules(yamlContent, validSensors);

            // Assert
            Debug.WriteLine("\n=== Test Assertions ===");
            Debug.WriteLine($"Rules count: {rules.Count}");

            var rule1 = rules[0];
            Debug.WriteLine($"First rule name: {rule1.Name}");
            Debug.WriteLine($"First rule conditions count: {rule1.Conditions.All.Count}");
            Debug.WriteLine($"First rule actions count: {rule1.Actions.Count}");
            Debug.WriteLine($"First action type: {rule1.Actions[0].GetType().Name}");
            Debug.WriteLine($"Second action type: {rule1.Actions[1].GetType().Name}");

            Assert.Equal(3, rules.Count);
            Assert.Equal("TemperatureConversion", rule1.Name);
            Assert.Equal(2, rule1.Conditions.All.Count);
            Assert.Equal(2, rule1.Actions.Count);
            Assert.IsType<SetValueAction>(rule1.Actions[0]);
            Assert.IsType<SendMessageAction>(rule1.Actions[1]);
        }

        [Fact]
        public void ParseRules_ShouldHandleEmptyConditions()
        {
            // Arrange
            string yamlContent =
                @"
rules:
  - name: 'NoConditionRule'
    conditions:
      all: []
    actions:
      - set_value:
          key: 'temp_key'
          value_expression: 'temperature_f * 1.8 + 32'
";
            var validSensors = new List<string> { "temperature_f", "temp_key" };

            // Act
            var rules = _parser.ParseRules(yamlContent, validSensors);

            // Assert
            Assert.Single(rules);
            Assert.Empty(rules[0].Conditions.All);
            Assert.Single(rules[0].Actions);
        }

        [Fact]
        public void ParseRules_ShouldHandleEmptyActions()
        {
            // Arrange
            string yamlContent =
                @"
rules:
  - name: 'NoActionsRule'
    conditions:
      all:
        - condition:
            type: comparison
            sensor: 'temperature_f'
            operator: '>'
            value: 100
    actions: []
";
            var validSensors = new List<string> { "temperature_f" };

            // Act
            var rules = _parser.ParseRules(yamlContent, validSensors);

            // Assert
            Assert.Single(rules);
            Assert.Empty(rules[0].Actions);
        }

        [Fact]
        public void ParseRules_ShouldHandleMissingOptionalFields()
        {
            // Arrange
            string yamlContent =
                @"
rules:
  - name: 'MissingFieldsRule'
    actions:
      - set_value:
          key: 'temp_key'
          value_expression: 'temperature_f * 1.8 + 32'
";
            var validSensors = new List<string> { "temperature_f", "temp_key" };

            // Act
            var rules = _parser.ParseRules(yamlContent, validSensors);

            // Assert
            Assert.Single(rules);
            Assert.NotNull(rules[0].Conditions);
            Assert.Empty(rules[0].Conditions.All);
            Assert.Empty(rules[0].Conditions.Any);
        }
    }
}
