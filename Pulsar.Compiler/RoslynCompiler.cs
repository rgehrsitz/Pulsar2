// File: Pulsar.Compiler/RoslynCompiler.cs

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;

namespace Pulsar.Compiler
{
    public static class RoslynCompiler
    {
        public static void CompileSource(string csharpCode, string outputDllPath)
        {
            // Parse the code into a SyntaxTree
            var syntaxTree = CSharpSyntaxTree.ParseText(csharpCode);

            // Create a compilation
            var assemblyName = Path.GetFileNameWithoutExtension(outputDllPath);
            var compilation = CSharpCompilation.Create(
                assemblyName,
                syntaxTrees: new[] { syntaxTree },
                references: GetMetadataReferences(),
                options: new CSharpCompilationOptions(
                    OutputKind.DynamicallyLinkedLibrary,
                    optimizationLevel: OptimizationLevel.Release,
                    allowUnsafe: false
                )
            );

            // Emit the DLL
            using (var dllStream = new FileStream(outputDllPath, FileMode.Create))
            {
                EmitResult result = compilation.Emit(dllStream);

                if (!result.Success)
                {
                    var errors = result
                        .Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)
                        .Select(d => $"{d.Id}: {d.GetMessage()}")
                        .ToList();

                    throw new InvalidOperationException(
                        $"Compilation failed with {errors.Count} errors:\n"
                            + string.Join("\n", errors)
                    );
                }
            }
        }

        private static List<MetadataReference> GetMetadataReferences()
        {
            var references = new List<MetadataReference>();

            // Add basic .NET references
            var trustedAssembliesPath =
                AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string
                ?? throw new InvalidOperationException(
                    "Unable to find trusted platform assemblies"
                );
            var trustedAssembliesPaths = trustedAssembliesPath.Split(Path.PathSeparator);

            var requiredAssemblies = new[]
            {
                "System.Runtime.dll",
                "System.Collections.dll",
                "System.Private.CoreLib.dll",
                "System.Console.dll",
                "System.Linq.dll",
                // Add other required assemblies here
            };

            foreach (var assemblyPath in trustedAssembliesPaths)
            {
                if (requiredAssemblies.Contains(Path.GetFileName(assemblyPath)))
                {
                    references.Add(MetadataReference.CreateFromFile(assemblyPath));
                }
            }

            return references;
        }
    }
}
