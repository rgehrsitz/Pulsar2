using System;
using System.Collections.Generic;
using System.Linq;
using Pulsar.Compiler.Generation;
using Pulsar.Compiler.Models;
using Xunit;

namespace Pulsar.Tests.CompilerTests
{
    public class CodeGeneratorTests
    {
        [Fact]
        public void GenerateCSharp_SingleRule_GeneratesValidCode()
        {
            // Arrange
            var rule = new RuleDefinition
            {
                Name = "SimpleRule",
                Conditions = new ConditionGroup
                {
                    All = new List<ConditionDefinition>
                    {
                        new ComparisonCondition
                        {
                            Type = ConditionType.Comparison,
                            Sensor = "temperature",
                            Operator = ComparisonOperator.GreaterThan,
                            Value = 100,
                        },
                    },
                },
                Actions = new List<ActionDefinition>
                {
                    new SetValueAction
                    {
                        Type = ActionType.SetValue,
                        Key = "alert",
                        Value = 1,
                    },
                },
            };

            // Act
            var code = CodeGenerator.GenerateCSharp(new List<RuleDefinition> { rule });

            // Assert
            Assert.Contains("public class CompiledRules", code);
            Assert.Contains("EvaluateLayer0", code);
            Assert.Contains("inputs[\"temperature\"] > 100", code);
            Assert.Contains("outputs[\"alert\"] = 1", code);
        }

        [Fact]
        public void GenerateCSharp_MultipleRules_GeneratesLayeredCode()
        {
            // Arrange
            var rule1 = new RuleDefinition
            {
                Name = "Rule1",
                Conditions = new ConditionGroup
                {
                    All = new List<ConditionDefinition>
                    {
                        new ComparisonCondition
                        {
                            Type = ConditionType.Comparison,
                            Sensor = "temp1",
                            Operator = ComparisonOperator.GreaterThan,
                            Value = 50,
                        },
                    },
                },
                Actions = new List<ActionDefinition>
                {
                    new SetValueAction { Key = "intermediate", Value = 1 },
                },
            };

            var rule2 = new RuleDefinition
            {
                Name = "Rule2",
                Conditions = new ConditionGroup
                {
                    All = new List<ConditionDefinition>
                    {
                        new ComparisonCondition
                        {
                            Type = ConditionType.Comparison,
                            Sensor = "intermediate",
                            Operator = ComparisonOperator.EqualTo,
                            Value = 1,
                        },
                    },
                },
                Actions = new List<ActionDefinition>
                {
                    new SetValueAction { Key = "output", Value = 2 },
                },
            };

            // Act
            var code = CodeGenerator.GenerateCSharp(new List<RuleDefinition> { rule1, rule2 });

            // Assert
            Assert.Contains("EvaluateLayer0", code);
            Assert.Contains("EvaluateLayer1", code);
            Assert.Contains("inputs[\"temp1\"] > 50", code);
            Assert.Contains("inputs[\"intermediate\"] == 1", code);
        }

        [Fact]
        public void GenerateCSharp_ExpressionCondition_GeneratesValidCode()
        {
            // Arrange
            var rule = new RuleDefinition
            {
                Name = "ExpressionRule",
                Conditions = new ConditionGroup
                {
                    All = new List<ConditionDefinition>
                    {
                        new ExpressionCondition
                        {
                            Type = ConditionType.Expression,
                            Expression = "temperature * 1.8 + 32 > 100",
                        },
                    },
                },
                Actions = new List<ActionDefinition>
                {
                    new SetValueAction
                    {
                        Key = "fahrenheit",
                        ValueExpression = "temperature * 1.8 + 32",
                    },
                },
            };

            // Act
            var code = CodeGenerator.GenerateCSharp(new List<RuleDefinition> { rule });

            // Assert
            Assert.Contains("(temperature * 1.8 + 32 > 100)", code);
            Assert.Contains("outputs[\"fahrenheit\"] = temperature * 1.8 + 32", code);
        }

        [Fact]
        public void GenerateCSharp_ComplexExpressionCondition_GeneratesValidCode()
        {
            // Arrange
            var rule = new RuleDefinition
            {
                Name = "ComplexExpressionRule",
                Conditions = new ConditionGroup
                {
                    All = new List<ConditionDefinition>
                    {
                        new ExpressionCondition
                        {
                            Type = ConditionType.Expression,
                            // Complex expression - requires parentheses due to function call and arithmetic
                            Expression = "Math.Abs(temperature - setpoint) > threshold",
                        },
                        new ExpressionCondition
                        {
                            Type = ConditionType.Expression,
                            // Simple expression - doesn't require parentheses
                            Expression = "rate_of_change > 5",
                        },
                    },
                },
                Actions = new List<ActionDefinition>
                {
                    new SetValueAction { Key = "alert", Value = 1 },
                },
            };

            // Act
            var code = CodeGenerator.GenerateCSharp(new List<RuleDefinition> { rule });

            // Assert
            // Test complex expression - should be parenthesized due to function call and arithmetic
            Assert.Contains("(Math.Abs(temperature - setpoint) > threshold)", code);

            // Test simple expression - parentheses not required as it's a simple comparison
            // Accept either form since they're logically equivalent
            bool hasSimpleExpression =
                code.Contains("rate_of_change > 5") || code.Contains("(rate_of_change > 5)");
            Assert.True(
                hasSimpleExpression,
                "Code should contain simple expression either with or without parentheses"
            );

            // Verify overall structure
            Assert.Contains("public class CompiledRules", code);
            Assert.Contains("outputs[\"alert\"] = 1", code);
        }

        [Fact]
        public void GenerateCSharp_EmptyRulesList_GeneratesMinimalValidCode()
        {
            // Arrange
            var rules = new List<RuleDefinition>();

            // Act
            var code = CodeGenerator.GenerateCSharp(rules);

            // Assert
            Assert.Contains("public class CompiledRules", code);
            Assert.DoesNotContain("EvaluateLayer", code);
        }

        [Fact]
        public void GenerateCSharp_AnyConditions_GeneratesValidCode()
        {
            // Arrange
            var rule = new RuleDefinition
            {
                Name = "AnyConditionRule",
                Conditions = new ConditionGroup
                {
                    Any = new List<ConditionDefinition>
                    {
                        new ComparisonCondition
                        {
                            Type = ConditionType.Comparison,
                            Sensor = "temp1",
                            Operator = ComparisonOperator.GreaterThan,
                            Value = 100,
                        },
                        new ComparisonCondition
                        {
                            Type = ConditionType.Comparison,
                            Sensor = "temp2",
                            Operator = ComparisonOperator.LessThan,
                            Value = 0,
                        },
                    },
                },
                Actions = new List<ActionDefinition>
                {
                    new SetValueAction { Key = "alert", Value = 1 },
                },
            };

            // Act
            var code = CodeGenerator.GenerateCSharp(new List<RuleDefinition> { rule });

            // Assert
            Assert.Contains("if (inputs[\"temp1\"] > 100 || inputs[\"temp2\"] < 0)", code);
            Assert.Contains("outputs[\"alert\"] = 1", code);
        }

        [Fact]
        public void GenerateCSharp_NestedConditions_GeneratesCorrectCode()
        {
            // Arrange
            var rule = new RuleDefinition
            {
                Name = "NestedRule",
                Conditions = new ConditionGroup
                {
                    All = new List<ConditionDefinition>
                    {
                        new ComparisonCondition
                        {
                            Sensor = "temp1",
                            Operator = ComparisonOperator.GreaterThan,
                            Value = 100,
                        },
                        new ConditionGroup
                        {
                            Any = new List<ConditionDefinition>
                            {
                                new ComparisonCondition
                                {
                                    Sensor = "pressure",
                                    Operator = ComparisonOperator.LessThan,
                                    Value = 950,
                                },
                                new ComparisonCondition
                                {
                                    Sensor = "humidity",
                                    Operator = ComparisonOperator.GreaterThan,
                                    Value = 80,
                                },
                            },
                        },
                    },
                },
                Actions = new List<ActionDefinition>
                {
                    new SetValueAction { Key = "alert", Value = 1 },
                },
            };

            // Act
            var code = CodeGenerator.GenerateCSharp(new List<RuleDefinition> { rule });

            // Assert
            System.Diagnostics.Debug.WriteLine("Generated code:");
            System.Diagnostics.Debug.WriteLine(code);
            Assert.Contains(
                "if (inputs[\"temp1\"] > 100 && (inputs[\"pressure\"] < 950 || inputs[\"humidity\"] > 80))",
                code
            );
        }

        [Fact]
        public void GenerateCSharp_MixedConditions_GeneratesCorrectCode()
        {
            // Arrange
            var rule = new RuleDefinition
            {
                Name = "MixedRule",
                Conditions = new ConditionGroup
                {
                    All = new List<ConditionDefinition>
                    {
                        new ComparisonCondition
                        {
                            Sensor = "temp1",
                            Operator = ComparisonOperator.GreaterThan,
                            Value = 100,
                        },
                        new ComparisonCondition
                        {
                            Sensor = "temp2",
                            Operator = ComparisonOperator.LessThan,
                            Value = 50,
                        },
                    },
                    Any = new List<ConditionDefinition>
                    {
                        new ComparisonCondition
                        {
                            Sensor = "pressure1",
                            Operator = ComparisonOperator.LessThan,
                            Value = 950,
                        },
                        new ComparisonCondition
                        {
                            Sensor = "pressure2",
                            Operator = ComparisonOperator.GreaterThan,
                            Value = 1100,
                        },
                    },
                },
                Actions = new List<ActionDefinition>
                {
                    new SetValueAction { Key = "alert", Value = 1 },
                },
            };

            // Act
            var code = CodeGenerator.GenerateCSharp(new List<RuleDefinition> { rule });

            // Assert
            Assert.Contains(
                "if (inputs[\"temp1\"] > 100 && inputs[\"temp2\"] < 50 && (inputs[\"pressure1\"] < 950 || inputs[\"pressure2\"] > 1100))",
                code
            );
        }

        [Fact]
        public void GenerateCSharp_DeepNestedConditions_GeneratesCorrectCode()
        {
            // Arrange
            var rule = new RuleDefinition
            {
                Name = "DeepNestedRule",
                Conditions = new ConditionGroup
                {
                    All = new List<ConditionDefinition>
                    {
                        new ConditionGroup
                        {
                            Any = new List<ConditionDefinition>
                            {
                                // First condition - simple comparison
                                new ComparisonCondition
                                {
                                    Sensor = "temp1",
                                    Operator = ComparisonOperator.GreaterThan,
                                    Value = 100,
                                },
                                // Second condition - nested AND group
                                new ConditionGroup
                                {
                                    All = new List<ConditionDefinition>
                                    {
                                        new ComparisonCondition
                                        {
                                            Sensor = "temp2",
                                            Operator = ComparisonOperator.LessThan,
                                            Value = 0,
                                        },
                                        new ComparisonCondition
                                        {
                                            Sensor = "pressure",
                                            Operator = ComparisonOperator.GreaterThan,
                                            Value = 1000,
                                        },
                                    },
                                },
                            },
                        },
                        // Adding another condition to verify multiple levels of nesting
                        new ConditionGroup
                        {
                            All = new List<ConditionDefinition>
                            {
                                new ComparisonCondition
                                {
                                    Sensor = "humidity",
                                    Operator = ComparisonOperator.GreaterThan,
                                    Value = 75,
                                },
                                new ExpressionCondition { Expression = "inputs[\"rate\"] > 5" },
                            },
                        },
                    },
                },
                Actions = new List<ActionDefinition>
                {
                    new SetValueAction { Key = "alert", Value = 1 },
                },
            };

            // Act
            var code = CodeGenerator.GenerateCSharp(new List<RuleDefinition> { rule });

            // Assert
            // Testing for the proper nesting of conditions with minimal necessary parentheses
            Assert.Contains(
                "if ((inputs[\"temp1\"] > 100 || (inputs[\"temp2\"] < 0 && inputs[\"pressure\"] > 1000)) && "
                    + "(inputs[\"humidity\"] > 75 && inputs[\"rate\"] > 5))",
                code
            );

            // Additional assertions to verify proper code structure
            Assert.Contains("public class CompiledRules", code);
            Assert.Contains(
                "public void Evaluate(Dictionary<string, double> inputs, Dictionary<string, double> outputs)",
                code
            );
            Assert.Contains("outputs[\"alert\"] = 1", code);
        }

        [Fact]
        public void GenerateCSharp_EmptyConditionGroups_GeneratesCorrectCode()
        {
            // Arrange
            var rule = new RuleDefinition
            {
                Name = "EmptyGroupsRule",
                Conditions = new ConditionGroup
                {
                    All = new List<ConditionDefinition>(),
                    Any = new List<ConditionDefinition>(),
                },
                Actions = new List<ActionDefinition>
                {
                    new SetValueAction { Key = "value", Value = 1 },
                },
            };

            // Act
            var code = CodeGenerator.GenerateCSharp(new List<RuleDefinition> { rule });

            // Assert
            Assert.Contains("outputs[\"value\"] = 1", code);
            Assert.DoesNotContain("if", code);
        }

        [Fact]
        public void GenerateCSharp_LayeredRules_GeneratesCorrectEvaluationOrder()
        {
            // Arrange
            var rule1 = CreateRule("InputRule", new[] { "raw_temp" }, "temp");
            var rule2 = CreateRule("ProcessingRule", new[] { "temp" }, "processed_temp");
            var rule3 = CreateRule("AlertRule", new[] { "processed_temp" }, "alert");
            var rules = new List<RuleDefinition> { rule3, rule1, rule2 }; // Intentionally out of order

            // Act
            var code = CodeGenerator.GenerateCSharp(rules);

            // Assert
            Assert.Contains(
                "public void Evaluate(Dictionary<string, double> inputs, Dictionary<string, double> outputs)",
                code
            );
            Assert.Contains("EvaluateLayer0(inputs, outputs);", code);
            Assert.Contains("EvaluateLayer1(inputs, outputs);", code);
            Assert.Contains("EvaluateLayer2(inputs, outputs);", code);

            // Verify layer ordering through method content
            int layer0Pos = code.IndexOf("private void EvaluateLayer0");
            int layer1Pos = code.IndexOf("private void EvaluateLayer1");
            int layer2Pos = code.IndexOf("private void EvaluateLayer2");

            Assert.True(layer0Pos > 0);
            Assert.True(layer1Pos > layer0Pos);
            Assert.True(layer2Pos > layer1Pos);

            // Verify rules are in correct layers
            var layer0 = code.Substring(layer0Pos, layer1Pos - layer0Pos);
            var layer1 = code.Substring(layer1Pos, layer2Pos - layer1Pos);
            var layer2 = code.Substring(layer2Pos);

            Assert.Contains("Rule: InputRule", layer0);
            Assert.Contains("Rule: ProcessingRule", layer1);
            Assert.Contains("Rule: AlertRule", layer2);
        }

        [Fact]
        public void GenerateCSharp_ParallelRules_GeneratesInSameLayer()
        {
            // Arrange
            var rule1 = CreateRule("TempRule", new[] { "raw_temp" }, "temp1");
            var rule2 = CreateRule("PressureRule", new[] { "raw_pressure" }, "pressure1");
            var rules = new List<RuleDefinition> { rule1, rule2 };

            // Act
            var code = CodeGenerator.GenerateCSharp(rules);

            // Assert
            Assert.Contains(
                "public void Evaluate(Dictionary<string, double> inputs, Dictionary<string, double> outputs)",
                code
            );
            Assert.Contains("EvaluateLayer0(inputs, outputs);", code);
            Assert.DoesNotContain("EvaluateLayer1", code);

            // Verify both rules are in layer 0
            int layer0Pos = code.IndexOf("private void EvaluateLayer0");
            var layer0 = code.Substring(layer0Pos);

            Assert.Contains("Rule: TempRule", layer0);
            Assert.Contains("Rule: PressureRule", layer0);
        }

        [Fact]
        public void GenerateCSharp_CyclicDependency_ThrowsException()
        {
            // Arrange
            var rule1 = CreateRule("Rule1", new[] { "value2" }, "value1");
            var rule2 = CreateRule("Rule2", new[] { "value1" }, "value2");
            var rules = new List<RuleDefinition> { rule1, rule2 };

            // Act & Assert
            var exception = Assert.Throws<InvalidOperationException>(
                () => CodeGenerator.GenerateCSharp(rules)
            );
            Assert.Contains("Cyclic dependency", exception.Message);
        }

        [Fact]
        public void GenerateCSharp_ComplexDependencyGraph_GeneratesCorrectLayers()
        {
            // Arrange
            var rules = new List<RuleDefinition>
            {
                CreateRule("InputProcessing1", new[] { "raw1" }, "processed1"),
                CreateRule("InputProcessing2", new[] { "raw2" }, "processed2"),
                CreateRule("Aggregation", new[] { "processed1", "processed2" }, "aggregate"),
                CreateRule("Alert1", new[] { "aggregate" }, "alert1"),
                CreateRule("Alert2", new[] { "aggregate" }, "alert2"),
                CreateRule("FinalAlert", new[] { "alert1", "alert2" }, "final_alert"),
            };

            // Act
            var code = CodeGenerator.GenerateCSharp(rules);

            // Assert
            Assert.Contains("EvaluateLayer0", code);
            Assert.Contains("EvaluateLayer1", code);
            Assert.Contains("EvaluateLayer2", code);
            Assert.Contains("EvaluateLayer3", code);

            // Verify layer contents
            int layer0Pos = code.IndexOf("private void EvaluateLayer0");
            int layer1Pos = code.IndexOf("private void EvaluateLayer1");
            int layer2Pos = code.IndexOf("private void EvaluateLayer2");
            int layer3Pos = code.IndexOf("private void EvaluateLayer3");

            var layer0 = code.Substring(layer0Pos, layer1Pos - layer0Pos);
            var layer1 = code.Substring(layer1Pos, layer2Pos - layer1Pos);
            var layer2 = code.Substring(layer2Pos, layer3Pos - layer2Pos);
            var layer3 = code.Substring(layer3Pos);

            // Input processing in layer 0
            Assert.Contains("Rule: InputProcessing1", layer0);
            Assert.Contains("Rule: InputProcessing2", layer0);

            // Aggregation in layer 1
            Assert.Contains("Rule: Aggregation", layer1);

            // Initial alerts in layer 2
            Assert.Contains("Rule: Alert1", layer2);
            Assert.Contains("Rule: Alert2", layer2);

            // Final alert in layer 3
            Assert.Contains("Rule: FinalAlert", layer3);
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
