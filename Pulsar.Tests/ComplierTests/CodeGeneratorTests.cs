// File: Pulsar.Tests/ComplierTests/CodeGeneratorTests.cs

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Pulsar.Compiler.Generation;
using Pulsar.Compiler.Models;
using Pulsar.Compiler.Validation;
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
            var generatedFiles = CodeGenerator.GenerateCSharp(new List<RuleDefinition> { rule });

            // Debug output for all files
            foreach (var file in generatedFiles)
            {
                Debug.WriteLine($"\nFile: {file.FileName}");
                Debug.WriteLine("Content:");
                Debug.WriteLine(file.Content);
            }

            // Get files
            var ruleFile = generatedFiles.First(f => f.FileName.StartsWith("RuleGroup_"));
            var coordinatorFile = generatedFiles.First(f => f.FileName == "RuleCoordinator.cs");

            // Basic structural assertions first
            Assert.Equal(2, generatedFiles.Count);
            Assert.Contains("public partial class CompiledRules", ruleFile.Content);
            Assert.Contains("SimpleRule", ruleFile.Content);
            Assert.Contains("Evaluate", coordinatorFile.Content);

            // Let's log the exact pattern we're looking for
            Debug.WriteLine("\nLooking for condition pattern in content:");
            var conditionPattern = "inputs[\"temperature\"] > 100";
            Debug.WriteLine($"Pattern: {conditionPattern}");
            Debug.WriteLine("Content relevant section:");
            Debug.WriteLine(ruleFile.Content.Substring(0, Math.Min(500, ruleFile.Content.Length)));

            // More specific assertions
            Assert.Contains(conditionPattern, ruleFile.Content);
            Assert.Contains("outputs[\"alert\"] = 1", ruleFile.Content);
            Assert.Contains($"EvaluateGroup_{ruleFile.LayerRange.Start}_{ruleFile.LayerRange.End}", coordinatorFile.Content);
        }

        [Fact]
        public void GenerateCSharp_MultipleRules_GeneratesValidCode()
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
            new SetValueAction
            {
                Type = ActionType.SetValue,
                Key = "intermediate",
                Value = 1,
            },
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
            new SetValueAction
            {
                Type = ActionType.SetValue,
                Key = "output",
                Value = 2,
            },
        },
            };

            // Act
            var config = new RuleGroupingConfig { MaxRulesPerFile = 1 }; // Force separate files
            var generatedFiles = CodeGenerator.GenerateCSharp(new List<RuleDefinition> { rule1, rule2 }, config);

            // Assert
            Assert.Equal(3, generatedFiles.Count); // Coordinator + two rule groups

            var ruleFiles = generatedFiles.Where(f => f.FileName.StartsWith("RuleGroup_"))
                                         .OrderBy(f => f.LayerRange.Start)
                                         .ToList();
            var coordinatorFile = generatedFiles.First(f => f.FileName == "RuleCoordinator.cs");

            // Verify rule1 comes before rule2 due to dependency
            Assert.Contains("Rule1", ruleFiles[0].Content);
            Assert.Contains("Rule2", ruleFiles[1].Content);
            Assert.Contains("inputs[\"temp1\"] > 50", ruleFiles[0].Content);
            Assert.Contains("inputs[\"intermediate\"] == 1", ruleFiles[1].Content);

            // Verify coordinator calls groups in correct order
            var coordinatorContent = coordinatorFile.Content;
            Assert.True(
                coordinatorContent.IndexOf($"EvaluateGroup_{ruleFiles[0].LayerRange.Start}_") <
                coordinatorContent.IndexOf($"EvaluateGroup_{ruleFiles[1].LayerRange.Start}_"),
                "Rules not evaluated in dependency order"
            );
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
            var generatedFiles = CodeGenerator.GenerateCSharp(new List<RuleDefinition> { rule });
            var ruleFile = generatedFiles.First(f => f.FileName.StartsWith("RuleGroup_"));

            // Assert
            Assert.Contains("if (inputs[\"temperature\"] * 1.8 + 32 > 100)", ruleFile.Content);
            Assert.Contains("outputs[\"fahrenheit\"] = inputs[\"temperature\"] * 1.8 + 32", ruleFile.Content);
        }

        [Fact]
        public void GenerateCSharp_EmptyRulesList_GeneratesMinimalValidCode()
        {
            // Arrange
            var rules = new List<RuleDefinition>();

            // Act
            var generatedFiles = CodeGenerator.GenerateCSharp(rules);

            // Assert
            Assert.Single(generatedFiles); // Only coordinator file
            Assert.Equal("RuleCoordinator.cs", generatedFiles[0].FileName);
            Assert.Contains("public void Evaluate", generatedFiles[0].Content);
            Assert.DoesNotContain("EvaluateGroup_", generatedFiles[0].Content);
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
            var generatedFiles = CodeGenerator.GenerateCSharp(new List<RuleDefinition> { rule });
            var ruleFile = generatedFiles.First(f => f.FileName.StartsWith("RuleGroup_"));

            // Assert
            Assert.Contains("if (inputs[\"temp1\"] > 100 || inputs[\"temp2\"] < 0)", ruleFile.Content);
            Assert.Contains("outputs[\"alert\"] = 1", ruleFile.Content);
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
            var generatedFiles = CodeGenerator.GenerateCSharp(new List<RuleDefinition> { rule });
            var code = generatedFiles[0].Content;

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
            var generatedFiles = CodeGenerator.GenerateCSharp(new List<RuleDefinition> { rule });
            var code = generatedFiles[0].Content;

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
                                new ComparisonCondition
                                {
                                    Sensor = "temp1",
                                    Operator = ComparisonOperator.GreaterThan,
                                    Value = 100,
                                },
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
                                new ExpressionCondition { Expression = "rate > 5" },
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
            var generatedFiles = CodeGenerator.GenerateCSharp(new List<RuleDefinition> { rule });
            var code = generatedFiles[0].Content;

            // Assert
            const string expectedCondition =
                "(inputs[\"temp1\"] > 100 || (inputs[\"temp2\"] < 0 && inputs[\"pressure\"] > 1000)) && (inputs[\"humidity\"] > 75 && inputs[\"rate\"] > 5)";

            // Test the if statement condition
            Assert.Contains($"if ({expectedCondition})", code);

            // Test the debug output with proper escaping
            Assert.Contains(
                $"System.Diagnostics.Debug.WriteLine(\"Checking condition: {expectedCondition.Replace("\"", "\\\"")}\")",
                code
            );

            // Test that the action is present
            Assert.Contains("outputs[\"alert\"] = 1", code);

            // Debug output for verification
            Debug.WriteLine("Expected debug statement:");
            Debug.WriteLine(
                $"System.Diagnostics.Debug.WriteLine(\"Checking condition: {expectedCondition.Replace("\"", "\\\"")}\")"
            );
            Debug.WriteLine("\nActual Code:");
            Debug.WriteLine(code);
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
            var generatedFiles = CodeGenerator.GenerateCSharp(new List<RuleDefinition> { rule });
            var code = generatedFiles[0].Content;

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
            var generatedFiles = CodeGenerator.GenerateCSharp(rules);
            var code = generatedFiles[0].Content;

            // Assert
            Assert.Contains(
                "public void Evaluate(Dictionary<string, double> inputs, Dictionary<string, double> outputs, RingBufferManager bufferManager)",
                code
            );
            Assert.Contains("EvaluateLayer0(inputs, outputs, bufferManager);", code);
            Assert.Contains("EvaluateLayer1(inputs, outputs, bufferManager);", code);
            Assert.Contains("EvaluateLayer2(inputs, outputs, bufferManager);", code);

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

            // Input processing in layer 0
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
            var generatedFiles = CodeGenerator.GenerateCSharp(rules);
            var code = generatedFiles[0].Content;

            // Assert
            Assert.Contains(
                "public void Evaluate(Dictionary<string, double> inputs, Dictionary<string, double> outputs, RingBufferManager bufferManager)",
                code
            );
            Assert.Contains("EvaluateLayer0(inputs, outputs, bufferManager);", code);
            Assert.DoesNotContain("EvaluateLayer1", code);

            // Verify both rules are in layer 0
            var layer0Start = code.IndexOf("private void EvaluateLayer0");
            var layer0Code = code.Substring(layer0Start);
            Assert.Contains("Rule: TempRule", layer0Code);
            Assert.Contains("Rule: PressureRule", layer0Code);
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
            var generatedFiles = CodeGenerator.GenerateCSharp(rules);
            var code = string.Join("\n", generatedFiles.Select(f => f.Content));

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

        [Fact]
        public void FixupExpression_HandlesComplexMathExpressions()
        {
            var testCases = new Dictionary<string, string>
            {
                {
                    "Math.Abs(temp - setpoint) > threshold",
                    "(Math.Abs(inputs[\"temp\"] - inputs[\"setpoint\"]) > inputs[\"threshold\"])"
                },
                { "temperature * 1.8 + 32 > 100", "(inputs[\"temperature\"] * 1.8 + 32 > 100)" },
                {
                    "rate > 5 && pressure < 1000",
                    "(inputs[\"rate\"] > 5 && inputs[\"pressure\"] < 1000)"
                },
            };

            foreach (var testCase in testCases)
            {
                var result = CodeGenerator.FixupExpression(testCase.Key);
                Assert.Equal(testCase.Value, result);
                Assert.True(ExpressionHelper.ValidateGeneratedExpression(result));
            }
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

        [Fact]
        public void GenerateCSharp_WithGroupingConfig_RespectsMaxRulesPerFile()
        {
            // Arrange
            var rules = new List<RuleDefinition>();
            for (int i = 0; i < 15; i++)
            {
                rules.Add(CreateRule($"Rule{i}", new[] { "input" }, $"output{i}"));
            }

            var config = new RuleGroupingConfig
            {
                MaxRulesPerFile = 5,
                MaxLinesPerFile = 1000,
                GroupParallelRules = true
            };

            // Act
            var generatedFiles = CodeGenerator.GenerateCSharp(rules, config);
            var ruleFiles = generatedFiles.Where(f => f.FileName != "RuleCoordinator.cs").ToList();

            // Assert
            Assert.True(ruleFiles.All(f =>
                f.Content.Split("Rule:").Length - 1 <= config.MaxRulesPerFile),
                "Some files contain more rules than MaxRulesPerFile");

            // Verify coordinator calls all groups
            var coordinator = generatedFiles.First(f => f.FileName == "RuleCoordinator.cs");
            foreach (var file in ruleFiles)
            {
                var methodName = $"EvaluateGroup_{file.LayerRange.Start}_{file.LayerRange.End}";
                Assert.Contains(methodName, coordinator.Content);
            }
        }

        [Fact]
        public void GenerateCSharp_WithDependencies_MaintainsOrderInGroups()
        {
            // Arrange
            var rule1 = CreateRule("Rule1", new[] { "input" }, "intermediate1");
            var rule2 = CreateRule("Rule2", new[] { "intermediate1" }, "intermediate2");
            var rule3 = CreateRule("Rule3", new[] { "intermediate2" }, "output");

            var config = new RuleGroupingConfig
            {
                MaxRulesPerFile = 2,  // Force splitting into multiple groups
                GroupParallelRules = true
            };

            // Act
            var generatedFiles = CodeGenerator.GenerateCSharp(new[] { rule1, rule2, rule3 }.ToList(), config);
            var coordinator = generatedFiles.First(f => f.FileName == "RuleCoordinator.cs");

            // Assert
            var groups = generatedFiles.Where(f => f.FileName != "RuleCoordinator.cs")
                                      .OrderBy(f => f.LayerRange.Start)
                                      .ToList();

            // Verify layer ordering
            for (int i = 1; i < groups.Count; i++)
            {
                Assert.True(groups[i - 1].LayerRange.End <= groups[i].LayerRange.Start,
                    "Groups are not properly ordered by layer");
            }

            // Verify coordinator calls methods in correct order
            var coordinatorContent = coordinator.Content;
            var evaluationLines = coordinatorContent
                .Split('\n')
                .Where(l => l.Contains("EvaluateGroup_"))
                .ToList();

            for (int i = 1; i < evaluationLines.Count; i++)
            {
                var prevGroupNum = int.Parse(evaluationLines[i - 1].Split('_')[1]);
                var currentGroupNum = int.Parse(evaluationLines[i].Split('_')[1]);
                Assert.True(prevGroupNum <= currentGroupNum,
                    "Coordinator is not calling groups in dependency order");
            }
        }

        [Fact]
        public void GenerateCSharp_WithParallelRules_GroupsCorrectly()
        {
            // Arrange
            var rules = new List<RuleDefinition>
    {
        CreateRule("ParallelRule1", new[] { "input1" }, "output1"),
        CreateRule("ParallelRule2", new[] { "input2" }, "output2"),
        CreateRule("ParallelRule3", new[] { "input3" }, "output3"),
        CreateRule("DependentRule", new[] { "output1", "output2" }, "finalOutput")
    };

            var config = new RuleGroupingConfig
            {
                MaxRulesPerFile = 2,
                GroupParallelRules = true
            };

            // Act
            var generatedFiles = CodeGenerator.GenerateCSharp(rules, config);

            // Assert
            var ruleFiles = generatedFiles.Where(f => f.FileName != "RuleCoordinator.cs").ToList();

            // Verify parallel rules are grouped together when possible
            var firstGroupContent = ruleFiles.First().Content;
            Assert.True(
                firstGroupContent.Contains("ParallelRule1") &&
                firstGroupContent.Contains("ParallelRule2") ||
                firstGroupContent.Contains("ParallelRule2") &&
                firstGroupContent.Contains("ParallelRule3") ||
                firstGroupContent.Contains("ParallelRule1") &&
                firstGroupContent.Contains("ParallelRule3"),
                "Parallel rules were not grouped together"
            );

            // Verify dependent rule is in a later group
            var lastGroupContent = ruleFiles.Last().Content;
            Assert.Contains("DependentRule", lastGroupContent);
        }

        [Fact]
        public void GenerateCSharp_WithLargeRules_RespectsMaxLinesPerFile()
        {
            // Arrange
            var rule1 = new RuleDefinition
            {
                Name = "LargeRule1",
                Conditions = new ConditionGroup
                {
                    All = Enumerable.Range(0, 20).Select(i =>
                        new ComparisonCondition
                        {
                            Type = ConditionType.Comparison,
                            Sensor = $"input{i}",
                            Operator = ComparisonOperator.GreaterThan,
                            Value = i
                        } as ConditionDefinition).ToList()
                },
                Actions = new List<ActionDefinition>
        {
            new SetValueAction
            {
                Type = ActionType.SetValue,
                Key = "output1",
                Value = 1
            }
        }
            };

            var rule2 = new RuleDefinition
            {
                Name = "LargeRule2",
                Conditions = new ConditionGroup
                {
                    All = Enumerable.Range(0, 20).Select(i =>
                        new ComparisonCondition
                        {
                            Type = ConditionType.Comparison,
                            Sensor = $"input{i}",
                            Operator = ComparisonOperator.LessThan,
                            Value = i
                        } as ConditionDefinition).ToList()
                },
                Actions = new List<ActionDefinition>
        {
            new SetValueAction
            {
                Type = ActionType.SetValue,
                Key = "output2",
                Value = 2
            }
        }
            };

            var config = new RuleGroupingConfig
            {
                MaxRulesPerFile = 10,
                MaxLinesPerFile = 100  // Set small to force splitting
            };

            // Act
            var generatedFiles = CodeGenerator.GenerateCSharp(new[] { rule1, rule2 }.ToList(), config);
            var ruleFiles = generatedFiles.Where(f => f.FileName != "RuleCoordinator.cs").ToList();

            // Assert
            Assert.True(ruleFiles.All(f =>
                f.Content.Split('\n').Length <= config.MaxLinesPerFile),
                "Some files exceed MaxLinesPerFile");
        }
    }
}
