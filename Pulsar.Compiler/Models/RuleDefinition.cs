// File: Pulsar.Compiler/Models/RuleDefinition.cs

using System;
using System.Collections.Generic;

namespace Pulsar.Compiler.Models
{
    public class RuleDefinition
    {
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public ConditionGroup? Conditions { get; set; }
        public List<ActionDefinition> Actions { get; set; } = new List<ActionDefinition>();
    }

    public class ConditionGroup : ConditionDefinition
    {
        public new ConditionType Type { get; set; } = ConditionType.Group;
        public List<ConditionDefinition> All { get; set; } = new List<ConditionDefinition>();
        public List<ConditionDefinition> Any { get; set; } = new List<ConditionDefinition>();
        public ConditionGroup? Parent { get; private set; }

        public void AddToAll(ConditionDefinition condition)
        {
            if (condition is ConditionGroup group)
            {
                group.Parent = this;
            }
            All.Add(condition);
        }

        public void AddToAny(ConditionDefinition condition)
        {
            if (condition is ConditionGroup group)
            {
                group.Parent = this;
            }
            Any.Add(condition);
        }
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
        Group,
    }

    public class ComparisonCondition : ConditionDefinition
    {
        public new ConditionType Type { get; set; } = ConditionType.Comparison;
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
        public new ConditionType Type { get; set; } = ConditionType.Expression;
        public string Expression { get; set; } = string.Empty;
    }

    public class ThresholdOverTimeCondition : ConditionDefinition
    {
        public new ConditionType Type { get; set; } = ConditionType.ThresholdOverTime;
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
        public new ActionType Type { get; set; } = ActionType.SetValue;
        public string Key { get; set; } = string.Empty;
        public double? Value { get; set; }
        public string? ValueExpression { get; set; }
    }

    public class SendMessageAction : ActionDefinition
    {
        public new ActionType Type { get; set; } = ActionType.SendMessage;
        public string Channel { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
    }
}
