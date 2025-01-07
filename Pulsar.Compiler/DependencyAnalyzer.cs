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
            var outputs = new Dictionary<string, RuleDefinition>();

            // Initialize empty lists for all rules
            foreach (var rule in rules)
            {
                graph[rule] = new List<RuleDefinition>();
                Debug.WriteLine($"Initialized graph entry for {rule.Name}");
            }

            // Collect outputs from each rule
            foreach (var rule in rules)
            {
                foreach (var action in rule.Actions)
                {
                    if (action is SetValueAction setValueAction)
                    {
                        outputs[setValueAction.Key] = rule;
                        Debug.WriteLine($"Recorded output {setValueAction.Key} for {rule.Name}");
                    }
                }
            }

            // Build dependencies - create edges FROM dependencies TO dependents
            foreach (var rule in rules)
            {
                var dependencies = GetDependencies(rule);
                Debug.WriteLine($"\nProcessing dependencies for {rule.Name}:");

                foreach (var dependency in dependencies)
                {
                    if (outputs.TryGetValue(dependency, out var dependencyRule))
                    {
                        // Add edge FROM dependency TO the current rule
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

                // Check if all dependencies are satisfied
                var dependenciesSatisfied = true;
                foreach (var dep in GetDependencies(rule))
                {
                    if (outputs.TryGetValue(dep, out var depRule) && !sorted.Contains(depRule))
                    {
                        dependenciesSatisfied = false;
                        break;
                    }
                }

                if (dependenciesSatisfied)
                {
                    // If all dependencies are in sorted list, add this rule
                    sorted.Add(rule);
                    Debug.WriteLine(
                        $"Added {rule.Name} to sorted list (all dependencies satisfied)"
                    );
                }

                // Visit all dependents
                foreach (var dependent in graph[rule])
                {
                    Debug.WriteLine($"Processing dependent {dependent.Name} of {rule.Name}");
                    if (!visited.Contains(dependent))
                    {
                        Visit(dependent, graph, visited, visiting, sorted);
                    }
                }

                // If we couldn't add it before because of dependencies, add it now
                if (!dependenciesSatisfied && !sorted.Contains(rule))
                {
                    sorted.Add(rule);
                    Debug.WriteLine(
                        $"Added {rule.Name} to sorted list (after processing dependents)"
                    );
                }

                visiting.Remove(rule);
                visited.Add(rule);
            }
        }
    }
}
