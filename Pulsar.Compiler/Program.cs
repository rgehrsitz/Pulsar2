// File: Pulsa.Compiler/Program.cs

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Serilog;
using Pulsar.Compiler.Parsers;
using Pulsar.Compiler.Models;
using Pulsar.Compiler.Generation;

namespace Pulsar.Compiler;

public class Program
{
    public static async Task<int> Main(string[] args)
    {
        var logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.Console()
            .CreateLogger();

        if (args.Length == 0 || args[0] == "--help" || args[0] == "-h")
        {
            PrintUsage();
            return 0;
        }

        try
        {
            var command = args[0].ToLower();
            var options = ParseArguments(args);

            switch (command)
            {
                case "compile":
                    await CompileRules(options, logger);
                    break;
                default:
                    logger.Error("Unknown command: {Command}", command);
                    PrintUsage();
                    return 1;
            }

            return 0;
        }
        catch (ArgumentException ex)
        {
            logger.Error(ex, "Invalid arguments provided");
            PrintUsage();
            return 1;
        }
        catch (Exception ex)
        {
            logger.Fatal(ex, "Fatal error during compilation");
            return 1;
        }
    }

    private static void PrintUsage()
    {
        Console.WriteLine("Pulsar Rule Compiler");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  compile [options]");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  --rules <path>     Single YAML file or directory containing YAML files");
        Console.WriteLine("  --config <path>    Path to system configuration file (default: system_config.yaml)");
        Console.WriteLine("  --output <path>    Output directory for generated source files");
        Console.WriteLine();
        Console.WriteLine("Examples:");
        Console.WriteLine("  compile --rules ./rules/myrules.yaml");
        Console.WriteLine("  compile --rules ./rules/ --config system_config.yaml");
    }

    private static Dictionary<string, string> ParseArguments(string[] args)
    {
        var options = new Dictionary<string, string>();
        for (int i = 1; i < args.Length; i += 2)
        {
            if (i + 1 < args.Length && args[i].StartsWith("--"))
            {
                options[args[i].Substring(2)] = args[i + 1];
            }
            else
            {
                throw new ArgumentException($"Invalid argument format: {args[i]}");
            }
        }
        return options;
    }

    private static async Task CompileRules(Dictionary<string, string> options, ILogger logger)
    {
        if (!options.TryGetValue("rules", out var rulesPath))
        {
            throw new ArgumentException("--rules argument is required");
        }

        var configPath = options.GetValueOrDefault("config", "system_config.yaml");
        var outputPath = options.GetValueOrDefault("output", "Generated");

        // Load system configuration
        var systemConfig = await LoadSystemConfig(configPath);
        logger.Information("Loaded system configuration with {count} valid sensors",
            systemConfig.ValidSensors.Count);

        // Get all rule files
        var ruleFiles = GetRuleFiles(rulesPath);
        logger.Information("Found {count} rule files to process", ruleFiles.Count);

        // Parse and validate all rules
        var parser = new DslParser();
        var allRules = new List<RuleDefinition>();

        foreach (var file in ruleFiles)
        {
            var yamlContent = await File.ReadAllTextAsync(file);
            var rules = parser.ParseRules(
                yamlContent,
                systemConfig.ValidSensors,
                Path.GetFileName(file));

            allRules.AddRange(rules);
            logger.Information("Parsed {count} rules from {file}",
                rules.Count, Path.GetFileName(file));
        }

        // Generate C# source files
        Directory.CreateDirectory(outputPath);
        var generatedFiles = CodeGenerator.GenerateCSharp(allRules);

        foreach (var file in generatedFiles)
        {
            var filePath = Path.Combine(outputPath, file.FileName);
            await File.WriteAllTextAsync(filePath, file.Content);
            logger.Information("Generated {file}", filePath);
        }

        logger.Information("Successfully compiled {count} rules to C# source files",
            allRules.Count);
    }

    private static List<string> GetRuleFiles(string rulesPath)
    {
        if (File.Exists(rulesPath))
        {
            // Single file
            return new List<string> { rulesPath };
        }

        if (Directory.Exists(rulesPath))
        {
            // Directory of files
            return Directory.GetFiles(rulesPath, "*.yaml", SearchOption.AllDirectories)
                .ToList();
        }

        throw new ArgumentException($"Rules path not found: {rulesPath}");
    }

    private static async Task<SystemConfig> LoadSystemConfig(string configPath)
    {
        if (!File.Exists(configPath))
        {
            throw new FileNotFoundException($"System configuration file not found: {configPath}");
        }

        var yaml = await File.ReadAllTextAsync(configPath);
        var deserializer = new YamlDotNet.Serialization.DeserializerBuilder().Build();
        return deserializer.Deserialize<SystemConfig>(yaml);
    }
}