// File: Pulsar.Compiler/Parsers/DslParser.cs

using System;
using System.Collections.Generic;
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
                .WithNamingConvention(CamelCaseNamingConvention.Instance)
                .Build();
        }

        public List<RuleDefinition> ParseRules(string yamlContent, List<string> validSensors)
        {
            var root = _deserializer.Deserialize<RuleRoot>(yamlContent);

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

            // Collect sensors from conditions
            if (rule.Conditions?.All != null)
                allSensors.AddRange(GetSensorsFromConditions(rule.Conditions.All));

            if (rule.Conditions?.Any != null)
                allSensors.AddRange(GetSensorsFromConditions(rule.Conditions.Any));

            // Collect sensors from actions
            if (rule.Actions != null)
                allSensors.AddRange(
                    rule.Actions.Where(a => a.SetValue != null).Select(a => a.SetValue.Key)
                );

            // Validate sensors against the valid list
            var invalidSensors = allSensors
                .Where(sensor => !validSensors.Contains(sensor))
                .ToList();

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
            if (condition.ConditionDetails.Type == "comparison")
            {
                return new ComparisonCondition
                {
                    Sensor = condition.ConditionDetails.Sensor,
                    Operator = ParseOperator(condition.ConditionDetails.Operator),
                    Value = condition.ConditionDetails.Value,
                };
            }

            throw new NotImplementedException(
                $"Unsupported condition type: {condition.ConditionDetails.Type}"
            );
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

        private List<ActionDefinition> ConvertActions(List<ActionYaml> actions)
        {
            return actions
                .Select(action =>
                {
                    if (action.SetValue != null)
                    {
                        return (ActionDefinition)
                            new SetValueAction
                            {
                                Key = action.SetValue.Key,
                                ValueExpression = action.SetValue.ValueExpression,
                            };
                    }

                    throw new NotImplementedException("Unsupported action type");
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
        public List<ActionYaml> Actions { get; set; } = new();
    }

    public class ConditionGroupYaml
    {
        public List<Condition> All { get; set; } = new();
        public List<Condition> Any { get; set; } = new();
    }

    public class Condition
    {
        public ConditionInner ConditionDetails { get; set; } = new();
    }

    public class ConditionInner
    {
        public string Type { get; set; } = string.Empty;
        public string Sensor { get; set; } = string.Empty;
        public string Operator { get; set; } = string.Empty;
        public double Value { get; set; }
    }

    public class ActionYaml
    {
        public SetValueActionYaml SetValue { get; set; } = new();
    }

    public class SetValueActionYaml
    {
        public string Key { get; set; } = string.Empty;
        public string ValueExpression { get; set; } = string.Empty;
    }
}
