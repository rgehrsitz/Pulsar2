using System;
using System.Collections.Generic;
using System.Diagnostics;
using Pulsar.Compiler.Analysis;
using Pulsar.Compiler.Models;
using Xunit;

namespace Pulsar.Tests.CompilerTests
{
    public class DependencyAnalyzerTests
    {
        private readonly DependencyAnalyzer _analyzer;

        public DependencyAnalyzerTests()
        {
            _analyzer = new DependencyAnalyzer();
        }

        [Fact]
        public void AnalyzeDependencies_EmptyRulesList_ReturnsEmptyList()
        {
            // Arrange
            var rules = new List<RuleDefinition>();

            // Act
            var result = _analyzer.AnalyzeDependencies(rules);

            // Assert
            Assert.Empty(result);
        }

        [Fact]
        public void AnalyzeDependencies_SingleRule_ReturnsSingleRule()
        {
            // Arrange
            var rule = CreateRule("Rule1", new[] { "temp" }, "output1");
            var rules = new List<RuleDefinition> { rule };

            // Act
            var result = _analyzer.AnalyzeDependencies(rules);

            // Assert
            Assert.Single(result);
            Assert.Equal("Rule1", result[0].Name);
        }

        [Fact]
        public void AnalyzeDependencies_SimpleDependencyChain_ReturnsCorrectOrder()
        {
            // Arrange
            var rule1 = CreateRule("Rule1", new[] { "temp" }, "intermediate");
            var rule2 = CreateRule("Rule2", new[] { "intermediate" }, "output");
            var rules = new List<RuleDefinition> { rule2, rule1 }; // Intentionally out of order

            // Debug: Print initial rules
            Debug.WriteLine("Initial rules:");
            foreach (var rule in rules)
            {
                Debug.WriteLine($"Rule: {rule.Name}");
                Debug.WriteLine(
                    "  Inputs: "
                        + string.Join(
                            ", ",
                            rule.Conditions.All.OfType<ComparisonCondition>().Select(c => c.Sensor)
                        )
                );
                Debug.WriteLine(
                    "  Outputs: "
                        + string.Join(
                            ", ",
                            rule.Actions.OfType<SetValueAction>().Select(a => a.Key)
                        )
                );
            }

            // Act
            var result = _analyzer.AnalyzeDependencies(rules);

            // Debug: Print result
            Debug.WriteLine("\nSorted rules:");
            foreach (var rule in result)
            {
                Debug.WriteLine($"Rule: {rule.Name}");
            }

            // Assert
            Assert.Equal(2, result.Count);
            Assert.Equal("Rule1", result[0].Name); // Should come first
            Assert.Equal("Rule2", result[1].Name); // Should be second
        }

        [Fact]
        public void AnalyzeDependencies_ComplexDependencies_ReturnsValidOrder()
        {
            // Arrange
            var rule1 = CreateRule("Rule1", new[] { "temp" }, "v1");
            var rule2 = CreateRule("Rule2", new[] { "pressure" }, "v2");
            var rule3 = CreateRule("Rule3", new[] { "v1", "v2" }, "output");
            var rules = new List<RuleDefinition> { rule3, rule1, rule2 };

            // Act
            var result = _analyzer.AnalyzeDependencies(rules);

            // Assert
            Assert.Equal(3, result.Count);
            Assert.True(IndexOf(result, "Rule3") > IndexOf(result, "Rule1"));
            Assert.True(IndexOf(result, "Rule3") > IndexOf(result, "Rule2"));
        }

        [Fact]
        public void AnalyzeDependencies_CyclicDependency_ThrowsException()
        {
            // Arrange
            var rule1 = CreateRule("Rule1", new[] { "v2" }, "v1");
            var rule2 = CreateRule("Rule2", new[] { "v1" }, "v2");
            var rules = new List<RuleDefinition> { rule1, rule2 };

            // Act & Assert
            var exception = Assert.Throws<InvalidOperationException>(
                () => _analyzer.AnalyzeDependencies(rules)
            );
            Assert.Contains("Cycle detected", exception.Message);
        }

        [Fact]
        public void AnalyzeDependencies_IndependentRules_PreservesRelativeOrder()
        {
            // Arrange
            var rule1 = CreateRule("Rule1", new[] { "temp1" }, "out1");
            var rule2 = CreateRule("Rule2", new[] { "temp2" }, "out2");
            var rules = new List<RuleDefinition> { rule1, rule2 };

            // Act
            var result = _analyzer.AnalyzeDependencies(rules);

            // Assert
            Assert.Equal(2, result.Count);
            Assert.Equal("Rule1", result[0].Name);
            Assert.Equal("Rule2", result[1].Name);
        }

        [Fact]
        public void AnalyzeDependencies_MultiLevelDependencies_ReturnsCorrectOrder()
        {
            // Arrange
            var rule1 = CreateRule("Rule1", new[] { "input" }, "v1");
            var rule2 = CreateRule("Rule2", new[] { "v1" }, "v2");
            var rule3 = CreateRule("Rule3", new[] { "v2" }, "v3");
            var rule4 = CreateRule("Rule4", new[] { "v3" }, "output");
            var rules = new List<RuleDefinition> { rule4, rule3, rule2, rule1 };

            // Act
            var result = _analyzer.AnalyzeDependencies(rules);

            // Assert
            Assert.Equal(4, result.Count);
            Assert.True(IndexOf(result, "Rule2") > IndexOf(result, "Rule1"));
            Assert.True(IndexOf(result, "Rule3") > IndexOf(result, "Rule2"));
            Assert.True(IndexOf(result, "Rule4") > IndexOf(result, "Rule3"));
        }

        // Helper methods
        private int IndexOf(List<RuleDefinition> rules, string ruleName)
        {
            return rules.FindIndex(r => r.Name == ruleName);
        }

        private RuleDefinition CreateRule(string name, string[] inputs, string output)
        {
            var conditions = new List<ConditionDefinition>();
            foreach (var input in inputs)
            {
                conditions.Add(
                    new ComparisonCondition
                    {
                        Type = ConditionType.Comparison,
                        Sensor = input,
                        Operator = ComparisonOperator.GreaterThan,
                        Value = 0,
                    }
                );
            }

            return new RuleDefinition
            {
                Name = name,
                Conditions = new ConditionGroup { All = conditions },
                Actions = new List<ActionDefinition>
                {
                    new SetValueAction
                    {
                        Type = ActionType.SetValue,
                        Key = output,
                        Value = 1,
                    },
                },
            };
        }
    }
}
