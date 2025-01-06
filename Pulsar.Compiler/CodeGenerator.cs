// File: Pulsar.Compiler/CodeGenerator.cs

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Pulsar.Compiler.Models;

namespace Pulsar.Compiler.Generation
{
    public class CodeGenerator
    {
        public static string GenerateCSharp(List<RuleDefinition> sortedRules)
        {
            var codeBuilder = new StringBuilder();
            codeBuilder.AppendLine("using System;");
            codeBuilder.AppendLine("using System.Collections.Generic;");
            codeBuilder.AppendLine();
            codeBuilder.AppendLine("public class CompiledRules");
            codeBuilder.AppendLine("{");

            int layer = 0;
            foreach (var group in sortedRules.GroupBy(r => r))
            {
                codeBuilder.AppendLine(
                    $"    public void EvaluateLayer{layer}(Dictionary<string, double> inputs, Dictionary<string, double> outputs)"
                );
                codeBuilder.AppendLine("    {");

                foreach (var rule in group)
                {
                    codeBuilder.AppendLine($"        // Rule: {rule.Name}");

                    if (rule.Conditions?.All != null)
                    {
                        foreach (var condition in rule.Conditions.All.OfType<ComparisonCondition>())
                        {
                            codeBuilder.AppendLine(
                                $"        if (inputs[\"{condition.Sensor}\"] {GetOperator(condition.Operator)} {condition.Value})"
                            );
                            codeBuilder.AppendLine("        {");

                            foreach (var action in rule.Actions.OfType<SetValueAction>())
                            {
                                codeBuilder.AppendLine(
                                    $"            outputs[\"{action.Key}\"] = {action.ValueExpression};"
                                );
                            }

                            codeBuilder.AppendLine("        }");
                        }
                    }
                }

                codeBuilder.AppendLine("    }");
                layer++;
            }

            codeBuilder.AppendLine("}");
            return codeBuilder.ToString();
        }

        private static string GetOperator(ComparisonOperator op)
        {
            return op switch
            {
                ComparisonOperator.LessThan => "<",
                ComparisonOperator.GreaterThan => ">",
                ComparisonOperator.LessThanOrEqual => "<=",
                ComparisonOperator.GreaterThanOrEqual => ">=",
                ComparisonOperator.EqualTo => "==",
                ComparisonOperator.NotEqualTo => "!=",
                _ => throw new InvalidOperationException("Unsupported operator"),
            };
        }
    }
}
