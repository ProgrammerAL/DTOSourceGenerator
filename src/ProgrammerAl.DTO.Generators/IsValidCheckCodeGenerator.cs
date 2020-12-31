using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;

using Microsoft.CodeAnalysis;

using ProgrammerAl.DTO.Attributes;

namespace ProgrammerAl.DTO.Generators
{
    public class IsValidCheckCodeGenerator
    {
        public string? GenerateCheckIsValidCode(DtoProperty property)
        {
            switch (property.IsValidCheckConfig)
            {
                case DtoStringPropertyIsValidCheckConfig stringValidityCheck:
                    return DeterminStringPropertyCheck(property, stringValidityCheck);
                case DtoPropertyIsValidCheckConfig dtoValidityCheck:
                    return DeterminDtoPropertyCheck(property, dtoValidityCheck);
                case DtoBasicPropertyIsValidCheckConfig basicValidityCheck:
                    return DeterminBasicPropertyCheck(property, basicValidityCheck);
                default:
                    throw new Exception("This shouldn't happen");
            }
        }

        private string? DeterminDtoPropertyCheck(DtoProperty property, DtoPropertyIsValidCheckConfig dtoValidityCheck)
        {
            if (dtoValidityCheck.CheckIsValid)
            {
                return $"{property.PropertyName}?.CheckIsValid() is true";
            }

            var basicValidityCheck = new DtoBasicPropertyIsValidCheckConfig(dtoValidityCheck.AllowNull);
            return DeterminBasicPropertyCheck(property, basicValidityCheck);
        }

        private string? DeterminStringPropertyCheck(DtoProperty property, DtoStringPropertyIsValidCheckConfig validityCheckConfig)
        {
            switch (validityCheckConfig.StringIsValidCheck)
            {
                case StringIsValidCheckType.AllowNull:
                    return null;
                case StringIsValidCheckType.AllowEmptyString:
                    return $"{property.PropertyName} is object";
                case StringIsValidCheckType.AllowWhenOnlyWhiteSpace:
                    return $"!string.IsNullOrEmpty({property.PropertyName})";
                case StringIsValidCheckType.RequiresNonWhitespaceText:
                    return $"!string.IsNullOrWhiteSpace({property.PropertyName})";
                default:
                    return $"!string.IsNullOrWhiteSpace({property.PropertyName})";
            }
        }

        private string? DeterminBasicPropertyCheck(DtoProperty property, DtoBasicPropertyIsValidCheckConfig validityCheckConfig)
        {
            if (validityCheckConfig.AllowNull)
            {
                return null;
            }
            //else if (validityCheckConfig.IsCollection)
            //{ 
            //}
            else
            {
                //Make sure it's not null
                return $"{property.PropertyName} is not null";
            }
        }
    }
}
