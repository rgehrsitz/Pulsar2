using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
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

            foreach (var rule in rules)
            {
                builder.AppendLine($"        // Rule: {rule.Name}");

                string? condition = GenerateCondition(rule.Conditions);
                Debug.WriteLine($"Generated condition: {condition}");

                if (!string.IsNullOrEmpty(condition))
                {
                    builder.AppendLine($"        if ({condition})");
                    builder.AppendLine("        {");
                    GenerateActions(builder, rule.Actions);
                    builder.AppendLine("        }");
                }
                else
                {
                    GenerateActions(builder, rule.Actions);
                }
            }

            builder.AppendLine("    }");
            builder.AppendLine();
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
                        conditionStr =
                            $"inputs[\"{comp.Sensor}\"] {GetOperator(comp.Operator)} {comp.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
                    }
                    else if (condition is ExpressionCondition expr)
                    {
                        // Expression needs parentheses if it:
                        // - Contains function calls (Math.)
                        // - Contains arithmetic operators (+ - * /)
                        // - Contains multiple conditions (&& ||)
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
                        conditionStr =
                            $"inputs[\"{comp.Sensor}\"] {GetOperator(comp.Operator)} {comp.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
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
