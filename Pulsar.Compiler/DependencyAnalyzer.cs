// File: Pulsar.Compiler/DependencyAnalyzer.cs

using System;
using System.Collections.Generic;
using System.Linq;
using Pulsar.Compiler.Models;

namespace Pulsar.Compiler.Analysis
{
    public class DependencyAnalyzer
    {
        public List<RuleDefinition> AnalyzeDependencies(List<RuleDefinition> rules)
        {
            // Build a dependency graph
            var graph = BuildDependencyGraph(rules);

            // Perform topological sort
            var sortedRules = TopologicalSort(graph);

            return sortedRules;
        }

        private Dictionary<RuleDefinition, List<RuleDefinition>> BuildDependencyGraph(
            List<RuleDefinition> rules
        )
        {
            var graph = new Dictionary<RuleDefinition, List<RuleDefinition>>();
            var outputs = new Dictionary<string, RuleDefinition>();

            // Collect outputs from each rule
            foreach (var rule in rules)
            {
                foreach (var action in rule.Actions)
                {
                    if (action is SetValueAction setValueAction)
                    {
                        outputs[setValueAction.Key] = rule;
                    }
                }
            }

            // Build dependencies
            foreach (var rule in rules)
            {
                graph[rule] = new List<RuleDefinition>();

                var dependencies = GetDependencies(rule);
                foreach (var dependency in dependencies)
                {
                    if (outputs.TryGetValue(dependency, out var dependencyRule))
                    {
                        graph[rule].Add(dependencyRule);
                    }
                }
            }

            return graph;
        }

        private List<string> GetDependencies(RuleDefinition rule)
        {
            var dependencies = new List<string>();

            // Collect dependencies from conditions
            if (rule.Conditions?.All != null)
            {
                dependencies.AddRange(
                    rule.Conditions.All.OfType<ComparisonCondition>().Select(cond => cond.Sensor)
                );
            }

            if (rule.Conditions?.Any != null)
            {
                dependencies.AddRange(
                    rule.Conditions.Any.OfType<ComparisonCondition>().Select(cond => cond.Sensor)
                );
            }

            return dependencies.Distinct().ToList();
        }

        private List<RuleDefinition> TopologicalSort(
            Dictionary<RuleDefinition, List<RuleDefinition>> graph
        )
        {
            var sorted = new List<RuleDefinition>();
            var visited = new HashSet<RuleDefinition>();
            var visiting = new HashSet<RuleDefinition>();

            foreach (var rule in graph.Keys)
            {
                if (!visited.Contains(rule))
                {
                    Visit(rule, graph, visited, visiting, sorted);
                }
            }

            sorted.Reverse();
            return sorted;
        }

        private void Visit(
            RuleDefinition rule,
            Dictionary<RuleDefinition, List<RuleDefinition>> graph,
            HashSet<RuleDefinition> visited,
            HashSet<RuleDefinition> visiting,
            List<RuleDefinition> sorted
        )
        {
            if (visiting.Contains(rule))
            {
                throw new InvalidOperationException(
                    $"Cycle detected involving rule '{rule.Name}'!"
                );
            }

            if (!visited.Contains(rule))
            {
                visiting.Add(rule);

                foreach (var dependency in graph[rule])
                {
                    Visit(dependency, graph, visited, visiting, sorted);
                }

                visiting.Remove(rule);
                visited.Add(rule);
                sorted.Add(rule);
            }
        }
    }
}
