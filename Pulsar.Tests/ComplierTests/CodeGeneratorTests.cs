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
                    Expression = "Math.Abs(temperature - setpoint) > threshold"
                },
                new ExpressionCondition
                {
                    Type = ConditionType.Expression,
                    Expression = "rate_of_change > 5"
                }
            }
                },
                Actions = new List<ActionDefinition>
        {
            new SetValueAction
            {
                Key = "alert",
                Value = 1
            }
        }
            };

            // Act
            var code = CodeGenerator.GenerateCSharp(new List<RuleDefinition> { rule });

            // Assert
            Assert.Contains("(Math.Abs(temperature - setpoint) > threshold)", code);
            Assert.Contains("(rate_of_change > 5)", code);
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
                        Value = 100
                    },
                    new ConditionGroup
                    {
                        Any = new List<ConditionDefinition>
                        {
                            new ComparisonCondition
                            {
                                Sensor = "pressure",
                                Operator = ComparisonOperator.LessThan,
                                Value = 950
                            },
                            new ComparisonCondition
                            {
                                Sensor = "humidity",
                                Operator = ComparisonOperator.GreaterThan,
                                Value = 80
                            }
                        }
                    }
                }
                },
                Actions = new List<ActionDefinition>
            {
                new SetValueAction
                {
                    Key = "alert",
                    Value = 1
                }
            }
            };

            // Act
            var code = CodeGenerator.GenerateCSharp(new List<RuleDefinition> { rule });

            // Assert
            System.Diagnostics.Debug.WriteLine("Generated code:");
            System.Diagnostics.Debug.WriteLine(code);
            Assert.Contains("if (inputs[\"temp1\"] > 100 && (inputs[\"pressure\"] < 950 || inputs[\"humidity\"] > 80))", code);
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
                        Sensor = "temp",
                        Operator = ComparisonOperator.GreaterThan,
                        Value = 100
                    }
                },
                    Any = new List<ConditionDefinition>
                {
                    new ComparisonCondition
                    {
                        Sensor = "pressure",
                        Operator = ComparisonOperator.LessThan,
                        Value = 950
                    }
                }
                },
                Actions = new List<ActionDefinition>
            {
                new SetValueAction
                {
                    Key = "alert",
                    Value = 1
                }
            }
            };

            // Act
            var code = CodeGenerator.GenerateCSharp(new List<RuleDefinition> { rule });

            // Assert
            Assert.Contains("if ((inputs[\"temp\"] > 100) && inputs[\"pressure\"] < 950)", code);
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
                                Value = 100
                            },
                            new ConditionGroup
                            {
                                All = new List<ConditionDefinition>
                                {
                                    new ComparisonCondition
                                    {
                                        Sensor = "temp2",
                                        Operator = ComparisonOperator.LessThan,
                                        Value = 0
                                    },
                                    new ComparisonCondition
                                    {
                                        Sensor = "pressure",
                                        Operator = ComparisonOperator.GreaterThan,
                                        Value = 1000
                                    }
                                }
                            }
                        }
                    }
                }
                },
                Actions = new List<ActionDefinition>
            {
                new SetValueAction
                {
                    Key = "alert",
                    Value = 1
                }
            }
            };

            // Act
            var code = CodeGenerator.GenerateCSharp(new List<RuleDefinition> { rule });

            // Assert
            Assert.Contains("if ((inputs[\"temp1\"] > 100 || (inputs[\"temp2\"] < 0 && inputs[\"pressure\"] > 1000)))", code);
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
                    Any = new List<ConditionDefinition>()
                },
                Actions = new List<ActionDefinition>
            {
                new SetValueAction
                {
                    Key = "value",
                    Value = 1
                }
            }
            };

            // Act
            var code = CodeGenerator.GenerateCSharp(new List<RuleDefinition> { rule });

            // Assert
            Assert.Contains("outputs[\"value\"] = 1", code);
            Assert.DoesNotContain("if", code);
        }
    }
}
