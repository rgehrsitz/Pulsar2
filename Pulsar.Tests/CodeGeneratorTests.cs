// File: Pulsar.Tests/CodeGeneratorTests.cs

using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using Pulsar.Compiler.Models;
using Pulsar.Compiler.Generation;

namespace Pulsar.Tests
{
    public class CodeGeneratorTests
    {
        [Fact]
        public void GenerateCSharp_ShouldCreateManifestWithSourceTracking()
        {
            // Arrange
            var rules = new List<RuleDefinition>
            {
                new RuleDefinition
                {
                    Name = "TestRule1",
                    Description = "Test rule 1",
                    SourceFile = "rules.yaml",
                    LineNumber = 10,
                    Actions = new List<ActionDefinition>
                    {
                        new SetValueAction
                        {
                            Key = "output1",
                            Value = 1.0
                        }
                    }
                },
                new RuleDefinition
                {
                    Name = "TestRule2",
                    Description = "Test rule 2",
                    SourceFile = "rules.yaml",
                    LineNumber = 20,
                    Conditions = new ConditionGroup
                    {
                        All = new List<ConditionDefinition>
                        {
                            new ComparisonCondition
                            {
                                Sensor = "input1",
                                Operator = ComparisonOperator.GreaterThan,
                                Value = 5.0
                            }
                        }
                    },
                    Actions = new List<ActionDefinition>
                    {
                        new SetValueAction
                        {
                            Key = "output2",
                            Value = 2.0
                        }
                    }
                }
            };

            // Act
            var generatedFiles = CodeGenerator.GenerateCSharp(rules);
            var manifest = generatedFiles.FirstOrDefault();
            var files = generatedFiles;

            // Assert
            Assert.NotNull(manifest);
            Assert.NotEmpty(files);
            Assert.Equal(3, files.Count); // 2 rule files + 1 coordinator
            Assert.Equal(2, manifest.RuleSourceMap.Count);

            // Verify rule source tracking
            var rule1Info = manifest.RuleSourceMap["TestRule1"];
            Assert.Equal("rules.yaml", rule1Info.SourceFile);
            Assert.Equal(10, rule1Info.LineNumber);
            Assert.Equal("RuleGroup_1.cs", rule1Info.GeneratedFile);
            Assert.True(rule1Info.GeneratedLineStart > 0);
            Assert.True(rule1Info.GeneratedLineEnd > rule1Info.GeneratedLineStart);

            var rule2Info = manifest.RuleSourceMap["TestRule2"];
            Assert.Equal("rules.yaml", rule2Info.SourceFile);
            Assert.Equal(20, rule2Info.LineNumber);
            Assert.Equal("RuleGroup_2.cs", rule2Info.GeneratedFile);
            Assert.True(rule2Info.GeneratedLineStart > 0);
            Assert.True(rule2Info.GeneratedLineEnd > rule2Info.GeneratedLineStart);

            // Verify file names
            var fileNames = files.Select(f => f.FileName).ToList();
            Assert.Contains("RuleGroup_1.cs", fileNames);
            Assert.Contains("RuleGroup_2.cs", fileNames);
            Assert.Contains("RuleCoordinator.cs", fileNames);
        }
    }
}
