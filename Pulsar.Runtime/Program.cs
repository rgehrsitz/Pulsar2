// File: Pulsar.Runtime/Program.cs

using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using Serilog;
using Serilog.Events;
using Serilog.Sinks.File;
using Pulsar.Runtime.Services;
using Pulsar.Runtime.Rules;
using Pulsar.Runtime.Buffers;

namespace Pulsar.Runtime;

public class Program
{
    public static async Task<int> Main(string[] args)
    {
        var config = LoadConfiguration(args);
        var logger = CreateLogger(config);

        try
        {
            logger.Information("Starting Pulsar Runtime v{Version}",
                typeof(Program).Assembly.GetName().Version);

            using var redis = new RedisService(config.RedisConnectionString, logger);
            using var bufferManager = new RingBufferManager(config.BufferCapacity);

            // Rule coordinator will be generated later
            // var coordinator = new RuleCoordinator(logger, bufferManager);

            using var orchestrator = new RuntimeOrchestrator(
                redis,
                logger,
                config.RequiredSensors,
                config.CycleTime ?? TimeSpan.FromMilliseconds(100),
                config.BufferCapacity);

            // Setup graceful shutdown
            var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (s, e) =>
            {
                logger.Information("Shutdown requested, stopping gracefully...");
                e.Cancel = true;
                cts.Cancel();
            };

            logger.Information("Starting orchestrator with {SensorCount} sensors, {CycleTime}ms cycle time",
                config.RequiredSensors.Length,
                config.CycleTime?.TotalMilliseconds ?? 100);

            await orchestrator.StartAsync();

            // Wait for cancellation
            try
            {
                await Task.Delay(Timeout.Infinite, cts.Token);
            }
            catch (OperationCanceledException)
            {
                // Normal shutdown
            }

            logger.Information("Shutting down...");
            await orchestrator.StopAsync();

            return 0;
        }
        catch (Exception ex)
        {
            logger.Fatal(ex, "Fatal error during runtime execution");
            return 1;
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }

    private static RuntimeConfig LoadConfiguration(string[] args)
    {
        var config = new RuntimeConfig();

        // First load from appsettings.json if it exists
        var configFile = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        if (File.Exists(configFile))
        {
            try
            {
                var jsonString = File.ReadAllText(configFile);
                var fileConfig = JsonSerializer.Deserialize<RuntimeConfig>(jsonString);
                if (fileConfig != null)
                {
                    config = fileConfig;
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Warning: Failed to load appsettings.json: {ex.Message}");
            }
        }

        // Then override with command line args
        for (int i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            var nextArg = i + 1 < args.Length ? args[i + 1] : null;

            switch (arg.ToLower())
            {
                case "--redis":
                    if (nextArg != null)
                    {
                        config.RedisConnectionString = nextArg;
                        i++;
                    }
                    break;

                case "--cycle":
                    if (nextArg != null && int.TryParse(nextArg, out var cycleMs))
                    {
                        config.CycleTime = TimeSpan.FromMilliseconds(cycleMs);
                        i++;
                    }
                    break;

                case "--log-level":
                    if (nextArg != null && Enum.TryParse<LogEventLevel>(nextArg, true, out var level))
                    {
                        config.LogLevel = level;
                        i++;
                    }
                    break;

                case "--capacity":
                    if (nextArg != null && int.TryParse(nextArg, out var capacity))
                    {
                        config.BufferCapacity = capacity;
                        i++;
                    }
                    break;

                case "--help" or "-h":
                    PrintUsage();
                    Environment.Exit(0);
                    break;
            }
        }

        ValidateConfig(config);
        return config;
    }

    private static void ValidateConfig(RuntimeConfig config)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(config.RedisConnectionString))
        {
            errors.Add("Redis connection string is required (--redis or appsettings.json)");
        }

        if (config.RequiredSensors == null || config.RequiredSensors.Length == 0)
        {
            errors.Add("At least one sensor must be configured");
        }

        if (config.BufferCapacity <= 0)
        {
            errors.Add("Buffer capacity must be greater than 0");
        }

        if (config.CycleTime?.TotalMilliseconds < 10)
        {
            errors.Add("Cycle time must be at least 10ms");
        }

        if (errors.Any())
        {
            PrintUsage();
            throw new ArgumentException(
                $"Invalid configuration:\n{string.Join("\n", errors)}");
        }
    }

    private static ILogger CreateLogger(RuntimeConfig config)
    {
        var loggerConfig = new LoggerConfiguration()
            .MinimumLevel.Is(config.LogLevel)
            .WriteTo.Console();

        // Add file logging if configured
        if (!string.IsNullOrEmpty(config.LogFile))
        {
            loggerConfig.WriteTo.File(
                config.LogFile,
                rollOnFileSizeLimit: true,
                fileSizeLimitBytes: 10_485_760, // 10MB
                retainedFileCountLimit: 7);
        }

        return loggerConfig.CreateLogger();
    }

    private static void PrintUsage()
    {
        Console.WriteLine("Pulsar Runtime");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  Pulsar.Runtime [options]");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  --redis <connection>   Redis connection string");
        Console.WriteLine("  --cycle <ms>          Evaluation cycle time in milliseconds (default: 100)");
        Console.WriteLine("  --log-level <level>   Minimum log level (default: Information)");
        Console.WriteLine("  --capacity <size>     Ring buffer capacity (default: 100)");
        Console.WriteLine();
        Console.WriteLine("Configuration can also be specified in appsettings.json");
    }
}

public class RuntimeConfig
{
    public string RedisConnectionString { get; set; } = "localhost:6379";
    public TimeSpan? CycleTime { get; set; }
    public LogEventLevel LogLevel { get; set; } = LogEventLevel.Information;
    public int BufferCapacity { get; set; } = 100;
    public string? LogFile { get; set; }
    public string[] RequiredSensors { get; set; } = Array.Empty<string>();
}