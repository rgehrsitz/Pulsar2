// File: Pulsar.Compiler/Models/SourceInfo.cs

namespace Pulsar.Compiler.Models
{
    public class SourceInfo
    {
        public string FileName { get; set; } = string.Empty;
        public int LineNumber { get; set; }
        public int ColumnNumber { get; set; }
        public string OriginalText { get; set; } = string.Empty;

        public override string ToString()
        {
            return $"{FileName}({LineNumber},{ColumnNumber})";
        }
    }
}