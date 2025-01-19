// File: Pulsar.Runtime/Program.cs

using System;
using System.Collections.Generic;
using System.Diagnostics;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
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

    internal static RuntimeConfig LoadConfiguration(string[] args, bool requireSensors = true, string? configPath = null)
    {
        var config = new RuntimeConfig();
        Debug.WriteLine($"Initial config - RedisConnectionString: {config.RedisConnectionString}");

        // First load from appsettings.json if it exists
        var configFile = configPath ?? Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        Debug.WriteLine($"Looking for config file at: {configFile}");

        if (File.Exists(configFile))
        {
            try
            {
                var jsonString = File.ReadAllText(configFile);
                Debug.WriteLine($"Read config file: {jsonString}");

                var fileConfig = JsonConvert.DeserializeObject<RuntimeConfig>(jsonString);
                Debug.WriteLine($"Deserialized config - RedisConnectionString: {fileConfig?.RedisConnectionString}");

                if (fileConfig != null)
                {
                    config = fileConfig;
                    Debug.WriteLine($"Assigned config - RedisConnectionString: {config.RedisConnectionString}");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error loading config file: {ex}");
                throw;
            }
        }
        else
        {
            Debug.WriteLine($"No config file found at: {configFile}");
        }

        // Then override with command line args
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--redis":
                    if (i + 1 < args.Length)
                    {
                        config.RedisConnectionString = args[++i];
                        Debug.WriteLine($"Set RedisConnectionString from args: {config.RedisConnectionString}");
                    }
                    break;
                case "--cycle":
                    if (i + 1 < args.Length)
                    {
                        try
                        {
                            config.CycleTime = TimeSpan.Parse(args[++i]);
                            Debug.WriteLine($"Set CycleTime from args: {config.CycleTime}");
                        }
                        catch (FormatException ex)
                        {
                            throw new FormatException($"The string was not recognized as a valid TimeSpan: {args[i]}", ex);
                        }
                    }
                    break;
                case "--log-level":
                    if (i + 1 < args.Length && Enum.TryParse<LogEventLevel>(args[++i], true, out var level))
                    {
                        config.LogLevel = level;
                        Debug.WriteLine($"Set LogLevel from args: {config.LogLevel}");
                    }
                    break;
                case "--capacity":
                    if (i + 1 < args.Length && int.TryParse(args[++i], out var capacity))
                    {
                        config.BufferCapacity = capacity;
                        Debug.WriteLine($"Set BufferCapacity from args: {config.BufferCapacity}");
                    }
                    break;
            }
        }

        if (requireSensors && (config.RequiredSensors == null || config.RequiredSensors.Length == 0))
        {
            throw new ArgumentException("At least one sensor must be specified in the configuration.");
        }

        Debug.WriteLine($"Final config - RedisConnectionString: {config.RedisConnectionString}");
        return config;
    }

    public static void ValidateConfig(RuntimeConfig config, bool requireSensors = true)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(config.RedisConnectionString))
        {
            errors.Add("Redis connection string is required (--redis or appsettings.json)");
        }

        if (requireSensors && (config.RequiredSensors == null || config.RequiredSensors.Length == 0))
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
    private string _redisConnectionString = "localhost:6379";
    
    [JsonProperty("RedisConnectionString")]
    public string RedisConnectionString
    {
        get => _redisConnectionString;
        set => _redisConnectionString = string.IsNullOrEmpty(value) ? "localhost:6379" : value;
    }

    [JsonProperty("CycleTime")]
    [JsonConverter(typeof(TimeSpanConverter))]
    public TimeSpan? CycleTime { get; set; }

    [JsonProperty("LogLevel")]
    public LogEventLevel LogLevel { get; set; } = LogEventLevel.Information;

    [JsonProperty("BufferCapacity")]
    public int BufferCapacity { get; set; } = 100;

    [JsonProperty("LogFile")]
    public string? LogFile { get; set; }

    [JsonProperty("RequiredSensors")]
    public string[] RequiredSensors { get; set; } = Array.Empty<string>();
}

public class TimeSpanConverter : JsonConverter<TimeSpan?>
{
    public override TimeSpan? ReadJson(JsonReader reader, Type objectType, TimeSpan? existingValue, bool hasExistingValue, JsonSerializer serializer)
    {
        if (reader.TokenType == JsonToken.String)
        {
            var value = reader.Value as string;
            if (string.IsNullOrEmpty(value)) return null;
            return TimeSpan.Parse(value);
        }
        return null;
    }

    public override void WriteJson(JsonWriter writer, TimeSpan? value, JsonSerializer serializer)
    {
        if (value.HasValue)
            writer.WriteValue(value.Value.ToString("c"));
        else
            writer.WriteNull();
    }
}