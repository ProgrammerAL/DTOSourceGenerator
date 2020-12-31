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

            var isValidCheckCodeConfigGenerator = new IsValidCheckCodeConfigGenerator();
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

                    var isValidCheckConfig = isValidCheckCodeConfigGenerator.GeneratePropertyIsValidCheckRules(propertySymbol, attributeSymbols, allClassesAddingDto);

                    return new DtoProperty(
                        PropertySymbol: propertySymbol,
                        PropertyName: propertySymbol.Name,
                        FullDataType: PropertyUtilities.GenerateDataTypeFullNameFromProperty(propertySymbol),
                        IsValidCheckConfig: isValidCheckConfig);
                })
                .ToImmutableArray();

            string sourceText = GenerateDtoClassSourceText(className, classNamespace, properties);

            return SourceText.From(sourceText, encoding: Encoding.UTF8);
        }

        private string GenerateDtoClassSourceText(string className, string classNamespace, ImmutableArray<DtoProperty> properties)
        {
            var propertiesString = string.Join(Environment.NewLine, properties.Select(x => x.GeneratePropertyCodeLine()));
            var checkIsValidCode = GenerateCheckIsValidCode(properties);
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

        private string GenerateCheckIsValidCode(ImmutableArray<DtoProperty> properties)
        {
            var isValidCheckCodeGenerator = new IsValidCheckCodeGenerator();
            var propertyChecks = properties.Select(property => isValidCheckCodeGenerator.GenerateCheckIsValidCode(property))
                .Where(x => x is object)
                .ToImmutableArray();

            var allChecksLine = string.Join($"{Environment.NewLine}{GeneratedClassStrings.SpacesForIsValidChecks}&& ", propertyChecks);

            return allChecksLine;
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
