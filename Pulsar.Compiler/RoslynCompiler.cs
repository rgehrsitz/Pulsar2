// File: Pulsar.Compiler/RoslynCompiler.cs

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using System.Diagnostics.Metrics;

namespace Pulsar.Compiler
{
    public static class RoslynCompiler
    {
        // Prometheus metrics
        private static readonly Meter s_meter = new("Pulsar.Compiler");
        private static readonly Counter<int> s_compilationAttempts = s_meter.CreateCounter<int>("pulsar_compilations_total");
        private static readonly Counter<int> s_compilationErrors = s_meter.CreateCounter<int>("pulsar_compilation_errors_total");
        private static readonly Histogram<double> s_compilationDuration = s_meter.CreateHistogram<double>("pulsar_compilation_duration_seconds");
        public static ILogger s_logger;

        static RoslynCompiler()
        {
            s_logger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .WriteTo.Console()
                .WriteTo.File("logs.txt", rollingInterval: RollingInterval.Day)
                .CreateLogger();
        }

        public static void SetLogger(ILogger logger)
        {
            s_logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public static void CompileSource(List<(string fileName, string content)> sourceFiles, string outputDllPath, bool debug = false)
        {
            s_compilationAttempts.Add(1);
            var sw = System.Diagnostics.Stopwatch.StartNew();

            try
            {
                // Validate inputs
                if (!sourceFiles.Any())
                {
                    throw new ArgumentException("Source files cannot be empty", nameof(sourceFiles));
                }
                if (string.IsNullOrEmpty(outputDllPath))
                {
                    throw new ArgumentException("Output path cannot be empty", nameof(outputDllPath));
                }

                EnsureOutputDirectory(outputDllPath);

                // Parse all source files
                var syntaxTrees = sourceFiles.Select(file =>
                {
                    var tree = CSharpSyntaxTree.ParseText(file.content);
                    ValidateSyntax(tree);
                    return tree;
                }).ToList();

                var compilation = CreateCompilation(syntaxTrees, outputDllPath, debug);
                EmitAssembly(compilation, outputDllPath, debug);

                sw.Stop();
                s_compilationDuration.Record(sw.Elapsed.TotalSeconds);

                s_logger.Information("Successfully compiled {FileCount} rule files to {OutputPath}",
                    sourceFiles.Count, outputDllPath);
            }
            catch (Exception ex)
            {
                s_compilationErrors.Add(1);
                s_logger.Error(ex, "Compilation failed for {OutputPath}", outputDllPath);
                throw;
            }
        }

        private static void ValidateInputs(string csharpCode, string outputDllPath)
        {
            if (string.IsNullOrWhiteSpace(csharpCode))
                throw new ArgumentException("Source code cannot be empty", nameof(csharpCode));

            if (string.IsNullOrEmpty(outputDllPath))
                throw new ArgumentException("Output path cannot be empty", nameof(outputDllPath));
        }

        private static void EnsureOutputDirectory(string outputDllPath)
        {
            var outputDir = Path.GetDirectoryName(outputDllPath);
            if (!string.IsNullOrEmpty(outputDir))
            {
                Directory.CreateDirectory(outputDir);
            }
        }

        private static void ValidateSyntax(SyntaxTree syntaxTree)
        {
            var diagnostics = syntaxTree.GetDiagnostics()
                .Where(d => d.Severity == DiagnosticSeverity.Error);

            if (diagnostics.Any())
            {
                var errors = diagnostics.Select(FormatDiagnostic);
                throw new CompilationException(
                    $"Syntax validation failed:\n{string.Join("\n", errors)}"
                );
            }
        }

        private static CSharpCompilation CreateCompilation(
            List<SyntaxTree> syntaxTrees,
            string outputDllPath,
            bool debug)
        {
            var assemblyName = Path.GetFileNameWithoutExtension(outputDllPath);
            var references = GetMetadataReferences();

            s_logger.Debug("Creating compilation with {ReferenceCount} references and {FileCount} source files",
                references.Count, syntaxTrees.Count);

            return CSharpCompilation.Create(
                assemblyName,
                syntaxTrees: syntaxTrees,
                references: references,
                options: new CSharpCompilationOptions(
                    OutputKind.DynamicallyLinkedLibrary,
                    optimizationLevel: debug ? OptimizationLevel.Debug : OptimizationLevel.Release,
                    allowUnsafe: false,
                    platform: Platform.AnyCpu
                )
            );
        }

        private static void EmitAssembly(
            CSharpCompilation compilation,
            string outputDllPath,
            bool debug)
        {
            var emitOptions = new EmitOptions(
                debugInformationFormat: debug ?
                    DebugInformationFormat.PortablePdb :
                    DebugInformationFormat.Embedded
            );

            string? pdbPath = debug ? Path.ChangeExtension(outputDllPath, ".pdb") : null;

            try
            {
                using (var dllStream = new FileStream(outputDllPath, FileMode.Create))
                using (var pdbStream = pdbPath != null ?
                    new FileStream(pdbPath, FileMode.Create) :
                    null)
                {
                    var result = compilation.Emit(
                        dllStream,
                        pdbStream,
                        options: emitOptions
                    );

                    if (!result.Success)
                    {
                        HandleCompilationFailure(result);
                    }
                }
            }
            catch (IOException ex)
            {
                throw new CompilationException(
                    $"Failed to write output files: {ex.Message}",
                    ex
                );
            }
        }

        private static void HandleCompilationFailure(EmitResult result)
        {
            // Log all diagnostics for debugging
            foreach (var diagnostic in result.Diagnostics)
            {
                s_logger.Debug("Diagnostic: {Id} {Severity} {Message}", diagnostic.Id, diagnostic.Severity, diagnostic.GetMessage());
            }

            // Filter only errors (can expand if necessary)
            var errors = result.Diagnostics
                .Where(d => d.Severity == DiagnosticSeverity.Error) // Include other severities if needed
                .Select(FormatDiagnostic)
                .ToList();

            var errorMessage = new StringBuilder();
            errorMessage.AppendLine($"Compilation failed with {errors.Count} errors:");

            foreach (var error in errors)
            {
                errorMessage.AppendLine(error);
                s_logger.Error("Compilation error: {Error}", error);
            }

            if (errors.Count == 0)
            {
                // If no errors are captured, add a fallback message
                s_logger.Error("Compilation failed, but no error diagnostics were captured.");
                errorMessage.AppendLine("No diagnostic errors were captured.");
            }

            throw new CompilationException(errorMessage.ToString());
        }

        private static string FormatDiagnostic(Diagnostic diagnostic)
        {
            var location = diagnostic.Location.GetLineSpan();
            return $"Line {location.StartLinePosition.Line + 1}: {diagnostic.Id} - {diagnostic.GetMessage()}";
        }

        private static List<MetadataReference> GetMetadataReferences()
        {
            var references = new List<MetadataReference>();
            var trustedAssembliesPath = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string;

            if (string.IsNullOrEmpty(trustedAssembliesPath))
            {
                throw new InvalidOperationException("Unable to find trusted platform assemblies");
            }

            var requiredAssemblies = new HashSet<string>
            {
                // Core functionality
                "System.Runtime.dll",
                "System.Collections.dll",
                "System.Private.CoreLib.dll",
                "System.Console.dll",
                "System.Linq.dll",
                "System.Collections.Generic.dll",
                "System.Math.dll",
                "System.Threading.dll",
                "System.IO.dll",

                // Diagnostics & Monitoring
                "System.Diagnostics.Debug.dll",
                "System.Diagnostics.DiagnosticSource.dll",
                "System.Diagnostics.Metrics.dll",

                // Redis Dependencies
                "StackExchange.Redis.dll",
                "NRedisStack.dll"
            };

            var addedAssemblies = new HashSet<string>(); // To track added assembly names
            foreach (var assemblyPath in trustedAssembliesPath.Split(Path.PathSeparator))
            {
                var fileName = Path.GetFileName(assemblyPath);
                if (requiredAssemblies.Contains(fileName) && !addedAssemblies.Contains(fileName))
                {
                    try
                    {
                        references.Add(MetadataReference.CreateFromFile(assemblyPath));
                        addedAssemblies.Add(fileName);
                        s_logger.Debug("Added reference: {Assembly}", assemblyPath);
                    }
                    catch (Exception ex)
                    {
                        s_logger.Warning(ex, "Failed to load assembly {Assembly}", assemblyPath);
                    }
                }
            }

            // Add explicit reference to Pulsar.Runtime.dll
            var currentDirectory = AppContext.BaseDirectory;
            var rootDirectory = FindSolutionRoot(currentDirectory);
            if (rootDirectory == null)
            {
                throw new InvalidOperationException("Unable to locate the solution root directory.");
            }

            var pulsarRuntimePath = Path.Combine(rootDirectory, "Pulsar.Runtime", "bin", "Debug", "net9.0", "Pulsar.Runtime.dll");

            if (File.Exists(pulsarRuntimePath))
            {
                references.Add(MetadataReference.CreateFromFile(pulsarRuntimePath));
                s_logger.Information("Added reference to Pulsar.Runtime: {Path}", pulsarRuntimePath);
            }
            else
            {
                s_logger.Error("Pulsar.Runtime.dll not found at {Path}", pulsarRuntimePath);
                throw new FileNotFoundException($"Pulsar.Runtime.dll not found at {pulsarRuntimePath}");
            }

            if (!references.Any())
            {
                throw new InvalidOperationException("Failed to load any required assemblies");
            }

            return references;
        }

        private static string? FindSolutionRoot(string startDirectory)
        {
            var directory = new DirectoryInfo(startDirectory);
            while (directory != null)
            {
                if (directory.GetFiles("*.sln").Any())
                {
                    return directory.FullName;
                }
                directory = directory.Parent;
            }
            return null;
        }

        private static IEnumerable<MetadataReference> GetNuGetReferences()
        {
            var nugetPackages = new[]
            {
                "NRedisStack",
                "StackExchange.Redis",
                "Serilog",
                "prometheus-net"
            };

            var references = new List<MetadataReference>();
            var nugetPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".nuget",
                "packages"
            );

            foreach (var package in nugetPackages)
            {
                try
                {
                    var packagePath = Directory.GetDirectories(nugetPath, package + "*").FirstOrDefault();
                    if (packagePath != null)
                    {
                        var dllPath = Directory.GetFiles(packagePath, $"{package}.dll", SearchOption.AllDirectories)
                            .FirstOrDefault();

                        if (dllPath != null)
                        {
                            references.Add(MetadataReference.CreateFromFile(dllPath));
                            s_logger.Debug("Added NuGet reference: {Assembly}", dllPath);
                        }
                    }
                }
                catch (Exception ex)
                {
                    s_logger.Warning(ex, "Failed to load NuGet package {Package}", package);
                }
            }
            return references;
        }

    }

    public class CompilationException : Exception
    {
        public CompilationException(string message) : base(message)
        {
            RoslynCompiler.s_logger.Error("Compilation Exception: {Message}", message);
        }

        public CompilationException(string message, Exception inner) : base(message, inner)
        {
            RoslynCompiler.s_logger.Error(inner, "Compilation Exception: {Message}", message);
        }
    }
}
