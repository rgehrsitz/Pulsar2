// File: Pulsar.Compiler/CodeGenerator.cs

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Pulsar.Compiler.Models;
using Pulsar.Compiler.Parsers;

namespace Pulsar.Compiler.Generation
{
    public class RuleGroupingConfig
    {
        // Default values chosen based on typical C# file size guidelines
        public int MaxRulesPerFile { get; set; } = 100;
        public int MaxLinesPerFile { get; set; } = 1000;
        public bool GroupParallelRules { get; set; } = true;
    }

    public class CodeGenerator
    {
        public static List<GeneratedFileInfo> GenerateCSharp(List<RuleDefinition> rules, RuleGroupingConfig? config = null)
        {
            config ??= new RuleGroupingConfig();
            if (rules == null)
            {
                throw new ArgumentNullException(nameof(rules));
            }

            // Handle empty rules list by generating minimal valid code
            if (!rules.Any())
            {
                return new List<GeneratedFileInfo>
        {
            GenerateEmptyRulesFile()
        };
            }

            var layerMap = AssignLayers(rules);
            var rulesByLayer = GetRulesByLayer(rules, layerMap);
            var files = new List<GeneratedFileInfo>();

            // Generate primary rule class
            var coordinatorFile = GenerateRuleCoordinatorClass(rulesByLayer);
            files.Add(coordinatorFile);

            // Generate layer files
            foreach (var layer in rulesByLayer)
            {
                var layerFile = GenerateRuleLayerClass(layer.Key, layer.Value);
                files.Add(layerFile);
            }

            return files;
        }

        private static GeneratedFileInfo GenerateEmptyRulesFile()
        {
            var builder = new StringBuilder();
            builder.AppendLine(GenerateCommonUsings());
            builder.AppendLine("namespace Pulsar.Generated");
            builder.AppendLine("{");
            builder.AppendLine("    public partial class CompiledRules");
            builder.AppendLine("    {");
            builder.AppendLine("        public void Evaluate(Dictionary<string, double> inputs, Dictionary<string, double> outputs, RingBufferManager bufferManager)");
            builder.AppendLine("        {");
            builder.AppendLine("            // No rules to evaluate");
            builder.AppendLine("        }");
            builder.AppendLine("    }");
            builder.AppendLine("}");

            return new GeneratedFileInfo
            {
                FileName = "RuleCoordinator.cs",
                FilePath = "Generated/RuleCoordinator.cs",
                Content = builder.ToString(),
                Hash = ComputeHash(builder.ToString()),
                Namespace = "Pulsar.Generated",
                LayerRange = new RuleLayerRange { Start = 0, End = 0 },
                ContainedRules = new List<string>()
            };
        }

        private static GeneratedFileInfo GenerateRuleCoordinatorClass(Dictionary<int, List<RuleDefinition>> rulesByLayer)
        {
            var builder = new StringBuilder();
            builder.AppendLine(GenerateFileHeader());
            builder.AppendLine("namespace Pulsar.Runtime.Rules");
            builder.AppendLine("{");
            builder.AppendLine("    /// <summary>");
            builder.AppendLine("    /// Generated rule coordinator - handles evaluation of all rule layers");
            builder.AppendLine("    /// </summary>");
            builder.AppendLine("    public partial class RuleCoordinator : IRuleCoordinator");
            builder.AppendLine("    {");
            builder.AppendLine("        private readonly ILogger _logger;");
            builder.AppendLine("        private readonly RingBufferManager _bufferManager;");
            builder.AppendLine();
            builder.AppendLine("        public RuleCoordinator(ILogger logger, RingBufferManager bufferManager)");
            builder.AppendLine("        {");
            builder.AppendLine("            _logger = logger;");
            builder.AppendLine("            _bufferManager = bufferManager;");
            builder.AppendLine("        }");
            builder.AppendLine();
            builder.AppendLine("        public void EvaluateRules(Dictionary<string, double> inputs, Dictionary<string, double> outputs)");
            builder.AppendLine("        {");

            // Call each layer in order
            foreach (var layer in rulesByLayer.Keys.OrderBy(k => k))
            {
                builder.AppendLine($"            EvaluateLayer{layer}(inputs, outputs);");
            }

            builder.AppendLine("        }");
            builder.AppendLine("    }");
            builder.AppendLine("}");

            return new GeneratedFileInfo
            {
                FileName = "RuleCoordinator.cs",
                FilePath = "Generated/RuleCoordinator.cs",
                Content = builder.ToString(),
                Hash = ComputeHash(builder.ToString()),
                Namespace = "Pulsar.Runtime.Rules",
                LayerRange = new RuleLayerRange { Start = 0, End = rulesByLayer.Keys.Max() },
                ContainedRules = new List<string>()
            };
        }

        private static GeneratedFileInfo GenerateRuleLayerClass(int layer, List<RuleDefinition> rules)
        {
            var builder = new StringBuilder();
            builder.AppendLine(GenerateFileHeader());
            builder.AppendLine("namespace Pulsar.Runtime.Rules");
            builder.AppendLine("{");
            builder.AppendLine("    public partial class RuleCoordinator");
            builder.AppendLine("    {");
            builder.AppendLine($"        private void EvaluateLayer{layer}(Dictionary<string, double> inputs, Dictionary<string, double> outputs)");
            builder.AppendLine("        {");

            foreach (var rule in rules)
            {
                builder.AppendLine($"            // Rule: {rule.Name}");
                if (rule.SourceInfo != null)
                {
                    builder.AppendLine($"            // Source: {rule.SourceInfo}");
                }
                if (!string.IsNullOrEmpty(rule.Description))
                {
                    builder.AppendLine($"            // Description: {rule.Description}");
                }

                string? condition = GenerateCondition(rule.Conditions);
                if (!string.IsNullOrEmpty(condition))
                {
                    builder.AppendLine($"            if ({condition})");
                    builder.AppendLine("            {");
                    GenerateActions(builder, rule.Actions);
                    builder.AppendLine("            }");
                }
                else
                {
                    GenerateActions(builder, rule.Actions);
                }
                builder.AppendLine();
            }

            builder.AppendLine("        }");
            builder.AppendLine("    }");
            builder.AppendLine("}");

            return new GeneratedFileInfo
            {
                FileName = $"RuleLayer{layer}.cs",
                FilePath = $"Generated/RuleLayer{layer}.cs",
                Content = builder.ToString(),
                Hash = ComputeHash(builder.ToString()),
                Namespace = "Pulsar.Runtime.Rules",
                LayerRange = new RuleLayerRange { Start = layer, End = layer },
                ContainedRules = rules.Select(r => r.Name).ToList()
            };
        }

        private static string GenerateFileHeader()
        {
            return @"// <auto-generated>
// This file was generated by the Pulsar Rule Compiler.
// Do not edit directly - any changes will be overwritten.
// </auto-generated>

using System;
using System.Collections.Generic;
using Serilog;
using Pulsar.Runtime.Buffers;
";
        }

        private static void GenerateRuleEvaluation(StringBuilder builder, RuleDefinition rule)
        {
            // Add source tracing comment with file info
            builder.AppendLine($"        // Rule: {rule.Name}");
            if (rule.SourceInfo != null)
            {
                builder.AppendLine($"        // Source: {rule.SourceInfo}");
            }
            if (!string.IsNullOrEmpty(rule.Description))
            {
                builder.AppendLine($"        // Description: {rule.Description}");
            }

            builder.AppendLine(
                $"        System.Diagnostics.Debug.WriteLine(\"Evaluating rule: {rule.Name}\");"
            );

            string? condition = GenerateCondition(rule.Conditions);
            System.Diagnostics.Debug.WriteLine($"Generated condition: {condition}");

            if (!string.IsNullOrEmpty(condition))
            {
                // Rest of the existing condition and action generation code remains the same
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

        private static Dictionary<int, List<RuleDefinition>> GetRulesByLayer(
            List<RuleDefinition> rules,
            Dictionary<string, int> layerMap
        )
        {
            var rulesByLayer = new Dictionary<int, List<RuleDefinition>>();

            // Group rules by their assigned layer
            foreach (var rule in rules)
            {
                var layer = layerMap[rule.Name];
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

        private static Dictionary<string, int> AssignLayers(List<RuleDefinition> rules)
        {
            var dependencies = new Dictionary<string, HashSet<string>>();
            var outputToRule = new Dictionary<string, string>();
            var layers = new Dictionary<string, int>();
            var visited = new HashSet<string>();

            // First, build a map of outputs to rules
            foreach (var rule in rules)
            {
                dependencies[rule.Name] = new HashSet<string>();
                foreach (var action in rule.Actions.OfType<SetValueAction>())
                {
                    outputToRule[action.Key] = rule.Name;
                }
            }

            // Then analyze dependencies to determine layers
            foreach (var rule in rules)
            {
                // Check conditions for dependencies
                if (rule.Conditions != null)
                {
                    foreach (var condition in GetAllConditions(rule.Conditions))
                    {
                        if (condition is ComparisonCondition comp && outputToRule.ContainsKey(comp.Sensor))
                        {
                            dependencies[rule.Name].Add(outputToRule[comp.Sensor]);
                        }
                    }
                }
            }

            foreach (var rule in rules)
            {
                if (!visited.Contains(rule.Name))
                {
                    AssignLayersDFS(rule.Name, dependencies, layers, visited, new HashSet<string>());
                }
            }

            return layers;
        }

        private static string? GenerateCondition(ConditionGroup? conditions)
        {
            if (conditions == null)
                return null;

            var result = "";

            // Handle All conditions first
            if (conditions.All?.Any() == true)
            {
                var allConditions = new List<string>();
                foreach (var condition in conditions.All)
                {
                    string? conditionStr = null;

                    if (condition is ComparisonCondition comp)
                    {
                        conditionStr = $"inputs[\"{comp.Sensor}\"] {GetOperator(comp.Operator)} {comp.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
                    }
                    else if (condition is ThresholdOverTimeCondition temporal)
                    {
                        var duration = $"TimeSpan.FromMilliseconds({temporal.Duration})";
                        conditionStr = $"bufferManager.IsAboveThresholdForDuration(\"{temporal.Sensor}\", {temporal.Threshold}, {duration})";
                    }
                    else if (condition is ExpressionCondition expr)
                    {
                        conditionStr = FixupExpression(expr.Expression);
                    }

                    if (!string.IsNullOrEmpty(conditionStr))
                    {
                        allConditions.Add(conditionStr);
                    }
                }

                if (allConditions.Any())
                {
                    result = string.Join(" && ", allConditions);
                }
            }

            // Handle Any conditions
            if (conditions.Any?.Any() == true)
            {
                var anyConditions = new List<string>();
                foreach (var condition in conditions.Any)
                {
                    string? conditionStr = null;

                    if (condition is ComparisonCondition comp)
                    {
                        conditionStr = $"inputs[\"{comp.Sensor}\"] {GetOperator(comp.Operator)} {comp.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
                    }
                    else if (condition is ThresholdOverTimeCondition temporal)
                    {
                        var duration = $"TimeSpan.FromMilliseconds({temporal.Duration})";
                        conditionStr = $"bufferManager.IsAboveThresholdForDuration(\"{temporal.Sensor}\", {temporal.Threshold}, {duration})";
                    }
                    else if (condition is ExpressionCondition expr)
                    {
                        conditionStr = FixupExpression(expr.Expression);
                    }

                    if (!string.IsNullOrEmpty(conditionStr))
                    {
                        anyConditions.Add(conditionStr);
                    }
                }

                if (anyConditions.Any())
                {
                    var anyPart = string.Join(" || ", anyConditions);
                    result = result.Length > 0 ? $"{result} && ({anyPart})" : anyPart;
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

        internal static string FixupExpression(string expression)
        {
            if (string.IsNullOrEmpty(expression))
            {
                return expression;
            }

            // List of Math functions to preserve
            var mathFunctions = new[]
            {
                "Math.Abs", "Math.Pow", "Math.Sqrt", "Math.Sin", "Math.Cos", "Math.Tan",
                "Math.Log", "Math.Exp", "Math.Floor", "Math.Ceiling", "Math.Round"
            };

            // First handle Math function calls
            foreach (var func in mathFunctions)
            {
                expression = expression.Replace(func.ToLower(), func);
            }

            // Wrap variable references in inputs[] dictionary access
            var wrappedExpression = Regex.Replace(
                expression,
                @"\b(?!Math\.)([a-zA-Z_][a-zA-Z0-9_]*)\b(?!\[|\()",
                "inputs[\"$1\"]"
            );

            // Replace power operator with Math.Pow
            wrappedExpression = Regex.Replace(
                wrappedExpression,
                @"(\w+|\))\s*\^(\s*\w+|\s*\d+(\.\d+)?|\s*\()",
                "Math.Pow($1,$2)"
            );

            // Add parentheses around complex expressions
            if (wrappedExpression.Contains(" ") || wrappedExpression.Contains("("))
            {
                wrappedExpression = $"({wrappedExpression})";
            }

            return wrappedExpression;
        }

        private static string ComputeHash(string content)
        {
            using var sha = System.Security.Cryptography.SHA256.Create();
            var bytes = System.Text.Encoding.UTF8.GetBytes(content);
            var hash = sha.ComputeHash(bytes);
            return Convert.ToBase64String(hash);
        }

        private static string GenerateCommonUsings()
        {
            return @"using System;
using System.Collections.Generic;
using System.Linq;
using Pulsar.Runtime;
using Pulsar.Runtime.Common;
using Pulsar.Runtime.Buffers;
";
        }
    }
}
