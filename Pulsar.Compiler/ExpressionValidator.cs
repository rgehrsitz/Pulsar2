// File: Pulsar.Compiler/ExpressionValidator.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Pulsar.Compiler.Validation
{
    public class ExpressionValidator
    {
        private static readonly HashSet<string> AllowedOperators = new()
        {
            "+", "-", "*", "/", ">", "<", ">=", "<=", "==", "!="
        };

        private static readonly HashSet<string> AllowedSpecialCharacters = new()
        {
            "(", ")"
        };

        private static readonly HashSet<string> AllowedFunctions = new()
        {
            "Math.Abs", "Math.Min", "Math.Max", "Math.Round"
            // Add other allowed functions as needed
        };

        public static void ValidateExpression(string expression)
        {
            System.Diagnostics.Debug.WriteLine($"Validating expression: {expression}");

            // Remove all whitespace for consistent processing
            expression = Regex.Replace(expression, @"\s+", "");
            System.Diagnostics.Debug.WriteLine($"Expression after whitespace removal: {expression}");

            // Validate balanced parentheses
            ValidateParentheses(expression);

            // Split into tokens (operators, functions, variables, literals)
            var tokens = TokenizeExpression(expression);

            var tokenList = tokens.ToList();
            System.Diagnostics.Debug.WriteLine($"Tokens: {string.Join(", ", tokenList)}");

            foreach (var token in tokenList)
            {
                System.Diagnostics.Debug.WriteLine($"Processing token: {token}");
                if (IsOperator(token))
                {
                    if (!AllowedOperators.Contains(token))
                    {
                        throw new ArgumentException($"Invalid operator in expression: {token}");
                    }
                }
                else if (IsFunction(token))
                {
                    if (!AllowedFunctions.Contains(token))
                    {
                        throw new ArgumentException($"Invalid function in expression: {token}");
                    }
                }
                else if (!IsValidIdentifier(token) && !IsNumeric(token) && !AllowedSpecialCharacters.Contains(token))
                {
                    throw new ArgumentException($"Invalid token in expression: {token}");
                }
            }
        }

        private static void ValidateParentheses(string expression)
        {
            int count = 0;
            foreach (char c in expression)
            {
                if (c == '(') count++;
                if (c == ')') count--;
                if (count < 0)
                {
                    throw new ArgumentException("Unmatched parentheses in expression");
                }
            }
            if (count != 0)
            {
                throw new ArgumentException("Unmatched parentheses in expression");
            }
        }

        private static IEnumerable<string> TokenizeExpression(string expression)
        {
            // Updated pattern to better handle decimals and function calls
            var tokenPattern = @"(?:Math\.[a-zA-Z][a-zA-Z0-9]*)|[a-zA-Z_][a-zA-Z0-9_]*|\d+\.\d+|\d+|[+\-*/><]=?|==|!=|\(|\)";
            return Regex.Matches(expression, tokenPattern)
                       .Select(m => m.Value);
        }

        private static bool IsOperator(string token) =>
            AllowedOperators.Contains(token);

        private static bool IsFunction(string token) =>
            token.Contains(".") && !IsNumeric(token); // Don't treat decimals as functions

        private static bool IsValidIdentifier(string token) =>
            Regex.IsMatch(token, @"^[a-zA-Z_][a-zA-Z0-9_]*$");

        private static bool IsNumeric(string token) =>
            double.TryParse(token, out _);
    }
}