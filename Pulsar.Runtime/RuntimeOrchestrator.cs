// File: Pulsar.Runtime/RuntimeOrchestrator.cs

using System.Reflection;
using Serilog;
using Pulsar.Runtime.Services;

namespace Pulsar.Runtime;
using Pulsar.Runtime.Buffers;

public class RuntimeOrchestrator : IDisposable
{
    private readonly IRedisService _redis;
    private readonly ILogger _logger;
    private readonly string[] _requiredSensors;
    private readonly CancellationTokenSource _cts;
    private readonly object _rulesLock = new();
    private readonly PeriodicTimer _timer;
    private readonly TimeSpan _cycleTime;

    private dynamic? _rulesInstance;
    private Task? _executionTask;
    private DateTime _lastWarningTime = DateTime.MinValue;
    private bool _disposed;
    private readonly RingBufferManager _bufferManager;

    public RuntimeOrchestrator(
        IRedisService redis,
        ILogger logger,
        string[] requiredSensors,
        TimeSpan? cycleTime = null,
        int bufferCapacity = 100)  // Add buffer capacity parameter
    {
        _redis = redis;
        _logger = logger;
        _requiredSensors = requiredSensors;
        _cts = new CancellationTokenSource();
        _cycleTime = cycleTime ?? TimeSpan.FromMilliseconds(100);
        _timer = new PeriodicTimer(_cycleTime);
        _bufferManager = new RingBufferManager(bufferCapacity);  // Initialize buffer manager

        _logger.Information(
            "Runtime orchestrator initialized with {SensorCount} sensors, {CycleTime}ms cycle time, and {BufferCapacity} buffer capacity",
            requiredSensors.Length,
            _cycleTime.TotalMilliseconds,
            bufferCapacity);
    }

    public void LoadRules(string dllPath)
    {
        try
        {
            var assembly = Assembly.LoadFrom(dllPath);
            var rulesType = assembly.GetType("CompiledRules")
                ?? throw new InvalidOperationException("CompiledRules type not found in assembly");

            lock (_rulesLock)
            {
                _rulesInstance = Activator.CreateInstance(rulesType);
            }

            _logger.Information("Successfully loaded rules from {DllPath}", dllPath);
        }
        catch (BadImageFormatException ex)
        {
            _logger.Fatal(ex, "Failed to load rules from {DllPath}", dllPath);

            // rethrow a new exception that *does* contain your message
            throw new InvalidOperationException($"Failed to load rules from '{dllPath}'", ex);
        }
        catch (Exception ex)
        {
            _logger.Fatal(ex, "Failed to load rules from {DllPath}", dllPath);
            throw;
        }
    }


    public async Task StartAsync()
    {
        if (_rulesInstance == null)
        {
            throw new InvalidOperationException("Rules must be loaded before starting");
        }

        _executionTask = ExecutionLoop();
        _logger.Information("Runtime execution started");
        await Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        _cts.Cancel();
        if (_executionTask != null)
        {
            await _executionTask;
        }
        _logger.Information("Runtime execution stopped");
    }

    private async Task ExecutionLoop()
    {
        try
        {
            while (await _timer.WaitForNextTickAsync(_cts.Token))
            {
                await ExecuteCycleAsync();
            }
        }
        catch (OperationCanceledException)
        {
            _logger.Information("Execution loop cancelled");
        }
        catch (Exception ex)
        {
            _logger.Fatal(ex, "Fatal error in execution loop");
            throw;
        }
    }
    public async Task ExecuteCycleAsync()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(
                nameof(RuntimeOrchestrator),
                "Cannot call ExecuteCycleAsync after the orchestrator has been disposed."
            );
        }

        var cycleStart = DateTime.UtcNow;
        var outputs = new Dictionary<string, double>();

        try
        {
            // Get all sensor values with timestamps in bulk
            var sensorData = await _redis.GetSensorValuesAsync(_requiredSensors);

            // Convert to format needed for rules evaluation while preserving timestamps
            var inputs = sensorData.ToDictionary(
                kvp => kvp.Key,
                kvp => kvp.Value.Value
            );

            // Update ring buffers with timestamps from Redis
            _bufferManager.UpdateBuffers(inputs);

            // Execute rules with access to both current values and buffer manager
            lock (_rulesLock)
            {
                if (_rulesInstance != null)
                {
                    ((dynamic)_rulesInstance).Evaluate(inputs, outputs, _bufferManager);
                }
            }

            // Write outputs with current timestamp
            if (outputs.Any())
            {
                await _redis.SetOutputValuesAsync(outputs);
            }

            // Check cycle time
            var cycleTime = DateTime.UtcNow - cycleStart;
            if (cycleTime > _cycleTime && DateTime.UtcNow - _lastWarningTime > TimeSpan.FromMinutes(1))
            {
                _logger.Warning(
                    "Cycle time ({ActualMs}ms) exceeded target ({TargetMs}ms)",
                    cycleTime.TotalMilliseconds,
                    _cycleTime.TotalMilliseconds);
                _lastWarningTime = DateTime.UtcNow;
            }
        }
        catch (Exception ex)
        {
            // Ensure we're logging the error with the specific message expected by the test
            _logger.Error(ex, "Error during execution cycle");
            throw; // Re-throw to maintain original behavior
        }
    }


    public void Dispose()
    {
        if (_disposed) return;

        _timer.Dispose();
        _cts.Dispose();

        if (_redis is IDisposable disposableRedis)
        {
            disposableRedis.Dispose();
        }

        _bufferManager.Clear();  // Clear all ring buffers
        _disposed = true;
    }
}