// File: Pulsa.Compiler/Program.cs

using System;
using System.Collections.Generic;
using System.IO;
using Serilog;
using YamlDotNet.Serialization;
using Pulsar.Compiler.Parsers;
using Pulsar.Compiler.Models;
using Pulsar.Compiler.Generation;

namespace Pulsar.Compiler
{
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
                    case "validate":
                        await ValidateRules(options, logger);
                        break;
                    case "generate":
                        await GenerateSources(options, logger);
                        break;
                    default:
                        logger.Error("Unknown command: {Command}", command);
                        PrintUsage();
                        return 1;
                }

                return 0;
            }
            catch (Exception ex)
            {
                logger.Error(ex, "An error occurred while executing the command");
                return 1;
            }
        }

        private static void PrintUsage()
        {
            Console.WriteLine("Pulsar Compiler CLI");
            Console.WriteLine();
            Console.WriteLine("Usage:");
            Console.WriteLine("  validate --rules <path> --config <path>");
            Console.WriteLine("  generate --rules <path> --config <path> --output <path>");
            Console.WriteLine();
            Console.WriteLine("Options:");
            Console.WriteLine("  --rules    Path to the rules YAML file");
            Console.WriteLine("  --config   Path to the system configuration file");
            Console.WriteLine("  --output   Output directory for generated source files (required for 'generate')");
            Console.WriteLine();
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

        private static async Task ValidateRules(Dictionary<string, string> options, ILogger logger)
        {
            if (!options.TryGetValue("rules", out var rulesPath) ||
                !options.TryGetValue("config", out var configPath))
            {
                throw new ArgumentException("Both --rules and --config are required for validation.");
            }

            var systemConfig = await LoadSystemConfig(configPath);
            logger.Information("Loaded system config with {count} valid sensors", systemConfig.ValidSensors.Count);

            var parser = new DslParser();
            var ruleDefinitions = parser.ParseRules(
                await File.ReadAllTextAsync(rulesPath),
                systemConfig.ValidSensors,
                Path.GetFileName(rulesPath)
            );

            logger.Information("Successfully validated {count} rules", ruleDefinitions.Count);
        }

        private static async Task GenerateSources(Dictionary<string, string> options, ILogger logger)
        {
            if (!options.TryGetValue("rules", out var rulesPath) ||
                !options.TryGetValue("config", out var configPath) ||
                !options.TryGetValue("output", out var outputDirPath))
            {
                throw new ArgumentException("Both --rules, --config, and --output are required for generation.");
            }

            // Verify the output directory is writable
            VerifyOutputDirectory(outputDirPath, logger);

            var systemConfig = await LoadSystemConfig(configPath);
            logger.Information("Loaded system config with {count} valid sensors", systemConfig.ValidSensors.Count);

            var parser = new DslParser();
            var ruleDefinitions = parser.ParseRules(
                await File.ReadAllTextAsync(rulesPath),
                systemConfig.ValidSensors,
                Path.GetFileName(rulesPath)
            );

            var codeFiles = CodeGenerator.GenerateCSharp(ruleDefinitions);
            var outputDir = new DirectoryInfo(outputDirPath);

            foreach (var file in codeFiles)
            {
                var filePath = Path.Combine(outputDir.FullName, file.FileName);
                await File.WriteAllTextAsync(filePath, file.Content);
                logger.Information("Generated file: {filePath}", filePath);
            }

            logger.Information("Successfully generated {count} source files", codeFiles.Count);
        }


        private static async Task<SystemConfig> LoadSystemConfig(string configPath)
        {
            var yaml = await File.ReadAllTextAsync(configPath);
            var deserializer = new DeserializerBuilder().Build();
            return deserializer.Deserialize<SystemConfig>(yaml);
        }

        private static void VerifyOutputDirectory(string outputDirPath, ILogger logger)
        {
            var outputDir = new DirectoryInfo(outputDirPath);
            if (!outputDir.Exists)
            {
                logger.Information("Creating output directory: {path}", outputDirPath);
                outputDir.Create();
            }

            // Verify we can write to it
            try
            {
                var testFile = Path.Combine(outputDir.FullName, ".write-test");
                File.WriteAllText(testFile, "");
                File.Delete(testFile);
                logger.Information("Output directory {path} is writable", outputDirPath);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"Output directory {outputDirPath} is not writable", ex);
            }
        }

    }

    public class SystemConfig
    {
        public int Version { get; set; }
        public List<string> ValidSensors { get; set; } = new();
    }
}
