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
    public class CodeGenerator
    {
        public static List<GeneratedFileInfo> GenerateCSharp(List<RuleDefinition> rules)
        {
            var files = new List<GeneratedFileInfo>();
            var layerMap = AssignLayers(rules);
            var rulesByLayer = GetRulesByLayer(rules, layerMap);
            var ruleSourceMap = new Dictionary<string, GeneratedSourceInfo>();

            // Generate layer files first to get line numbers
            foreach (var layer in rulesByLayer)
            {
                var layerFile = GenerateLayerFile(layer.Value, layer.Key);
                
                // Track source info for each rule in this layer
                var lineNumber = 1; // Start after the header
                foreach (var rule in layer.Value)
                {
                    var ruleInfo = new GeneratedSourceInfo
                    {
                        SourceFile = rule.SourceFile,
                        LineNumber = rule.LineNumber,
                        GeneratedFile = layerFile.FileName,
                        GeneratedLineStart = lineNumber,
                        GeneratedLineEnd = lineNumber + CountLinesForRule(rule)
                    };
                    ruleSourceMap[rule.Name] = ruleInfo;
                    lineNumber = ruleInfo.GeneratedLineEnd + 1;
                }
                
                files.Add(layerFile);
            }

            // Generate the coordinator class with source tracking
            var coordinatorFile = GenerateCoordinatorFile(rulesByLayer);
            coordinatorFile.RuleSourceMap = ruleSourceMap;
            files.Insert(0, coordinatorFile);

            return files;
        }

        private static int CountLinesForRule(RuleDefinition rule)
        {
            // Estimate the number of lines this rule will generate
            int lines = 4; // Basic overhead (rule header, debug line)
            
            if (rule.SourceInfo != null)
                lines++;
            
            if (!string.IsNullOrEmpty(rule.Description))
                lines++;

            if (rule.Conditions != null)
            {
                lines += 6; // if statement, debug lines, else block
                if (rule.Conditions.All != null)
                    lines += rule.Conditions.All.Count * 2;
                if (rule.Conditions.Any != null)
                    lines += rule.Conditions.Any.Count * 2;
            }

            lines += rule.Actions.Count * 2; // Each action takes about 2 lines

            return lines;
        }

        private static void WriteFileHeader(StringBuilder builder, string @namespace)
        {
            builder.AppendLine("using System;");
            builder.AppendLine("using System.Collections.Generic;");
            builder.AppendLine("using Pulsar;");
            builder.AppendLine();
            builder.AppendLine($"namespace {@namespace}");
            builder.AppendLine("{");
            builder.AppendLine("    public partial class CompiledRules");
            builder.AppendLine("    {");
        }

        private static void WriteFileFooter(StringBuilder builder)
        {
            builder.AppendLine("    }");
            builder.AppendLine("}");
        }

        private static GeneratedFileInfo GenerateCoordinatorFile(
            Dictionary<int, List<RuleDefinition>> rulesByLayer,
            string @namespace = "Pulsar.Generated"
        )
        {
            var builder = new StringBuilder();
            WriteFileHeader(builder, @namespace);

            builder.AppendLine("        public class RuleCoordinator");
            builder.AppendLine("        {");
            builder.AppendLine(
                "            public void EvaluateRules(Dictionary<string, double> inputs, Dictionary<string, double> outputs, RingBufferManager bufferManager)");
            builder.AppendLine("            {");

            // Call each layer's evaluation method in order
            foreach (var layer in rulesByLayer.Keys.OrderBy(l => l))
            {
                builder.AppendLine($"                EvaluateLayer{layer}(inputs, outputs, bufferManager);");
            }

            builder.AppendLine("            }");
            builder.AppendLine("        }");

            WriteFileFooter(builder);
            var content = builder.ToString();
            return new GeneratedFileInfo
            {
                FileName = "RuleCoordinator.cs",
                FilePath = "Generated/RuleCoordinator.cs",
                Content = content,
                Hash = ComputeHash(content),
                Namespace = @namespace,
                LayerRange = new RuleLayerRange
                {
                    Start = rulesByLayer.Keys.Min(),
                    End = rulesByLayer.Keys.Max()
                }
            };
        }

        private static GeneratedFileInfo GenerateLayerFile(
            List<RuleDefinition> rules, 
            int layer,
            string @namespace = "Pulsar.Generated"
        )
        {
            var builder = new StringBuilder();
            WriteFileHeader(builder, @namespace);

            // Generate the layer class
            builder.AppendLine($"        private void EvaluateLayer{layer}(Dictionary<string, double> inputs, Dictionary<string, double> outputs, RingBufferManager bufferManager)");
            builder.AppendLine("        {");

            foreach (var rule in rules)
            {
                GenerateRuleMethod(builder, rule);
            }

            builder.AppendLine("        }");

            WriteFileFooter(builder);
            var content = builder.ToString();
            return new GeneratedFileInfo
            {
                FileName = $"RuleGroup_{layer}.cs",
                FilePath = $"Generated/RuleGroup_{layer}.cs",
                Content = content,
                Hash = ComputeHash(content),
                Namespace = @namespace,
                LayerRange = new RuleLayerRange { Start = layer, End = layer }
            };
        }

        private static void GenerateRuleMethod(StringBuilder builder, RuleDefinition rule)
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
                        conditionStr = $"{comp.Sensor} {GetOperator(comp.Operator)} {comp.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
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

            // Handle Any conditions
            if (conditions.Any?.Any() == true)
            {
                var anyConditions = new List<string>();
                foreach (var condition in conditions.Any)
                {
                    string? conditionStr = null;

                    if (condition is ComparisonCondition comp)
                    {
                        conditionStr = $"{comp.Sensor} {GetOperator(comp.Operator)} {comp.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
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

        public static string FixupExpression(string expression)
        {
            // Replace any unsupported operators or functions with C# equivalents
            expression = expression.Replace("^", "Math.Pow");
            expression = expression.Replace("sqrt", "Math.Sqrt");
            expression = expression.Replace("abs", "Math.Abs");
            expression = expression.Replace("sin", "Math.Sin");
            expression = expression.Replace("cos", "Math.Cos");
            expression = expression.Replace("tan", "Math.Tan");
            expression = expression.Replace("log", "Math.Log");
            expression = expression.Replace("exp", "Math.Exp");
            expression = expression.Replace("floor", "Math.Floor");
            expression = expression.Replace("ceil", "Math.Ceiling");
            expression = expression.Replace("round", "Math.Round");

            // Handle power operator special case (a^b -> Math.Pow(a,b))
            var powerRegex = new Regex(@"Math\.Pow\(([^,]+)\)");
            expression = powerRegex.Replace(expression, match =>
            {
                var arg = match.Groups[1].Value;
                var parts = arg.Split('^');
                if (parts.Length == 2)
                {
                    return $"Math.Pow({parts[0].Trim()}, {parts[1].Trim()})";
                }
                return match.Value;
            });

            return expression;
        }

        private static string ComputeHash(string content)
        {
            using var sha = System.Security.Cryptography.SHA256.Create();
            var bytes = System.Text.Encoding.UTF8.GetBytes(content);
            var hash = sha.ComputeHash(bytes);
            return Convert.ToBase64String(hash);
        }
    }
}
