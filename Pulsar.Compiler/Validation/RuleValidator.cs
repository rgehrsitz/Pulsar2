// File: Pulsar.Compiler/Validation/RuleValidator.cs

// public void ValidateTemporalRules(List<RuleDefinition> rules)
// {
//     const double MINIMUM_MEANINGFUL_DURATION_MS = 100; // Configurable threshold

//     foreach (var rule in rules)
//     {
//         var temporalConditions = rule.Conditions.All
//             .OfType<ThresholdOverTimeCondition>();

//         foreach (var condition in temporalConditions)
//         {
//             if (condition.Duration < MINIMUM_MEANINGFUL_DURATION_MS)
//             {
//                 throw new RuleValidationException(
//                     $"Rule {rule.Name} has an unrealistically short temporal duration of {condition.Duration}ms. " +
//                     $"Minimum meaningful duration is {MINIMUM_MEANINGFUL_DURATION_MS}ms."
//                 );
//             }
//         }
//     }
// }