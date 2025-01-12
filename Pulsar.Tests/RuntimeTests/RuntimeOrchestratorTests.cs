// File: Pulsar.Tests/RuntimeTests/RuntimeOrchestratorTests.cs

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Moq;
using Pulsar.Runtime;
using Pulsar.Runtime.Services;
using StackExchange.Redis;
using Serilog;
using Xunit;
using Xunit.Abstractions;

namespace Pulsar.Tests.Runtime;

public class RuntimeOrchestratorTests : IDisposable
{
    private readonly Mock<IRedisService> _redisMock;
    private readonly Mock<ILogger> _loggerMock;
    private readonly string _testDllPath;
    private readonly RuntimeOrchestrator _orchestrator;
    private readonly string[] _testSensors = new[] { "sensor1", "sensor2" };
    private readonly ITestOutputHelper _output;

    public RuntimeOrchestratorTests(ITestOutputHelper output)
    {
        _output = output; // Initialize the output helper
        _redisMock = new Mock<IRedisService>();

        // Create logger mock for Serilog's fluent interface.
        // Since ILogger.Information(...) (and similar methods) return void,
        // you cannot do .Returns(_loggerMock.Object). Instead, remove or replace those lines.
        _loggerMock = new Mock<ILogger>();
        _loggerMock
            .Setup(x => x.Information(It.IsAny<string>(), It.IsAny<object[]>()))
            .Callback<string, object[]>((msg, args) =>
            {
                // If you want to do anything special here, place it in the callback
            });

        _loggerMock
            .Setup(x => x.Warning(It.IsAny<string>(), It.IsAny<object[]>()))
            .Callback<string, object[]>((msg, args) =>
            {
                // Same idea as above
            });

        _loggerMock
            .Setup(x => x.Error(It.IsAny<Exception>(), It.IsAny<string>(), It.IsAny<object[]>()))
            .Callback<Exception, string, object[]>((ex, msg, args) =>
            {
                // Handle error logs here
            });

        _loggerMock
            .Setup(x => x.Fatal(It.IsAny<Exception>(), It.IsAny<string>(), It.IsAny<object[]>()))
            .Callback<Exception, string, object[]>((ex, msg, args) =>
            {
                // Handle fatal logs here
            });

        // Create test DLL
        _testDllPath = CreateTestRulesDll();

        _orchestrator = new RuntimeOrchestrator(
            _redisMock.Object,
            _loggerMock.Object,
            _testSensors,
            TimeSpan.FromMilliseconds(100));
    }

    private string CreateTestRulesDll()
    {
        var dllPath = Path.Combine(Path.GetTempPath(), $"TestRules_{Guid.NewGuid()}.dll");
        var code = @"
        using System.Collections.Generic;
        using Pulsar.Runtime.Buffers;

        public class CompiledRules
        {
            public void Evaluate(
                Dictionary<string, double> inputs,
                Dictionary<string, double> outputs,
                RingBufferManager bufferManager)
            {
                if (inputs.TryGetValue(""sensor1"", out var value))
                {
                    outputs[""output1""] = value * 2;
                }
            }
        }";

        Pulsar.Compiler.RoslynCompiler.CompileSource(code, dllPath);
        return dllPath;
    }

    public void Dispose()
    {
        if (File.Exists(_testDllPath))
        {
            try { File.Delete(_testDllPath); }
            catch { /* Ignore cleanup errors */ }
        }
    }

    [Fact]
    public async Task ExecuteCycleAsync_ProcessesInputsAndOutputsCorrectly()
    {
        // Arrange
        var timestamp = DateTime.UtcNow;
        var inputs = new Dictionary<string, (double, DateTime)>
        {
            { "sensor1", (42.0, timestamp) },
            { "sensor2", (24.0, timestamp) }
        };

        _redisMock.Setup(x => x.GetSensorValuesAsync(It.IsAny<IEnumerable<string>>()))
            .ReturnsAsync(inputs);

        Dictionary<string, double>? capturedOutputs = null;
        _redisMock.Setup(x => x.SetOutputValuesAsync(It.IsAny<Dictionary<string, double>>()))
            .Callback<Dictionary<string, double>>(outputs => capturedOutputs = outputs)
            .Returns(Task.CompletedTask);

        // Act
        _orchestrator.LoadRules(_testDllPath);
        await _orchestrator.ExecuteCycleAsync();

        // Assert
        _redisMock.Verify(x => x.GetSensorValuesAsync(_testSensors), Times.Once);
        Assert.NotNull(capturedOutputs);
        Assert.Equal(84.0, capturedOutputs!["output1"]); // 42 * 2
    }

    [Fact]
    public async Task StartAsync_ThrowsIfRulesNotLoaded()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _orchestrator.StartAsync());
    }

    [Fact]
    public async Task ExecuteCycleAsync_HandlesRedisErrors()
    {
        // Arrange
        var testException = new RedisConnectionException(ConnectionFailureType.UnableToConnect, "Test error");

        // Capture all Error method calls
        var errorCalls = new List<(Exception Ex, string Message)>();

        _loggerMock
            .Setup(x => x.Error(
                It.IsAny<Exception>(),
                It.Is<string>(s => s.Contains("Error during execution cycle"))
            ))
            .Callback<Exception, string>((ex, msg) =>
            {
                errorCalls.Add((ex, msg));
                _output.WriteLine($"Error Called: Ex={ex}, Msg={msg}");
            })
            .Verifiable();

        _redisMock.Setup(x => x.GetSensorValuesAsync(It.IsAny<IEnumerable<string>>()))
            .ThrowsAsync(testException);

        // Act & Assert
        _orchestrator.LoadRules(_testDllPath);
        var ex = await Assert.ThrowsAsync<RedisConnectionException>(
            () => _orchestrator.ExecuteCycleAsync());

        // Verify error logging
        _loggerMock.Verify(
            x => x.Error(
                testException,
                It.Is<string>(s => s.Contains("Error during execution cycle"))),
            Times.Once,
            "Error logging was not called as expected");

        // Additional verification
        Assert.Single(errorCalls);
        var (capturedEx, capturedMsg) = errorCalls[0];
        Assert.Equal(testException, capturedEx);
        Assert.Contains("Error during execution cycle", capturedMsg);
    }

    [Fact]
    public async Task StartStop_WorksCorrectly()
    {
        // Arrange
        var inputs = new Dictionary<string, (double, DateTime)>
        {
            { "sensor1", (42.0, DateTime.UtcNow) },
            { "sensor2", (24.0, DateTime.UtcNow) }
        };

        _redisMock.Setup(x => x.GetSensorValuesAsync(It.IsAny<IEnumerable<string>>()))
            .ReturnsAsync(inputs);

        _orchestrator.LoadRules(_testDllPath);

        // Act
        await _orchestrator.StartAsync();
        await Task.Delay(250); // Allow a few cycles
        await _orchestrator.StopAsync();

        // Assert - should have multiple cycles worth of calls
        _redisMock.Verify(
            x => x.GetSensorValuesAsync(_testSensors),
            Times.AtLeast(2));
    }

    [Fact]
    public async Task ExecuteCycleAsync_LogsWarningOnSlowCycle()
    {
        // Arrange
        var inputs = new Dictionary<string, (double, DateTime)>
        {
            { "sensor1", (42.0, DateTime.UtcNow) },
            { "sensor2", (24.0, DateTime.UtcNow) }
        };

        // Simulate slow Redis operation
        _redisMock.Setup(x => x.GetSensorValuesAsync(It.IsAny<IEnumerable<string>>()))
            .Returns(async () =>
            {
                await Task.Delay(150); // Longer than cycle time
                return inputs;
            });

        // Act
        _orchestrator.LoadRules(_testDllPath);
        await _orchestrator.ExecuteCycleAsync();

        // Assert
        _loggerMock.Verify(
            x => x.Warning(
                It.Is<string>(s => s.Contains("Cycle time")),    // message template
                It.Is<double>(actual => actual > 100),          // first property
                It.Is<double>(target => target == 100)          // second property
            ),
            Times.Once);
    }

    [Fact]
    public void LoadRules_HandlesInvalidDll()
    {
        // Arrange
        var invalidDllPath = Path.Combine(Path.GetTempPath(), "Invalid.dll");
        File.WriteAllBytes(invalidDllPath, new byte[100]);

        try
        {
            // Act & Assert
            var ex = Assert.Throws<InvalidOperationException>(
                () => _orchestrator.LoadRules(invalidDllPath));

            // Now the *rethrown* exception includes "Failed to load rules..."
            Assert.Contains("Failed to load rules", ex.Message);

            _loggerMock.Verify(
                x => x.Fatal(
                    It.IsAny<Exception>(),
                    It.Is<string>(s => s.Contains("Failed to load rules")),
                    It.IsAny<string>()),
                Times.Once);
        }
        finally
        {
            try { File.Delete(invalidDllPath); } catch { /* Ignore cleanup */ }
        }
    }

    [Fact]
    public async Task Dispose_CleansUpResourcesCorrectly()
    {
        // Act
        _orchestrator.LoadRules(_testDllPath);
        await _orchestrator.StartAsync();
        await Task.Delay(100);

        // Act
        _orchestrator.Dispose();

        // Assert - attempting to execute cycle after dispose should throw
        await Assert.ThrowsAnyAsync<ObjectDisposedException>(
            () => _orchestrator.ExecuteCycleAsync());
    }
}
