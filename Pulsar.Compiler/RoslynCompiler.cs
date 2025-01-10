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

        static RoslynCompiler()
        {
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .WriteTo.Console()
                .WriteTo.File("logs.txt", rollingInterval: RollingInterval.Day)
                .CreateLogger();
        }

        public static void CompileSource(string csharpCode, string outputDllPath, bool debug = false)
        {
            s_compilationAttempts.Add(1);
            var sw = System.Diagnostics.Stopwatch.StartNew();

            try
            {
                ValidateInputs(csharpCode, outputDllPath);
                EnsureOutputDirectory(outputDllPath);

                var syntaxTree = CSharpSyntaxTree.ParseText(csharpCode);
                ValidateSyntax(syntaxTree);

                var compilation = CreateCompilation(syntaxTree, outputDllPath, debug);
                EmitAssembly(compilation, outputDllPath, debug);

                sw.Stop();
                s_compilationDuration.Record(sw.Elapsed.TotalSeconds);

                Log.Information("Successfully compiled rules to {OutputPath}", outputDllPath);
            }
            catch (Exception ex)
            {
                s_compilationErrors.Add(1);
                Log.Error(ex, "Compilation failed for {OutputPath}", outputDllPath);
                throw;
            }
        }

        private static void ValidateInputs(string csharpCode, string outputDllPath)
        {
            if (string.IsNullOrEmpty(csharpCode))
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
            SyntaxTree syntaxTree,
            string outputDllPath,
            bool debug)
        {
            var assemblyName = Path.GetFileNameWithoutExtension(outputDllPath);
            var references = GetMetadataReferences();

            Log.Debug("Creating compilation with {ReferenceCount} references", references.Count);

            return CSharpCompilation.Create(
                assemblyName,
                syntaxTrees: new[] { syntaxTree },
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
                Log.Debug("Diagnostic: {Id} {Severity} {Message}", diagnostic.Id, diagnostic.Severity, diagnostic.GetMessage());
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
                Log.Error("Compilation error: {Error}", error);
            }

            if (errors.Count == 0)
            {
                // If no errors are captured, add a fallback message
                Log.Error("Compilation failed, but no error diagnostics were captured.");
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
                        Log.Debug("Added reference: {Assembly}", assemblyPath);
                    }
                    catch (Exception ex)
                    {
                        Log.Warning(ex, "Failed to load assembly {Assembly}", assemblyPath);
                    }
                }
            }

            // Add NuGet package references if not found in trusted assemblies
            var nugetReferences = GetNuGetReferences();
            foreach (var reference in nugetReferences)
            {
                var fileName = Path.GetFileName(reference.Display);
                if (!addedAssemblies.Contains(fileName))
                {
                    references.Add(reference);
                    addedAssemblies.Add(fileName);
                    Log.Debug("Added NuGet reference: {Assembly}", reference.Display);
                }
            }

            // Log all loaded references for debugging
            foreach (var reference in references)
            {
                Log.Information("Loaded reference: {ReferenceDisplay}", reference.Display);
            }

            if (!references.Any())
            {
                throw new InvalidOperationException("Failed to load any required assemblies");
            }

            return references;
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
                            Log.Debug("Added NuGet reference: {Assembly}", dllPath);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Failed to load NuGet package {Package}", package);
                }
            }
            return references;
        }

    }

    public class CompilationException : Exception
    {
        public CompilationException(string message) : base(message)
        {
            Log.Error("Compilation Exception: {Message}", message);
        }

        public CompilationException(string message, Exception inner) : base(message, inner)
        {
            Log.Error(inner, "Compilation Exception: {Message}", message);
        }
    }
}