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
using Pulsar.Compiler;

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
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .WriteTo.File(Path.Combine(Directory.GetCurrentDirectory(), "TestLogs.log"), rollingInterval: RollingInterval.Day)
                .WriteTo.Sink(new ListSink(_logMessages))
                .CreateLogger();

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
            const string validCode = @"
        using System;
        using System.Collections.Generic;

        public class TestClass
        {
            public Dictionary<string, double> Process(Dictionary<string, double> input)
            {
                var output = new Dictionary<string, double>();
                foreach(var kvp in input)
                {
                    output[kvp.Key] = kvp.Value * 2;
                }
                return output;
            }
        }";

            // Act
            RoslynCompiler.CompileSource(validCode, _testOutputPath);

            // Assert
            Assert.True(File.Exists(_testOutputPath), "The output DLL should be created.");

            // Verify the log message is captured
            Assert.Contains("Successfully compiled rules to", _logMessages);

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
                () => RoslynCompiler.CompileSource(invalidCode, _testOutputPath)
            );

            // Verify error details
            Assert.Contains("Cannot implicitly convert type 'string' to 'int'", ex.Message);
            Assert.Contains("The name 'undefinedVar' does not exist in the current context", ex.Message);
        }

        [Theory]
        [InlineData("")]
        [InlineData(" ")]
        public void CompileSource_InvalidInputs_ThrowsArgumentException(string code)
        {
            Assert.Throws<ArgumentException>(
                () => RoslynCompiler.CompileSource(code, _testOutputPath)
            );
            AssertContainsLog("Source code cannot be empty");
        }

        private void AssertContainsLog(string expectedText)
        {
            Assert.Contains(_logMessages, msg => msg.Contains(expectedText));
        }
    }
}