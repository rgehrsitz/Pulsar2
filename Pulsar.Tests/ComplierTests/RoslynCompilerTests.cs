// File: Pulsar.Tests/ComplierTests/RoslynCompilerTests.cs

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Xunit;
using Xunit.Abstractions;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Serilog.Sinks.XUnit;
using Pulsar.Compiler;
using Pulsar.Runtime.Buffers;

namespace Pulsar.Tests.CompilerTests
{
    // Custom sink for capturing log messages
    public class ListSink : ILogEventSink
    {
        private readonly List<string> _logMessages;

        public ListSink(List<string> logMessages)
        {
            _logMessages = logMessages;
        }

        public void Emit(LogEvent logEvent)
        {
            _logMessages.Add(logEvent.RenderMessage());
        }
    }

    public class RoslynCompilerTests : IDisposable
    {
        private readonly string _testOutputPath;
        private readonly ITestOutputHelper _output;
        private readonly List<string> _logMessages;

        public RoslynCompilerTests(ITestOutputHelper output)
        {
            _output = output;
            _logMessages = new List<string>();
            Serilog.Debugging.SelfLog.Enable(Console.Error);

            // Configure Serilog for testing
            var logger = new LoggerConfiguration()
                .MinimumLevel.Verbose()
                .WriteTo.File(Path.Combine(Directory.GetCurrentDirectory(), "TestLogs.log"), rollingInterval: RollingInterval.Day)
                .WriteTo.Sink(new ListSink(_logMessages))
                .WriteTo.TestOutput(_output)  // Add test output for debugging
                .CreateLogger();

            RoslynCompiler.SetLogger(logger);

            _testOutputPath = Path.Combine(
                Path.GetTempPath(),
                $"TestCompiledRules_{Guid.NewGuid()}.dll"
            );
        }

        public void Dispose()
        {
            CleanupTestFiles();
            Log.CloseAndFlush();
        }

        private void CleanupTestFiles()
        {
            var filesToDelete = new[]
            {
                _testOutputPath,
                Path.ChangeExtension(_testOutputPath, ".pdb")
            };

            foreach (var file in filesToDelete)
            {
                if (File.Exists(file))
                {
                    try
                    {
                        File.Delete(file);
                    }
                    catch (Exception ex)
                    {
                        _output.WriteLine($"Warning: Failed to delete {file}: {ex.Message}");
                    }
                }
            }
        }

        [Fact]
        public void CompileSource_ValidCode_CreatesAssembly()
        {
            // Arrange
            var validCode = @"
            using System;
            using System.Collections.Generic;

            public class TestClass
            {
                public Dictionary<string, double> Process(Dictionary<string, double> input)
                {
                    var output = new Dictionary<string, double>();
                    foreach (var kvp in input)
                    {
                        output[kvp.Key] = kvp.Value * 2;
                    }
                    return output;
                }
            }";

            // Act
            RoslynCompiler.CompileSource(new List<(string, string)>
{
    ("TestClass.cs", validCode)
}, _testOutputPath);

            // Assert
            Assert.True(File.Exists(_testOutputPath), "The output DLL should be created.");

            // Verify the log message is captured
            AssertContainsLog("Successfully compiled rules");

            // Load and test the compiled assembly
            var assembly = Assembly.LoadFile(_testOutputPath);
            var type = assembly.GetType("TestClass");
            Assert.NotNull(type);

            var instance = Activator.CreateInstance(type);
            var method = type?.GetMethod("Process");
            Assert.NotNull(method);

            var input = new Dictionary<string, double> { { "test", 5.0 } };
            var result = method.Invoke(instance, new[] { input }) as Dictionary<string, double>;

            Assert.NotNull(result);
            Assert.Equal(10.0, result["test"]);
        }


        [Fact]
        public void CompileSource_InvalidCode_ThrowsDetailedException()
        {
            // Arrange
            const string invalidCode = @"
        public class Invalid
        {
            public void Method()
            {
                var result = undefinedVar.Process();  // Multiple errors
                int x = ""string"";                   // Type mismatch
            }
        }";

            // Act & Assert
            var ex = Assert.Throws<CompilationException>(
                () => RoslynCompiler.CompileSource(new List<(string, string)>
{
    ("Invalid.cs", invalidCode)
}, _testOutputPath)
            );

            // Verify error details
            Assert.Contains("Cannot implicitly convert type 'string' to 'int'", ex.Message);
            Assert.Contains("The name 'undefinedVar' does not exist in the current context", ex.Message);
        }

        private void AssertContainsLog(string expectedText)
        {
            // Dump all log messages for debugging
            foreach (var msg in _logMessages)
            {
                _output.WriteLine($"Log message: {msg}");
            }

            Assert.Contains(_logMessages, msg => msg.Contains(expectedText));
        }

        [Fact]
        public void CompileSource_MultipleFiles_CreatesAssembly()
        {
            // Arrange
            var sourceFiles = new List<(string fileName, string content)>
    {
        ("RuleGroup_1.cs", @"
        using System;
        using System.Collections.Generic;
        using Pulsar.Runtime.Buffers;

        namespace Pulsar.Generated
        {
            public partial class CompiledRules
            {
                private void EvaluateLayer0(Dictionary<string, double> inputs, Dictionary<string, double> outputs, RingBufferManager bufferManager)
                {
                    if (inputs[""temperature""] > 100)
                    {
                        outputs[""alert""] = 1;
                    }
                }
            }
        }"),
        ("RuleCoordinator.cs", @"
        namespace Pulsar.Generated
        {
            public partial class CompiledRules
            {
                public void Evaluate(Dictionary<string, double> inputs, Dictionary<string, double> outputs, RingBufferManager bufferManager)
                {
                    EvaluateLayer0(inputs, outputs, bufferManager);
                }
            }
        }")
    };

            try
            {
                // Act
                RoslynCompiler.CompileSource(sourceFiles, _testOutputPath);

                // Assert
                Assert.True(File.Exists(_testOutputPath), "The output DLL should be created.");

                // Verify the log messages
                AssertContainsLog($"Successfully compiled {sourceFiles.Count} rule files");

                // Load and test the compiled assembly
                var assembly = Assembly.LoadFile(_testOutputPath);
                var type = assembly.GetType("Pulsar.Generated.CompiledRules");
                Assert.NotNull(type);

                var instance = Activator.CreateInstance(type);
                var method = type?.GetMethod("Evaluate");
                Assert.NotNull(method);

                var input = new Dictionary<string, double> { { "temperature", 105.0 } };
                var outputs = new Dictionary<string, double>();
                var bufferManager = new RingBufferManager();

                method.Invoke(instance, new object[] { input, outputs, bufferManager });

                Assert.True(outputs.ContainsKey("alert"));
                Assert.Equal(1, outputs["alert"]);
            }
            catch (Exception ex)
            {
                _output.WriteLine($"Test failed: {ex}");
                throw;
            }
        }

        [Fact]
        public void CompileSource_InvalidFileInBatch_ThrowsDetailedException()
        {
            // Arrange
            var sourceFiles = new List<(string fileName, string content)>
    {
        ("Valid.cs", @"
        namespace Pulsar.Generated
        {
            public partial class CompiledRules
            {
                private void EvaluateLayer0() {}
            }
        }"),
        ("Invalid.cs", @"
        public class Invalid
        {
            public void Method()
            {
                var result = undefinedVar.Process();  // Error
                int x = ""string"";                   // Error
            }
        }")
    };

            // Act & Assert
            var ex = Assert.Throws<CompilationException>(
                () => RoslynCompiler.CompileSource(sourceFiles, _testOutputPath)
            );

            // Verify error details
            Assert.Contains("Cannot implicitly convert type 'string' to 'int'", ex.Message);
            Assert.Contains("The name 'undefinedVar' does not exist in the current context", ex.Message);

            // Verify error is associated with correct file
            Assert.Contains("Invalid.cs", ex.Message);
        }

        [Theory]
        [InlineData(new string[] { }, "Source files cannot be empty")]
        public void CompileSource_InvalidInputs_ThrowsArgumentException(string[] emptyContent, string expectedError)
        {
            var sourceFiles = emptyContent.Select(c => ("test.cs", c)).ToList();

            var ex = Assert.Throws<ArgumentException>(
                () => RoslynCompiler.CompileSource(sourceFiles, _testOutputPath)
            );
            Assert.Equal(expectedError, ex.Message);
        }

        [Fact]
        public void CompileSource_CrossFileRuleDependencies_WorksCorrectly()
        {
            // Arrange
            var sourceFiles = new List<(string fileName, string content)>
    {
        ("RuleGroup_1.cs", @"
        namespace Pulsar.Generated
        {
            public partial class CompiledRules
            {
                private void EvaluateLayer0(Dictionary<string, double> inputs, Dictionary<string, double> outputs, RingBufferManager bufferManager)
                {
                    // Rule: TempConversion
                    outputs[""temp_c""] = (inputs[""temp_f""] - 32) * 5/9;
                }
            }
        }"),
        ("RuleGroup_2.cs", @"
        namespace Pulsar.Generated
        {
            public partial class CompiledRules
            {
                private void EvaluateLayer1(Dictionary<string, double> inputs, Dictionary<string, double> outputs, RingBufferManager bufferManager)
                {
                    // Rule: HighTempAlert
                    if (outputs[""temp_c""] > 30)
                    {
                        outputs[""high_temp""] = 1;
                    }
                }
            }
        }"),
        ("RuleCoordinator.cs", @"
        namespace Pulsar.Generated
        {
            public partial class CompiledRules
            {
                public void Evaluate(Dictionary<string, double> inputs, Dictionary<string, double> outputs, RingBufferManager bufferManager)
                {
                    EvaluateLayer0(inputs, outputs, bufferManager);
                    EvaluateLayer1(inputs, outputs, bufferManager);
                }
            }
        }")
    };

            // Act & Test
            RoslynCompiler.CompileSource(sourceFiles, _testOutputPath);
            var assembly = Assembly.LoadFile(_testOutputPath);
            var type = assembly.GetType("Pulsar.Generated.CompiledRules");
            var instance = Activator.CreateInstance(type)
                             ?? throw new InvalidOperationException("Failed to create CompiledRules instance");

            var method = type.GetMethod("Evaluate");

            var inputs = new Dictionary<string, double> { { "temp_f", 100.0 } };
            var outputs = new Dictionary<string, double>();
            var bufferManager = new RingBufferManager();

            method.Invoke(instance, new object[] { inputs, outputs, bufferManager });

            Assert.True(outputs.ContainsKey("temp_c"));
            Assert.True(outputs.ContainsKey("high_temp"));
            Assert.Equal(37.77777777777778, outputs["temp_c"], 8);
            Assert.Equal(1, outputs["high_temp"]);
        }
    }
}