using System;
using System.Collections.Generic;
using System.Text;

using Microsoft.CodeAnalysis;

namespace ProgrammerAl.DTO.Generators
{
    public class PropertyUtilities
    {
        public static string GenerateDataTypeFullNameFromProperty(IPropertySymbol propertySymbol)
        {
            return propertySymbol.Type.ToDisplayString(NullableFlowState.MaybeNull, SymbolDisplayFormats.PropertyDataTypeDisplayFormat);
        }
    }
}
