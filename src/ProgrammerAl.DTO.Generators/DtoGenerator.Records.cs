using System;
using System.Collections.Generic;
using System.Text;

using Microsoft.CodeAnalysis;

using ProgrammerAl.DTO.Attributes;

namespace ProgrammerAl.DTO.Generators
{
    public record DtoProperty(
        IPropertySymbol PropertySymbol,
        string PropertyName,
        string FullDataType,
        IDtoPropertyIsValidCheckConfig IsValidCheckConfig)
    {
        /// <summary>
        /// HARD CODED TO FALSE FOR NOW - Meaning the nullable mark (is "?") is added to every property in the generated DTO class
        /// TODO: Figure out how to tell if the parent class is already nullable and then use that
        /// </summary>
        public bool IsParentNullable => false;

        public string GeneratePropertyCodeLine()
        {
            var nullableNotation = !IsParentNullable ? "?" : string.Empty;
            return $"{GeneratedClassStrings.SpacesForPropertyLines}public {FullDataType}{nullableNotation} {PropertyName} {{ get; set; }}";
        }
    }

    public interface IDtoPropertyIsValidCheckConfig { }
    public record DtoBasicPropertyIsValidCheckConfig(bool AllowNull) : IDtoPropertyIsValidCheckConfig;
    public record DtoPropertyIsValidCheckConfig(bool AllowNull, bool CheckIsValid) : IDtoPropertyIsValidCheckConfig;
    public record DtoStringPropertyIsValidCheckConfig(StringIsValidCheckType StringIsValidCheck) : IDtoPropertyIsValidCheckConfig;

    public record AttributeSymbols(INamedTypeSymbol DtoAttributeSymbol, INamedTypeSymbol DtoBasicPropertyCheckAttributeSymbol, INamedTypeSymbol DtoPropertyCheckAttributeSymbol, INamedTypeSymbol DtoStringPropertyCheckAttributeSymbol);
}

namespace System.Runtime.CompilerServices
{
    public class IsExternalInit { }
}
