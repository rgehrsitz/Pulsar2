// File: Pulsar.Compiler/Validation/ExpressionHelper.cs

using System;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Pulsar.Compiler.Validation
{
    public static class ExpressionHelper
    {
        public static bool ValidateGeneratedExpression(string expression)
        {
            try
            {
                var code =
                    $@"
                    using System;
                    using System.Collections.Generic;
                    public class Test {{
                        public bool Evaluate(Dictionary<string, double> inputs) {{
                            return {expression};
                        }}
                    }}";

                var tree = CSharpSyntaxTree.ParseText(code);
                var diagnostics = tree.GetDiagnostics();
                return !diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Expression validation failed: {ex.Message}");
                return false;
            }
        }
    }
}
