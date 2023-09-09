using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Logging;
using Tapper;
using Tapper.TypeMappers;

namespace TypedSignalR.Client.TypeScript;

internal class InterfaceTranspiler
{
    private readonly SpecialSymbols _specialSymbols;
    private readonly ITypedSignalRTranspilationOptions _options;
    private readonly ILogger _logger;

    public InterfaceTranspiler(SpecialSymbols specialSymbols, ITypedSignalRTranspilationOptions options, ILogger logger)
    {
        _specialSymbols = specialSymbols;
        _options = options;
        _logger = logger;
    }

    public IReadOnlyList<GeneratedSourceCode> Transpile(IEnumerable<INamedTypeSymbol> interfaceTypes)
    {
        var typeLookup = interfaceTypes.Distinct<INamedTypeSymbol>(SymbolEqualityComparer.Default)
            .ToLookup<INamedTypeSymbol, INamespaceSymbol>(static x => x.ContainingNamespace, SymbolEqualityComparer.Default);

        var outputSourceCodeList = new List<GeneratedSourceCode>(typeLookup.Count);

        foreach (var group in typeLookup)
        {
            var codeWriter = new CodeWriter();

            AddHeader(group, ref codeWriter);

            foreach (var type in group)
            {
                _logger.Log(LogLevel.Information, "Transpile {typename}...", type.ToDisplayString());

                AddInterface(type, _specialSymbols, _options, ref codeWriter);
            }

            var code = codeWriter.ToString().NormalizeNewLines("\n");

            outputSourceCodeList.Add(new GeneratedSourceCode($"TypedSignalR.Client/{group.Key}.ts", code));
        }

        return outputSourceCodeList;
    }

    private void AddHeader(IGrouping<INamespaceSymbol, INamedTypeSymbol> interfaceTypes, ref CodeWriter codeWriter)
    {
        codeWriter.AppendLine("/* THIS (.ts) FILE IS GENERATED BY TypedSignalR.Client.TypeScript */");
        codeWriter.AppendLine("/* eslint-disable */");
        codeWriter.AppendLine("/* tslint:disable */");
        codeWriter.AppendLine("// @ts-nocheck");
        codeWriter.AppendLine("import { IStreamResult, Subject } from '@microsoft/signalr';");

        var appearTypes = interfaceTypes
            .SelectMany(static x => x.GetMethods())
            .SelectMany(x =>
                x.Parameters
                    .Select(y => y.Type.GetFeaturedType(_specialSymbols))
                    .Concat(new[] { x.ReturnType.GetFeaturedType(_specialSymbols) })
            );

        var tapperAttributeAnnotatedTypesLookup = appearTypes
            .SelectMany(RoslynExtensions.GetRelevantTypes)
            .OfType<INamedTypeSymbol>()
            .Where(x => x.IsAttributeAnnotated(_specialSymbols.TranspilationSourceAttributeSymbols))
            .Distinct<INamedTypeSymbol>(SymbolEqualityComparer.Default)
            .ToLookup<INamedTypeSymbol, INamespaceSymbol>(static x => x.ContainingNamespace, SymbolEqualityComparer.Default);

        foreach (var groupingType in tapperAttributeAnnotatedTypesLookup)
        {
            // Be careful about the directory hierarchy.
            // Tapper generates a file named (namespace).ts directly under the specified directory(e.g. generated/HogeNamespace.ts).
            // TypedSignalR.Client.TypeScript creates a directory named TypedSignalR.Client in the specified directory
            // and generates TypeScript files there. (e.g. generated/TypedSignalR.Client/index.ts)
            // Therefore, in order to refer to the TypeScript file created by Tapper, we have to specify the directory one level up.
            codeWriter.AppendLine($"import {{ {string.Join(", ", groupingType.Select(x => x.Name))} }} from '../{groupingType.Key.ToDisplayString()}';");
        }

        codeWriter.AppendLine();
    }

    private static void AddInterface(
        INamedTypeSymbol interfaceSymbol,
        SpecialSymbols specialSymbols,
        ITypedSignalRTranspilationOptions options,
        ref CodeWriter codeWriter)
    {
        var doc = GetDocumentationFromSymbol(interfaceSymbol);
        var summaryList = doc?.GetElementsByTagName("summary");
        var summary = summaryList?.Count > 0 ? summaryList[0]?.InnerText.Trim() : null;

        if (!string.IsNullOrEmpty(summary))
        {
            codeWriter.AppendLine("/**");
            codeWriter.AppendLine($"* {summary}");
            codeWriter.AppendLine($"*/");
        }

        codeWriter.AppendLine($"export type {interfaceSymbol.Name} = {{");

        foreach (var method in interfaceSymbol.GetMethods())
        {
            WriteJSDoc(method, ref codeWriter);
            codeWriter.Append($"    {method.Name.Format(options.MethodStyle)}(");
            WriteParameters(method, options, specialSymbols, ref codeWriter);
            codeWriter.Append("): ");
            WriteReturnType(method, options, specialSymbols, ref codeWriter);
            codeWriter.AppendLine(";");
        }

        codeWriter.AppendLine("}");
        codeWriter.AppendLine();
    }

    private static void WriteJSDoc(IMethodSymbol methodSymbol, ref CodeWriter codeWriter)
    {
        var doc = GetDocumentationFromSymbol(methodSymbol);

        codeWriter.AppendLine("    /**");

        // Write method summary if available
        var summaryList = doc?.GetElementsByTagName("summary");
        var summary = summaryList?.Count > 0 ? summaryList[0]?.InnerText.Trim() : null;

        if (doc != null) codeWriter.AppendLine($"    * {summary ?? "Documentation unavailable."}");
        var parameterSummaries = doc?.GetElementsByTagName("param").Cast<XmlElement>().ToDictionary(x => x.GetAttribute("name"), x => x.InnerText.Trim()) ?? new();

        foreach (var parameter in methodSymbol.Parameters)
        {
            codeWriter.AppendLine(parameterSummaries.TryGetValue(parameter.Name, out string? paramSummary)
                ? $"    * @param {parameter.Name} {paramSummary} (Transpiled from {parameter.Type.ToDisplayString()})"
                : $"    * @param {parameter.Name} Transpiled from {parameter.Type.ToDisplayString()}");
        }

        var returnList = doc?.GetElementsByTagName("returns");
        var returnSummary = returnList?.Count > 0 ? returnList[0]?.InnerText.Trim() : null;

        codeWriter.AppendLine(string.IsNullOrEmpty(returnSummary)
            ? $"    * @returns Transpiled from {methodSymbol.ReturnType.ToDisplayString()}"
            : $"    * @returns {returnSummary} (Transpiled from {methodSymbol.ReturnType.ToDisplayString()})");
        codeWriter.AppendLine("    */");
    }

    private static void WriteParameters(IMethodSymbol methodSymbol, ITranspilationOptions options, SpecialSymbols specialSymbols, ref CodeWriter codeWriter)
    {
        if (methodSymbol.Parameters.Length == 0)
        {
            return;
        }

        if (methodSymbol.Parameters.Length == 1)
        {
            var parameter = methodSymbol.Parameters[0];

            if (SymbolEqualityComparer.Default.Equals(parameter.Type, specialSymbols.CancellationTokenSymbol))
            {
                return;
            }

            codeWriter.Append($"{parameter.Name}: {TypeMapper.MapTo(parameter.Type, options)}");
            return;
        }

        var paramStrings = methodSymbol.Parameters
            .Select(x =>
                SymbolEqualityComparer.Default.Equals(x.Type, specialSymbols.CancellationTokenSymbol)
                    ? null
                    : $"{x.Name}: {TypeMapper.MapTo(x.Type, options)}")
            .Where(x => x is not null);

        codeWriter.Append(string.Join(", ", paramStrings));
    }

    private static void WriteReturnType(
        IMethodSymbol methodSymbol,
        ITranspilationOptions options,
        SpecialSymbols specialSymbols,
        ref CodeWriter codeWriter)
    {
        var returnType = methodSymbol.ReturnType;

        // server-to-client streaming
        if (returnType.IsGenericType())
        {
            // IAsyncEnumerable<T>, ChannelReader<T>
            //     if parameter type -> Subject<T>
            //     if return type    -> IStreamResult<T>
            // TypeMapper.MapTo
            //     IAsyncEnumerable<T> -> Subject<T>
            //     ChannelReader<T>    -> Subject<T>

            // Support return type as streaming
            //     IAsyncEnumerable<T>
            //     Task<IAsyncEnumerable<T>>
            //     Task<ChannelReader<T>>

            // IAsyncEnumerable<T>
            if (SymbolEqualityComparer.Default.Equals(returnType.OriginalDefinition, specialSymbols.AsyncEnumerableSymbol))
            {
                var typeArg = ((INamedTypeSymbol)returnType).TypeArguments[0];
                codeWriter.Append($"IStreamResult<{TypeMapper.MapTo(typeArg, options)}>");
                return;
            }

            // Task<IAsyncEnumerable<T>>
            // Task<ChannelReader<T>>
            if (SymbolEqualityComparer.Default.Equals(returnType.OriginalDefinition, specialSymbols.GenericTaskSymbol))
            {
                var typeArg = ((INamedTypeSymbol)returnType).TypeArguments[0];

                if (typeArg.IsGenericType() && typeArg is INamedTypeSymbol namedTypeArg)
                {
                    // IAsyncEnumerable<T> or ChannelReader<T>
                    if (SymbolEqualityComparer.Default.Equals(namedTypeArg.OriginalDefinition, specialSymbols.AsyncEnumerableSymbol)
                        || SymbolEqualityComparer.Default.Equals(namedTypeArg.OriginalDefinition, specialSymbols.ChannelReaderSymbol))
                    {
                        var typeArg2 = namedTypeArg.TypeArguments[0];

                        codeWriter.Append($"IStreamResult<{TypeMapper.MapTo(typeArg2, options)}>");
                        return;
                    }
                }
            }
        }

        codeWriter.Append(TypeMapper.MapTo(returnType, options));
    }

    ///-------------------------------------------------------------------------------------------------
    /// <summary>   Gets the XML documentation from a symbol. </summary>
    ///
    /// <param name="symbol">   The symbol. </param>
    ///
    /// <returns>   The documentation from symbol.  </returns>
    ///-------------------------------------------------------------------------------------------------
    private static XmlDocument? GetDocumentationFromSymbol(ISymbol symbol)
    {
        var xmlDoc = symbol.GetDocumentationCommentXml();
        if (string.IsNullOrEmpty(xmlDoc))
            return null;

        XmlDocument? doc = new();
        doc.LoadXml(xmlDoc);
        return doc;
    }
}
