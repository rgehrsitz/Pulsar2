// File: Pulsar.Compiler/DependencyAnalyzer.cs

using System;
using System.Collections.Generic;
using System.Diagnostics;
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
            _outputs.Clear(); // Clear previous outputs

            // Initialize empty lists for all rules
            foreach (var rule in rules)
            {
                graph[rule] = new List<RuleDefinition>();
            }

            // Collect outputs from each rule
            foreach (var rule in rules)
            {
                foreach (var action in rule.Actions)
                {
                    if (action is SetValueAction setValueAction)
                    {
                        _outputs[setValueAction.Key] = rule;
                    }
                }
            }

            // Build dependencies
            foreach (var rule in rules)
            {
                var dependencies = GetDependencies(rule);
                foreach (var dependency in dependencies)
                {
                    if (_outputs.TryGetValue(dependency, out var dependencyRule))
                    {
                        graph[dependencyRule].Add(rule);
                        Debug.WriteLine($"Added edge from {dependencyRule.Name} to {rule.Name}");
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

            // Find nodes that have incoming edges (are depended upon)
            var hasIncomingEdges = new HashSet<RuleDefinition>();
            foreach (var kvp in graph)
            {
                foreach (var dependent in kvp.Value)
                {
                    hasIncomingEdges.Add(dependent);
                }
            }

            Debug.WriteLine("\nProcessing nodes with no incoming edges first (base nodes):");
            // First process nodes with no incoming edges (nothing depends on them)
            foreach (var rule in graph.Keys)
            {
                if (!hasIncomingEdges.Contains(rule))
                {
                    Debug.WriteLine($"Starting with base node: {rule.Name}");
                    if (!visited.Contains(rule))
                    {
                        Visit(rule, graph, visited, visiting, sorted);
                    }
                }
            }

            Debug.WriteLine("\nProcessing remaining nodes:");
            // Then process any remaining nodes
            foreach (var rule in graph.Keys)
            {
                if (!visited.Contains(rule))
                {
                    Debug.WriteLine($"Processing remaining node: {rule.Name}");
                    Visit(rule, graph, visited, visiting, sorted);
                }
            }

            return sorted;
        }

        private Dictionary<string, RuleDefinition> _outputs = new();

        private void Visit(
            RuleDefinition rule,
            Dictionary<RuleDefinition, List<RuleDefinition>> graph,
            HashSet<RuleDefinition> visited,
            HashSet<RuleDefinition> visiting,
            List<RuleDefinition> sorted
        )
        {
            Debug.WriteLine($"Visiting {rule.Name}");

            if (visiting.Contains(rule))
            {
                throw new InvalidOperationException(
                    $"Cycle detected involving rule '{rule.Name}'!"
                );
            }

            if (!visited.Contains(rule))
            {
                visiting.Add(rule);

                // Visit all dependents first
                foreach (var dependent in graph[rule])
                {
                    if (!visited.Contains(dependent))
                    {
                        Visit(dependent, graph, visited, visiting, sorted);
                    }
                }

                visiting.Remove(rule);
                visited.Add(rule);

                if (!sorted.Contains(rule))
                {
                    // If this rule has dependents, insert at beginning
                    // Otherwise append to maintain original order
                    if (graph[rule].Any())
                    {
                        sorted.Insert(0, rule);
                        Debug.WriteLine(
                            $"Added {rule.Name} to start of sorted list (has dependents)"
                        );
                    }
                    else
                    {
                        sorted.Add(rule);
                        Debug.WriteLine($"Added {rule.Name} to end of sorted list (no dependents)");
                    }
                }
            }
        }
    }
}
