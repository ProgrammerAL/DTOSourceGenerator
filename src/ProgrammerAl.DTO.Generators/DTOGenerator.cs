using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;
using Microsoft.CodeAnalysis.CSharp;
using System.Collections.Immutable;
using ProgrammerAl.DTO.Attributes;

namespace ProgrammerAl.DTO.Generators
{
    [Generator]
    public class DTOGenerator : ISourceGenerator
    {
        public void Initialize(GeneratorInitializationContext context)
        {
            // Register a syntax receiver that will be created for each generation pass
            context.RegisterForSyntaxNotifications(() => new SyntaxReceiver());
        }

        public void Execute(GeneratorExecutionContext context)
        {
            //System.Diagnostics.Debugger.Launch();

            if (context.SyntaxReceiver is not SyntaxReceiver receiver
                || !receiver.CandidateClasses.Any())
            {
                return;
            }

            var compilation = context.Compilation;

            var attributeSymbols = LoadAttributeSymbols(compilation);

            // loop over the candidate fields, and keep the ones that are annotated with the GenerateDTOAttribute
            var newClassSymbols = receiver.CandidateClasses
                .Select(declaration =>
                {
                    SemanticModel classModel = compilation.GetSemanticModel(declaration.SyntaxTree);
                    var classSymbol = classModel.GetDeclaredSymbol(declaration);

                    if (classSymbol is null)
                    {
                        throw new Exception($"Could not load class symbol from semantic model {declaration}");
                    }

                    if (classSymbol
                        .GetAttributes()
                        .Any(ad => ad.AttributeClass!.Equals(attributeSymbols.DtoAttributeSymbol, SymbolEqualityComparer.Default) is true))
                    {
                        return classSymbol;
                    }

                    return null;
                })
                .Where(x => x is object)
                .Select(x => x!)
                .ToImmutableArray();

            var allClassesAddingDto = newClassSymbols.Select(x => GenerateFullClassNameString(x)).ToImmutableArray();

            foreach (var classSymbol in newClassSymbols)
            {
                var codeText = GenerateDTO(classSymbol, allClassesAddingDto, attributeSymbols);
                var fileName = GenerateDTOClassFileName(classSymbol);

                context.AddSource(fileName, codeText);

                //For Local Debugging
                System.IO.File.WriteAllText(@$"c:/temp/GeneratedDTOs/{fileName}", codeText.ToString());
            }
        }

        private AttributeSymbols LoadAttributeSymbols(Compilation compilation)
        {
            var dtoAttributeSymbol = compilation!.GetTypeByMetadataName(typeof(GenerateDtoAttribute).FullName);
            var dtoBasicPropertyCheckAttributeSymbol = compilation!.GetTypeByMetadataName(typeof(BasicPropertyCheckAttribute).FullName);
            var dtoPropertyCheckAttributeSymbol = compilation!.GetTypeByMetadataName(typeof(DtoPropertyCheckAttribute).FullName);
            var dtoStringPropertyCheckAttributeSymbol = compilation!.GetTypeByMetadataName(typeof(StringPropertyCheckAttribute).FullName);

            if (dtoAttributeSymbol is null
                || dtoBasicPropertyCheckAttributeSymbol is null
                || dtoPropertyCheckAttributeSymbol is null
                || dtoStringPropertyCheckAttributeSymbol is null)
            {
                throw new Exception($"Cannot continue {nameof(DTOGenerator)} because the compilation could not find one or more of the required attributes");
            }

            return new AttributeSymbols(dtoAttributeSymbol, dtoBasicPropertyCheckAttributeSymbol, dtoPropertyCheckAttributeSymbol, dtoStringPropertyCheckAttributeSymbol);
        }

        private SourceText GenerateDTO(INamedTypeSymbol classSymbol, ImmutableArray<string> allClassesAddingDto, AttributeSymbols attributeSymbols)
        {
            var className = GenerateDTOClassName(classSymbol);
            var classNamespace = classSymbol.ContainingNamespace.ToDisplayString();

            var properties = classSymbol.GetMembers()
                .Where(x => x is IPropertySymbol)
                .Cast<IPropertySymbol>()
                .Select(propertySymbol =>
                {
                    //TODO: Consider skipping checking certain properties based on an Attribute

                    /*
                     * Possible data types to consider
                     * 
                     * int, string
                     * int?, string?
                     * Nullable<int>, Nullable<string>
                     * Integer, String
                     * List<int>, List<string>
                     * int[], string[],
                     * int[]?, string[]?
                     * int?[]?, string?[]?
                     * 
                     * 
                     * MyClass
                     */

                    var isValidCheckConfig = GeneratePropertyIsValidCheckRules(propertySymbol, attributeSymbols, allClassesAddingDto);

                    return new DtoProperty(
                        PropertySymbol: propertySymbol, 
                        PropertyName: propertySymbol.Name, 
                        FullDataType: GenerateDataTypeFullNameFromProperty(propertySymbol),
                        IsValidCheckConfig: isValidCheckConfig);
                })
                .ToImmutableArray();

            string sourceText = GenerateDtoClassSourceText(classSymbol, allClassesAddingDto, className, classNamespace, properties);

            return SourceText.From(sourceText, encoding: Encoding.UTF8);
        }

        private string GenerateDtoClassSourceText(INamedTypeSymbol classSymbol, ImmutableArray<string> allClassesAddingDto, string className, string classNamespace, ImmutableArray<DtoProperty> properties)
        {
            var propertiesString = string.Join(Environment.NewLine, properties.Select(x => x.GeneratePropertyCodeLine()));
            var checkIsValidCode = GenerateCheckIsValidCode(classSymbol, properties, allClassesAddingDto);
            var sourceText = GenerateClassSourceText(className, classNamespace, propertiesString, checkIsValidCode);

            return sourceText;
        }

        private static string GenerateClassSourceText(string className, string classNamespace, string propertiesString, string checkIsValidCode)
        {
            return $@"
namespace {classNamespace}
{{
    public class {className}
    {{
{propertiesString}

        public bool CheckIsValid()
        {{
            return {checkIsValidCode};
        }}
    }}
}}";
        }

        private IDtoPropertyIsValidCheckConfig GeneratePropertyIsValidCheckRules(
            IPropertySymbol propertySymbol,
            AttributeSymbols attributeSymbols,
            ImmutableArray<string> allClassesAddingDto)
        {
            var propertyAttributes = propertySymbol.GetAttributes();

            if (DataTypeIsString(propertySymbol))
            {
                return CreateDtoPropertyCheckConfigForString(propertyAttributes, attributeSymbols);
            }
            else if (DataTypeIsAnotherDto(propertySymbol, allClassesAddingDto))
            {
                return CreateDtoPropertyCheckConfigForDto(propertyAttributes, attributeSymbols);
            }
            else if (DataTypeIsNullable(propertySymbol))
            {
                return new DtoBasicPropertyIsValidCheckConfig(AllowNull: true);
            }

            //INamedTypeSymbol attributeSymbol = compilation.GetTypeByMetadataName("AutoNotify.AutoNotifyAttribute");

            //var propertyAttributes = propertySymbol.GetAttributes();
            //if(propertyAttributes.Any(x => x.AttributeClass?.Equals()))

            return CreateDefaultPropertyConfig();
        }

        private static string GenerateDataTypeFullNameFromProperty(IPropertySymbol propertySymbol)
        {
            return propertySymbol.Type.ToDisplayString(NullableFlowState.MaybeNull, SymbolDisplayFormats.PropertyDataTypeDisplayFormat);
        }

        private bool DataTypeIsNullable(IPropertySymbol propertySymbol)
        {
            string dataTypeFullName = GenerateDataTypeFullNameFromProperty(propertySymbol);

            //TODO: Check for Nullable<> too I guess
            return dataTypeFullName.EndsWith("?");
        }

        private IDtoPropertyIsValidCheckConfig CreateDefaultPropertyConfig() => new DtoBasicPropertyIsValidCheckConfig(AllowNull: false);

        private IDtoPropertyIsValidCheckConfig CreateDtoPropertyCheckConfigForDto(ImmutableArray<AttributeData> propertyAttributes, AttributeSymbols attributeSymbols)
        {
            var dtoPropertyAttribute = propertyAttributes.FirstOrDefault(x => x.AttributeClass?.Equals(attributeSymbols.DtoPropertyCheckAttributeSymbol, SymbolEqualityComparer.Default) is true);
            if (dtoPropertyAttribute is object)
            {
                var checkIsValidValue = dtoPropertyAttribute.NamedArguments.SingleOrDefault(x => x.Key == nameof(DtoPropertyCheckAttribute.CheckIsValid)).Value;
                var checkIsValid = (bool)checkIsValidValue.Value!;

                var allowNullValue = dtoPropertyAttribute.NamedArguments.SingleOrDefault(x => x.Key == nameof(BasicPropertyCheckAttribute.AllowNull)).Value;
                var allowNull = (bool)allowNullValue.Value!;

                return new DtoPropertyIsValidCheckConfig(allowNull, checkIsValid);
            }

            DtoBasicPropertyIsValidCheckConfig? propertyCheckConfig = CreateBasicPropertyCheckFromAttribute(propertyAttributes, attributeSymbols);
            if (propertyCheckConfig is object)
            {
                return propertyCheckConfig;
            }

            return new DtoPropertyIsValidCheckConfig(CheckIsValid: DtoPropertyCheckAttribute.DefaultCheckIsValid, AllowNull: BasicPropertyCheckAttribute.DefaultAllowNull);
        }

        private IDtoPropertyIsValidCheckConfig CreateDtoPropertyCheckConfigForString(ImmutableArray<AttributeData> propertyAttributes, AttributeSymbols attributeSymbols)
        {
            var stringPropertyAttribute = propertyAttributes.FirstOrDefault(x => x.AttributeClass?.Equals(attributeSymbols.DtoStringPropertyCheckAttributeSymbol, SymbolEqualityComparer.Default) is true);
            if (stringPropertyAttribute is object)
            {
                var value = stringPropertyAttribute.NamedArguments.SingleOrDefault(x => x.Key == nameof(StringPropertyCheckAttribute.StringIsValidCheckType)).Value;
                var stringCheckType = (StringIsValidCheckType)(value.Value!);

                return new DtoStringPropertyIsValidCheckConfig(stringCheckType);
            }

            //A string could instead have the DtoBasicPropertyCheckAttribute attribute applied. Check for that too
            DtoBasicPropertyIsValidCheckConfig? propertyCheckConfig = CreateBasicPropertyCheckFromAttribute(propertyAttributes, attributeSymbols);
            if (propertyCheckConfig is object && propertyCheckConfig.AllowNull)
            {
                return new DtoStringPropertyIsValidCheckConfig(StringIsValidCheckType.AllowNull);
            }

            return new DtoStringPropertyIsValidCheckConfig(StringPropertyCheckAttribute.DefaultStringIsValidCheckType);
        }

        private IDtoPropertyIsValidCheckConfig CreateDtoPropertyCheckConfigForBasicProperty(ImmutableArray<AttributeData> propertyAttributes, AttributeSymbols attributeSymbols)
        {
            DtoBasicPropertyIsValidCheckConfig? propertyCheckConfig = CreateBasicPropertyCheckFromAttribute(propertyAttributes, attributeSymbols);
            if (propertyCheckConfig is object)
            {
                return propertyCheckConfig;
            }

            return new DtoBasicPropertyIsValidCheckConfig(AllowNull: false);
        }

        private DtoBasicPropertyIsValidCheckConfig? CreateBasicPropertyCheckFromAttribute(ImmutableArray<AttributeData> propertyAttributes, AttributeSymbols attributeSymbols)
        {
            var basicPropertyAttribute = propertyAttributes.FirstOrDefault(x => x.AttributeClass?.Equals(attributeSymbols.DtoBasicPropertyCheckAttributeSymbol, SymbolEqualityComparer.Default) is true);
            if (basicPropertyAttribute is object)
            {
                var allowNullValue = basicPropertyAttribute.NamedArguments.SingleOrDefault(x => x.Key == nameof(BasicPropertyCheckAttribute.AllowNull)).Value;
                var allowNull = (bool)allowNullValue.Value!;

                return new DtoBasicPropertyIsValidCheckConfig(allowNull);
            }

            return null;
        }


        private bool DataTypeIsAnotherDto(IPropertySymbol propertySymbol, ImmutableArray<string> allDtoNamesBeingGenerated)
        {
            string dataTypeFullName = GenerateDataTypeFullNameFromProperty(propertySymbol);

            return allDtoNamesBeingGenerated.Any(x => string.Equals(x, dataTypeFullName, StringComparison.OrdinalIgnoreCase));
        }

        private bool DataTypeIsString(IPropertySymbol propertySymbol)
        {
            string dataTypeFullName = GenerateDataTypeFullNameFromProperty(propertySymbol);

            return
                string.Equals("string", dataTypeFullName, StringComparison.OrdinalIgnoreCase)
                || string.Equals("System.String", dataTypeFullName, StringComparison.OrdinalIgnoreCase);
        }

        private string GenerateCheckIsValidCode(INamedTypeSymbol classSymbol, ImmutableArray<DtoProperty> properties, ImmutableArray<string> allClassesBeingGenerated)
        {
            var propertyChecks = properties.Select(property =>
                {
                    switch (property.IsValidCheckConfig)
                    {
                        case DtoStringPropertyIsValidCheckConfig stringValidityCheck:
                            return DeterminStringPropertyCheck(property, stringValidityCheck);
                        case DtoPropertyIsValidCheckConfig dtoValidityCheck:
                            return DeterminDtoPropertyCheck(property, dtoValidityCheck);
                        case DtoBasicPropertyIsValidCheckConfig basicValidityCheck:
                            return DeterminBasicPropertyCheck(property, basicValidityCheck);
                    }

                    throw new Exception("This shouldn't happen");
                })
                .Where(x => x is object)
                .ToImmutableArray();

            var allChecksLine = string.Join($"{Environment.NewLine}{GeneratedClassStrings.SpacesForIsValidChecks}&& ", propertyChecks);

            return allChecksLine;
        }

        private string GenerateDTOClassFileName(INamedTypeSymbol classSymbol)
        {
            var className = GenerateDTOClassName(classSymbol);
            return $"{className}.cs";
        }

        private string GenerateDTOClassName(INamedTypeSymbol classSymbol)
        {
            return classSymbol.Name + "DTO";
        }

        private string GenerateFullClassNameString(INamedTypeSymbol classSymbol)
        {
            var classNamespace = classSymbol.ContainingNamespace.ToDisplayString();
            return $"{classNamespace}.{classSymbol.Name}";
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

        private class SyntaxReceiver : ISyntaxReceiver
        {
            public List<TypeDeclarationSyntax> CandidateClasses { get; } = new();

            /// <summary>
            /// Called for every syntax node in the compilation, we can inspect the nodes and save any information useful for generation
            /// </summary>
            public void OnVisitSyntaxNode(SyntaxNode syntaxNode)
            {
                // any field with at least one attribute is a candidate for property generation
                if (syntaxNode is ClassDeclarationSyntax classDeclarationSyntax
                    && classDeclarationSyntax.AttributeLists.Count > 0)
                {
                    CandidateClasses.Add(classDeclarationSyntax);
                }
                else if (syntaxNode is RecordDeclarationSyntax recordDeclarationSyntax
                    && recordDeclarationSyntax.AttributeLists.Count > 0)
                {
                    CandidateClasses.Add(recordDeclarationSyntax);
                }
            }
        }
    }
}
