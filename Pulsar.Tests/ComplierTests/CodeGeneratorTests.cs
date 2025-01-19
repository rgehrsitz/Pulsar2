// File: Pulsar.Tests/ComplierTests/CodeGeneratorTests.cs

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Pulsar.Compiler.Generation;
using Pulsar.Compiler.Models;
using Pulsar.Compiler.Validation;
using Pulsar.Runtime.Buffers;
using Pulsar.Runtime.Rules;
using Serilog;
using Xunit;
using Xunit.Abstractions;

namespace Pulsar.Tests.CompilerTests
{
    public class CodeGeneratorTests
    {

        private readonly ITestOutputHelper _output;

        public CodeGeneratorTests(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact]
        public void GenerateCSharp_ProducesValidAotCompatibleCode()
        {
            // Arrange
            var rules = new List<RuleDefinition>
    {
        new RuleDefinition
        {
            Name = "TemperatureConversion",
            Description = "Converts F to C",
            SourceInfo = new SourceInfo
            {
                FileName = "test.yaml",
                LineNumber = 1
            },
            Conditions = new ConditionGroup
            {
                All = new List<ConditionDefinition>
                {
                    new ComparisonCondition
                    {
                        Type = ConditionType.Comparison,
                        Sensor = "temperature_f",
                        Operator = ComparisonOperator.GreaterThan,
                        Value = -459.67 // Absolute zero in F
                    }
                }
            },
            Actions = new List<ActionDefinition>
            {
                new SetValueAction
                {
                    Type = ActionType.SetValue,
                    Key = "temperature_c",
                    ValueExpression = "(temperature_f - 32) * 5/9"
                }
            }
        }
    };

            // Act
            var generatedFiles = CodeGenerator.GenerateCSharp(rules);

            // Debug output for all generated files
            foreach (var file in generatedFiles)
            {
                _output.WriteLine($"\n=== {file.FileName} ===");
                _output.WriteLine(file.Content);
            }

            // Assert
            var layerFile = generatedFiles.FirstOrDefault(f => f.FileName.Contains("Layer0"));
            Assert.NotNull(layerFile);

            var expectedExpression = "outputs[\"temperature_c\"] = (inputs[\"temperature_f\"] - 32) * 5/9;";
            _output.WriteLine("\nLooking for expression:");
            _output.WriteLine(expectedExpression);

            Assert.Contains(expectedExpression, layerFile.Content);
        }

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
                }
            }
                },
                Actions = new List<ActionDefinition>
        {
            new SetValueAction
            {
                Type = ActionType.SetValue,
                Key = "alert",
                Value = 1,
            }
        }
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

            // Get files by type
            var mainFile = generatedFiles.FirstOrDefault(f => f.FileName == "CompiledRules.cs");
            var interfaceFile = generatedFiles.FirstOrDefault(f => f.FileName == "ICompiledRules.cs");
            var layerFile = generatedFiles.FirstOrDefault(f => f.FileName.Contains("Layer0"));

            // Basic structural assertions
            Assert.NotNull(mainFile);
            Assert.NotNull(interfaceFile);
            Assert.NotNull(layerFile);

            // Interface assertions
            Assert.Contains("public interface ICompiledRules", interfaceFile.Content);
            Assert.Contains("void Evaluate(Dictionary<string, double> inputs, Dictionary<string, double> outputs, RingBufferManager bufferManager)", interfaceFile.Content);

            // Main class assertions
            Assert.Contains("public partial class CompiledRules : ICompiledRules", mainFile.Content);
            Assert.Contains("private readonly ILogger _logger;", mainFile.Content);
            Assert.Contains("private readonly RingBufferManager _bufferManager;", mainFile.Content);
            Assert.Contains("public void Evaluate(Dictionary<string, double> inputs, Dictionary<string, double> outputs, RingBufferManager bufferManager)", mainFile.Content);

            // Layer implementation assertions
            Assert.Contains("SimpleRule", layerFile.Content);
            Assert.Contains("inputs[\"temperature\"] > 100", layerFile.Content);
            Assert.Contains("outputs[\"alert\"] = 1", layerFile.Content);
            Assert.Contains("_logger.Debug(\"Evaluating rule SimpleRule\")", layerFile.Content);
        }

        [Fact]
        public void GenerateCSharp_MultipleRules_GeneratesValidCode()
        {
            // Arrange
            var rules = new List<RuleDefinition>
        {
            CreateRule("Rule1", new[] { "temp1" }, "intermediate"),
            CreateRule("Rule2", new[] { "intermediate" }, "output")
        };

            // Act
            var generatedFiles = CodeGenerator.GenerateCSharp(rules);

            // Assert
            Assert.NotNull(generatedFiles.FirstOrDefault(f => f.FileName == "ICompiledRules.cs"));
            Assert.NotNull(generatedFiles.FirstOrDefault(f => f.FileName == "CompiledRules.cs"));
            Assert.NotNull(generatedFiles.FirstOrDefault(f => f.FileName.Contains("Layer0")));
            Assert.NotNull(generatedFiles.FirstOrDefault(f => f.FileName.Contains("Layer1")));

            // Verify rule ordering in main file
            var mainFile = generatedFiles.First(f => f.FileName == "CompiledRules.cs");
            var layer0Index = mainFile.Content.IndexOf("EvaluateLayer0");
            var layer1Index = mainFile.Content.IndexOf("EvaluateLayer1");
            Assert.True(layer0Index < layer1Index, "Rules not evaluated in dependency order");
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
                        Expression = "temperature * 1.8 + 32 > 100"
                    }
                }
                },
                Actions = new List<ActionDefinition>
            {
                new SetValueAction
                {
                    Key = "fahrenheit",
                    ValueExpression = "temperature * 1.8 + 32"
                }
            }
            };

            // Act
            var generatedFiles = CodeGenerator.GenerateCSharp(new List<RuleDefinition> { rule });

            // Assert
            var implFile = generatedFiles.First(f => f.FileName.Contains("Layer"));
            Assert.Contains("inputs[\"temperature\"] * 1.8 + 32 > 100", implFile.Content);
            Assert.Contains("outputs[\"fahrenheit\"] = inputs[\"temperature\"] * 1.8 + 32", implFile.Content);
        }

        [Fact]
        public void GenerateCSharp_EmptyRulesList_GeneratesMinimalValidCode()
        {
            // Arrange
            var rules = new List<RuleDefinition>();

            // Act
            var generatedFiles = CodeGenerator.GenerateCSharp(rules);

            // Assert
            Assert.Single(generatedFiles);
            var file = generatedFiles[0];
            Assert.Equal("CompiledRules.cs", file.FileName);
            Assert.Contains("public class CompiledRules : ICompiledRules", file.Content);
            Assert.Contains("private readonly ILogger _logger;", file.Content);
            Assert.Contains("private readonly RingBufferManager _bufferManager;", file.Content);
            Assert.Contains("public CompiledRules(ILogger logger, RingBufferManager bufferManager)", file.Content);
            Assert.Contains("_logger = logger ?? throw new ArgumentNullException(nameof(logger));", file.Content);
            Assert.Contains("_bufferManager = bufferManager ?? throw new ArgumentNullException(nameof(bufferManager));", file.Content);
            Assert.Contains("public void Evaluate(Dictionary<string, double> inputs, Dictionary<string, double> outputs, RingBufferManager bufferManager)", file.Content);
            Assert.Contains("_logger.Debug(\"No rules to evaluate\")", file.Content);
            Assert.DoesNotContain("EvaluateLayer", file.Content);
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
                    Sensor = "temp1",
                    Operator = ComparisonOperator.GreaterThan,
                    Value = 100,
                },
                new ComparisonCondition
                {
                    Sensor = "temp2",
                    Operator = ComparisonOperator.LessThan,
                    Value = 0,
                }
            },
                },
                Actions = new List<ActionDefinition>
        {
            new SetValueAction { Key = "alert", Value = 1 }
        },
            };

            // Act
            var generatedFiles = CodeGenerator.GenerateCSharp(new List<RuleDefinition> { rule });

            // Output all generated files for debugging
            _output.WriteLine("\nGenerated Files:");
            foreach (var file in generatedFiles)
            {
                _output.WriteLine($"--- {file.FileName} ---");
                _output.WriteLine(file.Content);
                _output.WriteLine("---END OF FILE---\n");
            }

            // Find the layer implementation file
            var layerFile = generatedFiles.FirstOrDefault(f =>
                f.FileName.Contains("Layer") &&
                f.Content.Contains("EvaluateLayer"));

            Assert.NotNull(layerFile);

            // Validate the condition
            var fileContent = layerFile.Content;

            // Debug output to help diagnose issues
            _output.WriteLine("\nGenerated code:");
            _output.WriteLine(fileContent);

            // The condition should follow C# conventions:
            // - No unnecessary parentheses around simple comparisons
            // - Parentheses around OR conditions for clarity
            bool conditionFound = System.Text.RegularExpressions.Regex.IsMatch(fileContent,
                @"if\s*\(\(inputs\[""temp1""\]\s*>\s*100\s*\|\|\s*inputs\[""temp2""\]\s*<\s*0\)\)");

            Assert.True(conditionFound,
                $"Expected condition following C# conventions not found. Generated code:\n{fileContent}");
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

            // Output all generated files for debugging
            _output.WriteLine("\nGenerated Files:");
            foreach (var file in generatedFiles)
            {
                _output.WriteLine($"--- {file.FileName} ---");
                _output.WriteLine(file.Content);
                _output.WriteLine("---END OF FILE---\n");
            }

            // Find the layer implementation file
            var layerFile = generatedFiles.FirstOrDefault(f =>
                f.FileName.Contains("Layer") &&
                f.Content.Contains("EvaluateLayer"));

            Assert.NotNull(layerFile);

            // Validate the condition
            var fileContent = layerFile.Content;

            // Debug output to help diagnose issues
            _output.WriteLine("\nGenerated code:");
            _output.WriteLine(fileContent);

            // The condition should follow C# conventions:
            // - No unnecessary parentheses around simple comparisons
            // - Parentheses only where needed for operator precedence (around OR conditions)
            bool conditionFound = System.Text.RegularExpressions.Regex.IsMatch(fileContent,
                @"if\s*\(\s*inputs\[""temp1""\]\s*>\s*100\s*&&\s*\(\s*inputs\[""pressure""\]\s*<\s*950\s*\|\|\s*inputs\[""humidity""\]\s*>\s*80\s*\)\)");

            Assert.True(conditionFound,
                $"Expected condition following C# conventions not found. Generated code:\n{fileContent}");

            // Verify the action
            Assert.Contains("outputs[\"alert\"] = 1", fileContent);
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

            // Output all generated files for debugging
            _output.WriteLine("\nGenerated Files:");
            foreach (var file in generatedFiles)
            {
                _output.WriteLine($"--- {file.FileName} ---");
                _output.WriteLine(file.Content);
                _output.WriteLine("---END OF FILE---\n");
            }

            // Find the layer implementation file
            var layerFile = generatedFiles.FirstOrDefault(f =>
                f.FileName.Contains("Layer") &&
                f.Content.Contains("EvaluateLayer"));

            Assert.NotNull(layerFile);

            // Validate the condition
            var fileContent = layerFile.Content;

            // Debug output to help diagnose issues
            _output.WriteLine("\nGenerated code:");
            _output.WriteLine(fileContent);

            // The condition should follow C# conventions:
            // - No unnecessary parentheses around simple comparisons
            // - Parentheses only where needed for operator precedence (around OR conditions)
            bool conditionFound = System.Text.RegularExpressions.Regex.IsMatch(fileContent,
                @"if\s*\(\s*inputs\[""temp1""\]\s*>\s*100\s*&&\s*inputs\[""temp2""\]\s*<\s*50\s*&&\s*\(\s*inputs\[""pressure1""\]\s*<\s*950\s*\|\|\s*inputs\[""pressure2""\]\s*>\s*1100\)\)");

            Assert.True(conditionFound,
                $"Expected condition following C# conventions not found. Generated code:\n{fileContent}");

            // Verify the action
            Assert.Contains("outputs[\"alert\"] = 1", fileContent);
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

            // Output all generated files for debugging
            _output.WriteLine("\nGenerated Files:");
            foreach (var file in generatedFiles)
            {
                _output.WriteLine($"--- {file.FileName} ---");
                _output.WriteLine(file.Content);
                _output.WriteLine("---END OF FILE---\n");
            }

            // Find the layer implementation file
            var layerFile = generatedFiles.FirstOrDefault(f =>
                f.FileName.Contains("Layer") &&
                f.Content.Contains("EvaluateLayer"));

            Assert.NotNull(layerFile);

            // Validate the condition
            var fileContent = layerFile.Content;

            // Debug output to help diagnose issues
            _output.WriteLine("\nGenerated code:");
            _output.WriteLine(fileContent);

            // The condition should follow C# conventions:
            // - No unnecessary parentheses around simple comparisons
            // - Parentheses only where needed for operator precedence (around OR conditions)
            bool conditionFound = System.Text.RegularExpressions.Regex.IsMatch(fileContent,
                @"if\s*\(\s*\(\s*inputs\[""temp1""\]\s*>\s*100\s*\|\|\s*\(\s*inputs\[""temp2""\]\s*<\s*0\s*&&\s*inputs\[""pressure""\]\s*>\s*1000\s*\)\s*\)\s*&&\s*inputs\[""humidity""\]\s*>\s*75\s*&&\s*inputs\[""rate""\]\s*>\s*5\)");

            Assert.True(conditionFound,
                $"Expected condition following C# conventions not found. Generated code:\n{fileContent}");

            // Verify the action
            Assert.Contains("outputs[\"alert\"] = 1", fileContent);
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
            var mainFile = generatedFiles.First(f => f.FileName == "CompiledRules.cs");

            // Assert
            Assert.Contains(
                "public void Evaluate(Dictionary<string, double> inputs, Dictionary<string, double> outputs, RingBufferManager bufferManager)",
                mainFile.Content
            );
            Assert.Contains("EvaluateLayer0(inputs, outputs, bufferManager);", mainFile.Content);
            Assert.Contains("EvaluateLayer1(inputs, outputs, bufferManager);", mainFile.Content);
            Assert.Contains("EvaluateLayer2(inputs, outputs, bufferManager);", mainFile.Content);

            // Verify layer ordering through method content
            int layer0Pos = mainFile.Content.IndexOf("private void EvaluateLayer0");
            int layer1Pos = mainFile.Content.IndexOf("private void EvaluateLayer1");
            int layer2Pos = mainFile.Content.IndexOf("private void EvaluateLayer2");

            Assert.True(layer0Pos > 0);
            Assert.True(layer1Pos > layer0Pos);
            Assert.True(layer2Pos > layer1Pos);

            // Verify rules are in correct layers
            var layer0 = mainFile.Content.Substring(layer0Pos, layer1Pos - layer0Pos);
            var layer1 = mainFile.Content.Substring(layer1Pos, layer2Pos - layer1Pos);
            var layer2 = mainFile.Content.Substring(layer2Pos);

            // Input processing in layer 0
            Assert.Contains("Rule: InputRule", layer0);
            Assert.Contains("_logger.Debug(\"Evaluating rule InputRule\")", layer0);
            Assert.Contains("Rule: ProcessingRule", layer1);
            Assert.Contains("_logger.Debug(\"Evaluating rule ProcessingRule\")", layer1);
            Assert.Contains("Rule: AlertRule", layer2);
            Assert.Contains("_logger.Debug(\"Evaluating rule AlertRule\")", layer2);
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

            // Assert
            Assert.Contains(generatedFiles, f => f.FileName == "CompiledRules.cs");
            Assert.Contains(generatedFiles, f => f.FileName == "ICompiledRules.cs");
            Assert.Contains(generatedFiles, f => f.FileName.Contains("Layer0"));

            var mainFile = generatedFiles.First(f => f.FileName == "CompiledRules.cs");
            var interfaceFile = generatedFiles.First(f => f.FileName == "ICompiledRules.cs");
            var layerFile = generatedFiles.First(f => f.FileName.Contains("Layer0"));

            // Verify interface
            Assert.Contains("void Evaluate(Dictionary<string, double> inputs, Dictionary<string, double> outputs, RingBufferManager bufferManager)", interfaceFile.Content);

            // Verify main class
            Assert.Contains("public void Evaluate(Dictionary<string, double> inputs, Dictionary<string, double> outputs, RingBufferManager bufferManager)", mainFile.Content);
            Assert.Contains("EvaluateLayer0(inputs, outputs, bufferManager);", mainFile.Content);
            Assert.DoesNotContain("EvaluateLayer1", mainFile.Content);

            // Verify layer implementation
            Assert.Contains("Rule: TempRule", layerFile.Content);
            Assert.Contains("Rule: PressureRule", layerFile.Content);
            Assert.Contains("using System;", layerFile.Content);
            Assert.Contains("using System.Collections.Generic;", layerFile.Content);
            Assert.Contains("using System.Linq;", layerFile.Content);
            Assert.Contains("using Serilog;", layerFile.Content);
            Assert.Contains("using Prometheus;", layerFile.Content);
            Assert.Contains("using Pulsar.Runtime.Buffers;", layerFile.Content);
            Assert.Contains("using Pulsar.Runtime.Common;", layerFile.Content);
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

        // Helper method for creating test rules
        private static RuleDefinition CreateRule(string name, string[] inputs, string output)
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
                    }
                }
            };
        }
    }
}
