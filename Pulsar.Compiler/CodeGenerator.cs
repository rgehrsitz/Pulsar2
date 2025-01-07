using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Diagnostics;
using Pulsar.Compiler.Models;

namespace Pulsar.Compiler.Generation
{
    public class CodeGenerator
    {
        public static string GenerateCSharp(List<RuleDefinition> sortedRules)
        {
            ArgumentNullException.ThrowIfNull(sortedRules, nameof(sortedRules));

            var codeBuilder = new StringBuilder();
            codeBuilder.AppendLine("using System;");
            codeBuilder.AppendLine("using System.Collections.Generic;");
            codeBuilder.AppendLine();
            codeBuilder.AppendLine("public class CompiledRules");
            codeBuilder.AppendLine("{");
            codeBuilder.AppendLine("    public void EvaluateRules(Dictionary<string, double> inputs, Dictionary<string, double> outputs)");
            codeBuilder.AppendLine("    {");

            foreach (var rule in sortedRules)
            {
                Debug.WriteLine($"\nProcessing rule: {rule.Name}");
                codeBuilder.AppendLine($"        // Rule: {rule.Name}");

                string? condition = GenerateCondition(rule.Conditions);
                Debug.WriteLine($"Generated condition before processing: {condition}");

                if (!string.IsNullOrEmpty(condition))
                {
                    // Remove outer parentheses if they exist
                    if (condition.StartsWith("(") && condition.EndsWith(")"))
                    {
                        condition = condition[1..^1];
                        Debug.WriteLine($"Condition after parentheses removal: {condition}");
                    }

                    codeBuilder.AppendLine($"        if ({condition})");
                    Debug.WriteLine($"Added if statement: if ({condition})");
                    codeBuilder.AppendLine("        {");
                    GenerateActions(codeBuilder, rule.Actions);
                    codeBuilder.AppendLine("        }");
                }
            }

            codeBuilder.AppendLine("    }");
            codeBuilder.AppendLine("}");

            var finalCode = codeBuilder.ToString();
            Debug.WriteLine("\n==== COMPLETE GENERATED CODE START ====");
            Debug.WriteLine(finalCode);
            Debug.WriteLine("==== COMPLETE GENERATED CODE END ====");

            return finalCode;
        }

        private static string? GenerateCondition(ConditionGroup? conditions)
        {
            if (conditions == null) return null;

            Debug.WriteLine("Processing ConditionGroup:");
            Debug.WriteLine($"  All conditions count: {conditions.All?.Count ?? 0}");
            Debug.WriteLine($"  Any conditions count: {conditions.Any?.Count ?? 0}");

            var result = "";

            // Handle All conditions
            if (conditions.All?.Any() == true)
            {
                var allConditions = new List<string>();
                foreach (var condition in conditions.All)
                {
                    Debug.WriteLine($"Processing All condition of type: {condition.GetType().Name}");
                    string? conditionStr = null;

                    if (condition is ComparisonCondition comp)
                    {
                        conditionStr = $"inputs[\"{comp.Sensor}\"] {GetOperator(comp.Operator)} {comp.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
                    }
                    else if (condition is ExpressionCondition expr)
                    {
                        conditionStr = $"({expr.Expression})";
                    }
                    else if (condition is ConditionGroup group)
                    {
                        var nestedCondition = GenerateCondition(group);
                        if (!string.IsNullOrEmpty(nestedCondition))
                        {
                            conditionStr = nestedCondition;
                        }
                    }

                    if (!string.IsNullOrEmpty(conditionStr))
                    {
                        Debug.WriteLine($"Adding ALL condition: {conditionStr}");
                        allConditions.Add(conditionStr);
                    }
                }

                if (allConditions.Any())
                {
                    result = $"({string.Join(" && ", allConditions)})";
                }
            }

            // Handle Any conditions
            if (conditions.Any?.Any() == true)
            {
                var anyConditions = new List<string>();
                foreach (var condition in conditions.Any)
                {
                    Debug.WriteLine($"Processing Any condition of type: {condition.GetType().Name}");
                    string? conditionStr = null;

                    if (condition is ComparisonCondition comp)
                    {
                        conditionStr = $"inputs[\"{comp.Sensor}\"] {GetOperator(comp.Operator)} {comp.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
                    }
                    else if (condition is ExpressionCondition expr)
                    {
                        conditionStr = $"({expr.Expression})";
                    }
                    else if (condition is ConditionGroup group)
                    {
                        var nestedCondition = GenerateCondition(group);
                        if (!string.IsNullOrEmpty(nestedCondition))
                        {
                            conditionStr = nestedCondition;
                        }
                    }

                    if (!string.IsNullOrEmpty(conditionStr))
                    {
                        Debug.WriteLine($"Adding ANY condition: {conditionStr}");
                        anyConditions.Add(conditionStr);
                    }
                }

                if (anyConditions.Any())
                {
                    var anyPart = string.Join(" || ", anyConditions);
                    result = result.Length > 0
                        ? $"{result} && {anyPart}"
                        : $"({anyPart})";
                }
            }

            Debug.WriteLine($"Final condition group result: {result}");
            return result.Length > 0 ? result : null;
        }

        private static void GenerateActions(StringBuilder builder, List<ActionDefinition> actions)
        {
            foreach (var action in actions.OfType<SetValueAction>())
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

                builder.AppendLine($"            outputs[\"{action.Key}\"] = {valueAssignment};");
            }
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
                _ => throw new InvalidOperationException($"Unsupported operator: {op}"),
            };
        }
    }
}