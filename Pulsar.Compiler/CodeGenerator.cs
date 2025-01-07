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

                    // Handle "All" conditions
                    string? allConditions = null;
                    if (rule.Conditions?.All != null && rule.Conditions.All.Any())
                    {
                        var conditions = rule.Conditions.All.Select(FormatCondition)
                                           .Where(c => !string.IsNullOrEmpty(c));
                        if (conditions.Any())
                        {
                            allConditions = string.Join(" && ", conditions);
                        }
                    }

                    // Handle "Any" conditions
                    string? anyConditions = null;
                    if (rule.Conditions?.Any != null && rule.Conditions.Any.Any())
                    {
                        var conditions = rule.Conditions.Any.Select(FormatCondition)
                                           .Where(c => !string.IsNullOrEmpty(c));
                        if (conditions.Any())
                        {
                            anyConditions = string.Join(" || ", conditions);
                        }
                    }

                    // Combine conditions
                    string? finalCondition = null;
                    if (allConditions != null && anyConditions != null)
                    {
                        finalCondition = $"({allConditions}) && ({anyConditions})";
                    }
                    else if (allConditions != null)
                    {
                        finalCondition = allConditions;
                    }
                    else if (anyConditions != null)
                    {
                        finalCondition = anyConditions;
                    }

                    if (finalCondition != null)
                    {
                        codeBuilder.AppendLine($"        if ({finalCondition})");
                        codeBuilder.AppendLine("        {");

                        foreach (var action in rule.Actions.OfType<SetValueAction>())
                        {
                            string valueAssignment;
                            if (action.Value.HasValue)
                            {
                                valueAssignment = action.Value.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
                            }
                            else if (!string.IsNullOrEmpty(action.ValueExpression))
                            {
                                valueAssignment = action.ValueExpression;
                            }
                            else
                            {
                                valueAssignment = "0";
                            }

                            codeBuilder.AppendLine(
                                $"            outputs[\"{action.Key}\"] = {valueAssignment};"
                            );
                        }

                        codeBuilder.AppendLine("        }");
                    }
                }

                codeBuilder.AppendLine("    }");
                layer++;
            }

            codeBuilder.AppendLine("}");
            return codeBuilder.ToString();
        }

        private static string FormatCondition(ConditionDefinition condition)
        {
            return condition switch
            {
                ComparisonCondition comp =>
                    $"inputs[\"{comp.Sensor}\"] {GetOperator(comp.Operator)} {comp.Value}",

                ExpressionCondition expr =>
                    expr.Expression,

                _ => string.Empty
            };
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