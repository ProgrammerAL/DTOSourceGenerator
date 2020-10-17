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

        private record AttributeSymbols(INamedTypeSymbol DtoAttributeSymbol, INamedTypeSymbol DtoBasicPropertyCheckAttributeSymbol, INamedTypeSymbol DtoPropertyCheckAttributeSymbol, INamedTypeSymbol DtoStringPropertyCheckAttributeSymbol);

        public void Execute(GeneratorExecutionContext context)
        {
            // System.Diagnostics.Debugger.Launch();

            if (context.SyntaxReceiver is not SyntaxReceiver receiver)
            {
                return;
            }

            var compilation = context.Compilation;

            var dtoAttributesNamespace = GenerateDTOAttributesNamespaceString();

            var dtoAttributeSymbol = compilation!.GetTypeByMetadataName($"{dtoAttributesNamespace}.{nameof(GenerateDTOAttribute)}");

            var dtoBasicPropertyCheckAttributeSymbol = compilation!.GetTypeByMetadataName($"{dtoAttributesNamespace}.{nameof(BasicPropertyCheckAttribute)}");
            var dtoPropertyCheckAttributeSymbol = compilation!.GetTypeByMetadataName($"{dtoAttributesNamespace}.{nameof(DTOPropertyCheckAttribute)}");
            var dtoStringPropertyCheckAttributeSymbol = compilation!.GetTypeByMetadataName($"{dtoAttributesNamespace}.{nameof(StringPropertyCheckAttribute)}");

            if (dtoAttributeSymbol is null
                || dtoBasicPropertyCheckAttributeSymbol is null
                || dtoPropertyCheckAttributeSymbol is null
                || dtoStringPropertyCheckAttributeSymbol is null)
            {
                throw new Exception($"Cannot continue {nameof(DTOGenerator)} because the compilation could not find one or more of the required attributes");
            }

            var attributeSymbols = new AttributeSymbols(dtoAttributeSymbol, dtoBasicPropertyCheckAttributeSymbol, dtoPropertyCheckAttributeSymbol, dtoStringPropertyCheckAttributeSymbol);

            // loop over the candidate fields, and keep the ones that are actually annotated
            var newClassSymbols = receiver.CandidateClasses
                .Select(declaration =>
                {
                    SemanticModel classModel = compilation.GetSemanticModel(declaration.SyntaxTree);
                    var classSymbol = classModel.GetDeclaredSymbol(declaration);

                    if (classSymbol is null)
                    {
                        throw new Exception($"Could not load class symbol frin semantic model {declaration}");
                    }

                    if (classSymbol
                        .GetAttributes()
                        .Any(ad => ad.AttributeClass!.Equals(dtoAttributeSymbol, SymbolEqualityComparer.Default) is true))
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
            }
        }

        private string GenerateDTOAttributesNamespaceString()
        {
            return "ProgrammerAl.DTO.Attributes";
        }

        private SourceText GenerateDTO(INamedTypeSymbol classSymbol, ImmutableArray<string> allClassesAddingDto, AttributeSymbols attributeSymbols)
        {
            var className = GenerateDTOClassName(classSymbol);
            var classNamespace = classSymbol.ContainingNamespace.ToDisplayString();

            var properties = classSymbol.GetMembers()
                .Where(x => x is IPropertySymbol)
                .Cast<IPropertySymbol>()
                .Select(x =>
                {
                    //TODO: Skip checking certain properties based on an Attribute

                    var memberName = x.Name;

                    var dataTypeFormatStyle = SymbolDisplayFormat.FullyQualifiedFormat.WithGlobalNamespaceStyle(SymbolDisplayGlobalNamespaceStyle.Omitted);
                    var dataTypeFullName = x.Type.ToDisplayString(NullableFlowState.MaybeNull, dataTypeFormatStyle);

                    var isValidCheckConfig = GeneratePropertyIsValidCheckRules(x, dataTypeFullName, attributeSymbols, allClassesAddingDto);

                    return new DTOProperty(x, memberName, dataTypeFullName, isValidCheckConfig);
                })
                .Where(x => x is object)
                .Select(x => x!)
                .ToImmutableArray();

            var propertySpaces = GenerateSpacesForPropertyLines();
            var propertyLines = properties.Select(x => $"{propertySpaces}public {x.FullDataType}? {x.PropertyName} {{ get; set; }}");

            var propertiesString = string.Join(Environment.NewLine, propertyLines);

            var checkIsValidCode = GenerateCheckIsValidCode(classSymbol, properties, allClassesAddingDto);

            var sourceText = $@"
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

            return SourceText.From(sourceText, encoding: Encoding.UTF8);
        }

        private string GenerateSpacesForPropertyLines() => "        ";
        private string GenerateSpacesForIsValidChecks() => "                   ";

        private IDtoPropertyIsValidCheckConfig GeneratePropertyIsValidCheckRules(IPropertySymbol propertySymbol, string dataTypeFullName, AttributeSymbols attributeSymbols, ImmutableArray<string> allClassesAddingDto)
        {
            var propertyAttributes = propertySymbol.GetAttributes();
            if (DataTypeIsString(dataTypeFullName))
            {
                return CreateDtoPropertyCheckConfigForString(propertyAttributes, attributeSymbols);
            }
            else if (DataTypeIsAnotherDTO(allClassesAddingDto, dataTypeFullName))
            {
                return CreateDtoPropertyCheckConfigForDto(propertyAttributes, attributeSymbols);
            }

            //INamedTypeSymbol attributeSymbol = compilation.GetTypeByMetadataName("AutoNotify.AutoNotifyAttribute");

            //var propertyAttributes = propertySymbol.GetAttributes();
            //if(propertyAttributes.Any(x => x.AttributeClass?.Equals()))

            return CreateDefaultPropertyConfig();
        }

        private IDtoPropertyIsValidCheckConfig CreateDefaultPropertyConfig() => new DtoBasicPropertyIsValidCheckConfig(AllowNull: false);

        private IDtoPropertyIsValidCheckConfig CreateDtoPropertyCheckConfigForDto(ImmutableArray<AttributeData> propertyAttributes, AttributeSymbols attributeSymbols)
        {
            var dtoPropertyAttribute = propertyAttributes.FirstOrDefault(x => x.AttributeClass?.Equals(attributeSymbols.DtoPropertyCheckAttributeSymbol, SymbolEqualityComparer.Default) is true);
            if (dtoPropertyAttribute is object)
            {
                var checkIsValidValue = dtoPropertyAttribute.NamedArguments.SingleOrDefault(x => x.Key == nameof(DTOPropertyCheckAttribute.CheckIsValid)).Value;
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

            return new DtoPropertyIsValidCheckConfig(CheckIsValid: DTOPropertyCheckAttribute.DefaultCheckIsValid, AllowNull: BasicPropertyCheckAttribute.DefaultAllowNull);
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


        private bool DataTypeIsAnotherDTO(ImmutableArray<string> allDtoNamesBeingGenerated, string dataTypeFullName)
        {
            return allDtoNamesBeingGenerated.Any(x => string.Equals(x, dataTypeFullName, StringComparison.OrdinalIgnoreCase));
        }

        private bool DataTypeIsString(string dataTypeFullName)
        {
            return
                string.Equals("string", dataTypeFullName, StringComparison.OrdinalIgnoreCase)
                || string.Equals("System.String", dataTypeFullName, StringComparison.OrdinalIgnoreCase);
        }

        private string GenerateCheckIsValidCode(INamedTypeSymbol classSymbol, ImmutableArray<DTOProperty> properties, ImmutableArray<string> allClassesBeingGenerated)
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

            var allChecksLine = string.Join($"{Environment.NewLine}{GenerateSpacesForIsValidChecks()}&& ", propertyChecks);

            return allChecksLine;
        }

        private string GenerateDTOClassFileName(INamedTypeSymbol classSymbol)
        {
            var className = GenerateDTOClassName(classSymbol);
            return $"{className}DTO.cs";
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

        private record DTOProperty(
            IPropertySymbol PropertySymbol,
            string PropertyName,
            string FullDataType,
            IDtoPropertyIsValidCheckConfig IsValidCheckConfig);

        private interface IDtoPropertyIsValidCheckConfig { }
        private record DtoBasicPropertyIsValidCheckConfig(bool AllowNull) : IDtoPropertyIsValidCheckConfig;
        private record DtoPropertyIsValidCheckConfig(bool AllowNull, bool CheckIsValid) : IDtoPropertyIsValidCheckConfig;
        private record DtoStringPropertyIsValidCheckConfig(StringIsValidCheckType StringIsValidCheck) : IDtoPropertyIsValidCheckConfig;

        private string? DeterminBasicPropertyCheck(DTOProperty property, DtoBasicPropertyIsValidCheckConfig validityCheckConfig)
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
                return $"{property.PropertyName} is object";
            }
        }

        private string? DeterminDtoPropertyCheck(DTOProperty property, DtoPropertyIsValidCheckConfig dtoValidityCheck)
        {
            if (dtoValidityCheck.CheckIsValid)
            {
                return $"{property.PropertyName}?.CheckIsValid() is true";
            }

            var basicValidityCheck = new DtoBasicPropertyIsValidCheckConfig(dtoValidityCheck.AllowNull);
            return DeterminBasicPropertyCheck(property, basicValidityCheck);
        }

        private string? DeterminStringPropertyCheck(DTOProperty property, DtoStringPropertyIsValidCheckConfig validityCheckConfig)
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
            }

            throw new Exception($"Could not determine how to validate the string property {property.PropertyName}");
        }

        private class SyntaxReceiver : ISyntaxReceiver
        {
            public List<TypeDeclarationSyntax> CandidateClasses { get; } = new();

            /// <summary>
            /// Called for every syntax node in the compilation, we can inspect the nodes and save any information useful for generation
            /// </summary>
            public void OnVisitSyntaxNode(SyntaxNode syntaxNode)
            {
                //System.Diagnostics.Debugger.Launch();

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

namespace System.Runtime.CompilerServices
{
    public class IsExternalInit { }
}
