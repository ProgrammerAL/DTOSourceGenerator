using Microsoft.CodeAnalysis;

using System;
using System.Collections.Generic;
using System.Text;

namespace ProgrammerAl.DTO.Generators
{
    internal static class GeneratedClassStrings
    {
        public const string SpacesForPropertyLines = "        ";
        public const string SpacesForIsValidChecks = "                   ";
    }

    public static class SymbolDisplayFormats
    {
        public static readonly SymbolDisplayFormat PropertyDataTypeDisplayFormat = SymbolDisplayFormat.FullyQualifiedFormat.WithGlobalNamespaceStyle(SymbolDisplayGlobalNamespaceStyle.Omitted);
    }
}
