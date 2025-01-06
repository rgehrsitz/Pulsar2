// File: Pulsar.Compiler/Models/RuleDefinition.cs

using System;
using System.Collections.Generic;

namespace Pulsar.Compiler.Models
{
    public class RuleDefinition
    {
        public string Name { get; set; } = string.Empty; // Default value to avoid null
        public string? Description { get; set; } // Nullable since it's optional
        public ConditionGroup Conditions { get; set; } = new ConditionGroup();
        public List<ActionDefinition> Actions { get; set; } = new List<ActionDefinition>();
    }

    public class ConditionGroup
    {
        public List<ConditionDefinition> All { get; set; } = new List<ConditionDefinition>();
        public List<ConditionDefinition> Any { get; set; } = new List<ConditionDefinition>();
    }

    public abstract class ConditionDefinition
    {
        public ConditionType Type { get; set; }
    }

    public enum ConditionType
    {
        Comparison,
        Expression,
        ThresholdOverTime,
    }

    public class ComparisonCondition : ConditionDefinition
    {
        public new ConditionType Type { get; set; } = ConditionType.Comparison; // Hides the base Type
        public string Sensor { get; set; } = string.Empty;
        public ComparisonOperator Operator { get; set; }
        public double Value { get; set; }
    }

    public enum ComparisonOperator
    {
        LessThan,
        GreaterThan,
        LessThanOrEqual,
        GreaterThanOrEqual,
        EqualTo,
        NotEqualTo,
    }

    public class ExpressionCondition : ConditionDefinition
    {
        public new ConditionType Type { get; set; } = ConditionType.Expression; // Hides the base Type
        public string Expression { get; set; } = string.Empty;
    }

    public class ThresholdOverTimeCondition : ConditionDefinition
    {
        public new ConditionType Type { get; set; } = ConditionType.ThresholdOverTime; // Hides the base Type
        public string Sensor { get; set; } = string.Empty;
        public double Threshold { get; set; }
        public int Duration { get; set; }
    }

    public abstract class ActionDefinition
    {
        public ActionType Type { get; set; }
    }

    public enum ActionType
    {
        SetValue,
        SendMessage,
    }

    public class SetValueAction : ActionDefinition
    {
        public new ActionType Type { get; set; } = ActionType.SetValue; // Hides the base Type
        public string Key { get; set; } = string.Empty;
        public double? Value { get; set; } // Nullable
        public string? ValueExpression { get; set; } // Nullable
    }

    public class SendMessageAction : ActionDefinition
    {
        public new ActionType Type { get; set; } = ActionType.SendMessage; // Hides the base Type
        public string Channel { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
    }
}
