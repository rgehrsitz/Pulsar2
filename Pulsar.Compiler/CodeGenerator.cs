// File: Pulsar.Compiler/CodeGenerator.cs

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Pulsar.Compiler.Models;

namespace Pulsar.Compiler.Generation
{
    public class CodeGenerator
    {
        private static Dictionary<string, RuleDefinition> _outputs = new();

        public static string GenerateCSharp(List<RuleDefinition> sortedRules)
        {
            // Store outputs from rules
            _outputs.Clear();
            foreach (var rule in sortedRules)
            {
                foreach (var action in rule.Actions.OfType<SetValueAction>())
                {
                    _outputs[action.Key] = rule;
                }
            }
            ArgumentNullException.ThrowIfNull(sortedRules, nameof(sortedRules));

            var codeBuilder = new StringBuilder();
            codeBuilder.AppendLine("using System;");
            codeBuilder.AppendLine("using System.Collections.Generic;");
            codeBuilder.AppendLine();
            codeBuilder.AppendLine("public class CompiledRules");
            codeBuilder.AppendLine("{");

            // Generate main evaluation method
            codeBuilder.AppendLine(
                "    public void Evaluate(Dictionary<string, double> inputs, Dictionary<string, double> outputs)"
            );
            codeBuilder.AppendLine("    {");

            // Get distinct layers
            var rulesByLayer = GetRulesByLayer(sortedRules);
            var layers = rulesByLayer.Keys.OrderBy(l => l).ToList();

            // Call each layer's evaluation method in order
            foreach (var layer in layers)
            {
                codeBuilder.AppendLine($"        EvaluateLayer{layer}(inputs, outputs);");
            }
            codeBuilder.AppendLine("    }");
            codeBuilder.AppendLine();

            // Generate individual layer methods
            foreach (var layer in layers)
            {
                var layerRules = rulesByLayer[layer];
                GenerateLayerMethod(codeBuilder, layer, layerRules);
            }

            codeBuilder.AppendLine("}");

            var finalCode = codeBuilder.ToString();
            Debug.WriteLine("\n==== COMPLETE GENERATED CODE START ====");
            Debug.WriteLine(finalCode);
            Debug.WriteLine("==== COMPLETE GENERATED CODE END ====");

            return finalCode;
        }

        private static Dictionary<int, List<RuleDefinition>> GetRulesByLayer(
            List<RuleDefinition> sortedRules
        )
        {
            var rulesByLayer = new Dictionary<int, List<RuleDefinition>>();

            // Analyze dependencies to determine layers
            var dependencies = new Dictionary<string, HashSet<string>>();
            var outputProducers = new Dictionary<string, RuleDefinition>();

            // First pass: collect all outputs and their producers
            foreach (var rule in sortedRules)
            {
                foreach (var action in rule.Actions.OfType<SetValueAction>())
                {
                    outputProducers[action.Key] = rule;
                }
            }

            // Second pass: build dependency graph
            foreach (var rule in sortedRules)
            {
                dependencies[rule.Name] = new HashSet<string>();

                // Check conditions for dependencies
                if (rule.Conditions != null)
                {
                    foreach (var condition in GetAllConditions(rule.Conditions))
                    {
                        if (condition is ComparisonCondition comp)
                        {
                            if (outputProducers.TryGetValue(comp.Sensor, out var producer))
                            {
                                dependencies[rule.Name].Add(producer.Name);
                            }
                        }
                    }
                }
            }

            // Assign layers based on longest path from root
            var layers = AssignLayers(dependencies);

            // Group rules by their assigned layer
            foreach (var rule in sortedRules)
            {
                var layer = layers[rule.Name];
                if (!rulesByLayer.ContainsKey(layer))
                {
                    rulesByLayer[layer] = new List<RuleDefinition>();
                }
                rulesByLayer[layer].Add(rule);
            }

            return rulesByLayer;
        }

        private static void AssignLayersDFS(
            string rule,
            Dictionary<string, HashSet<string>> dependencies,
            Dictionary<string, int> layers,
            HashSet<string> visited,
            HashSet<string> currentPath
        )
        {
            if (currentPath.Contains(rule))
            {
                throw new InvalidOperationException(
                    $"Cyclic dependency detected involving rule {rule}"
                );
            }

            if (visited.Contains(rule))
            {
                return;
            }

            currentPath.Add(rule);

            int maxDependencyLayer = -1;
            foreach (var dep in dependencies[rule])
            {
                if (!layers.ContainsKey(dep))
                {
                    AssignLayersDFS(dep, dependencies, layers, visited, currentPath);
                }
                maxDependencyLayer = Math.Max(maxDependencyLayer, layers[dep]);
            }

            layers[rule] = maxDependencyLayer + 1;
            visited.Add(rule);
            currentPath.Remove(rule);
        }

        private static void GenerateLayerMethod(
            StringBuilder builder,
            int layer,
            List<RuleDefinition> rules
        )
        {
            builder.AppendLine(
                $"    private void EvaluateLayer{layer}(Dictionary<string, double> inputs, Dictionary<string, double> outputs)"
            );
            builder.AppendLine("    {");

            // Add debug output at start of layer
            builder.AppendLine(
                $"        System.Diagnostics.Debug.WriteLine(\"Evaluating Layer {layer}\");"
            );
            builder.AppendLine("        System.Diagnostics.Debug.WriteLine(\"Current inputs:\");");
            builder.AppendLine("        foreach(var kvp in inputs)");
            builder.AppendLine("        {");
            builder.AppendLine(
                "            System.Diagnostics.Debug.WriteLine($\"  {kvp.Key}: {kvp.Value}\");"
            );
            builder.AppendLine("        }");

            foreach (var rule in rules)
            {
                builder.AppendLine($"        // Rule: {rule.Name}");
                builder.AppendLine(
                    $"        System.Diagnostics.Debug.WriteLine(\"Evaluating rule: {rule.Name}\");"
                );

                string? condition = GenerateCondition(rule.Conditions);
                System.Diagnostics.Debug.WriteLine($"Generated condition: {condition}");

                if (!string.IsNullOrEmpty(condition))
                {
                    // For the debug line, escape the condition string properly
                    var escapedCondition = condition.Replace("\"", "\\\"");
                    builder.AppendLine(
                        $"        System.Diagnostics.Debug.WriteLine(\"Checking condition: {escapedCondition}\");"
                    );

                    builder.AppendLine($"        if ({condition})");
                    builder.AppendLine("        {");
                    builder.AppendLine(
                        "            System.Diagnostics.Debug.WriteLine(\"Condition is true, executing actions\");"
                    );
                    GenerateActions(builder, rule.Actions);
                    builder.AppendLine("        }");
                    builder.AppendLine("        else");
                    builder.AppendLine("        {");
                    builder.AppendLine(
                        "            System.Diagnostics.Debug.WriteLine(\"Condition is false, skipping actions\");"
                    );
                    builder.AppendLine("        }");
                }
                else
                {
                    builder.AppendLine(
                        "            System.Diagnostics.Debug.WriteLine(\"No conditions, executing actions directly\");"
                    );
                    GenerateActions(builder, rule.Actions);
                }
            }

            // Add debug output at end of layer
            builder.AppendLine("        System.Diagnostics.Debug.WriteLine(\"Current outputs:\");");
            builder.AppendLine("        foreach(var kvp in outputs)");
            builder.AppendLine("        {");
            builder.AppendLine(
                "            System.Diagnostics.Debug.WriteLine($\"  {kvp.Key}: {kvp.Value}\");"
            );
            builder.AppendLine("        }");

            builder.AppendLine("    }");
            builder.AppendLine();
        }

        public static string FixupExpression(string expression)
        {
            var pattern =
                @"(?<function>Math\.\w+)|(?<number>\d*\.?\d+)|(?<operator>[+\-*/><]=?|==|!=)|(?<variable>[a-zA-Z_][a-zA-Z0-9_]*)|(?<parentheses>[\(\)])";

            var tokenized = new List<string>();
            int position = 0;

            foreach (Match match in Regex.Matches(expression, pattern))
            {
                // Add any skipped characters (spaces typically)
                if (match.Index > position)
                {
                    tokenized.Add(expression.Substring(position, match.Index - position));
                }

                var token = match.Value;

                if (match.Groups["function"].Success)
                {
                    // Math functions remain unchanged
                    tokenized.Add(token);
                }
                else if (match.Groups["number"].Success)
                {
                    // Numbers remain unchanged
                    tokenized.Add(token);
                }
                else if (match.Groups["operator"].Success)
                {
                    // Operators remain unchanged
                    tokenized.Add(token);
                }
                else if (match.Groups["variable"].Success)
                {
                    // Check if it's a Math function argument
                    bool isAfterMathDot = false;
                    if (match.Index >= 5)
                    {
                        var precedingText = expression.Substring(match.Index - 5, 5);
                        isAfterMathDot = precedingText.Equals("Math.", StringComparison.Ordinal);
                    }

                    if (!isAfterMathDot)
                    {
                        // Check if this is a computed value
                        if (_outputs.ContainsKey(token))
                        {
                            tokenized.Add($"outputs[\"{token}\"]");
                        }
                        else
                        {
                            tokenized.Add($"inputs[\"{token}\"]");
                        }
                    }
                    else
                    {
                        tokenized.Add(token);
                    }
                }
                else if (match.Groups["parentheses"].Success)
                {
                    // Parentheses remain unchanged
                    tokenized.Add(token);
                }

                position = match.Index + match.Length;
            }

            // Add any remaining characters
            if (position < expression.Length)
            {
                tokenized.Add(expression.Substring(position));
            }

            var result = string.Join("", tokenized).Trim();
            return NeedsParentheses(result) ? $"({result})" : result;
        }

        private static bool NeedsParentheses(string expression)
        {
            // Add parentheses if the expression:
            // 1. Contains certain operators (+, -, *, /) AND comparison operators
            // 2. Contains Math functions
            // 3. Contains multiple comparison operators
            var hasArithmetic = Regex.IsMatch(expression, @"[+\-*/]");
            var hasComparison = Regex.IsMatch(expression, @"[<>]=?|==|!=");
            var hasMathFunction = expression.Contains("Math.");
            var hasMultipleComparisons = Regex.Matches(expression, @"[<>]=?|==|!=").Count > 1;

            return (hasArithmetic && hasComparison) || hasMathFunction || hasMultipleComparisons;
        }

        public static class ExpressionHelper
        {
            public static bool ValidateGeneratedExpression(string expression)
            {
                try
                {
                    // Try to compile the expression
                    var code =
                        $@"
                using System;
                using System.Collections.Generic;
                public class Test {{
                    public bool Evaluate(Dictionary<string, double> inputs) {{
                        return {expression};
                    }}
                }}";

                    var tree = Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(code);
                    var diagnostics = tree.GetDiagnostics();
                    return !diagnostics.Any(d =>
                        d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error
                    );
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Expression validation failed: {ex.Message}");
                    return false;
                }
            }
        }

        private static string? GenerateCondition(ConditionGroup? conditions)
        {
            if (conditions == null)
                return null;

            Debug.WriteLine("Processing ConditionGroup:");
            Debug.WriteLine($"  All conditions count: {conditions.All?.Count ?? 0}");
            Debug.WriteLine($"  Any conditions count: {conditions.Any?.Count ?? 0}");

            var result = "";

            // Handle All conditions first
            if (conditions.All?.Any() == true)
            {
                var allConditions = new List<string>();
                foreach (var condition in conditions.All)
                {
                    Debug.WriteLine(
                        $"Processing All condition of type: {condition.GetType().Name}"
                    );
                    string? conditionStr = null;

                    if (condition is ComparisonCondition comp)
                    {
                        // Check if this sensor is a computed value
                        if (_outputs.ContainsKey(comp.Sensor))
                        {
                            conditionStr = $"outputs[\"{comp.Sensor}\"]";
                        }
                        else
                        {
                            conditionStr = $"inputs[\"{comp.Sensor}\"]";
                        }
                        conditionStr +=
                            $" {GetOperator(comp.Operator)} {comp.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
                    }
                    else if (condition is ExpressionCondition expr)
                    {
                        var fixedExpression = FixupExpression(expr.Expression);
                        bool needsParentheses =
                            fixedExpression.Contains("Math.")
                            || fixedExpression.Contains("+")
                            || fixedExpression.Contains("-")
                            || fixedExpression.Contains("*")
                            || fixedExpression.Contains("/")
                            || fixedExpression.Contains("&&")
                            || fixedExpression.Contains("||");

                        conditionStr = needsParentheses ? $"({fixedExpression})" : fixedExpression;
                        Debug.WriteLine(
                            $"Expression '{expr.Expression}' converted to '{conditionStr}'"
                        );
                    }
                    else if (condition is ConditionGroup group)
                    {
                        conditionStr = GenerateCondition(group);
                        // Always parenthesize nested group results
                        if (!string.IsNullOrEmpty(conditionStr))
                        {
                            conditionStr = $"({conditionStr})";
                        }
                    }

                    if (!string.IsNullOrEmpty(conditionStr))
                    {
                        allConditions.Add(conditionStr);
                    }
                }

                if (allConditions.Any())
                {
                    result = string.Join(" && ", allConditions);
                    // Parenthesize ALL group if it's nested and contains multiple conditions
                    if (conditions.Parent != null && allConditions.Count > 1)
                    {
                        result = $"({result})";
                    }
                }
            }

            // Handle Any conditions with the same expression handling
            if (conditions.Any?.Any() == true)
            {
                var anyConditions = new List<string>();
                foreach (var condition in conditions.Any)
                {
                    string? conditionStr = null;

                    if (condition is ComparisonCondition comp)
                    {
                        // Check if this sensor is a computed value
                        if (_outputs.ContainsKey(comp.Sensor))
                        {
                            conditionStr = $"outputs[\"{comp.Sensor}\"]";
                        }
                        else
                        {
                            conditionStr = $"inputs[\"{comp.Sensor}\"]";
                        }
                        conditionStr +=
                            $" {GetOperator(comp.Operator)} {comp.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
                    }
                    else if (condition is ExpressionCondition expr)
                    {
                        // Same expression complexity check for ANY conditions
                        bool needsParentheses =
                            expr.Expression.Contains("Math.")
                            || expr.Expression.Contains("+")
                            || expr.Expression.Contains("-")
                            || expr.Expression.Contains("*")
                            || expr.Expression.Contains("/")
                            || expr.Expression.Contains("&&")
                            || expr.Expression.Contains("||");

                        conditionStr = needsParentheses ? $"({expr.Expression})" : expr.Expression;
                        Debug.WriteLine(
                            $"Expression '{expr.Expression}' {(needsParentheses ? "needs" : "does not need")} parentheses"
                        );
                    }
                    else if (condition is ConditionGroup group)
                    {
                        conditionStr = GenerateCondition(group);
                        // Always parenthesize nested group results within ANY
                        if (!string.IsNullOrEmpty(conditionStr))
                        {
                            conditionStr = $"({conditionStr})";
                        }
                    }

                    if (!string.IsNullOrEmpty(conditionStr))
                    {
                        anyConditions.Add(conditionStr);
                    }
                }

                if (anyConditions.Any())
                {
                    var anyPart = string.Join(" || ", anyConditions);
                    // Always parenthesize ANY groups that are part of a larger condition
                    if (result.Length > 0 || conditions.Parent != null)
                    {
                        anyPart = $"({anyPart})";
                    }
                    result = result.Length > 0 ? $"{result} && {anyPart}" : anyPart;
                }
            }

            return result.Length > 0 ? result : null;
        }

        private static IEnumerable<ConditionDefinition> GetAllConditions(ConditionGroup group)
        {
            var conditions = new List<ConditionDefinition>();
            if (group.All != null)
                conditions.AddRange(group.All);
            if (group.Any != null)
                conditions.AddRange(group.Any);
            return conditions;
        }

        private static Dictionary<string, int> AssignLayers(
            Dictionary<string, HashSet<string>> dependencies
        )
        {
            var layers = new Dictionary<string, int>();
            var visited = new HashSet<string>();

            foreach (var rule in dependencies.Keys)
            {
                if (!visited.Contains(rule))
                {
                    AssignLayersDFS(rule, dependencies, layers, visited, new HashSet<string>());
                }
            }

            return layers;
        }

        private static void GenerateActions(StringBuilder builder, List<ActionDefinition> actions)
        {
            foreach (var action in actions.OfType<SetValueAction>())
            {
                string valueAssignment;
                if (action.Value.HasValue)
                {
                    valueAssignment = action.Value.Value.ToString(
                        System.Globalization.CultureInfo.InvariantCulture
                    );
                }
                else if (!string.IsNullOrEmpty(action.ValueExpression))
                {
                    valueAssignment = FixupExpression(action.ValueExpression);
                }
                else
                {
                    valueAssignment = "0";
                }

                // Properly escape quotation marks for debug output
                var escapedAssignment = valueAssignment.Replace("\"", "\\\"");
                builder.AppendLine(
                    $"            System.Diagnostics.Debug.WriteLine(\"Setting {action.Key} to {escapedAssignment}\");"
                );
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
