// File: Pulsar.Compiler/Parsers/DslParser.cs

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Pulsar.Compiler.Models;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Pulsar.Compiler.Parsers
{
    public class DslParser
    {
        private readonly IDeserializer _deserializer;

        public DslParser()
        {
            _deserializer = new DeserializerBuilder()
                .WithNamingConvention(UnderscoredNamingConvention.Instance) // Change this
                .IgnoreUnmatchedProperties()
                .Build();
        }

        public List<RuleDefinition> ParseRules(string yamlContent, List<string> validSensors)
        {
            var root = _deserializer.Deserialize<RuleRoot>(yamlContent);
            Debug.WriteLine($"Deserialized root: Rules count = {root.Rules.Count}");
            if (root.Rules.Any())
            {
                var firstRule = root.Rules.First();
                Debug.WriteLine(
                    $"First rule: Name = {firstRule.Name}, Actions count = {firstRule.Actions?.Count ?? 0}"
                );
            }

            var ruleDefinitions = new List<RuleDefinition>();

            foreach (var rule in root.Rules)
            {
                // Validate sensors and keys
                ValidateSensors(rule, validSensors);

                // Convert to RuleDefinition
                var ruleDefinition = new RuleDefinition
                {
                    Name = rule.Name,
                    Conditions = ConvertConditions(rule.Conditions),
                    Actions = ConvertActions(rule.Actions),
                };

                ruleDefinitions.Add(ruleDefinition);
            }

            return ruleDefinitions;
        }

        private void ValidateSensors(Rule rule, List<string> validSensors)
        {
            var allSensors = new List<string>();

            Debug.WriteLine($"\nValidating rule: {rule.Name}");
            Debug.WriteLine($"Valid sensors list: {string.Join(", ", validSensors)}");

            // Collect sensors from conditions
            if (rule.Conditions?.All != null)
            {
                foreach (var condition in rule.Conditions.All)
                {
                    Debug.WriteLine(
                        $"Processing All condition: Type = {condition.ConditionDetails.Type}"
                    );
                    if (
                        condition.ConditionDetails.Type == "threshold_over_time"
                        || condition.ConditionDetails.Type == "comparison"
                    )
                    {
                        Debug.WriteLine($"Adding sensor: {condition.ConditionDetails.Sensor}");
                        allSensors.Add(condition.ConditionDetails.Sensor);
                    }
                    else if (condition.ConditionDetails.Type == "expression")
                    {
                        Debug.WriteLine(
                            $"Expression condition: {condition.ConditionDetails.Expression}"
                        );
                        // We might need to parse the expression to extract sensors
                    }
                }
            }

            if (rule.Conditions?.Any != null)
            {
                foreach (var condition in rule.Conditions.Any)
                {
                    Debug.WriteLine(
                        $"Processing Any condition: Type = {condition.ConditionDetails.Type}"
                    );
                    if (
                        condition.ConditionDetails.Type == "threshold_over_time"
                        || condition.ConditionDetails.Type == "comparison"
                    )
                    {
                        Debug.WriteLine($"Adding sensor: {condition.ConditionDetails.Sensor}");
                        allSensors.Add(condition.ConditionDetails.Sensor);
                    }
                    else if (condition.ConditionDetails.Type == "expression")
                    {
                        Debug.WriteLine(
                            $"Expression condition: {condition.ConditionDetails.Expression}"
                        );
                        // We might need to parse the expression to extract sensors
                    }
                }
            }

            // Collect sensors from actions
            if (rule.Actions != null)
            {
                foreach (var actionItem in rule.Actions)
                {
                    Debug.WriteLine("Processing action:");
                    if (actionItem.SetValue != null)
                    {
                        Debug.WriteLine($"Adding SetValue key: {actionItem.SetValue.Key}");
                        allSensors.Add(actionItem.SetValue.Key);
                    }
                    if (actionItem.SendMessage != null)
                    {
                        Debug.WriteLine($"SendMessage channel: {actionItem.SendMessage.Channel}");
                        // Do we need to validate the channel?
                    }
                }
            }

            Debug.WriteLine($"All collected sensors: {string.Join(", ", allSensors)}");

            // Validate sensors against the valid list
            var invalidSensors = allSensors
                .Where(sensor => !validSensors.Contains(sensor))
                .ToList();

            Debug.WriteLine($"Invalid sensors found: {string.Join(", ", invalidSensors)}");

            if (invalidSensors.Any())
                throw new InvalidOperationException(
                    $"Invalid sensors or keys found: {string.Join(", ", invalidSensors)}"
                );
        }

        private IEnumerable<string> GetSensorsFromConditions(List<Condition> conditions)
        {
            foreach (var condition in conditions)
            {
                if (condition.ConditionDetails?.Sensor != null)
                    yield return condition.ConditionDetails.Sensor;
            }
        }

        private ConditionGroup ConvertConditions(ConditionGroupYaml? conditionGroupYaml)
        {
            // Ensure conditionGroupYaml is not null
            conditionGroupYaml ??= new ConditionGroupYaml();

            // Default to empty lists if null
            var allConditions = conditionGroupYaml.All ?? new List<Condition>();
            var anyConditions = conditionGroupYaml.Any ?? new List<Condition>();

            // Perform conversions
            return new ConditionGroup
            {
                All = allConditions.Select(ConvertCondition).ToList(),
                Any = anyConditions.Select(ConvertCondition).ToList(),
            };
        }

        private ConditionDefinition ConvertCondition(Condition condition)
        {
            switch (condition.ConditionDetails.Type)
            {
                case "comparison":
                    return new ComparisonCondition
                    {
                        Sensor = condition.ConditionDetails.Sensor,
                        Operator = ParseOperator(condition.ConditionDetails.Operator),
                        Value = condition.ConditionDetails.Value,
                    };
                case "expression":
                    return new ExpressionCondition
                    {
                        Expression = condition.ConditionDetails.Expression,
                    };
                case "threshold_over_time":
                    return new ThresholdOverTimeCondition
                    {
                        Sensor = condition.ConditionDetails.Sensor,
                        Threshold = condition.ConditionDetails.Value,
                        Duration = condition.ConditionDetails.Duration,
                    };
                default:
                    throw new NotImplementedException(
                        $"Unsupported condition type: {condition.ConditionDetails.Type}"
                    );
            }
        }

        private ComparisonOperator ParseOperator(string op)
        {
            return op switch
            {
                ">" => ComparisonOperator.GreaterThan,
                "<" => ComparisonOperator.LessThan,
                ">=" => ComparisonOperator.GreaterThanOrEqual,
                "<=" => ComparisonOperator.LessThanOrEqual,
                "==" => ComparisonOperator.EqualTo,
                "!=" => ComparisonOperator.NotEqualTo,
                _ => throw new InvalidOperationException($"Unsupported operator: {op}"),
            };
        }

        private List<ActionDefinition> ConvertActions(List<ActionListItem> actions)
        {
            Debug.WriteLine("=== Starting ConvertActions ===");
            Debug.WriteLine($"Number of actions: {actions.Count}");
            foreach (var actionItem in actions)
            {
                Debug.WriteLine($"Action item details:");
                Debug.WriteLine($"  Item is null?: {actionItem == null}");
                Debug.WriteLine($"  SetValue is null?: {actionItem?.SetValue == null}");
                Debug.WriteLine($"  SendMessage is null?: {actionItem?.SendMessage == null}");

                if (actionItem?.SetValue != null)
                {
                    Debug.WriteLine($"  SetValue.Key: {actionItem.SetValue.Key}");
                    Debug.WriteLine(
                        $"  SetValue.ValueExpression: {actionItem.SetValue.ValueExpression}"
                    );
                }
            }

            return actions
                .Select(actionItem =>
                {
                    Debug.WriteLine($"Processing action item");

                    if (actionItem.SetValue != null)
                    {
                        Debug.WriteLine($"Found SetValue action");
                        return new SetValueAction
                            {
                                Key = actionItem.SetValue.Key,
                                ValueExpression = actionItem.SetValue.ValueExpression,
                            } as ActionDefinition;
                    }

                    if (actionItem.SendMessage != null)
                    {
                        Debug.WriteLine($"Found SendMessage action");
                        return new SendMessageAction
                            {
                                Channel = actionItem.SendMessage.Channel,
                                Message = actionItem.SendMessage.Message,
                            } as ActionDefinition;
                    }

                    Debug.WriteLine($"No valid action type found");
                    throw new InvalidOperationException(
                        $"Unknown action type. Action item details: SetValue is {(actionItem.SetValue != null ? "present" : "null")}, SendMessage is {(actionItem.SendMessage != null ? "present" : "null")}"
                    );
                })
                .ToList();
        }
    }

    // Public classes for YAML Parsing
    public class RuleRoot
    {
        public List<Rule> Rules { get; set; } = new();
    }

    public class Rule
    {
        public string Name { get; set; } = string.Empty;
        public ConditionGroupYaml Conditions { get; set; } = new();
        public List<ActionListItem> Actions { get; set; } = new();
    }

    public class ActionListItem
    {
        [YamlMember(Alias = "set_value")]
        public SetValueActionYaml? SetValue { get; set; }

        [YamlMember(Alias = "send_message")]
        public SendMessageActionYaml? SendMessage { get; set; }
    }

    public class ConditionGroupYaml
    {
        public List<Condition> All { get; set; } = new();
        public List<Condition> Any { get; set; } = new();
    }

    public class Condition
    {
        [YamlMember(Alias = "condition")]
        public ConditionInner ConditionDetails { get; set; } = new();
    }

    public class ConditionInner
    {
        public string Type { get; set; } = string.Empty;
        public string Sensor { get; set; } = string.Empty;
        public string Operator { get; set; } = string.Empty;
        public double Value { get; set; }
        public string Expression { get; set; } = string.Empty;
        public int Duration { get; set; }
    }

    public class ActionYaml
    {
        [YamlMember(Alias = "set_value")]
        public SetValueActionYaml? SetValue { get; set; }

        [YamlMember(Alias = "send_message")]
        public SendMessageActionYaml? SendMessage { get; set; }
    }

    public class SetValueActionYaml
    {
        public string Key { get; set; } = string.Empty;
        public string ValueExpression { get; set; } = string.Empty;
    }

    public class SendMessageActionYaml
    {
        [YamlMember(Alias = "channel")]
        public string Channel { get; set; } = string.Empty;

        [YamlMember(Alias = "message")]
        public string Message { get; set; } = string.Empty;
    }
}
